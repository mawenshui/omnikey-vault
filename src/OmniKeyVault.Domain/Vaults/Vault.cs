namespace OmniKeyVault.Domain;

/// <summary>
/// Vault metadata per OKV_FORMAT.md §3.2.
/// Stored in the .okv header and the manifest.json (partial).
/// </summary>
public sealed class VaultMetadata
{
    public required Guid Uuid { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public ushort SchemaVersion { get; init; } = 1;
}

/// <summary>
/// Vault aggregate root per OKV_FORMAT.md §3.2 / ARCHITECTURE.md §4.3.
/// Holds profiles keyed by name. The DEK material is NOT stored here
/// (ADR-005: domain model does not hold key material) — wrapped DEKs
/// live in the .okv binary and are managed by CryptoProvider/LockService.
/// </summary>
public sealed class Vault
{
    public required VaultMetadata Metadata { get; init; }
    public VectorClock VectorClock { get; init; } = new();
    public IReadOnlyDictionary<string, Profile> Profiles { get; init; }
        = new Dictionary<string, Profile>(StringComparer.Ordinal);

    /// <summary>
    /// Returns the profile with the given name, or throws if absent.
    /// </summary>
    public Profile GetProfile(string name)
    {
        if (!Profiles.TryGetValue(name, out var p))
            throw new KeyNotFoundException($"Profile '{name}' does not exist.");
        return p;
    }

    /// <summary>
    /// True if the profile exists.
    /// </summary>
    public bool HasProfile(string name)
        => Profiles.ContainsKey(name);

    /// <summary>
    /// Returns all profile names in stable alphabetical order.
    /// </summary>
    public IEnumerable<string> ProfileNames
        => Profiles.Keys.OrderBy(n => n, StringComparer.Ordinal);
}
