using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Vault creation wizard per UI_UX_SPEC §4.1. Implements the 4 main steps:
///   1. Welcome
///   2. Master password + location
///   3. Recovery Key (must explicitly save)
///   4. Argon2id KDF (in progress, then opens MainWindow)
///
/// Fires <see cref="VaultCreated"/> with the unlocked container on success.
/// </summary>
public partial class CreateVaultWizard : Window
{
    private readonly CliContainer _container;
    private int _step = 1;
    private string? _generatedRecoveryKey;
    private string _vaultPath = string.Empty;

    /// <summary>Emitted with the unlocked container after a successful create + unlock flow.</summary>
    public event EventHandler<CliContainer>? VaultCreated;

    public CreateVaultWizard(CliContainer container, string defaultVaultPath)
    {
        InitializeComponent();
        _container = container;

        // Split default path into folder + name-without-extension for the new
        // two-field layout. If parsing fails, fall back to the directory of the
        // home + "vault".
        var defaultFolder = System.IO.Path.GetDirectoryName(defaultVaultPath);
        var defaultName = System.IO.Path.GetFileNameWithoutExtension(defaultVaultPath);
        if (string.IsNullOrEmpty(defaultFolder))
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            defaultFolder = System.IO.Path.Combine(home, "OmniKeyVault");
        }
        if (string.IsNullOrEmpty(defaultName)) defaultName = "vault";
        FolderBox.Text = defaultFolder;
        FilenameBox.Text = defaultName;

        FolderBox.TextChanged += (_, _) => UpdateFullPathPreview();
        FilenameBox.TextChanged += (_, _) => { EnsureOkvSuffix(); UpdateFullPathPreview(); };

