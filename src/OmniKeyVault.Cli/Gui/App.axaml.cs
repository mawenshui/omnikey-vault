using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

// Disambiguate: this namespace lives under `OmniKeyVault.Cli.Gui` which
// resolves the bare name `Application` to the sibling namespace
// `OmniKeyVault.Application` (the layer that owns VaultService / LockService).
// We always mean the Avalonia type here.
using AvaloniaApplication = Avalonia.Application;
using OmniKeyVault.Cli.Gui.Views;

namespace OmniKeyVault.Cli.Gui;

public partial class App : AvaloniaApplication
{
    private GuiShell? _shell;

    /// <summary>Path to the startup diagnostic log. v0.1 WinExe has no
    /// attached console, so any unhandled startup exception would otherwise
    /// disappear silently — making double-click debugging impossible. All
    /// startup steps write here; users can attach the file to bug reports.</summary>
    internal static readonly string StartupLogPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "okv-startup.log");

    internal static void Log(string msg)
    {
        try
        {
            // §1.2: Redact sensitive values before writing to log file
            var safeMsg = OmniKeyVault.Application.LogRedactor.Redact(msg);
            System.IO.File.AppendAllText(StartupLogPath,
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {safeMsg}\n");
        }
        catch { /* log failure is non-fatal */ }
    }

    public override void Initialize()
    {
        try { System.IO.File.Delete(StartupLogPath); } catch { }
        Log($"App.Initialize START (PID={System.Environment.ProcessId}, CWD={System.Environment.CurrentDirectory})");
        // Phase 11: load persisted user settings from %APPDATA%/OmniKeyVault/settings.json
        SettingsStore.Load();
        Log("SettingsStore.Load OK");
        // Apply saved theme preference (v1.1: light/dark/system)
        try
        {
            RequestedThemeVariant = SettingsStore.Theme switch
            {
                SettingsStore.AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
                SettingsStore.AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
            Log($"Theme applied: {SettingsStore.Theme}");
        }
        catch (Exception ex) { Log("Theme apply failed: " + ex.Message); }
        try
        {
            AvaloniaXamlLoader.Load(this);
            Log("AvaloniaXamlLoader.Load OK");
        }
        catch (Exception ex)
        {
            Log("AvaloniaXamlLoader.Load THREW: " + ex);
            throw;
        }
        // Global safety net — keep the process alive when a UI handler throws,
        // instead of letting Avalonia silently tear down the window. Logs the
        // exception to Debug output. v0.2 will surface this in a toast.
        Dispatcher.UIThread.UnhandledException += (s, e) =>
        {
            Log("[UI UnhandledException] " + e.Exception);
            OnUiThreadException(s, e);
        };
        // Catch non-UI thread exceptions too (e.g., background timers).
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log("[AppDomain.UnhandledException] " + e.ExceptionObject);
        };
        Log("App.Initialize END");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log("OnFrameworkInitializationCompleted START");
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Log("ApplicationLifetime = IClassicDesktopStyleApplicationLifetime");
                _shell = new GuiShell();
                Log("GuiShell constructed");

                // v1.4: Refactored 16 if-else demo routes into a dictionary.
                // Each entry maps an env var name to a demo launcher action.
                // The first env var set to "1" wins; if none match, ShowUnlock runs.
                var demoRoutes = new (string envVar, string label, Action<GuiShell> launch)[]
                {
                    ("OKV_GUI_DEMO_DEV",            "ShowDemoDevAsync",           s => _ = s.ShowDemoDevAsync()),
                    ("OKV_GUI_DEMO_RECOVERY",       "ShowRecoveryDemo",           s => s.ShowRecoveryDemo()),
                    ("OKV_GUI_DEMO_SETTINGS",       "ShowSettingsDemoAsync",      s => _ = s.ShowSettingsDemoAsync()),
                    ("OKV_GUI_DEMO_CREATE",         "ShowCreateDemo",             s => s.ShowCreateDemo()),
                    ("OKV_GUI_DEMO_CREATEFULL",     "ShowCreateFullDemoAsync",    s => _ = s.ShowCreateFullDemoAsync()),
                    ("OKV_GUI_DEMO_UNLOCK",         "ShowUnlockDemoAsync",        s => _ = s.ShowUnlockDemoAsync()),
                    ("OKV_GUI_DEMO_EDITOR",         "ShowEditorDemoAsync",        s => _ = s.ShowEditorDemoAsync()),
                    ("OKV_GUI_DEMO_SEARCH",         "ShowSearchDemoAsync",        s => _ = s.ShowSearchDemoAsync()),
                    ("OKV_GUI_DEMO_HISTORY",        "ShowHistoryDemoAsync",       s => _ = s.ShowHistoryDemoAsync()),
                    ("OKV_GUI_DEMO_PROFILE",        "ShowProfileSwitcherDemoAsync", s => _ = s.ShowProfileSwitcherDemoAsync()),
                    ("OKV_GUI_DEMO_SYNC_CONFLICT",  "ShowSyncConflictDemoAsync",  s => _ = s.ShowSyncConflictDemoAsync()),
                    ("OKV_GUI_DEMO_DEVICE_TRUST",   "ShowDeviceTrustDemo",        s => s.ShowDeviceTrustDemo()),
                    ("OKV_GUI_DEMO_SEED_EXPORT",    "ShowSeedExportDemoAsync",    s => _ = s.ShowSeedExportDemoAsync()),
                    ("OKV_GUI_DEMO_SEED_IMPORT",    "ShowSeedImportDemoAsync",    s => _ = s.ShowSeedImportDemoAsync()),
                    ("OKV_GUI_DEMO_KEEPASS_IMPORT", "ShowKeePassImportDemoAsync", s => _ = s.ShowKeePassImportDemoAsync()),
                };

                var activeFlags = string.Join(", ",
                    demoRoutes.Where(r => System.Environment.GetEnvironmentVariable(r.envVar) == "1")
                              .Select(r => r.envVar));
                Log($"env flags active: {activeFlags}{(string.IsNullOrEmpty(activeFlags) ? "(none)" : "")}");

                bool routed = false;
                foreach (var route in demoRoutes)
                {
                    if (System.Environment.GetEnvironmentVariable(route.envVar) == "1")
                    {
                        Log($"routing → {route.label}");
                        route.launch(_shell);
                        routed = true;
                        break;
                    }
                }

                if (!routed)
                {
                    // v1.9.1: If launched with --minimized (auto-start), don't show
                    // the unlock window — just the tray icon. Clicking the tray
                    // icon will show the main window.
                    if (OmniKeyVault.Application.AutoStartService.IsMinimizedStart)
                    {
                        Log("routing → ShowMinimized (tray only)");
                        _shell.ShowMinimizedToTray();
                    }
                    else
                    {
                        Log("routing → ShowUnlock");
                        _shell.ShowUnlock();
                        Log("ShowUnlock returned");
                    }
                }
            }
            else
            {
                Log("ApplicationLifetime is NOT IClassicDesktopStyleApplicationLifetime — " +
                    "the desktop lifetime didn't initialize (this is the silent-exit cause)");
            }
        }
        catch (Exception ex)
        {
            // P7-T7: Surface startup errors to the user instead of silently swallowing.
            Log("OnFrameworkInitializationCompleted THREW: " + ex);
            try
            {
                // Show a simple error dialog so the user knows what went wrong
                var errorWindow = new Window
                {
                    Title = "OmniKey Vault — 启动失败",
                    Width = 500, Height = 300,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Brushes.White,
                };
                var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
                panel.Children.Add(new TextBlock
                {
                    Text = "⚠ OmniKey Vault 启动失败",
                    FontSize = 18, FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.DarkRed,
                });
                panel.Children.Add(new TextBlock
                {
                    Text = ex.ToString(),
                    FontSize = 11, FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Foreground = Brushes.Black,
                    TextWrapping = TextWrapping.Wrap,
                });
                panel.Children.Add(new TextBlock
                {
                    Text = $"诊断日志已写入: {StartupLogPath}",
                    FontSize = 11, Foreground = Brushes.Gray,
                });
                var closeBtn = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 8) };
                closeBtn.Click += (_, _) => errorWindow.Close();
                panel.Children.Add(closeBtn);
                errorWindow.Content = panel;
                errorWindow.Show();
            }
            catch { /* if even the error dialog fails, the log file is the fallback */ }
        }
        Log("OnFrameworkInitializationCompleted END");
        base.OnFrameworkInitializationCompleted();
    }

    private void OnUiThreadException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Mark as handled so Avalonia doesn't tear down the window. v0.2 will
        // surface this in a "Something went wrong" toast in the active window.
            System.Diagnostics.Debug.WriteLine($"[UI UnhandledException] {e.Exception}");
            // P7-T7: log the exception instead of silently swallowing it.
            Log("[UI UnhandledException] " + e.Exception);
        e.Handled = true;
    }
}
