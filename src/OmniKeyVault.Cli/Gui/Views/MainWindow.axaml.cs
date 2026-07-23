using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Threading;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using OmniKeyVault.Cli.Gui.Views;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Main 3-column window: sidebar (profile / folders / tags) | entry list | detail panel.
/// Hosts the unlocked <see cref="CliContainer"/>; closing the window does NOT zero
/// the keys (the lock action is the only thing that zeros them per SEC INV-04).
/// </summary>
public partial class MainWindow : Window
{
    private readonly CliContainer _container;
    private string _activeProfile = "prod";
    private Entry? _selectedEntry;
    private System.Timers.Timer? _lockCountdownTimer;
    private int _lockMinutesLeft = 15;
    private System.Timers.Timer? _clipboardClearTimer;
    // v0.4 S7-T2: per-second idle timer (more precise than the 1-minute
    // lock countdown above). Fires IdleTimeoutReached when the user has
    // been idle for SettingsStore.AutoLockMinutes; subscribed from
    // StartLockCountdown to call OnLockClick.
    private IdleTimer? _idleTimer;
    // P4-T10: Single shared DispatcherTimer for all TOTP fields, instead of
    // per-field System.Timers.Timer that accumulates handlers.
    private DispatcherTimer? _totpTimer;
    private readonly List<Action> _totpRefreshActions = new();
    // v0.2 gap-fill: folder filter for the entry list. "all" = all entries;
    // "__none__" = entries with Folder == null; otherwise the folder's Guid.
    private string _activeFolderFilter = "all";
    // §2.3: Search debounce timer — 250ms delay before filtering entries.
    private DispatcherTimer? _searchDebounceTimer;
    // v2.0: Filter mode — "all" (default), "favorites", "recent".
    private string _filterMode = "all";

    /// <summary>Emitted when the user locks the vault. Host (GuiShell) handles window swap.</summary>
    public event EventHandler? Locked;

    public MainWindow(CliContainer container, string initialProfile = "prod")
    {
        InitializeComponent();
        _container = container;
        _activeProfile = initialProfile;

        // v1.9.1: If MinimizeToTrayOnClose is enabled, intercept the window
        // close button to minimize to tray instead of quitting.
        Closing += OnMainWindowClosing;
        DeviceIdText.Text = container.DeviceId;

        // v2.3.5: Bridge the in-memory ClipboardProvider to the real OS clipboard.
        // Uses Win32Clipboard (direct Win32 API) instead of Avalonia's OLE clipboard
        // to avoid "CoInitialize has not been called" errors.
        if (container.Clipboard is Infrastructure.ClipboardProvider cp)
        {
            cp.OsCopyAction = text => Win32Clipboard.SetText(text);
            cp.OsClearAction = () => Win32Clipboard.Clear();
        }

        StartLockCountdown();
        StartWatcherIfEnabled();
        StartSystemEventsIfEnabled();
        AutoSyncWebDavIfEnabled();
        RefreshProfileAndEntries();
        ApplyProfileChrome();

        // Toast on successful unlock (per UI_UX_SPEC §7 + docs/UI app.js unlock flow)
        var count = 0;
        try { if (_container.Vault.IsUnlocked) count = _container.Entries.List(_activeProfile, null, null, null).Count; } catch { }
        var msg = count > 0
            ? $"保险库已解锁 · 已加载 {count} 个条目"
            : "保险库已解锁";
        ToastService.Show(ToastContainer, msg, ToastType.Success);

        // v1.8: Entry expiration reminder — check all profiles for entries
        // that have ExpiresAt set and are expired or expiring within 7 days.
        // Shows a warning toast listing the affected entries so the user
        // can rotate their credentials before they stop working.
        CheckExpiringEntries();

        // v1.8: Audit log — record unlock event
        _container.AuditLog.LogUnlock(_container.Vault.CurrentVaultPath ?? "");

        // v2.0: Restore window position, register shortcuts, auto-archive
        RestoreWindowPosition();
        RegisterKeyboardShortcuts();
        AutoArchiveExpiredEntries();
        EnableDragDrop();
        StartPeriodicLeakCheck();

        // v2.3.7: Register global hotkey (Ctrl+Shift+V by default) to bring
        // the app to the foreground from any application.
        RegisterGlobalHotkey();

        // v2.0: Initialize S3 sync from settings
        if (SettingsStore.S3Enabled)
        {
            _container.S3Sync.Endpoint = SettingsStore.S3Endpoint;
            _container.S3Sync.Bucket = SettingsStore.S3Bucket;
            _container.S3Sync.AccessKey = SettingsStore.S3AccessKey;
            _container.S3Sync.SecretKey = SettingsStore.S3SecretKey;
            _container.S3Sync.Region = SettingsStore.S3Region ?? "us-east-1";
        }

        // v2.3: Initialize UX optimization features
        InitializeV3Features();
    }

    /// <summary>v1.9.1: If MinimizeToTrayOnClose is enabled, intercept the
    /// close button to hide the window and show tray icon instead of quitting.</summary>
    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Don't intercept if the app is explicitly quitting (tray → 退出)
        if (IsQuitting?.Invoke() == true) return;

