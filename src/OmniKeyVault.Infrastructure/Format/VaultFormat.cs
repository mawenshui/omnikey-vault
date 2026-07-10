using System.IO;
using System.Security.Cryptography;
using System.Text;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// Binary .okv file format reader/writer per OKV_FORMAT.md §4-§6.
///
/// Header layout (§4.1):
///   0   4   Magic           "OKV1" (0x4F 0x4B 0x56 0x01)
///   4   2   Header Version  0x01 0x00 (LE)
///   6   8   App Build Hash
///   14  16  Vault UUID      (16 bytes, GUID layout)
///   30  4   Argon2id m      (uint32 LE)
///   34  4   Argon2id t      (uint32 LE)
///   38  1   Argon2id p
///   39  32  KDF Salt
///   71  32  MK Verify Tag
///   103 32  Device Ed25519 PK
///   135 var Encrypted Profiles (§5)
///   var var Vault VectorClock (§6)
///   var 64  Ed25519 Signature (covers everything above)
///
/// All numbers are little-endian. Strings are UTF-8 length-prefixed (uint32 LE).
/// </summary>
public sealed class VaultFormat : IVaultFormat
{
    public static readonly byte[] Magic = { 0x4F, 0x4B, 0x56, 0x01 };
    public const ushort HeaderVersion = 0x0001;

    public byte[] ComputeBuildHash()
    {
        // Per OKV_FORMAT.md §4.3: SHA-256(git_commit_hash + dotnet_version + libsodium_version), first 8 bytes.
        // For v0.1 MVP we use a stable build identifier derived from assembly + runtime version.
        var buildId = $"okv-0.1.0;.NET-{Environment.Version};bc-2.4.0";
        var full = SHA256.HashData(Encoding.UTF8.GetBytes(buildId));
        var result = new byte[8];
        Buffer.BlockCopy(full, 0, result, 0, 8);
        return result;
    }

