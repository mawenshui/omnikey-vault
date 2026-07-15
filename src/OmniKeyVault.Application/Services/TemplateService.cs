﻿﻿﻿using System.Text.Json;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Loads platform templates from JSON files per PLATFORM_TEMPLATES.md §2.
/// Templates are pure JSON metadata — no credentials. Loaded at startup from
/// the templates/ directory (built-in) and %APPDATA%/OmniKeyVault/templates/
/// (user overrides).
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class TemplateService
{
    private readonly Dictionary<string, TemplateDefinition> _templates = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, TemplateDefinition> Templates => _templates;

    /// <summary>Load all template JSON files from the given directory.</summary>
    public int LoadFromDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var tpl = JsonSerializer.Deserialize<TemplateDefinition>(json, JsonOptions);
                if (tpl != null && !string.IsNullOrEmpty(tpl.Id))
                {
                    _templates[tpl.Id] = tpl;
                    count++;
                }
            }
            catch (Exception ex)
            {
                throw new ValidationException($"Failed to load template '{file}': {ex.Message}");
            }
        }
        return count;
    }

    /// <summary>Loads a single template definition from a JSON string (for testing).</summary>
    public TemplateDefinition LoadFromJson(string json)
    {
        var tpl = JsonSerializer.Deserialize<TemplateDefinition>(json, JsonOptions)
            ?? throw new ValidationException("Invalid template JSON.");
        if (string.IsNullOrEmpty(tpl.Id)) throw new ValidationException("Template must have an 'id'.");
        _templates[tpl.Id] = tpl;
        return tpl;
    }

    public TemplateDefinition Get(string id)
        => _templates.TryGetValue(id, out var t)
            ? t
            : throw new ValidationException($"Template '{id}' not found. Available: {string.Join(", ", _templates.Keys.OrderBy(k => k))}");

    public bool TryGet(string id, out TemplateDefinition? tpl)
        => _templates.TryGetValue(id, out tpl);

    /// <summary>Returns the 5 MVP templates (PLATFORM_TEMPLATES.md §3).</summary>
    public IEnumerable<TemplateDefinition> ListMvp()
        => _templates.Values.Where(t => t.MvpIncluded).OrderBy(t => t.Id, StringComparer.Ordinal);

    public IEnumerable<TemplateDefinition> ListAll()
        => _templates.Values.OrderBy(t => t.Id, StringComparer.Ordinal);

    /// <summary>v1.9: Filter templates by region ("domestic" or "international").
    /// Templates without a Region field default to "international".</summary>
    public IEnumerable<TemplateDefinition> ListByRegion(string region)
        => _templates.Values
            .Where(t => string.Equals(t.Region ?? "international", region, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Id, StringComparer.Ordinal);

    /// <summary>v1.9: Filter templates by category.</summary>
    public IEnumerable<TemplateDefinition> ListByCategory(string category)
        => _templates.Values
            .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Id, StringComparer.Ordinal);

    /// <summary>Builds a Domain Template (minimal storage form) from the full definition.</summary>
    public Template ToDomainTemplate(TemplateDefinition def)
    {
        var fields = def.Fields.Select(f => new TemplateField
        {
            Key = f.Key,
            Kind = ParseFieldKind(f.Kind),
            Sensitive = f.Sensitive,
            Required = f.Required,
            DefaultMask = f.DefaultMask,
            Validation = f.Validation != null
                ? new FieldValidation { Regex = f.Validation.Regex, Hint = f.Validation.Hint }
                : null
        }).ToList();
        return new Template { Id = def.Id, PlatformId = def.PlatformId, Fields = fields };
    }

    /// <summary>Creates a fresh Entry (empty values) from a template definition.</summary>
    public Entry CreateEntryFromTemplate(TemplateDefinition def, string name, DateTimeOffset now)
    {
        if (def.Kind == "secret" && def.Sensitive == false)
            throw new ValidationException($"Template '{def.Id}' has kind=secret but sensitive=false (invariant violation per PLATFORM_TEMPLATES.md §2.3).");
        foreach (var f in def.Fields)
            if (f.Kind == "secret" && f.Sensitive == false)
                throw new ValidationException($"Field '{f.Key}' has kind=secret but sensitive=false (invariant violation).");

        var fields = def.Fields.Select(f => new Field
        {
            Key = f.Key,
            Value = Array.Empty<byte>(),
            Kind = ParseFieldKind(f.Kind),
            Sensitive = f.Sensitive,
            Mask = f.DefaultMask,
            Validation = f.Validation != null
                ? new FieldValidation { Regex = f.Validation.Regex, Hint = f.Validation.Hint }
                : null
        }).ToList();

        return new Entry
        {
            Id = Guid.NewGuid(),  // v0.1 MVP uses Guid.NewGuid() (random UUIDv4). UUIDv7 generator available in ICryptoProvider.NewUuidV7.
            Type = ResolveEntryType(def),
            Name = name,
            PlatformId = def.PlatformId,
            Tags = Array.Empty<string>(),
            Folder = null,
            Fields = fields,
            Notes = null,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = null,
            Version = 1
        };
    }

    /// <summary>v1.9: Resolves EntryType from template category.
    /// ssh_key → SshKey, note → Note, certificate → Certificate,
    /// oauth → OAuth, everything else → ApiKey.</summary>
    private static EntryType ResolveEntryType(TemplateDefinition def)
    {
        return def.Category switch
        {
            "ssh_key" => EntryType.SshKey,
            "note" => EntryType.Note,
            "certificate" => EntryType.Certificate,
            "oauth" => EntryType.OAuth,
            _ => EntryType.ApiKey
        };
    }

    private static FieldKind ParseFieldKind(string s) => s.ToLowerInvariant() switch
    {
        "text" => FieldKind.Text,
        "secret" => FieldKind.Secret,
        "url" => FieldKind.Url,
        "number" => FieldKind.Number,
        "date" => FieldKind.Date,
        "totp_uri" => FieldKind.TotpUri,
        "file_ref" => FieldKind.FileRef,
        _ => throw new ValidationException($"Unknown field kind '{s}'.")
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

/// <summary>
/// Full template definition per PLATFORM_TEMPLATES.md §2.1.
/// Includes UI metadata (label, placeholder, group, icon, etc.) that is NOT
/// stored in the .okv binary — only loaded at runtime from JSON.
/// </summary>
public sealed class TemplateDefinition
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>v1.9: "domestic" (国内) or "international" (国际). Defaults to "international".</summary>
    public string Region { get; set; } = "international";
    public string? Icon { get; set; }
    public string OfficialDocsUrl { get; set; } = string.Empty;
    public string? AuthHeader { get; set; }
    public string? DefaultBaseUrl { get; set; }
    public bool MvpIncluded { get; set; }
    public string IntroducedIn { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool ExpiresAtSupported { get; set; } = true;
    public bool RotationSupported { get; set; }
    public string? RotationProvider { get; set; }
    public List<TemplateFieldDefinition> Fields { get; set; } = new();
    public string? Kind { get; set; }       // top-level (unused in MVP, but tolerated)
    public bool? Sensitive { get; set; }    // top-level (unused in MVP, but tolerated)
}

public sealed class TemplateFieldDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "text";
    public bool Sensitive { get; set; }
    public bool Required { get; set; }
    public string? DefaultMask { get; set; }
    public string? Placeholder { get; set; }
    public FieldValidationDefinition? Validation { get; set; }
    public bool ExpiresAtSupported { get; set; } = true;
    public bool Rotatable { get; set; }
    public string? Description { get; set; }
    public List<string>? Examples { get; set; }
    public string? Group { get; set; }
}

public sealed class FieldValidationDefinition
{
    public string? Regex { get; set; }
    public string? Hint { get; set; }
}
