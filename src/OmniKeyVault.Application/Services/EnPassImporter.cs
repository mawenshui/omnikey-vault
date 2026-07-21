using System.Text.Json;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.1: EnPass importer. EnPass can export to JSON format.
/// The JSON structure contains folders and items with fields.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class EnPassImporter
{
    private readonly EntryService _entryService;
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public EnPassImporter(EntryService entryService, VaultService vault, ICryptoProvider crypto)
    {
        _entryService = entryService;
        _vault = vault;
        _crypto = crypto;
    }

    /// <summary>Imports an EnPass JSON export. Returns the count of imported entries.</summary>
    public async Task<int> ImportAsync(string profileName, string jsonPath, CancellationToken ct = default)
    {
        _vault.GetProfile(profileName);
        if (!File.Exists(jsonPath))
            throw new ValidationException($"EnPass export file not found: {jsonPath}");

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        return ImportFromString(profileName, json);
    }

    /// <summary>Imports from an EnPass JSON string.</summary>
    public int ImportFromString(string profileName, string json)
    {
        _vault.GetProfile(profileName);
        var doc = JsonDocument.Parse(json);
        var count = 0;

        // EnPass JSON structure: { "items": [ { "title", "category", "fields": [...], "notes" } ] }
        // or { "folders": [ { "items": [...] } ] }
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                count += ImportItem(profileName, item);
            }
        }
        else if (doc.RootElement.TryGetProperty("folders", out var folders))
        {
            foreach (var folder in folders.EnumerateArray())
            {
                if (folder.TryGetProperty("items", out var folderItems))
                {
                    foreach (var item in folderItems.EnumerateArray())
                    {
                        count += ImportItem(profileName, item);
                    }
                }
            }
        }

        return count;
    }

    private int ImportItem(string profileName, JsonElement item)
    {
        try
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "Untitled" : "Untitled";
            var category = item.TryGetProperty("category", out var c) ? c.GetString() ?? "login" : "login";
            var notes = item.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";

            var fields = new List<Field>();
            if (item.TryGetProperty("fields", out var fieldsEl))
            {
                foreach (var field in fieldsEl.EnumerateArray())
                {
                    var label = field.TryGetProperty("label", out var l) ? l.GetString() ?? "field" : "field";
                    var value = field.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                    var type = field.TryGetProperty("type", out var tp) ? tp.GetString() ?? "text" : "text";

                    var isSensitive = type == "password" || type == "secret"
                        || label.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("pin", StringComparison.OrdinalIgnoreCase);

                    var kind = isSensitive ? FieldKind.Secret
                        : type == "url" ? FieldKind.Url
                        : type == "totp" ? FieldKind.TotpUri
                        : FieldKind.Text;
                    var encoded = FieldCodec.Encode(value);
                    fields.Add(new Field
                    {
                        Key = label,
                        Value = encoded,
                        Kind = kind,
                        Sensitive = isSensitive,
                    });
                }
            }

            var entryType = category.ToLowerInvariant() switch
            {
                "login" => EntryType.ApiKey,
                "password" => EntryType.Custom,
                "note" or "securenote" => EntryType.Note,
                "creditcard" => EntryType.Custom,
                "identity" => EntryType.Custom,
                _ => EntryType.Custom,
            };

            var entry = new Entry
            {
                Id = Guid.NewGuid(),
                Name = title,
                Type = entryType,
                PlatformId = "enpass",
                Fields = fields,
                Notes = notes,
                Tags = new List<string> { "imported", "enpass" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1u,
            };

            _vault.PutEntry(profileName, entry);
            return 1;
        }
        catch
        {
            return 0;
        }
    }
}
