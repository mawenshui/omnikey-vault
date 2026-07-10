namespace OmniKeyVault.Domain;

/// <summary>
/// Minimal template field stored in the .okv binary per OKV_FORMAT.md §3.6.
/// UI display metadata (label, placeholder, group, etc.) is loaded
/// from templates/*.json at runtime, not stored in the binary.
/// </summary>
public sealed record TemplateField
{
    public required string Key { get; init; }
    public required FieldKind Kind { get; init; }
    public required bool Sensitive { get; init; }
    public required bool Required { get; init; }
    public string? DefaultMask { get; init; }
    public FieldValidation? Validation { get; init; }
}

/// <summary>
/// Template reference stored in the .okv binary per OKV_FORMAT.md §3.6.
/// Full template definitions (with UI metadata) live in templates/*.json.
/// </summary>
public sealed record Template
{
    public required string Id { get; init; }
    public required string PlatformId { get; init; }
    public IReadOnlyList<TemplateField> Fields { get; init; } = Array.Empty<TemplateField>();
}
