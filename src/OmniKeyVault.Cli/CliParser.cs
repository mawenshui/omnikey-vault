using System.Text.Json;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli;

/// <summary>
/// Parses command-line arguments per CLI_SPEC.md §2. Supports global options,
/// subcommands, and command-specific options. Returns a structured parse result
/// or a list of errors with the appropriate exit code.
/// </summary>
public sealed class CliParser
{
    public CliParseResult Parse(string[] args)
    {
        var result = new CliParseResult();
        var i = 0;

        // Global options can appear before the subcommand
        while (i < args.Length && args[i].StartsWith("--"))
        {
            if (!TryParseGlobalOption(args, ref i, result, out var err))
                return Err(err);
        }

        if (i >= args.Length)
            return Err("No command specified. Run 'okv help' for usage.");

        var firstToken = args[i];
        if (firstToken.StartsWith("--"))
            return Err($"Unknown command '{firstToken}'. Run 'okv help' for usage.");

        if (!ValidCommands.Contains(firstToken))
            return Err($"Unknown command '{firstToken}'. Run 'okv help' for usage.");

        result.Command = firstToken;
        i++;
        if (result.Command == "help" || result.Command == "--help" || result.Command == "-h")
        {
            result.Command = "help";
            if (i < args.Length) result.Subcommand = args[i++];
            result.ExitCode = 0;
            result.IsHelpRequest = true;
            return result;
        }
        if (result.Command == "version" || result.Command == "--version" || result.Command == "-v")
        {
            result.Command = "version";
            result.ExitCode = 0;
            return result;
        }

        // Subcommand (for vault, profile, entry, template, sync, config, import, export)
        if (IsCommandWithSubcommand(result.Command) && i < args.Length && !args[i].StartsWith("--"))
        {
            var sub = args[i++];
            if (IsValidSubcommand(result.Command, sub))
            {
                result.Subcommand = sub;
            }
            else
            {
                return Err($"Unknown subcommand '{sub}' for command '{result.Command}'. Run 'okv help {result.Command}' for usage.");
            }
        }

        // Remaining options: try global first, then command-specific.
        while (i < args.Length)
        {
            if (!args[i].StartsWith("--"))
                return Err($"Unexpected argument '{args[i]}' after command '{result.Command}'.");
            // Try parsing as a global option first.
            var savedI = i;
            if (TryParseGlobalOption(args, ref i, result, out _))
                continue;
            // Restore i and parse as command-specific option.
            i = savedI;
            var key = args[i++].Substring(2);
            if (i >= args.Length || args[i].StartsWith("--"))
            {
                result.Options[key] = "true";
            }
            else
            {
                result.Options[key] = args[i++];
            }
        }

        result.ExitCode = 0;
        return result;

        CliParseResult Err(string msg)
        {
            result.ExitCode = ExitCodes.ArgumentError;
            result.ErrorMessage = msg;
            return result;
        }
    }

    private static readonly HashSet<string> ValidCommands = new(StringComparer.Ordinal)
    {
        "vault", "profile", "entry", "template", "sync", "config", "import", "export",
        "help", "version"
    };

    private static readonly HashSet<string> ValidVaultSubcommands = new(StringComparer.Ordinal)
        { "create", "unlock", "lock", "info", "change-password" };
    private static readonly HashSet<string> ValidEntrySubcommands = new(StringComparer.Ordinal)
        { "list", "get", "set", "delete", "rotate", "history", "search" };
    private static readonly HashSet<string> ValidTemplateSubcommands = new(StringComparer.Ordinal)
        { "list", "show", "apply" };
    private static readonly HashSet<string> ValidProfileSubcommands = new(StringComparer.Ordinal)
        { "list", "create", "switch", "delete", "info" };
    private static readonly HashSet<string> ValidSyncSubcommands = new(StringComparer.Ordinal)
        { "status", "force", "pause", "resume" };
    private static readonly HashSet<string> ValidConfigSubcommands = new(StringComparer.Ordinal)
        { "get", "set", "list" };

    private static bool IsCommandWithSubcommand(string cmd)
        => cmd is "vault" or "profile" or "entry" or "template" or "sync" or "config" or "export" or "import";

    private static bool IsValidSubcommand(string command, string subcommand)
    {
        return command switch
        {
            "vault" => ValidVaultSubcommands.Contains(subcommand),
            "entry" => ValidEntrySubcommands.Contains(subcommand),
            "template" => ValidTemplateSubcommands.Contains(subcommand),
            "profile" => ValidProfileSubcommands.Contains(subcommand),
            "sync" => ValidSyncSubcommands.Contains(subcommand),
            "config" => ValidConfigSubcommands.Contains(subcommand),
            // export/import use --format instead of a subcommand
            "export" or "import" => false,
            _ => false
        };
    }

    private static bool TryParseGlobalOption(string[] args, ref int i, CliParseResult result, out string err)
    {
        var opt = args[i].Substring(2);
        err = string.Empty;
        switch (opt)
        {
            case "help":
            case "h":
                result.IsHelpRequest = true;
                result.Command = "help";
                i++;
                return true;
            case "version":
            case "v":
                result.Command = "version";
                i++;
                return true;
            case "vault":
                result.VaultPath = GetValue(args, ref i);
                return true;
            case "profile":
                result.Profile = GetValue(args, ref i);
                return true;
            case "format":
                result.Format = GetValue(args, ref i);
                return true;
            case "yes":
                result.Yes = true;
                i++;
                return true;
            case "quiet":
                result.Quiet = true;
                i++;
                return true;
            case "verbose":
                result.Verbose = true;
                i++;
                return true;
            case "no-color":
                i++;
                return true;
            case "password-file":
                result.PasswordFile = GetValue(args, ref i);
                return true;
            case "password-env":
                result.PasswordEnv = GetValue(args, ref i);
                return true;
            case "password-stdin":
                result.PasswordStdin = true;
                i++;
                return true;
            default:
                err = $"Unknown global option '--{opt}'.";
                return false;
        }
    }

    private static string GetValue(string[] args, ref int i)
    {
        i++;
        if (i >= args.Length) throw new ArgumentException($"Option '{args[i - 1]}' requires a value.");
        return args[i++];
    }
}

public sealed class CliParseResult
{
    public string Command { get; set; } = string.Empty;
    public string? Subcommand { get; set; }
    public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);
    public int ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsHelpRequest { get; set; }

    // Global options
    public string? VaultPath { get; set; }
    public string? Profile { get; set; } = "prod";
    public string? Format { get; set; }  // null = user did not specify --format
    public bool Yes { get; set; }
    public bool Quiet { get; set; }
    public bool Verbose { get; set; }
    public string? PasswordFile { get; set; }
    public string? PasswordEnv { get; set; }
    public bool PasswordStdin { get; set; }

    public bool HasOption(string key) => Options.TryGetValue(key, out var v) && v != "false";
    public string? Option(string key) => Options.TryGetValue(key, out var v) ? v : null;
    public string OptionOr(string key, string def) => Options.TryGetValue(key, out var v) ? v : def;
}
