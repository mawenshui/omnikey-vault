using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// v1.8: Audit log viewer dialog. Shows all recorded audit entries in a
/// scrollable list with color-coded action types. Supports CSV export
/// and clearing the log.
/// </summary>
public partial class AuditLogWindow : Window
{
    private readonly AuditLogService _auditLog;

    public AuditLogWindow(AuditLogService auditLog)
    {
        InitializeComponent();
        _auditLog = auditLog;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var entries = await _auditLog.ReadAllAsync();
        LogList.Children.Clear();

        if (entries.Count == 0)
        {
            LogList.Children.Add(new TextBlock
            {
                Text = "暂无审计记录",
                FontSize = 14,
                Foreground = Res.Brush("FgMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
            SummaryText.Text = "0 条记录";
            return;
        }

        // Show most recent first
        foreach (var entry in entries.OrderByDescending(e => e.Timestamp))
        {
            var row = CreateEntryRow(entry);
            LogList.Children.Add(row);
        }

        SummaryText.Text = $"{entries.Count} 条记录 · 最近: {entries.Max(e => e.Timestamp).LocalDateTime:yyyy-MM-dd HH:mm:ss}";
    }

    private static Border CreateEntryRow(AuditEntry entry)
    {
        var (icon, color) = entry.Action switch
        {
            AuditAction.Unlock => ("🔓", "SuccessBrush"),
            AuditAction.Lock => ("🔒", "FgDimBrush"),
            AuditAction.CreateEntry => ("➕", "AccentBrush"),
            AuditAction.EditEntry => ("✏️", "WarningBrush"),
            AuditAction.DeleteEntry => ("🗑", "DangerBrush"),
            AuditAction.Rotate => ("🔄", "InfoBrush"),
            AuditAction.ChangePassword => ("🔑", "WarningBrush"),
            AuditAction.Sync => ("☁️", "AccentBrush"),
            AuditAction.Import => ("📥", "InfoBrush"),
            AuditAction.Export => ("📤", "InfoBrush"),
            _ => ("📋", "FgDimBrush"),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 4),
        };

        panel.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var infoStack = new StackPanel { Spacing = 1 };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"{entry.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {entry.Action}",
            FontSize = 12,
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Foreground = Res.Brush(color),
        });

        var detailParts = new List<string>();
        if (!string.IsNullOrEmpty(entry.EntryName)) detailParts.Add(entry.EntryName);
        if (!string.IsNullOrEmpty(entry.ProfileName)) detailParts.Add($"[{entry.ProfileName}]");
        if (!string.IsNullOrEmpty(entry.Detail)) detailParts.Add(entry.Detail);
        if (detailParts.Count > 0)
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", detailParts),
                FontSize = 11,
                Foreground = Res.Brush("FgMutedBrush"),
            });
        }

        panel.Children.Add(infoStack);

        return new Border
        {
            Background = Res.Brush("BgHoverBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Child = panel,
        };
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await _auditLog.FlushAsync();
        await LoadAsync();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "导出审计日志",
                    DefaultExtension = "csv",
                    SuggestedFileName = $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("CSV 文件")
                            { Patterns = new[] { "*.csv" } },
                    },
                });
            if (file == null) return;

            var path = file.Path.LocalPath;
            var count = await _auditLog.ExportCsvAsync(path);
            await LoadAsync();
            // Simple confirmation — no toast service in this window
            SummaryText.Text = $"已导出 {count} 条记录到 {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "导出失败: " + ex.Message;
        }
    }

    private async void OnClearClick(object? sender, RoutedEventArgs e)
    {
        // Simple confirm dialog
        var confirm = new Window
        {
            Title = "确认清空审计日志",
            Width = 360, Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = "确定要清空所有审计日志吗?此操作不可撤销。",
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        cancelBtn.Click += (_, _) => confirm.Close();
        var okBtn = new Button { Content = "清空", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        okBtn.Click += async (_, _) =>
        {
            _auditLog.Clear();
            confirm.Close();
            await LoadAsync();
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        sp.Children.Add(btnRow);
        confirm.Content = sp;
        await confirm.ShowDialog(this);
    }
}
