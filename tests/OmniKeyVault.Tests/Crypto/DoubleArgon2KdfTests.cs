using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Crypto;

/// <summary>
/// v1.6 Double Argon2id Key Stretching tests.
///
/// Validates the new KDF chain introduced for public-source-code hardening:
///   Round 1: MK1 = Argon2id(password, salt1, {256 MiB, t=3, p=4})
///   Round 2: MK2 = Argon2id(MK1,     salt2, {64 MiB,  t=3, p=1})
///   KEK      = HKDF-SHA256(MK2, "okv-kek-v2", salt1)
///
/// Security goal: an attacker with the .okv file + full source code must run
/// TWO Argon2id rounds per password guess (320 MiB total memory), doubling the
/// brute-force cost compared to v1 (single round, 256 MiB).
/// </summary>
public class DoubleArgon2KdfTests : IClassFixture<TempVaultDir>
{
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();
    private readonly TempVaultDir _dir;

    // Use reduced params for fast tests (8 MiB round 1, 1 MiB round 2)
    private readonly Argon2Params _testRound1 = Argon2Params.ForTests(8 * 1024 * 1024);
    private readonly Argon2Params _testRound2 = Argon2Params.ForTests(1 * 1024 * 1024);

    public DoubleArgon2KdfTests(TempVaultDir dir) => _dir = dir;

