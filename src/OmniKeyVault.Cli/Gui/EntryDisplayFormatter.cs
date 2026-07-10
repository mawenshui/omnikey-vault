using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Shared entry/field display formatting utilities (Phase 8 MVVM extraction).
/// Eliminates duplication between <see cref="MainWindow"/>, <see cref="EditorWindow"/>,
/// and <see cref="CommandHandlers"/>.
/// </summary>
public static class EntryDisplayFormatter
{
    /// <summary>Returns the display value for a field, applying masking when needed.</summary>
    public static string GetDisplayValue(Field field, bool reveal)
        => reveal ? field.ValueString : (field.Sensitive ? field.DisplayMask() : field.ValueString);

    /// <summary>Formats a timestamp for display in the detail panel.</summary>
    public static string FormatTimestamp(DateTimeOffset dt)
        => dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
