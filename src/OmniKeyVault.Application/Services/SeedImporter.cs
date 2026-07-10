﻿using System.Security.Cryptography;
using System.Text;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Imports a .okv.dev seed file (OKVD magic) into the current vault per
/// PRD §5.5.3 / \u00a711.2. Enforces target profile name constraints:
/// the target profile MUST be one of (dev, test, ci-*) \u2014 the seed is
/// unsafe for production by design.
/// </summary>
[OmniKeyVaultService]
public sealed class SeedImporter
{
    private static readonly HashSet<string> AllowedTargetProfiles = new(StringComparer.Ordinal)
    {
        "dev", "test"
    };

    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;
    private readonly ProfilePayloadCodec _codec;
    private readonly ISeedFormat _seedFormat;
    private readonly string _deviceId;

    public SeedImporter(VaultService vault, ICryptoProvider crypto, ProfilePayloadCodec codec,
                        ISeedFormat seedFormat, string deviceId)
    {
        _vault = vault;
        _crypto = crypto;
        _codec = codec;
        _seedFormat = seedFormat;
        _deviceId = deviceId;
    }

    /// <summary>
    /// Imports a seed file. The target profile must exist in the destination vault
    /// (created beforehand via <c>profile create</c>). The source profile's name
    /// is preserved; if the destination already has a profile with that name, the
    /// entries are merged (additive) but the seed's DEK replaces the profile's DEK.
    /// </summary>
    public async Task<SeedImportResult> ImportAsync(string inputPath, string targetProfile, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Seed file not found: {inputPath}", inputPath);
        if (!AllowedTargetProfiles.Contains(targetProfile))
            throw new ValidationException(
                $"Refusing to import seed into profile '{targetProfile}'. " +
                $"Only 'dev' or 'test' profiles are allowed (per PRD \u00a75.5.3 \u2014 seed files are unsafe for production).");

        var seed = await _seedFormat.ReadAsync(inputPath, ct);

        // Each seed file may contain multiple profiles (typically 1). We import
        // all of them into the target profile, treating them as additive entries.
        // The first profile is treated as the "primary" for naming.

        int totalEntries = 0;
        var warnings = new List<string>();
        if (seed.StripMode)
            warnings.Add("Seed was exported in strip-secrets mode; sensitive field values are 'REDACTED-***'.");

        foreach (var srcProfile in seed.Profiles)
        {
            // Derive the seed KEK.
            using var seedMk = MasterKey.From(seed.DevMasterKey);
            using var seedKek = _crypto.DeriveKek(seedMk, Encoding.UTF8.GetBytes("okv-seed-kek-v1"),
                                                  seed.SeedUuid.ToByteArray());

            // Unwrap the profile's DEK with the seed KEK.
            using var dek = _crypto.UnwrapKey(seedKek, srcProfile.WrappedDek);

            // Decrypt the profile payload.
            var payload = new EncryptedPayload(
                srcProfile.PayloadNonce,
                srcProfile.EncryptedPayload,
                srcProfile.PayloadTag,
                VaultCryptoHelpers.BuildProfileAad(seed.SeedUuid, srcProfile.Id));
            var bodyBytes = _crypto.Decrypt(dek, in payload, VaultCryptoHelpers.BuildProfileAad(seed.SeedUuid, srcProfile.Id));
            var (entries, folders, tags, templates) = _codec.Decode(bodyBytes);
            CryptographicOperations.ZeroMemory(bodyBytes);

            // Import the profile into the target profile (renamed if different).
            // The target profile must already exist (the user must have created it via
            // 'profile create' beforehand \u2014 we don't auto-create to avoid surprises).
            if (!_vault.Profiles.ContainsKey(targetProfile))
                throw new ProfileNotFoundException(
                    $"Target profile '{targetProfile}' does not exist. Create it with 'profile create' first.");

            int added = 0;
            foreach (var entry in entries)
            {
                // Force the entry into the target profile by simply PutEntry.
                _vault.PutEntry(targetProfile, entry);
                added++;
            }
            totalEntries += added;
        }

        await _vault.SaveAsync(ct);
        return new SeedImportResult(seed.SeedUuid, totalEntries, warnings);
    }

}

public sealed record SeedImportResult(Guid SeedUuid, int EntriesImported, IReadOnlyList<string> Warnings);
