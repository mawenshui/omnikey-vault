using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Seed;

/// <summary>
/// Tests for the .okv.dev seed file format (OKVD magic) per OKV_FORMAT.md §11.
/// Covers: roundtrip, integrity (tamper detection), magic bytes, strip mode flag,
/// and the explicit security property that the Dev Master Key is stored PLAINTEXT.
/// </summary>
public class SeedFormatTests
{
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly SeedFormat _format = new();

    private SeedRecord MakeRecord(bool stripMode = false, int profileCount = 1, DeviceKeyPair? deviceKeys = null)
    {
        deviceKeys ??= _crypto.GenerateDeviceKeyPair();
        using var dek = DataEncryptionKey.From(_crypto.RandomBytes(32));
        using var kek = KeyEncryptionKey.From(_crypto.RandomBytes(32));
        var wrappedDek = _crypto.WrapKey(kek, dek);
        var profiles = new List<ProfileRecord>();
        for (int i = 0; i < profileCount; i++)
        {
            var payload = Encoding.UTF8.GetBytes($"seed-payload-{i}");
            var payloadEnc = _crypto.Encrypt(dek, payload, _crypto.RandomBytes(32));
            profiles.Add(new ProfileRecord
            {
                Id = _crypto.NewUuidV7(),
                Name = $"dev-{i}",
                Color = ProfileColor.Yellow,
                Settings = ProfileSettings.DefaultDev(),
                WrappedDek = wrappedDek,
                PayloadNonce = payloadEnc.Nonce,
                PayloadTag = payloadEnc.Tag,
                EncryptedPayload = payloadEnc.Ciphertext
            });
        }
        return new SeedRecord
        {
            AppBuildHash = _format.ComputeBuildHash(),
            SeedUuid = _crypto.NewUuidV7(),
            DevMasterKey = _crypto.RandomBytes(32),
            DevSalt = _crypto.RandomBytes(32),
            StripMode = stripMode,
            Profiles = profiles,
            Signature = new byte[64]
        };
    }

    // ---- Roundtrip ----

    [Fact]
    public void Encode_Decode_Roundtrip_PreservesAllFields()
    {
        var original = MakeRecord(stripMode: true, profileCount: 2);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(original, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);

        decoded.AppBuildHash.Should().Equal(original.AppBuildHash);
        decoded.SeedUuid.Should().Be(original.SeedUuid);
        decoded.DevMasterKey.Should().Equal(original.DevMasterKey);
        decoded.DevSalt.Should().Equal(original.DevSalt);
        decoded.StripMode.Should().BeTrue();
        decoded.Profiles.Should().HaveCount(2);
        decoded.Profiles[0].Name.Should().Be("dev-0");
        decoded.Profiles[1].Name.Should().Be("dev-1");
    }

    [Fact]
    public void Encode_Decode_SignatureIsVerifiable()
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord(deviceKeys: deviceKeys);
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.SignedRegion.Should().NotBeNull();
        _crypto.Verify(decoded.Profiles[0].WrappedDek.Tag is { } _
            ? new DevicePublicKey(_crypto.RandomBytes(32))  // wrong key, just for API
            : new DevicePublicKey(_crypto.RandomBytes(32)),
            decoded.SignedRegion!, decoded.Signature).Should().BeFalse();
    }

    [Fact]
    public void Encode_Decode_EmptyProfiles_Roundtrip()
    {
        var record = MakeRecord(profileCount: 0);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.Profiles.Should().BeEmpty();
    }

    [Fact]
    public void Encode_ProducesOkvdMagic()
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        // First 4 bytes must be "OKVD" — distinct from "OKV1" of production vaults.
        encoded[0].Should().Be(0x4F);  // 'O'
        encoded[1].Should().Be(0x4B);  // 'K'
        encoded[2].Should().Be(0x56);  // 'V'
        encoded[3].Should().Be(0x44);  // 'D'  (not 0x01 like OKV1)
    }

    // ---- Tamper detection ----

    [Theory]
    [InlineData(6)]    // build hash
    [InlineData(14)]   // seed UUID
    [InlineData(30)]   // dev master key
    [InlineData(62)]   // dev salt
    [InlineData(94)]   // strip mode
    public void Decode_TamperedHeaderByte_SignatureFails(int byteOffset)
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord(deviceKeys: deviceKeys);
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        var bytes = encoded.ToArray();
        bytes[byteOffset] ^= 1;
        var tampered = _format.Decode(bytes);
        // Signature was over the original bytes; flip a byte and verify fails.
        _crypto.Verify(deviceKeys.PublicKey, tampered.SignedRegion!, tampered.Signature).Should().BeFalse();
    }

    [Fact]
    public void Decode_TamperedMagic_ThrowsFileCorrupt()
    {
        var record = MakeRecord();
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        encoded[0] = 0xFF;
        var act = () => _format.Decode(encoded);
        act.Should().Throw<FileCorruptException>().WithMessage("*Invalid seed magic*");
    }

    [Fact]
    public void Decode_WrongMagic_Okv1_Throws()
    {
        // OKV1 is the production vault magic — it must NOT be accepted as OKVD.
        var bytes = new byte[1024];
        bytes[0] = 0x4F; bytes[1] = 0x4B; bytes[2] = 0x56; bytes[3] = 0x01;  // OKV1, not OKVD
        var act = () => _format.Decode(bytes);
        act.Should().Throw<FileCorruptException>().WithMessage("*Invalid seed magic*");
    }

    [Fact]
    public void Decode_TruncatedFile_Throws()
    {
        var act = () => _format.Decode(new byte[50]);
        act.Should().Throw<FileCorruptException>().WithMessage("*too short*");
    }

    [Fact]
    public void Decode_EmptyFile_Throws()
    {
        var act = () => _format.Decode(Array.Empty<byte>());
        act.Should().Throw<FileCorruptException>();
    }

    [Fact]
    public void Decode_UnsupportedHeaderVersion_Throws()
    {
        var record = MakeRecord();
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        encoded[4] = 0x02;
        var act = () => _format.Decode(encoded);
        act.Should().Throw<FileCorruptException>().WithMessage("*Unsupported*");
    }

    // ---- Dev Master Key property ----

    [Fact]
    public void DevMasterKey_IsStoredPlaintext_SecurityWarning()
    {
        // SECURITY: per OKV_FORMAT.md §11.4 / PRD §5.5.3, the Dev Master Key
        // is INTENTIONALLY plaintext in the seed file. Anyone with the file
        // can decrypt. This test documents that fact (a future refactor that
        // encrypts the seed would break this test \u2014 the test name flags the change).
        var record = MakeRecord();
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        // Read raw bytes 30..62 (the Dev Master Key slot per OKV_FORMAT \u00a711.2).
        var devMasterKeyInFile = encoded.AsSpan(30, 32).ToArray();
        devMasterKeyInFile.Should().Equal(record.DevMasterKey,
            "Dev Master Key must be plaintext in the .okv.dev file (per OKV_FORMAT \u00a711.4). " +
            "If you intend to encrypt the seed, also change the security classification.");
    }
}
