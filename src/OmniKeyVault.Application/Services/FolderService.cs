using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Folder management service per ARCHITECTURE.md §4.2 / MANUAL §10.2.
/// Extracted from <see cref="VaultService"/> (Phase 7 God-Class split) to
/// isolate folder CRUD concerns. All methods require the Vault to be
/// unlocked (INV-04) and delegate state mutations to <see cref="VaultService"/>.
///
/// Threading: NOT thread-safe — caller must serialize (same model as VaultService).
/// </summary>
[OmniKeyVaultService]
public sealed class FolderService
{
    private readonly VaultService _vault;
    private readonly LockService _lock;

    public FolderService(VaultService vault, LockService lockService)
    {
        _vault = vault;
        _lock = lockService;
    }

    /// <summary>Returns all folders in the named profile, ordered by name.</summary>
    public IReadOnlyList<Folder> List(string profileName)
    {
        _lock.EnsureUnlocked();
        return _vault.ListFolders(profileName);
    }

    /// <summary>Creates a new folder. Names are unique per (profile, parent).</summary>
    public Folder Create(string profileName, string folderName, Guid? parentId = null)
    {
        _lock.EnsureUnlocked();
        return _vault.CreateFolder(profileName, folderName, parentId);
    }

    /// <summary>Renames a folder.</summary>
    public void Rename(string profileName, Guid folderId, string newName)
    {
        _lock.EnsureUnlocked();
        _vault.RenameFolder(profileName, folderId, newName);
    }

    /// <summary>Deletes a folder. Entries in the folder are moved to the parent
    /// (or root when the folder has no parent). Sub-folders are deleted recursively.</summary>
    public void Delete(string profileName, Guid folderId)
    {
        _lock.EnsureUnlocked();
        _vault.DeleteFolder(profileName, folderId);
    }
}