    public byte[] Encode(VaultRecord record, DevicePrivateKey signingKey)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false))
        {
            // Header (everything except signature)
            bw.Write(Magic);
            bw.Write((ushort)HeaderVersion); // LE ushort
            bw.Write(record.AppBuildHash);   // 8 bytes
            WriteGuid(bw, record.VaultUuid);
            bw.Write(record.Argon2Params.Memory); // uint32 LE
            bw.Write(record.Argon2Params.Time);   // uint32 LE
            bw.Write(record.Argon2Params.Parallelism); // 1 byte
            bw.Write(record.Salt);              // 32 bytes
            bw.Write(record.VerifyTag);         // 32 bytes
            bw.Write(record.DevicePublicKey.Bytes); // 32 bytes

            // Encrypted Profiles (§5)
            bw.Write((uint)record.Profiles.Count);
            foreach (var p in record.Profiles)
                WriteProfile(bw, p);

            // Vector Clock (§6)
            WriteVectorClock(bw, record.VectorClock);
        }

        // Signature covers everything in the buffer so far (OKV_FORMAT.md §13.1).
        var toSign = ms.ToArray();
        var sig = signingKey is null
            ? new byte[64]
            : SignWith(signingKey, toSign);

        using var ms2 = new MemoryStream();
        ms2.Write(toSign, 0, toSign.Length);
        ms2.Write(sig, 0, sig.Length);
        return ms2.ToArray();
    }

    public VaultRecord Decode(byte[] bytes)
    {
        if (bytes.Length < 135 + 64)
            throw new FileCorruptException("File too short to be a valid .okv file.");

        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

        // Magic + header version
        var magic = br.ReadBytes(4);
        if (!magic.SequenceEqual(Magic))
            throw new FileCorruptException($"Invalid magic bytes. Expected 'OKV1', got 0x{Convert.ToHexString(magic)}.");
        var headerVer = br.ReadUInt16();
        if (headerVer != HeaderVersion)
            throw new FileCorruptException($"Unsupported header version: {headerVer >> 8}.{headerVer & 0xFF}; expected 1.0.");

        var buildHash = br.ReadBytes(8);
        var uuid = ReadGuid(br);
        var m = br.ReadUInt32();
        var t = br.ReadUInt32();
        var p = br.ReadByte();
        var salt = br.ReadBytes(32);
        var verifyTag = br.ReadBytes(32);
        var devicePk = br.ReadBytes(32);

        var profiles = new List<ProfileRecord>();
        var profileCount = br.ReadUInt32();
        for (uint i = 0; i < profileCount; i++)
            profiles.Add(ReadProfile(br));

        var vc = ReadVectorClock(br);

        // Everything up to here is signed. The last 64 bytes are the signature.
        var signedLen = (int)ms.Position;
        var signedRegion = new byte[signedLen];
        Buffer.BlockCopy(bytes, 0, signedRegion, 0, signedLen);
        var signature = new byte[64];
        Buffer.BlockCopy(bytes, bytes.Length - 64, signature, 0, 64);

        return new VaultRecord
        {
            AppBuildHash = buildHash,
            VaultUuid = uuid,
            Argon2Params = new Argon2Params { Time = t, Memory = m, Parallelism = p, KeyLength = 32, SaltLength = (uint)salt.Length },
            Salt = salt,
            VerifyTag = verifyTag,
            DevicePublicKey = new DevicePublicKey(devicePk),
            Signature = signature,
            VectorClock = vc,
            Profiles = profiles,
            SignedRegion = signedRegion
        };
    }

    internal static byte[] ExtractSignedRegion(byte[] fullBytes) =>
        fullBytes.Take(fullBytes.Length - 64).ToArray();

    public async Task<VaultRecord> ReadAsync(string path, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return Decode(bytes);
    }

    public async Task WriteAsync(string path, VaultRecord record, DevicePrivateKey signingKey, CancellationToken ct = default)
    {
        var bytes = Encode(record, signingKey);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Atomic write per OKV_FORMAT.md §10.3: tmp -> fsync -> rename.
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await fs.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
            await fs.FlushAsync(ct);
            fs.Flush(flushToDisk: true);
        }
        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null);
        else
            File.Move(tmp, path);
    }

    // ---- Profile section (§5) ----
    private static void WriteProfile(BinaryWriter bw, ProfileRecord p)
    {
        WriteGuid(bw, p.Id);
        WriteString(bw, p.Name);
        // Wrapped DEK: nonce(24) + ct(32) + tag(16) = 72 bytes (DEK is 32B -> ct 32B + tag 16B = 48B; total 72B).
        // We write nonce(24) + ct(32) + tag(16) = 72 bytes; reader reads exactly that.
        bw.Write((byte)1); // version 1 of wrapped form
        bw.Write((byte)p.Color);
        // Wrapped key: nonce(24) + ciphertext(variable) + tag(16)
        bw.Write(p.WrappedDek.Nonce);
        bw.Write((uint)p.WrappedDek.Ciphertext.Length);
        bw.Write(p.WrappedDek.Ciphertext);
        bw.Write(p.WrappedDek.Tag);
        // Profile settings
        bw.Write(p.Settings.ParticipateInSync ? (byte)1 : (byte)0);
        bw.Write(p.Settings.AutoLockOnSwitch ? (byte)1 : (byte)0);
        bw.Write(p.Settings.IdleLockMinutes);
        // Encrypted payload: nonce(24) + ct(N) + tag(16)
        bw.Write(p.PayloadNonce);
        bw.Write((uint)p.PayloadTag.Length); // tag length (16)
        bw.Write(p.PayloadTag);
        bw.Write((uint)p.EncryptedPayload.Length);
        bw.Write(p.EncryptedPayload);
    }

    private static ProfileRecord ReadProfile(BinaryReader br)
    {
        var id = ReadGuid(br);
        var name = ReadString(br);
        var _ver = br.ReadByte();
        var color = (ProfileColor)br.ReadByte();
        var nonce = br.ReadBytes(24);
        var ctLen = br.ReadUInt32();
        var ct = br.ReadBytes((int)ctLen);
        var tag = br.ReadBytes(16);
        var partSync = br.ReadByte() != 0;
        var autoLockSwitch = br.ReadByte() != 0;
        var idleMin = br.ReadInt32();
        var payloadNonce = br.ReadBytes(24);
        var tagLen = br.ReadUInt32();
        var payloadTag = br.ReadBytes((int)tagLen);
        var payloadLen = br.ReadUInt32();
        var payload = br.ReadBytes((int)payloadLen);

        return new ProfileRecord
        {
            Id = id,
            Name = name,
            Color = color,
            Settings = new ProfileSettings
            {
                ParticipateInSync = partSync,
                AutoLockOnSwitch = autoLockSwitch,
                IdleLockMinutes = idleMin
            },
            WrappedDek = new WrappedKey(nonce, ct, tag),
            PayloadNonce = payloadNonce,
            PayloadTag = payloadTag,
            EncryptedPayload = payload
        };
    }

    // ---- Vector clock (§6) ----
    private static void WriteVectorClock(BinaryWriter bw, VectorClock vc)
    {
        bw.Write((uint)vc.Counters.Count);
        foreach (var (k, v) in vc.Counters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            WriteString(bw, k);
            bw.Write(v); // int64 LE
        }
    }

    private static VectorClock ReadVectorClock(BinaryReader br)
    {
        var count = br.ReadUInt32();
        var dict = new Dictionary<string, long>(StringComparer.Ordinal);
        for (uint i = 0; i < count; i++)
        {
            var k = ReadString(br);
            var v = br.ReadInt64();
            dict[k] = v;
        }
        return new VectorClock(dict);
    }

    // ---- Primitives ----
    private static void WriteString(BinaryWriter bw, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        bw.Write((uint)bytes.Length);
        bw.Write(bytes);
    }

    private static string ReadString(BinaryReader br)
    {
        var len = br.ReadUInt32();
        if (len > 1024 * 1024) throw new FileCorruptException($"String length {len} exceeds 1 MiB limit.");
        var bytes = br.ReadBytes((int)len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteGuid(BinaryWriter bw, Guid g)
    {
        // Guid.ToByteArray() returns the .NET GUID layout (mostly little-endian, with the
        // first 3 fields reversed). We preserve that exact layout on read for symmetry.
        bw.Write(g.ToByteArray());
    }

    private static Guid ReadGuid(BinaryReader br)
    {
        var bytes = br.ReadBytes(16);
        return new Guid(bytes);
    }

    private static byte[] SignWith(DevicePrivateKey key, ReadOnlySpan<byte> data)
    {
        // Ed25519 sign via the crypto provider.
        var crypto = new SodiumCryptoProvider();
        return crypto.Sign(key, data);
    }
}
