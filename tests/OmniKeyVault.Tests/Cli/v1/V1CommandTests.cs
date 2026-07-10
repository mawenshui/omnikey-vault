using System.Text;
using FluentAssertions;
using OmniKeyVault.Cli;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.Cli.V1;

/// <summary>
/// CLI integration tests for the v1.0 surface area additions:
///   - <c>entry search</c>    (v0.3 S6-T1 / S6-T2 — full-text + field-level search)
///   - <c>entry rotate</c>    (v0.4 S8-T1 / S8-T2 / S8-T3 — one-click platform rotation)
///   - <c>entry history</c>   (v0.4 S7-T6 — list / restore entry snapshots)
///   - <c>sync pause/resume</c> (v0.4 — process-local pause flag)
///   - <c>config get/set/list</c> (v0.2 gap-fill — CLI mirror of SettingsStore)
///   - <c>import --format kdbx-xml</c> (v0.3 S5-T6 — KeePass 2.x XML import)
///
/// The tests use the real service layer (no mocks) and the weak Argon2id
/// (32 MiB) test mode to keep total runtime under 5 s.
/// </summary>
public class V1CommandTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private const string Password = "test-password-123";
    private const string PasswordEnv = "OKV_TEST_MASTER_PASSWORD";

    public V1CommandTests(TempVaultDir dir)
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

    // ---- entry search ----

    [Fact]
    public async Task EntrySearch_FreeText_FindsEntry()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name OpenAI-prod --template openai"));
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name GitHub-PAT --template github"));
            stdout.GetStringBuilder().Clear();

            var exit = await h.HandleAsync(Parse(h, $"entry search --vault {path} --password-env {PasswordEnv} --query openai"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Should().Contain("OpenAI-prod");
            text.Should().NotContain("GitHub-PAT");
        }
    }

    [Fact]
    public async Task EntrySearch_FieldColon_FindsByFieldKey()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name E1 --template openai"));
            stdout.GetStringBuilder().Clear();

            // field:api_key matches entries that have a field named "api_key"
            // (regardless of value — the search matches on field key presence)
            var exit = await h.HandleAsync(Parse(h, $"entry search --vault {path} --password-env {PasswordEnv} --query field:api_key"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("E1");
        }
    }

    [Fact]
    public async Task EntrySearch_NoMatches_Exits0()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name E1 --template openai"));
            stdout.GetStringBuilder().Clear();

            var exit = await h.HandleAsync(Parse(h, $"entry search --vault {path} --password-env {PasswordEnv} --query nonexistent_xyz_zzz"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("(no matches)");
        }
    }

    // ---- entry rotate ----
    //
    // The rotate command requires a real platform API call to OpenAI / GitHub,
    // which can't be exercised in offline tests. The end-to-end "happy path"
    // is covered by:
    //   - tests/OmniKeyVault.Tests/V04/PlatformRotatorTests (rotator unit)
    //   - tests/OmniKeyVault.Tests/Gui/WatcherProviderTests (watcher integration)
    // Here we only assert the CLI's error paths.

    [Fact]
    public async Task EntryRotate_EntryNotFound_Exits7()
    {
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"entry rotate --vault {path} --password-env {PasswordEnv} --id 00000000-0000-0000-0000-000000000000"));
            exit.Should().Be(ExitCodes.EntryNotFound);
        }
    }

    [Fact]
    public async Task EntryRotate_BadUuid_Exits2()
    {
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"entry rotate --vault {path} --password-env {PasswordEnv} --id not-a-uuid"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    // ---- entry history ----

    [Fact]
    public async Task EntryHistory_EmptyHistory_Exits0WithMessage()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name E1 --template openai"));
            // Capture the entry id from stdout ("Created entry <id> from template...")
            var setText = stdout.ToString();
            var idStart = setText.IndexOf("Created entry ") + "Created entry ".Length;
            var idEnd = setText.IndexOf(' ', idStart);
            var entryId = setText.Substring(idStart, idEnd - idStart);
            stdout.GetStringBuilder().Clear();

            // Fresh entry: BackupService has no snapshots yet, so history
            // should print "No history" and exit 0 (not an error).
            var exit = await h.HandleAsync(Parse(h, $"entry history --vault {path} --password-env {PasswordEnv} --id {entryId}"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("No history");
        }
    }

    [Fact]
    public async Task EntryHistory_BadUuid_Exits2()
    {
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"entry history --vault {path} --password-env {PasswordEnv} --id not-a-uuid"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    // ---- sync pause / resume ----

    [Fact]
    public async Task SyncPause_AndResume_TogglesFlag()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            // pause / resume are process-local flags; the container's
            // SyncPauseState lives in CommandHandlers so this test just
            // verifies the CLI accepts both subcommands and emits the
            // expected confirmation text.
            var exit1 = await h.HandleAsync(Parse(h, "sync pause"));
            exit1.Should().Be(0);
            stdout.ToString().Should().Contain("paused");
            stdout.GetStringBuilder().Clear();

            var exit2 = await h.HandleAsync(Parse(h, "sync resume"));
            exit2.Should().Be(0);
            stdout.ToString().Should().Contain("resumed");
        }
    }

    // ---- config get / set / list ----

    [Fact]
    public async Task ConfigList_PrintsKnownKeys()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "config list"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Should().Contain("auto-lock-minutes");
            text.Should().Contain("clipboard-clear-seconds");
            text.Should().Contain("language");
            text.Should().Contain("watcher-enabled");
        }
    }

    [Fact]
    public async Task ConfigGet_KnownKey_PrintsValue()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "config get --key auto-lock-minutes"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("auto-lock-minutes");
            stdout.ToString().Should().MatchRegex(@"auto-lock-minutes\s*=\s*\d+");
        }
    }

    [Fact]
    public async Task ConfigGet_UnknownKey_Exits2()
    {
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "config get --key not-a-real-key"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    [Fact]
    public async Task ConfigSet_ValidKey_UpdatesValue()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit1 = await h.HandleAsync(Parse(h, "config set --key language --value en-US"));
            exit1.Should().Be(0);
            stdout.GetStringBuilder().Clear();

            var exit2 = await h.HandleAsync(Parse(h, "config get --key language"));
            exit2.Should().Be(0);
            stdout.ToString().Should().Contain("en-US");
        }
    }

    [Fact]
    public async Task ConfigSet_InvalidValue_Exits2()
    {
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "config set --key language --value Klingon"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    // ---- import kdbx-xml ----

    [Fact]
    public async Task ImportKdbxXml_MissingFile_Exits2()
    {
        // KeePassXmlImporter throws ValidationException for a missing file
        // (the file existence check is part of its own validation pass before
        // the XML parser runs). The CLI maps ValidationException → exit 2.
        // The unsupported-format gate is tested separately in V2CommandTests
        // (it triggers exit 12 before the importer is ever called).
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"import --vault {path} --password-env {PasswordEnv} --format kdbx-xml --input {Path.Combine(_dir.RandomPath())} --profile prod"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    // ---- vault change-password (P1-T3) ----

    [Fact]
    public async Task VaultChangePassword_Success_Exits0_AndNewPasswordUnlocks()
    {
        // P1-T3: `vault change-password` re-wraps all profile DEKs under a new KEK.
        // After change, the old password must fail and the new password must unlock.
        const string newPwd = "new-password-456";
        const string newPwdEnv = "OKV_TEST_NEW_PASSWORD";
        Environment.SetEnvironmentVariable(newPwdEnv, newPwd);
        try
        {
            var (c, h, stdout, _) = MakeContainer();
            using (c)
            {
                var path = NewVaultPath();
                await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));

                var exit = await h.HandleAsync(Parse(h,
                    $"vault change-password --vault {path} --old-password-env {PasswordEnv} --new-password-env {newPwdEnv}"));
                exit.Should().Be(0);
                stdout.ToString().Should().Contain("Master password changed");

                // Lock + unlock with the new password must succeed (exit 0).
                c.Vault.Lock();
                var unlockExit = await h.HandleAsync(Parse(h, $"vault unlock --vault {path} --password-env {newPwdEnv}"));
                unlockExit.Should().Be(0);
            }
        }
        finally { Environment.SetEnvironmentVariable(newPwdEnv, null); }
    }

    [Fact]
    public async Task VaultChangePassword_OldPasswordWrong_Exits4()
    {
        // Wrong old password → CryptoException → exit 4 (CryptoError).
        const string wrongPwd = "wrong-old-password-xxx";
        const string wrongPwdEnv = "OKV_TEST_WRONG_OLD";
        const string newPwdEnv = "OKV_TEST_NEW_PASSWORD_2";
        Environment.SetEnvironmentVariable(wrongPwdEnv, wrongPwd);
        Environment.SetEnvironmentVariable(newPwdEnv, "new-password-456");
        try
        {
            var (c, h, _, _) = MakeContainer();
            using (c)
            {
                var path = NewVaultPath();
                await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));

                var exit = await h.HandleAsync(Parse(h,
                    $"vault change-password --vault {path} --old-password-env {wrongPwdEnv} --new-password-env {newPwdEnv}"));
                exit.Should().Be(ExitCodes.CryptoError);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(wrongPwdEnv, null);
            Environment.SetEnvironmentVariable(newPwdEnv, null);
        }
    }

    [Fact]
    public async Task VaultChangePassword_MissingOldPasswordEnv_Exits2()
    {
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));

            // Omit --old-password-env entirely → ValidationException → exit 2.
            var exit = await h.HandleAsync(Parse(h,
                $"vault change-password --vault {path} --new-password-env {PasswordEnv}"));
            exit.Should().Be(ExitCodes.ArgumentError);
        }
    }

    [Fact]
    public async Task VaultChangePassword_NewPasswordTooShort_Exits2()
    {
        const string shortPwd = "short";
        const string shortPwdEnv = "OKV_TEST_SHORT_NEW";
        Environment.SetEnvironmentVariable(shortPwdEnv, shortPwd);
        try
        {
            var (c, h, _, _) = MakeContainer();
            using (c)
            {
                var path = NewVaultPath();
                await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));

                var exit = await h.HandleAsync(Parse(h,
                    $"vault change-password --vault {path} --old-password-env {PasswordEnv} --new-password-env {shortPwdEnv}"));
                exit.Should().Be(ExitCodes.ArgumentError);
            }
        }
        finally { Environment.SetEnvironmentVariable(shortPwdEnv, null); }
    }
}
