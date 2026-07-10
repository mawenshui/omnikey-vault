namespace OmniKeyVault.Domain;

/// <summary>
/// A credential entry per OKV_FORMAT.md §3.4.
/// Immutable value object — mutations produce a new Entry with
/// incremented <see cref="Version"/> (optimistic locking).
/// </summary>
public sealed record Entry
{
    public required Guid Id { get; init; }
    public required EntryType Type { get; init; }
    public required string Name { get; init; }
    public string? PlatformId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public Guid? Folder { get; init; }
    public IReadOnlyList<Field> Fields { get; init; } = Array.Empty<Field>();
    public string? Notes { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required uint Version { get; init; }

    /// <summary>
    /// Produces a new Entry with bumped version and updated timestamp.
    /// Used by EntryService.UpdateEntry per PRD §5.2.
    /// </summary>
    public Entry WithUpdate(Func<Entry, Entry> mutator, DateTimeOffset at)
    {
        var updated = mutator(this) with { UpdatedAt = at, Version = Version + 1 };
        return updated;
    }

    /// <summary>
    /// Returns the first field matching <paramref name="key"/>, or null.
    /// </summary>
    public Field? FindField(string key)
        => Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
}
