using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// v2.0: Additional functionality for MainWindow — keyboard shortcuts,
/// favorites, recent entries, batch operations, import/export, credential
/// leak detection, system notifications, window position memory, auto-archive,
/// auto-sync, and more.
/// </summary>
public partial class MainWindow
{
    // ---- v2.0: Favorites ----

    private static bool IsFavorite(Entry entry) =>
        SettingsStore.FavoriteEntries.Contains(entry.Id.ToString());

    private void ToggleFavorite(Entry entry)
    {
        var id = entry.Id.ToString();
        if (SettingsStore.FavoriteEntries.Contains(id))
        {
            SettingsStore.FavoriteEntries.Remove(id);
        }
        else
        {
            SettingsStore.FavoriteEntries.Add(id);
        }
        SettingsStore.Save();
        RefreshProfileAndEntries();
    }

    // ---- v2.0: Recent entries ----

    private void RecordRecentEntry(Entry entry)
    {
        var id = entry.Id.ToString();
        SettingsStore.RecentEntries.RemoveAll(e => e == id);
        SettingsStore.RecentEntries.Insert(0, id);
        if (SettingsStore.RecentEntries.Count > 10)
            SettingsStore.RecentEntries = SettingsStore.RecentEntries.Take(10).ToList();
        SettingsStore.Save();
    }

    // ---- v2.0: Keyboard shortcuts ----

