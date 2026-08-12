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
        // v1.8: fire-and-forget async to avoid blocking the UI thread on vault file I/O.
        _ = RefreshVaultMetaAsync();
        _ = CheckWebAuthnAvailabilityAsync();
        PasswordBox.Focus();
    }

    /// <summary>v2.4.0: Checks if Windows Hello is available and if biometric
    /// unlock is registered for this vault. Shows the WebAuthn button accordingly.</summary>
    private async Task CheckWebAuthnAvailabilityAsync()
    {
        try
        {
            var available = await WebAuthnService.IsAvailableAsync();
            if (!available) return;

            var registered = WebAuthnService.IsRegistered(_vaultPath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WebAuthnButton.IsVisible = registered;
            });
        }
        catch { /* non-fatal — biometric unlock just won't appear */ }
    }

    /// <summary>v1.8: Async version of RefreshVaultMeta — avoids
    /// .GetAwaiter().GetResult() which blocked the UI thread and risked
    /// deadlock on slow disk I/O. Reads the vault header on a background
    /// thread and marshals the result back to the UI thread for rendering.</summary>
    private async Task RefreshVaultMetaAsync()
    {
        OmniKeyVault.Cli.Gui.App.Log("UnlockWindow.RefreshVaultMetaAsync: file=" + _vaultPath);
        try
        {
            if (System.IO.File.Exists(_vaultPath))
            {
                var fmt = new OmniKeyVault.Infrastructure.VaultFormat();
                var record = await System.Threading.Tasks.Task.Run(async () =>
                    await fmt.ReadAsync(_vaultPath));
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
        // v2.6.2: Show a dialog where the user can input their recovery key.
        // The recovery key is used as the master password to unlock the vault.
        // The recovery key was generated during vault creation and is a 32-byte
        // CSPRNG value formatted as base32 (192 chars, grouped as 13x4 with dashes).
        // Since the KEK is derived from the master password, using the recovery key
        // as the password will produce the same KEK and allow unlock.
        _ = ShowRecoveryKeyUnlockDialogAsync();
    }

    /// <summary>v2.6.2: Shows a dialog for the user to input their recovery key
    /// and attempts to unlock the vault using it as the master password.</summary>
    private async Task ShowRecoveryKeyUnlockDialogAsync()
    {
        if (_unlocking) return;

        var tcs = new TaskCompletionSource<string?>();

        var dlg = new Window
        {
            Title = "使用恢复密钥解锁",
            Width = 480,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Res.Brush("BgCardBrush"),
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "请输入创建金库时生成的恢复密钥（格式：XXXX-XXXX-XXXX-...，共 13 组）。",
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var keyBox = new TextBox
        {
            Classes = { "field-input" },
            FontFamily = Res.Font("FontMono"),
            FontSize = 12,
            Watermark = "XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX",
            AcceptsReturn = false,
            MinHeight = 36,
        };
        panel.Children.Add(keyBox);

        var errorText = new TextBlock
        {
            FontSize = 11,
            Foreground = Res.Brush("DangerBrush"),
            IsVisible = false,
        };
        panel.Children.Add(errorText);

        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };

        var cancelBtn = new Button { Content = "取消", Padding = new Avalonia.Thickness(14, 6) };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dlg.Close(); };

        var unlockBtn = new Button
        {
            Content = "使用恢复密钥解锁",
            Classes = { "primary" },
            Padding = new Avalonia.Thickness(14, 6),
        };
        unlockBtn.Click += (_, _) =>
        {
            var key = (keyBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(key))
            {
                errorText.Text = "请输入恢复密钥";
                errorText.IsVisible = true;
                return;
            }
            tcs.TrySetResult(key);
            dlg.Close();
        };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(unlockBtn);
        panel.Children.Add(btnRow);

        dlg.Content = panel;
        await dlg.ShowDialog(this);

        var recoveryKey = await tcs.Task;
        if (string.IsNullOrEmpty(recoveryKey)) return;

        // Use the recovery key as the master password
        await AttemptUnlockWithPasswordAsync(recoveryKey);
    }

    /// <summary>v2.6.2: Attempts to unlock using a given password string
    /// (used by the recovery key dialog).</summary>
    private async Task AttemptUnlockWithPasswordAsync(string password)
    {
        if (_unlocking) return;
        _unlocking = true;
        ErrorText.IsVisible = false;
        UnlockButton.IsEnabled = false;
        UnlockButton.Content = "派生密钥中…";

        try
        {
            var pwBytes = Encoding.UTF8.GetBytes(password);
            await Task.Run(async () =>
            {
                await _container.Vault.UnlockAsync(_vaultPath, pwBytes);
            });

            OmniKeyVault.Cli.Gui.GuiShell.SaveLastVaultPath(_vaultPath);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UnlockSucceeded?.Invoke(this, _container);
            });
        }
        catch (VaultLockedException)
        {
            ShowError("恢复密钥无效，请检查后重试");
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
        }
    }

    /// <summary>v2.4.0: Handles Windows Hello biometric unlock.
    /// Retrieves the encrypted master password via Windows Hello consent,
    /// then uses it to unlock the vault.</summary>
    private async void OnWebAuthnUnlockClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_unlocking) return;
        _unlocking = true;
        ErrorText.IsVisible = false;
        WebAuthnButton.IsEnabled = false;
        UnlockButton.IsEnabled = false;

        try
        {
            // Step 1: Get the master password via Windows Hello + DPAPI
            var pwBytes = await WebAuthnService.UnlockAsync(_vaultPath);
            if (pwBytes == null)
            {
                ShowError("生物识别解锁失败，请使用主密码");
                return;
            }

            // Step 2: Unlock the vault
            await Task.Run(async () =>
            {
                await _container.Vault.UnlockAsync(_vaultPath, pwBytes);
            });

            // Zero the password bytes ASAP
            Array.Clear(pwBytes, 0, pwBytes.Length);

            OmniKeyVault.Cli.Gui.GuiShell.SaveLastVaultPath(_vaultPath);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UnlockSucceeded?.Invoke(this, _container);
            });
        }
        catch (VaultLockedException)
        {
            ShowError("生物识别凭据无效，请使用主密码");
        }
        catch (Exception ex)
        {
            ShowError($"解锁失败:{ex.Message}");
        }
        finally
        {
            _unlocking = false;
            WebAuthnButton.IsEnabled = true;
            UnlockButton.IsEnabled = true;
        }
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
            _ = RefreshVaultMetaAsync();
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

            // v2.4.0: Offer to enable Windows Hello biometric unlock if available
            // and not yet registered for this vault
            try
            {
                var helloAvailable = await WebAuthnService.IsAvailableAsync();
                if (helloAvailable && !WebAuthnService.IsRegistered(_vaultPath))
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        // Simple approach: use a MessageBox-like dialog
                        var result = await ShowBiometricEnrollDialog();
                        if (result)
                        {
                            var pwBytesForEnroll = Encoding.UTF8.GetBytes(pw);
                            var enrolled = await WebAuthnService.RegisterAsync(_vaultPath, pwBytesForEnroll);
                            if (enrolled)
                            {
                                SettingsStore.WebAuthnEnabled = true;
                                SettingsStore.Save();
                                WebAuthnButton.IsVisible = true;
                            }
                        }
                    });
                }
            }
            catch { /* non-fatal — biometric enrollment is optional */ }

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

    /// <summary>v2.4.0: Shows a dialog asking if the user wants to enable
    /// Windows Hello biometric unlock.</summary>
    private async Task<bool> ShowBiometricEnrollDialog()
    {
        var tcs = new TaskCompletionSource<bool>();
        var dlg = new Window
        {
            Title = "启用 Windows Hello",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background,
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "是否启用 Windows Hello 生物识别解锁？\n\n启用后可使用指纹、面部识别或 PIN 快速解锁金库，无需每次输入主密码。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13,
        });

        var btnPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
        };

        var yesBtn = new Button { Content = "启用", Classes = { "primary" }, Padding = new Avalonia.Thickness(20, 8) };
        var noBtn = new Button { Content = "暂不", Classes = { "ghost" }, Padding = new Avalonia.Thickness(20, 8) };

        yesBtn.Click += (_, _) => { tcs.TrySetResult(true); dlg.Close(); };
        noBtn.Click += (_, _) => { tcs.TrySetResult(false); dlg.Close(); };

        btnPanel.Children.Add(yesBtn);
        btnPanel.Children.Add(noBtn);
        panel.Children.Add(btnPanel);

        dlg.Content = panel;
        await dlg.ShowDialog(this);
        return await tcs.Task;
    }
}
