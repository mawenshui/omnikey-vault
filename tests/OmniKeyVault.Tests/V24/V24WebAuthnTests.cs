using System.Text;
using FluentAssertions;
using OmniKeyVault.Cli;
using Xunit;

namespace OmniKeyVault.Tests.V24;

/// <summary>
/// v2.4.0 P1 feature tests: WebAuthn / FIDO2 biometric unlock via DPAPI.
/// Tests the registration, unlock, and unregistration flow.
/// </summary>
public class V24WebAuthnTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly string _testPassword = "TestPassword123!";

    public V24WebAuthnTests()
    {
        // Use a unique fake vault path for each test
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid():N}.okv");
    }

    public void Dispose()
    {
        // Clean up: unregister the biometric data for the test vault
        try { WebAuthnService.Unregister(_testVaultPath); } catch { }
    }

    [Fact]
    public async Task WebAuthn_IsAvailable_ReturnsTrue()
    {
        var available = await WebAuthnService.IsAvailableAsync();
        available.Should().BeTrue("DPAPI is available on all Windows versions");
    }

    [Fact]
    public async Task WebAuthn_Register_ThenIsRegistered_ReturnsTrue()
    {
        var pwBytes = Encoding.UTF8.GetBytes(_testPassword);

        var result = await WebAuthnService.RegisterAsync(_testVaultPath, pwBytes);

        result.Should().BeTrue();
        WebAuthnService.IsRegistered(_testVaultPath).Should().BeTrue();
    }

    [Fact]
    public async Task WebAuthn_Register_ThenUnlock_ReturnsCorrectPassword()
    {
        var pwBytes = Encoding.UTF8.GetBytes(_testPassword);

        await WebAuthnService.RegisterAsync(_testVaultPath, pwBytes);
        var decrypted = await WebAuthnService.UnlockAsync(_testVaultPath);

        decrypted.Should().NotBeNull();
        Encoding.UTF8.GetString(decrypted!).Should().Be(_testPassword);
    }

    [Fact]
    public async Task WebAuthn_IsRegistered_ReturnsFalse_WhenNotRegistered()
    {
        var unregisteredPath = Path.Combine(Path.GetTempPath(), $"unregistered-{Guid.NewGuid():N}.okv");
        WebAuthnService.IsRegistered(unregisteredPath).Should().BeFalse();
    }

    [Fact]
    public async Task WebAuthn_Unlock_ReturnsNull_WhenNotRegistered()
    {
        var unregisteredPath = Path.Combine(Path.GetTempPath(), $"unregistered-{Guid.NewGuid():N}.okv");
        var result = await WebAuthnService.UnlockAsync(unregisteredPath);
        result.Should().BeNull();
    }

    [Fact]
    public async Task WebAuthn_Unregister_RemovesRegistration()
    {
        var pwBytes = Encoding.UTF8.GetBytes(_testPassword);
        await WebAuthnService.RegisterAsync(_testVaultPath, pwBytes);

        WebAuthnService.IsRegistered(_testVaultPath).Should().BeTrue();

        WebAuthnService.Unregister(_testVaultPath);

        WebAuthnService.IsRegistered(_testVaultPath).Should().BeFalse();
    }

    [Fact]
    public async Task WebAuthn_Register_ZerosPasswordBuffer()
    {
        var pwBytes = Encoding.UTF8.GetBytes(_testPassword);

        await WebAuthnService.RegisterAsync(_testVaultPath, pwBytes);

        // After registration, the password buffer should be zeroed
        pwBytes.Should().AllBeEquivalentTo((byte)0, "password buffer should be zeroed after registration");
    }

    [Fact]
    public async Task WebAuthn_DifferentVaults_HaveIndependentRegistrations()
    {
        var vault1 = Path.Combine(Path.GetTempPath(), $"vault1-{Guid.NewGuid():N}.okv");
        var vault2 = Path.Combine(Path.GetTempPath(), $"vault2-{Guid.NewGuid():N}.okv");

        try
        {
            var pw1 = Encoding.UTF8.GetBytes("Password1!");
            var pw2 = Encoding.UTF8.GetBytes("Password2!");

            await WebAuthnService.RegisterAsync(vault1, pw1);
            await WebAuthnService.RegisterAsync(vault2, pw2);

            WebAuthnService.IsRegistered(vault1).Should().BeTrue();
            WebAuthnService.IsRegistered(vault2).Should().BeTrue();

            var decrypted1 = await WebAuthnService.UnlockAsync(vault1);
            var decrypted2 = await WebAuthnService.UnlockAsync(vault2);

            Encoding.UTF8.GetString(decrypted1!).Should().Be("Password1!");
            Encoding.UTF8.GetString(decrypted2!).Should().Be("Password2!");
        }
        finally
        {
            WebAuthnService.Unregister(vault1);
            WebAuthnService.Unregister(vault2);
        }
    }
}
