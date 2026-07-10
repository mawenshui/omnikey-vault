using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Seed;

/// <summary>
/// End-to-end tests for SeedExporter + SeedImporter, exercising the full
/// dev master key roundtrip and the safety rails (force prod rejection,
/// dev/test only target profile restriction, strip-secrets redaction).
/// </summary>
public class SeedImportExportTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _vaultFmt = new();
    private readonly SeedFormat _seedFmt = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();
    private const string Password = "test-password-123";

    public SeedImportExportTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService lockSvc, ProfileService profiles, SeedExporter exporter, SeedImporter importer) CreateService()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _vaultFmt, ls, _codec, "test-device", _keystore);
        return (vs, ls,
                new ProfileService(vs, _crypto, ls),
                new SeedExporter(vs, _crypto, _codec, _seedFmt, "test-device"),
                new SeedImporter(vs, _crypto, _codec, _seedFmt, "test-device"));
    }

    private async Task<string> CreateVaultWithDevProfileAsync(VaultService vs, ProfileService ps, int entryCount = 1)
    {
        var path = _dir.RandomPath();
        await vs.CreateAsync(path, "T", Encoding.UTF8.GetBytes(Password),
            Argon2Params.ForTests(32 * 1024 * 1024));
        await ps.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
        for (int i = 0; i < entryCount; i++)
        {
            vs.PutEntry("dev", new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = $"entry-{i}",
                Fields = new[]
                {
                    new Field { Key = "api_key", Value = FieldCodec.Encode($"sk-proj-test-{i}"), Kind = FieldKind.Secret, Sensitive = true },
                    new Field { Key = "url", Value = FieldCodec.Encode("https://api.example.com"), Kind = FieldKind.Url, Sensitive = false }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
        }
        await vs.SaveAsync();
        return path;
    }

    private async Task<SeedRecord> ExportAsync(SeedExporter exporter, string sourceProfile, string outputPath, bool strip = false, bool allowProd = false)
    {
        exporter.StripSecrets = strip;
        exporter.AllowProdProfile = allowProd;
        return await exporter.ExportAsync(sourceProfile, outputPath, signingKey: null);
    }

    // ---- Roundtrip ----

    [Fact]
    public async Task ExportThenImport_PreservesEntries()
    {
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 3);
            var seedPath = _dir.RandomPath("okv.dev");
            await ExportAsync(exporter, "dev", seedPath);

            // Now import into a fresh target vault.
            var tgtPath = _dir.RandomPath();
            await vault.CreateAsync(tgtPath, "T2", Encoding.UTF8.GetBytes(Password),
                Argon2Params.ForTests(32 * 1024 * 1024));
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            var result = await importer.ImportAsync(seedPath, "dev");
            result.EntriesImported.Should().Be(3);
            vault.ListEntries("dev").Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task Import_IntoNonExistentProfile_Throws()
    {
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            await ExportAsync(exporter, "dev", seedPath);
            await profiles.DeleteAsync("dev");
            var act = async () => await importer.ImportAsync(seedPath, "dev");
            await act.Should().ThrowAsync<ProfileNotFoundException>();
        }
    }

    [Fact]
    public async Task Export_ProdProfile_RejectedByDefault()
    {
        // PRD §5.5.3 / SECURITY.md T5: seed files must not contain production data.
        var (vault, lockSvc, profiles, exporter, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            var act = async () => await ExportAsync(exporter, "prod", seedPath);
            await act.Should().ThrowAsync<ValidationException>().WithMessage("*prod*");
        }
    }

    [Fact]
    public async Task Export_ProdProfile_AllowedWithOverride()
    {
        var (vault, lockSvc, profiles, exporter, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            vault.PutEntry("prod", new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "prod-1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
            var seedPath = _dir.RandomPath("okv.dev");
            var record = await ExportAsync(exporter, "prod", seedPath, allowProd: true);
            record.Profiles[0].Name.Should().Be("prod");
        }
    }

    [Fact]
    public async Task Import_IntoProdProfile_Rejected()
    {
        // The target profile must be dev or test (SECURITY.md T5 isolation).
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            await ExportAsync(exporter, "dev", seedPath);
            var act = async () => await importer.ImportAsync(seedPath, "prod");
            await act.Should().ThrowAsync<ValidationException>().WithMessage("*dev*test*");
        }
    }

    [Fact]
    public async Task Import_IntoCustomCiProfile_Rejected()
    {
        // The allow-list is exactly { dev, test }; "ci-*" is not in v0.2.
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            await ExportAsync(exporter, "dev", seedPath);
            var act = async () => await importer.ImportAsync(seedPath, "ci-test");
            await act.Should().ThrowAsync<ValidationException>();
        }
    }

    // ---- Strip secrets ----

    [Fact]
    public async Task Export_StripSecrets_RedactsSensitiveValues()
    {
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            var record = await ExportAsync(exporter, "dev", seedPath, strip: true);
            record.StripMode.Should().BeTrue();

            // Import into a fresh target to verify redaction propagated.
            var tgtPath = _dir.RandomPath();
            await vault.CreateAsync(tgtPath, "T2", Encoding.UTF8.GetBytes(Password),
                Argon2Params.ForTests(32 * 1024 * 1024));
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            var result = await importer.ImportAsync(seedPath, "dev");
            result.Warnings.Should().Contain(w => w.Contains("strip-secrets"));

            var importedEntry = vault.ListEntries("dev").First();
            var apiKey = importedEntry.Fields.First(f => f.Key == "api_key");
            apiKey.ValueString.Should().Be("REDACTED-***");
            // url is not sensitive -- not redacted.
            importedEntry.Fields.First(f => f.Key == "url").ValueString.Should().Be("https://api.example.com");
        }
    }

    [Fact]
    public async Task Import_DefaultSecrets_NotRedacted()
    {
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            // No strip -- default is full secret preservation.
            await ExportAsync(exporter, "dev", seedPath);

            var tgtPath = _dir.RandomPath();
            await vault.CreateAsync(tgtPath, "T2", Encoding.UTF8.GetBytes(Password),
                Argon2Params.ForTests(32 * 1024 * 1024));
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            await importer.ImportAsync(seedPath, "dev");

            var entry = vault.ListEntries("dev").First();
            entry.Fields.First(f => f.Key == "api_key").ValueString.Should().StartWith("sk-proj-test-");
        }
    }

    [Fact]
    public async Task Import_DifferentEntries_AddsToExistingProfile()
    {
        // Exporting a profile with entries A, B and importing into a target that
        // already has entries C, D should result in {A, B, C, D}.
        // (Re-importing the SAME seed overwrites by UUID, which is a separate test.)
        string seedPath;
        {
            var (vault, lockSvc, profiles, exporter, _) = CreateService();
            using (vault) using (lockSvc)
            {
                // Create source vault with 2 entries, export.
                var srcPath = _dir.RandomPath();
                await vault.CreateAsync(srcPath, "T", Encoding.UTF8.GetBytes(Password),
                    Argon2Params.ForTests(32 * 1024 * 1024));
                await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
                for (int i = 0; i < 2; i++)
                {
                    vault.PutEntry("dev", new Entry
                    {
                        Id = _crypto.NewUuidV7(),
                        Type = EntryType.ApiKey,
                        Name = $"src-{i}",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Version = 1
                    });
                }
                seedPath = _dir.RandomPath("okv.dev");
                await ExportAsync(exporter, "dev", seedPath);
            }
        }

        // Fresh vault (new services) with 2 different entries.
        {
            var (vault, lockSvc, profiles, _, importer) = CreateService();
            using (vault) using (lockSvc)
            {
                var tgtPath = _dir.RandomPath();
                await vault.CreateAsync(tgtPath, "T2", Encoding.UTF8.GetBytes(Password),
                    Argon2Params.ForTests(32 * 1024 * 1024));
                await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
                for (int i = 0; i < 2; i++)
                {
                    vault.PutEntry("dev", new Entry
                    {
                        Id = _crypto.NewUuidV7(),
                        Type = EntryType.ApiKey,
                        Name = $"tgt-{i}",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Version = 1
                    });
                }
                var result = await importer.ImportAsync(seedPath, "dev");
                result.EntriesImported.Should().Be(2);
                vault.ListEntries("dev").Should().HaveCount(4);
            }
        }
    }

    [Fact]
    public async Task Import_SameEntries_OverwritesByUuid()
    {
        // Importing a seed with the same UUIDs overwrites the existing entries.
        // This is consistent with VaultService.PutEntry semantics.
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 2);
            var seedPath = _dir.RandomPath("okv.dev");
            await ExportAsync(exporter, "dev", seedPath);
            await importer.ImportAsync(seedPath, "dev");
            // 2 original + 2 same-UUID imports that overwrite (still 2).
            vault.ListEntries("dev").Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Import_NonexistentFile_Throws()
    {
        var (vault, lockSvc, _, _, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            var act = async () => await importer.ImportAsync(_dir.RandomPath("okv.dev"), "dev");
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
    }

    [Fact]
    public async Task Import_StrippedSeed_ProducesWarning()
    {
        var (vault, lockSvc, profiles, exporter, importer) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultWithDevProfileAsync(vault, profiles, entryCount: 1);
            var seedPath = _dir.RandomPath("okv.dev");
            await ExportAsync(exporter, "dev", seedPath, strip: true);
            var result = await importer.ImportAsync(seedPath, "dev");
            result.Warnings.Should().NotBeEmpty();
            result.Warnings.Should().ContainMatch("*strip*");
        }
    }
}
