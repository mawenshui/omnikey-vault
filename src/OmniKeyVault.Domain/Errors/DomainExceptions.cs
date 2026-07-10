namespace OmniKeyVault.Domain;

/// <summary>
/// Errors raised by the application layer. Per ARCHITECTURE.md §8.3,
/// each category has a stable exit code for CLI use (CLI_SPEC.md §11).
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int InternalError = 1;
    public const int ArgumentError = 2;
    public const int VaultLocked = 3;
    public const int CryptoError = 4;
    public const int ProfileNotFound = 5;
    public const int IoError = 6;
    public const int EntryNotFound = 7;
    public const int FieldNotFound = 8;
    public const int NameConflict = 9;
    public const int PlatformUnsupported = 10;
    public const int ApiCallFailed = 11;
    public const int FormatUnsupported = 12;
    public const int FileCorrupt = 13;
    public const int SyncConflict = 14;
    public const int NetworkError = 20;
    public const int RecoveryKeyError = 30;
}

/// <summary> Raised when a Service is invoked while the Vault is locked. Exit 3. </summary>
public sealed class VaultLockedException : Exception
{
    public VaultLockedException(string message) : base(message) { }
}

/// <summary> Raised when the master password is incorrect or AEAD fails. Exit 4. </summary>
public sealed class CryptoException : Exception
{
    public CryptoException(string message) : base(message) { }
    public CryptoException(string message, Exception inner) : base(message, inner) { }
}

/// <summary> Raised when a referenced profile does not exist. Exit 5. </summary>
public sealed class ProfileNotFoundException : Exception
{
    public ProfileNotFoundException(string name) : base($"Profile '{name}' not found.") { }
}

/// <summary> Raised when a referenced entry does not exist. Exit 7. </summary>
public sealed class EntryNotFoundException : Exception
{
    public EntryNotFoundException(Guid id) : base($"Entry '{id}' not found.") { }
}

/// <summary> Raised when a referenced field does not exist. Exit 8. </summary>
public sealed class FieldNotFoundException : Exception
{
    public FieldNotFoundException(string key) : base($"Field '{key}' not found.") { }
}

/// <summary> Raised when a name (profile, entry) is already in use. Exit 9. </summary>
public sealed class NameConflictException : Exception
{
    public NameConflictException(string message) : base(message) { }
}

/// <summary> Raised when the .okv file is corrupt or unsupported. Exit 13. </summary>
public sealed class FileCorruptException : Exception
{
    public FileCorruptException(string message) : base(message) { }
    public FileCorruptException(string message, Exception inner) : base(message, inner) { }
}

/// <summary> Raised on argument validation failure. Exit 2. </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

/// <summary> Raised when an import/export format is not supported. Exit 12. </summary>
public sealed class FormatUnsupportedException : Exception
{
    public FormatUnsupportedException(string format) : base($"Unsupported format '{format}'.") { }
}
