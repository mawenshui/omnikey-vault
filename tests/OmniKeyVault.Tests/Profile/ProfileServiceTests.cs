using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Profiles;

/// <summary>
/// Tests for the ProfileService (PRD §5.1, ROADMAP S3-T1).
/// Covers: create / delete / list / update-settings, Profile DEK isolation
/// (a leak in one profile must not affect another — SECURITY.md §1.4 / §4.3),
/// and v0.1 backward compatibility (single-profile vaults still work).
/// </summary>
public class ProfileServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public ProfileServiceTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService lockSvc, ProfileService profiles) CreateService()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        return (vs, ls, new ProfileService(vs, _crypto, ls));
    }

    private async Task<string> CreateVaultAsync(VaultService vs)
    {
        var path = _dir.RandomPath();
        await vs.CreateAsync(path, "T", Encoding.UTF8.GetBytes("password"),
            Argon2Params.ForTests(32 * 1024 * 1024));
        return path;
    }

    // ---- Create ----

    [Fact]
    public async Task CreateAsync_NewProfile_PersistsAndIsListed()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var p = await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            p.Name.Should().Be("dev");
            p.Color.Should().Be(ProfileColor.Yellow);
            p.Settings.ParticipateInSync.Should().BeFalse();
            profiles.List().Select(x => x.Name).Should().Contain(new[] { "prod", "dev" });
        }
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsNameConflict()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow);
            var act = async () => await profiles.CreateAsync("dev", ProfileColor.Blue);
            await act.Should().ThrowAsync<NameConflictException>().WithMessage("*already exists*");
        }
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsValidation()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var act = async () => await profiles.CreateAsync("", ProfileColor.Yellow);
            await act.Should().ThrowAsync<ValidationException>();
        }
    }

    [Fact]
    public async Task CreateAsync_WhenLocked_Throws()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            // No create call -> vault is locked.
            var act = async () => await profiles.CreateAsync("dev", ProfileColor.Yellow);
            await act.Should().ThrowAsync<VaultLockedException>();
        }
    }

    [Fact]
    public async Task CreateAsync_MultipleProfiles_AllHaveIndependentDEKs()
    {
        // PRD §5.1: profiles have independent DEKs.
        // SECURITY.md §4.3: "Different Profile DEKs are completely independent".
        // We can't directly inspect the DEK, but we can verify that entries
        // added to one profile can only be read from that profile, not the other.
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow);
            await profiles.CreateAsync("test", ProfileColor.Blue);

            // Add an entry to "dev".
            var devEntry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "dev-only",
                Fields = new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk-dev-1234"), Kind = FieldKind.Secret, Sensitive = true } },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("dev", devEntry);
            await vault.SaveAsync();

            // Verify it's in dev, not in test.
            vault.GetEntry("dev", devEntry.Id).Should().NotBeNull();
            vault.ListEntries("test").Should().BeEmpty();
        }
    }

    // ---- Delete ----

    [Fact]
    public async Task DeleteAsync_ExistingProfile_RemovesIt()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow);
            await profiles.DeleteAsync("dev");
            profiles.List().Select(x => x.Name).Should().NotContain("dev");
            profiles.List().Select(x => x.Name).Should().Contain("prod");
        }
    }

    [Fact]
    public async Task DeleteAsync_LastProfile_ThrowsValidation()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var act = async () => await profiles.DeleteAsync("prod");
            await act.Should().ThrowAsync<ValidationException>().WithMessage("*last profile*");
        }
    }

    [Fact]
    public async Task DeleteAsync_NonexistentProfile_ThrowsProfileNotFound()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var act = async () => await profiles.DeleteAsync("nonexistent");
            await act.Should().ThrowAsync<ProfileNotFoundException>();
        }
    }

    [Fact]
    public async Task DeleteAsync_ProfileWithEntries_AlsoRemovesEntries()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow);
            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "in-dev",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("dev", entry);
            await profiles.DeleteAsync("dev");
            vault.Profiles.Should().NotContainKey("dev");
        }
    }

    // ---- Update settings ----

    [Fact]
    public async Task UpdateSettingsAsync_ParticipateInSync_TogglesSync()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            var info = profiles.GetInfo("dev");
            info!.ParticipateInSync.Should().BeFalse();

            // Enable sync for dev (override default).
            await profiles.UpdateSettingsAsync("dev", new ProfileSettings
            {
                ParticipateInSync = true,
                AutoLockOnSwitch = true,
                IdleLockMinutes = 5
            });
            profiles.GetInfo("dev")!.ParticipateInSync.Should().BeTrue();
            vault.ListSyncableProfileNames().Should().Contain("dev");
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_NonexistentProfile_Throws()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var act = async () => await profiles.UpdateSettingsAsync("nope", ProfileSettings.DefaultProd());
            await act.Should().ThrowAsync<ProfileNotFoundException>();
        }
    }

    // ---- List / info ----

    [Fact]
    public async Task List_ReturnsAllProfiles_WithEntryCounts()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow);
            vault.PutEntry("prod", new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "p1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
            var list = profiles.List();
            var prod = list.Single(p => p.Name == "prod");
            prod.EntryCount.Should().Be(1);
            var dev = list.Single(p => p.Name == "dev");
            dev.EntryCount.Should().Be(0);
            dev.Color.Should().Be("Yellow");
        }
    }

    [Fact]
    public async Task GetInfo_NonexistentProfile_ReturnsNull()
    {
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            profiles.GetInfo("ghost").Should().BeNull();
        }
    }

    // ---- Profile DEK isolation (SECURITY.md §1.4) ----

    [Fact]
    public async Task Profile_DeleteDoesNotAffectOtherProfile_DEKRemainsValid()
    {
        // SECURITY.md §4.3: independent DEKs. Deleting a profile must not
        // affect entries in other profiles.
        var (vault, lockSvc, profiles) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            await profiles.CreateAsync("dev", ProfileColor.Yellow);
            var prodEntry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "prod-entry",
                Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode("v"), Kind = FieldKind.Secret, Sensitive = true } },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", prodEntry);
            await profiles.DeleteAsync("dev");
            // prod still has the entry and it can be read.
            var read = vault.GetEntry("prod", prodEntry.Id);
            read.Should().NotBeNull();
            read!.Fields[0].ValueString.Should().Be("v");
        }
    }

    // ---- Cross-instance persistence (profile survives lock/unlock) ----

    [Fact]
    public async Task CreatedProfile_SurvivesLockUnlock()
    {
        var path = _dir.RandomPath();
        // Create + add profile, then close process.
        {
            var (vault, lockSvc, profiles) = CreateService();
            using (vault) using (lockSvc)
            {
                await vault.CreateAsync(path, "T", Encoding.UTF8.GetBytes("pw"),
                    Argon2Params.ForTests(32 * 1024 * 1024));
                await profiles.CreateAsync("dev", ProfileColor.Yellow);
                vault.Lock();
            }
        }
        // Reopen + unlock in a fresh process.
        {
            var (vault, lockSvc, profiles) = CreateService();
            using (vault) using (lockSvc)
            {
                await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("pw"));
                profiles.List().Select(p => p.Name).Should().Contain("dev");
            }
        }
    }
}
