using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Unlock screen. Per UI_UX_SPEC §4.2 / docs/UI/index.html (unlock section).
/// Hosts the master password input and the "use recovery key" entry point.
/// On success, fires <see cref="UnlockSucceeded"/> and lets the host close
/// this window + open <see cref="MainWindow"/>.
/// </summary>
public partial class UnlockWindow : Window
{
    private readonly CliContainer _container;
    private string _vaultPath;
    private bool _unlocking;

    /// <summary>Emitted with the unlocked container on success. Host closes this window.</summary>
    public event EventHandler<CliContainer>? UnlockSucceeded;

    public UnlockWindow(CliContainer container, string vaultPath)
    {
        InitializeComponent();
        _container = container;
        _vaultPath = vaultPath;

        // Try to read header info (no password needed) so the card shows the
        // vault UUID + last access. Falls back to placeholder if file missing.
        RefreshVaultMeta();
        PasswordBox.Focus();
    }

    private void RefreshVaultMeta()
    {
        OmniKeyVault.Cli.Gui.App.Log("UnlockWindow.RefreshVaultMeta: file=" + _vaultPath);
        try
        {
            if (System.IO.File.Exists(_vaultPath))
            {
                var fmt = new OmniKeyVault.Infrastructure.VaultFormat();
                // CRITICAL: wrap in Task.Run so the async IO doesn't capture the
                // UI thread's SynchronizationContext. Without this, the .GetResult()
                // below deadlocks because ReadAsync's continuation tries to post
                // back to the UI thread that is currently blocked waiting for it.
                // This was the silent cause of "double-click okv.exe does nothing"
                // when the last-vault.txt pointed to a real (or demo) vault file.
                var record = System.Threading.Tasks.Task.Run(async () =>
                    await fmt.ReadAsync(_vaultPath)).GetAwaiter().GetResult();
                OmniKeyVault.Cli.Gui.App.Log("UnlockWindow.RefreshVaultMeta: read OK, uuid=" + record.VaultUuid);
                VaultIdText.Text = $"{record.VaultUuid} · {record.Profiles.Count} 个 Profile";
                // VaultRecord has no wall-clock LastModified; surface the build hash
                // (8 bytes truncated) as a fingerprint and rely on VectorClock for sync ordering.
                var buildShort = Convert.ToHexString(record.AppBuildHash)[..Math.Min(12, record.AppBuildHash.Length * 2)];
                LastAccessText.Text = $"Build: {buildShort}";
            }
            else
            {
                OmniKeyVault.Cli.Gui.App.Log("UnlockWindow.RefreshVaultMeta: file does not exist");
                VaultIdText.Text = "(no vault found)";
                LastAccessText.Text = "—";
            }
        }
        catch (Exception ex)
        {
            OmniKeyVault.Cli.Gui.App.Log("UnlockWindow.RefreshVaultMeta THREW: " + ex.Message);
            VaultIdText.Text = "(corrupted or unsupported)";
        }
    }