    /// <summary>v2.0: Register global keyboard shortcuts.</summary>
    private void RegisterKeyboardShortcuts()
    {
        KeyDown += OnGlobalKeyDown;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Ctrl+N: New entry
        if (ctrl && e.Key == Key.N)
        {
            e.Handled = true;
            OnNewEntryClick(this, new RoutedEventArgs());
        }
        // Ctrl+F: Focus search
        else if (ctrl && e.Key == Key.F)
        {
            e.Handled = true;
            SearchBox.Focus();
        }
        // Ctrl+L: Lock vault
        else if (ctrl && e.Key == Key.L)
        {
            e.Handled = true;
            OnLockClick(this, new RoutedEventArgs());
        }
        // Ctrl+S: Sync
        else if (ctrl && e.Key == Key.S)
        {
            e.Handled = true;
            OnSyncClick(this, new RoutedEventArgs());
        }
        // Ctrl+E: Export
        else if (ctrl && e.Key == Key.E)
        {
            e.Handled = true;
            OnExportClick(this, new RoutedEventArgs());
        }
        // Ctrl+I: Import
        else if (ctrl && e.Key == Key.I)
        {
            e.Handled = true;
            OnImportClick(this, new RoutedEventArgs());
        }
        // Ctrl+G: Password generator
        else if (ctrl && e.Key == Key.G)
        {
            e.Handled = true;
            ShowStandalonePasswordGenerator();
        }
        // Ctrl+D: Duplicate selected entry
        else if (ctrl && e.Key == Key.D)
        {
            e.Handled = true;
            if (_selectedEntry != null)
                _ = DuplicateEntryAsync(_selectedEntry);
        }
        // Ctrl+Shift+C: Check credential leaks
        else if (ctrl && shift && e.Key == Key.C)
        {
            e.Handled = true;
            _ = CheckCredentialLeaksAsync();
        }
        // F5: Refresh
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            RefreshProfileAndEntries();
            ToastService.Show(ToastContainer, "已刷新", ToastType.Info);
        }
        // v2.3: Ctrl+Shift+P — Command palette
        else if (ctrl && shift && e.Key == Key.P)
        {
            e.Handled = true;
            ShowCommandPalette();
        }
        // v2.3: Ctrl+Shift+F — Global search panel
        else if (ctrl && shift && e.Key == Key.F)
        {
            e.Handled = true;
            ShowGlobalSearchPanel();
        }
        // v2.3: F1 or ? — Shortcut cheatsheet
        else if (e.Key == Key.F1 || (!ctrl && !shift && e.Key == Key.OemQuestion))
        {
            e.Handled = true;
            ShowShortcutCheatsheet();
        }
        // v2.3: F2 — Edit selected entry
        else if (e.Key == Key.F2 && _selectedEntry != null)
        {
            e.Handled = true;
            OnDetailEditClick(this, new RoutedEventArgs());
        }
        // v2.3: Arrow Up/Down — Navigate entry list
        else if (e.Key == Key.Up && !ctrl && !shift)
        {
            // Only handle if not in a text input
            if (SearchBox.IsFocused) return;
            e.Handled = true;
            NavigateEntryList(-1);
        }
        else if (e.Key == Key.Down && !ctrl && !shift)
        {
            if (SearchBox.IsFocused) return;
            e.Handled = true;
            NavigateEntryList(1);
        }
        // v2.3: Delete key — Delete selected entry
        else if (e.Key == Key.Delete && _selectedEntry != null && !SearchBox.IsFocused)
        {
            e.Handled = true;
            _ = DeleteEntryAsync(_selectedEntry);
        }
        // v2.3: / — Focus search (vim style)
        else if (!ctrl && !shift && e.Key == Key.Oem2)
        {
            if (!SearchBox.IsFocused)
            {
                e.Handled = true;
                SearchBox.Focus();
            }
        }
        // v2.3: Ctrl+A — Select all entries for batch ops
        else if (ctrl && e.Key == Key.A && !SearchBox.IsFocused)
        {
            e.Handled = true;
            var entries = SafeListEntries(_activeProfile, null, null, null);
            _selectedEntries.Clear();
            foreach (var entry in entries) _selectedEntries.Add(entry.Id);
            RefreshProfileAndEntries();
            ToastService.Show(ToastContainer, $"已选择 {entries.Count} 个条目", ToastType.Info);
        }
        // v2.3: Ctrl+Shift+H — Security health report
        else if (ctrl && shift && e.Key == Key.H)
        {
            e.Handled = true;
            ShowSecurityHealthReport();
        }
    }

    /// <summary>v2.0: Standalone password generator dialog (Ctrl+G).</summary>
    private void ShowStandalonePasswordGenerator()
    {
        var dlg = new Window
        {
            Title = "密码生成器",
            Width = 480, Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

        var lenLabel = new TextBlock { Text = "密码长度: 20", FontSize = 12, Foreground = Res.Brush("FgBrush") };
        var lenSlider = new Slider { Minimum = 8, Maximum = 64, Value = 20, TickFrequency = 1, IsSnapToTickEnabled = true };
        lenSlider.PropertyChanged += (_, _) => lenLabel.Text = $"密码长度: {(int)lenSlider.Value}";
        sp.Children.Add(lenLabel);
        sp.Children.Add(lenSlider);

        var optsPanel = new StackPanel { Spacing = 6 };
        var upperCb = new CheckBox { Content = "大写字母 (A-Z)", IsChecked = true };
        var lowerCb = new CheckBox { Content = "小写字母 (a-z)", IsChecked = true };
        var digitCb = new CheckBox { Content = "数字 (0-9)", IsChecked = true };
        var symbolCb = new CheckBox { Content = "特殊符号 (!@#$...)", IsChecked = true };
        var noAmbiguousCb = new CheckBox { Content = "排除易混淆字符 (Il1O0)", IsChecked = true };
        optsPanel.Children.Add(upperCb);
        optsPanel.Children.Add(lowerCb);
        optsPanel.Children.Add(digitCb);
        optsPanel.Children.Add(symbolCb);
        optsPanel.Children.Add(noAmbiguousCb);
        sp.Children.Add(optsPanel);

        var pwdBox = new TextBox
        {
            FontFamily = Res.Font("FontMono"),
            FontSize = 14,
            IsReadOnly = true,
            Text = _container.PasswordGenerator.Generate(20, true, true, true, true, true),
        };
        sp.Children.Add(pwdBox);

        var strengthLabel = new TextBlock { FontSize = 12 };
        void UpdateStrength()
        {
            var score = PasswordGeneratorService.EstimateStrength(pwdBox.Text ?? "");
            strengthLabel.Text = $"强度: {PasswordGeneratorService.StrengthLabel(score)}";
            strengthLabel.Foreground = score switch
            {
                0 or 1 => Res.Brush("DangerBrush"),
                2 => Res.Brush("WarningBrush"),
                _ => Res.Brush("SuccessBrush"),
            };
        }
        UpdateStrength();
        sp.Children.Add(strengthLabel);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var regenBtn = new Button { Content = "🔄 重新生成", Padding = new Thickness(10, 4) };
        var copyBtn = new Button { Content = "📋 复制", Classes = { "primary" }, Padding = new Thickness(10, 4) };
        var closeBtn = new Button { Content = "关闭", Padding = new Thickness(10, 4) };

        void Regenerate()
        {
            pwdBox.Text = _container.PasswordGenerator.Generate(
                (int)lenSlider.Value,
                upperCb.IsChecked == true,
                lowerCb.IsChecked == true,
                digitCb.IsChecked == true,
                symbolCb.IsChecked == true,
                noAmbiguousCb.IsChecked == true);
            UpdateStrength();
        }

        regenBtn.Click += (_, _) => Regenerate();
        copyBtn.Click += (_, _) => CopyToClipboard(pwdBox.Text ?? "");
        closeBtn.Click += (_, _) => dlg.Close();

        lenSlider.PropertyChanged += (_, _) => Regenerate();
        upperCb.Click += (_, _) => Regenerate();
        lowerCb.Click += (_, _) => Regenerate();
        digitCb.Click += (_, _) => Regenerate();
        symbolCb.Click += (_, _) => Regenerate();
        noAmbiguousCb.Click += (_, _) => Regenerate();

        btnRow.Children.Add(regenBtn);
        btnRow.Children.Add(copyBtn);
        btnRow.Children.Add(closeBtn);
        sp.Children.Add(btnRow);

        dlg.Content = sp;
        dlg.ShowDialog(this);
    }

    // ---- v2.0: Credential leak detection ----

    /// <summary>v2.0: Check all password fields in the active profile for breaches.</summary>
    private async System.Threading.Tasks.Task CheckCredentialLeaksAsync()
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        ToastService.Show(ToastContainer, "正在检查密码泄露情况…", ToastType.Info);

        try
        {
            var entries = SafeListEntries(_activeProfile, null, null, null);
            var passwords = new List<(Entry Entry, Field Field, string Value)>();

            foreach (var entry in entries)
            {
                foreach (var field in entry.Fields)
                {
                    if (field.Kind == FieldKind.Secret && !string.IsNullOrEmpty(field.ValueString))
                    {
                        passwords.Add((entry, field, field.ValueString));
                    }
                }
            }

            if (passwords.Count == 0)
            {
                ToastService.Show(ToastContainer, "没有可检查的密码字段", ToastType.Info);
                return;
            }

            var leaked = new List<(string EntryName, string FieldKey, int Count)>();
            var checked_ = 0;

            foreach (var (entry, field, value) in passwords)
            {
                checked_++;
                if (checked_ % 5 == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        ToastService.Show(ToastContainer, $"正在检查… {checked_}/{passwords.Count}", ToastType.Info));
                }

                var count = await _container.CredentialLeakChecker.CheckPasswordAsync(value);
                if (count > 0)
                {
                    leaked.Add((entry.Name, field.Key, count));
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (leaked.Count == 0)
                {
                    ToastService.Show(ToastContainer, $"✓ 已检查 {passwords.Count} 个密码 — 未发现泄露", ToastType.Success);
                }
                else
                {
                    var names = string.Join("\n", leaked.Take(10).Select(l => $"  • {l.EntryName}.{l.FieldKey} — 泄露 {l.Count} 次"));
                    if (leaked.Count > 10) names += $"\n  … 还有 {leaked.Count - 10} 个";
                    ToastService.Show(ToastContainer, $"⚠ 发现 {leaked.Count} 个泄露密码:\n{names}", ToastType.Warning);
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ToastService.Show(ToastContainer, "泄露检测失败:" + ex.Message, ToastType.Error));
        }
    }

    // ---- v2.0: Encrypted container export ----

    /// <summary>v2.0: Export selected entries to an encrypted container file.</summary>
    private async void OnEncryptedExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            // Password prompt
            var pwdDlg = new Window
            {
                Title = "加密导出 — 设置密码",
                Width = 360, Height = 200,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res.Brush("BgCardBrush"),
            };
            var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            sp.Children.Add(new TextBlock { Text = "为加密容器设置一个独立的导出密码:", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            var pwdBox = new TextBox { Watermark = "导出密码", PasswordChar = '●' };
            sp.Children.Add(pwdBox);
            var confirmBox = new TextBox { Watermark = "确认密码", PasswordChar = '●' };
            sp.Children.Add(confirmBox);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += (_, _) => pwdDlg.Close();
            var export = new Button { Content = "导出", Classes = { "primary" }, Padding = new Thickness(14, 6) };
            string? exportPassword = null;
            export.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(pwdBox.Text) || pwdBox.Text != confirmBox.Text)
                {
                    ToastService.Show(ToastContainer, "密码不匹配", ToastType.Warning);
                    return;
                }
                exportPassword = pwdBox.Text;
                pwdDlg.Close();
            };
            row.Children.Add(cancel);
            row.Children.Add(export);
            sp.Children.Add(row);
            pwdDlg.Content = sp;
            await pwdDlg.ShowDialog(this);

            if (string.IsNullOrEmpty(exportPassword)) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "加密容器导出",
                DefaultExtension = "okvx",
                SuggestedFileName = $"vault-export-{DateTimeOffset.Now:yyyyMMdd}.okvx",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("OmniKey Vault 加密容器")
                        { Patterns = new[] { "*.okvx" } },
                },
            });
            if (file == null) return;
            var path = file.Path.LocalPath;

            var entries = SafeListEntries(_activeProfile, null, null, null);
            await _container.ContainerExporter.ExportAsync(entries, path, exportPassword);
            ToastService.Show(ToastContainer, $"已导出 {entries.Count} 个条目到加密容器", ToastType.Success);
            _container.AuditLog.LogExport(_activeProfile, "encrypted-container");
            _ = _container.AuditLog.FlushAsync();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "加密导出失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: Encrypted container import ----

    /// <summary>v2.0: Import entries from an encrypted container file.</summary>
    private async void OnEncryptedImportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择加密容器文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("OmniKey Vault 加密容器")
                        { Patterns = new[] { "*.okvx" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;

            // Password prompt
            var pwdDlg = new Window
            {
                Title = "加密导入 — 输入密码",
                Width = 360, Height = 160,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res.Brush("BgCardBrush"),
            };
            var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            sp.Children.Add(new TextBlock { Text = "输入加密容器的密码:", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            var pwdBox = new TextBox { Watermark = "密码", PasswordChar = '●' };
            sp.Children.Add(pwdBox);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += (_, _) => pwdDlg.Close();
            var import = new Button { Content = "导入", Classes = { "primary" }, Padding = new Thickness(14, 6) };
            string? importPassword = null;
            import.Click += (_, _) => { importPassword = pwdBox.Text; pwdDlg.Close(); };
            row.Children.Add(cancel);
            row.Children.Add(import);
            sp.Children.Add(row);
            pwdDlg.Content = sp;
            await pwdDlg.ShowDialog(this);

            if (string.IsNullOrEmpty(importPassword)) return;

            var entries = await _container.ContainerExporter.ImportAsync(path, importPassword);
            foreach (var entry in entries)
            {
                _container.Vault.PutEntry(_activeProfile, entry);
            }
            await _container.Vault.SaveAsync();
            ToastService.Show(ToastContainer, $"已从加密容器导入 {entries.Count} 个条目", ToastType.Success);
            _container.AuditLog.LogImport(_activeProfile, "encrypted-container", entries.Count);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "加密导入失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: .env import ----

    /// <summary>v2.0: Import a .env file as a new entry.</summary>
    private async void OnEnvImportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择 .env 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("环境变量文件")
                        { Patterns = new[] { "*.env", ".env", "*" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;

            var count = await _container.EnvFile.ImportAsync(_activeProfile, path, System.IO.Path.GetFileNameWithoutExtension(path));
            await _container.Vault.SaveAsync();
            ToastService.Show(ToastContainer, $"已从 .env 导入 {count} 个条目", ToastType.Success);
            _container.AuditLog.LogImport(_activeProfile, ".env", count);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, ".env 导入失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: .env export ----

    /// <summary>v2.0: Export current profile to .env format.</summary>
    private async void OnEnvExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出 .env 文件",
                DefaultExtension = "env",
                SuggestedFileName = $"export-{_activeProfile}-{DateTimeOffset.Now:yyyyMMdd}.env",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("环境变量文件")
                        { Patterns = new[] { "*.env" } },
                },
            });
            if (file == null) return;
            var path = file.Path.LocalPath;

            await _container.EnvFile.ExportAsync(_activeProfile, path);
            ToastService.Show(ToastContainer, $"已导出到 {System.IO.Path.GetFileName(path)}", ToastType.Success);
            _container.AuditLog.LogExport(_activeProfile, ".env");
            _ = _container.AuditLog.FlushAsync();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, ".env 导出失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: Batch operations ----

    /// <summary>v2.0: Batch export — exports all entries in current profile to JSON.</summary>
    private async void OnBatchExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "批量导出 JSON",
                DefaultExtension = "json",
                SuggestedFileName = $"batch-export-{_activeProfile}-{DateTimeOffset.Now:yyyyMMdd}.json",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                },
            });
            if (file == null) return;
            var path = file.Path.LocalPath;

            var entries = SafeListEntries(_activeProfile, null, null, null);
            var json = System.Text.Json.JsonSerializer.Serialize(entries.Select(e => new
            {
                name = e.Name,
                type = e.Type.ToString(),
                platform_id = e.PlatformId,
                tags = e.Tags,
                fields = e.Fields.Select(f => new { f.Key, value = FieldCodec.Decode(f.Value), kind = f.Kind.ToString(), f.Sensitive }),
                notes = e.Notes,
                created_at = e.CreatedAt,
                updated_at = e.UpdatedAt,
                expires_at = e.ExpiresAt,
            }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            await System.IO.File.WriteAllTextAsync(path, json);
            ToastService.Show(ToastContainer, $"已批量导出 {entries.Count} 个条目", ToastType.Success);
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "批量导出失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: Window position memory ----

    /// <summary>v2.0: Save current window position to settings.</summary>
    private void SaveWindowPosition()
    {
        try
        {
            SettingsStore.WindowX = Position.X;
            SettingsStore.WindowY = Position.Y;
            SettingsStore.WindowWidth = Width;
            SettingsStore.WindowHeight = Height;
            SettingsStore.Save();
        }
        catch { /* best-effort */ }
    }

    /// <summary>v2.0: Restore window position from settings.</summary>
    private void RestoreWindowPosition()
    {
        try
        {
            if (SettingsStore.WindowX.HasValue && SettingsStore.WindowY.HasValue)
            {
                Position = new PixelPoint((int)SettingsStore.WindowX.Value, (int)SettingsStore.WindowY.Value);
            }
            if (SettingsStore.WindowWidth.HasValue && SettingsStore.WindowHeight.HasValue)
            {
                Width = SettingsStore.WindowWidth.Value;
                Height = SettingsStore.WindowHeight.Value;
            }
        }
        catch { /* best-effort */ }
    }

    // ---- v2.0: Auto-archive expired entries ----

    /// <summary>v2.0: Moves expired entries to an "archived" tag after N days past expiry.</summary>
    private void AutoArchiveExpiredEntries()
    {
        try
        {
            if (!_container.Vault.IsUnlocked) return;
            if (SettingsStore.AutoArchiveDays <= 0) return;

            var now = DateTimeOffset.UtcNow;
            var threshold = now.AddDays(-SettingsStore.AutoArchiveDays);
            var archivedCount = 0;

            foreach (var profileName in _container.Vault.ListProfileNames())
            {
                try
                {
                    var entries = _container.Entries.List(profileName, null, null, null);
                    foreach (var entry in entries)
                    {
                        if (!entry.ExpiresAt.HasValue) continue;
                        if (entry.ExpiresAt.Value < threshold && !entry.Tags.Contains("archived"))
                        {
                            // Add "archived" tag
                            var newTags = entry.Tags.Append("archived").ToList();
                            var updated = entry with { Tags = newTags };
                            _container.Vault.PutEntry(profileName, updated);
                            archivedCount++;
                        }
                    }
                }
                catch { /* skip */ }
            }

            if (archivedCount > 0)
            {
                _ = _container.Vault.SaveAsync();
                ToastService.Show(ToastContainer, $"已自动归档 {archivedCount} 个过期条目", ToastType.Info);
            }
        }
        catch { /* best-effort */ }
    }

    // ---- v2.0: Auto-sync after changes ----

    /// <summary>v2.0: Auto-push to WebDAV/S3 after entry changes (if enabled).</summary>
    private async void AutoSyncAfterChangeAsync()
    {
        if (!SettingsStore.AutoSyncOnChange) return;
        if (!SettingsStore.WebDavEnabled && !SettingsStore.S3Enabled) return;

        try
        {
            await System.Threading.Tasks.Task.Delay(2000); // Debounce 2s

            if (SettingsStore.WebDavEnabled && _container.WebDavSync != null)
            {
                var vaultPath = _container.Vault.CurrentVaultPath;
                if (!string.IsNullOrEmpty(vaultPath))
                {
                    await _container.WebDavSync.PushAsync(vaultPath);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        ToastService.Show(ToastContainer, "已自动同步到云端", ToastType.Info));
                }
            }

            if (SettingsStore.S3Enabled && _container.S3Sync.IsConfigured)
            {
                var vaultPath = _container.Vault.CurrentVaultPath;
                if (!string.IsNullOrEmpty(vaultPath))
                {
                    await _container.S3Sync.PushAsync(vaultPath);
                }
            }
        }
        catch { /* best-effort — don't bother user on auto-sync failure */ }
    }

    // ---- v2.0: System notifications ----

    /// <summary>v2.0: Shows a system-level notification (Windows toast or tray balloon).</summary>
    private void ShowSystemNotification(string title, string message)
    {
        if (!SettingsStore.SystemNotificationsEnabled) return;
        try
        {
            // Use Avalonia's notification system if available
            var notifier = new Avalonia.Controls.Notifications.WindowNotificationManager(this)
            {
                Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight,
                MaxItems = 3,
            };
            notifier.Show(new Avalonia.Controls.Notifications.Notification(
                title, message, Avalonia.Controls.Notifications.NotificationType.Information));
        }
        catch { /* best-effort */ }
    }

    // ---- v2.0: SSH Agent integration ----

    /// <summary>v2.0: Load an SSH key from the selected entry into ssh-agent.</summary>
    private async void OnSshAgentLoadClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry == null) return;

        try
        {
            var keyField = _selectedEntry.Fields.FirstOrDefault(f => f.Sensitive || f.Key.Contains("key", StringComparison.OrdinalIgnoreCase));
            if (keyField == null)
            {
                ToastService.Show(ToastContainer, "未找到 SSH 密钥字段", ToastType.Warning);
                return;
            }

            var keyPem = FieldCodec.Decode(keyField.Value);
            var (success, msg) = _container.SshAgent.AddKey(keyPem);
            ToastService.Show(ToastContainer, msg, success ? ToastType.Success : ToastType.Error);
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "SSH Agent 加载失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: Emergency contact (Shamir) ----

    /// <summary>v2.0: Split the vault master password into Shamir shares for emergency contacts.</summary>
    private async void OnEmergencyShareClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new Window
        {
            Title = "紧急联系人 — Shamir 分片",
            Width = 520, Height = 480,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = "将主密码拆分为 N 份分片,任意 K 份可还原。将分片安全地分发给紧急联系人。",
            FontSize = 12, Foreground = Res.Brush("FgMutedBrush"), TextWrapping = TextWrapping.Wrap,
        });

        var secretBox = new TextBox { Watermark = "要分片的密钥 (如主密码)", PasswordChar = '●', Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(secretBox);

        var nLabel = new TextBlock { Text = "分片总数 N: 3", FontSize = 12 };
        var nSlider = new Slider { Minimum = 2, Maximum = 10, Value = 3, TickFrequency = 1, IsSnapToTickEnabled = true };
        nSlider.PropertyChanged += (_, _) => nLabel.Text = $"分片总数 N: {(int)nSlider.Value}";
        sp.Children.Add(nLabel);
        sp.Children.Add(nSlider);

        var kLabel = new TextBlock { Text = "恢复阈值 K: 2", FontSize = 12 };
        var kSlider = new Slider { Minimum = 2, Maximum = 10, Value = 2, TickFrequency = 1, IsSnapToTickEnabled = true };
        kSlider.PropertyChanged += (_, _) => kLabel.Text = $"恢复阈值 K: {(int)kSlider.Value}";
        sp.Children.Add(kLabel);
        sp.Children.Add(kSlider);

        var resultBox = new TextBox
        {
            Watermark = "分片结果将显示在这里…",
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 120,
            FontFamily = Res.Font("FontMono"),
            FontSize = 11,
        };
        sp.Children.Add(resultBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var generate = new Button { Content = "生成分片", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        var copy = new Button { Content = "复制", Padding = new Thickness(14, 6) };
        var close = new Button { Content = "关闭", Padding = new Thickness(14, 6) };

        generate.Click += (_, _) =>
        {
            var secret = secretBox.Text ?? "";
            if (string.IsNullOrEmpty(secret)) { return; }
            var n = (int)nSlider.Value;
            var k = (int)kSlider.Value;
            if (k > n) { resultBox.Text = "错误: K 不能大于 N"; return; }

            try
            {
                var secretBytes = System.Text.Encoding.UTF8.GetBytes(secret);
                var shares = ShamirSecretSharing.Split(secretBytes, n, k);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"已生成 {n} 份分片,需要 {k} 份才能恢复:\n");
                foreach (var (index, share) in shares)
                {
                    sb.AppendLine($"分片 #{index}:");
                    sb.AppendLine(ShamirSecretSharing.ShareToHex(index, share));
                    sb.AppendLine();
                }
                resultBox.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                resultBox.Text = "错误:" + ex.Message;
            }
        };

        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(resultBox.Text))
            {
                Win32Clipboard.SetText(resultBox.Text);
                ToastService.Show(ToastContainer, "已复制分片到剪贴板", ToastType.Success);
            }
        };

        close.Click += (_, _) => dlg.Close();
        btnRow.Children.Add(generate);
        btnRow.Children.Add(copy);
        btnRow.Children.Add(close);
        sp.Children.Add(btnRow);

        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.0: Certificate viewer ----

    /// <summary>v2.0: View certificate details for certificate-type entries.</summary>
    private async void OnViewCertificateClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry == null) return;

        try
        {
            var certField = _selectedEntry.Fields.FirstOrDefault(f => f.Key.Contains("cert", StringComparison.OrdinalIgnoreCase) || f.Key.Contains("pem", StringComparison.OrdinalIgnoreCase));
            if (certField == null)
            {
                ToastService.Show(ToastContainer, "未找到证书字段", ToastType.Warning);
                return;
            }

            var pem = FieldCodec.Decode(certField.Value);
            var info = _container.Certificates.ParsePem(pem);
            var (isExpired, isExpiringSoon, daysRemaining) = CertificateService.CheckExpiry(info);

            var status = isExpired ? "已过期" : isExpiringSoon ? $"{daysRemaining} 天后过期" : $"有效 ({daysRemaining} 天)";
            var statusColor = isExpired ? Res.Brush("DangerBrush") : isExpiringSoon ? Res.Brush("WarningBrush") : Res.Brush("SuccessBrush");

            var dlg = new Window
            {
                Title = "证书详情",
                Width = 480, Height = 400,
                CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res.Brush("BgCardBrush"),
            };

            var sp = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
            sp.Children.Add(new TextBlock { Text = "证书信息", FontSize = 14, FontWeight = FontWeight.SemiBold });
            sp.Children.Add(new TextBlock { Text = $"通用名称: {info.CommonName}", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            sp.Children.Add(new TextBlock { Text = $"颁发者: {info.Issuer}", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            sp.Children.Add(new TextBlock { Text = $"有效期: {info.NotBefore:yyyy-MM-dd} ~ {info.NotAfter:yyyy-MM-dd}", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            sp.Children.Add(new TextBlock { Text = $"状态: {status}", FontSize = 12, Foreground = statusColor, FontWeight = FontWeight.SemiBold });
            sp.Children.Add(new TextBlock { Text = $"指纹: {info.Thumbprint}", FontSize = 11, FontFamily = Res.Font("FontMono"), Foreground = Res.Brush("FgDimBrush") });
            sp.Children.Add(new TextBlock { Text = $"序列号: {info.SerialNumber}", FontSize = 11, FontFamily = Res.Font("FontMono"), Foreground = Res.Brush("FgDimBrush") });
            sp.Children.Add(new TextBlock { Text = $"密钥用途: {info.KeyUsage}", FontSize = 11, Foreground = Res.Brush("FgDimBrush") });
            sp.Children.Add(new TextBlock { Text = $"含私钥: {(info.HasPrivateKey ? "是" : "否")}", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });

            var closeBtn = new Button { Content = "关闭", Padding = new Thickness(14, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            closeBtn.Click += (_, _) => dlg.Close();
            sp.Children.Add(closeBtn);

            dlg.Content = sp;
            await dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "证书查看失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.0: Password history viewer ----

    // ---- v2.0: Drag-drop file support ----

    /// <summary>v2.0: Enable drag-drop of files onto the window for quick attachment import.</summary>
    private void EnableDragDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        DragDrop.DragOverEvent.AddClassHandler<Window>((_, e) => OnDragOver(this, e));
        DragDrop.DropEvent.AddClassHandler<Window>((_, e) => OnDrop(this, e));
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

#pragma warning disable CS0618
    private void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            var files = e.Data?.GetFiles();
            if (files == null) return;

            foreach (var file in files)
            {
                var path = file.Path.LocalPath;
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

                if (ext == ".csv" || ext == ".json" || ext == ".xml" || ext == ".dev" || ext == ".env")
                {
                    ToastService.Show(ToastContainer, $"检测到文件: {System.IO.Path.GetFileName(path)} — 可使用「导入」功能导入", ToastType.Info);
                }
            }
        }
        catch { /* best-effort */ }
    }
#pragma warning restore CS0618

    // ---- v2.0: Selective sync (profile exclusion) ----

    /// <summary>v2.0: Toggles whether a profile participates in sync operations.</summary>
    private static void ToggleProfileSyncExclusion(string profileName)
    {
        if (SettingsStore.SyncExcludedProfiles.Contains(profileName))
            SettingsStore.SyncExcludedProfiles.Remove(profileName);
        else
            SettingsStore.SyncExcludedProfiles.Add(profileName);
        SettingsStore.Save();
    }

    /// <summary>v2.0: Checks if a profile is excluded from sync.</summary>
    private static bool IsProfileSyncExcluded(string profileName) =>
        SettingsStore.SyncExcludedProfiles.Contains(profileName);

    // ---- v2.0: Auto-sync status indicator ----

    /// <summary>v2.0: Updates the sync status indicator in the status bar.</summary>
    private void UpdateSyncStatusIndicator(string status, bool isOk = true)
    {
        try
        {
            SyncText.Text = status;
            SyncDot.Fill = isOk ? Res.Brush("SuccessBrush") : Res.Brush("DangerBrush");
        }
        catch { /* best-effort */ }
    }

    // ---- v2.0: Custom field template creator ----

    /// <summary>v2.0: Opens a dialog to create a custom template.</summary>
    private async void OnCreateCustomTemplateClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new Window
        {
            Title = "创建自定义模板",
            Width = 520, Height = 600,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = "创建自定义字段模板", FontSize = 14, FontWeight = FontWeight.SemiBold });

        var idBox = new TextBox { Watermark = "模板 ID (如: my_service)", Margin = new Thickness(0, 4, 0, 0) };
        sp.Children.Add(idBox);

        var nameBox = new TextBox { Watermark = "显示名称" };
        sp.Children.Add(nameBox);

        var categoryBox = new ComboBox { PlaceholderText = "分类" };
        categoryBox.Items.Add(new ComboBoxItem { Content = "api_key", Tag = "api_key" });
        categoryBox.Items.Add(new ComboBoxItem { Content = "ssh_key", Tag = "ssh_key" });
        categoryBox.Items.Add(new ComboBoxItem { Content = "certificate", Tag = "certificate" });
        categoryBox.Items.Add(new ComboBoxItem { Content = "note", Tag = "note" });
        categoryBox.Items.Add(new ComboBoxItem { Content = "finance", Tag = "finance" });
        categoryBox.Items.Add(new ComboBoxItem { Content = "custom", Tag = "custom" });
        categoryBox.SelectedIndex = 0;
        sp.Children.Add(categoryBox);

        var fieldsPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(new TextBlock { Text = "字段定义:", FontSize = 12, FontWeight = FontWeight.Medium });
        sp.Children.Add(fieldsPanel);

        void AddFieldEditor(string key = "", string label = "", bool sensitive = false)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 4) };
            var kBox = new TextBox { Watermark = "key", Text = key, Margin = new Thickness(0, 0, 4, 0) };
            var lBox = new TextBox { Watermark = "label", Text = label, Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetColumn(kBox, 0);
            Grid.SetColumn(lBox, 1);
            var sensCb = new CheckBox { Content = "密文", IsChecked = sensitive };
            Grid.SetColumn(sensCb, 2);
            var delBtn = new Button { Content = "✕", Padding = new Thickness(6, 2), Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(delBtn, 3);
            delBtn.Click += (_, _) => fieldsPanel.Children.Remove(row);
            row.Children.Add(kBox);
            row.Children.Add(lBox);
            row.Children.Add(sensCb);
            row.Children.Add(delBtn);
            fieldsPanel.Children.Add(row);
        }

        var addFieldBtn = new Button { Content = "+ 添加字段", Padding = new Thickness(10, 4) };
        addFieldBtn.Click += (_, _) => AddFieldEditor();
        sp.Children.Add(addFieldBtn);

        // Pre-fill with 2 empty fields
        AddFieldEditor("username", "用户名", false);
        AddFieldEditor("password", "密码", true);

        var result = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 100, FontFamily = Res.Font("FontMono"), FontSize = 11 };
        sp.Children.Add(result);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var saveBtn = new Button { Content = "保存模板", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        var exportBtn = new Button { Content = "导出 JSON", Padding = new Thickness(14, 6) };
        var closeBtn = new Button { Content = "关闭", Padding = new Thickness(14, 6) };

        saveBtn.Click += (_, _) =>
        {
            var id = (idBox.Text ?? "").Trim();
            var name = (nameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
            {
                result.Text = "错误: 需要填写 ID 和名称";
                return;
            }
            var cat = (categoryBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "custom";
            var fields = new List<(string, string, string, bool, bool)>();
            foreach (var child in fieldsPanel.Children)
            {
                if (child is Grid g && g.Children.Count >= 3)
                {
                    var k = (g.Children[0] as TextBox)?.Text?.Trim() ?? "";
                    var l = (g.Children[1] as TextBox)?.Text?.Trim() ?? "";
                    var s = (g.Children[2] as CheckBox)?.IsChecked == true;
                    if (!string.IsNullOrEmpty(k))
                        fields.Add((k, l, s ? "secret" : "text", s, true));
                }
            }
            try
            {
                var def = _container.CommunityTemplates.CreateTemplate(id, name, cat, "domestic", id, fields);
                _container.CommunityTemplates.SaveToUserTemplates(def);
                result.Text = $"✓ 模板「{name}」已保存\n路径: %APPDATA%/OmniKeyVault/templates/{id}.json";
                ToastService.Show(ToastContainer, $"模板「{name}」已创建", ToastType.Success);
            }
            catch (Exception ex)
            {
                result.Text = "错误:" + ex.Message;
            }
        };

        exportBtn.Click += (_, _) =>
        {
            var id = (idBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(id)) { result.Text = "先填写 ID"; return; }
            try
            {
                var json = _container.CommunityTemplates.ExportTemplate(id);
                result.Text = json;
                ToastService.Show(ToastContainer, "JSON 已生成,可复制分享", ToastType.Info);
            }
            catch (Exception ex)
            {
                result.Text = "错误:" + ex.Message;
            }
        };

        closeBtn.Click += (_, _) => dlg.Close();
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(exportBtn);
        btnRow.Children.Add(closeBtn);
        sp.Children.Add(btnRow);

        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.1: Batch operations (edit/delete multiple entries) ----

    /// <summary>v2.1: Selected entries for batch operations.</summary>
    private readonly HashSet<Guid> _selectedEntries = new();

    /// <summary>v2.1: Toggles selection of an entry for batch operations.</summary>
    private void ToggleEntrySelection(Entry entry)
    {
        if (_selectedEntries.Contains(entry.Id))
            _selectedEntries.Remove(entry.Id);
        else
            _selectedEntries.Add(entry.Id);
        RefreshProfileAndEntries();
    }

    /// <summary>v2.1: Batch delete all selected entries.</summary>
    private async void OnBatchDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntries.Count == 0)
        {
            ToastService.Show(ToastContainer, "请先选择要删除的条目(右键菜单→选择)", ToastType.Warning);
            return;
        }

        var dlg = new Window
        {
            Title = "批量删除确认",
            Width = 380, Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = $"确认删除 {_selectedEntries.Count} 个选中的条目？此操作不可撤销。",
            FontSize = 13, Foreground = Res.Brush("FgMutedBrush"), TextWrapping = TextWrapping.Wrap,
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        cancel.Click += (_, _) => dlg.Close();
        var del = new Button { Content = "删除", Classes = { "primary" }, Background = Res.Brush("DangerBrush"), Foreground = Res.Brush("AccentFgBrush"), Padding = new Thickness(14, 6) };
        bool confirmed = false;
        del.Click += (_, _) => { confirmed = true; dlg.Close(); };
        row.Children.Add(cancel);
        row.Children.Add(del);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
        if (!confirmed) return;

        try
        {
            foreach (var id in _selectedEntries.ToList())
            {
                try { _container.Entries.Delete(_activeProfile, id); } catch { }
            }
            await _container.Vault.SaveAsync();
            var count = _selectedEntries.Count;
            _selectedEntries.Clear();
            _container.AuditLog.LogImport(_activeProfile, "batch-delete", count);
            _ = _container.AuditLog.FlushAsync();
            ToastService.Show(ToastContainer, $"已删除 {count} 个条目", ToastType.Success);
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "批量删除失败:" + ex.Message, ToastType.Error);
        }
    }

    /// <summary>v2.1: Batch edit — add/remove tags or set folder for multiple entries.</summary>
    private async void OnBatchEditClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntries.Count == 0)
        {
            ToastService.Show(ToastContainer, "请先选择要编辑的条目(右键菜单→选择)", ToastType.Warning);
            return;
        }

        var dlg = new Window
        {
            Title = "批量编辑",
            Width = 420, Height = 340,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = $"批量编辑 {_selectedEntries.Count} 个条目",
            FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Res.Brush("FgBrush"),
        });
        sp.Children.Add(new TextBlock { Text = "添加标签 (逗号分隔)", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var addTagsBox = new TextBox { Watermark = "tag1, tag2, ...", Classes = { "field" } };
        sp.Children.Add(addTagsBox);
        sp.Children.Add(new TextBlock { Text = "移除标签 (逗号分隔)", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var removeTagsBox = new TextBox { Watermark = "tag1, tag2, ...", Classes = { "field" } };
        sp.Children.Add(removeTagsBox);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        cancel.Click += (_, _) => dlg.Close();
        var apply = new Button { Content = "应用", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        apply.Click += async (_, _) =>
        {
            try
            {
                var addTags = (addTagsBox.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var removeTags = (removeTagsBox.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var modified = 0;

                foreach (var id in _selectedEntries.ToList())
                {
                    try
                    {
                        var entries = SafeListEntries(_activeProfile, null, null, null);
                        var entry = entries.FirstOrDefault(e => e.Id == id);
                        if (entry == null) continue;

                        var newTags = entry.Tags.ToList();
                        foreach (var t in addTags) if (!newTags.Contains(t)) newTags.Add(t);
                        foreach (var t in removeTags) newTags.RemoveAll(x => x == t);

                        var updated = entry with { Tags = newTags, UpdatedAt = DateTimeOffset.UtcNow, Version = entry.Version + 1 };
                        _container.Vault.PutEntry(_activeProfile, updated);
                        modified++;
                    }
                    catch { }
                }

                await _container.Vault.SaveAsync();
                _selectedEntries.Clear();
                dlg.Close();
                ToastService.Show(ToastContainer, $"已修改 {modified} 个条目的标签", ToastType.Success);
                RefreshProfileAndEntries();
            }
            catch (Exception ex)
            {
                ToastService.Show(ToastContainer, "批量编辑失败:" + ex.Message, ToastType.Error);
            }
        };
        row.Children.Add(cancel);
        row.Children.Add(apply);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.1: 1Password .1pux native import ----

    private async void OnImport1PuxClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择 1Password .1pux 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("1Password Export")
                        { Patterns = new[] { "*.1pux" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            var count = await _container.OnePuxImportNative.ImportAsync(_activeProfile, path);
            await _container.Vault.SaveAsync();
            ToastService.Show(ToastContainer, $"已从 1Password .1pux 导入 {count} 个条目", ToastType.Success);
            _container.AuditLog.LogImport(_activeProfile, "1pux", count);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "1Password 导入失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.1: KeePass KDBX binary import ----

    private async void OnImportKdbxClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择 KeePass KDBX 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("KeePass Database")
                        { Patterns = new[] { "*.kdbx", "*.kdb" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;

            // Prompt for KeePass master password
            var pwdDlg = new Window
            {
                Title = "输入 KeePass 主密码",
                Width = 360, Height = 160,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res.Brush("BgCardBrush"),
            };
            var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            sp.Children.Add(new TextBlock { Text = "输入 KeePass 数据库的主密码:", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            var pwdBox = new TextBox { Watermark = "主密码", PasswordChar = '●', Classes = { "field" } };
            sp.Children.Add(pwdBox);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += (_, _) => pwdDlg.Close();
            var import = new Button { Content = "导入", Classes = { "primary" }, Padding = new Thickness(14, 6) };
            string? kdbxPassword = null;
            import.Click += (_, _) => { kdbxPassword = pwdBox.Text; pwdDlg.Close(); };
            row.Children.Add(cancel);
            row.Children.Add(import);
            sp.Children.Add(row);
            pwdDlg.Content = sp;
            await pwdDlg.ShowDialog(this);

            if (string.IsNullOrEmpty(kdbxPassword)) return;

            var (count, msg) = await _container.KeePassKdbx.ImportAsync(_activeProfile, path, kdbxPassword);
            await _container.Vault.SaveAsync();
            ToastService.Show(ToastContainer, msg, ToastType.Success);
            _container.AuditLog.LogImport(_activeProfile, "kdbx", count);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "KDBX 导入失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.1: EnPass import ----

    private async void OnImportEnPassClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择 EnPass JSON 导出文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("EnPass JSON")
                        { Patterns = new[] { "*.json" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            var count = await _container.EnPassImport.ImportAsync(_activeProfile, path);
            await _container.Vault.SaveAsync();
            ToastService.Show(ToastContainer, $"已从 EnPass 导入 {count} 个条目", ToastType.Success);
            _container.AuditLog.LogImport(_activeProfile, "enpass", count);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "EnPass 导入失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.1: CSV import preview ----

    /// <summary>v2.1: Shows a preview dialog before importing a CSV file.</summary>
    private async void OnCsvImportPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择 CSV 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("CSV")
                        { Patterns = new[] { "*.csv" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            var csv = await System.IO.File.ReadAllTextAsync(path);

            // Show preview dialog
            var dlg = new Window
            {
                Title = "CSV 导入预览",
                Width = 700, Height = 500,
                CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res.Brush("BgCardBrush"),
            };
            var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            sp.Children.Add(new TextBlock
            {
                Text = "CSV 导入预览",
                FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Res.Brush("FgBrush"),
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"文件: {System.IO.Path.GetFileName(path)}",
                FontSize = 11, Foreground = Res.Brush("FgDimBrush"),
            });

            // Parse and show first 10 rows
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var previewLines = lines.Take(Math.Min(10, lines.Length)).ToArray();
            var previewText = string.Join("\n", previewLines);
            sp.Children.Add(new TextBlock { Text = $"前 {previewLines.Length} 行 (共 {lines.Length} 行):", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
            var previewBox = new TextBox
            {
                Text = previewText,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Height = 280,
                TextWrapping = TextWrapping.Wrap,
            };
            sp.Children.Add(previewBox);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += (_, _) => dlg.Close();
            var import = new Button { Content = "确认导入", Classes = { "primary" }, Padding = new Thickness(14, 6) };
            import.Click += async (_, _) =>
            {
                dlg.Close();
                try
                {
                    var count = await _container.CsvImport.ImportAsync(_activeProfile, path);
                    await _container.Vault.SaveAsync();
                    ToastService.Show(ToastContainer, $"已从 CSV 导入 {count} 个条目", ToastType.Success);
                    _container.AuditLog.LogImport(_activeProfile, "csv-preview", count);
                    _ = _container.AuditLog.FlushAsync();
                    RefreshProfileAndEntries();
                }
                catch (Exception ex)
                {
                    ToastService.Show(ToastContainer, "CSV 导入失败:" + ex.Message, ToastType.Error);
                }
            };
            row.Children.Add(cancel);
            row.Children.Add(import);
            sp.Children.Add(row);
            dlg.Content = sp;
            await dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "CSV 预览失败:" + ex.Message, ToastType.Error);
        }
    }

    // ---- v2.1: Search result quick copy ----

    /// <summary>v2.1: Copy a field value directly from search results.</summary>
    private void OnSearchQuickCopy(Entry entry)
    {
        var firstSecret = entry.Fields.FirstOrDefault(f => f.Sensitive);
        if (firstSecret != null)
        {
            CopyToClipboard(FieldCodec.Decode(firstSecret.Value));
        }
        else
        {
            var firstField = entry.Fields.FirstOrDefault();
            if (firstField != null)
                CopyToClipboard(FieldCodec.Decode(firstField.Value));
        }
    }

    // ---- v2.1: Password strength full library scan ----

    /// <summary>v2.1: Scans all entries across all profiles for weak passwords.</summary>
    private async void OnPasswordStrengthScanClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        ToastService.Show(ToastContainer, "正在扫描密码强度…", ToastType.Info);

        try
        {
            var weak = new List<(string Profile, string EntryName, string FieldKey, int Score)>();
            var total = 0;

            foreach (var profileName in _container.Vault.ListProfileNames())
            {
                try
                {
                    var entries = SafeListEntries(profileName, null, null, null);
                    foreach (var entry in entries)
                    {
                        foreach (var field in entry.Fields)
                        {
                            if (field.Kind == FieldKind.Secret && !string.IsNullOrEmpty(field.ValueString))
                            {
                                total++;
                                var score = PasswordGeneratorService.EstimateStrength(field.ValueString);
                                if (score <= 1)
                                {
                                    weak.Add((profileName, entry.Name, field.Key, score));
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (weak.Count == 0)
                {
                    ToastService.Show(ToastContainer, $"✓ 已扫描 {total} 个密码 — 全部为强密码", ToastType.Success);
                }
                else
                {
                    var names = string.Join("\n", weak.Take(10).Select(w => $"  • [{w.Profile}] {w.EntryName}.{w.FieldKey} — 强度:{w.Score}"));
                    if (weak.Count > 10) names += $"\n  … 还有 {weak.Count - 10} 个";
                    ToastService.Show(ToastContainer, $"⚠ 发现 {weak.Count} 个弱密码:\n{names}", ToastType.Warning);
                    if (SettingsStore.SystemNotificationsEnabled)
                    {
                        ShowSystemNotification("密码强度扫描完成", $"发现 {weak.Count} 个弱密码");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ToastService.Show(ToastContainer, "扫描失败:" + ex.Message, ToastType.Error));
        }
    }

    // ---- v2.1: Periodic leak detection ----

    private DispatcherTimer? _leakCheckTimer;

    /// <summary>v2.1: Starts a periodic leak detection timer (runs every 24 hours).</summary>
    private void StartPeriodicLeakCheck()
    {
        _leakCheckTimer?.Stop();
        _leakCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _leakCheckTimer.Tick += (_, _) => _ = CheckCredentialLeaksAsync();
        _leakCheckTimer.Start();
    }

    // ---- v2.1: CSV/Markdown export ----

    /// <summary>v2.1: Export current profile to CSV format.</summary>
    private async void OnCsvExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出 CSV",
                DefaultExtension = "csv",
                SuggestedFileName = $"export-{_activeProfile}-{DateTimeOffset.Now:yyyyMMdd}.csv",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                },
            });
            if (file == null) return;
            var path = file.Path.LocalPath;

            var entries = SafeListEntries(_activeProfile, null, null, null);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("name,type,platform,field_key,field_value,field_kind,sensitive,notes,tags");
            foreach (var entry in entries)
            {
                foreach (var f in entry.Fields)
                {
                    var value = FieldCodec.Decode(f.Value);
                    sb.AppendLine($"\"{EscapeCsv(entry.Name)}\",\"{entry.Type}\",\"{entry.PlatformId ?? ""}\",\"{EscapeCsv(f.Key)}\",\"{EscapeCsv(value)}\",\"{f.Kind}\",\"{f.Sensitive}\",\"{EscapeCsv(entry.Notes ?? "")}\",\"{EscapeCsv(string.Join(";", entry.Tags))}\"");
                }
            }
            await System.IO.File.WriteAllTextAsync(path, sb.ToString());
            ToastService.Show(ToastContainer, $"已导出 {entries.Count} 个条目到 CSV", ToastType.Success);
            _container.AuditLog.LogExport(_activeProfile, "csv");
            _ = _container.AuditLog.FlushAsync();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "CSV 导出失败:" + ex.Message, ToastType.Error);
        }
    }

    /// <summary>v2.1: Export current profile to Markdown format.</summary>
    private async void OnMarkdownExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出 Markdown",
                DefaultExtension = "md",
                SuggestedFileName = $"export-{_activeProfile}-{DateTimeOffset.Now:yyyyMMdd}.md",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } },
                },
            });
            if (file == null) return;
            var path = file.Path.LocalPath;

            var entries = SafeListEntries(_activeProfile, null, null, null);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# OmniKey Vault Export — {_activeProfile}");
            sb.AppendLine();
            sb.AppendLine($"导出时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"条目数: {entries.Count}");
            sb.AppendLine();
            foreach (var entry in entries)
            {
                sb.AppendLine($"## {entry.Name}");
                sb.AppendLine($"- **类型**: {entry.Type}");
                sb.AppendLine($"- **平台**: {entry.PlatformId ?? "—"}");
                if (entry.Tags.Count > 0)
                    sb.AppendLine($"- **标签**: {string.Join(", ", entry.Tags.Select(t => "`#" + t + "`"))}");
                sb.AppendLine();
                sb.AppendLine("| 字段 | 值 | 类型 |");
                sb.AppendLine("|------|------|------|");
                foreach (var f in entry.Fields)
                {
                    var value = f.Sensitive ? "••••••••" : FieldCodec.Decode(f.Value);
                    sb.AppendLine($"| {f.Key} | {value} | {f.Kind} |");
                }
                if (!string.IsNullOrEmpty(entry.Notes))
                {
                    sb.AppendLine();
                    sb.AppendLine($"**备注**: {entry.Notes}");
                }
                sb.AppendLine();
            }
            await System.IO.File.WriteAllTextAsync(path, sb.ToString());
            ToastService.Show(ToastContainer, $"已导出 {entries.Count} 个条目到 Markdown", ToastType.Success);
            _container.AuditLog.LogExport(_activeProfile, "markdown");
            _ = _container.AuditLog.FlushAsync();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "Markdown 导出失败:" + ex.Message, ToastType.Error);
        }
    }

    private static string EscapeCsv(string s) => s.Replace("\"", "\"\"");

    // ---- v2.1: Entry relationship links ----

    /// <summary>v2.1: Links an entry to another entry by adding a "related_entry" field.</summary>
    private async void OnLinkEntryClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry == null) return;

        var entries = SafeListEntries(_activeProfile, null, null, null)
            .Where(e => e.Id != _selectedEntry.Id)
            .ToList();
        if (entries.Count == 0)
        {
            ToastService.Show(ToastContainer, "没有可关联的其他条目", ToastType.Info);
            return;
        }

        var dlg = new Window
        {
            Title = "关联条目",
            Width = 420, Height = 400,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = "选择要关联的条目:", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var listBox = new ListBox { Height = 280 };
        foreach (var entry in entries)
        {
            listBox.Items.Add(new ListBoxItem { Content = $"{entry.Name} ({entry.PlatformId ?? "—"})", Tag = entry });
        }
        sp.Children.Add(listBox);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        cancel.Click += (_, _) => dlg.Close();
        var link = new Button { Content = "关联", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        link.Click += async (_, _) =>
        {
            var selected = (listBox.SelectedItem as ListBoxItem)?.Tag as Entry;
            if (selected == null) { dlg.Close(); return; }
            try
            {
                var linkedField = new Field
                {
                    Key = "related_entry",
                    Value = FieldCodec.Encode(selected.Id.ToString()),
                    Kind = FieldKind.Text,
                    Sensitive = false,
                };
                var updated = _selectedEntry with
                {
                    Fields = _selectedEntry.Fields.Append(linkedField).ToList(),
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Version = _selectedEntry.Version + 1,
                };
                _container.Vault.PutEntry(_activeProfile, updated);
                await _container.Vault.SaveAsync();
                dlg.Close();
                ToastService.Show(ToastContainer, $"已关联到「{selected.Name}」", ToastType.Success);
                RefreshProfileAndEntries();
            }
            catch (Exception ex)
            {
                ToastService.Show(ToastContainer, "关联失败:" + ex.Message, ToastType.Error);
            }
        };
        row.Children.Add(cancel);
        row.Children.Add(link);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.1: Custom expiration reminder rules ----

    /// <summary>v2.1: Shows a dialog to configure custom expiration reminder rules.</summary>
    private async void OnCustomExpiryRulesClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new Window
        {
            Title = "自定义过期提醒",
            Width = 400, Height = 280,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = "过期提醒设置", FontSize = 14, FontWeight = FontWeight.SemiBold });
        sp.Children.Add(new TextBlock { Text = "提前多少天开始提醒过期?", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var daysSlider = new Slider { Minimum = 1, Maximum = 90, Value = 7, TickFrequency = 1, IsSnapToTickEnabled = true };
        var daysLabel = new TextBlock { Text = "提前 7 天", FontSize = 12 };
        daysSlider.PropertyChanged += (_, _) => daysLabel.Text = $"提前 {(int)daysSlider.Value} 天";
        sp.Children.Add(daysLabel);
        sp.Children.Add(daysSlider);

        var notifyCb = new CheckBox { IsChecked = SettingsStore.SystemNotificationsEnabled, Content = "同时显示系统通知" };
        sp.Children.Add(notifyCb);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "保存", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        close.Click += (_, _) =>
        {
            SettingsStore.SystemNotificationsEnabled = notifyCb.IsChecked == true;
            SettingsStore.Save();
            ToastService.Show(ToastContainer, $"已保存 — 提前 {(int)daysSlider.Value} 天提醒", ToastType.Success);
            dlg.Close();
        };
        row.Children.Add(close);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.3.7: Global hotkey (Ctrl+Shift+V) ----

    private nint _hotkeyHwnd = nint.Zero;

    /// <summary>v2.3.7: Registers the global hotkey via HotkeyService.</summary>
    private void RegisterGlobalHotkey()
    {
        if (!SettingsStore.HotkeyEnabled) return;

        try
        {
            // Update config from settings
            _container.Hotkey.UpdateConfig(new HotkeyConfig
            {
                Enabled = SettingsStore.HotkeyEnabled,
                Modifiers = SettingsStore.HotkeyModifiers,
                Key = SettingsStore.HotkeyKey,
                WakeMethod = SettingsStore.HotkeyWakeMethod,
            });

            // Get the Win32 HWND for this window
            var handle = TryGetPlatformHandle();
            if (handle == null) return;

            _hotkeyHwnd = handle.Handle;
            if (_hotkeyHwnd == nint.Zero) return;

            if (_container.Hotkey.TryRegister(_hotkeyHwnd))
            {
                _container.Hotkey.HotkeyPressed += (_, _) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        // Bring window to foreground
                        Show();
                        Activate();
                        WindowState = WindowState.Normal;
                        Topmost = true;
                        Topmost = false; // flash to bring above other topmost windows
                    });
                };

                // Install a WndProc hook to intercept WM_HOTKEY (0x0312)
                // Avalonia's default WndProc doesn't expose WM_HOTKEY to managed code,
                // so we use SetWindowLong to subclass the window.
                InstallWndProcHook(_hotkeyHwnd);
            }
        }
        catch { /* best-effort: hotkey is non-critical */ }
    }

    /// <summary>Unregisters the global hotkey on window close.</summary>
    private void UnregisterGlobalHotkey()
    {
        try
        {
            _container.Hotkey.Unregister();
            if (_hotkeyHwnd != nint.Zero)
            {
                RemoveWndProcHook(_hotkeyHwnd);
                _hotkeyHwnd = nint.Zero;
            }
        }
        catch { }
    }

    // ---- Win32 WndProc subclassing for WM_HOTKEY ----

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint Msg, nint wParam, nint lParam);

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int GWLP_WNDPROC = -4;
    private const int WM_HOTKEY = 0x0312;
    private nint _originalWndProc = nint.Zero;
    private WndProcDelegate? _hookDelegate;

    private void InstallWndProcHook(nint hwnd)
    {
        _hookDelegate = HookedWndProc;
        var newPtr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_hookDelegate);
        _originalWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, newPtr);
    }

    private void RemoveWndProcHook(nint hwnd)
    {
        if (_originalWndProc != nint.Zero)
        {
            SetWindowLongPtr(hwnd, GWLP_WNDPROC, _originalWndProc);
            _originalWndProc = nint.Zero;
        }
    }

    private nint HookedWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY)
        {
            _container.Hotkey.ProcessMessage(hWnd, (int)msg, wParam, lParam);
        }
        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }
}
