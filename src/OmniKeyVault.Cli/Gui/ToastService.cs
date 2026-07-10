using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Bottom-right slide-in toast notifications. Per UI_UX_SPEC §7 and docs/UI/styles.css
/// (.toast-container). Four levels (info / success / warning / error) with colored
/// 3px left border. Auto-dismisses after 2.8s; in/out animation 240ms.
/// </summary>
public enum ToastType
{
    Info,
    Success,
    Warning,
    Error,
}

public static class ToastService
{
    private const int DisplayMs = 2800;  // per UI_UX_SPEC §7 + docs/UI app.js toast()
    private const int FadeMs = 240;
    private const int OffsetX = 40;

    /// <summary>Adds a toast to the given container (typically a StackPanel in the bottom-right of a window).</summary>
    public static void Show(Panel container, string message, ToastType type = ToastType.Info)
    {
        if (container == null) return;

        var (accentKey, icon) = type switch
        {
            ToastType.Success    => ("SuccessBrush", "✓"),
            ToastType.Warning    => ("WarningBrush", "!"),
            ToastType.Error      => ("DangerBrush",  "✕"),
            _                    => ("InfoBrush",    "i"),
        };

        var border = new Border
        {
            Background = Res.Brush("BgElevatedBrush"),
            BorderBrush = Res.Brush("BorderBrightBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 18, 10),
            MinWidth = 240,
            MaxWidth = 360,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0,
        };
        border.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 12, OffsetY = 6,
            Color = Color.FromArgb(0x60, 0x00, 0x00, 0x00),
        });

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
        };

        // Coloured 3px accent strip
        var accentStrip = new Border
        {
            Width = 3,
            Background = Res.Brush(accentKey),
            CornerRadius = new CornerRadius(1.5),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetColumn(accentStrip, 0);
        grid.Children.Add(accentStrip);

        // Icon + message
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
        };
        content.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Res.Brush(accentKey),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = Res.Brush("FgBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        border.Child = grid;

        // Start off-screen + transparent
        var transform = new TranslateTransform { X = OffsetX };
        border.RenderTransform = transform;

        container.Children.Add(border);

        // Animate in via DispatcherTimer (no Avalonia.Animation API issues)
        AnimateOpacity(border, 0, 1, FadeMs);
        AnimateTranslateX(transform, OffsetX, 0, FadeMs);

        // Auto-dismiss
        _ = AutoDismissAsync(container, border, transform);
    }

    private static async Task AutoDismissAsync(Panel container, Border border, TranslateTransform transform)
    {
        await Task.Delay(DisplayMs);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            AnimateOpacity(border, 1, 0, FadeMs);
            AnimateTranslateX(transform, 0, OffsetX, FadeMs, removeAfter: true, container: container, border: border);
        });
    }

    private static void AnimateOpacity(Border target, double from, double to, int ms)
    {
        var frames = 12;
        var stepMs = Math.Max(1, ms / frames);
        var t = 0;
        target.Opacity = from;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
        timer.Tick += (_, _) =>
        {
            t++;
            var p = (double)t / frames;
            target.Opacity = from + (to - from) * p;
            if (t >= frames)
            {
                target.Opacity = to;
                timer.Stop();
            }
        };
        timer.Start();
    }

    private static void AnimateTranslateX(TranslateTransform target, double from, double to, int ms,
        bool removeAfter = false, Panel? container = null, Border? border = null)
    {
        var frames = 12;
        var stepMs = Math.Max(1, ms / frames);
        var t = 0;
        target.X = from;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
        timer.Tick += (_, _) =>
        {
            t++;
            var p = (double)t / frames;
            target.X = from + (to - from) * p;
            if (t >= frames)
            {
                target.X = to;
                timer.Stop();
                if (removeAfter && container != null && border != null)
                    container.Children.Remove(border);
            }
        };
        timer.Start();
    }
}
