using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniKeyVault.Application;

/// <summary>
/// v1.9.1: Manages Windows startup registration for OmniKey Vault.
/// Uses the Windows Registry (HKCU\Software\Microsoft\Windows\CurrentVersion\Run)
/// to add/remove the application from auto-start.
///
/// When auto-started, the app receives a --minimized argument and should
/// start with only a tray icon visible (no main window).
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "OmniKeyVault";

    /// <summary>The command-line argument that indicates the app was auto-started.</summary>
    public const string MinimizedArg = "--minimized";

    /// <summary>Returns true if the app was launched with --minimized (auto-start).</summary>
    public static bool IsMinimizedStart =>
        Environment.GetCommandLineArgs().Contains(MinimizedArg, StringComparer.OrdinalIgnoreCase);

    /// <summary>Checks if auto-start is currently enabled in the registry.</summary>
    public static bool IsAutoStartEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Enables auto-start by writing to the registry.
    /// The app will be launched with --minimized on Windows startup.</summary>
    public static bool EnableAutoStart()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(AppName, $"\"{exePath}\" {MinimizedArg}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Disables auto-start by removing the registry entry.</summary>
    public static bool DisableAutoStart()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
