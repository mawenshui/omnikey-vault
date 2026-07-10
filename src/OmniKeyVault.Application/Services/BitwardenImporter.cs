﻿using System.Text;
using System.Text.Json;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Bitwarden JSON importer per PRD §5.6 / ROADMAP S2-T9.
/// Accepts the standard Bitwarden vault export format (encrypted=false):
///   { "encrypted": false, "items": [ { "name": "...", "login": { "username": ..., "password": ..., "uris": [...] }, "notes": ..., "fields": [...] } ] }
/// Each item becomes an OmniKeyVault Entry of type ApiKey (or Note if no login).
/// All imported entries go into the named target profile.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class BitwardenImporter
{
    private readonly EntryService _entryService;
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public BitwardenImporter(EntryService entryService, VaultService vault, ICryptoProvider crypto)
    {
        _entryService = entryService;
        _vault = vault;
        _crypto = crypto;
    }

    /// <summary>Imports a Bitwarden JSON file. Returns the count of imported entries.</summary>
    public async Task<int> ImportAsync(string profileName, string jsonPath, CancellationToken ct = default)
    {
        _vault.GetProfile(profileName);  // validates locked + profile exists
        if (!File.Exists(jsonPath))
            throw new ValidationException($"Bitwarden export file not found: {jsonPath}");

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        return ImportFromString(profileName, json);
    }

    /// <summary>Imports from a JSON string. Visible for tests.</summary>
    public int ImportFromString(string profileName, string json)
    {
        _vault.GetProfile(profileName);
        BitwardenExport? export;
        try
        {
            export = JsonSerializer.Deserialize<BitwardenExport>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            throw new ValidationException($"Failed to parse Bitwarden JSON: {ex.Message}");
        }
        if (export == null) throw new ValidationException("Bitwarden export is null.");
        if (export.Encrypted) throw new ValidationException("Encrypted Bitwarden exports are not supported in v0.1. Please re-export with 'File format: JSON' and password protection disabled.");

        var items = export.Items ?? new();
        var count = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Name)) continue;
            var fields = new List<Field>();
            if (item.Login != null)
            {
                if (!string.IsNullOrEmpty(item.Login.Username))
                    fields.Add(new Field { Key = "username", Value = FieldCodec.Encode(item.Login.Username), Kind = FieldKind.Text, Sensitive = false });
                if (!string.IsNullOrEmpty(item.Login.Password))
                    fields.Add(new Field { Key = "password", Value = FieldCodec.Encode(item.Login.Password), Kind = FieldKind.Secret, Sensitive = true });
                if (item.Login.Uris != null)
                {
                    foreach (var u in item.Login.Uris)
                        if (!string.IsNullOrEmpty(u.Uri))
                            fields.Add(new Field { Key = "url", Value = FieldCodec.Encode(u.Uri), Kind = FieldKind.Url, Sensitive = false });
                }
                if (!string.IsNullOrEmpty(item.Login.Totp))
                    fields.Add(new Field { Key = "totp_uri", Value = FieldCodec.Encode(item.Login.Totp), Kind = FieldKind.TotpUri, Sensitive = true });
            }
            if (item.Fields != null)
            {
                foreach (var f in item.Fields)
                {
                    if (string.IsNullOrEmpty(f.Name)) continue;
                    fields.Add(new Field
                    {
                        Key = SanitizeKey(f.Name),
                        Value = FieldCodec.Encode(f.Value ?? string.Empty),
                        Kind = string.Equals(f.Type, "1", StringComparison.Ordinal) ? FieldKind.Secret : FieldKind.Text,
                        Sensitive = string.Equals(f.Type, "1", StringComparison.Ordinal)
                    });
                }
            }

            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = item.Login != null ? EntryType.ApiKey : EntryType.Note,
                Name = item.Name,
                PlatformId = null,
                Tags = item.Folder != null ? new[] { item.Folder } : Array.Empty<string>(),
                Folder = null,
                Fields = fields,
                Notes = string.IsNullOrEmpty(item.Notes) ? null : item.Notes,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = null,
                Version = 1
            };
            _vault.PutEntry(profileName, entry);
            count++;
        }
        return count;
    }

    private static string SanitizeKey(string s)
    {
        // Field keys must be snake_case ASCII per PLATFORM_TEMPLATES.md §2.2.
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-') sb.Append('_');
        }
        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "field" : result;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}

// Bitwarden export schema (unencrypted variant). Fields not used in MVP are omitted.
public sealed class BitwardenExport
{
    public bool Encrypted { get; set; }
    public List<BitwardenItem>? Items { get; set; }
}

public sealed class BitwardenItem
{
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public string? Folder { get; set; }
    public BitwardenLogin? Login { get; set; }
    public List<BitwardenCustomField>? Fields { get; set; }
}

public sealed class BitwardenLogin
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Totp { get; set; }
    public List<BitwardenUri>? Uris { get; set; }
}

public sealed class BitwardenUri
{
    public string? Uri { get; set; }
}

public sealed class BitwardenCustomField
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Type { get; set; }  // "0"=text, "1"=hidden, "2"=boolean
}
