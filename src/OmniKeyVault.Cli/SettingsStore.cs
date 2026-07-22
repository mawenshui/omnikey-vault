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

    // ---- v1.9: Global hotkey ----
    public static bool HotkeyEnabled { get; set; } = true;
    public static string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public static string HotkeyKey { get; set; } = "V";
    public static string HotkeyWakeMethod { get; set; } = "double_click";

    // ---- v1.9: Browser extension API ----
    public static bool BrowserApiEnabled { get; set; }
    public static int BrowserApiPort { get; set; } = 14725;

    // ---- v1.9: Multi-vault ----
    /// <summary>List of recently used vault paths, persisted to settings.json.
    /// Most-recently-used is at index 0.</summary>
    public static List<string> RecentVaults { get; set; } = new();

    // ---- v1.9.1: Auto-start + tray ----
    public static bool AutoStartEnabled { get; set; }
    public static bool AutoCheckUpdateOnStartup { get; set; } = true;
    public static bool MinimizeToTrayOnClose { get; set; } = false;

    // ---- v2.0: New features ----
    /// <summary>v2.0: Auto-sync after entry changes (push to WebDAV/S3).</summary>
    public static bool AutoSyncOnChange { get; set; }
    /// <summary>v2.0: System notifications for expiring entries.</summary>
    public static bool SystemNotificationsEnabled { get; set; } = true;
    /// <summary>v2.0: Auto-archive expired entries after N days.</summary>
    public static int AutoArchiveDays { get; set; } = 30;
    /// <summary>v2.0: Window position X.</summary>
    public static double? WindowX { get; set; }
    /// <summary>v2.0: Window position Y.</summary>
    public static double? WindowY { get; set; }
    /// <summary>v2.0: Window width.</summary>
    public static double? WindowWidth { get; set; }
    /// <summary>v2.0: Window height.</summary>
    public static double? WindowHeight { get; set; }
    /// <summary>v2.0: Recently accessed entry IDs (MRU at index 0).</summary>
    public static List<string> RecentEntries { get; set; } = new();
    /// <summary>v2.0: Favorite entry IDs.</summary>
    public static HashSet<string> FavoriteEntries { get; set; } = new();
    /// <summary>v2.0: S3 sync configuration.</summary>
    public static string? S3Endpoint { get; set; }
    public static string? S3Bucket { get; set; }
    public static string? S3AccessKey { get; set; }
    public static string? S3SecretKey { get; set; }
    public static string? S3Region { get; set; } = "us-east-1";
    public static bool S3Enabled { get; set; }
    /// <summary>v2.0: Selective sync — which profiles participate in sync.</summary>
    public static HashSet<string> SyncExcludedProfiles { get; set; } = new();
    /// <summary>v2.0: WebAuthn/FIDO2 enabled.</summary>
    public static bool WebAuthnEnabled { get; set; }

    // ---- v2.3: UX optimization settings ----
    /// <summary>v2.3: Sidebar width (px), user-adjustable.</summary>
    public static double SidebarWidth { get; set; } = 220;
    /// <summary>v2.3: Detail panel width (px), user-adjustable.</summary>
    public static double DetailPanelWidth { get; set; } = 380;
    /// <summary>v2.3: Search history (max 10 items, MRU at index 0).</summary>
    public static List<string> SearchHistory { get; set; } = new();
    /// <summary>v2.3: Font size scale: "small", "medium", "large".</summary>
    public static string FontSizeScale { get; set; } = "medium";
    /// <summary>v2.3: Entry list row density: "compact", "standard", "comfortable".</summary>
    public static string ListDensity { get; set; } = "standard";
    /// <summary>v2.3: High contrast mode.</summary>
    public static bool HighContrastMode { get; set; }
    /// <summary>v2.3: Whether the sidebar tool groups are collapsed (JSON dict of group→bool).</summary>
    public static string? CollapsedToolGroups { get; set; }
    /// <summary>v2.3: Whether the detail panel is hidden (collapsed).</summary>
    public static bool DetailPanelHidden { get; set; }
    /// <summary>v2.3: Whether first-use guide has been completed.</summary>
    public static bool FirstUseGuideCompleted { get; set; }
    /// <summary>v2.3: Notification center — list of unread notifications (JSON array).</summary>
    public static List<NotificationItem> Notifications { get; set; } = new();

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
            // v1.9 settings
            if (root.TryGetProperty("hotkeyEnabled", out var he)) HotkeyEnabled = he.GetBoolean();
            if (root.TryGetProperty("hotkeyModifiers", out var hm)) HotkeyModifiers = hm.GetString() ?? "Ctrl+Shift";
            if (root.TryGetProperty("hotkeyKey", out var hk)) HotkeyKey = hk.GetString() ?? "V";
            if (root.TryGetProperty("hotkeyWakeMethod", out var hw)) HotkeyWakeMethod = hw.GetString() ?? "double_click";
            if (root.TryGetProperty("browserApiEnabled", out var bae)) BrowserApiEnabled = bae.GetBoolean();
            if (root.TryGetProperty("browserApiPort", out var bap)) BrowserApiPort = bap.GetInt32();
            if (root.TryGetProperty("recentVaults", out var rv) && rv.ValueKind == System.Text.Json.JsonValueKind.Array)
                RecentVaults = rv.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList();
            // v1.9.1 settings
            if (root.TryGetProperty("autoStartEnabled", out var ase)) AutoStartEnabled = ase.GetBoolean();
            if (root.TryGetProperty("autoCheckUpdateOnStartup", out var acu)) AutoCheckUpdateOnStartup = acu.GetBoolean();
            if (root.TryGetProperty("minimizeToTrayOnClose", out var mtc)) MinimizeToTrayOnClose = mtc.GetBoolean();
            // v2.0 settings
            if (root.TryGetProperty("autoSyncOnChange", out var asc)) AutoSyncOnChange = asc.GetBoolean();
            if (root.TryGetProperty("systemNotificationsEnabled", out var sne)) SystemNotificationsEnabled = sne.GetBoolean();
            if (root.TryGetProperty("autoArchiveDays", out var aad)) AutoArchiveDays = aad.GetInt32();
            if (root.TryGetProperty("windowX", out var wx)) WindowX = wx.GetDouble();
            if (root.TryGetProperty("windowY", out var wy)) WindowY = wy.GetDouble();
            if (root.TryGetProperty("windowWidth", out var ww)) WindowWidth = ww.GetDouble();
            if (root.TryGetProperty("windowHeight", out var wh)) WindowHeight = wh.GetDouble();
            if (root.TryGetProperty("recentEntries", out var re) && re.ValueKind == System.Text.Json.JsonValueKind.Array)
                RecentEntries = re.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (root.TryGetProperty("favoriteEntries", out var fe) && fe.ValueKind == System.Text.Json.JsonValueKind.Array)
                FavoriteEntries = fe.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
            if (root.TryGetProperty("s3Endpoint", out var s3e)) S3Endpoint = s3e.GetString();
            if (root.TryGetProperty("s3Bucket", out var s3b)) S3Bucket = s3b.GetString();
            if (root.TryGetProperty("s3AccessKey", out var s3a)) S3AccessKey = s3a.GetString();
            if (root.TryGetProperty("s3SecretKey", out var s3s)) S3SecretKey = s3s.GetString();
            if (root.TryGetProperty("s3Region", out var s3r)) S3Region = s3r.GetString();
            if (root.TryGetProperty("s3Enabled", out var s3en)) S3Enabled = s3en.GetBoolean();
            if (root.TryGetProperty("syncExcludedProfiles", out var sep) && sep.ValueKind == System.Text.Json.JsonValueKind.Array)
                SyncExcludedProfiles = sep.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
            if (root.TryGetProperty("webAuthnEnabled", out var wa)) WebAuthnEnabled = wa.GetBoolean();
            // v2.3 settings
            if (root.TryGetProperty("sidebarWidth", out var sw)) SidebarWidth = sw.GetDouble();
            if (root.TryGetProperty("detailPanelWidth", out var dpw)) DetailPanelWidth = dpw.GetDouble();
            if (root.TryGetProperty("searchHistory", out var sh) && sh.ValueKind == System.Text.Json.JsonValueKind.Array)
                SearchHistory = sh.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).Take(10).ToList();
            if (root.TryGetProperty("fontSizeScale", out var fss)) FontSizeScale = fss.GetString() ?? "medium";
            if (root.TryGetProperty("listDensity", out var ld)) ListDensity = ld.GetString() ?? "standard";
            if (root.TryGetProperty("highContrastMode", out var hcm)) HighContrastMode = hcm.GetBoolean();
            if (root.TryGetProperty("collapsedToolGroups", out var ctg)) CollapsedToolGroups = ctg.GetString();
            if (root.TryGetProperty("detailPanelHidden", out var dph)) DetailPanelHidden = dph.GetBoolean();
            if (root.TryGetProperty("firstUseGuideCompleted", out var fugc)) FirstUseGuideCompleted = fugc.GetBoolean();
            if (root.TryGetProperty("notifications", out var notifs) && notifs.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                Notifications = notifs.EnumerateArray().Select(e =>
                {
                    var n = new NotificationItem();
                    if (e.TryGetProperty("title", out var t)) n.Title = t.GetString() ?? "";
                    if (e.TryGetProperty("message", out var m)) n.Message = m.GetString() ?? "";
                    if (e.TryGetProperty("level", out var lv) && Enum.TryParse<NotificationLevel>(lv.GetString(), true, out var lvl)) n.Level = lvl;
                    if (e.TryGetProperty("time", out var tm) && DateTimeOffset.TryParse(tm.GetString(), out var dt)) n.Time = dt;
                    return n;
                }).Take(50).ToList();
            }
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
                // v1.9 settings
                hotkeyEnabled = HotkeyEnabled,
                hotkeyModifiers = HotkeyModifiers,
                hotkeyKey = HotkeyKey,
                hotkeyWakeMethod = HotkeyWakeMethod,
                browserApiEnabled = BrowserApiEnabled,
                browserApiPort = BrowserApiPort,
                recentVaults = RecentVaults,
                // v1.9.1 settings
                autoStartEnabled = AutoStartEnabled,
                autoCheckUpdateOnStartup = AutoCheckUpdateOnStartup,
                minimizeToTrayOnClose = MinimizeToTrayOnClose,
                // v2.0 settings
                autoSyncOnChange = AutoSyncOnChange,
                systemNotificationsEnabled = SystemNotificationsEnabled,
                autoArchiveDays = AutoArchiveDays,
                windowX = WindowX,
                windowY = WindowY,
                windowWidth = WindowWidth,
                windowHeight = WindowHeight,
                recentEntries = RecentEntries,
                favoriteEntries = FavoriteEntries.ToList(),
                s3Endpoint = S3Endpoint,
                s3Bucket = S3Bucket,
                s3AccessKey = S3AccessKey,
                s3SecretKey = S3SecretKey,
                s3Region = S3Region,
                s3Enabled = S3Enabled,
                syncExcludedProfiles = SyncExcludedProfiles.ToList(),
                webAuthnEnabled = WebAuthnEnabled,
                // v2.3 settings
                sidebarWidth = SidebarWidth,
                detailPanelWidth = DetailPanelWidth,
                searchHistory = SearchHistory,
                fontSizeScale = FontSizeScale,
                listDensity = ListDensity,
                highContrastMode = HighContrastMode,
                collapsedToolGroups = CollapsedToolGroups,
                detailPanelHidden = DetailPanelHidden,
                firstUseGuideCompleted = FirstUseGuideCompleted,
                notifications = Notifications.Select(n => new { title = n.Title, message = n.Message, level = n.Level.ToString(), time = n.Time.ToString("O") }).ToList(),
            };
            var json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(SettingsPath, json);
        }
        catch { /* best-effort: don't crash on permission errors */ }
    }
}

/// <summary>v2.3: A notification item for the notification center.</summary>
public class NotificationItem
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public NotificationLevel Level { get; set; } = NotificationLevel.Info;
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
}

public enum NotificationLevel { Info, Warning, Error, Success }
