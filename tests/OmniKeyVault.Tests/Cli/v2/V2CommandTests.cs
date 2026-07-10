using System.Text;
using FluentAssertions;
using OmniKeyVault.Cli;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.Cli.V2;

/// <summary>
/// CLI integration tests for v0.2 commands: profile, export (okv-dev), import (okv-dev),
/// sync. Verifies end-to-end CLI flows including new exit codes 5 (ProfileNotFound),
/// 9 (NameConflict), 14 (SyncConflict).
/// </summary>
public class V2CommandTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private const string Password = "test-password-123";
    private const string PasswordEnv = "OKV_TEST_MASTER_PASSWORD";

    public V2CommandTests(TempVaultDir dir)
    {
        _dir = dir;
        Environment.SetEnvironmentVariable(PasswordEnv, Password);
        Environment.SetEnvironmentVariable("OKV_TEST_MODE", "1");
    }

    private string NewVaultPath() => _dir.RandomPath();

    private (CliContainer container, CommandHandlers handlers, StringWriter stdout, StringWriter stderr) MakeContainer()
    {
        var container = new CliContainer("test-device-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        container.LoadTemplates();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var handlers = new CommandHandlers(container, stdout, stderr,
            readPassword: _ => Password,
            readStdinLine: () => null);
        return (container, handlers, stdout, stderr);
    }

    private CliParseResult Parse(CommandHandlers handlers, string cmd)
    {
        var p = new CliParser();
        var r = p.Parse(cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (r.ExitCode != 0 && !r.IsHelpRequest)
            throw new InvalidOperationException($"Parse error: {r.ErrorMessage}");
        handlers.SetPasswordSources(r.PasswordFile, r.PasswordEnv ?? PasswordEnv, r.PasswordStdin);
        return r;
    }

    // ---- profile ----

    [Fact]
    public async Task ProfileList_AfterCreate_ShowsAllProfiles()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            stdout.GetStringBuilder().Clear();

            await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name dev --color yellow"));
            stdout.GetStringBuilder().Clear();
            await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name test --color blue"));

            var exit = await h.HandleAsync(Parse(h, $"profile list --vault {path} --password-env {PasswordEnv}"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Should().Contain("prod");
            text.Should().Contain("dev");
            text.Should().Contain("test");
        }
    }

    [Fact]
    public async Task ProfileCreate_DuplicateName_Exits9()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name dev"));
            var exit = await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name dev"));
            exit.Should().Be(ExitCodes.NameConflict);
        }
    }

    [Fact]
    public async Task ProfileInfo_Nonexistent_Exits5()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"profile info --vault {path} --password-env {PasswordEnv} --name ghost"));
            exit.Should().Be(ExitCodes.ProfileNotFound);
        }
    }

    [Fact]
    public async Task ProfileDelete_Nonexistent_Exits5()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"profile delete --vault {path} --password-env {PasswordEnv} --name ghost --yes"));
            exit.Should().Be(ExitCodes.ProfileNotFound);
        }
    }

    [Fact]
    public async Task ProfileDelete_LastProfile_Exits2()
    {
        // Cannot delete the only profile left in a vault.
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"profile delete --vault {path} --password-env {PasswordEnv} --name prod --yes"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    [Fact]
    public async Task ProfileSwitch_Unknown_Exits5()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"profile switch --vault {path} --password-env {PasswordEnv} --name ghost"));
            exit.Should().Be(ExitCodes.ProfileNotFound);
        }
    }

    [Fact]
    public async Task ProfileSwitch_KnownProfile_Succeeds()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"profile switch --vault {path} --password-env {PasswordEnv} --name prod"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("Switched to profile 'prod'");
        }
    }

    // ---- export / import seed (okv-dev) ----

    [Fact]
    public async Task ExportOkvDev_FromDevProfile_ProducesValidFile()
    {
        var (c, h, stdout, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name dev --color yellow"));
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --profile dev --name myentry --template openai"));
            stdout.GetStringBuilder().Clear();

            var seedPath = _dir.RandomPath("okv.dev");
            var exit = await h.HandleAsync(Parse(h, $"export --vault {path} --password-env {PasswordEnv} --output {seedPath} --format okv-dev --source-profile dev"));
            exit.Should().Be(0);
            File.Exists(seedPath).Should().BeTrue();
            stdout.ToString().Should().Contain("Exported profile 'dev'");
        }
    }

    [Fact]
    public async Task ExportOkvDev_ProdByDefault_Exits2()
    {
        // SECURITY: prod profile is rejected by default (PRD §5.5.3 / SECURITY.md T5).
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var seedPath = _dir.RandomPath("okv.dev");
            var exit = await h.HandleAsync(Parse(h, $"export --vault {path} --password-env {PasswordEnv} --output {seedPath} --format okv-dev --source-profile prod"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    [Fact]
    public async Task ExportOkvDev_ProdWithOverride_Succeeds()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var seedPath = _dir.RandomPath("okv.dev");
            var exit = await h.HandleAsync(Parse(h, $"export --vault {path} --password-env {PasswordEnv} --output {seedPath} --format okv-dev --source-profile prod --allow-prod-profile"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("Exported profile 'prod'");
        }
    }

    [Fact]
    public async Task ImportOkvDev_IntoDevProfile_Succeeds()
    {
        var (c, h, stdout, _) = MakeContainer();
        string srcPath, seedPath;
        using (c)
        {
            srcPath = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {srcPath} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"profile create --vault {srcPath} --password-env {PasswordEnv} --name dev --color yellow"));
            await h.HandleAsync(Parse(h, $"entry set --vault {srcPath} --password-env {PasswordEnv} --profile dev --name imported-entry --template openai"));
            seedPath = _dir.RandomPath("okv.dev");
            var exit = await h.HandleAsync(Parse(h, $"export --vault {srcPath} --password-env {PasswordEnv} --output {seedPath} --format okv-dev --source-profile dev"));
            exit.Should().Be(0);
        }

        // Fresh vault for import.
        using (var c2 = new CliContainer("device-2"))
        {
            c2.LoadTemplates();
            var stdout2 = new StringWriter();
            var stderr2 = new StringWriter();
            var h2 = new CommandHandlers(c2, stdout2, stderr2,
                readPassword: _ => Password,
                readStdinLine: () => null);
            var tgtPath = NewVaultPath();
            await h2.HandleAsync(Parse(h2, $"vault create --vault {tgtPath} --password-env {PasswordEnv}"));
            await h2.HandleAsync(Parse(h2, $"profile create --vault {tgtPath} --password-env {PasswordEnv} --name dev --color yellow"));

            var exit = await h2.HandleAsync(Parse(h2, $"import --vault {tgtPath} --password-env {PasswordEnv} --input {seedPath} --format okv-dev --profile dev"));
            exit.Should().Be(0);
            stdout2.ToString().Should().Contain("Imported 1 entry(s)");
            stdout2.ToString().Should().Contain("Seed UUID:");
        }
    }

    [Fact]
    public async Task ImportOkvDev_IntoProdProfile_Exits2()
    {
        // SECURITY: target profile must be dev or test (PRD §5.5.3).
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name dev --color yellow"));
            var seedPath = _dir.RandomPath("okv.dev");
            await h.HandleAsync(Parse(h, $"export --vault {path} --password-env {PasswordEnv} --output {seedPath} --format okv-dev --source-profile dev"));

            var exit = await h.HandleAsync(Parse(h, $"import --vault {path} --password-env {PasswordEnv} --input {seedPath} --format okv-dev --profile prod"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    [Fact]
    public async Task ImportOkvDev_NonexistentFile_Exits6()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"profile create --vault {path} --password-env {PasswordEnv} --name dev --color yellow"));
            var exit = await h.HandleAsync(Parse(h, $"import --vault {path} --password-env {PasswordEnv} --input Z:\\nope.okv.dev --format okv-dev --profile dev"));
            exit.Should().Be(ExitCodes.IoError);
        }
    }

    [Fact]
    public async Task Import_UnsupportedFormat_Exits12()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"import --vault {path} --password-env {PasswordEnv} --input {path} --format rocket-science --profile prod"));
            exit.Should().Be(ExitCodes.FormatUnsupported);
        }
    }

    [Fact]
    public async Task Export_UnsupportedFormat_Exits12()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"export --vault {path} --password-env {PasswordEnv} --output {path}.export --format rocket-science"));
            exit.Should().Be(ExitCodes.FormatUnsupported);
        }
    }

    // ---- sync ----

    [Fact]
    public async Task SyncStatus_AfterVaultCreate_ShowsProfile()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            stdout.GetStringBuilder().Clear();
            var exit = await h.HandleAsync(Parse(h, $"sync status --vault {path} --password-env {PasswordEnv}"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("prod");
        }
    }

    [Fact]
    public async Task SyncStatus_JsonFormat_ProducesJson()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            stdout.GetStringBuilder().Clear();
            var exit = await h.HandleAsync(Parse(h, $"sync status --vault {path} --password-env {PasswordEnv} --format json"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("\"vault_uuid\"");
            stdout.ToString().Should().Contain("\"device_id\"");
        }
    }

    [Fact]
    public async Task SyncForce_MissingRemote_Exits2()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            // No --remote flag.
            var exit = await h.HandleAsync(Parse(h, $"sync force --vault {path} --password-env {PasswordEnv}"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    [Fact]
    public async Task SyncForce_NoRemoteFile_NoChange()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var remote = _dir.RandomPath();
            var exit = await h.HandleAsync(Parse(h, $"sync force --vault {path} --password-env {PasswordEnv} --remote {remote}"));
            exit.Should().Be(0);
            stdout.ToString().Should().Match(s => s.Contains("NoChange") || s.Contains("No remote"));
        }
    }

    [Fact]
    public async Task SyncForce_CorruptRemoteFile_Exits13()
    {
        // P1-T2: corrupt remote file is FileCorrupt (13), not SyncConflict (14).
        // SyncConflict is reserved for unrecoverable merge conflicts needing GUI.
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var remote = _dir.RandomPath();
            await File.WriteAllBytesAsync(remote, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            var exit = await h.HandleAsync(Parse(h, $"sync force --vault {path} --password-env {PasswordEnv} --remote {remote}"));
            exit.Should().Be(ExitCodes.FileCorrupt);
        }
    }

    // ---- help ----

    [Fact]
    public void Help_Root_ListsNewCommands()
    {
        HelpText.Root.Should().Contain("profile");
        HelpText.Root.Should().Contain("export");
        HelpText.Root.Should().Contain("sync");
    }

    [Fact]
    public void Help_Version_Reports1_0()
    {
        var text = HelpText.Root;
        text.Should().Contain("v1.0");
    }
}