        PasswordBox.TextChanged += (_, _) => UpdateStrength();
        ConfirmBox.TextChanged += (_, _) => UpdateStrength();
        UpdateFullPathPreview();
        ShowStep(1);
    }

    /// <summary>Toggles NextButton on step 3 based on the "saved" checkbox.</summary>
    internal void OnSavedCheckChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_step == 3) NextButton.IsEnabled = SavedCheck.IsChecked == true;
    }

    internal void ShowStep(int step)
    {
        _step = step;
        Step1Panel.IsVisible = step == 1;
        Step2Panel.IsVisible = step == 2;
        Step3Panel.IsVisible = step == 3;
        Step4Panel.IsVisible = step == 4;

        // Update stepper dots
        Dot1.Background = step >= 1 ? Res.Brush("AccentBrush") : Res.Brush("BorderBrightBrush");
        Dot2.Background = step >= 2 ? Res.Brush("AccentBrush") : Res.Brush("BorderBrightBrush");
        Dot3.Background = step >= 3 ? Res.Brush("AccentBrush") : Res.Brush("BorderBrightBrush");
        Dot4.Background = step >= 4 ? Res.Brush("AccentBrush") : Res.Brush("BorderBrightBrush");
        Line1.Background = step >= 2 ? Res.Brush("AccentDimBrush") : Res.Brush("BorderBrightBrush");
        Line2.Background = step >= 3 ? Res.Brush("AccentDimBrush") : Res.Brush("BorderBrightBrush");
        Line3.Background = step >= 4 ? Res.Brush("AccentDimBrush") : Res.Brush("BorderBrightBrush");

        StepLabel.Text = $"第 {step} 步 / 共 4 步";
        BackButton.IsVisible = step > 1 && step < 4;
        NextButton.Content = step switch
        {
            1 => "下一步 →",
            2 => "下一步 →",
            3 => "创建金库 →",
            _ => "完成",
        };
        NextButton.IsEnabled = step != 3 || SavedCheck.IsChecked == true;
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (_step == 2) OnNextClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        }
    }

    private void OnBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_step > 1 && _step < 4) ShowStep(_step - 1);
    }

    private async void OnNextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_step == 1) { ShowStep(2); PasswordBox.Focus(); return; }
        if (_step == 2)
        {
            if (!ValidateStep2()) return;
            // Proceed to Recovery Key display
            await GenerateRecoveryPreviewAsync();
            ShowStep(3);
            return;
        }
        if (_step == 3)
        {
            if (SavedCheck.IsChecked != true)
            {
                Step2Error.Text = "请先勾选「我已离线保存恢复密钥」";
                Step2Error.IsVisible = true;
                return;
            }
            await CreateVaultAsync();
            return;
        }
        // _step == 4: do nothing — completes by closing
    }

    private bool ValidateStep2()
    {
        var pw = PasswordBox.Text ?? string.Empty;
        var confirm = ConfirmBox.Text ?? string.Empty;
        if (pw.Length < 8)
        {
            ShowStep2Error("主密码至少 8 个字符");
            return false;
        }
        // §1.3: Reject weak passwords (score < 3)
        if (OmniKeyVault.Application.PasswordStrength.ShouldReject(pw))
        {
            ShowStep2Error("主密码强度不足。" + OmniKeyVault.Application.PasswordStrength.Suggestion(pw));
            return false;
        }
        if (pw != confirm)
        {
            ShowStep2Error("两次输入的主密码不一致");
            return false;
        }
        UpdateFullPathPreview();
        if (string.IsNullOrEmpty(_vaultPath))
        {
            ShowStep2Error("金库位置不能为空");
            return false;
        }
        var folder = System.IO.Path.GetDirectoryName(_vaultPath);
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
        {
            ShowStep2Error($"文件夹不存在或不可访问:{folder}");
            return false;
        }
        var name = System.IO.Path.GetFileName(_vaultPath);
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowStep2Error("金库名称包含非法字符");
            return false;
        }
        if (System.IO.File.Exists(_vaultPath))
        {
            ShowStep2Error("该文件夹中已存在同名 .okv 文件。请更改名称或选择其他文件夹。");
            return false;
        }
        Step2Error.IsVisible = false;
        return true;
    }

    private void EnsureOkvSuffix()
    {
        var name = FilenameBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return;
        if (!name.EndsWith(".okv", StringComparison.OrdinalIgnoreCase))
        {
            var caret = FilenameBox.CaretIndex;
            FilenameBox.Text = name + ".okv";
            try { FilenameBox.CaretIndex = Math.Min(caret + 4, FilenameBox.Text?.Length ?? 0); } catch { }
        }
    }

    private void UpdateFullPathPreview()
    {
        var folder = (FolderBox.Text ?? "").Trim();
        var name = (FilenameBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
        {
            FullPathText.Text = "(请选择文件夹并输入名称)";
            _vaultPath = string.Empty;
            return;
        }
        if (!name.EndsWith(".okv", StringComparison.OrdinalIgnoreCase))
            name += ".okv";
        try
        {
            _vaultPath = System.IO.Path.Combine(folder, name);
            FullPathText.Text = _vaultPath;
        }
        catch (Exception ex)
        {
            FullPathText.Text = "(路径无效:" + ex.Message + ")";
            _vaultPath = string.Empty;
        }
    }

    /// <summary>Open the OS folder picker (StorageProvider.OpenFolderPickerAsync) and fill the folder field.</summary>
    private async void OnBrowseFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null)
            {
                ShowStep2Error("当前环境不支持文件夹选择器,请手动输入路径");
                return;
            }
            // Try to start the picker from the current folder if it exists
            var currentFolder = (FolderBox.Text ?? "").Trim();
            Avalonia.Platform.Storage.IStorageFolder? startFolder = null;
            if (System.IO.Directory.Exists(currentFolder))
            {
                try { startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(new System.Uri(currentFolder)); } catch { }
            }
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择金库文件夹",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
            });
            if (folders.Count > 0)
            {
                FolderBox.Text = folders[0].Path.LocalPath;
                UpdateFullPathPreview();
            }
        }
        catch (Exception ex)
        {
            ShowStep2Error("打开文件夹选择器失败:" + ex.Message);
        }
    }

    private void ShowStep2Error(string msg)
    {
        Step2Error.Text = msg;
        Step2Error.IsVisible = true;
    }

    private void UpdateStrength()
    {
        var pw = PasswordBox.Text ?? string.Empty;
        int score = OmniKeyVault.Application.PasswordStrength.Score(pw);

        var labels = new[] { "极弱", "弱", "一般", "强", "极强" };
        var colors = new[] { "DangerBrush", "DangerBrush", "WarningBrush", "InfoBrush", "SuccessBrush" };
        var bars = new[] { Bar1, Bar2, Bar3, Bar4 };
        var activeColor = Res.Brush(score > 0 ? colors[Math.Min(score, colors.Length - 1)] : "BorderBrush");
        var inactiveColor = Res.Brush("BorderBrush");
        for (int i = 0; i < 4; i++)
            bars[i].Background = i < score ? activeColor : inactiveColor;
        StrengthText.Text = pw.Length == 0
            ? "强度:—"
            : $"强度:{labels[Math.Min(score, labels.Length - 1)]}";

        // Show improvement suggestion for weak passwords
        if (pw.Length > 0 && score < 3)
        {
            StrengthText.Text += " · " + OmniKeyVault.Application.PasswordStrength.Suggestion(pw);
        }
    }

    private async Task GenerateRecoveryPreviewAsync()
    {
        // Generate a preview by creating a throwaway vault and reading back the recovery key.
        // We then delete the throwaway vault and use the captured recovery key for display.
        var tmpPath = Path.Combine(Path.GetTempPath(), $"okv-preview-{Guid.NewGuid():N}.okv");
        try
        {
            var pw = Encoding.UTF8.GetBytes(PasswordBox.Text ?? "");
            var args = Argon2Params.Default;
            var result = await _container.Vault.CreateAsync(tmpPath, "preview", pw, args);
            // Lock to drop keys
            _container.Vault.Lock();
            _generatedRecoveryKey = result.RecoveryKey;
            // Render the 32-block grid
            BuildRecoveryGrid(_generatedRecoveryKey);
        }
        catch (Exception ex)
        {
            ShowStep2Error("生成恢复密钥失败:" + ex.Message);
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        }
    }

    private void BuildRecoveryGrid(string key)
    {
        RecoveryGrid.Children.Clear();
            var groups = RecoveryKeyRenderer.Split(key);
        for (int row = 0; row < 4; row++)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            for (int col = 0; col < 8; col++)
            {
                var idx = row * 8 + col;
                if (idx >= groups.Count) break;
                var cell = new Border { Classes = { "code-cell" } };
                cell.Child = new TextBlock { Classes = { "cell-text" }, Text = groups[idx] };
                rowPanel.Children.Add(cell);
            }
            RecoveryGrid.Children.Add(rowPanel);
        }
    }

    private async void OnCopyRecoveryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedRecoveryKey)) return;
        try
        {
            var cb = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cb != null)
            {
                await cb.SetTextAsync(_generatedRecoveryKey);
                CopyRecoveryText.Text = "✓  已复制";
                await Task.Delay(2000);
                await Dispatcher.UIThread.InvokeAsync(() => CopyRecoveryText.Text = "⧉  复制全部");
            }
        }
        catch { }
    }

    private void OnPrintRecoveryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedRecoveryKey)) return;
        // Save the recovery key to a text file and open it in the system default
        // text editor (Notepad on Windows, TextEdit on macOS, gedit/xdg-open on
        // Linux). The user can then press Ctrl+P / Cmd+P to actually print to
        // their physical printer, or use the system's "Print to PDF" option.
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "okv-recovery-key.txt");
            File.WriteAllText(path, RecoveryKeyRenderer.BuildTextContent(_generatedRecoveryKey));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            CopyRecoveryText.Text = "✓  已打开(可打印)";
        }
        catch (Exception ex)
        {
            ShowStep2Error("打印失败:" + ex.Message);
        }
    }

    private async void OnSavePdfRecoveryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedRecoveryKey)) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null)
            {
                ShowStep2Error("当前环境不支持 PDF 导出,请使用「打印」按钮");
                return;
            }
            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "保存恢复密钥为 PDF",
                DefaultExtension = "pdf",
                ShowOverwritePrompt = true,
                SuggestedFileName = $"omni-recovery-key-{DateTimeOffset.Now:yyyyMMdd}.pdf",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("PDF 文档")
                        { Patterns = new[] { "*.pdf" } },
                },
            });
            if (file == null) return;
            RecoveryKeyRenderer.SavePdf(_generatedRecoveryKey, file.Path.LocalPath);
            CopyRecoveryText.Text = "✓  PDF 已保存";
        }
        catch (Exception ex)
        {
            ShowStep2Error("PDF 导出失败:" + ex.Message);
        }
    }

    private async Task CreateVaultAsync()
    {
        ShowStep(4);
        CompleteText.Text = "正在创建金库…";
        CompleteDetail.Text = "派生 Argon2id 256 MiB 密钥…";
        CompleteIcon.Text = "⋯";
        NextButton.IsEnabled = false;
        var step4ShownAt = DateTimeOffset.UtcNow;
        try
        {
            var pw = Encoding.UTF8.GetBytes(PasswordBox.Text ?? "");
            var args = Argon2Params.Default;
            if (File.Exists(_vaultPath))
            {
                CompleteText.Text = "已存在该金库";
                CompleteDetail.Text = "请删除现有文件或选择其他位置";
                return;
            }
            // CreateAsync already activates the lock + caches the DEK for "prod"
            // (see VaultService.CreateAsync line 145-147). The follow-up UnlockAsync
            // was a no-op that re-derived MK+KEK and re-iterated profiles — but it
            // raced with the wizard's `await` continuation and is the most likely
            // cause of the "process exits silently after step 4" symptom. Remove
            // it; if the vault is ever needed in a different state, do it on the
            // caller (GuiShell.ShowMain) where we know exactly which state we want.
            var result = await _container.Vault.CreateAsync(_vaultPath, "default", pw, args);
            _generatedRecoveryKey = result.RecoveryKey;
            // Persist the canonical "last opened vault" path so a future launch
            // (or the UnlockWindow's "browse" fallback) can find this vault again.
            OmniKeyVault.Cli.Gui.GuiShell.SaveLastVaultPath(_vaultPath);

            CompleteIcon.Text = "✓";
            CompleteText.Text = "金库已创建";
            CompleteDetail.Text = $"UUID: {result.VaultUuid}";
            NextButton.Content = "进入金库 →";
            NextButton.IsEnabled = true;

            // Fire on UI thread. Wrap the event + close in its own try/catch so
            // any failure here shows a clear error in the UI rather than silently
            // tearing down the wizard (which is what the user reported as "crash").
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        VaultCreated?.Invoke(this, _container);
                    }
                    catch (Exception evtEx)
                    {
                        LogCrash("VaultCreated event handler threw", evtEx);
                        CompleteIcon.Text = "✕";
                        CompleteText.Text = "金库已创建,但打开主界面失败";
                        CompleteDetail.Text = evtEx.Message;
                        NextButton.IsEnabled = true;
                        throw;  // re-throw so outer catch can stop the close
                    }
                    try
                    {
                        Close();
                    }
                    catch (Exception closeEx)
                    {
                        LogCrash("Window.Close() threw", closeEx);
                        // non-fatal — the new MainWindow is already up at this point
                    }
                });
            }
            catch (Exception invokeEx)
            {
                // If VaultCreated already showed MainWindow before throwing, the
                // wizard closing is fine. If it didn't, leave the wizard on step 4
                // so the user can see the error.
                if (!IsVisible) return;  // MainWindow took over — we're done
                LogCrash("CreateVaultAsync: InvokeAsync failed and window still open", invokeEx);
                CompleteIcon.Text = "✕";
                CompleteText.Text = "金库已创建,但跳转到主界面失败";
                CompleteDetail.Text = invokeEx.Message;
                NextButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            LogCrash("CreateVaultAsync failed", ex);
            CompleteIcon.Text = "✕";
            CompleteText.Text = "创建失败";
            CompleteDetail.Text = ex.Message;
            NextButton.IsEnabled = true;
        }
        finally
        {
            // Defensive: if something took >30s on step 4, surface it (Argon2id
            // 256 MiB on a slow laptop can take 5-15s; we just want to know if
            // we're stuck vs. just slow).
            var elapsed = (DateTimeOffset.UtcNow - step4ShownAt).TotalSeconds;
            if (elapsed > 30 && CompleteText.Text == "正在创建金库…")
                CompleteDetail.Text += $"  (已耗时 {elapsed:F0} 秒)";
        }
    }

    /// <summary>Best-effort crash log to %TEMP%/okv-crash.log. v0.2 will surface
    /// these in a ToastService.Error; for now we just persist the stack so a
    /// support engineer can attach the file to a bug report.</summary>
    internal static void LogCrash(string context, Exception ex)
    {
        try
        {
            var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okv-crash.log");
            System.IO.File.AppendAllText(log,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {context}\n{ex}\n\n");
        }
        catch { /* log failure is non-fatal */ }
    }
}
