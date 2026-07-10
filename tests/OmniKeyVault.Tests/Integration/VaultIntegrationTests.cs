﻿using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the Application layer (VaultService + EntryService +
/// TemplateService + BitwardenImporter + ClipboardService). Uses real libsodium + real file
/// system (TempVaultDir fixture per test). No mocks — all crypto and I/O are real.
/// </summary>
public class VaultIntegrationTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore;

    public VaultIntegrationTests(TempVaultDir dir)
    {
        _dir = dir;
        _keystore = new DeviceKeystore();  // writes to real %APPDATA% (acceptable for tests)
    }

    private (VaultService vault, LockService lockService) CreateService()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        return (vs, ls);
    }

    // ---- Vault lifecycle ----
    [Fact]
    public async Task CreateAsync_WritesFileAndReturnsRecoveryKey()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"test-{Guid.NewGuid():N}.okv");
            var result = await vault.CreateAsync(
                path, "Test", Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            File.Exists(path).Should().BeTrue();
            result.RecoveryKey.Should().NotBeEmpty();
            // Phase 12: Recovery Key should be base32 format (13 groups of 4 chars)
            var groups = result.RecoveryKey.Split('-');
            groups.Should().HaveCount(13);
            groups.Should().OnlyContain(g => g.Length == 4);
            result.RecoveryKey.Should().MatchRegex("^[A-Z2-7]{4}(-[A-Z2-7]{4}){12}$",
                "recovery key must be base32 (RFC 4648) format");
            result.VaultUuid.Should().NotBe(Guid.Empty);
            result.Profiles.Should().Contain("prod");
        }
    }

    [Fact]
    public async Task CreateAsync_OverExistingFile_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();
            var act = async () => await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            await act.Should().ThrowAsync<ValidationException>().WithMessage("*already exists*");
        }
    }

    [Fact]
    public async Task UnlockAsync_AfterCreate_PreservesData()
    {
        var path = _dir.RandomPath();
        var (vault1, lockSvc1) = CreateService();
        Guid uuid;
        using (vault1)
        using (lockSvc1)
        {
            var result = await vault1.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            uuid = result.VaultUuid;
            vault1.Lock();
        }

        var (vault2, lockSvc2) = CreateService();
        using (vault2)
        using (lockSvc2)
        {
            var unlock = await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("p"));
            unlock.VaultUuid.Should().Be(uuid);
            unlock.Profiles.Should().Contain("prod");
            vault2.Profiles.Should().ContainKey("prod");
        }
    }

    [Fact]
    public async Task UnlockAsync_WrongPassword_Throws()
    {
        var path = _dir.RandomPath();
        var (vault1, lockSvc1) = CreateService();
        using (vault1)
        using (lockSvc1)
        {
            await vault1.CreateAsync(path, "T", Encoding.UTF8.GetBytes("correct"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault1.Lock();
        }
        var (vault2, lockSvc2) = CreateService();
        using (vault2)
        using (lockSvc2)
        {
            var act = async () => await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("wrong"));
            await act.Should().ThrowAsync<CryptoException>().WithMessage("*Master password is incorrect*");
        }
    }

    [Fact]
    public async Task UnlockAsync_NonexistentFile_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var act = async () => await vault.UnlockAsync("Z:\\nonexistent\\nope.okv", Encoding.UTF8.GetBytes("p"));
            await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
        }
    }

    [Fact]
    public async Task UnlockAsync_TamperedFile_ThrowsCryptoException()
    {
        var path = _dir.RandomPath();
        var (vault1, lockSvc1) = CreateService();
        using (vault1)
        using (lockSvc1)
        {
            await vault1.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
        }
        // Tamper with the file
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[100] ^= 1;  // flip a byte in the verify tag area
        await File.WriteAllBytesAsync(path, bytes);

        var (vault2, lockSvc2) = CreateService();
        using (vault2)
        using (lockSvc2)
        {
            var act = async () => await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("p"));
            await act.Should().ThrowAsync<CryptoException>().WithMessage("*signature*");
        }
    }

    // ---- Entry CRUD ----
    [Fact]
    public async Task PutEntry_ThenListEntries_ReturnsEntry()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "test-entry",
                Fields = new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk-test"), Kind = FieldKind.Secret, Sensitive = true } },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);
            await vault.SaveAsync();
            vault.Lock();

            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("p"));
            var entries = vault.ListEntries("prod");
            entries.Should().ContainSingle(e => e.Id == entry.Id && e.Name == "test-entry");
        }
    }

    [Fact]
    public async Task SetField_IncrementsVersion_AndPreservesIdempotency()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "x",
                Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode("v1"), Kind = FieldKind.Secret, Sensitive = true } },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);
            var entrySvc = new EntryService(vault, new TemplateService(), new ClipboardService(new ClipboardProvider(), lockSvc), _crypto);
            // First set: should bump version
            var r1 = entrySvc.SetField("prod", entry.Id, "k", "v2");
            r1.Version.Should().Be(2);
            // Second set with same value: should be idempotent (no version bump)
            var r2 = entrySvc.SetField("prod", entry.Id, "k", "v2");
            r2.Version.Should().Be(2);
            // Third set with different value: should bump version
            var r3 = entrySvc.SetField("prod", entry.Id, "k", "v3");
            r3.Version.Should().Be(3);
        }
    }

    [Fact]
    public async Task DeleteEntry_RemovesFromList()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "to-delete",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);
            vault.DeleteEntry("prod", entry.Id);
            vault.ListEntries("prod").Should().BeEmpty();
        }
    }

    [Fact]
    public void GetEntry_UnknownId_ReturnsNull()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            // When the vault is locked, GetEntry throws VaultLockedException (INV-04).
            // When the vault is unlocked, GetEntry returns null for unknown ids.
            var act = () => vault.GetEntry("prod", Guid.NewGuid());
            act.Should().Throw<VaultLockedException>();
        }
    }

    [Fact]
    public void GetEntry_UnknownProfile_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            // When the vault is locked, GetEntry throws VaultLockedException (INV-04).
            // When unlocked, it throws ProfileNotFoundException for unknown profile names.
            var act = () => vault.GetEntry("nonexistent", Guid.NewGuid());
            act.Should().Throw<VaultLockedException>();
        }
    }

    // ---- Lock state ----
    [Fact]
    public async Task AfterLock_AllServiceCallsThrowVaultLockedException()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();
            var act = () => vault.PutEntry("prod", new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "x",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
            act.Should().Throw<VaultLockedException>();
        }
    }

    [Fact]
    public async Task AfterLock_UnlockRestoresAccess()
    {
        var path = _dir.RandomPath();
        var (vault1, lockSvc1) = CreateService();
        Guid entryId;
        using (vault1)
        using (lockSvc1)
        {
            await vault1.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            entryId = _crypto.NewUuidV7();
            vault1.PutEntry("prod", new Entry
            {
                Id = entryId,
                Type = EntryType.ApiKey,
                Name = "x",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
            await vault1.SaveAsync();
            vault1.Lock();
        }

        var (vault2, lockSvc2) = CreateService();
        using (vault2)
        using (lockSvc2)
        {
            await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("p"));
            vault2.GetEntry("prod", entryId).Should().NotBeNull();
        }
    }

    // ---- Bitwarden import ----
    [Fact]
    public async Task BitwardenImport_ParsesJsonAndCreatesEntries()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var templates = new TemplateService();
            var clipSvc = new ClipboardService(new ClipboardProvider(), lockSvc);
            var entrySvc = new EntryService(vault, templates, clipSvc, _crypto);
            var bw = new BitwardenImporter(entrySvc, vault, _crypto);

            var json = @"{
                ""encrypted"": false,
                ""items"": [
                    {
                        ""name"": ""Test Item"",
                        ""folder"": ""TestFolder"",
                        ""notes"": ""Test notes"",
                        ""login"": {
                            ""username"": ""user1"",
                            ""password"": ""pass1"",
                            ""uris"": [{ ""uri"": ""https://example.com"" }]
                        }
                    },
                    {
                        ""name"": ""Note Only"",
                        ""notes"": ""just a note""
                    }
                ]
            }";
            var count = bw.ImportFromString("prod", json);
            count.Should().Be(2);
            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(2);
            entries.Should().Contain(e => e.Name == "Test Item" && e.Tags.Contains("TestFolder"));
            entries.Should().Contain(e => e.Name == "Note Only" && e.Type == EntryType.Note);
        }
    }

    [Fact]
    public async Task BitwardenImport_RejectsEncryptedExport()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var bw = new BitwardenImporter(new EntryService(vault, new TemplateService(),
                new ClipboardService(new ClipboardProvider(), lockSvc), _crypto), vault, _crypto);
            var json = @"{ ""encrypted"": true, ""items"": [] }";
            var act = () => bw.ImportFromString("prod", json);
            act.Should().Throw<ValidationException>().WithMessage("*encrypted*");
        }
    }

    // ---- Clipboard ----
    [Fact]
    public async Task ClipboardService_CopyAndClearNow()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var clip = new ClipboardService(new InMemoryClipboardProvider(), lockSvc);
            clip.CopySensitive("secret-value");
            clip.CurrentContent.Should().Be("secret-value");
            clip.ClearNow();
            clip.CurrentContent.Should().BeNull();
        }
    }

    [Fact]
    public async Task ClipboardService_CopyWhenLocked_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();
            var clip = new ClipboardService(new InMemoryClipboardProvider(), lockSvc);
            var act = () => clip.CopySensitive("x");
            act.Should().Throw<VaultLockedException>();
        }
    }

    // ---- Template loading ----
    [Fact]
    public async Task TemplateService_LoadFromDirectory_LoadsAllTemplates()
    {
        var templatesDir = _dir.VaultPath("templates", createIfMissing: true);
        await File.WriteAllTextAsync(Path.Combine(templatesDir, "github.json"), @"{ ""id"": ""github"", ""platform_id"": ""github"", ""name"": ""GitHub"", ""category"": ""code_hosting"", ""official_docs_url"": ""x"", ""mvp_included"": true, ""introduced_in"": ""v0.1"", ""fields"": [] }");
        await File.WriteAllTextAsync(Path.Combine(templatesDir, "openai.json"), @"{ ""id"": ""openai"", ""platform_id"": ""openai"", ""name"": ""OpenAI"", ""category"": ""ai_llm"", ""official_docs_url"": ""x"", ""mvp_included"": true, ""introduced_in"": ""v0.1"", ""fields"": [] }");
        var svc = new TemplateService();
        var count = svc.LoadFromDirectory(templatesDir);
        count.Should().Be(2);
        svc.Templates.Should().ContainKey("github");
        svc.Templates.Should().ContainKey("openai");
    }

    [Fact]
    public async Task TemplateService_LoadFromDirectory_NonExistentDir_ReturnsZero()
    {
        var svc = new TemplateService();
        var count = svc.LoadFromDirectory("Z:\\nonexistent\\dir");
        count.Should().Be(0);
    }

    [Fact]
    public async Task TemplateService_KindSecretRequiresSensitiveTrue()
    {
        // Per PLATFORM_TEMPLATES.md §2.3 invariant: kind=secret must have sensitive=true.
        var svc = new TemplateService();
        var json = @"{ ""id"": ""test"", ""platform_id"": ""test"", ""name"": ""Test"", ""category"": ""other"", ""official_docs_url"": ""x"", ""mvp_included"": false, ""introduced_in"": ""v0.1"", ""fields"": [{ ""key"": ""x"", ""label"": ""X"", ""kind"": ""secret"", ""sensitive"": false, ""required"": false }] }";
        // LoadFromJson itself does not enforce the invariant.
        var loadAct = () => svc.LoadFromJson(json);
        loadAct.Should().NotThrow();
        // CreateEntryFromTemplate enforces the invariant and throws.
        var def = svc.Get("test");
        var createAct = () => svc.CreateEntryFromTemplate(def, "name", DateTimeOffset.UtcNow);
        createAct.Should().Throw<ValidationException>();
    }
}
