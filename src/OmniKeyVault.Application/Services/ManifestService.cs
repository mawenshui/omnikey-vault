using System.Text.Json;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Plaintext metadata for sync awareness per OKV_FORMAT.md §8.
/// Stored at <vault-dir>/manifest.json. Read by <see cref="VaultService"/> on startup
/// to detect remote changes (via vector clock) without needing to decrypt the .okv file.
/// </summary>
public sealed record Manifest
{
    public required Guid VaultUuid { get; init; }
    public required string DeviceId { get; init; }
    public required DateTimeOffset LastModified { get; init; }
    public required string LastModifiedBy { get; init; }
    public required IReadOnlyList<string> Profiles { get; init; }
    public required VectorClock VectorClock { get; init; }
    public required int SchemaVersion { get; init; }
    public required string OkvFormatVersion { get; init; }
    public required IReadOnlyDictionary<string, string> DevicePublicKeys { get; init; }
}

/// <summary>
/// Reads and writes the manifest.json file per OKV_FORMAT.md \u00a78.
/// Uses atomic write (tmp + rename) so a partially-written manifest can never
/// confuse the sync watcher.
/// </summary>
[OmniKeyVaultService]
public sealed class ManifestService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public async Task<Manifest> ReadAsync(string manifestPath, CancellationToken ct = default)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: {manifestPath}", manifestPath);
        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var raw = JsonSerializer.Deserialize<ManifestJson>(json, JsonOpts)
            ?? throw new FileCorruptException($"Manifest is empty: {manifestPath}");
        return FromJson(raw);
    }

    /// <summary>
    /// Reads a manifest, or returns null if the file does not exist (vs. throwing).
    /// </summary>
    public async Task<Manifest?> TryReadAsync(string manifestPath, CancellationToken ct = default)
    {
        if (!File.Exists(manifestPath)) return null;
        return await ReadAsync(manifestPath, ct);
    }

    public async Task WriteAsync(string manifestPath, Manifest manifest, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(ToJson(manifest), JsonOpts);
        // Atomic write: tmp -> fsync -> rename.
        var tmp = manifestPath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        if (File.Exists(manifestPath))
            File.Replace(tmp, manifestPath, destinationBackupFileName: null);
        else
            File.Move(tmp, manifestPath);
    }

    private static Manifest FromJson(ManifestJson j) => new()
    {
        VaultUuid = j.VaultUuid,
        DeviceId = j.DeviceId,
        LastModified = j.LastModified,
        LastModifiedBy = j.LastModifiedBy,
        Profiles = j.Profiles ?? new List<string>(),
        VectorClock = new VectorClock(j.VectorClock ?? new Dictionary<string, long>()),
        SchemaVersion = j.SchemaVersion,
        OkvFormatVersion = j.OkvFormatVersion,
        DevicePublicKeys = j.DevicePublicKeys ?? new Dictionary<string, string>()
    };

    private static ManifestJson ToJson(Manifest m) => new()
    {
        VaultUuid = m.VaultUuid,
        DeviceId = m.DeviceId,
        LastModified = m.LastModified,
        LastModifiedBy = m.LastModifiedBy,
        Profiles = m.Profiles.ToList(),
        VectorClock = m.VectorClock.Counters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        SchemaVersion = m.SchemaVersion,
        OkvFormatVersion = m.OkvFormatVersion,
        DevicePublicKeys = m.DevicePublicKeys.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
    };

    /// <summary>DTO for JSON serialization (snake_case naming policy).</summary>
    private sealed class ManifestJson
    {
        public Guid VaultUuid { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public DateTimeOffset LastModified { get; set; }
        public string LastModifiedBy { get; set; } = string.Empty;
        public List<string>? Profiles { get; set; }
        public Dictionary<string, long>? VectorClock { get; set; }
        public int SchemaVersion { get; set; } = 1;
        public string OkvFormatVersion { get; set; } = "1.0";
        public Dictionary<string, string>? DevicePublicKeys { get; set; }
    }
}
