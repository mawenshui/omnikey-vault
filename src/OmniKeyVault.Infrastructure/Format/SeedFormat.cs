using System.IO;
using System.Security.Cryptography;
using System.Text;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// Binary .okv.dev seed file format per OKV_FORMAT.md §11.
///
/// Header layout (offset / length / field):
///   0    4    Magic            "OKVD" (0x4F 0x4B 0x56 0x44)  \u2014 NOT the production "OKV1" magic
///   4    2    Header Version   0x01 0x00
///   6    8    App Build Hash
///   14   16   Seed UUID
///   30   32   Dev Master Key (plaintext 32B \u2014 the file IS the "password" for the seed)
///   62   32   Dev Salt
///   94   1    Strip Mode (0=full, 1=strip-secrets)
///   95   var  Profiles section (same binary shape as .okv \u00a75; DEK is wrapped with a seed-derived KEK)
///   var  var  (no vector clock in seed files \u2014 seeds are not versioned)
///   var  64   Ed25519 Signature (covers everything before it)
///
/// SECURITY: This file format is INTENTIONALLY weak against an attacker who
/// obtains the file (per PRD \u00a75.5.3). The Dev Master Key is plaintext on disk.
/// The seed is meant for dev / test data distribution only. The CLI enforces
/// that seed files can ONLY be imported into dev/test profiles (see SeedImporter).
/// </summary>
public sealed class SeedFormat : ISeedFormat
{
    public static readonly byte[] Magic = { 0x4F, 0x4B, 0x56, 0x44 };
    public const ushort HeaderVersion = 0x0001;

    public byte[] ComputeBuildHash()
    {
        var buildId = $"okv-1.0.0-seed;.NET-{Environment.Version};bc-2.4.0";
        var full = SHA256.HashData(Encoding.UTF8.GetBytes(buildId));
        var result = new byte[8];
        Buffer.BlockCopy(full, 0, result, 0, 8);
        return result;
    }

    public byte[] Encode(SeedRecord record, DevicePrivateKey signingKey)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false))
        {
            bw.Write(Magic);
            bw.Write((ushort)HeaderVersion);
            bw.Write(record.AppBuildHash);
            WriteGuid(bw, record.SeedUuid);
            bw.Write(record.DevMasterKey);
            bw.Write(record.DevSalt);
            bw.Write(record.StripMode ? (byte)1 : (byte)0);
            // Profiles (same binary shape as .okv \u00a75).
            bw.Write((uint)record.Profiles.Count);
            foreach (var p in record.Profiles)
                WriteProfile(bw, p);
        }
        var toSign = ms.ToArray();
        var sig = signingKey is null
            ? new byte[64]
            : SignWith(signingKey, toSign);
        using var ms2 = new MemoryStream();
        ms2.Write(toSign, 0, toSign.Length);
        ms2.Write(sig, 0, sig.Length);
        return ms2.ToArray();
    }

    public SeedRecord Decode(byte[] bytes)
    {
        if (bytes.Length < 95 + 64)
            throw new FileCorruptException("File too short to be a valid .okv.dev seed file.");

        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

        var magic = br.ReadBytes(4);
        if (!magic.SequenceEqual(Magic))
            throw new FileCorruptException($"Invalid seed magic bytes. Expected 'OKVD', got 0x{Convert.ToHexString(magic)}.");
        var headerVer = br.ReadUInt16();
        if (headerVer != HeaderVersion)
            throw new FileCorruptException($"Unsupported seed header version: {headerVer >> 8}.{headerVer & 0xFF}; expected 1.0.");

        var buildHash = br.ReadBytes(8);
        var uuid = ReadGuid(br);
        var devMasterKey = br.ReadBytes(32);
        var devSalt = br.ReadBytes(32);
        var stripMode = br.ReadByte() != 0;

        var profiles = new List<ProfileRecord>();
        var profileCount = br.ReadUInt32();
        for (uint i = 0; i < profileCount; i++)
            profiles.Add(ReadProfile(br));

        var signedLen = (int)ms.Position;
        var signedRegion = new byte[signedLen];
        Buffer.BlockCopy(bytes, 0, signedRegion, 0, signedLen);
        var signature = new byte[64];
        Buffer.BlockCopy(bytes, bytes.Length - 64, signature, 0, 64);

        return new SeedRecord
        {
            AppBuildHash = buildHash,
            SeedUuid = uuid,
            DevMasterKey = devMasterKey,
            DevSalt = devSalt,
            StripMode = stripMode,
            Profiles = profiles,
            Signature = signature,
            SignedRegion = signedRegion
        };
    }

    public async Task<SeedRecord> ReadAsync(string path, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return Decode(bytes);
    }

    public async Task WriteAsync(string path, SeedRecord record, DevicePrivateKey signingKey, CancellationToken ct = default)
    {
        var bytes = Encode(record, signingKey);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Atomic write per OKV_FORMAT.md \u00a710.3: tmp -> fsync -> rename.
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

    // ---- Profile section (mirrors VaultFormat for compatibility) ----
    private static void WriteProfile(BinaryWriter bw, ProfileRecord p)
    {
        WriteGuid(bw, p.Id);
        WriteString(bw, p.Name);
        bw.Write((byte)1); // version
        bw.Write((byte)p.Color);
        // Wrapped DEK
        bw.Write(p.WrappedDek.Nonce);
        bw.Write((uint)p.WrappedDek.Ciphertext.Length);
        bw.Write(p.WrappedDek.Ciphertext);
        bw.Write(p.WrappedDek.Tag);
        // Settings
        bw.Write(p.Settings.ParticipateInSync ? (byte)1 : (byte)0);
        bw.Write(p.Settings.AutoLockOnSwitch ? (byte)1 : (byte)0);
        bw.Write(p.Settings.IdleLockMinutes);
        // Encrypted payload
        bw.Write(p.PayloadNonce);
        bw.Write((uint)p.PayloadTag.Length);
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

    private static void WriteGuid(BinaryWriter bw, Guid g) => bw.Write(g.ToByteArray());

    private static Guid ReadGuid(BinaryReader br)
    {
        var bytes = br.ReadBytes(16);
        return new Guid(bytes);
    }

    private static byte[] SignWith(DevicePrivateKey key, ReadOnlySpan<byte> data)
    {
        var crypto = new SodiumCryptoProvider();
        return crypto.Sign(key, data);
    }
}
