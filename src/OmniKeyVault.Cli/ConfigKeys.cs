using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli;

/// <summary>
/// P7-T4: Bridge between the CLI's <c>config get / set / list</c> commands
/// and the GUI's <see cref="SettingsStore"/>. v1.0 adds the CLI side so
/// scripted users can inspect / flip settings without opening the GUI.
///
/// The implementation is intentionally a simple key→reader/writer map; adding
/// a new key is a one-liner in <see cref="All"/>. The GUI's SettingsWindow
/// continues to be the primary editor — this is just a CLI mirror for CI /
/// automation use cases.
/// </summary>
internal static class ConfigKeys
{
    private static readonly Dictionary<string, Func<string>> _readers = new(StringComparer.Ordinal)
    {
        ["auto-lock-minutes"]      = () => SettingsStore.AutoLockMinutes.ToString(),
        ["clipboard-clear-seconds"] = () => SettingsStore.ClipboardClearSeconds.ToString(),
        ["language"]               = () => SettingsStore.Language,
        ["theme"]                  = () => SettingsStore.Theme.ToString(),
        ["sync-directory"]         = () => SettingsStore.SyncDirectory ?? "",
        ["watcher-enabled"]        = () => SettingsStore.WatcherEnabled ? "true" : "false",
        ["lock-on-session-lock"]   = () => SettingsStore.LockOnSessionLock ? "true" : "false",
        ["lock-on-suspend"]        = () => SettingsStore.LockOnSuspend ? "true" : "false",
        ["attachment-directory"]   = () => SettingsStore.AttachmentDirectory ?? "",
    };

    private static readonly Dictionary<string, Action<string>> _writers = new(StringComparer.Ordinal)
    {
        ["auto-lock-minutes"]      = v => { if (int.TryParse(v, out var n) && n > 0) SettingsStore.AutoLockMinutes = n; else throw new ValidationException($"auto-lock-minutes must be a positive integer (got '{v}')."); },
        ["clipboard-clear-seconds"] = v => { if (int.TryParse(v, out var n) && n > 0) SettingsStore.ClipboardClearSeconds = n; else throw new ValidationException($"clipboard-clear-seconds must be a positive integer (got '{v}')."); },
        ["language"]               = v => { if (v != "zh-CN" && v != "en-US") throw new ValidationException($"language must be 'zh-CN' or 'en-US' (got '{v}')."); SettingsStore.Language = v; },
        ["theme"]                  = v => { if (Enum.TryParse<SettingsStore.AppTheme>(v, true, out var t)) SettingsStore.Theme = t; else throw new ValidationException($"theme must be System|Light|Dark (got '{v}')."); },
        ["sync-directory"]         = v => SettingsStore.SyncDirectory = string.IsNullOrWhiteSpace(v) ? null : v,
        ["watcher-enabled"]        = v => { if (bool.TryParse(v, out var b)) SettingsStore.WatcherEnabled = b; else throw new ValidationException($"watcher-enabled must be true|false (got '{v}')."); },
        ["lock-on-session-lock"]   = v => { if (bool.TryParse(v, out var b)) SettingsStore.LockOnSessionLock = b; else throw new ValidationException($"lock-on-session-lock must be true|false (got '{v}')."); },
        ["lock-on-suspend"]        = v => { if (bool.TryParse(v, out var b)) SettingsStore.LockOnSuspend = b; else throw new ValidationException($"lock-on-suspend must be true|false (got '{v}')."); },
        ["attachment-directory"]   = v => SettingsStore.AttachmentDirectory = string.IsNullOrWhiteSpace(v) ? null : v,
    };

    public static string? Read(string key) => _readers.TryGetValue(key, out var r) ? r() : null;

    public static bool TryWrite(string key, string value, out string err)
    {
        if (!_writers.TryGetValue(key, out var w))
        {
            err = $"Unknown config key '{key}'. Run 'okv config list' for available keys.";
            return false;
        }
        try { w(value); err = ""; return true; }
        catch (ValidationException ex) { err = ex.Message; return false; }
    }

    public static IEnumerable<KeyValuePair<string, string>> All()
        => _readers.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value()));
}
