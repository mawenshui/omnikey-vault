using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.2.0: Ensures only one instance of OmniKey Vault runs at a time.
/// Uses a named Mutex for detection and a named pipe for IPC.
///
/// When a second instance starts:
/// 1. The Mutex check fails → the second instance is a "client"
/// 2. The client sends a "SHOW" command to the named pipe
/// 3. The first instance (server) receives the command and brings its
///    window to the foreground
/// 4. The client exits immediately
///
/// Usage in Program.cs:
/// <code>
/// if (!SingleInstanceService.TryStart())
/// {
///     SingleInstanceService.SignalShow(); // tell the running instance to show
///     return 0; // exit this process
/// }
/// // ... start the GUI ...
/// SingleInstanceService.StartServer(() => { /* bring window to front */ });
/// </code>
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public static class SingleInstanceService
{
    private const string MutexName = @"Global\OmniKeyVault_SingleInstance_Mutex";
    private const string PipeName = "OmniKeyVault_SingleInstance_Pipe";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _pipeCts;

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the first instance (mutex acquired).
    /// Returns false if another instance is already running.
    /// </summary>
    public static bool TryStart()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance already holds the mutex — we're the second instance
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Sends a "SHOW" command to the already-running instance via named pipe.
    /// Call this from the second instance before exiting.
    /// Returns true if the signal was sent successfully.
    /// </summary>
    public static bool SignalShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out,
                PipeOptions.None, System.Security.Principal.TokenImpersonationLevel.None);
            client.Connect(timeout: 3000); // 3 second timeout
            using var writer = new StreamWriter(client);
            writer.WriteLine("SHOW");
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts a named pipe server that listens for commands from subsequent
    /// instances. When a "SHOW" command is received, the callback is invoked
    /// on the UI thread (the callback should bring the main window to front).
    ///
    /// This runs in a background thread and exits when <see cref="Stop"/> is called.
    /// </summary>
    /// <param name="onShow">Callback invoked when a second instance signals "SHOW".
    /// This is called on a background thread — the callback must marshal to the
    /// UI thread itself (e.g., via Dispatcher.UIThread.Post).</param>
    public static void StartServer(Action onShow)
    {
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(ct);
                    if (line == "SHOW")
                    {
                        onShow();
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // normal shutdown
                }
                catch
                {
                    // If the pipe server fails (e.g., another instance grabbed it),
                    // wait briefly and retry
                    try { await Task.Delay(1000, ct); } catch { break; }
                }
            }
        }, ct);
    }

    /// <summary>
    /// Stops the named pipe server and releases the mutex.
    /// Call this on application shutdown.
    /// </summary>
    public static void Stop()
    {
        try { _pipeCts?.Cancel(); } catch { }
        try { _pipeCts?.Dispose(); } catch { }
        _pipeCts = null;
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }
}