    private (VaultService vault, LockService lockService) CreateService()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        return (vs, ls);
    }

    // =========================================================================
    // §1. DeriveMasterKeyV2 — KDF properties
    // =========================================================================

    [Fact]
    public void DeriveMasterKeyV2_Deterministic_SamePasswordAndSalts()
    {
        var salt1 = _crypto.RandomBytes(16);
        var salt2 = _crypto.RandomBytes(16);
        var pw = Encoding.UTF8.GetBytes("correct horse battery staple");

        using var mk1 = _crypto.DeriveMasterKeyV2(pw, salt1, salt2, _testRound1, _testRound2);
        using var mk2 = _crypto.DeriveMasterKeyV2(pw, salt1, salt2, _testRound1, _testRound2);

        mk1.Span.ToArray().Should().Equal(mk2.Span.ToArray());
        mk1.Length.Should().Be(32);
    }

    [Fact]
    public void DeriveMasterKeyV2_DifferentPasswords_ProduceDifferentKeys()
    {
        var salt1 = _crypto.RandomBytes(16);
        var salt2 = _crypto.RandomBytes(16);

        using var mk1 = _crypto.DeriveMasterKeyV2(Encoding.UTF8.GetBytes("pw1"), salt1, salt2, _testRound1, _testRound2);
        using var mk2 = _crypto.DeriveMasterKeyV2(Encoding.UTF8.GetBytes("pw2"), salt1, salt2, _testRound1, _testRound2);

        mk1.Span.ToArray().Should().NotEqual(mk2.Span.ToArray());
    }

    [Fact]
    public void DeriveMasterKeyV2_DifferentSalt2_ProduceDifferentKeys()
    {
        var salt1 = _crypto.RandomBytes(16);
        var salt2a = _crypto.RandomBytes(16);
        var salt2b = _crypto.RandomBytes(16);
        var pw = Encoding.UTF8.GetBytes("same password");

        using var mk1 = _crypto.DeriveMasterKeyV2(pw, salt1, salt2a, _testRound1, _testRound2);
        using var mk2 = _crypto.DeriveMasterKeyV2(pw, salt1, salt2b, _testRound1, _testRound2);

        mk1.Span.ToArray().Should().NotEqual(mk2.Span.ToArray());
    }

    [Fact]
    public void DeriveMasterKeyV2_DifferentSalt1_ProduceDifferentKeys()
    {
        var salt1a = _crypto.RandomBytes(16);
        var salt1b = _crypto.RandomBytes(16);
        var salt2 = _crypto.RandomBytes(16);
        var pw = Encoding.UTF8.GetBytes("same password");

        using var mk1 = _crypto.DeriveMasterKeyV2(pw, salt1a, salt2, _testRound1, _testRound2);
        using var mk2 = _crypto.DeriveMasterKeyV2(pw, salt1b, salt2, _testRound1, _testRound2);

        mk1.Span.ToArray().Should().NotEqual(mk2.Span.ToArray());
    }

    // =========================================================================
    // §2. v1 vs v2 incompatibility — different KEK derivation
    // =========================================================================

    [Fact]
    public void V2KDF_ProducesDifferentKEK_ThanV1KDF()
    {
        // Given the same password and salt1, the v2 double-KDF path must produce
        // a different KEK than the v1 single-KDF path. This is because:
        // 1. v2 uses double Argon2id (MK1 → MK2), while v1 uses single Argon2id (MK)
        // 2. v2 uses domain separator "okv-kek-v2", while v1 uses "okv-kek-v1"
        var salt1 = _crypto.RandomBytes(16);
        var salt2 = _crypto.RandomBytes(16);
        var pw = Encoding.UTF8.GetBytes("test-password");

        // v1 path
        using var mkV1 = _crypto.DeriveMasterKey(pw, salt1, _testRound1);
        using var kekV1 = _crypto.DeriveKek(mkV1, Encoding.UTF8.GetBytes("okv-kek-v1"), salt1);

        // v2 path
        using var mkV2 = _crypto.DeriveMasterKeyV2(pw, salt1, salt2, _testRound1, _testRound2);
        using var kekV2 = _crypto.DeriveKek(mkV2, Encoding.UTF8.GetBytes("okv-kek-v2"), salt1);

        kekV1.Span.ToArray().Should().NotEqual(kekV2.Span.ToArray(),
            "v2 KEK must differ from v1 KEK to ensure v1-encrypted DEKs cannot be unwrapped with the v2 KEK");
    }

    [Fact]
    public void V2KDF_DomainSeparator_PreventsCrossVersionKeyReuse()
    {
        // Even if (hypothetically) MK_v1 == MK_v2, the different HKDF info strings
        // ("okv-kek-v1" vs "okv-kek-v2") ensure the KEKs are different.
        var salt1 = _crypto.RandomBytes(16);
        var mkBytes = _crypto.RandomBytes(32); // same MK for both
        using var mk = MasterKey.From(mkBytes);

        using var kekV1 = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v1"), salt1);
        using var kekV2 = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v2"), salt1);

        kekV1.Span.ToArray().Should().NotEqual(kekV2.Span.ToArray());
    }

    // =========================================================================
    // §3. End-to-end vault lifecycle with v2 KDF
    // =========================================================================

    [Fact]
    public async Task CreateAsync_NewVault_UsesHeaderVersion2()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-kdf-{Guid.NewGuid():N}.okv");
            await vault.CreateAsync(path, "V2Test",
                Encoding.UTF8.GetBytes("password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            // Read the raw file and check the header version bytes (offset 4-5, LE ushort)
            var bytes = await File.ReadAllBytesAsync(path);
            var headerVersion = BitConverter.ToUInt16(bytes, 4);
            headerVersion.Should().Be(0x0002, "new vaults created with v1.6+ must use header version 2");
        }
    }

    [Fact]
    public async Task CreateAndUnlock_V2Vault_RoundtripSucceeds()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-roundtrip-{Guid.NewGuid():N}.okv");
            var password = Encoding.UTF8.GetBytes("my-secret-password");

            // Create
            await vault.CreateAsync(path, "V2Test", password,
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();

            // Unlock with the same password
            var result = await vault.UnlockAsync(path, password);
            result.Profiles.Should().Contain("prod");
            vault.IsUnlocked.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Unlock_V2Vault_WrongPassword_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-wrongpw-{Guid.NewGuid():N}.okv");
            await vault.CreateAsync(path, "V2Test",
                Encoding.UTF8.GetBytes("correct-password"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();

            var act = async () => await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("wrong-password"));
            await act.Should().ThrowAsync<CryptoException>().WithMessage("*Master password is incorrect*");
        }
    }

    [Fact]
    public async Task CreateAndUnlock_V2Vault_DataIntegrity()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-integrity-{Guid.NewGuid():N}.okv");
            var password = Encoding.UTF8.GetBytes("strong-password-456");

            // Create and add an entry
            await vault.CreateAsync(path, "V2Test", password,
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.PutEntry("prod", new Entry
            {
                Id = Guid.NewGuid(),
                Name = "GitHub Token",
                PlatformId = "github",
                Type = EntryType.ApiKey,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1,
                Fields = new List<Field>
                {
                    new() { Key = "token", Value = FieldCodec.Encode("ghp_abc123secret"), Kind = FieldKind.Secret, Sensitive = true },
                    new() { Key = "username", Value = FieldCodec.Encode("user@example.com"), Kind = FieldKind.Text, Sensitive = false },
                },
                Tags = new List<string> { "test", "v2" },
                Notes = "Test entry for v2 KDF",
            });
            await vault.SaveAsync();
            vault.Lock();

            // Unlock and verify the entry
            await vault.UnlockAsync(path, password);
            var entries = vault.ListEntries("prod").ToList();
            entries.Should().HaveCount(1);
            entries[0].Name.Should().Be("GitHub Token");
            var tokenField = entries[0].FindField("token");
            tokenField.Should().NotBeNull();
            tokenField!.ValueString.Should().Be("ghp_abc123secret");
        }
    }

    // =========================================================================
    // §4. Backward compatibility — v1 vaults can still be unlocked
    // =========================================================================

    [Fact]
    public async Task Unlock_V1Vault_StillWorks_AfterV2Upgrade()
    {
        // Manually create a v1 vault (header version 1) and verify it can be unlocked
        // with the v1 KDF path.
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v1-compat-{Guid.NewGuid():N}.okv");
            var password = Encoding.UTF8.GetBytes("v1-password");

            // Create a vault record manually with header version 1
            var deviceKeys = _crypto.GenerateDeviceKeyPair();
            var salt = _crypto.RandomBytes(32);
            var args = Argon2Params.ForTests(32 * 1024 * 1024);

            // v1 KDF: single Argon2id
            using var mk = _crypto.DeriveMasterKey(password, salt.AsSpan(0, 16), args);
            using var kek = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v1"), salt.AsSpan(0, 16));
            var verifyTag = _crypto.ComputeVerifyTag(kek, Array.Empty<byte>());

            // Create a profile with encrypted payload
            var vaultUuid = _crypto.NewUuidV7();
            var dekBytes = _crypto.RandomBytes(32);
            var dek = DataEncryptionKey.From(dekBytes);
            var wrappedDek = _crypto.WrapKey(kek, dek);
            var profileId = _crypto.NewUuidV7();
            var payloadBytes = _codec.Encode(Array.Empty<Entry>(), Array.Empty<Folder>(), Array.Empty<string>(), Array.Empty<Template>());
            var payload = _crypto.Encrypt(dek, payloadBytes, VaultCryptoHelpers.BuildProfileAad(vaultUuid, profileId));

            var record = new VaultRecord
            {
                HeaderVersion = 1,  // v1
                AppBuildHash = _format.ComputeBuildHash(),
                VaultUuid = vaultUuid,
                Argon2Params = args,
                Salt = salt,
                VerifyTag = verifyTag,
                DevicePublicKey = deviceKeys.PublicKey,
                Signature = new byte[64],
                VectorClock = new VectorClock().Increment("test-device"),
                Profiles = new List<ProfileRecord>
                {
                    new ProfileRecord
                    {
                        Id = profileId,
                        Name = "prod",
                        Color = ProfileColor.Green,
                        Settings = ProfileSettings.DefaultProd(),
                        WrappedDek = wrappedDek,
                        PayloadNonce = payload.Nonce,
                        PayloadTag = payload.Tag,
                        EncryptedPayload = payload.Ciphertext
                    }
                }
            };

            // Save device key for unlock
            _keystore.Save(record.VaultUuid, deviceKeys.PrivateKey.Span.ToArray());
            await _format.WriteAsync(path, record, deviceKeys.PrivateKey);

            // Unlock with VaultService — should detect v1 header and use single KDF
            var result = await vault.UnlockAsync(path, password);
            result.Profiles.Should().Contain("prod");
            vault.IsUnlocked.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Unlock_V1Vault_WrongPassword_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v1-wrongpw-{Guid.NewGuid():N}.okv");
            var password = Encoding.UTF8.GetBytes("v1-correct");

            // Create a v1 vault manually
            var deviceKeys = _crypto.GenerateDeviceKeyPair();
            var salt = _crypto.RandomBytes(32);
            var args = Argon2Params.ForTests(32 * 1024 * 1024);

            using var mk = _crypto.DeriveMasterKey(password, salt.AsSpan(0, 16), args);
            using var kek = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v1"), salt.AsSpan(0, 16));
            var verifyTag = _crypto.ComputeVerifyTag(kek, Array.Empty<byte>());

            var vaultUuid = _crypto.NewUuidV7();
            var dekBytes = _crypto.RandomBytes(32);
            var dek = DataEncryptionKey.From(dekBytes);
            var wrappedDek = _crypto.WrapKey(kek, dek);
            var profileId = _crypto.NewUuidV7();
            var payloadBytes = _codec.Encode(Array.Empty<Entry>(), Array.Empty<Folder>(), Array.Empty<string>(), Array.Empty<Template>());
            var payload = _crypto.Encrypt(dek, payloadBytes, VaultCryptoHelpers.BuildProfileAad(vaultUuid, profileId));

            var record = new VaultRecord
            {
                HeaderVersion = 1,
                AppBuildHash = _format.ComputeBuildHash(),
                VaultUuid = vaultUuid,
                Argon2Params = args,
                Salt = salt,
                VerifyTag = verifyTag,
                DevicePublicKey = deviceKeys.PublicKey,
                Signature = new byte[64],
                VectorClock = new VectorClock().Increment("test-device"),
                Profiles = new List<ProfileRecord>
                {
                    new ProfileRecord
                    {
                        Id = profileId,
                        Name = "prod",
                        Color = ProfileColor.Green,
                        Settings = ProfileSettings.DefaultProd(),
                        WrappedDek = wrappedDek,
                        PayloadNonce = payload.Nonce,
                        PayloadTag = payload.Tag,
                        EncryptedPayload = payload.Ciphertext
                    }
                }
            };

            _keystore.Save(record.VaultUuid, deviceKeys.PrivateKey.Span.ToArray());
            await _format.WriteAsync(path, record, deviceKeys.PrivateKey);

            // Try wrong password
            var act = async () => await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("wrong"));
            await act.Should().ThrowAsync<CryptoException>().WithMessage("*Master password is incorrect*");
        }
    }

    // =========================================================================
    // §5. ChangePassword with v2 KDF
    // =========================================================================

    [Fact]
    public async Task ChangePassword_V2Vault_RoundtripSucceeds()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-changepw-{Guid.NewGuid():N}.okv");
            var oldPassword = Encoding.UTF8.GetBytes("old-password-v2");
            var newPassword = Encoding.UTF8.GetBytes("new-password-v2");

            // Create v2 vault
            await vault.CreateAsync(path, "V2Test", oldPassword,
                Argon2Params.ForTests(32 * 1024 * 1024));

            // Add an entry
            vault.PutEntry("prod", new Entry
            {
                Id = Guid.NewGuid(),
                Name = "Secret",
                Type = EntryType.ApiKey,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1,
                Fields = new List<Field>
                {
                    new() { Key = "password", Value = FieldCodec.Encode("p@ssw0rd"), Kind = FieldKind.Secret, Sensitive = true },
                },
            });
            await vault.SaveAsync();

            // Change password (ChangePasswordAsync already persists to disk)
            await vault.ChangePasswordAsync(oldPassword, newPassword);
            vault.Lock();

            // Unlock with new password
            await vault.UnlockAsync(path, newPassword);
            var entries = vault.ListEntries("prod").ToList();
            entries.Should().HaveCount(1);
            entries[0].Name.Should().Be("Secret");
            var pwField = entries[0].FindField("password");
            pwField.Should().NotBeNull();
            pwField!.ValueString.Should().Be("p@ssw0rd");

            // Old password should fail
            vault.Lock();
            var act = async () => await vault.UnlockAsync(path, oldPassword);
            await act.Should().ThrowAsync<CryptoException>();
        }
    }

    [Fact]
    public async Task ChangePassword_V2Vault_WrongOldPassword_Throws()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-changepw-fail-{Guid.NewGuid():N}.okv");
            await vault.CreateAsync(path, "V2Test",
                Encoding.UTF8.GetBytes("correct-old"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var act = async () => await vault.ChangePasswordAsync(
                Encoding.UTF8.GetBytes("wrong-old"),
                Encoding.UTF8.GetBytes("new-password"));
            await act.Should().ThrowAsync<CryptoException>().WithMessage("*Old master password is incorrect*");
        }
    }

    // =========================================================================
    // §6. Security invariant — source code + .okv file ≠ decryption
    // =========================================================================

    [Fact]
    public async Task SourceCodeLeak_CannotDecryptVault_WithoutPassword()
    {
        // Simulate an attacker who has:
        // 1. The full source code (they know the algorithm)
        // 2. The .okv file (they have the ciphertext)
        // 3. But NOT the master password
        //
        // The attacker should NOT be able to derive the correct KEK
        // from any password other than the real one.
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-source-leak-{Guid.NewGuid():N}.okv");
            var realPassword = Encoding.UTF8.GetBytes("the-real-secret-password-42");
            await vault.CreateAsync(path, "V2Test", realPassword,
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();

            // Attacker reads the .okv file
            var record = await _format.ReadAsync(path);

            // Attacker tries common passwords (simulating a dictionary attack)
            var commonPasswords = new[]
            {
                "password", "123456", "admin", "letmein", "welcome",
                "monkey", "dragon", "master", "qwerty", "abc123",
                "password1", "iloveyou", "trustno1", "sunshine", "princess"
            };

            foreach (var guess in commonPasswords)
            {
                var guessBytes = Encoding.UTF8.GetBytes(guess);

                // Attacker uses the v2 double KDF (they read the source code)
                var argsR2 = Argon2Params.ForTests(64 * 1024 * 1024);
                using var mk = _crypto.DeriveMasterKeyV2(
                    guessBytes,
                    record.Salt.AsSpan(0, 16),
                    record.Salt.AsSpan(16, 16),
                    record.Argon2Params, argsR2);
                using var kek = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v2"), record.Salt.AsSpan(0, 16));
                var computedTag = _crypto.ComputeVerifyTag(kek, Array.Empty<byte>());

                // The verify tag should NOT match — the password is wrong
                _crypto.FixedTimeEquals(computedTag, record.VerifyTag).Should().BeFalse(
                    $"password '{guess}' should not match the verify tag");
            }

            // The real password SHOULD match
            {
                var argsR2 = Argon2Params.ForTests(64 * 1024 * 1024);
                using var mk = _crypto.DeriveMasterKeyV2(
                    realPassword,
                    record.Salt.AsSpan(0, 16),
                    record.Salt.AsSpan(16, 16),
                    record.Argon2Params, argsR2);
                using var kek = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v2"), record.Salt.AsSpan(0, 16));
                var computedTag = _crypto.ComputeVerifyTag(kek, Array.Empty<byte>());
                _crypto.FixedTimeEquals(computedTag, record.VerifyTag).Should().BeTrue(
                    "the real password must match the verify tag");
            }
        }
    }

    [Fact]
    public async Task V1KDF_CannotUnlockV2Vault()
    {
        // An attacker who knows the v1 KDF path but not that the vault uses v2
        // would try the v1 single-Argon2id path. This must fail.
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-vs-v1-{Guid.NewGuid():N}.okv");
            var password = Encoding.UTF8.GetBytes("test-password-v2");
            await vault.CreateAsync(path, "V2Test", password,
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();

            // Read the vault record
            var record = await _format.ReadAsync(path);

            // Try v1 KDF (single Argon2id + "okv-kek-v1")
            using var mkV1 = _crypto.DeriveMasterKey(password, record.Salt.AsSpan(0, 16), record.Argon2Params);
            using var kekV1 = _crypto.DeriveKek(mkV1, Encoding.UTF8.GetBytes("okv-kek-v1"), record.Salt.AsSpan(0, 16));
            var tagV1 = _crypto.ComputeVerifyTag(kekV1, Array.Empty<byte>());

            _crypto.FixedTimeEquals(tagV1, record.VerifyTag).Should().BeFalse(
                "v1 KDF must not match a v2 vault's verify tag — different KDF chain + domain separator");

            // v2 KDF (double Argon2id + "okv-kek-v2") should match
            var argsR2 = Argon2Params.ForTests(64 * 1024 * 1024);
            using var mkV2 = _crypto.DeriveMasterKeyV2(password, record.Salt.AsSpan(0, 16), record.Salt.AsSpan(16, 16), record.Argon2Params, argsR2);
            using var kekV2 = _crypto.DeriveKek(mkV2, Encoding.UTF8.GetBytes("okv-kek-v2"), record.Salt.AsSpan(0, 16));
            var tagV2 = _crypto.ComputeVerifyTag(kekV2, Array.Empty<byte>());

            _crypto.FixedTimeEquals(tagV2, record.VerifyTag).Should().BeTrue(
                "v2 KDF must match a v2 vault's verify tag");
        }
    }

    // =========================================================================
    // §7. Save/Load roundtrip preserves v2 header version
    // =========================================================================

    [Fact]
    public async Task SaveAsync_PreservesHeaderVersion2()
    {
        var (vault, lockSvc) = CreateService();
        using (vault)
        using (lockSvc)
        {
            var path = _dir.VaultPath($"v2-save-{Guid.NewGuid():N}.okv");
            await vault.CreateAsync(path, "V2Test",
                Encoding.UTF8.GetBytes("password"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            // Add an entry and save
            vault.PutEntry("prod", new Entry
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Type = EntryType.ApiKey,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1,
                Fields = new List<Field>
                {
                    new() { Key = "secret", Value = FieldCodec.Encode("secret"), Kind = FieldKind.Secret, Sensitive = true },
                },
            });
            await vault.SaveAsync();
            vault.Lock();

            // Verify header version is still 2 after save
            var bytes = await File.ReadAllBytesAsync(path);
            var headerVersion = BitConverter.ToUInt16(bytes, 4);
            headerVersion.Should().Be(0x0002, "SaveAsync must preserve the header version");

            // Unlock again to verify data integrity
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("password"));
            var entries = vault.ListEntries("prod").ToList();
            entries.Should().HaveCount(1);
            entries[0].Name.Should().Be("Test");
        }
    }
}
