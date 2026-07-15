using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// v1.9: Vault switcher dialog. Lists recently used vaults and allows
/// switching between them (locks the current vault, unlocks the selected one).
/// Also supports browsing for a vault file and creating a new vault.
/// </summary>
public partial class VaultSwitcherWindow : Window
{
    private string _currentVaultPath;

    /// <summary>Fired when the user selects a different vault. Host should
    /// lock the current vault and unlock the selected one.</summary>
    public event EventHandler<string>? VaultSelected;
    /// <summary>Fired when the user wants to create a new vault.</summary>
    public event EventHandler? CreateVaultRequested;

    public VaultSwitcherWindow(string currentVaultPath)
    {
        InitializeComponent();
        _currentVaultPath = currentVaultPath;
        BuildList();
    }

    private void BuildList()
    {
        VaultList.Children.Clear();

        var vaults = SettingsStore.RecentVaults
            .Where(p => File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        // Ensure current vault is in the list
        if (!vaults.Any(v => string.Equals(v, _currentVaultPath, StringComparison.OrdinalIgnoreCase)))
            vaults.Insert(0, _currentVaultPath);

        foreach (var vaultPath in vaults)
        {
            var isCurrent = string.Equals(vaultPath, _currentVaultPath, StringComparison.OrdinalIgnoreCase);
            var btn = new Button { Classes = { "profile-row" } };
            if (isCurrent) btn.Classes.Add("active");

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            // Icon
            grid.Children.Add(new TextBlock
            {
                Text = isCurrent ? "📂" : "📁",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(grid.Children[0], 0);

            // Name + path
            var nameStack = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 0, 0) };
            var fileName = Path.GetFileNameWithoutExtension(vaultPath);
            nameStack.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 14,
                FontWeight = FontWeight.Medium,
                Foreground = Res.Brush("FgBrush"),
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = vaultPath,
                FontFamily = Res.Font("FontMono"),
                FontSize = 11,
                Foreground = Res.Brush("FgDimBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(nameStack, 1);
            grid.Children.Add(nameStack);

            // Active checkmark
            if (isCurrent)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = "✓ 当前",
                    FontSize = 11,
                    Foreground = Res.Brush("AccentBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                Grid.SetColumn(grid.Children[2], 2);
            }

            btn.Content = grid;
            if (!isCurrent)
            {
                btn.Click += (_, _) =>
                {
                    VaultSelected?.Invoke(this, vaultPath);
                    Close();
                };
            }
            VaultList.Children.Add(btn);
        }

        if (vaults.Count == 0)
        {
            VaultList.Children.Add(new TextBlock
            {
                Text = "暂无其他保险箱",
                FontSize = 14,
                Foreground = Res.Brush("FgMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "选择保险箱文件 (.okv)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("OmniKey Vault")
                            { Patterns = new[] { "*.okv" } },
                    },
                });
            if (files.Count == 0) return;
            var picked = files[0].Path.LocalPath;
            if (!File.Exists(picked)) return;
            VaultSelected?.Invoke(this, picked);
            Close();
        }
        catch { /* best-effort */ }
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        CreateVaultRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
