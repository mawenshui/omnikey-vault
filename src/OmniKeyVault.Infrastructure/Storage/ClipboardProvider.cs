using OmniKeyVault.Contracts;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// Clipboard provider with auto-clear after the configured timeout (PRD §5.11).
/// v2.3.4: Added OsCopyAction callback so the GUI layer can bridge
/// the real OS clipboard. When set, Copy() forwards the text to the
/// OS clipboard in addition to tracking it in memory for auto-clear.
/// This fixes the bug where BrowserExtensionApiService copied field
/// values via ClipboardService → ClipboardProvider, but the in-memory
/// provider never touched the actual OS clipboard.
/// </summary>
public sealed class ClipboardProvider : IClipboardProvider
{
    private string? _content;
    private Timer? _clearTimer;
    private readonly object _gate = new();

    /// <summary>v2.3.4: Set by the GUI layer to bridge the OS clipboard.
    /// When non-null, Copy() calls this action to write to the real clipboard.</summary>
    public Action<string>? OsCopyAction { get; set; }

    /// <summary>v2.3.4: Set by the GUI layer to clear the real OS clipboard.</summary>
    public Action? OsClearAction { get; set; }

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
        // v2.3.4: Forward to the real OS clipboard if the GUI layer has set the callback
        OsCopyAction?.Invoke(text);
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
        // v2.3.4: Also clear the real OS clipboard
        OsClearAction?.Invoke();
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
