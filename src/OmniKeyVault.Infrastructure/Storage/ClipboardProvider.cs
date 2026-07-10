using OmniKeyVault.Contracts;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// Clipboard provider with auto-clear after the configured timeout (PRD §5.11).
/// v0.1 MVP: in-memory copy with 8-second auto-clear timer. The GUI layer
/// (v0.2+ per ROADMAP S2-T8) will add OS-level clipboard integration.
/// </summary>
public sealed class ClipboardProvider : IClipboardProvider
{
    private string? _content;
    private Timer? _clearTimer;
    private readonly object _gate = new();

    public string? CurrentContent
    {
        get { lock (_gate) { return _content; } }
    }

    public event EventHandler? Cleared;

    public void Copy(string text, int clearAfterSeconds = 8)
    {
        lock (_gate)
        {
            _content = text;
            _clearTimer?.Dispose();
            _clearTimer = new Timer(_ => ClearInternal(), null, clearAfterSeconds * 1000, Timeout.Infinite);
        }
    }

    /// <summary>Forces immediate clear — used in tests and on app exit.</summary>
    public void ClearNow() => ClearInternal();

    private void ClearInternal()
    {
        lock (_gate)
        {
            if (_content != null)
            {
                _content = null;
            }
        }
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _clearTimer?.Dispose();
        _clearTimer = null;
        lock (_gate) { _content = null; }
    }
}

/// <summary>Alias for tests — same semantics as ClipboardProvider.</summary>
public sealed class InMemoryClipboardProvider : IClipboardProvider
{
    private readonly ClipboardProvider _inner = new();

    public string? CurrentContent => _inner.CurrentContent;
    public event EventHandler? Cleared
    {
        add => _inner.Cleared += value;
        remove => _inner.Cleared -= value;
    }
    public void Copy(string text, int clearAfterSeconds = 8) => _inner.Copy(text, clearAfterSeconds);
    public void ClearNow() => _inner.ClearNow();
    public void Dispose() => _inner.Dispose();
}
