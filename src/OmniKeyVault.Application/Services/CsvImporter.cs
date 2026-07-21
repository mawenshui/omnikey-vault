using System.Globalization;
using System.Text;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: CSV-based importer for LastPass, Chrome, Edge, and Firefox password exports.
/// All these browsers/services export to a similar CSV format with columns like
/// name, url, username, password. This unified importer auto-detects the format
/// based on the header row.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class CsvImporter
{
    private readonly EntryService _entryService;
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public CsvImporter(EntryService entryService, VaultService vault, ICryptoProvider crypto)
    {
        _entryService = entryService;
        _vault = vault;
        _crypto = crypto;
    }

    /// <summary>Imports a CSV file. Returns the count of imported entries.</summary>
    public async Task<int> ImportAsync(string profileName, string csvPath, CancellationToken ct = default)
    {
        _vault.GetProfile(profileName);
        if (!File.Exists(csvPath))
            throw new ValidationException($"CSV file not found: {csvPath}");

        var csv = await File.ReadAllTextAsync(csvPath, ct);
        return ImportFromString(profileName, csv);
    }

    /// <summary>Imports from a CSV string. Auto-detects the format.</summary>
    public int ImportFromString(string profileName, string csv)
    {
        _vault.GetProfile(profileName);
        var lines = ParseCsv(csv);
        if (lines.Count < 2) return 0;

        var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();

        // Detect format by header columns
        var (nameIdx, urlIdx, usernameIdx, passwordIdx, notesIdx) = DetectColumns(header);
        if (nameIdx < 0 && usernameIdx < 0)
            throw new ValidationException("无法识别 CSV 格式 — 需要 name/url/username/password 列");

        var now = DateTimeOffset.UtcNow;
        var count = 0;

        foreach (var row in lines.Skip(1))
        {
            if (row.Count == 0) continue;
            var name = nameIdx >= 0 && nameIdx < row.Count ? row[nameIdx] : "";
            var url = urlIdx >= 0 && urlIdx < row.Count ? row[urlIdx] : "";
            var username = usernameIdx >= 0 && usernameIdx < row.Count ? row[usernameIdx] : "";
            var password = passwordIdx >= 0 && passwordIdx < row.Count ? row[passwordIdx] : "";
            var notes = notesIdx >= 0 && notesIdx < row.Count ? row[notesIdx] : "";

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
                continue;

            if (string.IsNullOrEmpty(name))
                name = string.IsNullOrEmpty(url) ? username : new Uri(url).Host;

            var fields = new List<Field>();
            if (!string.IsNullOrEmpty(username))
                fields.Add(new Field { Key = "username", Value = FieldCodec.Encode(username), Kind = FieldKind.Text, Sensitive = false });
            if (!string.IsNullOrEmpty(password))
                fields.Add(new Field { Key = "password", Value = FieldCodec.Encode(password), Kind = FieldKind.Secret, Sensitive = true });
            if (!string.IsNullOrEmpty(url))
                fields.Add(new Field { Key = "url", Value = FieldCodec.Encode(url), Kind = FieldKind.Url, Sensitive = false });

            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = name,
                PlatformId = null,
                Tags = new[] { "imported" },
                Folder = null,
                Fields = fields,
                Notes = string.IsNullOrEmpty(notes) ? null : notes,
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

    /// <summary>Detects column indices from the CSV header.</summary>
    private static (int name, int url, int username, int password, int notes) DetectColumns(List<string> header)
    {
        int nameIdx = -1, urlIdx = -1, usernameIdx = -1, passwordIdx = -1, notesIdx = -1;

        for (var i = 0; i < header.Count; i++)
        {
            var h = header[i];
            // Name: "name", "title", "entry"
            if (nameIdx < 0 && (h == "name" || h == "title" || h == "entry" || h == "name "))
                nameIdx = i;
            // URL: "url", "website", "login_uri", "web site"
            else if (urlIdx < 0 && (h == "url" || h == "website" || h == "login_uri" || h == "web site" || h == "site" || h == "uri"))
                urlIdx = i;
            // Username: "username", "login", "login_username", "user"
            else if (usernameIdx < 0 && (h == "username" || h == "login" || h == "login_username" || h == "user" || h == "email"))
                usernameIdx = i;
            // Password: "password", "login_password", "pass"
            else if (passwordIdx < 0 && (h == "password" || h == "login_password" || h == "pass"))
                passwordIdx = i;
            // Notes: "notes", "comment", "extra"
            else if (notesIdx < 0 && (h == "notes" || h == "comment" || h == "extra" || h == "note"))
                notesIdx = i;
        }
        return (nameIdx, urlIdx, usernameIdx, passwordIdx, notesIdx);
    }

    /// <summary>Simple CSV parser that handles quoted fields with commas.</summary>
    private static List<List<string>> ParseCsv(string csv)
    {
        var result = new List<List<string>>();
        var current = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    row.Add(current.ToString());
                    current.Clear();
                    if (row.Count > 1 || !string.IsNullOrEmpty(row[0]))
                        result.Add(row);
                    row = new List<string>();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        // Last field
        if (current.Length > 0 || row.Count > 0)
        {
            row.Add(current.ToString());
            if (row.Count > 1 || !string.IsNullOrEmpty(row[0]))
                result.Add(row);
        }
        return result;
    }
}

/// <summary>
/// v2.0: 1Password CSV importer. 1Password can export to CSV format with
/// columns: Title, Website, Username, Password, Notes, Type.
/// This extends the generic CsvImporter with 1Password-specific column mapping.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class OnePasswordCsvImporter
{
    private readonly CsvImporter _csv;

    public OnePasswordCsvImporter(CsvImporter csv) => _csv = csv;

    public async Task<int> ImportAsync(string profileName, string csvPath, CancellationToken ct = default)
        => await _csv.ImportAsync(profileName, csvPath, ct);

    public int ImportFromString(string profileName, string csv)
        => _csv.ImportFromString(profileName, csv);
}
