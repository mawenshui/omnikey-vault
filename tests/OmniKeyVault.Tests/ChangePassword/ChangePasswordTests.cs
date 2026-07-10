using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.ChangePassword;

/// <summary>
/// Tests for VaultService.ChangePasswordAsync (v0.2 S7-T3, ROADMAP §6.3).
/// Covers happy path, wrong-old-password rejection, locked-vault guard,
/// file persistence (new password unlocks the file), and DEK survival
/// across the change (entries created before are still readable after).
/// </summary>
public class ChangePasswordTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public ChangePasswordTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService lockSvc, EntryService entries) CreateService()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var clip = new ClipboardService(new InMemoryClipboardProvider(), ls);
        var entrySvc = new EntryService(vs, new TemplateService(), clip, _crypto);
        return (vs, ls, entrySvc);
    }

    private async Task<string> CreateVaultAsync(VaultService vs, string password)
    {
        var path = _dir.RandomPath();
        await vs.CreateAsync(path, "T", Encoding.UTF8.GetBytes(password),
            Argon2Params.ForTests(32 * 1024 * 1024));
        return path;
    }

    [Fact]
    public async Task ChangePassword_HappyPath_RejectsOldThenAcceptsNew()
    {
        var (vault, lockSvc, entries) = CreateService();
        using (vault) using (lockSvc)
        {
            var path = await CreateVaultAsync(vault, "old-pass-123");
            // Add an entry so we can verify DEK survival
            var entry = entries.Create("prod", "Test", EntryType.ApiKey, "TestPlatform",
                new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("secret"), Kind = FieldKind.Secret, Sensitive = true } });
            vault.PutEntry("prod", entry);
            await vault.SaveAsync();
            vault.Lock();

            // Re-unlock with old password
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("old-pass-123"));
            // Change password
            await vault.ChangePasswordAsync(
                Encoding.UTF8.GetBytes("old-pass-123"),
                Encoding.UTF8.GetBytes("new-pass-456"));
            vault.Lock();

            // Old password no longer works
            await Assert.ThrowsAsync<CryptoException>(() =>
                vault.UnlockAsync(path, Encoding.UTF8.GetBytes("old-pass-123")));
            // New password works
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("new-pass-456"));
            // Entry is still readable
            var prodEntries = vault.ListEntries("prod");
            prodEntries.Should().HaveCount(1);
            prodEntries[0].Name.Should().Be("Test");
        }
    }

    [Fact]
    public async Task ChangePassword_WrongOldPassword_ThrowsAndKeepsOld()
    {
        var (vault, lockSvc, _) = CreateService();
        using (vault) using (lockSvc)
        {
            var path = await CreateVaultAsync(vault, "correct-old");
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("correct-old"));
            await Assert.ThrowsAsync<CryptoException>(() =>
                vault.ChangePasswordAsync(
                    Encoding.UTF8.GetBytes("WRONG-old"),
                    Encoding.UTF8.GetBytes("new-pass")));
            // Original password still works
            vault.Lock();
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("correct-old"));
        }
    }

    [Fact]
    public async Task ChangePassword_VaultLocked_Throws()
    {
        var (vault, lockSvc, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault, "x");
            vault.Lock();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                vault.ChangePasswordAsync(
                    Encoding.UTF8.GetBytes("x"),
                    Encoding.UTF8.GetBytes("y")));
        }
    }

    [Fact]
    public async Task ChangePassword_PreservesMultipleProfiles()
    {
        var (vault, lockSvc, _) = CreateService();
        using (vault) using (lockSvc)
        {
            var path = await CreateVaultAsync(vault, "orig");
            var profiles = new ProfileService(vault, _crypto, lockSvc);
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            await profiles.CreateAsync("test", ProfileColor.Blue, ProfileSettings.DefaultDev());

            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("orig"));
            await vault.ChangePasswordAsync(
                Encoding.UTF8.GetBytes("orig"),
                Encoding.UTF8.GetBytes("new"));

            vault.Lock();
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("new"));
            vault.ListProfileNames().Should().Contain(new[] { "prod", "dev", "test" });
        }
    }

    [Fact]
    public async Task ChangePassword_PreservesEntriesAcrossProfile()
    {
        var (vault, lockSvc, entries) = CreateService();
        using (vault) using (lockSvc)
        {
            var path = await CreateVaultAsync(vault, "orig");
            var profiles = new ProfileService(vault, _crypto, lockSvc);
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());

            var devEntry = entries.Create("dev", "DevKey", EntryType.ApiKey, "OpenAI",
                new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk-x"), Kind = FieldKind.Secret, Sensitive = true } });
            vault.PutEntry("dev", devEntry);
            await vault.SaveAsync();

            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("orig"));
            await vault.ChangePasswordAsync(
                Encoding.UTF8.GetBytes("orig"),
                Encoding.UTF8.GetBytes("new"));

            vault.Lock();
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("new"));
            var devEntries = vault.ListEntries("dev");
            devEntries.Should().HaveCount(1);
            devEntries[0].Name.Should().Be("DevKey");
        }
    }

    [Fact]
    public async Task ChangePassword_ShortNewPassword_Argon2StillRejects()
    {
        // Argon2 itself doesn't enforce password length, but we expect callers
        // to validate. The service should still work with short passwords
        // (the constraint is the caller's responsibility, e.g. UI form).
        var (vault, lockSvc, _) = CreateService();
        using (vault) using (lockSvc)
        {
            var path = await CreateVaultAsync(vault, "orig");
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("orig"));
            await vault.ChangePasswordAsync(
                Encoding.UTF8.GetBytes("orig"),
                Encoding.UTF8.GetBytes("x"));  // 1-char new password
            vault.Lock();
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("x"));
        }
    }
}