    private void OnRevealToggle(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        PasswordBox.RevealPassword = !PasswordBox.RevealPassword;
        RevealIcon.Text = PasswordBox.RevealPassword ? "🙈" : "👁";
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_unlocking)
        {
            e.Handled = true;
            _ = AttemptUnlockAsync();
        }
    }

    private async void OnUnlockClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_unlocking) return;
        await AttemptUnlockAsync();
    }

    private void OnRecoveryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // v0.1 GUI: open the RecoveryKeyWindow to show the user the 32-block
        // format. Real cryptographic recovery flow lands in v0.2 (the recovery
        // key in the .okv header is currently encrypted with the master-derived
        // KEK, so we cannot unlock without it without additional key-rewrapping work).
        var sample = SampleRecoveryKey();
        var dlg = new RecoveryKeyWindow(sample) { Title = "恢复密钥 · 格式预览" };
        dlg.ShowDialog(this);
    }

    /// <summary>Emitted when the user wants to create a new vault. Host (GuiShell) opens the wizard.</summary>
    public event EventHandler? CreateVaultRequested;

    /// <summary>Manually browse for an existing .okv file. Used when the
    /// default-vault path doesn't exist (fresh install, user moved their vault,
    /// or vault lives on an external drive). Re-points <see cref="_vaultPath"/>
    /// at the picked file and re-reads its header so the meta block updates.</summary>
    private async void OnBrowseVaultClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null)
            {
                ShowError("当前环境不支持文件选择器");
                return;
            }
            // Try to start in the default vault's parent folder so the user
            // doesn't have to navigate from %USERPROFILE% every time.
            Avalonia.Platform.Storage.IStorageFolder? startFolder = null;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_vaultPath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(new System.Uri(dir));
            }
            catch { /* non-fatal — picker falls back to its own default */ }

            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择金库文件 (.okv)",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("OmniKey Vault")
                        { Patterns = new[] { "*.okv" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("所有文件")
                        { Patterns = new[] { "*" } },
                },
            });
            if (files.Count == 0) return;
            var picked = files[0].Path.LocalPath;
            if (!System.IO.File.Exists(picked))
            {
                ShowError($"文件不存在:{picked}");
                return;
            }
            _vaultPath = picked;
            OmniKeyVault.Cli.Gui.GuiShell.SaveLastVaultPath(_vaultPath);
            RefreshVaultMeta();
            PasswordBox.Focus();
        }
        catch (Exception ex)
        {
            ShowError("打开文件选择器失败:" + ex.Message);
        }
    }

    private void OnCreateVaultClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CreateVaultRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string SampleRecoveryKey()
    {
        // Deterministic 192-char key so the grid always looks consistent.
        // Real recovery keys are generated by VaultService.CreateAsync per OKV_FORMAT §3.2.
        const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var seed = "OKV1-RECOVERY-DEMO";
        var sb = new System.Text.StringBuilder(192);
        int x = 0;
        foreach (var c in seed) x = (x * 31 + c) & 0x7fffffff;
        for (int i = 0; i < 192; i++)
        {
            x = (x * 1103515245 + 12345) & 0x7fffffff;
            sb.Append(alpha[x % alpha.Length]);
        }
        return sb.ToString();
    }

    private async Task AttemptUnlockAsync()
    {
        if (_unlocking) return;
        var pw = PasswordBox.Text ?? string.Empty;
        if (pw.Length == 0)
        {
            ShowError("请输入主密码");
            return;
        }

        _unlocking = true;
        ErrorText.IsVisible = false;
        UnlockButton.IsEnabled = false;
        UnlockButton.Content = "派生密钥中…";

        try
        {
            // KDF is slow (Argon2id 256MiB). Run on background thread to keep UI alive.
            var pwBytes = Encoding.UTF8.GetBytes(pw);
            await Task.Run(async () =>
            {
                await _container.Vault.UnlockAsync(_vaultPath, pwBytes);
            });

            // Success — persist the path so the next launch auto-detects this
            // vault (even if the user just browsed to a non-default location).
            OmniKeyVault.Cli.Gui.GuiShell.SaveLastVaultPath(_vaultPath);

            // Success — fire event on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UnlockSucceeded?.Invoke(this, _container);
            });
        }
        catch (VaultLockedException)
        {
            ShowError("凭据错误,请重试");
        }
        catch (System.IO.FileNotFoundException)
        {
            ShowError($"未找到保险库文件:{_vaultPath}");
        }
        catch (Exception ex)
        {
            ShowError($"解锁失败:{ex.Message}");
        }
        finally
        {
            _unlocking = false;
            UnlockButton.IsEnabled = true;
            UnlockButton.Content = "解锁保险库";
            // Zero the password buffer ASAP
            PasswordBox.Text = string.Empty;
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.IsVisible = true;
    }
}
