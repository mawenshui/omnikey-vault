using FluentAssertions;
using OmniKeyVault.Cli;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.Cli;

/// <summary>
/// Tests for the CLI parser and command dispatch per CLI_SPEC.md 搂16.
/// CLI-PARSE-01..04: parsing correctness.
/// CLI-EXIT-01..02: exit code semantics.
/// </summary>
public class CliParserTests
{
    [Fact]
    public void CLI_PARSE_01_HelpCommand_ReturnsHelpResult()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "help" });
        r.IsHelpRequest.Should().BeTrue();
        r.Command.Should().Be("help");
    }

    [Fact]
    public void CLI_PARSE_01_VersionCommand_ReturnsVersion()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "version" });
        r.Command.Should().Be("version");
        r.ExitCode.Should().Be(0);
    }

    [Fact]
    public void CLI_PARSE_01_VaultSubcommand_ParsesSubcommand()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "vault", "create" });
        r.Command.Should().Be("vault");
        r.Subcommand.Should().Be("create");
    }

    [Fact]
    public void CLI_PARSE_01_GlobalOption_BeforeCommand_ParsedAsGlobal()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "--vault", "/tmp/test.okv", "vault", "create" });
        r.VaultPath.Should().Be("/tmp/test.okv");
        r.Command.Should().Be("vault");
        r.Subcommand.Should().Be("create");
    }

    [Fact]
    public void CLI_PARSE_01_GlobalOption_AfterCommand_ParsedAsGlobal()
    {
        // Per CLI_SPEC 搂2.3, global options can appear before or after the subcommand.
        var p = new CliParser();
        var r = p.Parse(new[] { "vault", "create", "--vault", "/tmp/test.okv" });
        r.VaultPath.Should().Be("/tmp/test.okv");
        r.Command.Should().Be("vault");
        r.Subcommand.Should().Be("create");
    }

    [Fact]
    public void CLI_PARSE_01_PasswordEnv_Parsed()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "vault", "unlock", "--password-env", "OKV_MASTER_PASSWORD" });
        r.PasswordEnv.Should().Be("OKV_MASTER_PASSWORD");
    }

    [Fact]
    public void CLI_PARSE_01_PasswordFile_Parsed()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "vault", "unlock", "--password-file", "/tmp/pw" });
        r.PasswordFile.Should().Be("/tmp/pw");
    }

    [Fact]
    public void CLI_PARSE_01_YesFlag_Parsed()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "entry", "delete", "--id", "abc", "--yes" });
        r.Yes.Should().BeTrue();
    }

    [Fact]
    public void CLI_PARSE_01_FormatOption_Parsed()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "entry", "list", "--format", "json" });
        r.Format.Should().Be("json");
    }

    [Fact]
    public void CLI_PARSE_01_CommandSpecificOptions_AfterSubcommand()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "entry", "set", "--name", "test", "--template", "openai" });
        r.Command.Should().Be("entry");
        r.Subcommand.Should().Be("set");
        r.Option("name").Should().Be("test");
        r.Option("template").Should().Be("openai");
    }

    [Fact]
    public void CLI_PARSE_02_UnknownSubcommand_ReturnsExitCode2()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "vault", "delete-the-universe" });
        r.ExitCode.Should().Be(ExitCodes.ArgumentError);
        r.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CLI_PARSE_03_NoCommand_ReturnsExitCode2()
    {
        var p = new CliParser();
        var r = p.Parse(Array.Empty<string>());
        r.ExitCode.Should().Be(ExitCodes.ArgumentError);
        r.ErrorMessage.Should().Contain("No command");
    }

    [Fact]
    public void CLI_PARSE_03_UnknownGlobalOption_ReturnsExitCode2()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "--unknown-option", "x" });
        r.ExitCode.Should().Be(ExitCodes.ArgumentError);
    }

    [Fact]
    public void CLI_PARSE_04_HelpFlag_TopLevel_ShowsHelp()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "--help" });
        r.IsHelpRequest.Should().BeTrue();
    }

    [Fact]
    public void CLI_PARSE_04_HelpCommand_Subcommand_ShowsCommandHelp()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "help", "vault" });
        r.IsHelpRequest.Should().BeTrue();
        r.Subcommand.Should().Be("vault");
    }

    [Fact]
    public void HelpText_Root_ContainsCommandList()
    {
        var text = HelpText.Root;
        text.Should().Contain("vault");
        text.Should().Contain("entry");
        text.Should().Contain("template");
        text.Should().Contain("import");
        text.Should().Contain("export");
        text.Should().Contain("profile");
        text.Should().Contain("sync");
        text.Should().Contain("version");
    }

    [Fact]
    public void HelpText_For_Vault_ContainsSubcommands()
    {
        var text = HelpText.For("vault");
        text.Should().Contain("create");
        text.Should().Contain("unlock");
        text.Should().Contain("lock");
        text.Should().Contain("info");
    }

    [Fact]
    public void HelpText_For_Profile_ContainsSubcommands()
    {
        var text = HelpText.For("profile");
        text.Should().Contain("list");
        text.Should().Contain("create");
        text.Should().Contain("switch");
        text.Should().Contain("delete");
        text.Should().Contain("info");
    }

    [Fact]
    public void HelpText_For_Sync_ContainsSubcommands()
    {
        var text = HelpText.For("sync");
        text.Should().Contain("status");
        text.Should().Contain("force");
    }

    [Fact]
    public void HelpText_For_Export_DocumentsStripSecrets()
    {
        var text = HelpText.For("export");
        text.Should().Contain("strip-secrets");
        text.Should().Contain("okv-dev");
        text.Should().Contain("REDACTED");
    }

    [Fact]
    public void CLI_PARSE_01_ProfileList_ParsesSubcommand()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "profile", "list" });
        r.Command.Should().Be("profile");
        r.Subcommand.Should().Be("list");
        r.ExitCode.Should().Be(0);
    }

    [Fact]
    public void CLI_PARSE_01_ProfileCreate_ParsesOptions()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "profile", "create", "--name", "dev", "--color", "yellow", "--no-sync" });
        r.Command.Should().Be("profile");
        r.Subcommand.Should().Be("create");
        r.Option("name").Should().Be("dev");
        r.Option("color").Should().Be("yellow");
        r.Options["no-sync"].Should().Be("true");
    }

    [Fact]
    public void CLI_PARSE_01_ProfileSwitch_ParsesName()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "profile", "switch", "--name", "dev" });
        r.Command.Should().Be("profile");
        r.Subcommand.Should().Be("switch");
        r.Option("name").Should().Be("dev");
    }

    [Fact]
    public void CLI_PARSE_01_SyncForce_ParsesRemote()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "sync", "force", "--remote", "/tmp/remote.okv" });
        r.Command.Should().Be("sync");
        r.Subcommand.Should().Be("force");
        r.Option("remote").Should().Be("/tmp/remote.okv");
    }

    [Fact]
    public void CLI_PARSE_01_ExportOkvDev_ParsesAllOptions()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "export", "--output", "seed.okv.dev", "--format", "okv-dev", "--source-profile", "dev", "--strip-secrets" });
        r.Command.Should().Be("export");
        r.Option("output").Should().Be("seed.okv.dev");
        r.Format.Should().Be("okv-dev");
        r.Option("source-profile").Should().Be("dev");
        r.Options["strip-secrets"].Should().Be("true");
    }

    [Fact]
    public void CLI_PARSE_01_ImportOkvDev_ParsesFormat()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "import", "--input", "seed.okv.dev", "--format", "okv-dev", "--profile", "dev" });
        r.Command.Should().Be("import");
        r.Option("input").Should().Be("seed.okv.dev");
        r.Format.Should().Be("okv-dev");
        r.Profile.Should().Be("dev");
    }

    [Fact]
    public void CLI_PARSE_02_ProfileUnknownSubcommand_ReturnsExitCode2()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "profile", "nuclear-strike" });
        r.ExitCode.Should().Be(ExitCodes.ArgumentError);
    }

    [Fact]
    public void CLI_PARSE_02_SyncUnknownSubcommand_ReturnsExitCode2()
    {
        var p = new CliParser();
        var r = p.Parse(new[] { "sync", "sing-a-song" });
        r.ExitCode.Should().Be(ExitCodes.ArgumentError);
    }
}
