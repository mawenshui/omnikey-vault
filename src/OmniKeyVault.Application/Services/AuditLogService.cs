using System.Collections.Concurrent;

namespace OmniKeyVault.Application;

/// <summary>
/// v1.8: Local audit log service. Records all critical operations
/// (unlock, lock, create, edit, delete, rotate, change-password, sync)
/// to a persistent JSON-lines file so the user can review "who did what
/// and when" without relying on the OS event log.
///
/// The log file lives at %APPDATA%/OmniKeyVault/audit.log and is
/// append-only. Each line is a JSON object with a timestamp, action,
/// and optional detail. Sensitive values (passwords, API keys) are
/// never logged — only entry names and operation types.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class AuditLogService
{
    private readonly string _logPath;
    private readonly ConcurrentQueue<AuditEntry> _buffer = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _flushTimer;

    /// <summary>Max entries buffered before a forced flush.</summary>
    private const int FlushThreshold = 50;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    public AuditLogService()
    {
        _logPath = ResolveLogPath();
        EnsureDirectory();
        _flushTimer = new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>Records an audit event. Non-blocking — buffered and flushed periodically.</summary>
    public void Log(AuditAction action, string? detail = null, string? profileName = null, string? entryName = null)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Action = action,
            Detail = detail,
            ProfileName = profileName,
            EntryName = entryName,
        };
        _buffer.Enqueue(entry);
        if (_buffer.Count >= FlushThreshold)
            _ = FlushAsync();
    }

    /// <summary>Convenience method for unlock events.</summary>
    public void LogUnlock(string vaultPath) =>
        Log(AuditAction.Unlock, detail: vaultPath);

    /// <summary>Convenience method for lock events.</summary>
    public void LogLock() =>
        Log(AuditAction.Lock);

    /// <summary>Convenience method for entry create events.</summary>
    public void LogCreateEntry(string profileName, string entryName) =>
        Log(AuditAction.CreateEntry, profileName: profileName, entryName: entryName);

    /// <summary>Convenience method for entry edit events.</summary>
    public void LogEditEntry(string profileName, string entryName) =>
        Log(AuditAction.EditEntry, profileName: profileName, entryName: entryName);

    /// <summary>Convenience method for entry delete events.</summary>
    public void LogDeleteEntry(string profileName, string entryName) =>
        Log(AuditAction.DeleteEntry, profileName: profileName, entryName: entryName);

    /// <summary>Convenience method for credential rotation events.</summary>
    public void LogRotate(string profileName, string entryName, string platformId) =>
        Log(AuditAction.Rotate, detail: platformId, profileName: profileName, entryName: entryName);

    /// <summary>Convenience method for change-password events.</summary>
    public void LogChangePassword() =>
        Log(AuditAction.ChangePassword);

    /// <summary>Convenience method for sync events.</summary>
    public void LogSync(string direction, string? outcome = null) =>
        Log(AuditAction.Sync, detail: string.IsNullOrEmpty(outcome) ? direction : $"{direction}: {outcome}");

    /// <summary>Convenience method for import events.</summary>
    public void LogImport(string profileName, string format, int count) =>
        Log(AuditAction.Import, detail: $"{format}: {count} entries", profileName: profileName);

    /// <summary>Convenience method for export events.</summary>
    public void LogExport(string profileName, string format) =>
        Log(AuditAction.Export, detail: format, profileName: profileName);

    /// <summary>Flushes all buffered entries to disk. Thread-safe.</summary>
    public async Task FlushAsync()
    {
        if (_buffer.IsEmpty) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_buffer.IsEmpty) return;
            var entries = new List<AuditEntry>();
            while (_buffer.TryDequeue(out var entry))
                entries.Add(entry);
            if (entries.Count == 0) return;

            var lines = entries.Select(e => System.Text.Json.JsonSerializer.Serialize(e, JsonOpts));
            await File.AppendAllLinesAsync(_logPath, lines).ConfigureAwait(false);
        }
        catch
        {
            // Audit logging is best-effort — never crash the app on a log write failure.
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads all audit entries from the log file. Returns an empty list if the file doesn't exist.</summary>
    public async Task<IReadOnlyList<AuditEntry>> ReadAllAsync()
    {
        if (!File.Exists(_logPath)) return Array.Empty<AuditEntry>();
        var entries = new List<AuditEntry>();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var lines = await File.ReadAllLinesAsync(_logPath).ConfigureAwait(false);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = System.Text.Json.JsonSerializer.Deserialize<AuditEntry>(line, JsonOpts);
                    if (entry != null) entries.Add(entry);
                }
                catch { /* skip malformed lines */ }
            }
        }
        catch
        {
            // best-effort read
        }
        finally
        {
            _gate.Release();
        }
        return entries;
    }

    /// <summary>Exports audit entries to a CSV file at the given path.</summary>
    public async Task<int> ExportCsvAsync(string outputPath)
    {
        var entries = await ReadAllAsync();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Timestamp,Action,Profile,EntryName,Detail");
        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(e.Action.ToString()),
                EscapeCsv(e.ProfileName ?? ""),
                EscapeCsv(e.EntryName ?? ""),
                EscapeCsv(e.Detail ?? "")));
        }
        await File.WriteAllTextAsync(outputPath, sb.ToString());
        return entries.Count;
    }

    /// <summary>Clears the audit log file.</summary>
    public void Clear()
    {
        try { if (File.Exists(_logPath)) File.Delete(_logPath); }
        catch { /* best-effort */ }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string ResolveLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "audit.log");
    }

    private void EnsureDirectory()
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch { /* best-effort */ }
    }
}

/// <summary>Types of auditable actions.</summary>
public enum AuditAction
{
    Unlock,
    Lock,
    CreateEntry,
    EditEntry,
    DeleteEntry,
    Rotate,
    ChangePassword,
    Sync,
    Import,
    Export,
}

/// <summary>A single audit log entry. Serialized as JSON-lines.</summary>
public sealed record AuditEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required AuditAction Action { get; init; }
    public string? Detail { get; init; }
    public string? ProfileName { get; init; }
    public string? EntryName { get; init; }
}
