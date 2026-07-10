﻿using System.Text;
using FluentAssertions;
using OmniKeyVault.Cli;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Cli;

/// <summary>
/// End-to-end CLI integration tests. Invokes the actual handlers via CliContainer,
/// exercises real crypto, real file I/O, real Argon2id, real CLI parsing.
/// </summary>
public class CliIntegrationTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private const string Password = "test-password-123";
    private const string PasswordEnv = "OKV_TEST_MASTER_PASSWORD";

    public CliIntegrationTests(TempVaultDir dir)
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
        r.PasswordFile ??= null;
        r.PasswordEnv ??= PasswordEnv;
        // Wire the password sources into the handlers (mirrors Program.cs).
        handlers.SetPasswordSources(r.PasswordFile, r.PasswordEnv, r.PasswordStdin);
        return r;
    }

    private CliParseResult Parse(string cmd) => throw new InvalidOperationException("Use Parse(handlers, cmd) overload.");

    // ---- CLI-EXIT-01: exit codes ----
    [Fact]
    public async Task CLI_EXIT_01_VaultCreate_SucceedsWithExit0()
    {
        var (c, h, stdout, stderr) = MakeContainer();
        using (c)
        {
            var parsed = Parse(h, $"vault create --vault {NewVaultPath()} --password-env {PasswordEnv}");
            var exit = await h.HandleAsync(parsed);
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("Vault created");
            stdout.ToString().Should().Contain("Recovery Key");
        }
    }

    [Fact]
    public async Task CLI_EXIT_01_Version_ExitsZero()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "version"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("OmniKey Vault v");
            stdout.ToString().Should().MatchRegex(@"OmniKey Vault v\d+\.\d+\.\d+");
        }
    }

    [Fact]
    public async Task CLI_EXIT_01_Help_ExitsZero()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "help"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("Usage:");
        }
    }

    [Fact]
    public async Task CLI_EXIT_01_UnknownCommand_Exits2()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var p = new CliParser();
            var r = p.Parse(new[] { "nuke-the-vault" });
            r.ExitCode.Should().Be(ExitCodes.ArgumentError);
            var exit = await h.HandleAsync(r);
            exit.Should().Be(ExitCodes.ArgumentError);
            stderr.ToString().Should().Contain("Unknown command");
        }
    }

    [Fact]
    public async Task CLI_EXIT_01_WrongPassword_Exits4()
    {
        // Create with correct password
        var path = NewVaultPath();
        var (c, h, _, _) = MakeContainer();
        using (c)
        {
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
        }
        // Try to unlock with wrong password
        var (c2, h2, stdout, stderr) = MakeContainer();
        using (c2)
        {
            Environment.SetEnvironmentVariable(PasswordEnv, "wrong-password");
            // VERIFY: the env var was actually set
            var actualEnv = Environment.GetEnvironmentVariable(PasswordEnv);
            if (actualEnv != "wrong-password")
            {
                throw new Exception($"Env var was not set! Expected 'wrong-password', got '{actualEnv}'.");
            }
            try
            {
                var exit = await h2.HandleAsync(Parse(h2, $"vault unlock --vault {path} --password-env {PasswordEnv}"));
                if (exit != ExitCodes.CryptoError)
                {
                    throw new Exception($"Expected CryptoError, got {exit}. STDOUT: {stdout}. STDERR: {stderr}.");
                }
                exit.Should().Be(ExitCodes.CryptoError);
            }
            finally { Environment.SetEnvironmentVariable(PasswordEnv, Password); }
        }
    }

    [Fact]
    public async Task CLI_EXIT_01_VaultInfo_NonexistentFile_Exits2()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "vault info --vault Z:\\nope\\nada.okv"));
            exit.Should().Be(ExitCodes.ArgumentError);
            stderr.ToString().Should().Contain("not found");
        }
    }

    // ---- CLI-FLOW: create + entry CRUD ----
    [Fact]
    public async Task FullFlow_CreateEntryListGetDelete_Roundtrip()
    {
        var (c, h, stdout, stderr) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            // 1. Create vault
            var exit = await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            exit.Should().Be(0);

            // 2. Create entry from template
            exit = await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name MyOpenAIProd --template openai"));
            exit.Should().Be(0);
            var createOutput = stdout.ToString();
            createOutput.Should().Contain("Created entry");
            var entryIdMatch = createOutput.Split("Created entry ")[1].Split(' ')[0];
            stdout.GetStringBuilder().Clear();

            // 3. List entries
            exit = await h.HandleAsync(Parse(h, $"entry list --vault {path} --password-env {PasswordEnv}"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("MyOpenAIProd");

            // 4. Set field from stdin
            h.SetPasswordSources(null, null, stdin: true);  // not used here, but ensure not stale
            exit = await h.HandleAsync(Parse(h, $"entry get --vault {path} --password-env {PasswordEnv} --id {entryIdMatch} --field api_key --format raw"));
            // Field is empty since we just created it; should not error
            exit.Should().Be(0);
            stdout.GetStringBuilder().Clear();

            // 5. Delete entry (with --yes to skip prompt)
            exit = await h.HandleAsync(Parse(h, $"entry delete --vault {path} --password-env {PasswordEnv} --id {entryIdMatch} --yes"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("Deleted entry");

            // 6. List — should be empty
            stdout.GetStringBuilder().Clear();
            exit = await h.HandleAsync(Parse(h, $"entry list --vault {path} --password-env {PasswordEnv}"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("(no entries)");
        }
    }

    [Fact]
    public async Task TemplateList_MvpOnly_ShowsFiveTemplates()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "template list --mvp-only"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Should().Contain("github");
            text.Should().Contain("openai");
            text.Should().Contain("aws_iam_long_term");
            text.Should().Contain("stripe");
            text.Should().Contain("supabase");
            text.Should().Contain("Y");  // MVP column
        }
    }

    [Fact]
    public async Task TemplateShow_ReturnsFullDefinition()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "template show --id openai"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Should().Contain("OpenAI");
            text.Should().Contain("api_key");
            text.Should().Contain("sk-proj-");
        }
    }

    [Fact]
    public async Task TemplateShow_JsonFormat_ReturnsJson()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var exit = await h.HandleAsync(Parse(h, "template show --id github --format json"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Should().Contain("\"id\"");
            text.Should().Contain("\"github\"");
            text.Should().Contain("\"fields\"");
        }
    }

    [Fact]
    public async Task EntryList_JsonFormat_ReturnsJsonArray()
    {
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            var path = NewVaultPath();
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            stdout.GetStringBuilder().Clear();
            await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name TestEntry --template openai"));
            stdout.GetStringBuilder().Clear();
            var exit = await h.HandleAsync(Parse(h, $"entry list --vault {path} --password-env {PasswordEnv} --format json"));
            exit.Should().Be(0);
            var text = stdout.ToString();
            text.Trim().Should().StartWith("[");
            text.Should().Contain("TestEntry");
        }
    }

    [Fact]
    public async Task BitwardenImport_ImportsItems()
    {
        var path = NewVaultPath();
        var (c, h, stdout, _) = MakeContainer();
        using (c)
        {
            // Create vault first
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            // Write a Bitwarden JSON
            var jsonPath = _dir.VaultPath("bitwarden.json");
            await File.WriteAllTextAsync(jsonPath, @"{
                ""encrypted"": false,
                ""items"": [
                    { ""name"": ""Test1"", ""login"": { ""username"": ""u"", ""password"": ""p"" } },
                    { ""name"": ""Test2"", ""login"": { ""username"": ""u2"", ""password"": ""p2"" } }
                ]
            }");
            stdout.GetStringBuilder().Clear();
            var exit = await h.HandleAsync(Parse(h, $"import --vault {path} --password-env {PasswordEnv} --input {jsonPath}"));
            exit.Should().Be(0);
            stdout.ToString().Should().Contain("Imported 2 item(s)");
        }
    }

    [Fact]
    public async Task EntryGet_UnknownEntry_Exits7()
    {
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            await h.HandleAsync(Parse(h, $"vault create --vault {NewVaultPath()} --password-env {PasswordEnv}"));
            var exit = await h.HandleAsync(Parse(h, $"entry get --vault {NewVaultPath()} --password-env {PasswordEnv} --id 00000000-0000-0000-0000-000000000000"));
            exit.Should().Be(ExitCodes.EntryNotFound);
        }
    }

    [Fact]
    public async Task EntryGet_UnknownField_Exits8()
    {
        var path = NewVaultPath();
        var (c, h, _, stderr) = MakeContainer();
        using (c)
        {
            await h.HandleAsync(Parse(h, $"vault create --vault {path} --password-env {PasswordEnv}"));
            var create = await h.HandleAsync(Parse(h, $"entry set --vault {path} --password-env {PasswordEnv} --name T --template openai"));
            // Get ID
            var (c2, h2, stdout, _) = MakeContainer();
            using (c2)
            {
                await h2.HandleAsync(Parse(h, $"entry list --vault {path} --password-env {PasswordEnv} --format json"));
                var json = stdout.ToString();
                var idStart = json.IndexOf("\"id\": \"") + 7;
                var idEnd = json.IndexOf("\"", idStart);
                var id = json.Substring(idStart, idEnd - idStart);
                var exit = await h2.HandleAsync(Parse(h, $"entry get --vault {path} --password-env {PasswordEnv} --id {id} --field nonexistent"));
                exit.Should().Be(ExitCodes.FieldNotFound);
            }
        }
    }
}
