using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Profile management service per PRD §5.1 / ROADMAP S3-T1 / ARCHITECTURE.md §4.2.
/// A Profile is a named namespace within a Vault with its own DEK (depth-of-defense
/// per SECURITY.md §1.4). Profiles can be created / updated / deleted / listed.
/// All write methods require the Vault to be unlocked (INV-04).
/// </summary>
[OmniKeyVaultService]
public sealed class ProfileService
{
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;
    private readonly LockService _lock;

    public ProfileService(VaultService vault, ICryptoProvider crypto, LockService lockService)
    {
        _vault = vault;
        _crypto = crypto;
        _lock = lockService;
    }

    /// <summary>Returns summary information about all profiles (CLI `profile list`).</summary>
    public IReadOnlyList<ProfileInfo> List()
    {
        _lock.EnsureUnlocked();
        var counts = _vault.GetEntryCounts();
        var list = new List<ProfileInfo>();
        foreach (var name in _vault.ListProfileNames())
        {
            var p = _vault.GetProfile(name);
            list.Add(new ProfileInfo(
                Name: p.Name,
                Color: p.Color.ToString(),
                EntryCount: counts.TryGetValue(name, out var c) ? c : 0,
                ParticipateInSync: p.Settings.ParticipateInSync,
                IdleLockMinutes: p.Settings.IdleLockMinutes
            ));
        }
        return list;
    }

    /// <summary>Creates a new profile with a fresh DEK. Persists immediately.</summary>
    public async Task<Profile> CreateAsync(string name, ProfileColor color, ProfileSettings? settings = null, CancellationToken ct = default)
    {
        var profile = _vault.CreateProfile(name, color, settings);
        await _vault.SaveAsync(ct);
        return profile;
    }

    /// <summary>Deletes a profile (must not be the last). Persists immediately.</summary>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        _vault.DeleteProfile(name);
        await _vault.SaveAsync(ct);
    }

    /// <summary>Updates profile settings (sync participation, idle-lock). Persists immediately.</summary>
    public async Task UpdateSettingsAsync(string name, ProfileSettings settings, CancellationToken ct = default)
    {
        _vault.UpdateProfileSettings(name, settings);
        await _vault.SaveAsync(ct);
    }

    /// <summary>Returns detailed info for one profile (CLI `profile info`).</summary>
    public ProfileInfo? GetInfo(string name)
    {
        _lock.EnsureUnlocked();
        if (!_vault.Profiles.TryGetValue(name, out var p)) return null;
        return new ProfileInfo(
            Name: p.Name,
            Color: p.Color.ToString(),
            EntryCount: p.Entries.Count,
            ParticipateInSync: p.Settings.ParticipateInSync,
            IdleLockMinutes: p.Settings.IdleLockMinutes
        );
    }
}

/// <summary>
/// Lightweight summary of a Profile, used by CLI `profile list` and `profile info`.
/// </summary>
public sealed record ProfileInfo(
    string Name,
    string Color,
    int EntryCount,
    bool ParticipateInSync,
    int IdleLockMinutes
);
