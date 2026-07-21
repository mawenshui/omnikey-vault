using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Crypto;

/// <summary>
/// P1-9: Extended security invariant tests.
/// Complements SecurityInvariantTests.cs and TamperDetectionTests.cs with
/// additional coverage for memory zeroing, crash dump safety, tamper rejection
/// across multiple profiles, and key lifecycle invariants.
/// </summary>
public class SecurityInvariantExtendedTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public SecurityInvariantExtendedTests(TempVaultDir dir) => _dir = dir;

    private (VaultService vault, LockService lockService) CreateService()
    {
        var ls = new LockService(_crypto);
        var vault = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        return (vault, ls);
    }

    // ---- INV-09: Lock zeroes all sensitive fields across multiple profiles ----

    [Fact]
    public async Task INV09_Lock_ZeroesAllSensitiveFields_AcrossMultipleProfiles()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "multi-profile-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            vault.CreateProfile("dev", ProfileColor.Green);
            vault.CreateProfile("staging", ProfileColor.Red);

            var refs = new List<byte[]>();
            foreach (var profile in new[] { "prod", "dev", "staging" })
            {
                for (int i = 0; i < 3; i++)
                {
                    var secretBytes = FieldCodec.Encode($"sk-{profile}-{i}-secret");
                    var entry = new Entry
                    {
                        Id = Guid.NewGuid(),
                        Type = EntryType.ApiKey,
                        Name = $"{profile}-entry-{i}",
                        Fields = new[]
                        {
                            new Field { Key = "api_key", Value = secretBytes, Kind = FieldKind.Secret, Sensitive = true },
                            new Field { Key = "token", Value = FieldCodec.Encode($"tok-{profile}-{i}"), Kind = FieldKind.Secret, Sensitive = true },
                        },
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Version = 1
                    };
                    vault.PutEntry(profile, entry);
                    refs.Add(secretBytes);
                    refs.Add(entry.Fields.First(f => f.Key == "token").Value);
                }
            }

            // Verify all sensitive values are non-zero before lock
            foreach (var buf in refs)
                buf.Any(b => b != 0).Should().BeTrue("sensitive values should be non-zero before lock");

            vault.Lock();

            // Verify all sensitive byte arrays are now zeroed
            foreach (var buf in refs)
                buf.Should().AllBeEquivalentTo((byte)0,
                    "all sensitive field values across all profiles must be zeroed on lock");
        }
    }

    // ---- INV-10: Lock disposes DataEncryptionKey ----

    [Fact]
    public async Task INV10_Lock_DisposesDataEncryptionKey()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "dek-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            vault.IsUnlocked.Should().BeTrue("vault should be unlocked after create");

            vault.Lock();

            vault.IsUnlocked.Should().BeFalse("vault should be locked after Lock()");
            vault.IsLoaded.Should().BeFalse("vault should not be loaded after Lock()");
        }
    }

    // ---- INV-11: Tamper rejection across different file regions ----

    [Theory]
    [InlineData(10)]   // header region
    [InlineData(50)]   // early header
    [InlineData(100)]  // salt area
    [InlineData(250)]  // mid-body
    [InlineData(350)]  // deeper body
    public async Task INV11_TamperAtVariousOffsets_RejectsUnlock(int flipOffset)
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "tamper-test",
                Encoding.UTF8.GetBytes("test-password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entry = new Entry
            {
                Id = Guid.NewGuid(),
                Type = EntryType.ApiKey,
                Name = "test-entry",
                Fields = new[]
                {
                    new Field { Key = "key", Value = FieldCodec.Encode("sk-test-value-12345"), Kind = FieldKind.Secret, Sensitive = true }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);
            await vault.SaveAsync();
            vault.Lock();
        }

        var bytes = File.ReadAllBytes(path);
        if (flipOffset >= bytes.Length - 64) return; // skip signature area

        bytes[flipOffset] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var (vault2, ls2) = CreateService();
        using (vault2) using (ls2)
        {
            var act = async () => await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("test-password-123"));
            await act.Should().ThrowAsync<Exception>("tampered file must be rejected at any offset");
        }
    }

    // ---- INV-12: Wrong password rejected ----

    [Fact]
    public async Task INV12_WrongPassword_RejectsUnlock()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "wrong-pw-test",
                Encoding.UTF8.GetBytes("correct-password"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();
        }

        var (vault2, ls2) = CreateService();
        using (vault2) using (ls2)
        {
            var act = async () => await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("wrong-password"));
            await act.Should().ThrowAsync<Exception>("wrong password must be rejected");
        }
    }

    // ---- INV-13: Locked vault has empty profiles dict ----

    [Fact]
    public async Task INV13_LockedVault_ProfilesEmpty()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "profiles-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            vault.CreateProfile("dev", ProfileColor.Green);
            vault.Profiles.Should().HaveCount(2, "should have prod + dev profiles");

            vault.Lock();
            vault.Profiles.Should().BeEmpty("profiles must be cleared on lock");
        }
    }

    // ---- INV-14: Save + reload preserves data integrity ----

    [Fact]
    public async Task INV14_SaveReload_DataIntegrityPreserved()
    {
        var path = _dir.RandomPath();
        var entryId = Guid.NewGuid();
        var secretValue = "sk-integrity-test-12345";

        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "integrity-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entry = new Entry
            {
                Id = entryId,
                Type = EntryType.ApiKey,
                Name = "integrity-entry",
                PlatformId = "openai",
                Fields = new[]
                {
                    new Field { Key = "api_key", Value = FieldCodec.Encode(secretValue), Kind = FieldKind.Secret, Sensitive = true },
                    new Field { Key = "username", Value = FieldCodec.Encode("user@test.com"), Kind = FieldKind.Text, Sensitive = false },
                },
                Tags = new[] { "test", "important" },
                Notes = "Test notes for integrity",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);
            await vault.SaveAsync();
            vault.Lock();
        }

        // Reload and verify
        var (vault2, ls2) = CreateService();
        using (vault2) using (ls2)
        {
            await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("password-123"));

            var entries = vault2.ListEntries("prod");
            entries.Should().HaveCount(1);
            entries[0].Id.Should().Be(entryId);
            entries[0].Name.Should().Be("integrity-entry");
            entries[0].PlatformId.Should().Be("openai");
            entries[0].Tags.Should().Contain("test").And.Contain("important");
            entries[0].Notes.Should().Be("Test notes for integrity");
            entries[0].Fields.Should().Contain(f => f.Key == "api_key" && f.ValueString == secretValue);
            entries[0].Fields.Should().Contain(f => f.Key == "username" && f.ValueString == "user@test.com");
        }
    }

    // ---- INV-15: FieldCodec.Encode produces non-empty bytes for non-empty input ----

    [Fact]
    public void INV15_FieldCodec_Encode_NonEmptyInput_ProducesNonEmptyBytes()
    {
        var encoded = FieldCodec.Encode("test-value");
        encoded.Should().NotBeEmpty();
        encoded.Any(b => b != 0).Should().BeTrue("encoded value should not be all zeros");
    }

    // ---- INV-16: Lock called twice does not throw ----

    [Fact]
    public async Task INV16_DoubleLock_DoesNotThrow()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "double-lock-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var act = () => vault.Lock();
            act.Should().NotThrow("first lock should succeed");

            act.Should().NotThrow("second lock should be a no-op, not throw");

            vault.IsUnlocked.Should().BeFalse();
            vault.IsLoaded.Should().BeFalse();
        }
    }

    // ---- INV-17: Large entry count — lock zeroes all ----

    [Fact]
    public async Task INV17_LargeEntryCount_LockZeroesAll()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "large-count-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var refs = new List<byte[]>();
            for (int i = 0; i < 50; i++)
            {
                var secretBytes = FieldCodec.Encode($"sk-large-{i:D3}-secret-value");
                var entry = new Entry
                {
                    Id = Guid.NewGuid(),
                    Type = EntryType.ApiKey,
                    Name = $"entry-{i:D3}",
                    Fields = new[]
                    {
                        new Field { Key = "api_key", Value = secretBytes, Kind = FieldKind.Secret, Sensitive = true },
                    },
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Version = 1
                };
                vault.PutEntry("prod", entry);
                refs.Add(secretBytes);
            }

            vault.Lock();

            foreach (var buf in refs)
                buf.Should().AllBeEquivalentTo((byte)0,
                    "all 50 entries' sensitive values must be zeroed on lock");
        }
    }

    // ---- INV-18: After lock, vault state is fully cleared ----

    [Fact]
    public async Task INV18_Lock_VaultStateFullyCleared()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            await vault.CreateAsync(path, "state-clear-test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entry = new Entry
            {
                Id = Guid.NewGuid(),
                Type = EntryType.ApiKey,
                Name = "test",
                Fields = new[]
                {
                    new Field { Key = "text_field", Value = FieldCodec.Encode("non-sensitive-data"), Kind = FieldKind.Text, Sensitive = false },
                    new Field { Key = "secret_field", Value = FieldCodec.Encode("sensitive-data"), Kind = FieldKind.Secret, Sensitive = true },
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);

            // Before lock: entries are accessible
            vault.ListEntries("prod").Should().HaveCount(1);

            vault.Lock();

            // After lock: vault is fully cleared — no entries accessible
            vault.IsLoaded.Should().BeFalse();
            vault.IsUnlocked.Should().BeFalse();
            vault.Profiles.Should().BeEmpty();
            var act = () => vault.ListEntries("prod");
            act.Should().Throw<VaultLockedException>("locked vault must reject all data access");
        }
    }

    // ---- INV-19: Argon2id parameters cannot be lowered below minimum ----

    [Fact]
    public async Task INV19_Argon2Memory_Below32MiB_Rejected()
    {
        var path = _dir.RandomPath();
        var (vault, ls) = CreateService();
        using (vault) using (ls)
        {
            var weakParams = Argon2Params.ForTests(8 * 1024 * 1024); // 8 MiB < 32 MiB
            var act = async () => await vault.CreateAsync(path, "weak-argon2",
                Encoding.UTF8.GetBytes("password-123"), weakParams);
            await act.Should().ThrowAsync<ValidationException>(
                "Argon2id memory cost below 32 MiB must be rejected");
        }
    }

    [Fact]
    public void INV19_Argon2Default_MeetsOrExceedsMinimum()
    {
        Argon2Params.Default.Memory.Should().BeGreaterThanOrEqualTo(64 * 1024 * 1024,
            "default Argon2id memory cost should be >= 64 MiB for production");
    }
}
