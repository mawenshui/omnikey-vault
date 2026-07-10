using System;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using OmniKeyVault.Cli.Gui;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// Recovery Key display per UI_UX_SPEC §4.1 step 4 / docs/UI/app.js.
/// Shows 32 groups of 6 characters in a 4×8 grid. User must explicitly
/// confirm "I have saved the recovery key offline" before the "Confirm" button
/// is enabled (per the design's anti-tamper friction).
/// </summary>
public partial class RecoveryKeyWindow : Window
{
    private readonly string _recoveryKey;

    public RecoveryKeyWindow(string recoveryKey)
    {
        InitializeComponent();
        _recoveryKey = recoveryKey ?? throw new ArgumentNullException(nameof(recoveryKey));
        BuildGrid();
        ConfirmCheck.IsCheckedChanged += (_, _) =>
            ConfirmButton.IsEnabled = ConfirmCheck.IsChecked == true;
    }

    private void BuildGrid()
    {
        GridContainer.Children.Clear();
        // Use the shared RecoveryKeyRenderer for splitting so the on-screen grid
        // and the print / PDF output always use the same padding rules.
        var groups = RecoveryKeyRenderer.Split(_recoveryKey);
        for (int row = 0; row < RecoveryKeyRenderer.Rows; row++)
        {
            var rowPanel = new Avalonia.Controls.StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,                       // wider spacing matches larger cell width
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            for (int col = 0; col < RecoveryKeyRenderer.Columns; col++)
            {
                var idx = row * RecoveryKeyRenderer.Columns + col;
                if (idx >= groups.Count) break;
                var cell = new Border { Classes = { "code-cell" } };
                cell.Child = new TextBlock
                {
                    Classes = { "cell-text" },
                    Text = groups[idx],
                };
                rowPanel.Children.Add(cell);
            }
            GridContainer.Children.Add(rowPanel);
        }
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(_recoveryKey);
                CopyButtonText.Text = "✓  已复制";
                await Task.Delay(2000);
                await Dispatcher.UIThread.InvokeAsync(() => CopyButtonText.Text = "⧉  复制");
            }
        }
        catch { }
    }

    /// <summary>Trigger the system print dialog (Windows: Notepad print dialog with
    /// printer + "Microsoft Print to PDF" option; macOS: TextEdit print panel;
    /// Linux: xdg-open print). Avoids the Avalonia SaveFilePicker which can hang
    /// in this build — ShellExecute + Verb="print" goes straight to the OS
    /// print pipeline so the user picks a printer (or "Print to PDF") in one click.</summary>
    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okv-recovery-key.txt");
            System.IO.File.WriteAllText(path, RecoveryKeyRenderer.BuildTextContent(_recoveryKey));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Verb = "print",         // OS print dialog
                UseShellExecute = true,
            });
            CopyButtonText.Text = "✓  已发送至系统打印对话框";
        }
        catch (Exception ex)
        {
            CopyButtonText.Text = "✕  " + ex.Message;
        }
    }

    /// <summary>Write a single-page PDF containing the recovery key to a fixed
    /// location under the user's Documents folder, then open it with the system
    /// default PDF viewer. No file picker (Avalonia's SaveFilePicker hangs in this
    /// build on Windows; users who need a different location can use "Save As"
    /// from the PDF viewer's menu).</summary>
    private void OnSavePdfClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Use Documents/omni-recovery-keys/ — a dedicated folder so multiple
            // exports don't clutter the user's Documents root.
            var docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            var dir = System.IO.Path.Combine(docs, "omni-recovery-keys");
            System.IO.Directory.CreateDirectory(dir);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var path = System.IO.Path.Combine(dir, $"omni-recovery-key-{stamp}.pdf");
            RecoveryKeyRenderer.SavePdf(_recoveryKey, path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,        // opens with default PDF viewer
            });
            CopyButtonText.Text = $"✓  PDF 已保存到 {path}";
        }
        catch (Exception ex)
        {
            CopyButtonText.Text = "✕  " + ex.Message;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close();
}
