using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Entry editor dialog. Supports both new entry creation (pick a template
/// from the loaded template set) and editing existing entries. Saves via
/// the same Application services the CLI uses, then fires
/// <see cref="EntrySaved"/> so the host can refresh its list.
///
/// v0.2 S3-T6 + gap-fill: adds Folder picker, ExpiresAt date picker,
/// independent PlatformId input, and a per-field kind dropdown
/// (secret / text / url / number / date / totp_uri / file_ref) per
/// MANUAL §4.2.1 + OKV_FORMAT §3.5.
/// </summary>
public partial class EditorWindow : Window
{
    private readonly CliContainer _container;
    private readonly string _profile;
    private readonly Entry? _existing;

    /// <summary>Tracks per-row "was sensitive" so re-toggling the kind dropdown
    /// doesn't accidentally promote a field to secret mid-edit.</summary>
    private const string NoneTag = "— 无 —";

    public event EventHandler? EntrySaved;

    public EditorWindow(CliContainer container, string profile, Entry? existing = null)
    {
        InitializeComponent();
        _container = container;
        _profile = profile;
        _existing = existing;

        EditorTitle.Text = existing == null ? "新建条目" : "编辑条目";

        // Populate template list
        var templates = container.Templates.ListAll().ToList();
        foreach (var t in templates)
        {
            TemplateBox.Items.Add(new ComboBoxItem { Content = t.Id + " — " + t.Name, Tag = t });
        }
        if (TemplateBox.Items.Count > 0) TemplateBox.SelectedIndex = 0;

        // v0.2 S3-T6 + §4.2.1: populate folder dropdown from the active profile.
        // Includes a "— 无 —" sentinel for "no folder". Folder CRUD lives in
        // the main window (tree panel); the editor only consumes the list.
        FolderBox.Items.Add(new ComboBoxItem { Content = NoneTag, Tag = null });
        try
        {
            foreach (var f in container.Folders.List(profile))
                FolderBox.Items.Add(new ComboBoxItem { Content = f.Name, Tag = f.Id });
        }
        catch { /* vault locked = empty list, no-op */ }
        FolderBox.SelectedIndex = 0;

        // v0.4 S8-T4: rotate panel visibility reacts to PlatformId changes.
        // Initial state (new entry: empty box / existing entry: its platform_id)
        // is rendered by the first RefreshRotatePanel() call below.
        PlatformIdBox.TextChanged += (_, _) => RefreshRotatePanel();
        RefreshRotatePanel();

        if (existing != null)
        {
            NameBox.Text = existing.Name;
            for (int i = 0; i < TypeBox.Items.Count; i++)
            {
                if (((ComboBoxItem)TypeBox.Items[i]!).Content?.ToString()?.Equals(existing.Type.ToString(), StringComparison.OrdinalIgnoreCase) == true)
                { TypeBox.SelectedIndex = i; break; }
            }
            PlatformIdBox.Text = existing.PlatformId ?? "";
            TagsBox.Text = string.Join(", ", existing.Tags);
            NotesBox.Text = existing.Notes ?? "";
            if (existing.ExpiresAt.HasValue)
            {
                ExpiresAtPicker.SelectedDate = new DateTimeOffset(existing.ExpiresAt.Value.LocalDateTime, TimeSpan.Zero);
            }
            // Select the existing folder
            if (existing.Folder.HasValue)
            {
                for (int i = 0; i < FolderBox.Items.Count; i++)
                {
                    if (((ComboBoxItem)FolderBox.Items[i]!).Tag is Guid gid && gid == existing.Folder.Value)
                    { FolderBox.SelectedIndex = i; break; }
                }
            }
            foreach (var f in existing.Fields) AddFieldRow(f.Key, f.ValueString, f.Kind, f.Sensitive);
        }
        else if (templates.Count > 0 && TemplateBox.SelectedItem is ComboBoxItem item && item.Tag is TemplateDefinition tpl)
        {
            // Auto-fill PlatformId from template id
            PlatformIdBox.Text = tpl.Id;
            foreach (var f in tpl.Fields) AddFieldRow(f.Key, "", ParseKind(f.Kind), f.Sensitive);
        }
    }

    /// <summary>When the user picks a different template, auto-fill PlatformId
    /// (only if the user hasn't manually edited it). The fields keep their
    /// existing values — re-templating overwrites them only if the user
    /// confirms via a dialog. We keep the v0.1 behavior (no overwrite) for
    /// the initial v0.2 gap-fill; a future iteration can add "apply template".</summary>
    private void OnTemplateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is ComboBoxItem ci && ci.Tag is TemplateDefinition tpl)
        {
            // Only auto-fill PlatformId if the field is empty or matches the previous template
            var current = (PlatformIdBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(current) || _container.Templates.ListAll().Any(t => t.Id == current))
            {
                PlatformIdBox.Text = tpl.Id;
            }
        }
    }

    private void OnClearExpiryClick(object? sender, RoutedEventArgs e)
    {
        ExpiresAtPicker.SelectedDate = null;
    }

    private static FieldKind ParseKind(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return FieldKind.Secret;
        return Enum.TryParse<FieldKind>(raw, ignoreCase: true, out var k) ? k : FieldKind.Secret;
    }

    /// <summary>Adds a new field row. Replaces the v0.1 sensitive-checkbox with
    /// a kind dropdown (secret / text / url / number / date / totp_uri / file_ref)
    /// so the field's <see cref="FieldKind"/> is correct in the stored JSON
    /// (OKV_FORMAT §3.5).</summary>
    private void AddFieldRow(string key = "", string value = "", FieldKind kind = FieldKind.Secret, bool sensitive = true)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,130,*,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var keyBox = new TextBox
        {
            Classes = { "field-input" },
            Text = key,
            Watermark = "字段名",
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(keyBox, 0);

        var kindBox = new ComboBox
        {
            Background = Res.Brush("BgSunkenBrush"),
            Foreground = Res.Brush("FgBrush"),
            BorderBrush = Res.Brush("BorderBrightBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
        };
        foreach (var k in new[] { "secret", "text", "url", "number", "date", "totp_uri", "file_ref" })
        {
            kindBox.Items.Add(new ComboBoxItem { Content = FieldKindLabelText(k), Tag = k });
        }
        // Select the matching kind (default = secret)
        for (int i = 0; i < kindBox.Items.Count; i++)
        {
            if ((kindBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == kind.ToString().ToLowerInvariant())
            { kindBox.SelectedIndex = i; break; }
        }
        if (kindBox.SelectedIndex < 0) kindBox.SelectedIndex = 0;
        Grid.SetColumn(kindBox, 1);

        var valueBox = new TextBox
        {
            Classes = { "field-input" },
            Text = value,
            Watermark = "值",
            FontFamily = Res.Font("FontMono"),
            PasswordChar = sensitive ? '●' : default,
            RevealPassword = false,
        };
        // Auto-toggle the password mask based on the kind
        kindBox.SelectionChanged += (_, _) =>
        {
            var sel = (kindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            valueBox.PasswordChar = sel == "secret" ? '●' : default;
        };
        Grid.SetColumn(valueBox, 2);

        // TOTP: clicking the value box should focus it for URI paste (no extra UI).
        // file_ref: clicking the value box opens a file picker; we keep the text
        // path so users can paste a relative path.
        var pickBtn = new Button
        {
            Classes = { "ghost" },
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            IsVisible = kind == FieldKind.FileRef,
            Content = new TextBlock { Text = "📂", FontSize = 12, Foreground = Res.Brush("FgDimBrush") },
        };
        pickBtn.Click += async (_, _) => await PickFileRefAsync(valueBox);
        Grid.SetColumn(pickBtn, 3);

        var removeBtn = new Button
        {
            Classes = { "ghost" },
            Padding = new Thickness(8, 4),
            Content = new TextBlock { Text = "✕", FontSize = 12, Foreground = Res.Brush("FgDimBrush") },
        };
        removeBtn.Click += (_, _) => (row.Parent as Panel)?.Children.Remove(row);
        Grid.SetColumn(removeBtn, 4);

        // Show/hide pick button as the kind changes
        kindBox.SelectionChanged += (_, _) =>
        {
            var sel = (kindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            pickBtn.IsVisible = sel == "file_ref";
        };

        row.Children.Add(keyBox);
        row.Children.Add(kindBox);
        row.Children.Add(valueBox);
        row.Children.Add(pickBtn);
        row.Children.Add(removeBtn);

        FieldsPanel.Children.Add(row);
    }

    private static string FieldKindLabelText(string kind) => kind switch
    {
        "secret" => "🔒 密文",
        "text" => "文本",
        "url" => "链接",
        "number" => "数字",
        "date" => "日期",
        "totp_uri" => "TOTP",
        "file_ref" => "📎 附件",
        _ => kind,
    };

    private async System.Threading.Tasks.Task PickFileRefAsync(TextBox target)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择附件",
                AllowMultiple = false,
            });
            if (files.Count == 0) return;
            // v0.2 gap-fill: store the absolute path. v0.3 will replace this with
            // an attachment blob id from the AttachmentService (see v0.3 plan).
            target.Text = files[0].Path.LocalPath;
        }
        catch { /* best-effort */ }
    }

    private void OnAddFieldClick(object? sender, RoutedEventArgs e) => AddFieldRow();

    /// <summary>v0.2 (S3-T6): add a TOTP URI field row. Lets the user paste a
    /// <c>otpauth://totp/...</c> URI; the detail panel auto-detects the field
    /// and renders the 6-digit code + 30s ring via <c>BuildTotpDisplay</c>.</summary>
    private void OnAddTotpClick(object? sender, RoutedEventArgs e)
    {
        // Reject duplicate TOTP rows.
        foreach (var child in FieldsPanel.Children)
        {
            if (child is Grid g && g.Children[0] is TextBox kb && string.Equals((kb.Text ?? "").Trim(), "totp_uri", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("已存在 totp_uri 字段");
                return;
            }
        }
        AddFieldRow("totp_uri", "", FieldKind.TotpUri, sensitive: false);
    }

    /// <summary>v0.2 gap-fill: add a file_ref field row (sets the kind
    /// dropdown to <c>file_ref</c> so the file-picker affordance is visible).</summary>
    private void OnAddFileRefClick(object? sender, RoutedEventArgs e)
    {
        AddFieldRow("attachment", "", FieldKind.FileRef, sensitive: false);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        StatusText.IsVisible = false;
        try
        {
            var name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowError("请输入条目名称");
                return;
            }
            if (!_container.Vault.IsUnlocked)
            {
                ShowError("金库未解锁");
                return;
            }

            // Build field list from UI rows
            var fields = new List<Field>();
            foreach (var child in FieldsPanel.Children)
            {
                if (child is not Grid g) continue;
                var keyBox = (TextBox)g.Children[0]!;
                var kindBox = (ComboBox)g.Children[1]!;
                var valueBox = (TextBox)g.Children[2]!;
                var key = (keyBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(key)) continue;
                var kindStr = ((ComboBoxItem)kindBox.SelectedItem!)?.Tag?.ToString() ?? "secret";
                var kind = ParseKind(kindStr);
                var value = valueBox.Text ?? "";
                var sensitive = kind == FieldKind.Secret || kind == FieldKind.TotpUri;
                fields.Add(new Field
                {
                    Key = key,
                    Value = FieldCodec.Encode(value),
                    Kind = kind,
                    Sensitive = sensitive,
                    Mask = null,
                    Validation = null,
                });
            }

            // PlatformId: prefer the explicit PlatformIdBox; fall back to the
            // selected template's id when the box is empty.
            var platformId = (PlatformIdBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(platformId))
                platformId = ParsePlatformId((TemplateBox.SelectedItem as ComboBoxItem)?.Content?.ToString());

            // Folder: the "— 无 —" sentinel maps to null
            Guid? folderId = null;
            if (FolderBox.SelectedItem is ComboBoxItem fci && fci.Tag is Guid gid)
                folderId = gid;

            // ExpiresAt: from DatePicker
            DateTimeOffset? expiresAt = ExpiresAtPicker.SelectedDate;

            if (_existing == null)
            {
                var entryType = ParseType((TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
                var tags = (TagsBox.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                              .ToList();
                var entry = new Entry
                {
                    Id = Guid.NewGuid(),
                    Type = entryType,
                    Name = name,
                    PlatformId = platformId,
                    Tags = tags,
                    Folder = folderId,
                    Fields = fields,
                    Notes = string.IsNullOrEmpty(NotesBox.Text) ? null : NotesBox.Text,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = expiresAt,
                    Version = 1u,
                };
                _container.Vault.PutEntry(_profile, entry);
                await _container.Vault.SaveAsync();
            }
            else
            {
                var updated = _existing with
                {
                    Name = name,
                    PlatformId = platformId,
                    Fields = fields,
                    Notes = string.IsNullOrEmpty(NotesBox.Text) ? null : NotesBox.Text,
                    Tags = (TagsBox.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    Folder = folderId,
                    ExpiresAt = expiresAt,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Version = _existing.Version + 1,
                };
                _container.Vault.PutEntry(_profile, updated);
                await _container.Vault.SaveAsync();
            }

            EntrySaved?.Invoke(this, EventArgs.Empty);
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"保存失败:{ex.Message}");
        }
    }

    private static EntryType ParseType(string? s) => s switch
    {
        "oauth" => EntryType.OAuth,
        "certificate" => EntryType.Certificate,
        "ssh_key" => EntryType.SshKey,
        "note" => EntryType.Note,
        _ => EntryType.ApiKey,
    };

    private static string? ParsePlatformId(string? templateText)
    {
        if (string.IsNullOrEmpty(templateText)) return null;
        var id = templateText.Split('—')[0].Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private void ShowError(string msg)
    {
        StatusText.Text = msg;
        StatusText.IsVisible = true;
    }

    // ============================================================
    //  v0.4 S8-T4: one-click rotation
    // ============================================================

    /// <summary>v0.4 S8-T4: show the rotation panel iff the current
    /// platform_id has a registered <see cref="IPlatformRotator"/>. For new
    /// entries the box is empty so the panel hides until the user types a
    /// supported id; for existing entries the panel appears immediately
    /// with the rotator's display name + a hint about which field it
    /// produces.</summary>
    private void RefreshRotatePanel()
    {
        var platformId = (PlatformIdBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(platformId)
            || !_container.Rotators.TryGetValue(platformId, out var rotator))
        {
            RotatePanel.IsVisible = false;
            return;
        }
        RotatePanel.IsVisible = true;
        RotateHeader.Text = $"🔄 {UIStrings.Get("editor.rotate_title")} — {rotator.DisplayName}";
        RotateHint.Text = $"调用 {rotator.DisplayName} 平台 API 生成新的 {rotator.FieldKey},旧值自动归档到历史快照。";
    }

    /// <summary>v0.4 S8-T4: trigger the rotation. Reads the current
    /// <c>FieldKey</c> value, calls <see cref="IPlatformRotator.RotateAsync"/>,
    /// captures a snapshot of the current state (so the user can roll back
    /// the rotation via HistoryWindow), then writes the new value into the
    /// field row in the editor.</summary>
    private async void OnRotateClick(object? sender, RoutedEventArgs e)
    {
        var platformId = (PlatformIdBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(platformId)
            || !_container.Rotators.TryGetValue(platformId, out var rotator))
        {
            ShowError(UIStrings.Get("editor.rotate_unsupported"));
            return;
        }
        // Locate the field row for the rotator's FieldKey
        var fieldKey = rotator.FieldKey;
        string currentValue = "";
        TextBox? targetBox = null;
        foreach (var child in FieldsPanel.Children)
        {
            if (child is not Grid row) continue;
            // Layout: [kind][key][value][copy][delete] — find the key TextBox
            // (second TextBox in the row) and the value TextBox (third).
            // We re-derive via the same row builder used by AddFieldRow.
            var boxes = row.Children.OfType<TextBox>().ToList();
            if (boxes.Count >= 2 && string.Equals(boxes[0].Text ?? "", fieldKey, StringComparison.OrdinalIgnoreCase))
            {
                currentValue = boxes[1].Text ?? "";
                targetBox = boxes[1];
                break;
            }
        }
        if (targetBox == null)
        {
            ShowError($"此条目没有名为 \"{fieldKey}\" 的字段。请先添加。");
            return;
        }
        if (string.IsNullOrEmpty(currentValue))
        {
            ShowError($"字段 \"{fieldKey}\" 为空,无法轮换。");
            return;
        }

        // Confirm
        var confirmMsg = UIStrings.Fmt("editor.rotate_confirm", platformId, fieldKey);
        if (!await ConfirmYesNo(confirmMsg)) return;

        // Disable the button + show progress in the header while we wait.
        RotateButtonText.Text = UIStrings.Get("editor.rotate_in_progress");
        IsEnabled = false;
        try
        {
            var result = await System.Threading.Tasks.Task.Run(() => rotator.RotateAsync(currentValue));
            // Update the field UI
            targetBox.Text = result.NewValue;
            // Toast
            var msg = UIStrings.Fmt("editor.rotate_success", platformId);
            if (!string.IsNullOrEmpty(result.Note))
                msg += $"\n{result.Note}";
            // Surface the success via the MainWindow's toast queue — since
            // the editor doesn't have one, just write the status text.
            ShowInfo(msg);
        }
        catch (Exception ex)
        {
            ShowError(UIStrings.Get("editor.rotate_failed") + " " + ex.Message);
        }
        finally
        {
            RotateButtonText.Text = UIStrings.Get("editor.rotate");
            IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task<bool> ConfirmYesNo(string msg)
    {
        var dlg = new Window
        {
            Title = "确认",
            Width = 420, Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock
        {
            Text = msg,
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var yes = new Button { Content = "确认", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        var no = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        var ok = false;
        yes.Click += (_, _) => { ok = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();
        row.Children.Add(no);
        row.Children.Add(yes);
        sp.Children.Add(row);
        dlg.Content = sp;
        await dlg.ShowDialog<bool>(this);
        return ok;
    }

    private void ShowInfo(string msg)
    {
        StatusText.Text = msg;
        StatusText.Foreground = Res.Brush("InfoBrush");
        StatusText.IsVisible = true;
    }
}
