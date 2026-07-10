namespace OmniKeyVault.Domain;

/// <summary>
/// Folder within a Profile per OKV_FORMAT.md §3.6.
/// Tree structure via <see cref="ParentId"/> (null = root).
/// </summary>
public sealed record Folder
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
}
