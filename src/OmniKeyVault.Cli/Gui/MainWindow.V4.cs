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
/// v2.4.0: Entry management enhancements — drag-to-folder, multi-tab detail view.
/// This partial class extends MainWindow with:
/// - Entry drag-to-folder: drag an entry row onto a sidebar folder to move it
/// - Multi-tab detail view: open multiple entries in tabs for side-by-side comparison
/// </summary>
public partial class MainWindow
{
    // ---- v2.4.0: Drag-to-folder ----

    private Point? _dragStartPoint;
    private Guid? _dragEntryId;

    /// <summary>v2.4.0: Called from BuildEntryRow to attach drag-to-folder handlers.</summary>
    internal void AttachEntryDragHandlers(Button btn, Entry entry)
    {
        btn.PointerPressed += (s, e) =>
        {
            _dragStartPoint = e.GetPosition(this);
            _dragEntryId = entry.Id;
        };

        btn.PointerMoved += async (s, e) =>
        {
            if (_dragStartPoint == null || _dragEntryId == null) return;
            var pos = e.GetPosition(this);
            var diff = _dragStartPoint.Value - pos;
            if (System.Math.Abs(diff.X) > 4 || System.Math.Abs(diff.Y) > 4)
            {
                // Start drag
#pragma warning disable CS0618
                var data = new DataObject();
                data.Set("application/x-okv-entry-id", _dragEntryId.Value.ToString());
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618
                _dragStartPoint = null;
            }
        };

        btn.PointerReleased += (s, e) =>
        {
            _dragStartPoint = null;
        };
    }

    /// <summary>v2.4.0: Called from BuildFolderButton to make it a drop target.</summary>
    internal void AttachFolderDropHandler(Button btn, Folder folder)
    {
        DragDrop.SetAllowDrop(btn, true);
        btn.AddHandler(DragDrop.DragOverEvent, (s, e) =>
        {
#pragma warning disable CS0618
            if (e.Data?.Contains("application/x-okv-entry-id") == true)
            {
                e.DragEffects = DragDropEffects.Move;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
#pragma warning restore CS0618
            e.Handled = true;
        });

        btn.AddHandler(DragDrop.DropEvent, async (s, e) =>
        {
#pragma warning disable CS0618
            if (e.Data?.Contains("application/x-okv-entry-id") != true) return;
            var entryIdStr = e.Data.Get("application/x-okv-entry-id") as string;
#pragma warning restore CS0618
            if (!Guid.TryParse(entryIdStr, out var entryId)) return;

            try
            {
                var entry = _container.Vault.GetEntry(_activeProfile, entryId);
                if (entry == null) return;

                // Move entry to the target folder
                var updated = entry with { Folder = folder.Id, UpdatedAt = DateTimeOffset.UtcNow, Version = entry.Version + 1 };
                _container.Vault.PutEntry(_activeProfile, updated);
                await _container.Vault.SaveAsync();

                ToastService.Show(ToastContainer, $"已将「{entry.Name}」移动到「{folder.Name}」", ToastType.Success);
                RefreshProfileAndEntries();
            }
            catch (Exception ex)
            {
                ToastService.Show(ToastContainer, "移动失败: " + ex.Message, ToastType.Error);
            }
        });
    }

    // ---- v2.4.0: Multi-tab detail view ----

    private readonly List<(Guid EntryId, string Name)> _openTabs = new();
    private Guid? _activeTabEntryId;

    /// <summary>v2.4.0: Open an entry in a new tab (or switch to it if already open).</summary>
    internal void OpenEntryInTab(Entry entry)
    {
        // Check if already open
        var existing = _openTabs.FirstOrDefault(t => t.EntryId == entry.Id);
        if (existing.EntryId != Guid.Empty)
        {
            _activeTabEntryId = entry.Id;
        }
        else
        {
            // Limit to 8 tabs
            if (_openTabs.Count >= 8)
            {
                _openTabs.RemoveAt(0);
            }
            _openTabs.Add((entry.Id, entry.Name));
            _activeTabEntryId = entry.Id;
        }
        RenderTabStrip();
    }

    /// <summary>v2.4.0: Close a tab by entry ID.</summary>
    internal void CloseTab(Guid entryId)
    {
        var idx = _openTabs.FindIndex(t => t.EntryId == entryId);
        if (idx < 0) return;
        _openTabs.RemoveAt(idx);

        if (_activeTabEntryId == entryId)
        {
            _activeTabEntryId = _openTabs.Count > 0 ? _openTabs[^1].EntryId : null;
            if (_activeTabEntryId.HasValue)
            {
                var entry = _container.Vault.GetEntry(_activeProfile, _activeTabEntryId.Value);
                if (entry != null)
                {
                    _selectedEntry = entry;
                    RenderDetail(entry);
                }
            }
        }
        RenderTabStrip();
    }

    /// <summary>v2.4.0: Render the tab strip above the detail panel.</summary>
    private void RenderTabStrip()
    {
        var tabStrip = this.FindControl<StackPanel>("DetailTabStrip");
        if (tabStrip == null) return;

        tabStrip.Children.Clear();
        tabStrip.IsVisible = _openTabs.Count > 1;

        if (_openTabs.Count <= 1) return;

        foreach (var tab in _openTabs)
        {
            var isActive = tab.EntryId == _activeTabEntryId;
            var tabContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            tabContent.Children.Add(new TextBlock
            {
                Text = tab.Name,
                FontSize = 11,
                Foreground = isActive ? Res.Brush("AccentBrightBrush") : Res.Brush("FgMutedBrush"),
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            tabContent.Children.Add(new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Foreground = Res.Brush("FgFaintBrush"),
            });

            var tabBtn = new Button
            {
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 2, 0),
                FontSize = 11,
                Background = isActive ? Res.Brush("BgSunkenBrush") : null,
                Content = tabContent,
                Tag = tab.EntryId,
            };

            var capturedId = tab.EntryId;
            tabBtn.Click += (s, e) =>
            {
                _activeTabEntryId = capturedId;
                var entry = _container.Vault.GetEntry(_activeProfile, capturedId);
                if (entry != null)
                {
                    _selectedEntry = entry;
                    RenderDetail(entry);
                }
                RenderTabStrip();
            };

            // Close on middle-click
            tabBtn.PointerPressed += (s, e) =>
            {
                var props = e.GetCurrentPoint(tabBtn).Properties;
                if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed)
                {
                    CloseTab(capturedId);
                    e.Handled = true;
                }
            };

            tabStrip.Children.Add(tabBtn);
        }
    }
}
