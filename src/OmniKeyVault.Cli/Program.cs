using OmniKeyVault.Cli;
using OmniKeyVault.Cli.Gui;

// Entry point for the okv executable. Per ARCHITECTURE.md §5.2 / ADR-006:
//   okv                   → launch Avalonia GUI (full feature parity since v0.2)
//   okv <subcommand> [...] → run CLI
// The GUI has full feature parity with the CLI since v0.2 (14 views).
// Both GUI and CLI share the same Application layer services.

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

// GUI mode: no subcommand → Avalonia main window
if (cliArgs.Length == 0)
    return AvaloniaApp.RunGui(Environment.GetCommandLineArgs());

// CLI mode: parse + dispatch (unchanged from the pre-GUI implementation)
var parser = new CliParser();
var parsed = parser.Parse(cliArgs);

if (parsed.ExitCode != 0 && !parsed.IsHelpRequest)
{
    Console.Error.WriteLine(parsed.ErrorMessage ?? "Parse error.");
    if (!parsed.Quiet) Console.Error.WriteLine("Run 'okv help' for usage.");
    return parsed.ExitCode;
}

if (parsed.IsHelpRequest)
{
    // Print specific-command help if a subcommand is named, otherwise root.
    if (string.IsNullOrEmpty(parsed.Subcommand))
        Console.Out.WriteLine(HelpText.Root);
    else
        Console.Out.WriteLine(HelpText.For(parsed.Subcommand));
    return 0;
}

// Set up DI container
var deviceId = Environment.MachineName + "-" + Environment.ProcessId;
using var container = new CliContainer(deviceId);
container.LoadTemplates();

// P2-T1: Register process-exit hooks for SecureKey cleanup per INTERNAL.md §7.3.
// The `using` above handles normal return; these hooks cover Ctrl+C and
// AppDomain.ProcessExit (unhandled exceptions, SIGTERM on Linux, Windows shutdown).
// CliContainer.Dispose is idempotent so double-call is safe.
AppDomain.CurrentDomain.ProcessExit += (_, _) => container.Dispose();
Console.CancelKeyPress += (_, e) =>
{
    container.Dispose();
    // e.Cancel defaults to false → process terminates after handler returns.
    // We do NOT set e.Cancel=true (that would keep the process alive, defeating cleanup).
};

var handlers = new CommandHandlers(container, Console.Out, Console.Error,
    readPassword: prompt =>
    {
        Console.Error.Write(prompt);
        // Read password without echo. In CI / non-TTY, fall back to plain ReadLine.
        var pw = ReadPasswordFromConsole();
        Console.Error.WriteLine();
        return pw;
    },
    readStdinLine: () => Console.In.ReadLine());

handlers.SetPasswordSources(parsed.PasswordFile, parsed.PasswordEnv, parsed.PasswordStdin);

var exit = await handlers.HandleAsync(parsed);
return exit;

static string ReadPasswordFromConsole()
{
    // Simple cross-platform password read: if TTY, read char-by-char without echo.
    // In non-interactive contexts (CI), we fall back to a plain ReadLine with a warning.
    if (!Console.IsInputRedirected)
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
            }
            else
            {
                sb.Append(key.KeyChar);
            }
        }
        return sb.ToString();
    }
    else
    {
        return Console.In.ReadLine() ?? string.Empty;
    }
}
