namespace OmniKeyVault.Domain;

/// <summary>
/// A named profile within a Vault per OKV_FORMAT.md §3.3.
/// Profiles have independent DEKs (PRD §5.1) — compromise of one
/// does not affect others (depth-of-defense per SECURITY.md §1.4).
/// DEK is NOT stored in the domain model (ADR-005) — it lives in
/// the wrapped form in the .okv binary header, managed by CryptoProvider.
/// </summary>
public sealed class Profile
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public ProfileColor Color { get; init; } = ProfileColor.Green;
    public ProfileSettings Settings { get; init; } = ProfileSettings.DefaultProd();
    public IReadOnlyList<Entry> Entries { get; init; } = Array.Empty<Entry>();
    public IReadOnlyList<Folder> Folders { get; init; } = Array.Empty<Folder>();
    public IReadOnlyList<Template> Templates { get; init; } = Array.Empty<Template>();

    /// <summary>
    /// Returns the entry with the given id, or null.
    /// </summary>
    public Entry? FindEntry(Guid id)
        => Entries.FirstOrDefault(e => e.Id == id);
}
