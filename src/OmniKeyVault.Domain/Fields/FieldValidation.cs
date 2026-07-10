namespace OmniKeyVault.Domain;

/// <summary>
/// Field validation rule per OKV_FORMAT.md §3.5.
/// Regex is a soft validation — failure warns but does not block save
/// unless --strict-validation is active (see PLATFORM_TEMPLATES.md §2.5).
/// </summary>
public sealed record FieldValidation
{
    public string? Regex { get; init; }
    public string? Hint { get; init; }

    public bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(Regex))
            return true;
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(value, Regex);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern — treat as no validation.
            return true;
        }
    }
}