        if (SettingsStore.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            this.Hide();
            // Notify GuiShell to ensure tray icon is visible
            RequestMinimizeToTray?.Invoke();
        }
    }

    /// <summary>v1.9.1: Callback invoked when the user closes the window
    /// with MinimizeToTrayOnClose enabled. GuiShell subscribes to create
    /// the tray icon and prevent process exit.</summary>
    public Action? RequestMinimizeToTray { get; set; }

    /// <summary>v1.9.1: Returns true if the app is explicitly quitting
    /// (e.g. user clicked "退出" in the tray menu). When true, the closing
    /// handler should NOT cancel the close.</summary>
    public Func<bool>? IsQuitting { get; set; }

    /// <summary>v1.8: Checks all profiles for entries with ExpiresAt set.
    /// Shows a warning toast for entries that are expired or expiring within 7 days.
    /// Covers all profiles (not just the active one) so the user is aware of
    /// expiring credentials across their entire vault.</summary>
    private void CheckExpiringEntries()
    {
        try
        {
            if (!_container.Vault.IsUnlocked) return;
            var now = DateTimeOffset.UtcNow;
            var threshold = now.AddDays(7);
            var expired = new List<(string Profile, string Name, DateTimeOffset ExpiresAt)>();
            var expiring = new List<(string Profile, string Name, DateTimeOffset ExpiresAt)>();

            foreach (var profileName in _container.Vault.ListProfileNames())
            {
                try
                {
                    var entries = _container.Entries.List(profileName, null, null, null);
                    foreach (var entry in entries)
                    {
                        if (!entry.ExpiresAt.HasValue) continue;
                        var expires = entry.ExpiresAt.Value;
                        if (expires <= now)
                            expired.Add((profileName, entry.Name, expires));
                        else if (expires <= threshold)
                            expiring.Add((profileName, entry.Name, expires));
                    }
                }
                catch { /* skip unreadable profiles */ }
            }

            if (expired.Count > 0)
            {
                var names = string.Join(", ", expired.Take(5).Select(e => e.Name));
                var suffix = expired.Count > 5 ? $" 等 {expired.Count} 个" : "";
                ToastService.Show(ToastContainer,
                    $"⚠ {expired.Count} 个条目已过期:{names}{suffix} · 请尽快轮换",
                    ToastType.Warning);
            }
            else if (expiring.Count > 0)
            {
                var names = string.Join(", ", expiring.Take(5).Select(e => $"{e.Name}({e.ExpiresAt.LocalDateTime:MM-dd})"));
                var suffix = expiring.Count > 5 ? $" 等 {expiring.Count} 个" : "";
                ToastService.Show(ToastContainer,
                    $"⏰ {expiring.Count} 个条目将在 7 天内过期:{names}{suffix}",
                    ToastType.Info);
            }
        }
        catch { /* best-effort: don't crash on expiry check */ }
    }

    /// <summary>v0.2 S4-T1: start the <see cref="IWatcherProvider"/> on the
    /// configured sync directory (or the vault file's parent directory as a
    /// fallback). On any <c>FileChanged</c> event we show a toast prompting
    /// the user to re-sync; the actual sync still goes through the explicit
    /// "Sync" button (or <c>Ctrl+R</c>) to avoid surprising the user with
    /// overwrites.</summary>
    private void StartWatcherIfEnabled()
    {
        if (!SettingsStore.WatcherEnabled) return;
        if (_container.Vault.CurrentVaultPath == null) return;

        // Priority: explicit SyncDirectory, else the vault's parent dir.
        var dir = !string.IsNullOrEmpty(SettingsStore.SyncDirectory)
            ? SettingsStore.SyncDirectory
            : System.IO.Path.GetDirectoryName(_container.Vault.CurrentVaultPath);

        if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;
        if (!_container.StartWatching(dir)) return;

        _container.Watcher.FileChanged += OnVaultFileChanged;
        // §3.2: Subscribe to sync errors so the user gets a toast notification
        _container.Sync.SyncError += OnSyncError;
    }

    private void OnVaultFileChanged(object? sender, string fullPath)
    {
        // Bounce onto the UI thread (FileSystemWatcher fires on a worker).
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var name = System.IO.Path.GetFileName(fullPath);
            if (!name.EndsWith(".okv", StringComparison.OrdinalIgnoreCase)) return;
            LastSyncText.Text = "远端有更新";
            ToastService.Show(ToastContainer,
                $"检测到 {name} 变化 · 点击「同步」查看最新",
                ToastType.Info);
        });
    }

    /// <summary>§3.2: Handle sync errors from SyncService — show a toast to the user.</summary>
    private void OnSyncError(string message, Exception ex)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ToastService.Show(ToastContainer, message, ToastType.Error);
        });
    }

    /// <summary>v0.2 S7-T1 / MANUAL §12.5: subscribe to OS session-lock + suspend
    /// events and lock the vault immediately when they fire. Honors the
    /// <see cref="SettingsStore.LockOnSessionLock"/> and
    /// <see cref="SettingsStore.LockOnSuspend"/> toggles.</summary>
    private void StartSystemEventsIfEnabled()
    {
        _container.SystemEvents.SessionLocked += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!SettingsStore.LockOnSessionLock) return;
                if (_container.Vault.IsUnlocked) OnLockClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            });
        _container.SystemEvents.SystemSuspending += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!SettingsStore.LockOnSuspend) return;
                if (_container.Vault.IsUnlocked) OnLockClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            });
        _container.SystemEvents.Start();
    }

    // ============================================================
    //  Profile + entry list
    // ============================================================

    private void RefreshProfileAndEntries()
    {
        StatusProfileText.Text = _activeProfile;
        var all = SafeListEntries(_activeProfile, tag: null, platform: null, search: null);
        var filtered = ApplyFolderFilter(all);
        // Apply search on the already-filtered set. Delegates to the
        // v0.3 SearchService so the quick-filter uses the same query engine
        // (and field-level syntax) as the advanced SearchWindow.
        var search = SearchBox.Text;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = _container.Search.SearchEntries(search, filtered);
        }
        // v2.0: Apply favorites/recent filter
        if (_filterMode == "favorites")
        {
            filtered = filtered.Where(e => SettingsStore.FavoriteEntries.Contains(e.Id.ToString())).ToList();
            ListTitle.Text = "⭐ 收藏夹";
        }
        else if (_filterMode == "recent")
        {
            var recentIds = SettingsStore.RecentEntries;
            filtered = filtered
                .Where(e => recentIds.Contains(e.Id.ToString()))
                .OrderByDescending(e => recentIds.IndexOf(e.Id.ToString()))
                .ToList();
            ListTitle.Text = "🕘 最近使用";
        }

        ProfileCountText.Text = filtered.Count.ToString();
        FolderAllCount.Text = all.Count.ToString();
        FolderNoneCount.Text = all.Count(e => !e.Folder.HasValue).ToString();
        // v2.0: Update favorites + recent counts
        FavoritesCount.Text = all.Count(e => SettingsStore.FavoriteEntries.Contains(e.Id.ToString())).ToString();
        RecentCount.Text = SettingsStore.RecentEntries.Count.ToString();
        RebuildFoldersPanel(all);
        RebuildTagPanel(all);
        RenderEntryList(filtered);
    }

    private IReadOnlyList<Entry> ApplyFolderFilter(IReadOnlyList<Entry> entries)
    {
        return _activeFolderFilter switch
        {
            "all" => entries.ToList(),
            "__none__" => entries.Where(e => !e.Folder.HasValue).ToList(),
            _ => Guid.TryParse(_activeFolderFilter, out var gid)
                ? entries.Where(e => e.Folder == gid).ToList()
                : entries.ToList(),
        };
    }

    /// <summary>v0.2 gap-fill: build a real folder tree from
    /// <see cref="VaultService.ListFolders"/>. Each row is a button that sets
    /// <see cref="_activeFolderFilter"/> to the folder's Guid; the entry list
    /// re-filters automatically. Right-click → rename / delete.</summary>
    private void RebuildFoldersPanel(IReadOnlyList<Entry> all)
    {
        FoldersList.Children.Clear();
        IReadOnlyList<Folder> folders;
        try { folders = _container.Folders.List(_activeProfile); }
        catch { return; }
        var byParent = new Dictionary<Guid, List<Folder>>();
        foreach (var f in folders)
        {
            var key = f.ParentId ?? Guid.Empty;
            if (!byParent.TryGetValue(key, out var list))
            {
                list = new List<Folder>();
                byParent[key] = list;
            }
            list.Add(f);
        }
        foreach (var k in byParent.Keys.ToList())
            byParent[k] = byParent[k].OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        if (byParent.TryGetValue(Guid.Empty, out var roots))
        {
            foreach (var f in roots)
            {
                FoldersList.Children.Add(BuildFolderButton(f, all, depth: 0));
                if (byParent.TryGetValue(f.Id, out var kids))
                    foreach (var c in kids) FoldersList.Children.Add(BuildFolderButton(c, all, depth: 1));
            }
        }
    }

    private Button BuildFolderButton(Folder folder, IReadOnlyList<Entry> all, int depth)
    {
        var count = all.Count(e => e.Folder == folder.Id);
        var btn = new Button
        {
            Classes = { "sidebar-item" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Tag = folder.Id.ToString(),
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(depth > 0 ? $"Auto,*,Auto,Auto" : "Auto,*,Auto,Auto"),
        };
        if (depth > 0)
        {
            grid.Children.Add(new Border { Width = depth * 14 });
            Grid.SetColumn(grid.Children[^1], 0);
        }
        int col = depth > 0 ? 1 : 0;
        grid.Children.Add(new TextBlock { Text = "📁", FontSize = 12, Foreground = Res.Brush("FgDimBrush") });
        Grid.SetColumn(grid.Children[^1], col);
        grid.Children.Add(new TextBlock
        {
            Text = folder.Name,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
            FontSize = 13,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(grid.Children[^1], col + 1);
        grid.Children.Add(new TextBlock
        {
            Text = count.ToString(),
            FontFamily = Res.Font("FontMono"),
            FontSize = 11,
            Foreground = Res.Brush("FgFaintBrush"),
            Margin = new Avalonia.Thickness(0, 0, 6, 0),
        });
        Grid.SetColumn(grid.Children[^1], col + 2);
        grid.Children.Add(new TextBlock { Text = "⋯", FontSize = 12, Foreground = Res.Brush("FgDimBrush") });
        Grid.SetColumn(grid.Children[^1], col + 3);
        btn.Content = grid;
        btn.Click += (_, _) =>
        {
            _activeFolderFilter = folder.Id.ToString();
            ListTitle.Text = folder.Name;
            RefreshProfileAndEntries();
        };
        // Right-click context menu
        var menu = new Avalonia.Controls.ContextMenu();
        var renameItem = new Avalonia.Controls.MenuItem { Header = "重命名" };
        renameItem.Click += (_, _) => RenameFolderInteractive(folder);
        var deleteItem = new Avalonia.Controls.MenuItem { Header = "🗑 删除" };
        deleteItem.Click += (_, _) => DeleteFolderInteractive(folder, count);
        menu.Items.Add(renameItem);
        menu.Items.Add(new Avalonia.Controls.Separator());
        menu.Items.Add(deleteItem);
        btn.ContextMenu = menu;
        return btn;
    }

    private void OnAddFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        var nameBox = new TextBox { Watermark = "文件夹名称" };
        var dlg = new Window
        {
            Title = "新建文件夹",
            Width = 360, Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = "新建文件夹", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Res.Brush("FgBrush") });
        sp.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Avalonia.Thickness(14, 6) };
        cancel.Click += (_, _) => dlg.Close();
        var create = new Button { Content = "创建", Classes = { "primary" }, Padding = new Avalonia.Thickness(14, 6) };
        create.Click += async (_, _) =>
        {
            var name = (nameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                _container.Folders.Create(_activeProfile, name);
                await _container.Vault.SaveAsync();
                dlg.Close();
                ToastService.Show(ToastContainer, $"已创建文件夹「{name}」", ToastType.Success);
                RefreshProfileAndEntries();
            }
            catch (Exception ex)
            {
                ToastService.Show(ToastContainer, "创建失败:" + ex.Message, ToastType.Error);
            }
        };
        row.Children.Add(cancel);
        row.Children.Add(create);
        sp.Children.Add(row);
        dlg.Content = sp;
        dlg.ShowDialog(this);
    }

    private void RenameFolderInteractive(Folder folder)
    {
        var nameBox = new TextBox { Text = folder.Name, Watermark = "新名称" };
        var dlg = new Window
        {
            Title = "重命名文件夹",
            Width = 360, Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = $"重命名「{folder.Name}」", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Res.Brush("FgBrush") });
        sp.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Avalonia.Thickness(14, 6) };
        cancel.Click += (_, _) => dlg.Close();
        var save = new Button { Content = "保存", Classes = { "primary" }, Padding = new Avalonia.Thickness(14, 6) };
        save.Click += async (_, _) =>
        {
            var name = (nameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                _container.Folders.Rename(_activeProfile, folder.Id, name);
                await _container.Vault.SaveAsync();
                dlg.Close();
                ToastService.Show(ToastContainer, "已重命名", ToastType.Success);
                RefreshProfileAndEntries();
            }
            catch (Exception ex)
            {
                ToastService.Show(ToastContainer, "重命名失败:" + ex.Message, ToastType.Error);
            }
        };
        row.Children.Add(cancel);
        row.Children.Add(save);
        sp.Children.Add(row);
        dlg.Content = sp;
        dlg.ShowDialog(this);
    }

    private async void DeleteFolderInteractive(Folder folder, int entryCount)
    {
        if (entryCount > 0)
        {
            var dlg = new Window
            {
                Title = "确认删除",
                Width = 380, Height = 160,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res.Brush("BgCardBrush"),
            };
            var sp = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
            sp.Children.Add(new TextBlock
            {
                Text = $"文件夹「{folder.Name}」含 {entryCount} 个条目。删除后条目将移至「未分类」,子文件夹一并删除。",
                FontSize = 12, Foreground = Res.Brush("FgMutedBrush"), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Padding = new Avalonia.Thickness(14, 6) };
            cancel.Click += (_, _) => dlg.Close();
            var del = new Button { Content = "删除", Classes = { "primary" }, Background = Res.Brush("DangerBrush"), Foreground = Res.Brush("AccentFgBrush"), Padding = new Avalonia.Thickness(14, 6) };
            bool ok = false;
            del.Click += (_, _) => { ok = true; dlg.Close(); };
            row.Children.Add(cancel);
            row.Children.Add(del);
            sp.Children.Add(row);
            dlg.Content = sp;
            await dlg.ShowDialog(this);
            if (!ok) return;
        }
        try
        {
            _container.Folders.Delete(_activeProfile, folder.Id);
            await _container.Vault.SaveAsync();
            // Reset filter to "all" if we were filtering on this folder
            if (_activeFolderFilter == folder.Id.ToString())
            {
                _activeFolderFilter = "all";
                ListTitle.Text = "全部条目";
            }
            ToastService.Show(ToastContainer, "已删除文件夹", ToastType.Success);
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "删除失败:" + ex.Message, ToastType.Error);
        }
    }

    private IReadOnlyList<Entry> SafeListEntries(string profile, string? tag, string? platform, string? search)
    {
        try
        {
            if (!_container.Vault.IsUnlocked) return Array.Empty<Entry>();
            return _container.Entries.List(profile, tag, platform, search);
        }
        catch
        {
            return Array.Empty<Entry>();
        }
    }

    private void RebuildTagPanel(IReadOnlyList<Entry> entries)
    {
        TagsPanel.Children.Clear();
        var tags = entries.SelectMany(e => e.Tags).Distinct().OrderBy(t => t).Take(20);
        var mono = Res.Font("FontMono");
        var muted = Res.Brush("FgMutedBrush");
        var sunken = Res.Brush("BgSunkenBrush");
        var border = Res.Brush("BorderBrush");
        foreach (var tag in tags)
        {
            var btn = new Button
            {
                Content = "#" + tag,
                FontFamily = mono,
                FontSize = 11,
                Foreground = muted,
                Background = sunken,
                BorderBrush = border,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(8, 3),
                Margin = new Avalonia.Thickness(0, 0, 4, 4),
            };
            btn.Click += (_, _) =>
            {
                SearchBox.Text = tag;
                RefreshProfileAndEntries();
            };
            TagsPanel.Children.Add(btn);
        }
    }

    private void RenderEntryList(IReadOnlyList<Entry> entries)
    {
        EntryListPanel.Children.Clear();

        if (entries.Count == 0)
        {
            EntryListPanel.IsVisible = false;
            EmptyState.IsVisible = true;
            // v2.3: Use guided empty state when vault is unlocked and empty
            if (_container.Vault.IsUnlocked && string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                RenderGuidedEmptyState();
            }
            else
            {
                EmptyTitle.Text = _container.Vault.IsUnlocked
                    ? (string.IsNullOrWhiteSpace(SearchBox.Text) ? "金库为空" : "没有匹配的条目")
                    : "金库未解锁";
                EmptySub.Text = _container.Vault.IsUnlocked
                    ? (string.IsNullOrWhiteSpace(SearchBox.Text) ? "点击 + 新建条目 开始使用" : "试试清除筛选条件或更换搜索关键词。")
                    : "请运行 okv vault unlock 解锁金库";
            }
        }
        else
        {
            EntryListPanel.IsVisible = true;
            EmptyState.IsVisible = false;
            foreach (var entry in entries)
                EntryListPanel.Children.Add(BuildEntryRow(entry));
        }

        ListCountText.Text = $"{entries.Count} 个条目";
    }

    private Button BuildEntryRow(Entry entry)
    {
        var btn = new Button { Classes = { "entry-row" }, Tag = entry.Id };
        if (_selectedEntry?.Id == entry.Id) btn.Classes.Add("selected");

        var primaryField = entry.Fields.FirstOrDefault(f => f.Sensitive) ?? entry.Fields.FirstOrDefault();
        var primaryValue = primaryField is null ? "" : MaskValue(FieldCodec.Decode(primaryField.Value));

        // v2.3: Use density-aware padding
        var rowPadding = GetEntryRowPadding();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };

        // v2.3: Use entry type icon with colored badge
        var platformMark = new Border
        {
            Width = 32, Height = 32,
            CornerRadius = new Avalonia.CornerRadius(6),
            Background = EntryTypeColor(entry.Type),
            Child = new TextBlock
            {
                Text = EntryTypeIcon(entry.Type),
                FontSize = 16,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(platformMark, 0);

        var main = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(12, 0, 0, 0) };
        main.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 14, FontWeight = Avalonia.Media.FontWeight.Medium,
            Foreground = Res.Brush("FgBrush"),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        });
        if (primaryField != null)
        {
            var fieldRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            var keyTxt = new TextBlock
            {
                Text = primaryField.Key + ":",
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = Res.Brush("FgDimBrush"),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(keyTxt, 0);
            fieldRow.Children.Add(keyTxt);
            var valTxt = new TextBlock
            {
                Text = primaryValue,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = Res.Brush("FgMutedBrush"),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(valTxt, 1);
            fieldRow.Children.Add(valTxt);
            main.Children.Add(fieldRow);
        }
        if (entry.Tags.Count > 0)
        {
            var tagPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 2, 0, 0) };
            foreach (var tag in entry.Tags.Take(4))
            {
                tagPanel.Children.Add(new Border
                {
                    Background = Res.Brush("BgSunkenBrush"),
                    CornerRadius = new Avalonia.CornerRadius(2),
                    Padding = new Avalonia.Thickness(4, 1),
                    Child = new TextBlock
                    {
                        Text = "#" + tag,
                        FontFamily = Res.Font("FontMono"),
                        FontSize = 10,
                        Foreground = Res.Brush("FgDimBrush"),
                    },
                });
            }
            main.Children.Add(tagPanel);
        }
        Grid.SetColumn(main, 1);

        var meta = new StackPanel { Spacing = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        meta.Children.Add(new TextBlock
        {
            Text = entry.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd"),
            FontFamily = Res.Font("FontMono"),
            FontSize = 10,
            Foreground = Res.Brush("FgFaintBrush"),
        });
        // v2.3: Expiry indicator badge
        var expiryLabel = ExpiryLabel(entry);
        if (!string.IsNullOrEmpty(expiryLabel))
        {
            meta.Children.Add(new Border
            {
                Background = ExpiryColor(entry),
                CornerRadius = new Avalonia.CornerRadius(2),
                Padding = new Avalonia.Thickness(4, 1),
                Child = new TextBlock
                {
                    Text = expiryLabel,
                    FontSize = 9,
                    Foreground = Res.Brush("AccentFgBrush"),
                },
            });
        }
        if (primaryField != null)
        {
            var copyBtn = new Button
            {
                Background = Avalonia.Media.Brush.Parse("#146366f1"),
                BorderBrush = Res.Brush("AccentDimBrush"),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(6, 3),
                Content = new TextBlock { Text = "⧉", FontSize = 11, Foreground = Res.Brush("AccentBrightBrush") },
            };
            copyBtn.Click += (_, _) => CopyToClipboard(FieldCodec.Decode(primaryField.Value));
            meta.Children.Add(copyBtn);
        }
        Grid.SetColumn(meta, 2);

        // v2.0: Favorite toggle button
        var favBtn = new Button
        {
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(4),
            Content = new TextBlock
            {
                Text = IsFavorite(entry) ? "⭐" : "☆",
                FontSize = 14,
                Foreground = IsFavorite(entry) ? Res.Brush("WarningBrush") : Res.Brush("FgDimBrush"),
            },
        };
        ToolTip.SetTip(favBtn, IsFavorite(entry) ? "取消收藏" : "添加收藏");
        favBtn.Click += (_, _) => ToggleFavorite(entry);
        Grid.SetColumn(favBtn, 3);

        grid.Children.Add(platformMark);
        grid.Children.Add(main);
        grid.Children.Add(meta);
        grid.Children.Add(favBtn);
        btn.Content = grid;
        btn.Click += (_, _) => SelectEntry(entry);

        // Right-click context menu (per UI_UX_SPEC §4.3.3)
        var flyout = new Avalonia.Controls.MenuFlyout();
        var copyItem = new Avalonia.Controls.MenuItem { Header = "⧉  复制主字段" };
        copyItem.Click += (_, _) =>
        {
            if (primaryField != null) CopyToClipboard(FieldCodec.Decode(primaryField.Value));
        };
        flyout.Items.Add(copyItem);
        var copyAllItem = new Avalonia.Controls.MenuItem { Header = "📋  复制全部字段(JSON)" };
        copyAllItem.Click += (_, _) =>
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(entry.Fields.Select(f => new { f.Key, f.Value, f.Kind, f.Sensitive }));
                Win32Clipboard.SetText(json);
                ToastService.Show(ToastContainer, "已复制全部字段", ToastType.Success);
            }
            catch { }
        };
        flyout.Items.Add(copyAllItem);
        flyout.Items.Add(new Avalonia.Controls.Separator());
        var editItem = new Avalonia.Controls.MenuItem { Header = "✎  编辑" };
        editItem.Click += (_, _) => OnDetailEditClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        flyout.Items.Add(editItem);
        var dupItem = new Avalonia.Controls.MenuItem { Header = "⧉  复制为新条目" };
        dupItem.Click += async (_, _) => await DuplicateEntryAsync(entry);
        flyout.Items.Add(dupItem);
        var historyItem = new Avalonia.Controls.MenuItem { Header = "🕐  查看历史" };
        historyItem.Click += (_, _) => OnViewHistoryClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        flyout.Items.Add(historyItem);
        flyout.Items.Add(new Avalonia.Controls.Separator());
        // v2.0: Favorite toggle
        var favItem = new Avalonia.Controls.MenuItem { Header = IsFavorite(entry) ? "☆  取消收藏" : "⭐  添加收藏" };
        favItem.Click += (_, _) => ToggleFavorite(entry);
        flyout.Items.Add(favItem);
        // v2.0: SSH Agent load (for ssh_key type entries)
        var sshItem = new Avalonia.Controls.MenuItem { Header = "🔑  加载到 SSH Agent" };
        sshItem.Click += (_, _) => OnSshAgentLoadClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        flyout.Items.Add(sshItem);
        // v2.0: Certificate viewer (for certificate type entries)
        var certItem = new Avalonia.Controls.MenuItem { Header = "📜  查看证书详情" };
        certItem.Click += (_, _) => OnViewCertificateClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        flyout.Items.Add(certItem);
        flyout.Items.Add(new Avalonia.Controls.Separator());
        var delItem = new Avalonia.Controls.MenuItem { Header = "🗑  删除" };
        delItem.Click += async (_, _) => await DeleteEntryAsync(entry);
        flyout.Items.Add(delItem);
        btn.ContextFlyout = flyout;
        return btn;
    }

    /// <summary>Duplicate an entry as a new (unlocked-version) entry.</summary>
    private async System.Threading.Tasks.Task DuplicateEntryAsync(Entry entry)
    {
        try
        {
            var copy = entry with
            {
                Id = Guid.NewGuid(),
                Name = entry.Name + " (副本)",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1u,
            };
            _container.Vault.PutEntry(_activeProfile, copy);
            await _container.Vault.SaveAsync();
            // v1.8: Audit log
            _container.AuditLog.LogCreateEntry(_activeProfile, copy.Name);
            _ = _container.AuditLog.FlushAsync();
            ToastService.Show(ToastContainer, "已复制为新条目", ToastType.Success);
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "复制失败:" + ex.Message, ToastType.Error);
        }
    }

    /// <summary>Delete an entry with confirmation dialog.</summary>
    private async System.Threading.Tasks.Task DeleteEntryAsync(Entry entry)
    {
        var dlg = new Window
        {
            Title = "确认删除",
            Width = 360, Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = $"确认删除条目 \"{entry.Name}\" ?",
            FontSize = 13,
            Foreground = Res.Brush("FgMutedBrush"),
        });
        var btnPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        var cancel = new Button
        {
            Content = "取消",
            Background = Res.Brush("BgSunkenBrush"),
            Foreground = Res.Brush("FgMutedBrush"),
            Padding = new Avalonia.Thickness(14, 6),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(4),
        };
        bool confirmed = false;
        cancel.Click += (_, _) => dlg.Close();
        var del = new Button
        {
            Content = "删除",
            Background = Res.Brush("DangerBrush"),
            Foreground = Res.Brush("AccentFgBrush"),
            Padding = new Avalonia.Thickness(14, 6),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(4),
        };
        del.Click += (_, _) => { confirmed = true; dlg.Close(); };
        btnPanel.Children.Add(cancel);
        btnPanel.Children.Add(del);
        sp.Children.Add(btnPanel);
        dlg.Content = sp;
        await dlg.ShowDialog<bool>(this);
        if (!confirmed) return;
        try
        {
            _container.Entries.Delete(_activeProfile, entry.Id);
            await _container.Vault.SaveAsync();
            // v1.8: Audit log
            _container.AuditLog.LogDeleteEntry(_activeProfile, entry.Name);
            _ = _container.AuditLog.FlushAsync();
            _selectedEntry = null;
            DetailContent.IsVisible = false;
            DetailEmpty.IsVisible = true;
            ToastService.Show(ToastContainer, $"已删除 {entry.Name}", ToastType.Success);
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "删除失败:" + ex.Message, ToastType.Error);
        }
    }

    private void SelectEntry(Entry entry)
    {
        _selectedEntry = entry;
        // v2.0: Record as recent entry
        RecordRecentEntry(entry);
        RefreshProfileAndEntries();
        RenderDetail(entry);

        // v2.0: System notification for entry selection
        if (SettingsStore.SystemNotificationsEnabled)
        {
            ShowSystemNotification("条目已选中", entry.Name);
        }
    }

    private void RenderDetail(Entry entry)
    {
        DetailEmpty.IsVisible = false;
        DetailContent.IsVisible = true;
        DetailTitle.Text = entry.Name;
        // v2.3: Enhanced subtitle with type icon and expiry
        var expiryText = ExpiryLabel(entry);
        DetailSubtitle.Text = $"{EntryTypeIcon(entry.Type)} {entry.PlatformId ?? "—"} · {entry.Type}" +
            (string.IsNullOrEmpty(expiryText) ? "" : $" · {expiryText}");

        DetailFieldsPanel.Children.Clear();
        foreach (var f in entry.Fields) DetailFieldsPanel.Children.Add(BuildFieldRow(f));

        // v2.3: Quick actions toolbar at the top of detail panel
        var quickActions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
        };
        var editAction = new Button { Content = "✎ 编辑", Padding = new Avalonia.Thickness(8, 4), FontSize = 11 };
        editAction.Click += (_, _) => OnDetailEditClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        quickActions.Children.Add(editAction);
        var copyAction = new Button { Content = "⧉ 复制主字段", Padding = new Avalonia.Thickness(8, 4), FontSize = 11 };
        copyAction.Click += (_, _) =>
        {
            var secret = entry.Fields.FirstOrDefault(f => f.Sensitive) ?? entry.Fields.FirstOrDefault();
            if (secret != null) CopyToClipboard(FieldCodec.Decode(secret.Value));
        };
        quickActions.Children.Add(copyAction);
        var historyAction = new Button { Content = "🕐 历史", Padding = new Avalonia.Thickness(8, 4), FontSize = 11 };
        historyAction.Click += (_, _) => OnViewHistoryClick(this, new Avalonia.Interactivity.RoutedEventArgs());
        quickActions.Children.Add(historyAction);
        var favAction = new Button
        {
            Content = IsFavorite(entry) ? "⭐ 取消收藏" : "☆ 收藏",
            Padding = new Avalonia.Thickness(8, 4),
            FontSize = 11,
        };
        favAction.Click += (_, _) => ToggleFavorite(entry);
        quickActions.Children.Add(favAction);
        DetailFieldsPanel.Children.Insert(0, quickActions);

        if (!string.IsNullOrEmpty(entry.Notes))
        {
            DetailNotesSection.IsVisible = true;
            DetailNotes.Text = entry.Notes;
        }
        else DetailNotesSection.IsVisible = false;

        MetaCreated.Text = entry.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        MetaUpdated.Text = entry.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        MetaVersion.Text = "v" + entry.Version;
        MetaFolder.Text = entry.Folder?.ToString() ?? "—";
    }

    private Border BuildFieldRow(Field f)
    {
        var row = new Border
        {
            BorderBrush = Res.Brush("BorderBrush"),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Padding = new Avalonia.Thickness(0, 8),
        };
        var sp = new StackPanel { Spacing = 4 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        head.Children.Add(new TextBlock
        {
            Text = f.Key,
            FontFamily = Res.Font("FontMono"),
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        });
        head.Children.Add(new Border
        {
            Background = Avalonia.Media.Brushes.Transparent,
            BorderBrush = f.Sensitive ? Res.Brush("AccentDimBrush") : Res.Brush("BorderBrightBrush"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(2),
            Padding = new Avalonia.Thickness(4, 1),
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            Child = new TextBlock
            {
                Text = FieldKindLabel(f.Kind),
                FontFamily = Res.Font("FontMono"),
                FontSize = 9, LetterSpacing = 0.5,
                Foreground = f.Sensitive ? Res.Brush("AccentBrush") : Res.Brush("FgFaintBrush"),
            },
        });
        Grid.SetColumn(head.Children[1], 1);

        // Reveal-on-hold button (per UI-INT-02 + UI_UX_SPEC §4.4.2)
        TextBlock? valueTextBlock = null;
        if (f.Sensitive)
        {
            var revealBtn = new Button
            {
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Padding = new Avalonia.Thickness(4),
                Content = new TextBlock { Text = "👁", FontSize = 12, Foreground = Res.Brush("FgDimBrush") },
            };
            ToolTip.SetTip(revealBtn, "按住显示明文");
            valueTextBlock = new TextBlock
            {
                Text = MaskValue(FieldCodec.Decode(f.Value)),
                FontFamily = Res.Font("FontMono"),
                FontSize = 12,
                Foreground = Res.Brush("FgMutedBrush"),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            var exposedDot = new Ellipse
            {
                Width = 6, Height = 6,
                Fill = Res.Brush("DangerBrush"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0, 0, 0),
            };
            var stack = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            stack.Children.Add(revealBtn);
            stack.Children.Add(exposedDot);
            Grid.SetColumn(stack, 2);
            head.Children.Add(stack);

            // Pointer handlers on the value text block
            var captured = valueTextBlock;
            var dot = exposedDot;
            var valueRef = FieldCodec.Decode(f.Value);
            void Reveal(object? s, Avalonia.Input.PointerEventArgs e)
            {
                if (captured == null) return;
                captured.Text = valueRef;
                captured.Foreground = Res.Brush("AccentBrightBrush");
                dot.Opacity = 1.0;
            }
            void Mask(object? s, Avalonia.Input.PointerEventArgs e)
            {
                if (captured == null) return;
                captured.Text = MaskValue(valueRef);
                captured.Foreground = Res.Brush("FgMutedBrush");
                dot.Opacity = 0;
            }
            revealBtn.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, Reveal, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            revealBtn.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, Mask, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            revealBtn.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent, Mask, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
        else
        {
            valueTextBlock = new TextBlock
            {
                Text = FieldCodec.Decode(f.Value),
                FontFamily = Res.Font("FontMono"),
                FontSize = 12,
                Foreground = Res.Brush("FgMutedBrush"),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
        }

        var copyBtn = new Button
        {
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(4),
            Content = new TextBlock { Text = "⧉", FontSize = 12, Foreground = Res.Brush("FgDimBrush") },
        };
        ToolTip.SetTip(copyBtn, "复制");
        copyBtn.Click += (_, _) => CopyToClipboard(FieldCodec.Decode(f.Value));
        Grid.SetColumn(copyBtn, 3);
        head.Children.Add(copyBtn);
        sp.Children.Add(head);

        sp.Children.Add(valueTextBlock);

        // TOTP display for totp_uri kind
        if (f.Kind == FieldKind.TotpUri)
        {
            sp.Children.Add(BuildTotpDisplay(FieldCodec.Decode(f.Value)));
        }

        row.Child = sp;
        return row;
    }

    /// <summary>Builds a TOTP code + 30s countdown ring per UI_UX_SPEC §4.4.2.</summary>
    private Border BuildTotpDisplay(string totpUri)
    {
        var border = new Border
        {
            Background = Avalonia.Media.Brush.Parse("#0e6366f1"),
            BorderBrush = Res.Brush("AccentDimBrush"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12, 8),
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
        };
        var codeText = new TextBlock
        {
            Text = "—— —— ——",
            FontFamily = Res.Font("FontMono"),
            FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Medium,
            LetterSpacing = 4,
            Foreground = Res.Brush("AccentBrightBrush"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var ringPanel = new Grid
        {
            Width = 28, Height = 28,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        // SVG-style countdown ring
        var ringCanvas = new Avalonia.Controls.Canvas
        {
            Width = 28, Height = 28,
        };
        var ringBg = new Ellipse
        {
            Width = 24, Height = 24,
            Stroke = Res.Brush("AccentDimBrush"),
            StrokeThickness = 2,
        };
        ringBg.RenderTransform = new Avalonia.Media.TranslateTransform(2, 2);
        ringCanvas.Children.Add(ringBg);
        var ringFg = new Ellipse
        {
            Width = 24, Height = 24,
            Stroke = Res.Brush("AccentBrush"),
            StrokeThickness = 2,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 75.4 },
            StrokeDashOffset = 0,
        };
        ringFg.RenderTransform = new Avalonia.Media.TranslateTransform(2, 2);
        ringCanvas.Children.Add(ringFg);
        var secondsText = new TextBlock
        {
            Text = "30",
            FontFamily = Res.Font("FontMono"),
            FontSize = 11,
            Foreground = Res.Brush("FgMutedBrush"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Width = 28, Height = 28,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };

        // TOTP compute + 1Hz tick
        // P4-T10: Register with the shared DispatcherTimer instead of creating
        // a per-field System.Timers.Timer. All TOTP fields share one timer.
        var uri = totpUri ?? string.Empty;
        void Refresh()
        {
            try
            {
                var (secret, digits) = ParseOtpAuthUri(uri);
                if (secret == null)
                {
                    codeText.Text = "无效 URI";
                    return;
                }
                var now = DateTimeOffset.UtcNow;
                var code = _container.Totp.GenerateCode(secret!, now, digits);
                var remaining = _container.Totp.GetRemainingSeconds(now);
                codeText.Text = FormatCode(code, digits);
                secondsText.Text = remaining.ToString();
                // StrokeDashOffset: 0 = full, 75.4 = empty
                ringFg.StrokeDashOffset = (75.4 * remaining) / 30.0;
            }
            catch { }
        }
        Refresh();
        _totpRefreshActions.Add(Refresh);
        EnsureTotpTimerStarted();

        ringPanel.Children.Add(ringCanvas);
        ringPanel.Children.Add(secondsText);
        Grid.SetColumn(codeText, 0);
        Grid.SetColumn(ringPanel, 1);
        grid.Children.Add(codeText);
        grid.Children.Add(ringPanel);
        border.Child = grid;
        return border;
    }

    private static string FormatCode(string code, int digits) =>
        digits switch
        {
            6 => $"{code.Substring(0, 3)} {code.Substring(3, 3)}",
            8 => $"{code.Substring(0, 4)} {code.Substring(4, 4)}",
            _ => code,
        };

    /// <summary>Parses otpauth:// URI to extract base32 secret + digits.</summary>
    internal static (byte[]? secret, int digits) ParseOtpAuthUri(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            return (null, 6);
        var qIndex = uri.IndexOf('?');
        if (qIndex < 0) return (null, 6);
        var query = uri.Substring(qIndex + 1);
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        string? secret = null;
        int digits = 6;
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = Uri.UnescapeDataString(part.Substring(0, eq));
            var val = Uri.UnescapeDataString(part.Substring(eq + 1));
            if (string.Equals(key, "secret", StringComparison.OrdinalIgnoreCase)) secret = val;
            else if (string.Equals(key, "digits", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(val, out var d)) digits = d;
        }
        if (string.IsNullOrEmpty(secret)) return (null, digits);
        return (Base32Decode(secret), digits);
    }

    private static readonly char[] Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    /// <summary>RFC 4648 base32 decoder (no padding required).</summary>
    internal static byte[] Base32Decode(string input)
    {
        var s = input.ToUpperInvariant().Replace(" ", "").Replace("-", "");
        var bytes = new List<byte>((s.Length * 5) / 8);
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            int v = Array.IndexOf(Base32Alphabet, c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return bytes.ToArray();
    }

    // ============================================================
    //  Topbar / sidebar handlers
    // ============================================================

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        // §2.3: Debounce search input — 250ms delay so we don't filter on every keystroke.
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        // v2.3: Record search history
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            RecordSearchHistory(SearchBox.Text);
        RefreshProfileAndEntries();
        // v2.3: Update search result count
        var all = SafeListEntries(_activeProfile, null, null, null);
        var filtered = ApplyFolderFilter(all);
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            filtered = _container.Search.SearchEntries(SearchBox.Text, filtered);
        UpdateSearchResultCount(filtered.Count, all.Count);
    }

    private async void OnWebDavPullClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        if (_container.WebDavSync == null || !SettingsStore.WebDavEnabled)
        {
            ToastService.Show(ToastContainer, "请先在设置中配置 WebDAV", ToastType.Warning);
            return;
        }
        await PerformWebDavPullAsync();
    }

    private async void OnWebDavPushClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        if (_container.WebDavSync == null || !SettingsStore.WebDavEnabled)
        {
            ToastService.Show(ToastContainer, "请先在设置中配置 WebDAV", ToastType.Warning);
            return;
        }
        await PerformWebDavPushAsync();
    }

    private void OnLocalSyncClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        // Local folder sync - just refresh the list from the vault file
        SyncText.Text = "同步中…(本地文件夹)";
        SyncDot.Fill = Res.Brush("InfoBrush");
        ToastService.Show(ToastContainer, "重新加载金库文件…", ToastType.Info);
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                ReloadAfterSync();
                SyncText.Text = $"刚刚同步 · 来自 {_container.DeviceId}";
                SyncDot.Fill = Res.Brush("SuccessBrush");
                ToastService.Show(ToastContainer, "同步完成", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastService.Show(ToastContainer, "同步失败: " + ex.Message, ToastType.Error);
                SyncText.Text = "同步失败";
                SyncDot.Fill = Res.Brush("DangerBrush");
            }
        }, TimeSpan.FromSeconds(0.5));
    }

    /// <summary>Legacy sync method - performs full WebDAV sync cycle (deprecated).</summary>
    private async void OnSyncClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }

        // If WebDAV is configured, perform remote sync
        if (_container.WebDavSync != null && SettingsStore.WebDavEnabled)
        {
            await PerformWebDavSyncAsync();
            return;
        }

        // Fallback: local folder sync (original behavior)
        OnLocalSyncClick(sender, e);
    }

    /// <summary>Performs a full WebDAV sync cycle: download → merge → upload.</summary>
    private async System.Threading.Tasks.Task PerformWebDavSyncAsync()
    {
        var vaultPath = _container.Vault.CurrentVaultPath;
        if (string.IsNullOrEmpty(vaultPath))
        {
            ToastService.Show(ToastContainer, "金库路径未知", ToastType.Error);
            return;
        }
        SyncText.Text = "WebDAV 同步中…";
        SyncDot.Fill = Res.Brush("InfoBrush");
        ToastService.Show(ToastContainer, "正在与 WebDAV 服务器同步…", ToastType.Info);

        try
        {
            var result = await System.Threading.Tasks.Task.Run(() =>
                _container.WebDavSync!.SyncAsync(vaultPath));

            if (result.SyncResult != null)
            {
                switch (result.SyncResult.Outcome)
                {
                    case SyncOutcome.NoChange:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Info);
                        LastSyncText.Text = "刚刚 · 无变化";
                        break;
                    case SyncOutcome.TookRemote:
                        // Check if this was a UUID mismatch TakeRemote (requires re-unlock)
                        if (result.Message.Contains("重新解锁"))
                        {
                            ToastService.Show(ToastContainer, result.Message, ToastType.Warning);
                            LastSyncText.Text = "需重新解锁";
                        }
                        else
                        {
                            ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                            LastSyncText.Text = "刚刚 · 拉取";
                            ReloadAfterSync();
                        }
                        break;
                    case SyncOutcome.LocalAhead:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                        LastSyncText.Text = "刚刚 · 推送";
                        break;
                    case SyncOutcome.Merged:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                        LastSyncText.Text = $"刚刚 · 合并 {result.SyncResult.EntriesMerged}";
                        ReloadAfterSync();
                        break;
                    case SyncOutcome.FailedRemoteUnreadable:
                        ToastService.Show(ToastContainer, "远端文件损坏: " + result.Message, ToastType.Error);
                        LastSyncText.Text = "远端损坏";
                        break;
                    case SyncOutcome.FailedConflict:
                        ToastService.Show(ToastContainer, "同步冲突: " + result.Message, ToastType.Error);
                        LastSyncText.Text = "冲突";
                        break;
                    case SyncOutcome.RemoteVaultMismatch:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Error);
                        LastSyncText.Text = "金库不匹配";
                        break;
                }

                // If conflicts detected, show the conflict resolver
                if (result.SyncResult.ConflictsDetected > 0)
                {
                    // For WebDAV sync, conflicts are already resolved with local-wins.
                    // Just inform the user.
                    ToastService.Show(ToastContainer,
                        $"{result.SyncResult.ConflictsDetected} 个冲突已按本地优先原则解决",
                        ToastType.Warning);
                }
            }
            else
            {
                // No SyncResult means it was a first-push or error
                if (result.Uploaded)
                {
                    ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                    LastSyncText.Text = "刚刚 · 首次上传";
                }
                else
                {
                    ToastService.Show(ToastContainer, result.Message, ToastType.Error);
                    LastSyncText.Text = "同步失败";
                }
            }
            SyncDot.Fill = Res.Brush("SuccessBrush");
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "WebDAV 同步出错: " + ex.Message, ToastType.Error);
            LastSyncText.Text = "错误";
            SyncDot.Fill = Res.Brush("DangerBrush");
        }
    }

    /// <summary>Performs WebDAV pull: download → merge.</summary>
    private async System.Threading.Tasks.Task PerformWebDavPullAsync()
    {
        var vaultPath = _container.Vault.CurrentVaultPath;
        if (string.IsNullOrEmpty(vaultPath))
        {
            ToastService.Show(ToastContainer, "金库路径未知", ToastType.Error);
            return;
        }
        SyncText.Text = "从云端拉取中…";
        SyncDot.Fill = Res.Brush("InfoBrush");
        ToastService.Show(ToastContainer, "正在从 WebDAV 服务器拉取…", ToastType.Info);

        try
        {
            var result = await System.Threading.Tasks.Task.Run(() =>
                _container.WebDavSync!.PullAsync(vaultPath));

            if (result.SyncResult != null)
            {
                switch (result.SyncResult.Outcome)
                {
                    case SyncOutcome.NoChange:
                    case SyncOutcome.LocalAhead:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Info);
                        LastSyncText.Text = "刚刚 · 无变化";
                        break;
                    case SyncOutcome.TookRemote:
                        // Check if this was a UUID mismatch TakeRemote (requires re-unlock)
                        if (result.Message.Contains("重新解锁"))
                        {
                            ToastService.Show(ToastContainer, result.Message, ToastType.Warning);
                            LastSyncText.Text = "需重新解锁";
                        }
                        else
                        {
                            ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                            LastSyncText.Text = "刚刚 · 拉取";
                            ReloadAfterSync();
                        }
                        break;
                    case SyncOutcome.Merged:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                        LastSyncText.Text = $"刚刚 · 合并 {result.SyncResult.EntriesMerged}";
                        ReloadAfterSync();
                        break;
                    case SyncOutcome.FailedRemoteUnreadable:
                        ToastService.Show(ToastContainer, "远端文件损坏: " + result.Message, ToastType.Error);
                        LastSyncText.Text = "远端损坏";
                        break;
                    case SyncOutcome.FailedConflict:
                        ToastService.Show(ToastContainer, "同步冲突: " + result.Message, ToastType.Error);
                        LastSyncText.Text = "冲突";
                        break;
                    case SyncOutcome.RemoteVaultMismatch:
                        ToastService.Show(ToastContainer, result.Message, ToastType.Error);
                        LastSyncText.Text = "金库不匹配";
                        break;
                }

                if (result.SyncResult.ConflictsDetected > 0)
                {
                    ToastService.Show(ToastContainer,
                        $"{result.SyncResult.ConflictsDetected} 个冲突已按本地优先原则解决",
                        ToastType.Warning);
                }
            }
            else
            {
                ToastService.Show(ToastContainer, result.Message, ToastType.Error);
                LastSyncText.Text = "拉取失败";
            }
            SyncDot.Fill = Res.Brush("SuccessBrush");
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "WebDAV 拉取出错: " + ex.Message, ToastType.Error);
            LastSyncText.Text = "错误";
            SyncDot.Fill = Res.Brush("DangerBrush");
        }
    }

    /// <summary>Performs WebDAV push: upload local vault.</summary>
    private async System.Threading.Tasks.Task PerformWebDavPushAsync()
    {
        var vaultPath = _container.Vault.CurrentVaultPath;
        if (string.IsNullOrEmpty(vaultPath))
        {
            ToastService.Show(ToastContainer, "金库路径未知", ToastType.Error);
            return;
        }
        SyncText.Text = "推送到云端中…";
        SyncDot.Fill = Res.Brush("InfoBrush");
        ToastService.Show(ToastContainer, "正在推送到 WebDAV 服务器…", ToastType.Info);

        try
        {
            var result = await System.Threading.Tasks.Task.Run(() =>
                _container.WebDavSync!.PushAsync(vaultPath));

            if (result.Uploaded)
            {
                ToastService.Show(ToastContainer, result.Message, ToastType.Success);
                LastSyncText.Text = "刚刚 · 推送";
                SyncDot.Fill = Res.Brush("SuccessBrush");
            }
            else
            {
                ToastService.Show(ToastContainer, result.Message, ToastType.Error);
                LastSyncText.Text = "推送失败";
                SyncDot.Fill = Res.Brush("DangerBrush");
            }
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "WebDAV 推送出错: " + ex.Message, ToastType.Error);
            LastSyncText.Text = "错误";
            SyncDot.Fill = Res.Brush("DangerBrush");
        }
    }

    /// <summary>Auto-sync via WebDAV after unlock (if enabled).</summary>
    private async void AutoSyncWebDavIfEnabled()
    {
        if (!SettingsStore.WebDavAutoSync) return;
        if (!SettingsStore.WebDavEnabled) return;
        if (_container.WebDavSync == null) return;
        if (string.IsNullOrEmpty(_container.Vault.CurrentVaultPath)) return;

        // Delay slightly to let the UI settle after unlock
        await System.Threading.Tasks.Task.Delay(1000);
        if (!_container.Vault.IsUnlocked) return;

        try
        {
            var result = await _container.WebDavSync.SyncAsync(_container.Vault.CurrentVaultPath!);
            if (result.SyncResult != null && result.SyncResult.Outcome == SyncOutcome.TookRemote)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ToastService.Show(ToastContainer, "已从云端拉取最新数据", ToastType.Success);
                    LastSyncText.Text = "刚刚 · 云端拉取";
                    _container.AuditLog.LogSync("pull", "success");
                    ReloadAfterSync();
                });
            }
            else if (result.Uploaded && result.SyncResult == null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ToastService.Show(ToastContainer, "已上传金库到云端", ToastType.Info);
                    LastSyncText.Text = "刚刚 · 云端上传";
                    _container.AuditLog.LogSync("push", "auto");
                });
            }
        }
        catch { /* best-effort: don't bother the user on auto-sync failure */ }
    }

    private void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_container);
        dlg.ShowDialog(this);
    }

    /// <summary>v1.8: Open the audit log viewer dialog.</summary>
    private async void OnAuditLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Flush pending entries first so the viewer shows the latest state
        await _container.AuditLog.FlushAsync();

        var dlg = new AuditLogWindow(_container.AuditLog);
        await dlg.ShowDialog(this);
    }

    /// <summary>v1.9: Open the vault switcher dialog.</summary>
    private void OnVaultSwitcherClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var currentPath = _container.Vault.CurrentVaultPath ?? "";
        var dlg = new VaultSwitcherWindow(currentPath);
        dlg.VaultSelected += (_, vaultPath) =>
        {
            // Add to recent vaults and trigger re-unlock
            AddRecentVault(vaultPath);
            // Emit the Locked event so GuiShell shows the unlock window
            // pointing at the new vault path
            GuiShell.SaveLastVaultPath(vaultPath);
            Locked?.Invoke(this, EventArgs.Empty);
        };
        dlg.ShowDialog(this);
    }

    /// <summary>v1.9: Adds a vault path to the recent vaults list (MRU at index 0).</summary>
    private static void AddRecentVault(string vaultPath)
    {
        try
        {
            var list = SettingsStore.RecentVaults
                .Where(p => !string.Equals(p, vaultPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            list.Insert(0, vaultPath);
            SettingsStore.RecentVaults = list.Take(10).ToList();
            SettingsStore.Save();
        }
        catch { /* best-effort */ }
    }

    /// <summary>v0.2 S3-T3: open the SeedExport dialog for the current profile.</summary>
    private void OnExportSeedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        var dlg = new SeedExportWindow(_container);
        dlg.ShowDialog(this);
    }

    /// <summary>v0.2 S3-T4: open the SeedImport dialog.</summary>
    private async void OnImportSeedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        var dlg = new SeedImportWindow(_container);
        if (await dlg.ShowDialog<bool>(this)) RefreshProfileAndEntries();
    }

    /// <summary>v0.2 S4-T6: real sync. Opens a folder picker for the sync target
    /// (a sibling directory with another .okv file, e.g. on a cloud drive).
    /// Then calls SyncService.SyncAsync. If concurrent edits produced
    /// <c>ConflictsDetected &gt; 0</c>, shows the <see cref="SyncConflictResolver"/>
    /// dialog so the user can pick KeepLocal / TakeRemote / Merge per
    /// MANUAL §11.7 + ROADMAP S4-T5.</summary>
    private async void OnSyncNowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null)
            {
                ToastService.Show(ToastContainer, "当前环境不支持文件夹选择", ToastType.Warning);
                return;
            }
            // Default suggestion: a sibling directory named "okv-sync"
            var vaultDir = System.IO.Path.GetDirectoryName(_container.Vault.CurrentVaultPath) ?? "";
            Avalonia.Platform.Storage.IStorageFolder? startFolder = null;
            if (System.IO.Directory.Exists(vaultDir))
                startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(new System.Uri(vaultDir));
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择同步目录(应包含另一个 .okv 文件,例如云盘路径)",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
            });
            if (folders.Count == 0) return;
            var syncDir = folders[0].Path.LocalPath;
            // Find first .okv file in the sync dir
            var remotePath = System.IO.Directory.EnumerateFiles(syncDir, "*.okv").FirstOrDefault();
            if (remotePath == null)
            {
                ToastService.Show(ToastContainer, $"{syncDir} 中没有 .okv 文件", ToastType.Warning);
                return;
            }
            LastSyncText.Text = "同步中…";
            ToastService.Show(ToastContainer, $"正在与 {System.IO.Path.GetFileName(remotePath)} 同步…", ToastType.Info);
            var result = await System.Threading.Tasks.Task.Run(() =>
                _container.Sync.SyncAsync(_container.Vault.CurrentVaultPath, remotePath));

            // 1. Always show the basic outcome as a toast.
            switch (result.Outcome)
            {
                case OmniKeyVault.Application.SyncOutcome.NoChange:
                    ToastService.Show(ToastContainer, "已是最新,无需同步", ToastType.Info);
                    LastSyncText.Text = "刚刚 · 已是最新";
                    break;
                case OmniKeyVault.Application.SyncOutcome.TookRemote:
                    ToastService.Show(ToastContainer, "已从远端拉取更新", ToastType.Success);
                    LastSyncText.Text = "刚刚 · 拉取";
                    ReloadAfterSync();
                    break;
                case OmniKeyVault.Application.SyncOutcome.LocalAhead:
                    ToastService.Show(ToastContainer, "本地版本更新(请把本地文件复制到同步目录)", ToastType.Warning);
                    LastSyncText.Text = "刚刚 · 本地更新";
                    break;
                case OmniKeyVault.Application.SyncOutcome.Merged:
                    ToastService.Show(ToastContainer, $"已合并 {result.EntriesMerged} 个条目", ToastType.Success);
                    LastSyncText.Text = $"刚刚 · 合并 {result.EntriesMerged}";
                    ReloadAfterSync();
                    break;
                case OmniKeyVault.Application.SyncOutcome.FailedRemoteUnreadable:
                    ToastService.Show(ToastContainer, "远端文件损坏或不可读:" + (result.Message ?? "未知"), ToastType.Error);
                    LastSyncText.Text = "远端损坏";
                    break;
                case OmniKeyVault.Application.SyncOutcome.FailedConflict:
                    ToastService.Show(ToastContainer, "同步冲突需手动解决:" + (result.Message ?? "未知"), ToastType.Error);
                    LastSyncText.Text = "冲突";
                    break;
            }

            // 2. v0.2 S4-T5 / MANUAL §11.7: when concurrent edits produced
            // conflicts, surface the wizard. The auto-merge already happened with
            // local-wins; the wizard lets the user override (TakeRemote / re-merge).
            if (result.ConflictsDetected > 0)
            {
                await ShowConflictResolverAsync(_container.Vault.CurrentVaultPath, remotePath, result);
            }
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "同步出错:" + ex.Message, ToastType.Error);
            LastSyncText.Text = "错误";
        }
    }

    /// <summary>v0.2 S4-T5: open the conflict resolution wizard and apply the
    /// user's choice. Local-wins is the default; TakeRemote overwrites the local
    /// vault with the remote file (a <c>.pre-sync-…</c> backup is left behind);
    /// Merge re-applies the local-wins pass and reports the conflict counts.</summary>
    private async System.Threading.Tasks.Task ShowConflictResolverAsync(string localPath, string remotePath, OmniKeyVault.Application.SyncResult initial)
    {
        var dlg = new SyncConflictResolver(initial);
        var tcs = new System.Threading.Tasks.TaskCompletionSource<SyncConflictResolver.Resolution?>();
        dlg.Resolved += (_, resolution) => tcs.TrySetResult(resolution);
        dlg.Closed += (_, _) => tcs.TrySetResult(null);
        await dlg.ShowDialog(this);
        var choice = await tcs.Task;
        if (choice == null) return;  // user closed without choosing

        try
        {
            switch (choice.Value)
            {
                case SyncConflictResolver.Resolution.KeepLocal:
                    // The initial merge already chose local-wins; nothing to do.
                    ToastService.Show(ToastContainer, "已保留本地版本(默认 local-wins)", ToastType.Info);
                    LastSyncText.Text = "刚刚 · 保留本地";
                    break;
                case SyncConflictResolver.Resolution.TakeRemote:
                    // Re-run TakeRemote: copy remote file over local + re-derive.
                    var took = await System.Threading.Tasks.Task.Run(() =>
                        _container.Sync.TakeRemoteAsync(localPath, remotePath));
                    if (took.Outcome == OmniKeyVault.Application.SyncOutcome.TookRemote)
                    {
                        ToastService.Show(ToastContainer, "已采用远端版本(本地已备份)", ToastType.Success);
                        LastSyncText.Text = "刚刚 · 采用远端";
                        // After TakeRemote we must re-unlock to read the new state.
                        ReUnlockFromDisk(localPath);
                    }
                    else
                    {
                        ToastService.Show(ToastContainer, "采用远端失败:" + took.Message, ToastType.Error);
                    }
                    break;
                case SyncConflictResolver.Resolution.Merge:
                    // Re-apply the local-wins merge. Idempotent on the disk state.
                    var merged = await System.Threading.Tasks.Task.Run(() =>
                        _container.Sync.ApplyLocalWinsMergeAsync(localPath, remotePath));
                    ToastService.Show(ToastContainer, $"合并完成 · {merged.EntriesMerged} 项 · {merged.ConflictsDetected} 冲突 (local-wins)",
                        merged.ConflictsDetected == 0 ? ToastType.Success : ToastType.Warning);
                    LastSyncText.Text = $"刚刚 · 合并 {merged.EntriesMerged}";
                    ReloadAfterSync();
                    break;
            }
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "应用解决方案失败:" + ex.Message, ToastType.Error);
        }
    }

    /// <summary>After <c>TakeRemote</c> the in-memory state is stale (the on-disk
    /// file was overwritten). Re-unlock from disk to pick up the new state. If
    /// the user's master password is no longer in scope, fall back to a toast
    /// prompt asking them to lock + unlock again.</summary>
    private void ReUnlockFromDisk(string vaultPath)
    {
        try
        {
            // Read-only: peek at the new manifest to confirm the file changed.
            var manifestPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(vaultPath) ?? ".", "manifest.json");
            if (System.IO.File.Exists(manifestPath))
            {
                ReloadAfterSync();
                ToastService.Show(ToastContainer, "提示:已在主窗口重新加载远端版本;如需写入请先锁定后重新解锁",
                    ToastType.Info);
            }
        }
        catch
        {
            // best-effort — caller will see the next refresh
        }
    }

    private void ReloadAfterSync()
    {
        // The vault state may have changed; rebuild the entry list from the active profile.
        try { RefreshProfileAndEntries(); } catch { /* best-effort */ }
    }

    /// <summary>v0.2 S3-T5: open HistoryWindow for the selected entry (called from context menu).</summary>
    internal void OnViewHistoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedEntry == null) return;
        var dlg = new HistoryWindow(_container, _activeProfile, _selectedEntry);
        dlg.ShowDialog(this);
    }

    private void OnLockClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // v1.8: Audit log — record lock event
        _container.AuditLog.LogLock();
        _ = _container.AuditLog.FlushAsync();

        StopLockCountdown();
        _container.Vault.Lock();
        ToastService.Show(ToastContainer, "保险库已锁定 · 内存已清空", ToastType.Info);
        Locked?.Invoke(this, EventArgs.Empty);
    }

    private void OnProfileChipClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var switcher = new ProfileSwitcherWindow(_container, _activeProfile);
        switcher.ProfileSelected += (_, newProfile) =>
        {
            if (newProfile != _activeProfile)
            {
                var prev = _activeProfile;
                _activeProfile = newProfile;
                ApplyProfileChrome();
                RefreshProfileAndEntries();
                ToastService.Show(ToastContainer, $"已切换到 {newProfile} 配置文件", ToastType.Info);
            }
        };
        switcher.ShowDialog(this);
    }

    private void ApplyProfileChrome()
    {
        ProfileNameText.Text = _activeProfile;
        StatusProfileText.Text = _activeProfile;
        var dot = _activeProfile switch
        {
            "dev" => Res.Brush("ProfileDevBrush"),
            "test" => Res.Brush("ProfileTestBrush"),
            _ => Res.Brush("ProfileProdBrush"),
        };
        StatusProfileDot.Fill = dot;
        ProfileDot.Fill = dot;

        // Banner + watermark (per UI_UX_SPEC §5.3 + docs/UI 05-dev-profile.png)
        if (_activeProfile == "dev" || _activeProfile == "test")
        {
            ProfileBanner.IsVisible = true;
            ProfileBanner.Background = _activeProfile == "test"
                ? Res.Brush("InfoBrush")
                : Res.Brush("WarningBrush");
            BannerText.Text = _activeProfile == "test"
                ? "TEST — 测试数据"
                : "DEV — 非生产数据";
            ProfileWatermark.IsVisible = true;
            ProfileWatermark.Text = _activeProfile.ToUpperInvariant();
            ProfileWatermark.Foreground = _activeProfile == "test"
                ? Res.Brush("InfoBrush")
                : Res.Brush("WarningBrush");
        }
        else
        {
            ProfileBanner.IsVisible = false;
            ProfileWatermark.IsVisible = false;
        }
    }

    private void OnFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        _activeFolderFilter = tag;
        _filterMode = "all"; // Reset filter mode when folder is selected
        ListTitle.Text = tag switch
        {
            "all" => "全部条目",
            "__none__" => "未分类",
            _ => FolderNameById(tag) ?? "文件夹",
        };
        RefreshProfileAndEntries();
    }

    private string? FolderNameById(string id)
    {
        try
        {
            foreach (var f in _container.Folders.List(_activeProfile))
                if (f.Id.ToString() == id) return f.Name;
        }
        catch { }
        return null;
    }

    private void OnSortClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Show a small popup menu: name / updated / created
        var menu = new Avalonia.Controls.ContextMenu();
        var byName = new Avalonia.Controls.MenuItem { Header = "按名称 (A→Z)" };
        byName.Click += (_, _) => ApplySortAndRender(x => x.Name, ascending: true);
        var byUpdated = new Avalonia.Controls.MenuItem { Header = "按更新时间 (新→旧)" };
        byUpdated.Click += (_, _) => ApplySortAndRender(x => x.UpdatedAt, ascending: false);
        var byCreated = new Avalonia.Controls.MenuItem { Header = "按创建时间 (新→旧)" };
        byCreated.Click += (_, _) => ApplySortAndRender(x => x.CreatedAt, ascending: false);
        menu.Items.Add(byName);
        menu.Items.Add(byUpdated);
        menu.Items.Add(byCreated);
        if (sender is Control c) menu.Open(c);
    }

    private void ApplySortAndRender<TKey>(System.Func<Entry, TKey> key, bool ascending)
    {
        var entries = SafeListEntries(_activeProfile, null, null, SearchBox.Text);
        var sorted = ascending ? entries.OrderBy(key).ToList() : entries.OrderByDescending(key).ToList();
        RenderEntryList(sorted);
        var mode = ascending ? "↑" : "↓";
        ToastService.Show(ToastContainer, $"已排序:{sorted.Count} 个条目", ToastType.Info);
    }

    private void OnNewEntryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var editor = new EditorWindow(_container, _activeProfile);
        editor.EntrySaved += (_, entry) =>
        {
            // v1.8: Audit log — record entry create/edit
            _container.AuditLog.LogEditEntry(_activeProfile, entry.Name);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        };
        editor.ShowDialog(this);
    }

    /// <summary>Open file picker → import Bitwarden JSON or okv-dev seed.</summary>
    private async void OnImportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
                Title = "选择导入文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Bitwarden JSON")
                        { Patterns = new[] { "*.json" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("KeePass 2 XML")
                        { Patterns = new[] { "*.xml" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("CSV (LastPass/Chrome/Edge/Firefox/1Password)")
                        { Patterns = new[] { "*.csv" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("OKV Dev seed")
                        { Patterns = new[] { "*.dev" } },
                    new Avalonia.Platform.Storage.FilePickerFileType(".env")
                        { Patterns = new[] { "*.env", ".env", "*.env.*" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("All")
                        { Patterns = new[] { "*" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            int count = 0;
            string format;
            // v0.3 S5-T6: disambiguate .json by inspecting the file head
            // (Bitwarden vs KeePass 2 XML). A Bitwarden file starts with
            // `{ "encrypted": ...`; a KeePass XML starts with `<?xml` or
            // `<KeePassFile`. When in doubt the user gets a clear error
            // and can use the dedicated KeePass Import window.
            if (ext == ".json" || ext == ".xml")
            {
                var head = "";
                try
                {
                    using var fs = System.IO.File.OpenRead(path);
                    var buf = new byte[Math.Min(64, (int)fs.Length)];
                    fs.Read(buf, 0, buf.Length);
                    head = System.Text.Encoding.UTF8.GetString(buf).TrimStart();
                }
                catch { /* fall through */ }
                if (head.StartsWith("<?xml") || head.StartsWith("<KeePassFile", StringComparison.OrdinalIgnoreCase))
                {
                    format = "KeePass 2 XML";
                    var r = await _container.KeePassXml.ImportAsync(_activeProfile, path);
                    count = r.EntriesImported;
                }
                else if (head.StartsWith("{") || head.StartsWith("["))
                {
                    format = "Bitwarden JSON";
                    count = await _container.Bitwarden.ImportAsync(_activeProfile, path);
                }
                else
                {
                    ToastService.Show(ToastContainer, "无法识别 JSON 格式 — 请用「导入 KeePass」或「导入 Bitwarden」菜单项", ToastType.Error);
                    return;
                }
            }
            else if (ext == ".dev")
            {
                format = "okv-dev seed";
                var result = await _container.SeedImport.ImportAsync(path, _activeProfile);
                count = result.EntriesImported;
            }
            else if (ext == ".csv")
            {
                format = "CSV (LastPass/Chrome/1Password)";
                count = await _container.CsvImport.ImportAsync(_activeProfile, path);
            }
            else if (ext == ".env" || System.IO.Path.GetFileName(path).StartsWith(".env"))
            {
                format = ".env";
                count = await _container.EnvFile.ImportAsync(_activeProfile, path, System.IO.Path.GetFileNameWithoutExtension(path));
            }
            else
            {
                ToastService.Show(ToastContainer, "无法识别文件格式 (.json / .xml / .csv / .dev / .env)", ToastType.Error);
                return;
            }
            await _container.Vault.SaveAsync();
            ToastService.Show(ToastContainer, $"已从 {format} 导入 {count} 个条目", ToastType.Success);
            // v1.8: Audit log
            _container.AuditLog.LogImport(_activeProfile, format, count);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, $"导入失败:{ex.Message}", ToastType.Error);
        }
    }

    /// <summary>v0.3 S5-T6: dedicated KeePass 2 XML import dialog. Opened
    /// from the sidebar "Dev Tools" section + a new "导入 KeePass" button.
    /// Uses the same file picker but with a richer flow (profile targeting,
    /// result summary, error details).</summary>
    private void OnImportKeePassClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        var dlg = new KeePassImportWindow(_container, _activeProfile)
        {
            Title = "OmniKey Vault — 从 KeePass 导入",
        };
        dlg.Closed += (_, _) => RefreshProfileAndEntries();
        dlg.Show();
    }

    /// <summary>v0.3 S6-T3: open the advanced search window with field-level
    /// syntax + per-field highlight.</summary>
    private void OnAdvancedSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_container.Vault.IsUnlocked)
        {
            ToastService.Show(ToastContainer, "请先解锁金库", ToastType.Warning);
            return;
        }
        var dlg = new SearchWindow(_container, _activeProfile)
        {
            Title = "OmniKey Vault — 高级搜索",
        };
        dlg.EntryActivated += (_, entry) =>
        {
            _selectedEntry = entry;
            RenderDetail(entry);
            dlg.Close();
        };
        dlg.Show();
    }

    /// <summary>Open save dialog → export current profile to okv-dev seed.</summary>
    private async void OnExportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
            var profile = _activeProfile;
            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出种子文件",
                DefaultExtension = "dev",
                ShowOverwritePrompt = true,
                SuggestedFileName = $"seed.{profile}.{DateTimeOffset.Now:yyyyMMdd}.okv.dev",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("OKV Dev seed")
                        { Patterns = new[] { "*.dev" } },
                },
            });
            if (file == null) return;
            var path = file.Path.LocalPath;

            // Export requires non-prod profile (per CLI convention); auto-fallback
            // for prod with --allow-prod-profile equivalent.
            _container.SeedExport.AllowProdProfile = true;
            _container.SeedExport.StripSecrets = false;
            await _container.SeedExport.ExportAsync(profile, path);
            ToastService.Show(ToastContainer, $"已导出 {profile} 到 {System.IO.Path.GetFileName(path)}", ToastType.Success);
            // v1.8: Audit log
            _container.AuditLog.LogExport(profile, "seed");
            _ = _container.AuditLog.FlushAsync();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, $"导出失败:{ex.Message}", ToastType.Error);
        }
    }

    private void OnDetailEditClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedEntry == null) return;
        var editor = new EditorWindow(_container, _activeProfile, _selectedEntry);
        editor.EntrySaved += (_, entry) =>
        {
            // v1.8: Audit log
            _container.AuditLog.LogEditEntry(_activeProfile, entry.Name);
            _ = _container.AuditLog.FlushAsync();
            RefreshProfileAndEntries();
        };
        editor.ShowDialog(this);
    }

    // ============================================================
    //  Clipboard copy with 8s auto-clear (per UI_UX_SPEC §5.1)
    // ============================================================

