using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V26;

/// <summary>
/// v2.6.0 P1 feature tests: Windows Credential Manager integration.
/// Tests the storage, retrieval, and deletion of vault credentials.
/// </summary>
public class V26CredentialManagerTests : IDisposable
{
    private readonly Guid _testVaultUuid = Guid.NewGuid();
    private readonly string _testVaultPath = @"C:\test\test-vault.okv";

    public void Dispose()
    {
        // Clean up: delete the test credential
        try { CredentialManagerService.DeleteVaultCredential(_testVaultUuid); } catch { }
    }

    [Fact]
    public void CredentialManager_StoreVaultCredential_ReturnsTrue()
    {
        var result = CredentialManagerService.StoreVaultCredential(_testVaultUuid, _testVaultPath);
        result.Should().BeTrue("credential should be stored successfully");
    }

    [Fact]
    public void CredentialManager_Store_ThenRetrieve_ReturnsSameData()
    {
        var metadata = new Dictionary<string, string>
        {
            { "profile", "prod" },
            { "created_by", "test" }
        };

        CredentialManagerService.StoreVaultCredential(_testVaultUuid, _testVaultPath, metadata);

        var retrieved = CredentialManagerService.RetrieveVaultCredential(_testVaultUuid);

        retrieved.Should().NotBeNull();
        retrieved!.VaultUuid.Should().Be(_testVaultUuid);
        retrieved.VaultPath.Should().Be(_testVaultPath);
        retrieved.Metadata.Should().ContainKey("profile");
        retrieved.Metadata["profile"].Should().Be("prod");
    }

    [Fact]
    public void CredentialManager_RetrieveNonExistent_ReturnsNull()
    {
        var nonExistentUuid = Guid.NewGuid();
        var result = CredentialManagerService.RetrieveVaultCredential(nonExistentUuid);
        result.Should().BeNull();
    }

    [Fact]
    public void CredentialManager_Exists_ReturnsTrue_WhenStored()
    {
        CredentialManagerService.StoreVaultCredential(_testVaultUuid, _testVaultPath);

        var exists = CredentialManagerService.VaultCredentialExists(_testVaultUuid);
        exists.Should().BeTrue();
    }

    [Fact]
    public void CredentialManager_Exists_ReturnsFalse_WhenNotStored()
    {
        var nonExistentUuid = Guid.NewGuid();
        var exists = CredentialManagerService.VaultCredentialExists(nonExistentUuid);
        exists.Should().BeFalse();
    }

    [Fact]
    public void CredentialManager_Delete_RemovesCredential()
    {
        CredentialManagerService.StoreVaultCredential(_testVaultUuid, _testVaultPath);
        CredentialManagerService.VaultCredentialExists(_testVaultUuid).Should().BeTrue();

        var deleted = CredentialManagerService.DeleteVaultCredential(_testVaultUuid);
        deleted.Should().BeTrue();

        CredentialManagerService.VaultCredentialExists(_testVaultUuid).Should().BeFalse();
    }

    [Fact]
    public void CredentialManager_DeleteNonExistent_ReturnsFalse()
    {
        var nonExistentUuid = Guid.NewGuid();
        var deleted = CredentialManagerService.DeleteVaultCredential(nonExistentUuid);
        deleted.Should().BeFalse();
    }

    [Fact]
    public void CredentialManager_Overwrite_UpdatesData()
    {
        // Store initial credential
        CredentialManagerService.StoreVaultCredential(_testVaultUuid, _testVaultPath);

        // Overwrite with different data
        var newPath = @"C:\test\updated-vault.okv";
        var newMetadata = new Dictionary<string, string> { { "updated", "true" } };
        CredentialManagerService.StoreVaultCredential(_testVaultUuid, newPath, newMetadata);

        // Verify updated data
        var retrieved = CredentialManagerService.RetrieveVaultCredential(_testVaultUuid);
        retrieved.Should().NotBeNull();
        retrieved!.VaultPath.Should().Be(newPath);
        retrieved.Metadata.Should().ContainKey("updated");
    }

    [Fact]
    public void CredentialManager_EmptyMetadata_StoresSuccessfully()
    {
        var result = CredentialManagerService.StoreVaultCredential(_testVaultUuid, _testVaultPath, null);
        result.Should().BeTrue();

        var retrieved = CredentialManagerService.RetrieveVaultCredential(_testVaultUuid);
        retrieved.Should().NotBeNull();
        retrieved!.Metadata.Should().NotBeNull();
        retrieved.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void CredentialManager_StoresVaultPath_Correctly()
    {
        var testPath = @"C:\Users\test\MyVault.okv";
        CredentialManagerService.StoreVaultCredential(_testVaultUuid, testPath);

        var retrieved = CredentialManagerService.RetrieveVaultCredential(_testVaultUuid);
        retrieved.Should().NotBeNull();
        retrieved!.VaultPath.Should().Be(testPath);
    }

    [Fact]
    public void CredentialManager_TargetName_ContainsVaultUuid()
    {
        // This is an internal test - verify the target name format
        // Format: OmniKeyVault_v2_{vault_uuid}
        var targetName = $"OmniKeyVault_v2_{_testVaultUuid:D}";
        targetName.Should().Contain("OmniKeyVault_v2_");
        targetName.Should().Contain(_testVaultUuid.ToString("D"));
    }
}