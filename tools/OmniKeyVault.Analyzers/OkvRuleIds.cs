namespace OmniKeyVault.Analyzers;

/// <summary>
/// Rule IDs for the OmniKey Vault Roslyn analyzers.
/// Per docs/SECURITY.md §10.1, these enforce cryptographic invariants
/// that would otherwise rely on human review alone.
/// </summary>
public static class OkvRuleIds
{
    /// <summary>string must not be used as ICryptoProvider parameter (INV-01).</summary>
    public const string Okv0001 = "OKV0001";

    /// <summary>SecureKey must be in using / try-finally (INV-03).</summary>
    public const string Okv0002 = "OKV0002";

    /// <summary>== / != must not be used to compare secrets (INV-07).</summary>
    public const string Okv0003 = "OKV0003";

    /// <summary>Service methods must call EnsureUnlocked first (INV-04).</summary>
    public const string Okv0004 = "OKV0004";
}
