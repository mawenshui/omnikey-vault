using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Wraps the clipboard provider with Vault-unlock enforcement (ARCHITECTURE.md §4.2).
/// Copying sensitive values requires the Vault to be unlocked.
/// </summary>
[OmniKeyVaultService]
public sealed class ClipboardService
{
    private readonly IClipboardProvider _clipboard;
    private readonly LockService _lock;

    public ClipboardService(IClipboardProvider clipboard, LockService lockService)
    {
        _clipboard = clipboard;
        _lock = lockService;
    }

    /// <summary>
    /// Copies the given value to the clipboard. Auto-clears after
    /// <paramref name="clearAfterSeconds"/> seconds (default 8, per PRD §5.11).
    /// </summary>
    public void CopySensitive(string value, int clearAfterSeconds = 8)
    {
        _lock.EnsureUnlocked();
        if (string.IsNullOrEmpty(value))
            throw new ValidationException("Cannot copy empty value.");
        _clipboard.Copy(value, clearAfterSeconds);
    }

    /// <summary>Forces immediate clear of the clipboard.</summary>
    public void ClearNow() => _clipboard.ClearNow();

    public string? CurrentContent => _clipboard.CurrentContent;
}
