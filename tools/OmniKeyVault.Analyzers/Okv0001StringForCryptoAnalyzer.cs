using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OmniKeyVault.Analyzers;

/// <summary>
/// OKV0001: forbid <c>string</c> as a parameter of <c>ICryptoProvider</c>
/// methods (or any method on a type implementing <c>ICryptoProvider</c>).
///
/// Per docs/SECURITY.md §10.1 INV-01: <c>ICryptoProvider</c> methods must
/// accept <c>ReadOnlySpan&lt;byte&gt;</c> / <c>Span&lt;byte&gt;</c> / <c>byte[]</c>,
/// never <c>string</c>, because:
///   - strings are immutable and interned → cannot be zeroed.
///   - strings sit on the managed heap indefinitely until GC.
///   - UTF-8 encoding belongs at the call boundary (CLI), not in crypto code.
///
/// The analyzer fires on the method DECLARATION (interface or implementation),
/// not on call sites — the C# type system already prevents passing <c>string</c>
/// where <c>byte[]</c> is expected. This rule guards against future regressions
/// where someone adds a new method to <c>ICryptoProvider</c> with a <c>string</c>
/// parameter (e.g. <c>DeriveMasterKey(string password, ...)</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Okv0001StringForCryptoAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    internal static readonly DiagnosticDescriptor Rule = new(
        id: OkvRuleIds.Okv0001,
        title: "String must not be used as ICryptoProvider parameter",
        messageFormat: "OKV0001: parameter '{0}' of crypto method '{1}' is 'string' — sensitive crypto material (passwords, keys, nonces, salts, AAD, plaintext, ciphertext) must use byte[]/ReadOnlySpan<byte>/Span<byte> per SECURITY.md §10.1 INV-01.",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Per SECURITY.md §10.1, ICryptoProvider methods must not accept string for sensitive data (passwords, keys, nonces, etc.). Strings are immutable + interned → cannot be zeroed, risking INV-03 violation on lock.",
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethodSymbol, SymbolKind.Method);
    }

    private static void AnalyzeMethodSymbol(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        // Skip property getters, event add/remove, operators, constructors (covered separately if needed).
        if (method.MethodKind != MethodKind.Ordinary) return;

        // Check if the method is on ICryptoProvider or an implementation.
        if (!IsOnCryptoProviderOrImplementation(method.ContainingType)) return;

        // Check each parameter for string type + sensitive name.
        foreach (var param in method.Parameters)
        {
            if (param.Type.SpecialType != SpecialType.System_String) continue;
            if (!IsSensitiveParamName(param.Name)) continue;
            var diagnostic = Diagnostic.Create(Rule, param.Locations[0],
                param.Name, method.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>Check if the containing type is ICryptoProvider or implements it.
    /// Uses name-based matching so the analyzer works without referencing the
    /// real OmniKeyVault.Contracts assembly (simplifies analyzer deployment).</summary>
    private static bool IsOnCryptoProviderOrImplementation(INamedTypeSymbol? type)
    {
        if (type is null) return false;
        if (type.Name == "ICryptoProvider") return true;
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == "ICryptoProvider") return true;
        }
        return false;
    }

    /// <summary>Determine if a parameter name suggests it carries sensitive material.
    /// Conservative — flags any name containing common crypto-material keywords.</summary>
    private static bool IsSensitiveParamName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("password") || lower.Contains("passwd") ||
               lower.Contains("secret") || lower.Contains("key") ||
               lower.Contains("nonce") || lower.Contains("salt") ||
               lower.Contains("aad") || lower.Contains("plaintext") ||
               lower.Contains("ciphertext") || lower.Contains("master") ||
               lower.Contains("dek") || lower.Contains("kek") || lower.Contains("mk") ||
               lower.Contains("recovery") || lower.Contains("verify");
    }
}
