﻿using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Format;

/// <summary>
/// Tests for the .okv binary format reader/writer per OKV_FORMAT.md 搂4-搂6.
/// Covers FMT-RW-01..05 (roundtrip), FMT-INTEG-01..04 (integrity),
/// and FMT-RECOV-01..03 (crash recovery). FMT-RECOV tests live in
/// StorageTests since they exercise the file-system layer.
/// </summary>
public class FormatTests
{
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();

    private VaultRecord MakeRecord(uint argonMemory = 8 * 1024 * 1024, uint argonTime = 3, byte parallelism = 1, int profileCount = 1, DeviceKeyPair? deviceKeys = null)
    {
        deviceKeys ??= _crypto.GenerateDeviceKeyPair();
        using var dek = DataEncryptionKey.From(_crypto.RandomBytes(32));
        using var kek = KeyEncryptionKey.From(_crypto.RandomBytes(32));
        var wrappedDek = _crypto.WrapKey(kek, dek);
        var profiles = new List<ProfileRecord>();
        for (int i = 0; i < profileCount; i++)
        {
            // Make each profile unique and non-empty so the file is properly formed.
            var payload = Encoding.UTF8.GetBytes($"profile-{i}-empty-body");
            var payloadEnc = _crypto.Encrypt(dek, payload, _crypto.RandomBytes(32));
            profiles.Add(new ProfileRecord
            {
                Id = _crypto.NewUuidV7(),
                Name = $"profile-{i}",
                Color = ProfileColor.Green,
                Settings = ProfileSettings.DefaultProd(),
                WrappedDek = wrappedDek,
                PayloadNonce = payloadEnc.Nonce,
                PayloadTag = payloadEnc.Tag,
                EncryptedPayload = payloadEnc.Ciphertext
            });
        }
        return new VaultRecord
        {
            AppBuildHash = _format.ComputeBuildHash(),
            VaultUuid = _crypto.NewUuidV7(),
            Argon2Params = new Argon2Params { Time = argonTime, Memory = argonMemory, Parallelism = parallelism, KeyLength = 32 },
            Salt = _crypto.RandomBytes(32),
            VerifyTag = _crypto.RandomBytes(32),
            DevicePublicKey = deviceKeys.PublicKey,
            Signature = new byte[64],
            VectorClock = new VectorClock().Increment("test-device"),
            Profiles = profiles
        };
    }

    // ---- FMT-RW-01: roundtrip preserves data ----
    [Fact]
    public void Encode_Decode_Roundtrip_PreservesAllFields()
    {
        var original = MakeRecord(profileCount: 2);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();

        var encoded = _format.Encode(original, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);

        decoded.VaultUuid.Should().Be(original.VaultUuid);
        decoded.Argon2Params.Memory.Should().Be(original.Argon2Params.Memory);
        decoded.Argon2Params.Time.Should().Be(original.Argon2Params.Time);
        decoded.Argon2Params.Parallelism.Should().Be(original.Argon2Params.Parallelism);
        decoded.Salt.Should().Equal(original.Salt);
        decoded.VerifyTag.Should().Equal(original.VerifyTag);
        decoded.DevicePublicKey.Bytes.Should().Equal(original.DevicePublicKey.Bytes);
        decoded.Profiles.Count.Should().Be(2);
        decoded.Profiles[0].Name.Should().Be("profile-0");
        decoded.Profiles[1].Name.Should().Be("profile-1");
        decoded.VectorClock.Counters.Should().BeEquivalentTo(original.VectorClock.Counters);
    }

    [Fact]
    public void Encode_Decode_SignatureIsVerifiable()
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var original = MakeRecord(deviceKeys: deviceKeys);
        var encoded = _format.Encode(original, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.SignedRegion.Should().NotBeNull();
        _crypto.Verify(decoded.DevicePublicKey, decoded.SignedRegion!, decoded.Signature).Should().BeTrue();
    }

