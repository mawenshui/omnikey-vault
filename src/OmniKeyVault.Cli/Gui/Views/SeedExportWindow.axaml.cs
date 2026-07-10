using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Export a profile as an unencrypted <c>.okv.dev</c> seed file (S3-T3).
/// v0.2: dev/test profile is exported to a portable seed; optionally
/// strip-secrets for sharing schemas without leaking credentials. The
/// seed file is NOT protected by the master password (per OKV_FORMAT §5).
/// </summary>
public partial class SeedExportWindow : Window
{
    private readonly CliContainer _container;

    public SeedExportWindow(CliContainer container)
    {
        InitializeComponent();
        _container = container;
        // Populate profile picker
        foreach (var name in _container.Vault.ListProfileNames())
        {
            ProfileBox.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }
        if (ProfileBox.Items.Count > 0) ProfileBox.SelectedIndex = 0;
        StripSecretsBox.IsCheckedChanged += (_, _) => UpdateHint();
        UpdateHint();
    }

    private void UpdateHint()
    {
        if (StripSecretsBox.IsChecked == true)
        {
            HintText.Text = "⚠ Strip 模式:导出的 seed 不含任何 Secret 字段,适合团队共享 schema 与非敏感字段。";
            HintText.Foreground = Res.Brush("WarningBrush");
        }
        else
        {
            HintText.Text = "完整导出:包含 Secret 字段。仅用于受控环境,切勿公开分享。";
            HintText.Foreground = Res.Brush("FgDimBrush");
        }
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            var dir = System.IO.Path.Combine(docs, "omni-seeds");
            System.IO.Directory.CreateDirectory(dir);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var profile = ((ComboBoxItem)ProfileBox.SelectedItem!).Tag!.ToString();
            var suggested = System.IO.Path.Combine(dir, $"seed.{profile}.{stamp}.okv.dev");
            PathBox.Text = suggested;
        }
        catch (Exception ex)
        {
            ShowError("无法生成默认路径:" + ex.Message);
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        StatusText.IsVisible = false;
        var profile = ((ComboBoxItem)ProfileBox.SelectedItem!)?.Tag?.ToString();
        if (string.IsNullOrEmpty(profile))
        {
            ShowError("请选择要导出的 Profile");
            return;
        }
        var path = (PathBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(path))
        {
            ShowError("请选择导出路径");
            return;
        }
        if (!path.EndsWith(".okv.dev", StringComparison.OrdinalIgnoreCase))
            path += ".okv.dev";
        try
        {
            _container.SeedExport.AllowProdProfile = true;
            _container.SeedExport.StripSecrets = StripSecretsBox.IsChecked == true;
            var seed = await _container.SeedExport.ExportAsync(profile, path);
            var entriesCount = seed.Profiles.Sum(p => p.PayloadNonce.Length > 0 ? 1 : 0);  // rough
            // Simpler: just count the produced seed's profile records
            var profileCount = seed.Profiles.Count;
            StatusText.Text = $"✓ 已导出:UUID={seed.SeedUuid},profiles={profileCount}";
            StatusText.Foreground = Res.Brush("SuccessBrush");
            StatusText.IsVisible = true;
            // Open the folder to show the file
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            ShowError("导出失败:" + ex.Message);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ShowError(string msg)
    {
        StatusText.Text = msg;
        StatusText.Foreground = Res.Brush("DangerBrush");
        StatusText.IsVisible = true;
    }
}