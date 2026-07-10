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
/// Entry history viewer (v0.2 S3-T5). Shows all snapshots for a single
/// entry; user can preview a snapshot's fields, then restore to that
/// version (creates a new current version with the snapshot's data).
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly CliContainer _container;
    private readonly string _profile;
    private readonly Entry _entry;
    private IReadOnlyList<EntrySnapshot> _snapshots = Array.Empty<EntrySnapshot>();
    private EntrySnapshot? _selected;

    public HistoryWindow(CliContainer container, string profile, Entry entry)
    {
        InitializeComponent();
        _container = container;
        _profile = profile;
        _entry = entry;
        HistoryTitle.Text = $"「{entry.Name}」的修改历史";
        LoadSnapshots();
    }

    private void LoadSnapshots()
    {
        try
        {
            _snapshots = _container.Backup.ListHistory(_profile, _entry.Id);
        }
        catch (Exception ex)
        {
            StatusText.Text = "加载历史失败:" + ex.Message;
            StatusText.IsVisible = true;
            _snapshots = Array.Empty<EntrySnapshot>();
        }
        SnapshotList.Children.Clear();
        if (_snapshots.Count == 0)
        {
            EmptyText.IsVisible = true;
            SnapshotList.IsVisible = false;
            return;
        }
        EmptyText.IsVisible = false;
        SnapshotList.IsVisible = true;
        foreach (var snap in _snapshots.OrderByDescending(s => s.Version))
        {
            var btn = new Button { Classes = { "profile-row" }, Tag = snap };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Res.Brush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(grid.Children[0], 0);
            var meta = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 0, 0) };
            meta.Children.Add(new TextBlock { Text = $"v{snap.Version}", FontFamily = Res.Font("FontMono"), FontSize = 12, Foreground = Res.Brush("FgBrush") });
            meta.Children.Add(new TextBlock
            {
                Text = $"{snap.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {snap.Reason ?? "—"}",
                FontFamily = Res.Font("FontMono"), FontSize = 10, Foreground = Res.Brush("FgDimBrush"),
            });
            Grid.SetColumn(meta, 1);
            grid.Children.Add(meta);
            grid.Children.Add(new TextBlock { Text = "👁", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(grid.Children[2], 2);
            btn.Content = grid;
            btn.Click += (_, _) => SelectSnapshot(snap);
            SnapshotList.Children.Add(btn);
        }
        // Default-select the most recent (which is the previous version, since the
        // current entry is the latest)
        if (_snapshots.Count > 0) SelectSnapshot(_snapshots.OrderByDescending(s => s.Version).First());
    }

    private void SelectSnapshot(EntrySnapshot snap)
    {
        _selected = snap;
        PreviewTitle.Text = $"v{snap.Version} · {snap.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm}";
        PreviewBody.Children.Clear();
        foreach (var f in snap.Entry.Fields)
        {
            var row = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock
            {
                Text = f.Key,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                LetterSpacing = 1,
                Foreground = Res.Brush("FgDimBrush"),
            });
            row.Children.Add(new TextBlock
            {
                Text = f.Value.Length == 0 ? "(空)" : FieldCodec.Decode(f.Value),
                FontFamily = Res.Font("FontMono"),
                FontSize = 12,
                Foreground = Res.Brush("FgMutedBrush"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            PreviewBody.Children.Add(row);
        }
        RestoreButton.IsEnabled = true;
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        // Confirm
        if (!await Confirm($"还原 v{_selected.Version} → 覆盖当前条目(创建新版本,旧版本可在历史中恢复)?"))
            return;
        try
        {
            _container.Backup.Restore(_profile, _entry.Id, _selected.Version);
            await _container.Vault.SaveAsync();
            StatusText.Text = $"✓ 已还原到 v{_selected.Version}";
            StatusText.Foreground = Res.Brush("SuccessBrush");
            StatusText.IsVisible = true;
            LoadSnapshots();  // refresh
        }
        catch (Exception ex)
        {
            StatusText.Text = "还原失败:" + ex.Message;
            StatusText.Foreground = Res.Brush("DangerBrush");
            StatusText.IsVisible = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async System.Threading.Tasks.Task<bool> Confirm(string msg)
    {
        var dlg = new Window
        {
            Title = "确认还原",
            Width = 380, Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = msg,
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var yes = new Button { Content = "还原", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        var no = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        var ok = false;
        yes.Click += (_, _) => { ok = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();
        row.Children.Add(no);
        row.Children.Add(yes);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog<bool>(this);
        return ok;
    }
}