using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: .env file import/export service.
/// .env files are commonly used for development environment variables.
/// Format: KEY=value (one per line), with # for comments.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class EnvFileService
{
    private readonly EntryService _entryService;
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public EnvFileService(EntryService entryService, VaultService vault, ICryptoProvider crypto)
    {
        _entryService = entryService;
        _vault = vault;
        _crypto = crypto;
    }

    /// <summary>Imports a .env file. Each KEY=value pair becomes a field in a single entry.</summary>
    public async Task<int> ImportAsync(string profileName, string envPath, string? entryName = null, CancellationToken ct = default)
    {
        _vault.GetProfile(profileName);
        if (!File.Exists(envPath))
            throw new ValidationException($"File not found: {envPath}");

        var content = await File.ReadAllTextAsync(envPath, ct);
        return ImportFromString(profileName, content, entryName);
    }

    /// <summary>Imports from a .env string. Returns 1 (single entry with all variables).</summary>
    public int ImportFromString(string profileName, string content, string? entryName = null)
    {
        _vault.GetProfile(profileName);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var fields = new List<Field>();
        var name = entryName ?? Path.GetFileNameWithoutExtension("env");

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith('#')) continue;

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = line[..eqIdx].Trim().Trim('"', '\'');
            var value = line[(eqIdx + 1)..].Trim().Trim('"', '\'');

            if (string.IsNullOrEmpty(key)) continue;

            // Determine if value looks sensitive (contains password, secret, key, token)
            var isSensitive = key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                              key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                              key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                              key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                              key.Contains("credential", StringComparison.OrdinalIgnoreCase);

            fields.Add(new Field
            {
                Key = key.ToLowerInvariant().Replace(" ", "_"),
                Value = FieldCodec.Encode(value),
                Kind = isSensitive ? FieldKind.Secret : FieldKind.Text,
                Sensitive = isSensitive
            });
        }

        if (fields.Count == 0) return 0;

        var now = DateTimeOffset.UtcNow;
        var entry = new Entry
        {
            Id = _crypto.NewUuidV7(),
            Type = EntryType.ApiKey,
            Name = name,
            PlatformId = null,
            Tags = new[] { "imported", "env" },
            Folder = null,
            Fields = fields,
            Notes = null,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = null,
            Version = 1
        };
        _vault.PutEntry(profileName, entry);
        return 1;
    }

    /// <summary>Exports entries from a profile to .env format.</summary>
    public string ExportToString(string profileName, Func<Field, bool>? fieldFilter = null)
    {
        _vault.GetProfile(profileName);
        var entries = _vault.ListEntries(profileName);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Exported from OmniKey Vault profile: {profileName}");
        sb.AppendLine($"# Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            sb.AppendLine($"# Entry: {entry.Name}");
            foreach (var field in entry.Fields)
            {
                if (fieldFilter != null && !fieldFilter(field)) continue;
                var value = FieldCodec.Decode(field.Value);
                sb.AppendLine($"{field.Key.ToUpperInvariant()}={value}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Exports to a file.</summary>
    public async Task ExportAsync(string profileName, string outputPath, CancellationToken ct = default)
    {
        var content = ExportToString(profileName);
        await File.WriteAllTextAsync(outputPath, content, ct);
    }
}
