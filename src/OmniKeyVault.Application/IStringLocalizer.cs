namespace OmniKeyVault.Application;

/// <summary>
/// Locale tag per BCP-47. v0.3 ships <c>zh-CN</c> (default) and <c>en-US</c>;
/// additional locales can be added by registering a new <see cref="IStringLocalizer"/>
/// implementation and updating <see cref="LocaleRegistry"/>.
/// </summary>
public static class Locales
{
    public const string ZhCn = "zh-CN";
    public const string EnUs = "en-US";
    public const string Default = ZhCn;

    public static bool IsValid(string tag) =>
        tag == ZhCn || tag == EnUs;
}

/// <summary>
/// Minimal i18n abstraction. <see cref="Get"/> returns the localized string for
/// the key, or the key itself if missing. <see cref="Format"/> applies
/// <see cref="string.Format(string,object[])"/> with invariant culture so
/// positional placeholders behave the same in zh-CN / en-US.
/// </summary>
public interface IStringLocalizer
{
    /// <summary>Tag identifying the locale (e.g. <c>"zh-CN"</c>).</summary>
    string Locale { get; }

    /// <summary>Returns the localized string for <paramref name="key"/>; falls
    /// back to the key if the translation is missing.</summary>
    string Get(string key);

    /// <summary>Returns <c>string.Format(localizer.Get(key), args)</c> using
    /// invariant culture. Used by <c>UIStrings.Fmt</c>.</summary>
    string Format(string key, params object[] args);
}

/// <summary>In-process localizer registry. Defaults to zh-CN; switch via
/// <see cref="SetActive"/> (called from <see cref="SettingsStore.Language"/>
/// change). GUI re-pulls strings on each call so the change is visible after
/// <see cref="MainWindow.RebuildLocalizedText"/> (or window reopen).</summary>
public static class LocaleRegistry
{
    private static IStringLocalizer _active = new ZhCnLocalizer();

    /// <summary>Currently active localizer. Never null.</summary>
    public static IStringLocalizer Active => _active;

    /// <summary>All registered localizers, ordered as registered (default first).</summary>
    public static IReadOnlyList<IStringLocalizer> All { get; } = new IStringLocalizer[]
    {
        new ZhCnLocalizer(),
        new EnUsLocalizer(),
    };

    /// <summary>Switch the active localizer by tag. Unknown tags are ignored
    /// (active localizer is unchanged).</summary>
    public static void SetActive(string tag)
    {
        foreach (var l in All)
        {
            if (string.Equals(l.Locale, tag, StringComparison.OrdinalIgnoreCase))
            {
                _active = l;
                return;
            }
        }
    }

    /// <summary>Resolve a localizer by tag; returns null if not registered.</summary>
    public static IStringLocalizer? Find(string tag)
    {
        foreach (var l in All)
            if (string.Equals(l.Locale, tag, StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }
}
