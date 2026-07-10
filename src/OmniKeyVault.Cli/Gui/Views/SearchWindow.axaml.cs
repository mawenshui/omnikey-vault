using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// v0.3 S6-T3: advanced search window. Mirrors the field-level syntax
/// described in <see cref="SearchService"/> and renders per-field highlights
/// in each result row so the user can see exactly which field matched.
///
/// Triggered by the MainWindow's "高级搜索" button next to the search box.
/// Closes on Escape; double-click on a result jumps back to the entry in
/// MainWindow via the <see cref="EntryActivated"/> event.
/// </summary>
public partial class SearchWindow : Window
{
    private readonly CliContainer _container;
    private readonly string _profile;

    /// <summary>Emitted when the user double-clicks a result row. MainWindow
    /// listens to this and selects the entry in its detail panel.</summary>
    public event EventHandler<Entry>? EntryActivated;

    public SearchWindow(CliContainer container, string profile)
    {
        InitializeComponent();
        _container = container;
        _profile = profile;
        ProfileText.Text = $"Profile: {profile} · {_container.Vault.ListEntries(profile).Count} entries";
        QueryBox.Watermark = "tags:dev AND platform:openai";
        // Re-run search on every keystroke (debounced visually by the
        // user — searches complete in &lt; 200 ms for 1000 entries).
        QueryBox.TextChanged += (_, _) => RunSearch();
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnSearchClick(object? sender, RoutedEventArgs e) => RunSearch();

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        QueryBox.Text = "";
        RunSearch();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void RunSearch()
    {
        var q = QueryBox.Text ?? "";
        ResultsPanel.Children.Clear();
        if (string.IsNullOrWhiteSpace(q))
        {
            StatusText.Text = UIStrings.Get("search.placeholder");
            return;
        }
        IReadOnlyList<Entry> entries;
        try { entries = _container.Vault.ListEntries(_profile); }
        catch (Exception ex)
        {
            StatusText.Text = "✕ " + ex.Message;
            return;
        }
        var hits = _container.Search.Search(q, entries);
        if (hits.Count == 0)
        {
            StatusText.Text = UIStrings.Get("search.no_results");
            return;
        }
        StatusText.Text = UIStrings.Fmt("search.results_fmt", hits.Count);
        foreach (var hit in hits)
        {
            ResultsPanel.Children.Add(BuildHitRow(hit));
        }
    }

    private Button BuildHitRow(SearchHit hit)
    {
        var entry = hit.Entry;
        var btn = new Button { Classes = { "entry-row" }, Tag = entry };
        var sp = new StackPanel { Spacing = 4 };
        // Title row
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        title.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontFamily = Res.Font("FontDisplay"),
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Foreground = Res.Brush("FgBrush"),
        });
        Grid.SetColumn(title.Children[0], 0);
        title.Children.Add(new TextBlock
        {
            Text = entry.PlatformId ?? entry.Type.ToString().ToLowerInvariant(),
            FontFamily = Res.Font("FontMono"),
            FontSize = 10,
            Foreground = Res.Brush("FgDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(title.Children[1], 1);
        sp.Children.Add(title);
        // Subtitle: tags + type
        if (entry.Tags.Count > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "🏷 " + string.Join(", ", entry.Tags),
                FontFamily = Res.Font("FontMono"),
                FontSize = 10,
                Foreground = Res.Brush("FgDimBrush"),
            });
        }
        // Field hits: render each matched value with the match span in accent color
        foreach (var fh in hit.FieldHits.Take(5))
        {
            sp.Children.Add(BuildFieldHitRow(fh));
        }
        // Score
        sp.Children.Add(new TextBlock
        {
            Text = $"score: {hit.Score:F1}",
            FontFamily = Res.Font("FontMono"),
            FontSize = 9,
            Foreground = Res.Brush("FgFaintBrush"),
        });
        btn.Content = sp;
        btn.DoubleTapped += (_, _) => EntryActivated?.Invoke(this, entry);
        return btn;
    }

    private static StackPanel BuildFieldHitRow(FieldHit fh)
    {
        var sp = new StackPanel { Spacing = 1, Margin = new Thickness(0, 2, 0, 2) };
        sp.Children.Add(new TextBlock
        {
            Text = $"{UIStrings.Get("search.field_highlighted")}: {fh.FieldKey}",
            FontFamily = Res.Font("FontMono"),
            FontSize = 9,
            LetterSpacing = 1.2,
            Foreground = Res.Brush("FgFaintBrush"),
        });
        // Build a TextBlock with the matched span in accent color.
        // Approach: render the prefix in muted, the matched substring in
        // accent bold, the suffix in muted. Avalonia's TextBlock doesn't
        // support inline spans without Runs, so we use inline Inlines.
        var tb = new TextBlock
        {
            FontFamily = Res.Font("FontMono"),
            FontSize = 11,
            Foreground = Res.Brush("FgMutedBrush"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var v = fh.MatchedValue ?? "";
        int start = Math.Max(0, fh.StartIndex);
        int len = Math.Max(0, fh.Length);
        var inlines = tb.Inlines!;
        if (start >= v.Length || len == 0)
        {
            // No valid span — just render the full value muted
            inlines.Add(new Run(v) { FontFamily = Res.Font("FontMono") });
        }
        else
        {
            int safeStart = Math.Min(start, v.Length);
            int safeEnd = Math.Min(start + len, v.Length);
            if (safeStart > 0) inlines.Add(new Run(v.Substring(0, safeStart)));
            if (safeEnd > safeStart)
                inlines.Add(new Run(v.Substring(safeStart, safeEnd - safeStart))
                {
                    Foreground = Res.Brush("AccentBrush"),
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                });
            if (safeEnd < v.Length) inlines.Add(new Run(v.Substring(safeEnd)));
        }
        sp.Children.Add(tb);
        return sp;
    }
}
