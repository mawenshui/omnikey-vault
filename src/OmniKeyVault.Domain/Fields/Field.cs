namespace OmniKeyVault.Domain;

/// <summary>
/// A single field within an Entry per OKV_FORMAT.md §3.5.
/// Immutable value object. Fields with <see cref="Kind"/> = Secret
/// must have <see cref="Sensitive"/> = true (enforced at template load).
///
/// P6-T1: <see cref="Value"/> is <c>byte[]</c> (not <c>string</c>) so that
/// sensitive field values can be zeroed on lock (INV-03). Use
/// <c>FieldCodec.Encode/Decode</c> for string ↔ byte[] conversion.
/// </summary>
public sealed record Field
{
    public required string Key { get; init; }
    /// <summary>P6-T1: Raw UTF-8 bytes. Use <c>FieldCodec.Decode(Value)</c>
    /// for string access, or the <see cref="ValueString"/> helper property.
    /// Sensitive values should be zeroed on lock via
    /// <c>CryptographicOperations.ZeroMemory</c>.</summary>
    public required byte[] Value { get; init; }
    public required FieldKind Kind { get; init; }
    public required bool Sensitive { get; init; }
    public string? Mask { get; init; }
    public FieldValidation? Validation { get; init; }

    /// <summary>P6-T1: Convenience property for string access. Decodes
    /// the UTF-8 bytes on each call. For hot paths, cache the result.</summary>
    public string ValueString => System.Text.Encoding.UTF8.GetString(Value);

    /// <summary>
    /// Returns the masked representation for UI display.
    /// If a custom Mask is provided, uses it; otherwise generates
    /// one per the rules in PLATFORM_TEMPLATES.md §2.4.
    /// </summary>
    public string DisplayMask()
    {
        if (!string.IsNullOrEmpty(Mask))
            return Mask;

        if (Value == null || Value.Length == 0)
            return string.Empty;

        return GenerateDefaultMask(ValueString);
    }

    private static string GenerateDefaultMask(string value)
    {
        // Per PLATFORM_TEMPLATES.md §2.4 dynamic mask rules:
        //   length <= 8: all bullets
        //   9-16:        first 3 + bullet + last 2
        //   > 16:        prefix until first '-' or first 6 + bullets + last 4
        if (value.Length <= 8)
            return new string('\u2022', Math.Max(4, value.Length));

        if (value.Length <= 16)
            return value.Substring(0, 3) + "\u2022" + value.Substring(value.Length - 2);

        int prefixEnd = value.IndexOf('-');
        string prefix = prefixEnd > 0 && prefixEnd <= 8
            ? value.Substring(0, prefixEnd + 1)
            : value.Substring(0, 6);
        return prefix + new string('\u2022', 8) + value.Substring(value.Length - 4);
    }
}
