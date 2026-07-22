using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Infrastructure;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Real settings dialog. Auto-lock minutes + clipboard clear seconds are
/// persisted via the static <see cref="SettingsStore"/> and read on each
/// timer tick by the owning MainWindow. Theme is fixed to dark in v0.1.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly CliContainer _container;
    private bool _suppressEvents;

    public SettingsWindow(CliContainer container)
    {
        InitializeComponent();
        _container = container;
        BuildDeviceInfoPanel();
        var ver = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "unknown";
        AboutText.Text = $"OmniKey Vault v{ver}\n.NET 8 + Avalonia 11.3\n本地优先 · 端到端加密\n\n" +
                         "文档:docs/MANUAL.md\n" +
                         "源码:github.com/mawenshui/omnikey-vault\n" +
                         "反馈:github.com/mawenshui/omnikey-vault/issues";

        // Initialize ComboBoxes + checkboxes from current settings
        _suppressEvents = true;
        // Language
        for (int i = 0; i < LanguageBox.Items.Count; i++)
        {
            if ((LanguageBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == SettingsStore.Language)
            { LanguageBox.SelectedIndex = i; break; }
        }
        if (LanguageBox.SelectedIndex < 0) LanguageBox.SelectedIndex = 0;
        // Theme
        for (int i = 0; i < ThemeBox.Items.Count; i++)
        {
            if ((ThemeBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == SettingsStore.Theme.ToString())
            { ThemeBox.SelectedIndex = i; break; }
        }
        if (ThemeBox.SelectedIndex < 0) ThemeBox.SelectedIndex = 0;
        // Auto-lock + clipboard
        AutoLockBox.SelectedIndex = SettingsStore.AutoLockMinutes switch
        {
            5 => 0, 10 => 1, 15 => 2, 30 => 3, 60 => 4, _ => 2,
        };
        ClipboardBox.SelectedIndex = SettingsStore.ClipboardClearSeconds switch
        {
            4 => 0, 8 => 1, 16 => 2, 30 => 3, _ => 1,
        };
        // Session-lock / suspend toggles
        LockOnSessionLockBox.IsChecked = SettingsStore.LockOnSessionLock;
        LockOnSuspendBox.IsChecked = SettingsStore.LockOnSuspend;
        // Sync
        SyncDirBox.Text = SettingsStore.SyncDirectory ?? "";
        WatcherEnabledBox.IsChecked = SettingsStore.WatcherEnabled;
        // WebDAV
        WebDavUrlBox.Text = SettingsStore.WebDavServerUrl ?? "";
        WebDavUserBox.Text = SettingsStore.WebDavUsername ?? "";
        WebDavPassBox.Text = SettingsStore.WebDavPassword ?? "";
        WebDavPathBox.Text = SettingsStore.WebDavRemoteFilePath ?? "vault.okv";
        WebDavEnabledBox.IsChecked = SettingsStore.WebDavEnabled;
        WebDavAutoSyncBox.IsChecked = SettingsStore.WebDavAutoSync;
        // v1.9.1: update + autostart
        AutoCheckUpdateBox.IsChecked = SettingsStore.AutoCheckUpdateOnStartup;
        AutoStartBox.IsChecked = OmniKeyVault.Application.AutoStartService.IsAutoStartEnabled();
        MinimizeToTrayBox.IsChecked = SettingsStore.MinimizeToTrayOnClose;
        // v1.9: browser extension API
        BrowserApiEnabledBox.IsChecked = SettingsStore.BrowserApiEnabled;
        // v2.0: S3 sync
        S3EndpointBox.Text = SettingsStore.S3Endpoint ?? "";
        S3BucketBox.Text = SettingsStore.S3Bucket ?? "";
        S3AccessKeyBox.Text = SettingsStore.S3AccessKey ?? "";
        S3SecretKeyBox.Text = SettingsStore.S3SecretKey ?? "";
        S3RegionBox.Text = SettingsStore.S3Region ?? "us-east-1";
        S3EnabledBox.IsChecked = SettingsStore.S3Enabled;
        // v2.0: advanced settings
        AutoSyncOnChangeBox.IsChecked = SettingsStore.AutoSyncOnChange;
        SystemNotificationsBox.IsChecked = SettingsStore.SystemNotificationsEnabled;
        AutoArchiveDaysBox.SelectedIndex = SettingsStore.AutoArchiveDays switch
        {
            7 => 0, 14 => 1, 30 => 2, 60 => 3, 0 => 4, _ => 2,
        };
        UpdateBrowserApiTokenDisplay();
        // v2.3: Accessibility settings
        for (int i = 0; i < FontSizeBox.Items.Count; i++)
        {
            if ((FontSizeBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == SettingsStore.FontSizeScale)
            { FontSizeBox.SelectedIndex = i; break; }
        }
        if (FontSizeBox.SelectedIndex < 0) FontSizeBox.SelectedIndex = 1;
        for (int i = 0; i < ListDensityBox.Items.Count; i++)
        {
            if ((ListDensityBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == SettingsStore.ListDensity)
            { ListDensityBox.SelectedIndex = i; break; }
        }
        if (ListDensityBox.SelectedIndex < 0) ListDensityBox.SelectedIndex = 1;
        HighContrastBox.IsChecked = SettingsStore.HighContrastMode;
        _suppressEvents = false;

        BuildProfilesPanel();
    }

    /// <summary>v0.2: render device info as a 2-column label/value grid so long
    /// hostnames + vault paths wrap cleanly inside the panel instead of pushing
    /// the layout into awkward stacked "label:value" lines. v0.2 gap-fill:
    /// also lists other devices registered in <c>manifest.json</c>'s
    /// <c>device_public_keys</c> map with a per-row "revoke" action.</summary>
    private async void BuildDeviceInfoPanel()
    {
        DeviceInfoPanel.Children.Clear();
        void Row(string label, string value, IBrush? valueBrush = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = Res.Brush("FgDimBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0),
            });
            Grid.SetColumn(g.Children[0], 0);
            g.Children.Add(new TextBlock
            {
                Text = value,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = valueBrush ?? Res.Brush("FgMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(g.Children[1], 1);
            DeviceInfoPanel.Children.Add(g);
        }
        Row("当前设备", _container.DeviceId);
        Row("金库路径", _container.Vault.CurrentVaultPath ?? "(未打开)");
        Row("解锁状态", _container.Lock.IsUnlocked ? "已解锁" : "锁定",
            valueBrush: _container.Lock.IsUnlocked ? Res.Brush("SuccessBrush") : Res.Brush("FgMutedBrush"));

        // v0.2 S4-T8 / MANUAL §11.6: list devices known to this vault
        // (read from manifest.json's device_public_keys).
        DeviceInfoPanel.Children.Add(new Border
        {
            BorderBrush = Res.Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 6, 0, 6),
        });
        DeviceInfoPanel.Children.Add(new TextBlock
        {
            Text = "已注册设备(来自 manifest.json)",
            FontFamily = Res.Font("FontMono"),
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Res.Brush("FgFaintBrush"),
            Margin = new Thickness(0, 4, 0, 4),
        });

        IReadOnlyDictionary<string, string> knownDevices = new Dictionary<string, string>();
        try
        {
            if (!string.IsNullOrEmpty(_container.Vault.CurrentVaultPath))
            {
                var manifestPath = OmniKeyVault.Application.SyncService.ManifestPathFor(_container.Vault.CurrentVaultPath);
                if (System.IO.File.Exists(manifestPath))
                {
                    var manifest = await _container.Sync.GetOrCreateLocalManifestAsync(_container.Vault.CurrentVaultPath);
                    knownDevices = manifest.DevicePublicKeys;
                }
            }
        }
        catch { /* best-effort */ }

        if (knownDevices.Count == 0)
        {
            DeviceInfoPanel.Children.Add(new TextBlock
            {
                Text = "(尚无已注册设备)",
                FontSize = 11,
                Foreground = Res.Brush("FgDimBrush"),
                FontStyle = Avalonia.Media.FontStyle.Italic,
            });
            return;
        }

        foreach (var (deviceId, pubKeyB64) in knownDevices)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 2, 0, 2),
            };
            var fp = pubKeyB64.Length >= 12 ? pubKeyB64.Substring(0, 12) + "…" : pubKeyB64;
            var isSelf = deviceId == _container.DeviceId;
            var label = new TextBlock
            {
                Text = (isSelf ? "★ " : "  ") + deviceId + "  ·  " + fp,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = isSelf ? Res.Brush("AccentBrush") : Res.Brush("FgMutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);
            if (!isSelf)
            {
                var rev = new Button
                {
                    Classes = { "ghost" },
                    Padding = new Thickness(8, 2),
                    FontSize = 10,
                    Content = new TextBlock { Text = "吊销" },
                };
                rev.Click += (_, _) => RevokeDevice(deviceId);
                Grid.SetColumn(rev, 1);
                row.Children.Add(rev);
            }
            DeviceInfoPanel.Children.Add(row);
        }
    }

    /// <summary>Removes a device's public key from the manifest and re-saves
    /// the vault. The other device's next sync attempt will be rejected
    /// (SEC-T8-02: unknown signature → user prompt).</summary>
    private async void RevokeDevice(string deviceId)
    {
        try
        {
            if (string.IsNullOrEmpty(_container.Vault.CurrentVaultPath)) return;
            var manifestPath = OmniKeyVault.Application.SyncService.ManifestPathFor(_container.Vault.CurrentVaultPath);
            if (!System.IO.File.Exists(manifestPath)) return;
            var manifest = await _container.Sync.GetOrCreateLocalManifestAsync(_container.Vault.CurrentVaultPath);
            var keys = new Dictionary<string, string>(manifest.DevicePublicKeys);
            keys.Remove(deviceId);
            var updated = manifest with { DevicePublicKeys = keys };
            await _container.Manifests.WriteAsync(manifestPath, updated);
            ShowStatus($"✓ 已吊销设备 {deviceId}", success: true);
            BuildDeviceInfoPanel();
        }
        catch (Exception ex)
        {
            ShowStatus("✕ 吊销失败:" + ex.Message, success: false);
        }
    }

    /// <summary>v0.2 (S3-T7): inline profile settings editor. Each profile
    /// gets a row with its color dot, a "同步参与" toggle, "切换后自动锁"
    /// toggle, and a "空闲锁" combo. Saves via ProfileService.UpdateSettingsAsync.</summary>
    private void BuildProfilesPanel()
    {
        ProfilesPanel.Children.Clear();
        foreach (var info in _container.Profiles.List())
        {
            var row = new Border
            {
                BorderBrush = Res.Brush("BorderBrightBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6),
                Background = Res.Brush("BgElevatedBrush"),
            };
            var sp = new StackPanel { Spacing = 6 };
            // Header: dot + name + entry count
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            // info.Color is a string (e.g. "Green", "Yellow"); map to brush key
            var brushKey = info.Color switch
            {
                "Green" => "ProfileProdBrush",
                "Yellow" => "ProfileDevBrush",
                "Blue" => "ProfileTestBrush",
                "Red" => "DangerBrush",
                "Purple" => "AccentBrush",
                _ => "ProfileProdBrush",
            };
            header.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill = Res.Brush(brushKey),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(header.Children[0], 0);
            header.Children.Add(new TextBlock
            {
                Text = $"{info.Name} · {info.EntryCount} 个条目",
                FontSize = 12,
                Foreground = Res.Brush("FgBrush"),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(header.Children[1], 1);
            sp.Children.Add(header);

            // Sync participation toggle
            var syncCheck = new CheckBox
            {
                IsChecked = info.ParticipateInSync,
                Content = new TextBlock { Text = "参与同步", FontSize = 11, Foreground = Res.Brush("FgMutedBrush") },
            };
            syncCheck.IsCheckedChanged += async (_, _) =>
            {
                try
                {
                    var current = _container.Vault.GetProfile(info.Name);
                    var s = current.Settings with
                    {
                        ParticipateInSync = syncCheck.IsChecked == true,
                    };
                    await System.Threading.Tasks.Task.Run(() =>
                        _container.Profiles.UpdateSettingsAsync(info.Name, s));
                    ShowStatus($"✓ {info.Name} 同步设置已更新", success: true);
                }
                catch (Exception ex) { ShowStatus("更新失败:" + ex.Message, success: false); }
            };
            sp.Children.Add(syncCheck);

            // Note: ProfileInfo record doesn't expose AutoLockOnSwitch directly;
            // we read it via the live Profile.Settings (best-effort).
            bool autoLockOnSwitch = false;
            try { autoLockOnSwitch = _container.Vault.GetProfile(info.Name).Settings.AutoLockOnSwitch; } catch { }
            var autoLockCheck = new CheckBox
            {
                IsChecked = autoLockOnSwitch,
                Content = new TextBlock { Text = "切换后自动锁定", FontSize = 11, Foreground = Res.Brush("FgMutedBrush") },
            };
            autoLockCheck.IsCheckedChanged += async (_, _) =>
            {
                try
                {
                    var current = _container.Vault.GetProfile(info.Name);
                    var s = current.Settings with
                    {
                        AutoLockOnSwitch = autoLockCheck.IsChecked == true,
                    };
                    await System.Threading.Tasks.Task.Run(() =>
                        _container.Profiles.UpdateSettingsAsync(info.Name, s));
                    ShowStatus($"✓ {info.Name} 锁定策略已更新", success: true);
                }
                catch (Exception ex) { ShowStatus("更新失败:" + ex.Message, success: false); }
            };
            sp.Children.Add(autoLockCheck);

            // Idle lock minutes display
            var idleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            idleRow.Children.Add(new TextBlock
            {
                Text = $"空闲锁:{info.IdleLockMinutes} 分钟",
                FontSize = 11,
                Foreground = Res.Brush("FgMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(idleRow.Children[0], 0);
            sp.Children.Add(idleRow);

            row.Child = sp;
            ProfilesPanel.Children.Add(row);
        }
    }

    private TextBlock? _statusBlock;
    private void ShowStatus(string msg, bool success)
    {
        if (_statusBlock == null)
        {
            _statusBlock = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            // Place it right after the profiles panel
            ((StackPanel)ProfilesPanel.Parent!).Children.Add(_statusBlock);
        }
        _statusBlock.Text = msg;
        _statusBlock.Foreground = success ? Res.Brush("SuccessBrush") : Res.Brush("DangerBrush");
    }

    private void OnAutoLockChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var minutes = AutoLockBox.SelectedIndex switch
        {
            0 => 5, 1 => 10, 2 => 15, 3 => 30, 4 => 60, _ => 15,
        };
        SettingsStore.AutoLockMinutes = minutes;
    }

    private void OnClipboardChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var seconds = ClipboardBox.SelectedIndex switch
        {
            0 => 4, 1 => 8, 2 => 16, 3 => 30, _ => 8,
        };
        SettingsStore.ClipboardClearSeconds = seconds;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var tag = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrEmpty(tag)) return;
        SettingsStore.Language = tag;
        // v0.3 S6-T6: actually switch the active localizer. All subsequent
        // UIStrings.Get(...) calls return the new locale's strings. The owning
        // MainWindow picks up the change on its next refresh (entries list,
        // detail panel, status bar); the user can also reopen the window for
        // a full re-render of static XAML text.
        if (UIStrings.SetLocale(tag))
        {
            ShowStatus(UIStrings.Fmt("settings.language_changed", tag), success: true);
        }
        else
        {
            ShowStatus($"⚠ Unknown locale: {tag}", success: false);
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var tag = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrEmpty(tag)) return;
        SettingsStore.Theme = tag switch
        {
            "Light" => SettingsStore.AppTheme.Light,
            "Dark" => SettingsStore.AppTheme.Dark,
            _ => SettingsStore.AppTheme.System,
        };
        // v1.1: both light and dark themes are wired via ThemeDictionaries + DynamicResource.
        if (Avalonia.Application.Current is App app)
        {
            app.RequestedThemeVariant = SettingsStore.Theme switch
            {
                SettingsStore.AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
                SettingsStore.AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
        }
        ShowStatus($"✓ 主题已切换为 {tag}", success: true);
    }

    private void OnLockOnSessionLockChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.LockOnSessionLock = LockOnSessionLockBox.IsChecked == true;
    }

    private void OnLockOnSuspendChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.LockOnSuspend = LockOnSuspendBox.IsChecked == true;
    }

    private void OnWatcherEnabledChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.WatcherEnabled = WatcherEnabledBox.IsChecked == true;
        ShowStatus($"✓ 文件监听已{(SettingsStore.WatcherEnabled ? "启用" : "停用")} · 下次解锁时生效", success: true);
    }

    private async void OnBrowseSyncDirClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            Avalonia.Platform.Storage.IStorageFolder? startFolder = null;
            var current = (SyncDirBox.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(current) && System.IO.Directory.Exists(current))
            {
                try { startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(new System.Uri(current)); } catch { }
            }
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择同步目录(云盘 / 共享文件夹)",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
            });
            if (folders.Count > 0)
            {
                SyncDirBox.Text = folders[0].Path.LocalPath;
                SettingsStore.SyncDirectory = SyncDirBox.Text;
                ShowStatus("✓ 同步目录已设置", success: true);
            }
        }
        catch (Exception ex)
        {
            ShowStatus("✕ " + ex.Message, success: false);
        }
    }

    private void OnWebDavEnabledChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.WebDavEnabled = WebDavEnabledBox.IsChecked == true;
        ShowStatus($"✓ WebDAV 同步已{(SettingsStore.WebDavEnabled ? "启用" : "停用")} · 点击「保存配置」持久化", success: true);
    }

    private void OnWebDavAutoSyncChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.WebDavAutoSync = WebDavAutoSyncBox.IsChecked == true;
    }

    private async void OnTestWebDavClick(object? sender, RoutedEventArgs e)
    {
        // Save current form values to settings before testing
        SaveWebDavFormToSettings();
        var url = (WebDavUrlBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(url))
        {
            WebDavStatusText.IsVisible = true;
            WebDavStatusText.Text = "✕ 请先填写服务器地址";
            WebDavStatusText.Foreground = Res.Brush("DangerBrush");
            return;
        }
        WebDavStatusText.IsVisible = true;
        WebDavStatusText.Text = "正在测试连接…";
        WebDavStatusText.Foreground = Res.Brush("FgMutedBrush");
        try
        {
            var config = new RemoteSyncConfig
            {
                ServerUrl = url,
                Username = (WebDavUserBox.Text ?? "").Trim(),
                Password = WebDavPassBox.Text ?? "",
                RemoteFilePath = string.IsNullOrWhiteSpace(WebDavPathBox.Text) ? "vault.okv" : WebDavPathBox.Text.Trim(),
                Enabled = true,
            };
            using var provider = new WebDavSyncProvider(config);
            var error = await provider.TestConnectionAsync();
            if (error == null)
            {
                WebDavStatusText.Text = "✓ 连接成功!服务器可访问。";
                WebDavStatusText.Foreground = Res.Brush("SuccessBrush");
            }
            else
            {
                WebDavStatusText.Text = "✕ " + error;
                WebDavStatusText.Foreground = Res.Brush("DangerBrush");
            }
        }
        catch (Exception ex)
        {
            WebDavStatusText.Text = "✕ 测试失败:" + ex.Message;
            WebDavStatusText.Foreground = Res.Brush("DangerBrush");
        }
    }

    private void OnSaveWebDavClick(object? sender, RoutedEventArgs e)
    {
        SaveWebDavFormToSettings();
        SettingsStore.Save();
        WebDavStatusText.IsVisible = true;
        WebDavStatusText.Text = "✓ WebDAV 配置已保存";
        WebDavStatusText.Foreground = Res.Brush("SuccessBrush");
    }

    private void SaveWebDavFormToSettings()
    {
        SettingsStore.WebDavServerUrl = (WebDavUrlBox.Text ?? "").Trim();
        SettingsStore.WebDavUsername = (WebDavUserBox.Text ?? "").Trim();
        SettingsStore.WebDavPassword = WebDavPassBox.Text ?? "";
        SettingsStore.WebDavRemoteFilePath = string.IsNullOrWhiteSpace(WebDavPathBox.Text)
            ? "vault.okv" : WebDavPathBox.Text.Trim();
        SettingsStore.WebDavEnabled = WebDavEnabledBox.IsChecked == true;
        SettingsStore.WebDavAutoSync = WebDavAutoSyncBox.IsChecked == true;
    }

    private void OnChangePasswordClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ShowInfo("请先解锁金库");
            return;
        }
        ShowChangePasswordDialog();
    }

    /// <summary>v0.2: in-window change-password flow. Asks for current + new
    /// password, then calls <c>VaultService.ChangePasswordAsync</c>.</summary>
    private async void ShowChangePasswordDialog()
    {
        var dlg = new Window
        {
            Title = "修改主密码",
            Width = 420, Height = 320,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = "修改主密码",
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
        });
        sp.Children.Add(new TextBlock
        {
            Text = "修改后,所有现有的 .okv 备份和同步远端会立即失效,需要重新同步。",
            FontSize = 11,
            Foreground = Res.Brush("FgDimBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        sp.Children.Add(new TextBlock { Text = "当前主密码", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var oldBox = new TextBox { Classes = { "field" }, PasswordChar = '●' };
        sp.Children.Add(oldBox);
        sp.Children.Add(new TextBlock { Text = "新主密码 (≥ 8 字符)", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var newBox = new TextBox { Classes = { "field" }, PasswordChar = '●' };
        sp.Children.Add(newBox);
        sp.Children.Add(new TextBlock { Text = "确认新密码", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var confirmBox = new TextBox { Classes = { "field" }, PasswordChar = '●' };
        sp.Children.Add(confirmBox);
        var status = new TextBlock { FontSize = 11, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        sp.Children.Add(status);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        cancel.Click += (_, _) => dlg.Close();
        var ok = new Button { Content = "修改", Classes = { "primary" }, Padding = new Thickness(14, 6), IsEnabled = false };
        ok.Click += async (_, _) =>
        {
            try
            {
                var oldPw = System.Text.Encoding.UTF8.GetBytes(oldBox.Text ?? "");
                var newPw = System.Text.Encoding.UTF8.GetBytes(newBox.Text ?? "");
                var confirmPw = System.Text.Encoding.UTF8.GetBytes(confirmBox.Text ?? "");
                if (newPw.Length < 8)
                {
                    status.Text = "✕ 新密码至少 8 字符";
                    status.Foreground = Res.Brush("DangerBrush");
                    status.IsVisible = true;
                    return;
                }
                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(newPw, confirmPw))
                {
                    status.Text = "✕ 两次输入的新密码不一致";
                    status.Foreground = Res.Brush("DangerBrush");
                    status.IsVisible = true;
                    return;
                }
                ok.IsEnabled = false;
                cancel.IsEnabled = false;
                status.Text = "正在重新派生密钥...";
                status.Foreground = Res.Brush("InfoBrush");
                status.IsVisible = true;
                await _container.Vault.ChangePasswordAsync(oldPw, newPw);
                status.Text = "✓ 主密码已修改。提示:请将 .okv 重新同步到所有其他设备。";
                status.Foreground = Res.Brush("SuccessBrush");
                await System.Threading.Tasks.Task.Delay(1500);
                dlg.Close();
            }
            catch (Exception ex)
            {
                status.Text = "✕ " + ex.Message;
                status.Foreground = Res.Brush("DangerBrush");
                ok.IsEnabled = true;
                cancel.IsEnabled = true;
            }
        };
        // Enable OK only when all three fields have content
        void UpdateOk() { ok.IsEnabled = !string.IsNullOrEmpty(oldBox.Text) && !string.IsNullOrEmpty(newBox.Text) && !string.IsNullOrEmpty(confirmBox.Text); }
        oldBox.TextChanged += (_, _) => UpdateOk();
        newBox.TextChanged += (_, _) => UpdateOk();
        confirmBox.TextChanged += (_, _) => UpdateOk();
        row.Children.Add(cancel);
        row.Children.Add(ok);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    private void OnViewRecoveryKeyClick(object? sender, RoutedEventArgs e) =>
        ShowInfo("查看恢复密钥:请使用 CLI: okv vault info");

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // ---- v1.9.1: Update check ----

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "正在检查更新…";
        UpdateStatusText.Foreground = Res.Brush("FgMutedBrush");
        try
        {
            var result = await _container.UpdateChecker.CheckForUpdateAsync();
            if (result.Failed)
            {
                // v2.3.2: Distinguish "check failed" from "no update"
                UpdateStatusText.Text = "✕ 检查失败: " + (result.ErrorMessage ?? "未知错误");
                UpdateStatusText.Foreground = Res.Brush("DangerBrush");
            }
            else if (result.HasUpdate && result.Info != null)
            {
                var info = result.Info;
                UpdateStatusText.Text = $"📦 发现新版本 {info.TagName}";
                UpdateStatusText.Foreground = Res.Brush("AccentBrush");
                // Show update details dialog
                await ShowUpdateDialog(info);
            }
            else
            {
                UpdateStatusText.Text = $"✓ 已是最新版本 (v{OmniKeyVault.Application.UpdateService.CurrentVersion.ToString(3)})";
                UpdateStatusText.Foreground = Res.Brush("SuccessBrush");
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "✕ 检查失败: " + ex.Message;
            UpdateStatusText.Foreground = Res.Brush("DangerBrush");
        }
    }

    private async System.Threading.Tasks.Task ShowUpdateDialog(OmniKeyVault.Application.UpdateInfo info)
    {
        var dlg = new Window
        {
            Title = $"发现新版本 {info.TagName}",
            Width = 520, Height = 500,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = $"📦 {info.Name}",
            FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (info.PublishedAt.HasValue)
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"发布时间: {info.PublishedAt.Value:yyyy-MM-dd HH:mm}",
                FontSize = 11, Foreground = Res.Brush("FgDimBrush"),
            });
        }
        sp.Children.Add(new Border { BorderBrush = Res.Brush("BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0) });
        var bodyScroll = new ScrollViewer { MaxHeight = 200, Margin = new Thickness(0, 0, 0, 0) };
        bodyScroll.Content = new TextBlock
        {
            Text = string.IsNullOrEmpty(info.Body) ? "(无更新说明)" : info.Body,
            FontSize = 11, Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        sp.Children.Add(bodyScroll);

        // v2.2.0: Direct download + auto-install (no browser needed)
        var installerAsset = OmniKeyVault.Application.UpdateService.FindInstallerAsset(info);
        var progressText = new TextBlock
        {
            FontSize = 11,
            Foreground = Res.Brush("FgMutedBrush"),
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };
        sp.Children.Add(progressText);

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsVisible = false,
            Height = 20,
        };
        sp.Children.Add(progressBar);

        // Buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        var openBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(14, 6), Content = new TextBlock { Text = "查看发布页", FontSize = 12 } };
        openBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true }); } catch { }
        };
        btnRow.Children.Add(openBtn);

        Button? downloadBtn = null;
        var closeBtn = new Button { Classes = { "primary" }, Padding = new Thickness(14, 6), Content = new TextBlock { Text = "关闭", FontSize = 12 } };
        closeBtn.Click += (_, _) => dlg.Close();

        if (installerAsset != null)
        {
            downloadBtn = new Button { Classes = { "primary" }, Padding = new Thickness(14, 6) };
            var sizeMb = installerAsset.Size / 1024.0 / 1024.0;
            downloadBtn.Content = new TextBlock { Text = $"⬇ 下载并安装 ({sizeMb:F1} MB)", FontSize = 12 };

            downloadBtn.Click += async (_, _) =>
            {
                await DownloadAndInstallAsync(_container.UpdateChecker, installerAsset, progressBar, progressText, downloadBtn, openBtn, closeBtn, dlg);
            };
            btnRow.Children.Add(downloadBtn);
        }
        else
        {
            // No installer asset found — show manual download links
            if (info.Assets.Count > 0)
            {
                sp.Children.Add(new TextBlock { Text = "下载:", FontSize = 11, Foreground = Res.Brush("FgDimBrush") });
                foreach (var asset in info.Assets)
                {
                    var linkBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 4) };
                    var assetMb = asset.Size / 1024.0 / 1024.0;
                    linkBtn.Content = new TextBlock
                    {
                        Text = $"⬇ {asset.Name} ({assetMb:F1} MB)",
                        FontSize = 11, Foreground = Res.Brush("AccentBrush"),
                    };
                    var url = asset.DownloadUrl;
                    linkBtn.Click += (_, _) =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                    };
                    sp.Children.Add(linkBtn);
                }
            }
        }

        btnRow.Children.Add(closeBtn);
        sp.Children.Add(btnRow);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    /// <summary>v2.2.0: Downloads the installer with a progress bar, then
    /// launches it in silent mode and exits the app so files can be replaced.</summary>
    private static async System.Threading.Tasks.Task DownloadAndInstallAsync(
        OmniKeyVault.Application.UpdateService updateService,
        OmniKeyVault.Application.UpdateAsset asset,
        ProgressBar progressBar,
        TextBlock progressText,
        Button downloadBtn,
        Button openBtn,
        Button closeBtn,
        Window owner)
    {
        downloadBtn.IsEnabled = false;
        openBtn.IsEnabled = false;
        closeBtn.IsEnabled = false;
        progressBar.IsVisible = true;
        progressText.IsVisible = true;
        progressText.Text = "正在下载更新…";
        progressText.Foreground = Res.Brush("AccentBrush");

        try
        {
            var progress = new Progress<OmniKeyVault.Application.DownloadProgress>(p =>
            {
                progressBar.Value = p.Percentage;
                progressText.Text = $"正在下载… {p.ReceivedMb} / {p.TotalMb} MB ({p.Percentage:F0}%)";
            });

            var downloadedPath = await updateService.DownloadAssetAsync(asset, progress);

            progressBar.Value = 100;
            progressText.Text = "✓ 下载完成，正在启动安装程序…";
            progressText.Foreground = Res.Brush("SuccessBrush");

            // Brief delay so the user sees "download complete" before the UAC prompt
            await System.Threading.Tasks.Task.Delay(800);

            // Launch the installer (triggers UAC elevation) — the installer
            // will close the running app and replace files automatically.
            OmniKeyVault.Application.UpdateService.LaunchInstaller(downloadedPath);

            // Exit the application so the installer can replace files.
            // The installer's [Run] section launches the updated app after install.
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                System.Environment.Exit(0);
            }
        }
        catch (OperationCanceledException)
        {
            progressBar.IsVisible = false;
            progressText.Text = "✕ 下载已取消";
            progressText.Foreground = Res.Brush("DangerBrush");
            downloadBtn.IsEnabled = true;
            openBtn.IsEnabled = true;
            closeBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            progressBar.IsVisible = false;
            progressText.Text = "✕ 下载失败: " + ex.Message + "\n您仍可通过「查看发布页」手动下载。";
            progressText.Foreground = Res.Brush("DangerBrush");
            downloadBtn.IsEnabled = true;
            openBtn.IsEnabled = true;
            closeBtn.IsEnabled = true;
        }
    }

    private void OnAutoCheckUpdateChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.AutoCheckUpdateOnStartup = AutoCheckUpdateBox.IsChecked == true;
        SettingsStore.Save();
    }

    // ---- v1.9.1: Auto-start ----

    private void OnAutoStartChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var enabled = AutoStartBox.IsChecked == true;
        var ok = enabled
            ? OmniKeyVault.Application.AutoStartService.EnableAutoStart()
            : OmniKeyVault.Application.AutoStartService.DisableAutoStart();
        SettingsStore.AutoStartEnabled = enabled && ok;
        SettingsStore.Save();
        ShowStatus(ok
            ? $"✓ 自启动已{(enabled ? "启用" : "关闭")}"
            : "✕ 设置自启动失败（权限不足）", success: ok);
    }

    private void OnMinimizeToTrayChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.MinimizeToTrayOnClose = MinimizeToTrayBox.IsChecked == true;
        SettingsStore.Save();
    }

    // ---- v1.9: Browser extension API ----

    private void UpdateBrowserApiTokenDisplay()
    {
        if (SettingsStore.BrowserApiEnabled && _container.BrowserApi.IsRunning)
        {
            BrowserApiTokenText.Text = _container.BrowserApi.AuthToken;
        }
        else
        {
            BrowserApiTokenText.Text = "(未启用 — 勾选上方开关来启动 API)";
        }
    }

    private void OnBrowserApiEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var enabled = BrowserApiEnabledBox.IsChecked == true;
        SettingsStore.BrowserApiEnabled = enabled;
        SettingsStore.Save();
        if (enabled)
        {
            try
            {
                _container.BrowserApi.Start(SettingsStore.BrowserApiPort);
                UpdateBrowserApiTokenDisplay();
                ShowBrowserApiStatus($"✓ API 已启动 · 监听 127.0.0.1:{SettingsStore.BrowserApiPort}", success: true);
            }
            catch (Exception ex)
            {
                ShowBrowserApiStatus("✕ 启动失败: " + ex.Message, success: false);
            }
        }
        else
        {
            _container.BrowserApi.Stop();
            UpdateBrowserApiTokenDisplay();
            ShowBrowserApiStatus("API 已停止", success: true);
        }
    }

    private async void OnCopyTokenClick(object? sender, RoutedEventArgs e)
    {
        if (!_container.BrowserApi.IsRunning)
        {
            ShowBrowserApiStatus("✕ 请先启用浏览器扩展 API", success: false);
            return;
        }
        var token = _container.BrowserApi.AuthToken;
        try
        {
            // v2.3.4: Use Avalonia's OS clipboard directly instead of the
            // in-memory ClipboardProvider (which never touched the real clipboard).
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(token);
                ShowBrowserApiStatus("✓ 令牌已复制到剪贴板（将在 8 秒后自动清空）", success: true);

                // Auto-clear after 8 seconds
                _ = Task.Delay(8000).ContinueWith(async _ =>
                {
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            if (clipboard != null) await clipboard.ClearAsync();
                        });
                    }
                    catch { }
                });
            }
            else
            {
                ShowBrowserApiStatus("✕ 无法访问剪贴板", success: false);
            }
        }
        catch (Exception ex)
        {
            ShowBrowserApiStatus("✕ 复制失败: " + ex.Message, success: false);
        }
    }

    private void OnRegenerateTokenClick(object? sender, RoutedEventArgs e)
    {
        _container.BrowserApi.RegenerateToken();
        UpdateBrowserApiTokenDisplay();
        ShowBrowserApiStatus("✓ 令牌已重新生成 · 请在浏览器扩展中重新配对", success: true);
    }

    private void ShowBrowserApiStatus(string msg, bool success)
    {
        BrowserApiStatusText.IsVisible = true;
        BrowserApiStatusText.Text = msg;
        BrowserApiStatusText.Foreground = success ? Res.Brush("SuccessBrush") : Res.Brush("DangerBrush");
    }

    private void ShowInfo(string msg)
    {
        var dlg = new Window
        {
            Title = "提示",
            Width = 380, Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var panel = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = msg,
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var ok = new Button
        {
            Content = "好的",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Background = Res.Brush("AccentBrush"),
            Foreground = Res.Brush("AccentFgBrush"),
            Padding = new Avalonia.Thickness(14, 6),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(4),
        };
        ok.Click += (_, _) => dlg.Close();
        panel.Children.Add(ok);
        dlg.Content = panel;
        dlg.ShowDialog(this);
    }

    // ---- v2.0: S3 sync settings ----

    private void OnS3EnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.S3Enabled = S3EnabledBox.IsChecked == true;
        ShowStatus($"✓ S3 同步已{(SettingsStore.S3Enabled ? "启用" : "停用")} · 点击「保存 S3 配置」持久化", success: true);
    }

    private void OnSaveS3Click(object? sender, RoutedEventArgs e)
    {
        SettingsStore.S3Endpoint = (S3EndpointBox.Text ?? "").Trim();
        SettingsStore.S3Bucket = (S3BucketBox.Text ?? "").Trim();
        SettingsStore.S3AccessKey = (S3AccessKeyBox.Text ?? "").Trim();
        SettingsStore.S3SecretKey = S3SecretKeyBox.Text ?? "";
        SettingsStore.S3Region = string.IsNullOrWhiteSpace(S3RegionBox.Text) ? "us-east-1" : S3RegionBox.Text.Trim();
        SettingsStore.S3Enabled = S3EnabledBox.IsChecked == true;
        SettingsStore.Save();
        S3StatusText.IsVisible = true;
        S3StatusText.Text = "✓ S3 配置已保存";
        S3StatusText.Foreground = Res.Brush("SuccessBrush");
    }

    // ---- v2.0: Advanced settings ----

    private void OnAutoSyncChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.AutoSyncOnChange = AutoSyncOnChangeBox.IsChecked == true;
        SettingsStore.Save();
    }

    private void OnSystemNotificationsChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.SystemNotificationsEnabled = SystemNotificationsBox.IsChecked == true;
        SettingsStore.Save();
    }

    private void OnAutoArchiveDaysChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var tag = (AutoArchiveDaysBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        SettingsStore.AutoArchiveDays = int.TryParse(tag, out var days) ? days : 30;
        SettingsStore.Save();
    }

    // ---- v2.3: Accessibility settings ----

    private void OnFontSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var tag = (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        SettingsStore.FontSizeScale = tag ?? "medium";
        SettingsStore.Save();
    }

    private void OnListDensityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var tag = (ListDensityBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        SettingsStore.ListDensity = tag ?? "standard";
        SettingsStore.Save();
    }

    private void OnHighContrastChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SettingsStore.HighContrastMode = HighContrastBox.IsChecked == true;
        SettingsStore.Save();
    }
}
