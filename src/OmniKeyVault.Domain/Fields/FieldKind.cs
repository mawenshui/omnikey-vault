namespace OmniKeyVault.Domain;

/// <summary>
/// Field kind enumeration per OKV_FORMAT.md §3.5.
/// Serialized as a single byte in the binary format.
/// </summary>
public enum FieldKind : byte
{
    Text = 1,
    Secret = 2,
    Url = 3,
    Number = 4,
    Date = 5,
    TotpUri = 6,
    FileRef = 7
}
