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
                var demoFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_DEV");
                var recFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_RECOVERY");
                var setFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_SETTINGS");
                var creFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_CREATE");
                var creFullFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_CREATEFULL");
                var unlFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_UNLOCK");
                var edFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_EDITOR");
                var srchFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_SEARCH");
                var histFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_HISTORY");
                var profFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_PROFILE");
                var syncFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_SYNC_CONFLICT");
                var devTrustFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_DEVICE_TRUST");
                var seedExpFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_SEED_EXPORT");
                var seedImpFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_SEED_IMPORT");
                var kpFlag = System.Environment.GetEnvironmentVariable("OKV_GUI_DEMO_KEEPASS_IMPORT");
                Log($"env flags: DEV={demoFlag} REC={recFlag} SET={setFlag} CRE={creFlag} CREF={creFullFlag} UNL={unlFlag} ED={edFlag} SRCH={srchFlag} HIST={histFlag} PROF={profFlag} SYNC={syncFlag} TRUST={devTrustFlag} SEXP={seedExpFlag} SIMP={seedImpFlag} KP={kpFlag}");
                if (demoFlag == "1")
                {
                    Log("routing → ShowDemoDevAsync");
                    _ = _shell.ShowDemoDevAsync();
                }
                else if (recFlag == "1")
                {
                    Log("routing → ShowRecoveryDemo");
                    _shell.ShowRecoveryDemo();
                }
                else if (setFlag == "1")
                {
                    Log("routing → ShowSettingsDemoAsync");
                    _ = _shell.ShowSettingsDemoAsync();
                }
                else if (creFlag == "1")
                {
                    Log("routing → ShowCreateDemo");
                    _shell.ShowCreateDemo();
                }
                else if (creFullFlag == "1")
                {
                    Log("routing → ShowCreateFullDemoAsync");
                    _ = _shell.ShowCreateFullDemoAsync();
                }
                else if (unlFlag == "1")
                {
                    Log("routing → ShowUnlockDemoAsync");
                    _ = _shell.ShowUnlockDemoAsync();
                }
                else if (edFlag == "1")
                {
                    Log("routing → ShowEditorDemoAsync");
                    _ = _shell.ShowEditorDemoAsync();
                }
                else if (srchFlag == "1")
                {
                    Log("routing → ShowSearchDemoAsync");
                    _ = _shell.ShowSearchDemoAsync();
                }
                else if (histFlag == "1")
                {
                    Log("routing → ShowHistoryDemoAsync");
                    _ = _shell.ShowHistoryDemoAsync();
                }
                else if (profFlag == "1")
                {
                    Log("routing → ShowProfileSwitcherDemoAsync");
                    _ = _shell.ShowProfileSwitcherDemoAsync();
                }
                else if (syncFlag == "1")
                {
                    Log("routing → ShowSyncConflictDemoAsync");
                    _ = _shell.ShowSyncConflictDemoAsync();
                }
                else if (devTrustFlag == "1")
                {
                    Log("routing → ShowDeviceTrustDemo");
                    _shell.ShowDeviceTrustDemo();
                }
                else if (seedExpFlag == "1")
                {
                    Log("routing → ShowSeedExportDemoAsync");
                    _ = _shell.ShowSeedExportDemoAsync();
                }
                else if (seedImpFlag == "1")
                {
                    Log("routing → ShowSeedImportDemoAsync");
                    _ = _shell.ShowSeedImportDemoAsync();
                }
                else if (kpFlag == "1")
                {
                    Log("routing → ShowKeePassImportDemoAsync");
                    _ = _shell.ShowKeePassImportDemoAsync();
                }
                else
                {
                    Log("routing → ShowUnlock");
                    _shell.ShowUnlock();
                    Log("ShowUnlock returned");
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
