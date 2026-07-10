using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OmniKeyVault.Application;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// v0.3 S5-T6: GUI entry for KeePass 2.x XML import. User picks a .xml file
/// (File → Export → KeePass 2 XML in KeePass), confirms, and the import runs
/// against the currently active profile. Result toast includes imported /
/// skipped counts; on error the import is rolled back per-entry so a single
/// bad row doesn't poison the whole batch.
/// </summary>
public partial class KeePassImportWindow : Window
{
    private readonly CliContainer _container;
    private readonly string _targetProfile;

    public KeePassImportWindow(CliContainer container, string targetProfile)
    {
        InitializeComponent();
        _container = container;
        _targetProfile = targetProfile;
        TargetProfileText.Text = $"{targetProfile} (当前 Profile)";
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sp = StorageProvider;
            if (sp == null) return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 KeePass 2.x XML 导出文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KeePass XML") { Patterns = new[] { "*.xml" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                },
            });
            if (files != null && files.Count > 0)
            {
                PathBox.Text = files[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"✕ 浏览失败: {ex.Message}", success: false);
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var path = PathBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
        {
            ShowStatus("✕ 请先选择 XML 文件", success: false);
            return;
        }
        if (!System.IO.File.Exists(path))
        {
            ShowStatus($"✕ 文件不存在: {path}", success: false);
            return;
        }
        ImportButton.IsEnabled = false;
        ShowStatus("正在导入…", success: null);
        try
        {
            // Run on a background thread so the UI doesn't freeze on a large
            // KeePass export. The importer parses the entire XML + creates
            // N entries + flushes to disk; for 100+ entries this can take
            // a few hundred ms.
            var result = await System.Threading.Tasks.Task.Run(() =>
                _container.KeePassXml.ImportFromString(_targetProfile,
                    System.IO.File.ReadAllText(path)));

            // Persist to disk
            await _container.Vault.SaveAsync();

            // Build status
            var summary = $"✓ {UIStrings.Fmt("keepass.import_success_fmt", result.EntriesImported, _targetProfile)}";
            if (result.EntriesSkipped > 0 || result.Errors.Count > 0)
                summary += $" (跳过 {result.EntriesSkipped}";
            if (result.Errors.Count > 0)
                summary += $", {result.Errors.Count} 错误";
            if (result.EntriesSkipped > 0 || result.Errors.Count > 0)
                summary += ")";
            ShowStatus(summary, success: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"{UIStrings.Get("keepass.import_failed")} {ex.Message}", success: false);
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string text, bool? success)
    {
        StatusText.Text = text;
        StatusText.Foreground = success switch
        {
            true => Res.Brush("SuccessBrush"),
            false => Res.Brush("DangerBrush"),
            _ => Res.Brush("InfoBrush"),
        };
        StatusText.IsVisible = true;
    }
}
