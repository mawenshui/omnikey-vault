using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: Password history tracking for individual fields.
/// Records previous values of sensitive fields when they are changed,
/// allowing users to view and restore old passwords.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class PasswordHistoryService
{
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public PasswordHistoryService(VaultService vault, ICryptoProvider crypto)
    {
        _vault = vault;
        _crypto = crypto;
    }

    private static string HistoryPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "OmniKeyVault", "password-history.json");

    /// <summary>Records a field value change in the password history.</summary>
    public void RecordChange(Guid entryId, string entryName, string fieldKey, string oldValue, string newValue)
    {
        var history = LoadHistory();
        var now = DateTimeOffset.UtcNow;

        var entry = history.FirstOrDefault(h => h.EntryId == entryId && h.FieldKey == fieldKey);
        if (entry == null)
        {
            entry = new PasswordHistoryRecord
            {
                EntryId = entryId,
                EntryName = entryName,
                FieldKey = fieldKey,
                Versions = new List<PasswordVersion>()
            };
            history.Add(entry);
        }

        entry.EntryName = entryName; // Update in case entry was renamed
        entry.Versions.Add(new PasswordVersion
        {
            Value = oldValue,
            ChangedAt = now
        });

        // Keep at most 20 versions per field
        if (entry.Versions.Count > 20)
            entry.Versions = entry.Versions.Skip(entry.Versions.Count - 20).ToList();

        SaveHistory(history);
    }

    /// <summary>Gets the password history for a specific entry + field.</summary>
    public List<PasswordVersion> GetHistory(Guid entryId, string fieldKey)
    {
        var history = LoadHistory();
        var entry = history.FirstOrDefault(h => h.EntryId == entryId && h.FieldKey == fieldKey);
        return entry?.Versions ?? new List<PasswordVersion>();
    }

    /// <summary>Gets all password history for an entry.</summary>
    public List<PasswordHistoryRecord> GetEntryHistory(Guid entryId)
    {
        var history = LoadHistory();
        return history.Where(h => h.EntryId == entryId).ToList();
    }

    /// <summary>Clears the password history for an entry.</summary>
    public void ClearHistory(Guid entryId)
    {
        var history = LoadHistory();
        history.RemoveAll(h => h.EntryId == entryId);
        SaveHistory(history);
    }

    private static List<PasswordHistoryRecord> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return new List<PasswordHistoryRecord>();
            var json = File.ReadAllText(HistoryPath);
            return System.Text.Json.JsonSerializer.Deserialize<List<PasswordHistoryRecord>>(json) ?? new();
        }
        catch { return new List<PasswordHistoryRecord>(); }
    }

    private static void SaveHistory(List<PasswordHistoryRecord> history)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(HistoryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(history, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryPath, json);
        }
        catch { /* best-effort */ }
    }
}

public sealed class PasswordHistoryRecord
{
    public Guid EntryId { get; set; }
    public string EntryName { get; set; } = "";
    public string FieldKey { get; set; } = "";
    public List<PasswordVersion> Versions { get; set; } = new();
}

public sealed class PasswordVersion
{
    public string Value { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
}
