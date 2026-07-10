namespace OmniKeyVault.Cli;

/// <summary>
/// In-memory user settings. Phase 11: persisted to
/// %APPDATA%/OmniKeyVault/settings.json on changes and loaded on startup.
/// Default values match UI_UX_SPEC §5.5 (15 min auto-lock) and §5.1 (8 s clipboard).
/// </summary>
/// <remarks>
/// v1.4: Extracted from SettingsWindow.axaml.cs to its own file.
/// Previously this class was nested at the bottom of the Settings view
/// code-behind (SettingsWindow.axaml.cs:691), which made it invisible to
/// code search tools and coupled persistence logic to the view layer.
/// </remarks>
public static class SettingsStore
{
    public static int AutoLockMinutes { get; set; } = 15;
    public static int ClipboardClearSeconds { get; set; } = 8;

    // v0.2 S6-T6: language + theme. zh-CN is the default per MANUAL §15.4
    // (and the explicit per-locale string table in §15.3); en-US lands in v0.3.
    public static string Language { get; set; } = "zh-CN";

    public enum AppTheme { System, Light, Dark }
    public static AppTheme Theme { get; set; } = AppTheme.Light;

    // v0.2 S4-T1: sync directory + watcher enable. When null/empty, the GUI
    // starts the watcher on the vault file's parent directory so out-of-band
    // replacements still trigger a toast.
    public static string? SyncDirectory { get; set; }
    public static bool WatcherEnabled { get; set; } = true;

    // v0.2 S7-T1: when true, the OS system-events subscription triggers an
    // immediate lock on SessionLock + Suspend per MANUAL §12.5.
    public static bool LockOnSessionLock { get; set; } = true;
    public static bool LockOnSuspend { get; set; } = true;

    // v0.3 S6-T4: where encrypted attachment blobs are stored. When null/
    // empty the Application/AttachmentService uses
    // %APPDATA%/OmniKeyVault/attachments/ as the default.
    public static string? AttachmentDirectory { get; set; }

    // ---- WebDAV remote sync ----
    public static string? WebDavServerUrl { get; set; }
    public static string? WebDavUsername { get; set; }
    public static string? WebDavPassword { get; set; }
    public static string? WebDavRemoteFilePath { get; set; } = "vault.okv";
    public static bool WebDavEnabled { get; set; }
    public static bool WebDavAutoSync { get; set; }

    // ---- Phase 11: JSON file persistence ----

    private static readonly string SettingsPath = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "OmniKeyVault", "settings.json");

    private static bool _loaded;

    /// <summary>Loads settings from %APPDATA%/OmniKeyVault/settings.json.
    /// Called once on startup. If the file doesn't exist, defaults are kept.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            var json = System.IO.File.ReadAllText(SettingsPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("autoLockMinutes", out var alm)) AutoLockMinutes = alm.GetInt32();
            if (root.TryGetProperty("clipboardClearSeconds", out var ccs)) ClipboardClearSeconds = ccs.GetInt32();
            if (root.TryGetProperty("language", out var lang)) Language = lang.GetString()!;
            if (root.TryGetProperty("theme", out var th) && Enum.TryParse<AppTheme>(th.GetString(), true, out var t)) Theme = t;
            if (root.TryGetProperty("syncDirectory", out var sd)) SyncDirectory = sd.GetString();
            if (root.TryGetProperty("watcherEnabled", out var we)) WatcherEnabled = we.GetBoolean();
            if (root.TryGetProperty("lockOnSessionLock", out var lsl)) LockOnSessionLock = lsl.GetBoolean();
            if (root.TryGetProperty("lockOnSuspend", out var ls)) LockOnSuspend = ls.GetBoolean();
            if (root.TryGetProperty("attachmentDirectory", out var ad)) AttachmentDirectory = ad.GetString();
            if (root.TryGetProperty("webDavServerUrl", out var wsu)) WebDavServerUrl = wsu.GetString();
            if (root.TryGetProperty("webDavUsername", out var wun)) WebDavUsername = wun.GetString();
            if (root.TryGetProperty("webDavPassword", out var wpw)) WebDavPassword = wpw.GetString();
            if (root.TryGetProperty("webDavRemoteFilePath", out var wrp)) WebDavRemoteFilePath = wrp.GetString();
            if (root.TryGetProperty("webDavEnabled", out var wen)) WebDavEnabled = wen.GetBoolean();
            if (root.TryGetProperty("webDavAutoSync", out var was)) WebDavAutoSync = was.GetBoolean();
        }
        catch { /* best-effort: keep defaults on parse error */ }
    }

    /// <summary>Persists current settings to %APPDATA%/OmniKeyVault/settings.json.</summary>
    public static void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            var obj = new
            {
                autoLockMinutes = AutoLockMinutes,
                clipboardClearSeconds = ClipboardClearSeconds,
                language = Language,
                theme = Theme.ToString(),
                syncDirectory = SyncDirectory,
                watcherEnabled = WatcherEnabled,
                lockOnSessionLock = LockOnSessionLock,
                lockOnSuspend = LockOnSuspend,
                attachmentDirectory = AttachmentDirectory,
                webDavServerUrl = WebDavServerUrl,
                webDavUsername = WebDavUsername,
                webDavPassword = WebDavPassword,
                webDavRemoteFilePath = WebDavRemoteFilePath,
                webDavEnabled = WebDavEnabled,
                webDavAutoSync = WebDavAutoSync,
            };
            var json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(SettingsPath, json);
        }
        catch { /* best-effort: don't crash on permission errors */ }
    }
}