private void CopyToClipboard(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        try
        {
            // v2.3.5: Use direct Win32 clipboard API instead of Avalonia's
            // OLE-based clipboard, which requires CoInitialize/OleInitialize
            // and fails with "尚未调用Coinitialize" on some threads.
            Win32Clipboard.SetText(value);
            // v2.3: Enhanced copy feedback with clipboard clear countdown
            ToastService.Show(ToastContainer, $"✓ 已复制 · {SettingsStore.ClipboardClearSeconds} 秒后自动清空", ToastType.Success);

            _clipboardClearTimer?.Stop();
            _clipboardClearTimer = new System.Timers.Timer(SettingsStore.ClipboardClearSeconds * 1000) { AutoReset = false };
            _clipboardClearTimer.Elapsed += (_, _) =>
            {
                try
                {
                    Win32Clipboard.Clear();
                    Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Show(ToastContainer, "剪贴板已清空", ToastType.Info);
                    });
                }
                catch { }
            };
            _clipboardClearTimer.Start();
        }
        catch (Exception ex)
        {
            ToastService.Show(ToastContainer, "剪贴板复制失败:" + ex.Message, ToastType.Error);
        }
    }

    // ============================================================
    //  Lock countdown
    // ============================================================

    private void StartLockCountdown()
    {
        _lockMinutesLeft = SettingsStore.AutoLockMinutes;
        UpdateLockCountdown();
        _lockCountdownTimer?.Stop();
        _lockCountdownTimer = new System.Timers.Timer(60_000) { AutoReset = true };
        _lockCountdownTimer.Elapsed += (_, _) =>
        {
            _lockMinutesLeft--;
            if (_lockMinutesLeft <= 0)
            {
                Dispatcher.UIThread.InvokeAsync(() => OnLockClick(this, new Avalonia.Interactivity.RoutedEventArgs()));
                return;
            }
            Dispatcher.UIThread.InvokeAsync(UpdateLockCountdown);
        };
        _lockCountdownTimer.Start();

        // v0.4 S7-T2: per-second IdleTimer wired to lock on user inactivity.
        // Subscribes to the same OnLockClick path; the coarse 1-minute
        // countdown above is kept for the status-bar display.
        _idleTimer?.Dispose();
        _idleTimer = new IdleTimer(SettingsStore.AutoLockMinutes)
        {
            IdleMinutes = SettingsStore.AutoLockMinutes,
        };
        _idleTimer.IdleTimeoutReached += (_, _) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ToastService.Show(ToastContainer, UIStrings.Get("autolock.warning_fmt").Replace("{0}", "0"), ToastType.Warning);
                OnLockClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            });
        };
        _idleTimer.Start();
        // Hook the window's pointer + key events to reset the idle timer
        // on every user interaction. De-dupe by comparing the last input
        // tick to avoid hammering the lock state.
        PointerMoved += OnAnyUserInput;
        KeyDown += OnAnyUserInput;
    }

    private void OnAnyUserInput(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Called on pointer / key events. We just nudge the IdleTimer;
        // it debounces internally.
        _idleTimer?.RecordActivity();
    }

    private void StopLockCountdown()
    {
        _lockCountdownTimer?.Stop();
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private void UpdateLockCountdown() => LockCountdownText.Text = $"已解锁 · 剩余 {_lockMinutesLeft} 分钟";

    // ============================================================
    //  Helpers
    // ============================================================

    private static string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= 8) return "••••••••";
        return value.Substring(0, 3) + "•••••••••••" + value.Substring(value.Length - 4);
    }

    private static string PlatformInitial(string? platform) =>
        string.IsNullOrEmpty(platform) ? "?" : platform.Substring(0, 1).ToUpperInvariant();

    private Avalonia.Media.IBrush PlatformBrush(string? platform) => platform switch
    {
        "openai" => Avalonia.Media.Brush.Parse("#10a37f"),
        "github" => Avalonia.Media.Brush.Parse("#a89c87"),
        "aws_iam_long_term" or "aws_sts_temporary" => Avalonia.Media.Brush.Parse("#ff9900"),
        "stripe" => Avalonia.Media.Brush.Parse("#635bff"),
        "supabase" => Avalonia.Media.Brush.Parse("#3ecf8e"),
        "anthropic" => Avalonia.Media.Brush.Parse("#d97757"),
        _ => Res.Brush("AccentBrush"),
    };

    private static string FieldKindLabel(FieldKind kind) => kind switch
    {
        FieldKind.Secret => "密文",
        FieldKind.Text => "文本",
        FieldKind.Url => "链接",
        FieldKind.Number => "数字",
        FieldKind.Date => "日期",
        FieldKind.TotpUri => "TOTP",
        FieldKind.FileRef => "文件",
        _ => kind.ToString(),
    };

    // ============================================================
    //  P4-T10: Shared TOTP timer
    // ============================================================

    /// <summary>P4-T10: Lazily starts a single DispatcherTimer that ticks all
    /// registered TOTP refresh actions every second. All TOTP fields share
    /// this timer instead of each creating its own System.Timers.Timer.</summary>
    private void EnsureTotpTimerStarted()
    {
        if (_totpTimer != null) return;
        _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += (_, _) =>
        {
            foreach (var refresh in _totpRefreshActions)
                refresh();
        };
        _totpTimer.Start();
    }

    // ============================================================
    //  v2.0: Sidebar filter handlers (favorites + recent)
    // ============================================================

    private void OnFavoritesFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _filterMode = _filterMode == "favorites" ? "all" : "favorites";
        if (_filterMode == "all") ListTitle.Text = "全部条目";
        RefreshProfileAndEntries();
        ToastService.Show(ToastContainer,
            _filterMode == "favorites" ? "已切换到收藏夹" : "已切换到全部条目", ToastType.Info);
    }

    private void OnRecentFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _filterMode = _filterMode == "recent" ? "all" : "recent";
        if (_filterMode == "all") ListTitle.Text = "全部条目";
        RefreshProfileAndEntries();
        ToastService.Show(ToastContainer,
            _filterMode == "recent" ? "已切换到最近使用" : "已切换到全部条目", ToastType.Info);
    }

    // ============================================================
    //  v2.0: Password generator + leak check sidebar triggers
    // ============================================================

    private void OnShowPasswordGeneratorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowStandalonePasswordGenerator();
    }

    private void OnCheckLeaksClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = CheckCredentialLeaksAsync();
    }

    // ============================================================
    //  P4-T9: Cleanup on close
    // ============================================================

    /// <summary>P4-T9: Override OnClosed to dispose all timers and unsubscribe
    /// all event handlers, preventing memory leaks from dangling references.</summary>
    protected override void OnClosed(EventArgs e)
    {
        // v2.0: Save window position
        SaveWindowPosition();
        // Stop and dispose all timers
        StopLockCountdown();
        _clipboardClearTimer?.Stop();
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;

        // P4-T10: Stop shared TOTP timer
        _totpTimer?.Stop();
        _totpTimer = null;
        _totpRefreshActions.Clear();

        // §2.3: Stop search debounce timer
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = null;

        // Unsubscribe event handlers registered in StartLockCountdown
        PointerMoved -= OnAnyUserInput;
        KeyDown -= OnAnyUserInput;

        // Unsubscribe watcher (StartWatcherIfEnabled)
        if (_container.Watcher != null)
            _container.Watcher.FileChanged -= OnVaultFileChanged;

        // §3.2: Unsubscribe sync error handler
        _container.Sync.SyncError -= OnSyncError;

// Stop and unsubscribe system events (StartSystemEventsIfEnabled)
_container.SystemEvents.Stop();

// v2.3.7: Unregister global hotkey
UnregisterGlobalHotkey();

base.OnClosed(e);
    }
}
