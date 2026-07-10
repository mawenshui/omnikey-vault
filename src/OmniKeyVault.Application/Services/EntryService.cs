﻿using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Entry CRUD service per ARCHITECTURE.md §4.2 / PRD §5.2.
/// All write operations require the Vault to be unlocked (INV-04).
/// The service does NOT itself re-encrypt + persist — caller invokes
/// VaultService.SaveAsync to commit. This separation lets us batch
/// multiple entry mutations into a single atomic save.
/// </summary>
[OmniKeyVaultService]
public sealed class EntryService
{
    private readonly VaultService _vault;
    private readonly TemplateService _templates;
    private readonly ClipboardService _clipboard;
    private readonly ICryptoProvider _crypto;

    public EntryService(VaultService vault, TemplateService templates, ClipboardService clipboard, ICryptoProvider crypto)
    {
        _vault = vault;
        _templates = templates;
        _clipboard = clipboard;
        _crypto = crypto;
    }

    /// <summary>Creates a new entry from a template. Returns the new entry (caller can fill fields then call Update).</summary>
    public Entry CreateFromTemplate(string profileName, string templateId, string name)
    {
        _vault.GetProfile(profileName);  // throws if locked or missing
        var def = _templates.Get(templateId);
        var now = DateTimeOffset.UtcNow;
        return _templates.CreateEntryFromTemplate(def, name, now);
    }

    /// <summary>Creates a new bare entry (no template). Used for Bitwarden imports and custom entries.</summary>
    public Entry Create(string profileName, string name, EntryType type, string? platformId, IEnumerable<Field> fields, IEnumerable<string>? tags = null, DateTimeOffset? expiresAt = null)
    {
        _vault.GetProfile(profileName);
        var now = DateTimeOffset.UtcNow;
        return new Entry
        {
            Id = _crypto.NewUuidV7(),
            Type = type,
            Name = name,
            PlatformId = platformId,
            Tags = tags?.ToList() ?? new List<string>(),
            Folder = null,
            Fields = fields.ToList(),
            Notes = null,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt,
            Version = 1
        };
    }

    /// <summary>Inserts or updates an entry. Returns the persisted version (with bumped version on update).</summary>
    public Entry Upsert(string profileName, Entry entry)
    {
        _vault.GetProfile(profileName);
        var existing = _vault.ListEntries(profileName).FirstOrDefault(e => e.Id == entry.Id);
        var final = existing == null
            ? entry
            : entry with { Version = existing.Version + 1, UpdatedAt = DateTimeOffset.UtcNow, CreatedAt = existing.CreatedAt };
        _vault.PutEntry(profileName, final);
        return final;
    }

    /// <summary>Sets a single field on an entry. Idempotent: same value does not bump version (CLI_SPEC §5.3).</summary>
    public Entry SetField(string profileName, Guid entryId, string fieldKey, string value)
    {
        var entry = _vault.GetEntry(profileName, entryId) ?? throw new EntryNotFoundException(entryId);
        var existingField = entry.FindField(fieldKey);
        if (existingField != null && existingField.ValueString == value)
            return entry;  // idempotent — no version bump

        var encodedValue = FieldCodec.Encode(value);
        var newFields = entry.Fields
            .Select(f => f.Key == fieldKey ? f with { Value = encodedValue } : f)
            .ToList();
        if (existingField == null)
        {
            // Add a new field as a plain secret by default
            newFields.Add(new Field { Key = fieldKey, Value = encodedValue, Kind = FieldKind.Secret, Sensitive = true });
        }
        var updated = entry with { Fields = newFields, Version = entry.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
        _vault.PutEntry(profileName, updated);
        return updated;
    }

    /// <summary>Returns the field value (plaintext) — caller is responsible for safe handling.</summary>
    public string GetField(string profileName, Guid entryId, string fieldKey)
    {
        var entry = _vault.GetEntry(profileName, entryId) ?? throw new EntryNotFoundException(entryId);
        var field = entry.FindField(fieldKey) ?? throw new FieldNotFoundException(fieldKey);
        return field.ValueString;
    }

    /// <summary>Copies a field value to the clipboard with 8s auto-clear (PRD §5.11).</summary>
    public void CopyField(string profileName, Guid entryId, string fieldKey, int clearAfterSeconds = 8)
    {
        var value = GetField(profileName, entryId, fieldKey);
        _clipboard.CopySensitive(value, clearAfterSeconds);
    }

    /// <summary>Deletes an entry. Throws if not found.</summary>
    public void Delete(string profileName, Guid entryId)
    {
        var entry = _vault.GetEntry(profileName, entryId) ?? throw new EntryNotFoundException(entryId);
        _vault.DeleteEntry(profileName, entryId);
    }

    /// <summary>Returns all entries in a profile, optionally filtered by tag/platform/search.</summary>
    public IReadOnlyList<Entry> List(string profileName, string? tag = null, string? platform = null, string? search = null)
    {
        var all = _vault.ListEntries(profileName);
        IEnumerable<Entry> filtered = all;
        if (!string.IsNullOrEmpty(tag)) filtered = filtered.Where(e => e.Tags.Contains(tag, StringComparer.Ordinal));
        if (!string.IsNullOrEmpty(platform)) filtered = filtered.Where(e => string.Equals(e.PlatformId, platform, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(e =>
                e.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.PlatformId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                e.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
        return filtered.OrderByDescending(e => e.UpdatedAt).ToList();
    }
}
