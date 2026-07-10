using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaApplication = Avalonia.Application;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Centralized typed resource lookup. Resources are defined in App.axaml
/// (Application.Resources with ThemeDictionaries) and looked up here to
/// avoid the verbose Application.Current!.Resources[key] as T pattern in
/// every call site. Uses FindResource so theme-aware resources from
/// ThemeDictionaries are resolved correctly.
/// </summary>
internal static class Res
{
    public static IBrush Brush(string key)
    {
        if (AvaloniaApplication.Current?.FindResource(key) is IBrush b)
            return b;
        return Brushes.Transparent;
    }

    public static FontFamily Font(string key)
    {
        if (AvaloniaApplication.Current?.FindResource(key) is FontFamily f)
            return f;
        return FontFamily.Default;
    }

    public static double Double(string key)
    {
        if (AvaloniaApplication.Current?.FindResource(key) is double d)
            return d;
        return 0;
    }

    public static T? Get<T>(string key) where T : class
    {
        return AvaloniaApplication.Current?.FindResource(key) as T;
    }
}