    // ---- FMT-RW-02: large vault ----
    [Fact]
    public void Encode_Decode_LargeRecord_Roundtrip()
    {
        var original = MakeRecord(profileCount: 50);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(original, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.Profiles.Count.Should().Be(50);
    }

    // ---- FMT-RW-03: special characters / UTF-8 ----
    [Fact]
    public void Encode_Decode_Utf8Strings_Roundtrip()
    {
        var record = MakeRecord();
        var modified = new VaultRecord
        {
            AppBuildHash = record.AppBuildHash,
            VaultUuid = record.VaultUuid,
            Argon2Params = record.Argon2Params,
            Salt = record.Salt,
            VerifyTag = record.VerifyTag,
            DevicePublicKey = record.DevicePublicKey,
            Signature = record.Signature,
            VectorClock = record.VectorClock,
            Profiles = new[] { record.Profiles[0] with { Name = "娴嬭瘯妗ｆ鍚?馃攼" } }
        };
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(modified, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.Profiles[0].Name.Should().Be("娴嬭瘯妗ｆ鍚?馃攼");
    }

    // ---- FMT-RW-04: empty vault (no profiles) ----
    [Fact]
    public void Encode_Decode_EmptyVault_Roundtrip()
    {
        var record = MakeRecord(profileCount: 0);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.Profiles.Should().BeEmpty();
    }

    // ---- FMT-RW-05: 16-byte salt slot (v0.1 deviation) ----
    [Fact]
    public void Encode_Decode_DefaultSalt_Is32Bytes()
    {
        var record = MakeRecord();
        record.Salt.Length.Should().Be(32);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        decoded.Salt.Length.Should().Be(32);
    }

    // ---- FMT-INTEG-01: tamper any header byte 鈫?signature fails ----
    [Theory]
    [InlineData(6)]   // app build hash
    [InlineData(14)]  // vault UUID
    [InlineData(30)]  // argon2id m
    [InlineData(38)]  // argon2id p
    [InlineData(39)]  // KDF salt
    [InlineData(71)]  // verify tag
    [InlineData(103)] // device PK
    public void Decode_TamperedHeaderByte_ProducesSignedRegionThatFailsVerify(int byteOffset)
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord(deviceKeys: deviceKeys);
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        var bytes = encoded.ToArray();
        bytes[byteOffset] ^= 1;
        var tamperedRecord = _format.Decode(bytes);
        _crypto.Verify(tamperedRecord.DevicePublicKey, tamperedRecord.SignedRegion!, tamperedRecord.Signature).Should().BeFalse();
    }

    [Fact]
    public void Decode_TamperedMagic_ThrowsFileCorruptException()
    {
        // Special case: byte 0 is the magic, and an invalid magic causes early
        // parse failure (FileCorruptException) rather than a signature failure.
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord(deviceKeys: deviceKeys);
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        encoded[0] = 0xFF;
        var act = () => _format.Decode(encoded);
        act.Should().Throw<FileCorruptException>().WithMessage("*Invalid magic*");
    }

    // ---- FMT-INTEG-02: invalid magic 鈫?exception ----
    [Fact]
    public void Decode_InvalidMagic_ThrowsFileCorruptException()
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        encoded[0] = 0xFF;
        var act = () => _format.Decode(encoded);
        act.Should().Throw<FileCorruptException>().WithMessage("*Invalid magic*");
    }

    [Fact]
    public void Decode_TruncatedFile_ThrowsFileCorruptException()
    {
        var bytes = new byte[50];
        var act = () => _format.Decode(bytes);
        act.Should().Throw<FileCorruptException>().WithMessage("*too short*");
    }

    [Fact]
    public void Decode_EmptyFile_ThrowsFileCorruptException()
    {
        var act = () => _format.Decode(Array.Empty<byte>());
        act.Should().Throw<FileCorruptException>();
    }

    // ---- FMT-INTEG-03: invalid header version → exception ----
    [Fact]
    public void Decode_UnsupportedHeaderVersion_ThrowsFileCorruptException()
    {
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord();
        var encoded = _format.Encode(record, deviceKeys.PrivateKey);
        // Header version bytes are 4-5 (LE ushort). Change to major=3 (unsupported).
        encoded[4] = 0x03;
        var act = () => _format.Decode(encoded);
        act.Should().Throw<FileCorruptException>().WithMessage("*Unsupported header version*");
    }

    // ---- FMT-INTEG-04: unknown device signature 鈫?verifiable in principle ----
    [Fact]
    public void Decode_UnknownDeviceSignature_CanBeDetectedViaVerify()
    {
        var realKeys = _crypto.GenerateDeviceKeyPair();
        var record = MakeRecord();
        var encoded = _format.Encode(record, realKeys.PrivateKey);
        var decoded = _format.Decode(encoded);
        // Wrong device keys
        var wrongKeys = _crypto.GenerateDeviceKeyPair();
        _crypto.Verify(wrongKeys.PublicKey, decoded.SignedRegion!, decoded.Signature).Should().BeFalse();
    }

    // ---- Build hash ----
    [Fact]
    public void ComputeBuildHash_Returns8Bytes_Deterministic()
    {
        var h1 = _format.ComputeBuildHash();
        var h2 = _format.ComputeBuildHash();
        h1.Length.Should().Be(8);
        h1.Should().Equal(h2);
    }
}
