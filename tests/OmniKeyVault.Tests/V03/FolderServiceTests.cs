using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>
/// Phase 10: Tests for FolderService (extracted from VaultService in Phase 7).
/// Verifies folder CRUD operations, uniqueness constraints, and recursive delete.
/// </summary>
public class FolderServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();

    public FolderServiceTests(TempVaultDir dir) => _dir = dir;

    private async Task<(VaultService vault, LockService lockSvc, FolderService folders)> CreateServicesAsync()
    {
        var path = _dir.RandomPath();
        var ls = new LockService(_crypto);
        var ks = new DeviceKeystore();
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", ks);
        await vs.CreateAsync(path, "test", Encoding.UTF8.GetBytes("password123"),
            Argon2Params.ForTests(32 * 1024 * 1024));
        var folders = new FolderService(vs, ls);
        return (vs, ls, folders);
    }

    [Fact]
    public async Task List_EmptyVault_ReturnsEmptyList()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var result = folders.List("prod");
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Create_AddsFolderToList()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var folder = folders.Create("prod", "test-folder");
            folder.Name.Should().Be("test-folder");
            folder.Id.Should().NotBeEmpty();

            var list = folders.List("prod");
            list.Should().ContainSingle(f => f.Name == "test-folder");
        }
    }

    [Fact]
    public async Task Create_DuplicateName_ThrowsConflict()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            folders.Create("prod", "folder-a");
            var act = () => folders.Create("prod", "folder-a");
            act.Should().Throw<NameConflictException>();
        }
    }

    [Fact]
    public async Task Create_WithParent_CreatesNested()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var parent = folders.Create("prod", "parent");
            var child = folders.Create("prod", "child", parent.Id);
            child.ParentId.Should().Be(parent.Id);
        }
    }

    [Fact]
    public async Task Rename_UpdatesFolderName()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var folder = folders.Create("prod", "old-name");
            folders.Rename("prod", folder.Id, "new-name");
            var list = folders.List("prod");
            list.Should().ContainSingle(f => f.Name == "new-name");
        }
    }

    [Fact]
    public async Task Delete_RemovesFolder()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var folder = folders.Create("prod", "to-delete");
            folders.Delete("prod", folder.Id);
            folders.List("prod").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Delete_RecursiveDeletesSubFolders()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var parent = folders.Create("prod", "parent");
            var child = folders.Create("prod", "child", parent.Id);
            var grandchild = folders.Create("prod", "grandchild", child.Id);

            folders.Delete("prod", parent.Id);

            var remaining = folders.List("prod");
            remaining.Should().NotContain(f => f.Id == parent.Id);
            remaining.Should().NotContain(f => f.Id == child.Id);
            remaining.Should().NotContain(f => f.Id == grandchild.Id);
        }
    }

    [Fact]
    public async Task Create_EmptyName_ThrowsValidation()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var act = () => folders.Create("prod", "");
            act.Should().Throw<ValidationException>();
        }
    }

    [Fact]
    public async Task Create_OnNonExistentProfile_Throws()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var act = () => folders.Create("nonexistent", "test");
            act.Should().Throw<ProfileNotFoundException>();
        }
    }

    [Fact]
    public async Task List_OnNonExistentProfile_ReturnsEmpty()
    {
        var (vs, ls, folders) = await CreateServicesAsync();
        using (vs) using (ls)
        {
            var result = folders.List("nonexistent");
            result.Should().BeEmpty();
        }
    }
}
