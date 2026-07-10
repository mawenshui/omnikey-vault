using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Crypto;

/// <summary>
/// P10-T3: SEC-T8-01 tamper detection tests.
/// Verifies that flipping a single byte in the vault file body causes unlock to fail
/// with a crypto error (AEAD tag mismatch), not silently corrupt data.
/// </summary>
public class TamperDetectionTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore;

    public TamperDetectionTests(TempVaultDir dir)
    {
        _dir = dir;
        _keystore = new DeviceKeystore();
    }

    [Theory]
    [InlineData(140)]   // profile section start
    [InlineData(200)]   // mid-payload
    [InlineData(300)]   // deeper in payload
    [InlineData(400)]   // near end
    [InlineData(500)]   // vector clock area
    public async Task SEC_T8_01_OneByteTamper_RejectsFile(int flipOffset)
    {
        var path = _dir.RandomPath();
        var (vault, lockSvc) = CreateService();
        using (vault) using (lockSvc)
        {
            await vault.CreateAsync(path, "tamper-test", Encoding.UTF8.GetBytes("test-password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var entry = new Entry
            {
                Id = _crypto.NewUuidV7(),
                Type = EntryType.ApiKey,
                Name = "tamper-entry",
                Fields = new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk-tamper-test"), Kind = FieldKind.Secret, Sensitive = true } },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };
            vault.PutEntry("prod", entry);
            await vault.SaveAsync();
            vault.Lock();
        }

        // Read the file, flip one byte, write it back
        var bytes = File.ReadAllBytes(path);
        if (flipOffset >= bytes.Length - 64) return; // skip if offset is in signature or beyond

        bytes[flipOffset] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        // Attempt to unlock should fail with crypto error
        var (vault2, lockSvc2) = CreateService();
        using (vault2) using (lockSvc2)
        {
            var act = async () => await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("test-password-123"));
            await act.Should().ThrowAsync<Exception>("tampered file must be rejected");
        }
    }

    [Fact]
    public async Task SEC_T8_02_SignatureTamper_RejectsFile()
    {
        var path = _dir.RandomPath();
        var (vault, lockSvc) = CreateService();
        using (vault) using (lockSvc)
        {
            await vault.CreateAsync(path, "sig-test", Encoding.UTF8.GetBytes("test-password-123"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            vault.Lock();
        }

        // Flip a byte in the signature (last 64 bytes)
        var bytes = File.ReadAllBytes(path);
        var sigOffset = bytes.Length - 32; // middle of signature
        bytes[sigOffset] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var (vault2, lockSvc2) = CreateService();
        using (vault2) using (lockSvc2)
        {
            var act = async () => await vault2.UnlockAsync(path, Encoding.UTF8.GetBytes("test-password-123"));
            await act.Should().ThrowAsync<Exception>("signature tamper must be rejected");
        }
    }

    private (VaultService vault, LockService lockService) CreateService()
    {
        var ls = new LockService(_crypto);
        var vault = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        return (vault, ls);
    }
}

/// <summary>
/// P8-T6/T7: Tests for extracted utility classes (OtpAuthUtils, FieldMasking, CryptoConstants).
/// </summary>
public class UtilityTests
{
    [Fact]
    public void FieldMasking_ShortValue_AllBullets()
    {
        FieldMasking.MaskValue("abc123").Should().Be("••••••••");
    }

    [Fact]
    public void FieldMasking_LongValue_PrefixAndSuffixVisible()
    {
        var masked = FieldMasking.MaskValue("sk-proj-abcdefghij");
        masked.Should().StartWith("sk-");
        masked.Should().EndWith("ghij");
        masked.Should().Contain("•");
    }

    [Fact]
    public void FieldMasking_CustomMask_UsedWhenProvided()
    {
        FieldMasking.MaskValue("anything", "custom-mask").Should().Be("custom-mask");
    }

    [Fact]
    public void FieldMasking_EmptyValue_ReturnsEmpty()
    {
        FieldMasking.MaskValue("").Should().Be("");
    }

    [Fact]
    public void FieldMasking_ByteArray_DecodesAndMasks()
    {
        var bytes = FieldCodec.Encode("sk-test-12345");
        var masked = FieldMasking.MaskValue(bytes);
        masked.Should().StartWith("sk-");
        masked.Should().EndWith("2345");
    }

    [Fact]
    public void OtpAuthUtils_Base32Decode_KnownSecret()
    {
        var decoded = OtpAuthUtils.Base32Decode("JBSWY3DPEHPK3PXP");
        decoded.Should().NotBeNull();
        decoded.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void OtpAuthUtils_ParseSecretFromUri_ExtractsSecret()
    {
        var uri = "otpauth://totp/OpenAI?secret=JBSWY3DPEHPK3PXP&issuer=OpenAI";
        var secret = OtpAuthUtils.ParseSecretFromUri(uri);
        secret.Should().NotBeNull();
        secret!.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OtpAuthUtils_ParseSecretFromUri_InvalidUri_ReturnsNull()
    {
        OtpAuthUtils.ParseSecretFromUri("not-an-otpauth-uri").Should().BeNull();
        OtpAuthUtils.ParseSecretFromUri("").Should().BeNull();
    }

    [Fact]
    public void CryptoConstants_AreNotNullOrEmpty()
    {
        CryptoConstants.KekContext.Should().NotBeNullOrEmpty();
        CryptoConstants.KwrapContext.Should().NotBeNullOrEmpty();
        CryptoConstants.VerifyContext.Should().NotBeNullOrEmpty();
        CryptoConstants.SeedKekContext.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CryptoConstants_HaveExpectedValues()
    {
        CryptoConstants.KekContext.Should().Be("okv-kek-v1");
        CryptoConstants.KwrapContext.Should().Be("okv-kwrap-v1");
        CryptoConstants.VerifyContext.Should().Be("okv-verify-v1");
        CryptoConstants.SeedKekContext.Should().Be("okv-seed-kek-v1");
    }

    [Fact]
    public void FieldCodec_EncodeDecode_RoundTrip()
    {
        const string original = "sk-proj-test-12345-unicode-中文";
        var encoded = FieldCodec.Encode(original);
        var decoded = FieldCodec.Decode(encoded);
        decoded.Should().Be(original);
    }

    [Fact]
    public void FieldCodec_Zero_ClearsBytes()
    {
        var bytes = FieldCodec.Encode("sensitive-data");
        FieldCodec.Zero(bytes);
        bytes.Should().AllBeEquivalentTo((byte)0);
    }

    [Fact]
    public void FieldCodec_Encode_Null_ReturnsEmpty()
    {
        FieldCodec.Encode(null!).Should().BeEmpty();
    }

    [Fact]
    public void FieldCodec_Decode_Null_ReturnsEmpty()
    {
        FieldCodec.Decode(null).Should().BeEmpty();
        FieldCodec.Decode(Array.Empty<byte>()).Should().BeEmpty();
    }
}
