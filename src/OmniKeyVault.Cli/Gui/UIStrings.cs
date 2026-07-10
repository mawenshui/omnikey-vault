using OmniKeyVault.Application;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Centralized UI string lookup per MANUAL §15. v0.3 ships <c>zh-CN</c> as
/// the default and <c>en-US</c> as a runtime-switchable secondary locale.
///
/// The legacy v0.1 / v0.2 API is preserved verbatim: <c>UIStrings.Get(key)</c>
/// and <c>UIStrings.Fmt(key, args)</c> still work exactly as before. Under
/// the hood they delegate to <see cref="LocaleRegistry.Active"/>, which the
/// Settings → Language change handler updates via <see cref="SetLocale"/>.
/// </summary>
/// <remarks>
/// Per MANUAL §15.2 the following are NOT localized (always English):
/// template ids, field keys, profile names, magic bytes, algorithm names,
/// UUIDs, and the credential values themselves. The strings in this file are
/// only the user-facing chrome (buttons, menus, labels, status messages,
/// error toasts, etc.).
/// </remarks>
public static class UIStrings
{
    /// <summary>Look up a localized string. Falls back to the key itself if
    /// the active localizer does not provide a translation. The string is
    /// resolved on each call so locale changes are reflected on the next
    /// text re-fetch (e.g. window re-render or list refresh).</summary>
    public static string Get(string key) => LocaleRegistry.Active.Get(key);

    /// <summary>Format helper: <c>UIStrings.Fmt("main.count_fmt", 42)</c> → "42 个条目" or "42 entries".</summary>
    public static string Fmt(string key, params object[] args) =>
        LocaleRegistry.Active.Format(key, args);

    /// <summary>Switch the active locale at runtime. Called by
    /// <c>SettingsWindow.OnLanguageChanged</c>; safe to call from any thread
    /// (the registry holds a single reference; the next <see cref="Get"/>
    /// call will see the new locale).</summary>
    /// <returns><c>true</c> if the tag was recognized; <c>false</c> if the
    /// tag is not registered (caller should ignore).</returns>
    public static bool SetLocale(string tag)
    {
        if (!Locales.IsValid(tag)) return false;
        LocaleRegistry.SetActive(tag);
        return true;
    }

    /// <summary>Return the currently active locale tag (e.g. <c>"zh-CN"</c>).</summary>
    public static string CurrentLocale => LocaleRegistry.Active.Locale;
}
