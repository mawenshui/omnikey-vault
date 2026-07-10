using Avalonia;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// GUI launcher for the okv executable. Per ARCHITECTURE.md §5.2 / ADR-006,
/// running <c>okv</c> with no arguments starts the Avalonia desktop GUI;
/// passing any subcommand falls through to the CLI in <see cref="Program"/>.
///
/// This is a v0.1 MVP GUI skeleton (ROADMAP S2-T1): the main window renders
/// a placeholder shell that confirms the GUI pipeline works. Full feature
/// parity with the CLI is staged across S2-T2..S2-T11.
/// </summary>
internal static class AvaloniaApp
{
    /// <summary>Entry point used from <c>Program.cs</c> when no CLI args are given.</summary>
    public static int RunGui(string[] args)
    {
        return BuildAppBuilder().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Public for headless test harnesses (X11 / Win32 / off-screen).</summary>
    public static AppBuilder BuildAppBuilder() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
