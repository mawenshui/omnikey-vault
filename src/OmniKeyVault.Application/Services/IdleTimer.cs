namespace OmniKeyVault.Application;

/// <summary>
/// Idle-detection auto-lock timer per v0.4 S7-T2 / MANUAL §12.5 / §4.9.
///
/// The timer counts wall-clock seconds since the last user activity (mouse
/// move, key press, or explicit <see cref="RecordActivity"/> call) and fires
/// <see cref="IdleTimeoutReached"/> when the elapsed time exceeds
/// <see cref="IdleMinutes"/>. The owning window (MainWindow) wires this to
/// <c>VaultService.Lock()</c> via a small bridge (the lock itself lives in
/// the Application layer to stay cross-platform testable).
///
/// Real "user input" detection lives in the GUI layer (Avalonia subscribes
/// to pointer / key events). The Application layer just owns the timer +
/// timeout policy so the same logic is reusable by the future CLI / service
/// builds.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class IdleTimer : IDisposable
{
    private readonly object _lock = new();
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private int _idleMinutes = 15;
    private bool _running;
    private bool _disposed;

    /// <summary>Raised on the timer's polling thread (NOT the UI thread) when
    /// the idle threshold is crossed. The subscriber is responsible for
    /// marshalling back to the UI thread (the MainWindow does this via
    /// <c>Dispatcher.UIThread.InvokeAsync</c>).</summary>
    public event EventHandler<IdleTimeoutEventArgs>? IdleTimeoutReached;

    public IdleTimer(int idleMinutes = 15)
    {
        _idleMinutes = Math.Max(1, idleMinutes);
    }

    /// <summary>Idle timeout in minutes. Setting this resets the timer.</summary>
    public int IdleMinutes
    {
        get { lock (_lock) return _idleMinutes; }
        set { lock (_lock) { _idleMinutes = Math.Max(1, value); _lastActivity = DateTimeOffset.UtcNow; } }
    }

    /// <summary>Seconds elapsed since the last activity (or since Start if no
    /// activity has been recorded). Used by the status bar's countdown label.</summary>
    public int SecondsSinceActivity
    {
        get
        {
            lock (_lock)
            {
                return (int)Math.Floor((DateTimeOffset.UtcNow - _lastActivity).TotalSeconds);
            }
        }
    }

    /// <summary>Remaining seconds before idle timeout fires. Clamped to [0, IdleMinutes*60].</summary>
    public int SecondsUntilTimeout
    {
        get
        {
            lock (_lock)
            {
                var elapsed = (DateTimeOffset.UtcNow - _lastActivity).TotalSeconds;
                var remaining = _idleMinutes * 60.0 - elapsed;
                return (int)Math.Max(0, Math.Ceiling(remaining));
            }
        }
    }

    /// <summary>Mark user activity (resets the idle countdown). Cheap; safe
    /// to call on every mouse / key event the GUI observes.</summary>
    public void RecordActivity()
    {
        lock (_lock) _lastActivity = DateTimeOffset.UtcNow;
    }

    /// <summary>Start the polling loop. Idempotent — calling twice is a no-op.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_running || _disposed) return;
            _running = true;
            _lastActivity = DateTimeOffset.UtcNow;
        }
        _ = PollLoopAsync();
    }

    /// <summary>Stop the polling loop. Safe to call when not running.</summary>
    public void Stop()
    {
        lock (_lock) { _running = false; }
    }

    private async Task PollLoopAsync()
    {
        // Poll once a second. Cheap (one DateTimeOffset subtraction per tick)
        // and avoids pulling in a full System.Timers.Timer + Dispatcher
        // dependency for the Application layer.
        while (true)
        {
            await Task.Delay(1000);
            bool shouldFire = false;
            lock (_lock)
            {
                if (!_running || _disposed) return;
                if ((DateTimeOffset.UtcNow - _lastActivity).TotalMinutes >= _idleMinutes)
                {
                    shouldFire = true;
                    _running = false;  // one-shot: caller must Start() again after lock
                }
            }
            if (shouldFire)
            {
                try { IdleTimeoutReached?.Invoke(this, new IdleTimeoutEventArgs(_idleMinutes)); }
                catch { /* swallow — GUI should not crash the timer */ }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock) { _disposed = true; _running = false; }
    }
}

public sealed class IdleTimeoutEventArgs : EventArgs
{
    public int IdleMinutes { get; }
    public IdleTimeoutEventArgs(int idleMinutes) => IdleMinutes = idleMinutes;
}
