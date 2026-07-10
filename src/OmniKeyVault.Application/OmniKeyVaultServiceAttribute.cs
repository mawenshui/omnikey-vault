namespace OmniKeyVault.Application;

/// <summary>
/// Marks a class as an OmniKey Vault service that requires lock-state checks.
/// The OKV0004 Roslyn analyzer will verify that public methods in classes
/// decorated with this attribute call EnsureUnlocked() (either directly or
/// via a delegation pattern) before performing locked operations.
///
/// Per docs/plan-v1.1-optimization.md Phase 3 / SECURITY.md §10.1 INV-04.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OmniKeyVaultServiceAttribute : Attribute
{
    /// <summary>
    /// If true, the OKV0004 analyzer will skip this class entirely.
    /// Use for services that don't require vault unlock (e.g. TemplateService).
    /// </summary>
    public bool NoLockRequired { get; init; }
}
