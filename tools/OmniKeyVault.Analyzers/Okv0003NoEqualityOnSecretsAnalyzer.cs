using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OmniKeyVault.Analyzers;

/// <summary>
/// OKV0003: forbid <c>==</c> / <c>!=</c> on secret-typed operands
/// (<c>byte[]</c>, <c>ReadOnlySpan&lt;byte&gt;</c>, <c>Memory&lt;byte&gt;</c>,
/// <c>SecureKey</c>, <c>MasterKey</c>, <c>KeyEncryptionKey</c>, <c>DataEncryptionKey</c>).
///
/// Per docs/SECURITY.md §10.1 INV-07: secret comparisons must use
/// <c>CryptographicOperations.FixedTimeEquals</c> to prevent timing attacks.
///
/// Note: <c>byte[] == byte[]</c> is reference equality in C# (almost always a bug);
/// <c>ReadOnlySpan&lt;byte&gt; == ReadOnlySpan&lt;byte&gt;</c> does not compile,
/// but <c>a.SequenceEqual(b)</c> is the non-constant-time alternative this rule
/// would also flag in a future extension.
///
/// Excludes: comparisons against <c>null</c> (a == null / a != null) which are
/// always safe (no timing leak from a null check).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Okv0003NoEqualityOnSecretsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    internal static readonly DiagnosticDescriptor Rule = new(
        id: OkvRuleIds.Okv0003,
        title: "Do not use == or != to compare secrets",
        messageFormat: "OKV0003: use CryptographicOperations.FixedTimeEquals to compare '{0}' (type '{1}') — == / != is vulnerable to timing attacks per SECURITY.md §10.1 INV-07. (For byte[], == is also reference equality, almost always a bug.)",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Per SECURITY.md §10.1, byte[]/ReadOnlySpan<byte>/SecureKey secrets must be compared with CryptographicOperations.FixedTimeEquals, not == or !=.",
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBinaryExpression,
            SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
    }

    private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;

        // Skip null comparisons (a == null, null == a, a != null, etc.) — always safe.
        if (IsNullLiteral(binary.Left) || IsNullLiteral(binary.Right)) return;

        var leftType = context.SemanticModel.GetTypeInfo(binary.Left).Type;
        var rightType = context.SemanticModel.GetTypeInfo(binary.Right).Type;

        // Report if EITHER operand is a secret type.
        // (e.g. byte[] == byte[] is a bug regardless of which side the user thinks is "the secret".)
        ITypeSymbol? reportedType = null;
        string exprText = "";
        if (IsSecretType(leftType))
        {
            reportedType = leftType;
            exprText = binary.Left.ToString();
        }
        else if (IsSecretType(rightType))
        {
            reportedType = rightType;
            exprText = binary.Right.ToString();
        }

        if (reportedType is null) return;

        var diagnostic = Diagnostic.Create(Rule, binary.GetLocation(),
            exprText, reportedType.ToDisplayString());
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsNullLiteral(ExpressionSyntax? expr)
        => expr is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.NullKeyword);

    /// <summary>Determine if a type is secret-typed (must use FixedTimeEquals).
    /// Conservative: only flags clearly secret types to avoid false positives on
    /// user-defined record types that happen to be named like a secret.</summary>
    private static bool IsSecretType(ITypeSymbol? type)
    {
        if (type is null) return false;
        var name = type.Name;

        // byte[] (1D array of byte)
        if (type is IArrayTypeSymbol array && array.ElementType?.SpecialType == SpecialType.System_Byte)
            return true;

        // ReadOnlySpan<byte> / Memory<byte> / Span<byte>
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var defName = named.Name; // e.g. "ReadOnlySpan" (without <T>)
            if ((defName == "ReadOnlySpan" || defName == "Span" || defName == "Memory" ||
                 defName == "ReadOnlyMemory") &&
                named.TypeArguments.Length == 1 &&
                named.TypeArguments[0].SpecialType == SpecialType.System_Byte)
                return true;
        }

        // OmniKey Vault-specific secret key wrapper types
        return name == "SecureKey" || name == "MasterKey" ||
               name == "KeyEncryptionKey" || name == "DataEncryptionKey" ||
               name == "DevicePrivateKey" || name == "DevicePublicKey" ||
               name == "WrappedKey" || name == "EncryptedPayload";
    }
}
