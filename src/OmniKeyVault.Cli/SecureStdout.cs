using System.Security.Cryptography;
using System.Text;

namespace OmniKeyVault.Cli;

/// <summary>
/// P4-T6: Secure stdout wrapper for raw secret output. After writing a
/// secret to stdout via <c>entry get --format raw</c>, the internal buffer
/// is zeroed after a 30-second delay. A <c>AppDomain.ProcessExit</c> handler
/// ensures zeroing also happens on early termination.
///
/// Limitation: terminal scrollback buffer cannot be cleared — this is a
/// known constraint documented in INTERNAL.md §7. Users should clear their
/// terminal scrollback manually or use a terminal with scrollback disabled
/// when piping secrets.
/// </summary>
public sealed class SecureStdout : IDisposable
{
    private readonly TextWriter _realStdout;
    private byte[]? _lastRawOutput;
    private System.Timers.Timer? _clearTimer;
    private const int ClearDelayMs = 30_000;
    private bool _disposed;

    public SecureStdout(TextWriter realStdout)
    {
        _realStdout = realStdout ?? throw new ArgumentNullException(nameof(realStdout));
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>Write raw secret bytes to stdout and schedule zeroing of the
    /// internal copy after 30 seconds.</summary>
    public void WriteRaw(string value)
    {
        if (value == null) return;

        // Zero any previous output immediately
        ZeroPrevious();

        // Store a copy so we can zero it later
        _lastRawOutput = Encoding.UTF8.GetBytes(value);

        // Write to the real stdout
        _realStdout.Write(value);
        _realStdout.Flush();

        // Schedule zeroing after 30 seconds
        _clearTimer?.Stop();
        _clearTimer = new System.Timers.Timer(ClearDelayMs) { AutoReset = false };
        _clearTimer.Elapsed += (_, _) => ZeroPrevious();
        _clearTimer.Start();
    }

    private void ZeroPrevious()
    {
        if (_lastRawOutput != null)
        {
            CryptographicOperations.ZeroMemory(_lastRawOutput);
            _lastRawOutput = null;
        }
        _clearTimer?.Stop();
        _clearTimer?.Dispose();
        _clearTimer = null;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        ZeroPrevious();
    }

    public void Dispose()
    {
        if (_disposed) return;
        ZeroPrevious();
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _disposed = true;
    }
}
