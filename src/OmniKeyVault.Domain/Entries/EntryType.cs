namespace OmniKeyVault.Domain;

/// <summary>
/// Entry type enumeration per OKV_FORMAT.md §3.4.
/// Serialized as a single byte in the binary format.
/// </summary>
public enum EntryType : byte
{
    ApiKey = 1,
    OAuth = 2,
    Certificate = 3,
    SshKey = 4,
    Note = 5,
    Custom = 255
}
