namespace OmniKeyVault.Domain;

/// <summary>
/// Profile color enumeration per OKV_FORMAT.md §3.3.
/// Used for UI banner coloring (prod=green / dev=yellow / test=blue).
/// Serialized as a single byte in the binary format.
/// </summary>
public enum ProfileColor : byte
{
    Green = 1,
    Yellow = 2,
    Blue = 3,
    Red = 4,
    Purple = 5
}
