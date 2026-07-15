using System.Runtime.InteropServices;
using System.Text.Json;

namespace OmniKeyVault.Application;

/// <summary>
/// v1.9: Global hotkey + auto-fill service. Registers a system-wide hotkey
/// to bring OmniKey Vault to the foreground, and provides clipboard-based
/// auto-fill into other applications.
///
/// Hotkey configuration supports:
/// - Custom modifier + key combinations (e.g. Ctrl+Shift+V)
/// - Wake methods: single click, double click, long press on tray icon
/// - Conflict detection via Win32 RegisterHotKey API
///
/// Auto-fill strategy:
/// 1. Copy the selected field value to clipboard
/// 2. Simulate Ctrl+V in the target window (user initiates the paste)
/// 3. Clear clipboard after SettingsStore.ClipboardClearSeconds
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class HotkeyService : IDisposable
{
    private HotkeyConfig _config = new();
    private readonly ClipboardService _clipboard;
    private int _hotkeyId = 0x9001;
    private bool _registered;
    private nint _hwnd = nint.Zero;

    public HotkeyConfig Config => _config;
    public bool IsRegistered => _registered;

    /// <summary>Fired when the global hotkey is pressed. Host should show the main window.</summary>
    public event EventHandler? HotkeyPressed;

    public HotkeyService(ClipboardService clipboard)
    {
        _clipboard = clipboard;
    }

    /// <summary>Loads hotkey config from a JSON file at %APPDATA%/OmniKeyVault/hotkey.json.</summary>
    public void LoadConfig()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            _config = JsonSerializer.Deserialize<HotkeyConfig>(json, JsonOpts) ?? new HotkeyConfig();
        }
        catch { /* best-effort */ }
    }

    /// <summary>Saves hotkey config to %APPDATA%/OmniKeyVault/hotkey.json.</summary>
    public void SaveConfig()
    {
        try
        {
            var path = GetConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_config, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Updates the hotkey configuration. Call SaveConfig() to persist.</summary>
    public void UpdateConfig(HotkeyConfig config)
    {
        _config = config;
    }

    /// <summary>Attempts to register the global hotkey with Win32.
    /// Returns true on success, false if the combination is already taken.</summary>
    public bool TryRegister(nint hwnd)
    {
        if (_registered) Unregister();
        _hwnd = hwnd;

        if (!_config.Enabled) return false;
        if (string.IsNullOrEmpty(_config.Key)) return false;

        var mod = ParseModifiers(_config.Modifiers);
        var vk = ParseVirtualKey(_config.Key);
        if (vk == 0) return false;

        _registered = RegisterHotKey(hwnd, _hotkeyId, mod, vk);
        return _registered;
    }

    /// <summary>Unregisters the global hotkey.</summary>
    public void Unregister()
    {
        if (_registered && _hwnd != nint.Zero)
        {
            UnregisterHotKey(_hwnd, _hotkeyId);
            _registered = false;
        }
    }

    /// <summary>Processes a Windows message. Returns true if the message was the hotkey.</summary>
    public bool ProcessMessage(nint hwnd, int msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && wParam == _hotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    /// <summary>Checks if a hotkey combination is available (not already registered by another app).</summary>
    public bool IsCombinationAvailable(string modifiers, string key)
    {
        var mod = ParseModifiers(modifiers);
        var vk = ParseVirtualKey(key);
        if (vk == 0) return false;

        // Try to register on a temporary window, then immediately unregister
        var result = RegisterHotKey(nint.Zero, _hotkeyId + 1, mod, vk);
        if (result) UnregisterHotKey(nint.Zero, _hotkeyId + 1);
        return result;
    }

    /// <summary>Auto-fills a value by copying it to clipboard.
    /// The user then pastes (Ctrl+V) in the target application.
    /// After ClipboardClearSeconds, the clipboard is automatically cleared.</summary>
    public async Task AutoFillAsync(string value)
    {
        _clipboard.CopySensitive(value);
        await Task.CompletedTask;
    }

    // ---- Win32 interop ----

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private static uint ParseModifiers(string? modifiers)
    {
        if (string.IsNullOrEmpty(modifiers)) return 0;
        uint result = 0;
        if (modifiers.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) result |= MOD_CONTROL;
        if (modifiers.Contains("Alt", StringComparison.OrdinalIgnoreCase)) result |= MOD_ALT;
        if (modifiers.Contains("Shift", StringComparison.OrdinalIgnoreCase)) result |= MOD_SHIFT;
        if (modifiers.Contains("Win", StringComparison.OrdinalIgnoreCase)) result |= MOD_WIN;
        return result | MOD_NOREPEAT;
    }

    private static uint ParseVirtualKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        // Single character → ASCII
        if (key.Length == 1)
        {
            var c = char.ToUpper(key[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }
        // Special keys
        return key.ToUpperInvariant() switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "SPACE" => 0x20, "ENTER" => 0x0D, "TAB" => 0x09,
            "INSERT" => 0x2D, "DELETE" => 0x2E,
            "HOME" => 0x24, "END" => 0x23,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            _ => 0
        };
    }

    private static string GetConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "hotkey.json");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Dispose()
    {
        Unregister();
    }
}

/// <summary>v1.9: Hotkey configuration.</summary>
public sealed class HotkeyConfig
{
    /// <summary>Whether the global hotkey is enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Modifier keys, e.g. "Ctrl+Shift".</summary>
    public string Modifiers { get; set; } = "Ctrl+Shift";
    /// <summary>Virtual key, e.g. "V", "F1", "Space".</summary>
    public string Key { get; set; } = "V";
    /// <summary>Wake method: "single_click", "double_click", "long_press".</summary>
    public string WakeMethod { get; set; } = "double_click";
    /// <summary>For long_press: milliseconds to hold.</summary>
    public int LongPressMs { get; set; } = 500;
}
