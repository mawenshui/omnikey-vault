namespace OmniKeyVault.Domain;

/// <summary>
/// In-memory snapshot of an Entry at a specific version, per PRD §5.5.2.
///
/// v0.2: tracked in-memory only (the on-disk snapshot files at
/// .okv.snapshots/&lt;profile&gt;/&lt;entry-id&gt;/&lt;version&gt;.entry.enc
/// are deferred to v0.4 per ROADMAP S7-T5). The v0.2 BackupService
/// retains the snapshot bytes in memory so the user can view history
/// and restore the most recent few versions without going to disk.
/// </summary>
public sealed class EntrySnapshot
{
    public required Guid EntryId { get; init; }
    public required string ProfileName { get; init; }
    public required uint Version { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required Entry Entry { get; init; }
    /// <summary>Device ID that produced this snapshot (for audit).</summary>
    public required string DeviceId { get; init; }
    /// <summary>Short human-readable reason (e.g. "manual edit", "auto-save").</summary>
    public string? Reason { get; init; }
}
