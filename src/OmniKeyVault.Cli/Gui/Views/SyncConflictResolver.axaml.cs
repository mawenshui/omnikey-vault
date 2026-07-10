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
/// Emits <see cref="Resolved"/> with the chosen action. The host (MainWindow)
/// then dispatches the corresponding SyncService call.
/// </summary>
public partial class SyncConflictResolver : Window
{
    public enum Resolution { KeepLocal, TakeRemote, Merge }

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

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}