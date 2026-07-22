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

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// v2.3: UX optimization features for MainWindow — sidebar restructure,
/// keyboard navigation, search enhancements, detail panel improvements,
/// inline editing, command palette, notification center, onboarding,
/// accessibility, and more.
/// </summary>
public partial class MainWindow
{
    // ---- v2.3: Sidebar tool group collapse state ----

    private readonly Dictionary<string, bool> _toolGroupCollapsed = new()
    {
        { "import-export", false },
        { "security", false },
        { "batch", true },
        { "sync", true },
        { "other", true },
    };

    private void LoadToolGroupCollapseState()
    {
        if (string.IsNullOrEmpty(SettingsStore.CollapsedToolGroups)) return;
        try
        {
            var parts = SettingsStore.CollapsedToolGroups.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('|');
                if (kv.Length == 2 && _toolGroupCollapsed.ContainsKey(kv[0]))
                    _toolGroupCollapsed[kv[0]] = bool.Parse(kv[1]);
            }
        }
        catch { /* best-effort */ }
    }

    private void SaveToolGroupCollapseState()
    {
        SettingsStore.CollapsedToolGroups = string.Join(";",
            _toolGroupCollapsed.Select(kv => $"{kv.Key}|{kv.Value}"));
        SettingsStore.Save();
    }

    // ---- v2.3: Sidebar width adjustable ----

    private Grid? _mainLayoutGrid;
    private bool _isResizingSidebar;
    private bool _isResizingDetail;

    private void SetupPanelResizers()
    {
        _mainLayoutGrid = this.FindControl<Grid>("MainLayoutGrid");
        if (_mainLayoutGrid == null) return;

        // Apply saved widths
        if (_mainLayoutGrid.ColumnDefinitions.Count >= 3)
        {
            _mainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(SettingsStore.SidebarWidth);
            _mainLayoutGrid.ColumnDefinitions[2].Width = new GridLength(SettingsStore.DetailPanelWidth);
        }

        // Apply detail panel hidden state
        if (SettingsStore.DetailPanelHidden && _mainLayoutGrid.ColumnDefinitions.Count >= 3)
        {
            _mainLayoutGrid.ColumnDefinitions[2].Width = new GridLength(0);
        }
    }

    private void OnSidebarSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mainLayoutGrid == null) return;
        _isResizingSidebar = true;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnSidebarSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingSidebar || _mainLayoutGrid == null) return;
        var pos = e.GetPosition(this);
        var newWidth = System.Math.Clamp(pos.X, 160, 400);
        _mainLayoutGrid.ColumnDefinitions[0].Width = new GridLength(newWidth);
    }

    private void OnSidebarSplitterReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingSidebar || _mainLayoutGrid == null) return;
        _isResizingSidebar = false;
        SettingsStore.SidebarWidth = _mainLayoutGrid.ColumnDefinitions[0].Width.Value;
        SettingsStore.Save();
        e.Pointer.Capture(null);
    }

    private void OnDetailSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mainLayoutGrid == null) return;
        _isResizingDetail = true;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnDetailSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingDetail || _mainLayoutGrid == null) return;
        var pos = e.GetPosition(this);
        var detailWidth = System.Math.Clamp(Bounds.Width - pos.X, 280, 600);
        _mainLayoutGrid.ColumnDefinitions[2].Width = new GridLength(detailWidth);
    }

    private void OnDetailSplitterReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingDetail || _mainLayoutGrid == null) return;
        _isResizingDetail = false;
        SettingsStore.DetailPanelWidth = _mainLayoutGrid.ColumnDefinitions[2].Width.Value;
        SettingsStore.Save();
        e.Pointer.Capture(null);
    }

    /// <summary>v2.3: Toggle detail panel visibility.</summary>
    private void ToggleDetailPanel()
    {
        if (_mainLayoutGrid == null || _mainLayoutGrid.ColumnDefinitions.Count < 3) return;
        SettingsStore.DetailPanelHidden = !SettingsStore.DetailPanelHidden;
        SettingsStore.Save();
        _mainLayoutGrid.ColumnDefinitions[2].Width = SettingsStore.DetailPanelHidden
            ? new GridLength(0)
            : new GridLength(SettingsStore.DetailPanelWidth);
        ToastService.Show(ToastContainer,
            SettingsStore.DetailPanelHidden ? "详情面板已隐藏 — 双击条目重新打开" : "详情面板已显示",
            ToastType.Info);
    }

    // ---- v2.3: Keyboard navigation in entry list ----

    private int _selectedEntryIndex = -1;

    /// <summary>v2.3: Navigate entry list with arrow keys.</summary>
    private void NavigateEntryList(int direction)
    {
        var entries = EntryListPanel.Children.OfType<Button>().ToList();
        if (entries.Count == 0) return;

        _selectedEntryIndex = System.Math.Clamp(
            _selectedEntryIndex + direction, 0, entries.Count - 1);

        // Update visual selection
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].Classes.Remove("selected");
        }
        entries[_selectedEntryIndex].Classes.Add("selected");

        // Scroll into view
        entries[_selectedEntryIndex].BringIntoView();

        // Update detail if the entry has a Tag (entry ID)
        if (entries[_selectedEntryIndex].Tag is Guid entryId)
        {
            var allEntries = SafeListEntries(_activeProfile, null, null, null);
            var entry = allEntries.FirstOrDefault(e => e.Id == entryId);
            if (entry != null)
            {
                _selectedEntry = entry;
                RenderDetail(entry);
            }
        }
    }

    // ---- v2.3: Search history ----

    private void RecordSearchHistory(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        query = query.Trim();
        SettingsStore.SearchHistory.RemoveAll(s => s == query);
        SettingsStore.SearchHistory.Insert(0, query);
        if (SettingsStore.SearchHistory.Count > 10)
            SettingsStore.SearchHistory = SettingsStore.SearchHistory.Take(10).ToList();
        SettingsStore.Save();
    }

    /// <summary>v2.3: Show search history as a flyout below the search box.</summary>
    private void ShowSearchHistoryFlyout()
    {
        if (SettingsStore.SearchHistory.Count == 0) return;

        var flyout = new MenuFlyout();
        foreach (var term in SettingsStore.SearchHistory.Take(10))
        {
            var item = new MenuItem { Header = term };
            var captured = term;
            item.Click += (_, _) =>
            {
                SearchBox.Text = captured;
                SearchBox.Focus();
                SearchBox.CaretIndex = captured.Length;
                RefreshProfileAndEntries();
            };
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "🗑 清除搜索历史" };
        clearItem.Click += (_, _) =>
        {
            SettingsStore.SearchHistory.Clear();
            SettingsStore.Save();
            ToastService.Show(ToastContainer, "搜索历史已清除", ToastType.Info);
        };
        flyout.Items.Add(clearItem);

        flyout.ShowAt(SearchBox);
    }

    // ---- v2.3: Search result count ----

    private void UpdateSearchResultCount(int matched, int total)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            // Hide count when no search
            var countBorder = this.FindControl<Border>("SearchCountBorder");
            if (countBorder != null) countBorder.IsVisible = false;
        }
        else
        {
            var countBorder = this.FindControl<Border>("SearchCountBorder");
            if (countBorder != null)
            {
                countBorder.IsVisible = true;
                var countText = countBorder.Child as TextBlock;
                if (countText != null) countText.Text = $"{matched} / {total}";
            }
        }
    }

    // ---- v2.3: Command palette (Ctrl+Shift+P) ----

    private async void ShowCommandPalette()
    {
        var dlg = new Window
        {
            Title = "命令面板",
            Width = 560, Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
            ShowInTaskbar = false,
        };

        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

        var searchBox = new TextBox
        {
            Watermark = "输入命令名称…",
            FontSize = 14,
            Padding = new Thickness(12, 10),
        };
        sp.Children.Add(searchBox);

        var listBox = new ListBox { Height = 320 };
        sp.Children.Add(listBox);

        var commands = new List<(string Name, string Category, Action Action)>
        {
            ("新建条目", "条目", () => OnNewEntryClick(this, new RoutedEventArgs())),
            ("锁定金库", "安全", () => OnLockClick(this, new RoutedEventArgs())),
            ("同步 (拉取云端)", "同步", () => OnWebDavPullClick(this, new RoutedEventArgs())),
            ("同步 (推送到云端)", "同步", () => OnWebDavPushClick(this, new RoutedEventArgs())),
            ("本地同步", "同步", () => OnSyncClick(this, new RoutedEventArgs())),
            ("密码生成器", "工具", () => ShowStandalonePasswordGenerator()),
            ("密码泄露检测", "安全", () => _ = CheckCredentialLeaksAsync()),
            ("密码强度扫描", "安全", () => OnPasswordStrengthScanClick(this, new RoutedEventArgs())),
            ("导出 Seed", "导入导出", () => OnExportSeedClick(this, new RoutedEventArgs())),
            ("导入 Seed", "导入导出", () => OnImportSeedClick(this, new RoutedEventArgs())),
            ("导入 KeePass (XML)", "导入导出", () => OnImportKeePassClick(this, new RoutedEventArgs())),
            ("导入 KeePass (KDBX)", "导入导出", () => OnImportKdbxClick(this, new RoutedEventArgs())),
            ("导入 1Password (.1pux)", "导入导出", () => OnImport1PuxClick(this, new RoutedEventArgs())),
            ("导入 EnPass (JSON)", "导入导出", () => OnImportEnPassClick(this, new RoutedEventArgs())),
            ("CSV 导入预览", "导入导出", () => OnCsvImportPreviewClick(this, new RoutedEventArgs())),
            ("加密容器导出", "导入导出", () => OnEncryptedExportClick(this, new RoutedEventArgs())),
            ("加密容器导入", "导入导出", () => OnEncryptedImportClick(this, new RoutedEventArgs())),
            ("导出 .env", "导入导出", () => OnEnvExportClick(this, new RoutedEventArgs())),
            ("导入 .env", "导入导出", () => OnEnvImportClick(this, new RoutedEventArgs())),
            ("批量导出 JSON", "导入导出", () => OnBatchExportClick(this, new RoutedEventArgs())),
            ("导出 CSV", "导入导出", () => OnCsvExportClick(this, new RoutedEventArgs())),
            ("导出 Markdown", "导入导出", () => OnMarkdownExportClick(this, new RoutedEventArgs())),
            ("批量编辑标签", "批量操作", () => OnBatchEditClick(this, new RoutedEventArgs())),
            ("批量删除", "批量操作", () => OnBatchDeleteClick(this, new RoutedEventArgs())),
            ("关联条目", "条目", () => OnLinkEntryClick(this, new RoutedEventArgs())),
            ("过期提醒设置", "设置", () => OnCustomExpiryRulesClick(this, new RoutedEventArgs())),
            ("紧急联系人 (Shamir)", "安全", () => OnEmergencyShareClick(this, new RoutedEventArgs())),
            ("创建自定义模板", "工具", () => OnCreateCustomTemplateClick(this, new RoutedEventArgs())),
            ("审计日志", "工具", () => OnAuditLogClick(this, new RoutedEventArgs())),
            ("设置", "设置", () => OnSettingsClick(this, new RoutedEventArgs())),
            ("切换保险箱", "导航", () => OnVaultSwitcherClick(this, new RoutedEventArgs())),
            ("隐藏/显示详情面板", "视图", () => ToggleDetailPanel()),
            ("快捷键速查", "帮助", () => ShowShortcutCheatsheet()),
            ("刷新 (F5)", "视图", () => { RefreshProfileAndEntries(); ToastService.Show(ToastContainer, "已刷新", ToastType.Info); }),
        };

        void PopulateList(string? filter = null)
        {
            listBox.Items.Clear();
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? commands
                : commands.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || c.Category.Contains(filter, StringComparison.OrdinalIgnoreCase));
            foreach (var cmd in filtered)
            {
                var item = new ListBoxItem
                {
                    Content = $"[{cmd.Category}] {cmd.Name}",
                    Tag = cmd.Action,
                };
                listBox.Items.Add(item);
            }
            if (listBox.ItemCount > 0) listBox.SelectedIndex = 0;
        }

        PopulateList();

        searchBox.TextChanged += (_, _) => PopulateList(searchBox.Text);

        searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && listBox.SelectedIndex < listBox.ItemCount - 1)
            {
                listBox.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.Key == Key.Up && listBox.SelectedIndex > 0)
            {
                listBox.SelectedIndex--;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (listBox.SelectedItem is ListBoxItem sel && sel.Tag is Action act)
                {
                    dlg.Close();
                    act();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dlg.Close();
                e.Handled = true;
            }
        };

        listBox.DoubleTapped += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem sel && sel.Tag is Action act)
            {
                dlg.Close();
                act();
            }
        };

        dlg.Content = sp;
        searchBox.Focus();
        await dlg.ShowDialog(this);
    }

    // ---- v2.3: Global search panel (Ctrl+Shift+F) ----

    private async void ShowGlobalSearchPanel()
    {
        var dlg = new Window
        {
            Title = "全局搜索",
            Width = 720, Height = 480,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
            ShowInTaskbar = false,
        };

        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

        var searchBox = new TextBox
        {
            Watermark = "搜索条目名称、字段、标签… (支持 name:xxx field:key:val)",
            FontSize = 14,
            Padding = new Thickness(12, 10),
        };
        sp.Children.Add(searchBox);

        var resultsPanel = new StackPanel { Spacing = 4 };
        var scroll = new ScrollViewer { Content = resultsPanel, MaxHeight = 360 };
        sp.Children.Add(scroll);

        void DoSearch(string? query)
        {
            resultsPanel.Children.Clear();
            if (string.IsNullOrWhiteSpace(query)) return;

            var allProfiles = _container.Vault.ListProfileNames();
            foreach (var profile in allProfiles)
            {
                try
                {
                    var entries = SafeListEntries(profile, null, null, null);
                    var matched = _container.Search.SearchEntries(query, entries);
                    foreach (var entry in matched.Take(50))
                    {
                        var card = new Border
                        {
                            Background = Res.Brush("BgSunkenBrush"),
                            BorderBrush = Res.Brush("BorderBrush"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(12, 8),
                            Margin = new Thickness(0, 0, 0, 4),
                        };
                        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                        var info = new StackPanel { Spacing = 2 };
                        info.Children.Add(new TextBlock
                        {
                            Text = $"[{profile}] {entry.Name}",
                            FontSize = 13,
                            FontWeight = FontWeight.Medium,
                            Foreground = Res.Brush("FgBrush"),
                        });
                        var firstField = entry.Fields.FirstOrDefault();
                        if (firstField != null)
                        {
                            info.Children.Add(new TextBlock
                            {
                                Text = $"{firstField.Key}: {MaskValue(FieldCodec.Decode(firstField.Value))}",
                                FontFamily = Res.Font("FontMono"),
                                FontSize = 11,
                                Foreground = Res.Brush("FgDimBrush"),
                            });
                        }
                        Grid.SetColumn(info, 0);
                        grid.Children.Add(info);

                        var copyBtn = new Button
                        {
                            Content = "⧉ 复制",
                            Padding = new Thickness(8, 4),
                            FontSize = 11,
                        };
                        var capturedEntry = entry;
                        copyBtn.Click += (_, _) =>
                        {
                            var secret = capturedEntry.Fields.FirstOrDefault(f => f.Sensitive) ?? capturedEntry.Fields.FirstOrDefault();
                            if (secret != null) CopyToClipboard(FieldCodec.Decode(secret.Value));
                        };
                        Grid.SetColumn(copyBtn, 1);
                        grid.Children.Add(copyBtn);

                        card.Child = grid;
                        resultsPanel.Children.Add(card);
                    }
                }
                catch { /* skip */ }
            }

            if (resultsPanel.Children.Count == 0)
            {
                resultsPanel.Children.Add(new TextBlock
                {
                    Text = "没有匹配的条目",
                    FontSize = 13,
                    Foreground = Res.Brush("FgMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40),
                });
            }
        }

        var debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        debounceTimer.Tick += (_, _) => { debounceTimer.Stop(); DoSearch(searchBox.Text); };
        searchBox.TextChanged += (_, _) => { debounceTimer.Stop(); debounceTimer.Start(); };

        searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dlg.Close(); e.Handled = true; }
        };

        dlg.Content = sp;
        searchBox.Focus();
        await dlg.ShowDialog(this);
    }

    // ---- v2.3: Shortcut cheatsheet (F1 or ?) ----

    private async void ShowShortcutCheatsheet()
    {
        var dlg = new Window
        {
            Title = "快捷键速查",
            Width = 480, Height = 560,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(24), Spacing = 4 };

        sp.Children.Add(new TextBlock
        {
            Text = "键盘快捷键",
            FontSize = 18, FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
            Margin = new Thickness(0, 0, 0, 16),
        });

        var shortcuts = new[]
        {
            ("Ctrl + N", "新建条目"),
            ("Ctrl + F", "聚焦搜索框"),
            ("Ctrl + L", "锁定金库"),
            ("Ctrl + S", "同步"),
            ("Ctrl + E", "导出"),
            ("Ctrl + I", "导入"),
            ("Ctrl + G", "密码生成器"),
            ("Ctrl + D", "复制选中条目"),
            ("Ctrl + A", "全选条目"),
            ("Ctrl + Shift + C", "凭据泄露检测"),
            ("Ctrl + Shift + P", "命令面板"),
            ("Ctrl + Shift + F", "全局搜索面板"),
            ("F1  /  ?", "快捷键速查"),
            ("F2", "编辑选中条目"),
            ("F5", "刷新"),
            ("↑  /  ↓", "浏览条目列表"),
            ("Enter", "打开选中条目详情"),
            ("Esc", "关闭对话框"),
            ("Delete", "删除选中条目"),
            ("/", "聚焦搜索框"),
        };

        foreach (var (key, desc) in shortcuts)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), Margin = new Thickness(0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = key,
                FontFamily = Res.Font("FontMono"),
                FontSize = 12,
                Foreground = Res.Brush("AccentBrightBrush"),
            });
            var descBlock = new TextBlock
            {
                Text = desc,
                FontSize = 12,
                Foreground = Res.Brush("FgMutedBrush"),
                Margin = new Thickness(0, 0, 0, 0),
            };
            Grid.SetColumn(descBlock, 1);
            row.Children.Add(descBlock);
            sp.Children.Add(row);
        }

        var closeBtn = new Button
        {
            Content = "关闭",
            Classes = { "primary" },
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        closeBtn.Click += (_, _) => dlg.Close();
        sp.Children.Add(closeBtn);

        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.3: Notification center ----

    private int _unreadNotificationCount = 0;

    /// <summary>v2.3: Add a notification to the notification center.</summary>
    private void AddNotification(string title, string message, NotificationLevel level = NotificationLevel.Info)
    {
        SettingsStore.Notifications.Insert(0, new NotificationItem
        {
            Title = title,
            Message = message,
            Level = level,
            Time = DateTimeOffset.Now,
        });
        if (SettingsStore.Notifications.Count > 50)
            SettingsStore.Notifications = SettingsStore.Notifications.Take(50).ToList();
        SettingsStore.Save();
        _unreadNotificationCount++;
        UpdateNotificationBadge();
    }

    private void UpdateNotificationBadge()
    {
        var badge = this.FindControl<Border>("NotificationBadge");
        if (badge == null) return;
        _unreadNotificationCount = SettingsStore.Notifications.Count;
        badge.IsVisible = _unreadNotificationCount > 0;
        if (badge.Child is TextBlock tb)
            tb.Text = _unreadNotificationCount > 99 ? "99+" : _unreadNotificationCount.ToString();
    }

    /// <summary>v2.3: Show the notification center panel.</summary>
    private async void ShowNotificationCenter()
    {
        var dlg = new Window
        {
            Title = "通知中心",
            Width = 460, Height = 520,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = "通知中心",
            FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
        });
        var clearBtn = new Button { Content = "清除全部", Padding = new Thickness(10, 4), FontSize = 11 };
        header.Children.Add(clearBtn);
        Grid.SetColumn(clearBtn, 1);
        sp.Children.Add(header);

        var scrollViewer = new ScrollViewer { MaxHeight = 400 };
        var notifPanel = new StackPanel { Spacing = 6 };

        void RenderNotifications()
        {
            notifPanel.Children.Clear();
            if (SettingsStore.Notifications.Count == 0)
            {
                notifPanel.Children.Add(new TextBlock
                {
                    Text = "暂无通知",
                    FontSize = 13,
                    Foreground = Res.Brush("FgMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40),
                });
                return;
            }

            foreach (var n in SettingsStore.Notifications)
            {
                var (color, icon) = n.Level switch
                {
                    NotificationLevel.Warning => (Res.Brush("WarningBrush"), "⚠"),
                    NotificationLevel.Error => (Res.Brush("DangerBrush"), "✕"),
                    NotificationLevel.Success => (Res.Brush("SuccessBrush"), "✓"),
                    _ => (Res.Brush("InfoBrush"), "i"),
                };

                var card = new Border
                {
                    Background = Res.Brush("BgSunkenBrush"),
                    BorderBrush = Res.Brush("BorderBrush"),
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Padding = new Thickness(12, 8),
                    Margin = new Thickness(0, 0, 0, 4),
                };
                card.BorderThickness = new Thickness(3, 0, 0, 0);
                card.BorderBrush = color;

                var info = new StackPanel { Spacing = 2 };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                titleRow.Children.Add(new TextBlock { Text = icon, FontSize = 12, Foreground = color });
                titleRow.Children.Add(new TextBlock
                {
                    Text = n.Title,
                    FontSize = 12, FontWeight = FontWeight.SemiBold,
                    Foreground = Res.Brush("FgBrush"),
                });
                info.Children.Add(titleRow);
                info.Children.Add(new TextBlock
                {
                    Text = n.Message,
                    FontSize = 11,
                    Foreground = Res.Brush("FgMutedBrush"),
                    TextWrapping = TextWrapping.Wrap,
                });
                info.Children.Add(new TextBlock
                {
                    Text = n.Time.LocalDateTime.ToString("MM-dd HH:mm"),
                    FontFamily = Res.Font("FontMono"),
                    FontSize = 10,
                    Foreground = Res.Brush("FgFaintBrush"),
                });
                card.Child = info;
                notifPanel.Children.Add(card);
            }
        }

        RenderNotifications();
        scrollViewer.Content = notifPanel;
        sp.Children.Add(scrollViewer);

        clearBtn.Click += (_, _) =>
        {
            SettingsStore.Notifications.Clear();
            SettingsStore.Save();
            _unreadNotificationCount = 0;
            UpdateNotificationBadge();
            RenderNotifications();
            ToastService.Show(ToastContainer, "通知已清除", ToastType.Info);
        };

        var closeBtn = new Button
        {
            Content = "关闭",
            Classes = { "primary" },
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeBtn.Click += (_, _) => dlg.Close();
        sp.Children.Add(closeBtn);

        dlg.Content = sp;
        await dlg.ShowDialog(this);

        _unreadNotificationCount = 0;
        UpdateNotificationBadge();
    }

    // ---- v2.3: First-use onboarding guide ----

    private async void ShowFirstUseGuide()
    {
        if (SettingsStore.FirstUseGuideCompleted) return;

        var dlg = new Window
        {
            Title = "欢迎使用 OmniKey Vault",
            Width = 520, Height = 440,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(28), Spacing = 16 };

        sp.Children.Add(new TextBlock
        {
            Text = "🔐 欢迎使用 OmniKey Vault",
            FontSize = 22, FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        sp.Children.Add(new TextBlock
        {
            Text = "让我们用几分钟时间了解核心功能：",
            FontSize = 13,
            Foreground = Res.Brush("FgMutedBrush"),
        });

        var steps = new[]
        {
            ("①", "新建条目", "点击侧边栏「+ 新建条目」按钮，选择平台模板（如 GitHub、OpenAI），填入凭据并保存。"),
            ("②", "搜索与筛选", "使用顶部搜索框搜索条目，按 `/` 快速聚焦。支持 name:github 等字段级语法。"),
            ("③", "配置同步", "在设置中配置 WebDAV 或 S3 同步，实现多设备数据同步。"),
            ("④", "安全设置", "设置自动锁定时间、剪贴板自动清空，确保数据安全。"),
            ("⑤", "快捷键", "按 F1 或 ? 查看所有快捷键，按 Ctrl+Shift+P 打开命令面板快速执行操作。"),
        };

        foreach (var (num, title, desc) in steps)
        {
            var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = num,
                FontSize = 16, FontWeight = FontWeight.Bold,
                Foreground = Res.Brush("AccentBrush"),
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = Res.Brush("FgBrush"),
            });
            row.Children.Add(titleRow);
            row.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                Foreground = Res.Brush("FgMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 0, 0),
            });
            sp.Children.Add(row);
        }

        var startBtn = new Button
        {
            Content = "开始使用 →",
            Classes = { "primary" },
            Padding = new Thickness(20, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        startBtn.Click += (_, _) =>
        {
            SettingsStore.FirstUseGuideCompleted = true;
            SettingsStore.Save();
            dlg.Close();
        };
        sp.Children.Add(startBtn);

        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.3: Security health report ----

    private async void ShowSecurityHealthReport()
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        var dlg = new Window
        {
            Title = "安全健康报告",
            Width = 500, Height = 480,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(24), Spacing = 12 };

        sp.Children.Add(new TextBlock
        {
            Text = "🛡 安全健康报告",
            FontSize = 18, FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
        });

        try
        {
            var totalEntries = 0;
            var weakPasswords = 0;
            var expiredEntries = 0;
            var expiringEntries = 0;
            var totalSecrets = 0;

            var now = DateTimeOffset.UtcNow;
            var threshold = now.AddDays(7);

            foreach (var profileName in _container.Vault.ListProfileNames())
            {
                try
                {
                    var entries = SafeListEntries(profileName, null, null, null);
                    totalEntries += entries.Count;
                    foreach (var entry in entries)
                    {
                        if (entry.ExpiresAt.HasValue)
                        {
                            if (entry.ExpiresAt.Value <= now) expiredEntries++;
                            else if (entry.ExpiresAt.Value <= threshold) expiringEntries++;
                        }
                        foreach (var field in entry.Fields)
                        {
                            if (field.Kind == FieldKind.Secret && !string.IsNullOrEmpty(field.ValueString))
                            {
                                totalSecrets++;
                                var score = PasswordGeneratorService.EstimateStrength(field.ValueString);
                                if (score <= 1) weakPasswords++;
                            }
                        }
                    }
                }
                catch { }
            }

            var lastSync = SettingsStore.WebDavEnabled ? "已配置" : "未配置";

            var items = new[]
            {
                ("条目总数", totalEntries.ToString(), Res.Brush("FgBrush")),
                ("密码总数", totalSecrets.ToString(), Res.Brush("FgBrush")),
                ("弱密码", weakPasswords.ToString(), weakPasswords > 0 ? Res.Brush("DangerBrush") : Res.Brush("SuccessBrush")),
                ("已过期条目", expiredEntries.ToString(), expiredEntries > 0 ? Res.Brush("DangerBrush") : Res.Brush("SuccessBrush")),
                ("即将过期", expiringEntries.ToString(), expiringEntries > 0 ? Res.Brush("WarningBrush") : Res.Brush("SuccessBrush")),
                ("同步状态", lastSync, Res.Brush("InfoBrush")),
            };

            foreach (var (label, value, color) in items)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
                row.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    Foreground = Res.Brush("FgMutedBrush"),
                });
                var valBlock = new TextBlock
                {
                    Text = value,
                    FontSize = 14, FontWeight = FontWeight.SemiBold,
                    Foreground = color,
                };
                Grid.SetColumn(valBlock, 1);
                row.Children.Add(valBlock);
                sp.Children.Add(row);
            }

            // Summary
            var issues = weakPasswords + expiredEntries;
            sp.Children.Add(new Border
            {
                BorderBrush = Res.Brush("BorderBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 8, 0, 0),
            });

            sp.Children.Add(new TextBlock
            {
                Text = issues == 0
                    ? "✓ 安全状况良好，未发现问题"
                    : $"⚠ 发现 {issues} 个需要关注的安全问题",
                FontSize = 14, FontWeight = FontWeight.SemiBold,
                Foreground = issues == 0 ? Res.Brush("SuccessBrush") : Res.Brush("WarningBrush"),
            });
        }
        catch (Exception ex)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "报告生成失败: " + ex.Message,
                FontSize = 12,
                Foreground = Res.Brush("DangerBrush"),
            });
        }

        var closeBtn = new Button
        {
            Content = "关闭",
            Classes = { "primary" },
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        closeBtn.Click += (_, _) => dlg.Close();
        sp.Children.Add(closeBtn);

        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.3: Sync progress + log ----

    private readonly List<string> _syncLogEntries = new();

    /// <summary>v2.3: Show sync progress bar in status bar.</summary>
    private void ShowSyncProgress(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            SyncText.Text = message;
            SyncDot.Fill = Res.Brush("WarningBrush");
        });
    }

    /// <summary>v2.3: Show sync log dialog after sync completes.</summary>
    private async void ShowSyncLogDialog()
    {
        if (_syncLogEntries.Count == 0) return;

        var dlg = new Window
        {
            Title = "同步日志",
            Width = 520, Height = 400,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };

        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        sp.Children.Add(new TextBlock
        {
            Text = "同步日志",
            FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush("FgBrush"),
        });

        var logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = Res.Font("FontMono"),
            FontSize = 11,
            Height = 280,
            TextWrapping = TextWrapping.Wrap,
            Text = string.Join("\n", _syncLogEntries),
        };
        sp.Children.Add(logBox);

        var closeBtn = new Button
        {
            Content = "关闭",
            Classes = { "primary" },
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeBtn.Click += (_, _) => dlg.Close();
        sp.Children.Add(closeBtn);

        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    // ---- v2.3: Entry type visual indicators ----

    /// <summary>v2.3: Returns an icon for the entry type.</summary>
    private static string EntryTypeIcon(EntryType type) => type switch
    {
        EntryType.SshKey => "🔑",
        EntryType.Certificate => "📜",
        EntryType.OAuth => "🔗",
        EntryType.Note => "📝",
        _ => "🔐",
    };

    /// <summary>v2.3: Returns a color brush for the entry type.</summary>
    private static IBrush EntryTypeColor(EntryType type) => type switch
    {
        EntryType.SshKey => Brush.Parse("#8b5cf6"),
        EntryType.Certificate => Brush.Parse("#f59e0b"),
        EntryType.OAuth => Brush.Parse("#3b82f6"),
        EntryType.Note => Brush.Parse("#10b981"),
        _ => Res.Brush("AccentBrush"),
    };

    // ---- v2.3: Expiry visual indicators ----

    /// <summary>v2.3: Returns a color for expiry status.</summary>
    private static IBrush ExpiryColor(Entry? entry)
    {
        if (entry?.ExpiresAt == null) return Res.Brush("FgFaintBrush");
        var now = DateTimeOffset.UtcNow;
        if (entry.ExpiresAt.Value <= now) return Res.Brush("DangerBrush");
        if (entry.ExpiresAt.Value <= now.AddDays(7)) return Res.Brush("WarningBrush");
        return Res.Brush("SuccessBrush");
    }

    /// <summary>v2.3: Returns an expiry label.</summary>
    private static string ExpiryLabel(Entry? entry)
    {
        if (entry?.ExpiresAt == null) return "";
        var now = DateTimeOffset.UtcNow;
        if (entry.ExpiresAt.Value <= now) return "已过期";
        var days = (entry.ExpiresAt.Value - now).TotalDays;
        if (days <= 7) return $"{(int)days}天后过期";
        return "";
    }

    // ---- v2.3: Toast stacking improvement ----

    /// <summary>v2.3: Enhanced toast with stacking support. Overrides the default ToastService
    /// behavior by ensuring multiple toasts stack vertically without overlapping.</summary>
    private void ShowToast(string message, ToastType type = ToastType.Info)
    {
        // Limit max visible toasts to 5 to prevent overflow
        while (ToastContainer.Children.Count >= 5)
        {
            ToastContainer.Children.RemoveAt(0);
        }
        ToastService.Show(ToastContainer, message, type);
    }

    // ---- v2.3: Empty state guided onboarding ----

    /// <summary>v2.3: Render an enhanced empty state with guided actions.</summary>
    private void RenderGuidedEmptyState()
    {
        EmptyTitle.Text = "🗝 欢迎使用 OmniKey Vault";
        EmptySub.Text = "开始你的凭据安全管理之旅";

        // Clear existing children and add guided actions
        EmptyState.Children.Clear();
        EmptyState.Children.Add(new TextBlock
        {
            Text = "🗝",
            FontSize = 48,
            Opacity = 0.3,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        EmptyState.Children.Add(new TextBlock
        {
            Text = "金库为空",
            FontSize = 18, FontWeight = FontWeight.Medium,
            Foreground = Res.Brush("FgMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var actionsSp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var newBtn = new Button
        {
            Content = "+ 新建条目",
            Classes = { "primary-sm" },
        };
        newBtn.Click += (_, _) => OnNewEntryClick(this, new RoutedEventArgs());
        actionsSp.Children.Add(newBtn);

        var importBtn = new Button
        {
            Content = "↓ 导入数据",
            Classes = { "ghost-sm" },
        };
        importBtn.Click += (_, _) => OnImportClick(this, new RoutedEventArgs());
        actionsSp.Children.Add(importBtn);

        var syncBtn = new Button
        {
            Content = "☁ 配置同步",
            Classes = { "ghost-sm" },
        };
        syncBtn.Click += (_, _) => OnSettingsClick(this, new RoutedEventArgs());
        actionsSp.Children.Add(syncBtn);

        EmptyState.Children.Add(actionsSp);

        EmptyState.Children.Add(new TextBlock
        {
            Text = "提示: 按 Ctrl+Shift+P 打开命令面板，按 F1 查看快捷键",
            FontSize = 11,
            Foreground = Res.Brush("FgDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    // ---- v2.3: Apply font size scale ----

    /// <summary>v2.3: Apply font size scale to the window.</summary>
    private void ApplyFontSizeScale()
    {
        var scale = SettingsStore.FontSizeScale switch
        {
            "small" => 0.875,
            "large" => 1.15,
            _ => 1.0,
        };
        // Apply via RenderTransform scale — simplest approach for Avalonia
        if (scale != 1.0)
        {
            this.RenderTransform = new ScaleTransform(scale, scale);
        }
        else
        {
            this.RenderTransform = null;
        }
    }

    // ---- v2.3: Apply list density ----

    /// <summary>v2.3: Get entry row padding based on density setting.</summary>
    private static Thickness GetEntryRowPadding() => SettingsStore.ListDensity switch
    {
        "compact" => new Thickness(12, 4),
        "comfortable" => new Thickness(12, 16),
        _ => new Thickness(12, 10),
    };

    /// <summary>v2.3: Get entry row spacing based on density setting.</summary>
    private static double GetEntryRowSpacing() => SettingsStore.ListDensity switch
    {
        "compact" => 0,
        "comfortable" => 4,
        _ => 2,
    };

    // ---- v2.3: High contrast mode ----

    /// <summary>v2.3: Apply high contrast theme.</summary>
    private void ApplyHighContrastMode()
    {
        if (!SettingsStore.HighContrastMode) return;

        // Override key brushes for high contrast
        var resources = Avalonia.Application.Current?.Resources;
        if (resources == null) return;

        resources["BgBrush"] = new SolidColorBrush(Color.Parse("#000000"));
        resources["BgElevatedBrush"] = new SolidColorBrush(Color.Parse("#1a1a1a"));
        resources["BgSunkenBrush"] = new SolidColorBrush(Color.Parse("#0d0d0d"));
        resources["FgBrush"] = new SolidColorBrush(Color.Parse("#ffffff"));
        resources["FgMutedBrush"] = new SolidColorBrush(Color.Parse("#e0e0e0"));
        resources["FgDimBrush"] = new SolidColorBrush(Color.Parse("#cccccc"));
        resources["FgFaintBrush"] = new SolidColorBrush(Color.Parse("#999999"));
        resources["BorderBrush"] = new SolidColorBrush(Color.Parse("#666666"));
        resources["BorderBrightBrush"] = new SolidColorBrush(Color.Parse("#888888"));
        resources["AccentBrush"] = new SolidColorBrush(Color.Parse("#ffff00"));
        resources["AccentBrightBrush"] = new SolidColorBrush(Color.Parse("#ffff66"));
        resources["WarningBrush"] = new SolidColorBrush(Color.Parse("#ffaa00"));
        resources["DangerBrush"] = new SolidColorBrush(Color.Parse("#ff4444"));
        resources["SuccessBrush"] = new SolidColorBrush(Color.Parse("#00ff00"));
    }

    // ---- v2.3: Initialize all v2.3 features ----

    /// <summary>v2.3: Initialize all UX optimization features.</summary>
    private void InitializeV3Features()
    {
        LoadToolGroupCollapseState();
        SetupPanelResizers();
        ApplyFontSizeScale();
        ApplyHighContrastMode();
        UpdateNotificationBadge();

        // Show first-use guide if not completed
        if (!SettingsStore.FirstUseGuideCompleted)
        {
            Dispatcher.UIThread.Post(() => ShowFirstUseGuide(), DispatcherPriority.Loaded);
        }

        // Add initial notifications for any existing expired entries
        try
        {
            if (_container.Vault.IsUnlocked)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var profileName in _container.Vault.ListProfileNames())
                {
                    try
                    {
                        var entries = SafeListEntries(profileName, null, null, null);
                        foreach (var entry in entries)
                        {
                            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= now)
                            {
                                AddNotification("条目已过期",
                                    $"[{profileName}] {entry.Name} 已于 {entry.ExpiresAt.Value.LocalDateTime:MM-dd} 过期",
                                    NotificationLevel.Warning);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { /* best-effort */ }
    }
}
