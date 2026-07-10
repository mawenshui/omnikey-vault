namespace OmniKeyVault.Cli;

/// <summary>
/// P7-T4: Process-local flag to suppress the GUI's FileSystemWatcher-driven
/// auto-sync. CLI <c>sync pause</c> sets it; <c>sync resume</c> clears it.
/// Lives in-process; each CLI invocation starts unpaused. The GUI may read
/// this flag via a future cross-process IPC (currently GUI is unaffected).
/// </summary>
internal static class SyncPauseState
{
    public static bool IsPaused { get; set; }
}
