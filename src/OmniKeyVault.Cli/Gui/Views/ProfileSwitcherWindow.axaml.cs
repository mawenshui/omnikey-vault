using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Profile switcher dialog (per UI_UX_SPEC §4.5). Lists all profiles with
/// colored dots + entry counts; emits <see cref="ProfileSelected"/> with
/// the chosen name on click. v0.2 (S3-T2): also supports creating and
/// deleting profiles inline — the "+ 新建" button opens a name+color
/// sub-dialog, and each non-prod row has a "🗑" delete affordance.
/// </summary>
public partial class ProfileSwitcherWindow : Window
{
    private readonly CliContainer _container;
    private readonly string _currentProfile;

    public event EventHandler<string>? ProfileSelected;
    /// <summary>Fired when the profile set changed (create/delete) so the host
    /// can refresh its sidebar/counts. The new set is in <see cref="CliContainer"/>.</summary>
    public event EventHandler? ProfilesChanged;

    public ProfileSwitcherWindow(CliContainer container, string currentProfile)
    {
        InitializeComponent();
        _container = container;
        _currentProfile = currentProfile;
        BuildList();
    }

    private void BuildList()
    {
        ProfileList.Children.Clear();
        var profileNames = GetProfileNames().ToList();
        foreach (var name in profileNames)
        {
            var count = SafeCount(name);
            var btn = new Button { Classes = { "profile-row" } };
            if (name == _currentProfile) btn.Classes.Add("active");

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };

            // Colored dot per profile
            grid.Children.Add(new Ellipse
            {
                Width = 10, Height = 10,
                Fill = Res.Brush(ProfileBrushKey(name)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(grid.Children[0], 0);

            // Name + meta
            var nameStack = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 0, 0) };
            nameStack.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 14,
                Foreground = Res.Brush("FgBrush"),
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = $"{count} 个条目 · {(IsLocalOnly(name) ? "仅本地" : "已同步")}",
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = Res.Brush("FgDimBrush"),
            });
            Grid.SetColumn(nameStack, 1);
            grid.Children.Add(nameStack);

            // Active checkmark
            if (name == _currentProfile)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = "✓",
                    FontSize = 14,
                    Foreground = Res.Brush("AccentBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                });
                Grid.SetColumn(grid.Children[2], 2);
            }

            // Delete button (only for non-prod and non-current to avoid destructive mistakes)
            if (name != "prod" && name != _currentProfile)
            {
                var delBtn = new Button
                {
                    Classes = { "ghost" },
                    Padding = new Thickness(6, 2),
                    Content = new TextBlock { Text = "🗑", FontSize = 11 },
                };
                delBtn.Click += (_, _) => ConfirmAndDelete(name);
                Grid.SetColumn(delBtn, 3);
                grid.Children.Add(delBtn);
            }

            btn.Content = grid;
            // Click anywhere on the row (except the delete button) to switch.
            btn.Click += (_, _) =>
            {
                ProfileSelected?.Invoke(this, name);
                Close();
            };
            ProfileList.Children.Add(btn);
        }
    }

    private void ConfirmAndDelete(string name)
    {
        if (SafeCount(name) > 0)
        {
            ShowConfirm($"Profile \"{name}\" 包含 {SafeCount(name)} 个条目,确认删除?",
                onYes: () => DeleteProfile(name));
        }
        else
        {
            DeleteProfile(name);
        }
    }

    private void DeleteProfile(string name)
    {
        try
        {
            _container.Vault.DeleteProfile(name);
            _container.Profiles.DeleteAsync(name).GetAwaiter().GetResult();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            BuildList();
        }
        catch (Exception ex)
        {
            ShowConfirm("删除失败:" + ex.Message, onYes: null);
        }
    }

    private async void OnNewProfileClick(object? sender, RoutedEventArgs e)
    {
        // Use a sub-dialog with a TextBox + color picker.
        var dlg = new Window
        {
            Title = "新建 Profile",
            Width = 380, Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = "Profile 名称 (小写字母数字, ≤ 32 字符)", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var nameBox = new TextBox { Classes = { "field" }, Watermark = "例如:staging / sandbox / team-a" };
        sp.Children.Add(nameBox);

        sp.Children.Add(new TextBlock { Text = "颜色", FontSize = 12, Foreground = Res.Brush("FgMutedBrush") });
        var colorBox = new ComboBox { Classes = { "field" } };
        foreach (ProfileColor c in Enum.GetValues<ProfileColor>())
        {
            colorBox.Items.Add(new ComboBoxItem { Content = c.ToString(), Tag = c });
        }
        colorBox.SelectedIndex = 0;
        sp.Children.Add(colorBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(14, 6) };
        cancelBtn.Click += (_, _) => dlg.Close();
        var createBtn = new Button { Content = "创建", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        createBtn.Click += async (_, _) =>
        {
            var name = (nameBox.Text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || name.Length > 32)
            {
                ShowConfirm("名称必须 1-32 字符", onYes: null);
                return;
            }
            if (GetProfileNames().Contains(name))
            {
                ShowConfirm($"已存在 Profile \"{name}\"", onYes: null);
                return;
            }
            try
            {
                var color = (ProfileColor)((ComboBoxItem)colorBox.SelectedItem!).Tag!;
                var settings = new ProfileSettings
                {
                    ParticipateInSync = name == "prod",  // default: prod sync, others local-only
                    AutoLockOnSwitch = name != "prod",  // non-prod locks on switch (dev safety)
                    IdleLockMinutes = name == "prod" ? 15 : 5,
                };
                await _container.Profiles.CreateAsync(name, color, settings);
                ProfilesChanged?.Invoke(this, EventArgs.Empty);
                BuildList();
                dlg.Close();
            }
            catch (Exception ex)
            {
                ShowConfirm("创建失败:" + ex.Message, onYes: null);
            }
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(createBtn);
        sp.Children.Add(btnRow);
        dlg.Content = sp;
        await dlg.ShowDialog(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private System.Collections.Generic.IEnumerable<string> GetProfileNames()
    {
        if (_container.Vault.IsLoaded && _container.Vault.Profiles.Count > 0)
            return _container.Vault.Profiles.Keys;
        return new[] { "prod", "dev", "test" };
    }

    private int SafeCount(string name)
    {
        try { return _container.Entries.List(name, null, null, null).Count; }
        catch { return 0; }
    }

    private static bool IsLocalOnly(string name) =>
        name != "prod";   // v0.2: only prod participates in sync by default

    private static string ProfileBrushKey(string name) => name switch
    {
        "dev" => "ProfileDevBrush",
        "test" => "ProfileTestBrush",
        _ => "ProfileProdBrush",
    };

    private void ShowConfirm(string msg, Action? onYes)
    {
        var dlg = new Window
        {
            Title = "确认",
            Width = 360, Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res.Brush("BgCardBrush"),
        };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = msg,
            FontSize = 12,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = onYes == null ? "好的" : "确定", Classes = { "primary" }, Padding = new Thickness(14, 6) };
        ok.Click += (_, _) => { onYes?.Invoke(); dlg.Close(); };
        if (onYes != null)
        {
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += (_, _) => dlg.Close();
            btnRow.Children.Add(cancel);
        }
        btnRow.Children.Add(ok);
        panel.Children.Add(btnRow);
        dlg.Content = panel;
        dlg.ShowDialog(this);
    }
}
