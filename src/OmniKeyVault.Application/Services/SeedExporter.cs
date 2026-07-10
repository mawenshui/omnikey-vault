﻿using System.Security.Cryptography;
using System.Text;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Exports a Vault profile to a .okv.dev seed file (OKVD magic).
/// Per PRD §5.5.3 / OKV_FORMAT.md §11.
///
/// SECURITY: The output file contains a plaintext Dev Master Key in its
/// header \u2014 the file is the credential. The exporter rejects production
/// profiles (prod) by default (or accepts only when <see cref="AllowProdProfile"/>
/// is set). Optionally redacts sensitive fields when <see cref="StripSecrets"/> is true.
/// </summary>
[OmniKeyVaultService]
public sealed class SeedExporter
{
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;
    private readonly ProfilePayloadCodec _codec;
    private readonly ISeedFormat _seedFormat;
    private readonly string _deviceId;

    public SeedExporter(VaultService vault, ICryptoProvider crypto, ProfilePayloadCodec codec,
                        ISeedFormat seedFormat, string deviceId)
    {
        _vault = vault;
        _crypto = crypto;
        _codec = codec;
        _seedFormat = seedFormat;
        _deviceId = deviceId;
    }

    public bool StripSecrets { get; set; }
    public bool AllowProdProfile { get; set; }

    /// <summary>
    /// Exports the named profile to a .okv.dev file. Returns the SeedRecord that was written.
    /// </summary>
    public async Task<SeedRecord> ExportAsync(string sourceProfile, string outputPath, DevicePrivateKey? signingKey = null, CancellationToken ct = default)
    {
        if (!_vault.Profiles.ContainsKey(sourceProfile))
            throw new ProfileNotFoundException(sourceProfile);
        if (sourceProfile == "prod" && !AllowProdProfile)
            throw new ValidationException("Refusing to export 'prod' profile as a seed (no production data in dev files). Use --allow-prod-profile to override.");

        // 1. Generate a fresh random Dev Master Key (32B). This is the seed's credential.
        var devMasterKey = _crypto.RandomBytes(32);
        var seedUuid = _crypto.NewUuidV7();
        var devSalt = _crypto.RandomBytes(32);  // 32B salt slot per OKV_FORMAT \u00a711.2.

        // 2. Derive the seed KEK from Dev Master Key.
        //    We abuse MasterKey as a generic 32B "input key material" container.
        using var seedMk = MasterKey.From(devMasterKey);
        using var seedKek = _crypto.DeriveKek(seedMk, Encoding.UTF8.GetBytes("okv-seed-kek-v1"), seedUuid.ToByteArray());

        // 3. For each profile in the source vault, re-wrap its DEK with seed KEK and
        //    re-encrypt the payload bytes with the (unchanged) DEK. The payload is the
        //    same plaintext bytes; we just swap the wrapping key.
        var profile = _vault.GetProfile(sourceProfile);
        var dek = _vault.CurrentVault != null
            ? GetDekFromLockService(sourceProfile)
            : throw new VaultLockedException("Vault is locked.");
        var wrappedDek = _crypto.WrapKey(seedKek, dek);

        // 4. Optionally redact sensitive fields before encrypting the payload.
        var entries = profile.Entries;
        if (StripSecrets)
            entries = entries.Select(RedactSensitiveFields).ToList();
        var tags = VaultCryptoHelpers.CollectTags(entries);
        var payloadBytes = _codec.Encode(entries, profile.Folders, tags, profile.Templates);
        var aad = VaultCryptoHelpers.BuildProfileAad(seedUuid, profile.Id);
        var payload = _crypto.Encrypt(dek, payloadBytes, aad);
        CryptographicOperations.ZeroMemory(payloadBytes);

        var profileRecord = new ProfileRecord
        {
            Id = profile.Id,
            Name = profile.Name,
            Color = profile.Color,
            Settings = profile.Settings,
            WrappedDek = wrappedDek,
            PayloadNonce = payload.Nonce,
            PayloadTag = payload.Tag,
            EncryptedPayload = payload.Ciphertext
        };

        // 5. Build the SeedRecord and write it.
        var record = new SeedRecord
        {
            AppBuildHash = _seedFormat.ComputeBuildHash(),
            SeedUuid = seedUuid,
            DevMasterKey = devMasterKey,
            DevSalt = devSalt,
            StripMode = StripSecrets,
            Profiles = new[] { profileRecord },
            Signature = new byte[64]  // filled by SeedFormat.Encode
        };

        if (signingKey == null)
        {
            // No device key supplied \u2014 generate an ephemeral one so the file is still signed.
            // In practice the CLI supplies the device's private key.
            var kp = _crypto.GenerateDeviceKeyPair();
            signingKey = kp.PrivateKey;
        }
        await _seedFormat.WriteAsync(outputPath, record, signingKey, ct);
        return record;
    }


    private DataEncryptionKey GetDekFromLockService(string profileName)
    {
        // Reach into the existing LockService via VaultService.GetDek (already internal helper).
        // We don't have direct access, so we expose via VaultService.
        return _vault.GetDekForSeed(profileName);
    }

    private static Entry RedactSensitiveFields(Entry e)
    {
        var redactedFields = e.Fields.Select(f =>
            f.Sensitive ? f with { Value = FieldCodec.Encode("REDACTED-***") } : f).ToList();
        return e with { Fields = redactedFields };
    }
}
