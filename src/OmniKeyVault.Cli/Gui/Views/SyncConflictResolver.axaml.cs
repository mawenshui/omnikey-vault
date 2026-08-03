using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// v0.2 S4-T5: sync conflict resolution wizard. Shown when SyncService
/// detects concurrent modifications on two devices. Lets the user pick
/// from three strategies:
///   1. Keep local (overwrite remote with local state)
///   2. Take remote (overwrite local with remote state)
///   3. Merge (combine non-conflicting entries; conflicts left to user judgment)
///
/// v2.4.0: Enhanced with batch processing — "全部采用本地", "全部采用远端"
/// options, and conflict details list showing which entries have conflicts.
/// </summary>
public partial class SyncConflictResolver : Window
{
    public enum Resolution { KeepLocal, TakeRemote, Merge, AllLocal, AllRemote }

    public event EventHandler<Resolution>? Resolved;

    private readonly SyncResult _result;

    public SyncConflictResolver(SyncResult result)
    {
        InitializeComponent();
        _result = result;
        Title = "同步冲突解决";
        SummaryText.Text = $"检测到 {_result.ConflictsDetected} 个冲突条目,Vector Clock 无法自动合并。";
        LocalVectorText.Text = FormatClock(_result.LocalManifest?.VectorClock);
        RemoteVectorText.Text = FormatClock(_result.RemoteManifest?.VectorClock);

        // v2.4.0: Show conflict details if available
        if (_result.ConflictsDetected > 0)
        {
            ShowConflictDetails();
        }
    }

    /// <summary>v2.4.0: Show a summary of conflict details in the dialog.</summary>
    private void ShowConflictDetails()
    {
        ConflictDetailsSection.IsVisible = true;

        // Show summary info about the conflicts
        var detail = new Border
        {
            Background = Res.Brush("BgSunkenBrush"),
            BorderBrush = Res.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"冲突数: {_result.ConflictsDetected}",
                        FontSize = 12,
                        Foreground = Res.Brush("FgBrush"),
                    },
                    new TextBlock
                    {
                        Text = $"已合并: {_result.EntriesMerged} 个新/更新条目",
                        FontSize = 11,
                        Foreground = Res.Brush("FgDimBrush"),
                    },
                    new TextBlock
                    {
                        Text = $"结果: {_result.Message}",
                        FontSize = 11,
                        Foreground = Res.Brush("FgDimBrush"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
        ConflictDetailsPanel.Children.Add(detail);
    }

    private static string FormatClock(VectorClock? clock)
    {
        if (clock == null) return "(无)";
        return string.Join(", ", clock.Counters.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private void OnKeepLocal(object? sender, RoutedEventArgs e)
    {
        Resolved?.Invoke(this, Resolution.KeepLocal);
        Close();
    }

    private void OnTakeRemote(object? sender, RoutedEventArgs e)
    {
        Resolved?.Invoke(this, Resolution.TakeRemote);
        Close();
    }

    private void OnMerge(object? sender, RoutedEventArgs e)
    {
        Resolved?.Invoke(this, Resolution.Merge);
        Close();
    }

    /// <summary>v2.4.0: Batch — all local. Same as KeepLocal but explicitly
    /// communicates that all conflicts should use local version.</summary>
    private void OnAllLocal(object? sender, RoutedEventArgs e)
    {
        Resolved?.Invoke(this, Resolution.AllLocal);
        Close();
    }

    /// <summary>v2.4.0: Batch — all remote. Same as TakeRemote but explicitly
    /// communicates that all conflicts should use remote version.</summary>
    private void OnAllRemote(object? sender, RoutedEventArgs e)
    {
        Resolved?.Invoke(this, Resolution.AllRemote);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
