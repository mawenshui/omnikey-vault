using System.IO.Compression;
using System.Text.Json;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.1: 1Password .1pux native format importer.
/// The .1pux format is a ZIP archive containing an export.data file
/// with JSON that describes all vaults, items, and fields.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class OnePuxImporter
{
    private readonly EntryService _entryService;
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public OnePuxImporter(EntryService entryService, VaultService vault, ICryptoProvider crypto)
    {
        _entryService = entryService;
        _vault = vault;
        _crypto = crypto;
    }

    /// <summary>Imports a .1pux file (ZIP archive). Returns the count of imported entries.</summary>
    public async Task<int> ImportAsync(string profileName, string puxPath, CancellationToken ct = default)
    {
        _vault.GetProfile(profileName);
        if (!File.Exists(puxPath))
            throw new ValidationException($"1Password export file not found: {puxPath}");

        using var archive = ZipFile.OpenRead(puxPath);
        var exportDataEntry = archive.Entries.FirstOrDefault(e => e.Name == "export.data")
            ?? throw new ValidationException("Invalid .1pux file: export.data not found in archive");

        using var stream = exportDataEntry.Open();
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var count = 0;

        // The .1pux format has: { "accounts": [ { "vaults": [ { "items": [ ... ] } ] } ] }
        if (doc.RootElement.TryGetProperty("accounts", out var accounts))
        {
            foreach (var account in accounts.EnumerateArray())
            {
                if (account.TryGetProperty("vaults", out var vaults))
                {
                    foreach (var vault in vaults.EnumerateArray())
                    {
                        if (vault.TryGetProperty("items", out var items))
                        {
                            foreach (var item in items.EnumerateArray())
                            {
                                count += ImportItem(profileName, item);
                            }
                        }
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
            var typeName = item.TryGetProperty("category", out var cat) ? cat.GetString() ?? "Login" : "Login";
            var urls = new List<string>();
            var notes = item.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";

            // Extract URLs
            if (item.TryGetProperty("urls", out var urlsEl))
            {
                foreach (var url in urlsEl.EnumerateArray())
                {
                    if (url.TryGetProperty("href", out var href))
                        urls.Add(href.GetString() ?? "");
                }
            }

            // Build fields from item.fields
            var fields = new List<Field>();
            if (item.TryGetProperty("fields", out var fieldsEl))
            {
                foreach (var field in fieldsEl.EnumerateArray())
                {
                    var id = field.TryGetProperty("id", out var fid) ? fid.GetString() ?? "" : "";
                    var label = field.TryGetProperty("label", out var fl) ? fl.GetString() ?? id : id;
                    var value = field.TryGetProperty("value", out var fv) ? fv.GetString() ?? "" : "";
                    var purpose = field.TryGetProperty("purpose", out var fp) ? fp.GetString() ?? "" : "";

                    // Determine if sensitive
                    var isSensitive = purpose == "PASSWORD" || id.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("secret", StringComparison.OrdinalIgnoreCase);

                    var kind = isSensitive ? FieldKind.Secret : FieldKind.Text;
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

            // Map 1Password category to OmniKeyVault entry type
            var entryType = typeName switch
            {
                "LOGIN" => EntryType.ApiKey,
                "PASSWORD" => EntryType.Custom,
                "SECURE_NOTE" => EntryType.Note,
                "CREDIT_CARD" => EntryType.Custom,
                "IDENTITY" => EntryType.Custom,
                _ => EntryType.Custom,
            };

            var platformId = urls.FirstOrDefault()?.ExtractDomain() ?? "1password";

            var entry = new Entry
            {
                Id = Guid.NewGuid(),
                Name = title,
                Type = entryType,
                PlatformId = platformId,
                Fields = fields,
                Notes = notes,
                Tags = new List<string> { "imported", "1password" },
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

/// <summary>Extension methods for URL processing.</summary>
internal static class UrlExtensions
{
    public static string ExtractDomain(this string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host.Replace("www.", "");
        }
        catch { }
        return url;
    }
}
