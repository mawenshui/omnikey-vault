using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Import an <c>.okv.dev</c> seed file into a dev/test profile (S3-T4).
/// Production data isolation is enforced: importing into a prod profile is
/// blocked unless the user explicitly types a confirmation phrase. Warnings
/// from the importer are surfaced inline (stripped secrets, version skew,
/// etc.).</summary>
public partial class SeedImportWindow : Window
{
    private readonly CliContainer _container;

    public SeedImportWindow(CliContainer container)
    {
        InitializeComponent();
        _container = container;
        // Populate target profile picker. Default: existing dev/test profiles
        // + an option to create a new one. We do NOT default to "prod" — this
        // is the production-isolation safety in PRD §5.5.1.
        foreach (var name in _container.Vault.ListProfileNames())
        {
            if (name != "prod")
                TargetProfileBox.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }
        // Add "new profile..." marker
        TargetProfileBox.Items.Add(new ComboBoxItem { Content = "+ 新建 profile", Tag = "__new__" });
        TargetProfileBox.SelectedIndex = 0;
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            var dir = System.IO.Path.Combine(docs, "omni-seeds");
            if (System.IO.Directory.Exists(dir))
            {
                var files = System.IO.Directory.GetFiles(dir, "*.okv.dev");
                if (files.Length > 0)
                {
                    SeedPathBox.Text = files.OrderByDescending(System.IO.File.GetLastWriteTime).First();
                    return;
                }
            }
            SeedPathBox.Text = System.IO.Path.Combine(dir, "seed.dev.okv.dev");
        }
        catch (Exception ex)
        {
            ShowError("浏览失败:" + ex.Message);
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        StatusText.IsVisible = false;
        var path = (SeedPathBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            ShowError("请选择有效的 .okv.dev 文件");
            return;
        }
        var targetItem = (ComboBoxItem)TargetProfileBox.SelectedItem!;
        var targetTag = targetItem.Tag?.ToString() ?? "";
        string targetProfile;
        bool isProd;
        if (targetTag == "__new__")
        {
            // Auto-create a new dev profile with a sensible name
            targetProfile = "imported-" + DateTimeOffset.Now.ToString("yyyyMMdd");
            isProd = false;
        }
        else
        {
            targetProfile = targetTag;
            isProd = targetProfile == "prod";
        }

        // Hard-block: prod must type confirmation
        if (isProd)
        {
            var confirm = ConfirmTextBox.Text?.Trim() ?? "";
            if (!confirm.Equals("IMPORT TO PROD", StringComparison.Ordinal))
            {
                ShowError("导入到 prod 必须输入「IMPORT TO PROD」确认(防止误操作污染生产数据)");
                ConfirmBox.IsVisible = true;
                ConfirmTextBox.Focus();
                return;
            }
        }

        try
        {
            // Auto-create if needed
            if (targetTag == "__new__")
            {
                await _container.Profiles.CreateAsync(targetProfile, ProfileColor.Yellow,
                    new ProfileSettings { ParticipateInSync = false, AutoLockOnSwitch = true, IdleLockMinutes = 5 });
            }
            var result = await _container.SeedImport.ImportAsync(path, targetProfile);
            var msg = $"✓ 已导入到 \"{targetProfile}\":{result.EntriesImported} 个条目";
            if (result.Warnings.Count > 0)
                msg += $"\n⚠ 警告 ({result.Warnings.Count}):\n  • " + string.Join("\n  • ", result.Warnings.Take(5));
            StatusText.Text = msg;
            StatusText.Foreground = Res.Brush("SuccessBrush");
            StatusText.IsVisible = true;
        }
        catch (Exception ex)
        {
            ShowError("导入失败:" + ex.Message);
        }
    }

    private void OnTargetProfileChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Show confirmation field only if "prod" is selected
        var isProd = (TargetProfileBox.SelectedItem is ComboBoxItem ci && ci.Tag?.ToString() == "prod");
        ConfirmBox.IsVisible = isProd;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ShowError(string msg)
    {
        StatusText.Text = msg;
        StatusText.Foreground = Res.Brush("DangerBrush");
        StatusText.IsVisible = true;
    }
}