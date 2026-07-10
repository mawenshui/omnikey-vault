using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OmniKeyVault.Analyzers;

/// <summary>
/// OKV0002: <c>SecureKey</c> subtypes (MasterKey, KeyEncryptionKey,
/// DataEncryptionKey, DevicePrivateKey) declared as local variables must be
/// disposed via <c>using</c> statement, <c>using</c> declaration modifier,
/// or explicit <c>Dispose()</c> call.
///
/// Per docs/SECURITY.md §10.1 INV-03: keys must be zeroed when no longer needed.
///
/// Heuristic suppressions (not flagged):
///   - <c>using</c> statement or <c>using</c> declaration modifier
///   - Variable assigned to an instance field (ownership transfer)
///   - Variable passed to <c>ActivateKeys</c>/<c>CacheDek</c>/<c>CacheDeviceKey</c> (ownership transfer)
///   - Variable returned from the method (ownership transfer)
///   - Variable is a "borrowed reference" from <c>_lock.GetDek</c>,
///     <c>_lock.CurrentKek</c>, <c>_lock.GetDeviceKey</c>, etc. (caller does not own it)
///   - Variable is explicitly <c>Dispose()</c>d later in the method body
///   - Variable is stored in a local collection (e.g. dictionary, list) for later caching
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Okv0002SecureKeyDisposeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    internal static readonly DiagnosticDescriptor Rule = new(
        id: OkvRuleIds.Okv0002,
        title: "SecureKey must be disposed via using or try-finally",
        messageFormat: "OKV0002: local '{0}' of type '{1}' is a SecureKey subtype but is not in a 'using' statement or 'using' declaration, and ownership is not transferred — key material will remain on the heap until GC, violating SECURITY.md §10.1 INV-03.",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Per SECURITY.md §10.1, SecureKey subtypes must be disposed via using/try-finally to ensure key material is zeroed.",
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    private static readonly HashSet<string> SecureKeyTypeNames = new(StringComparer.Ordinal)
    {
        "SecureKey", "MasterKey", "KeyEncryptionKey",
        "DataEncryptionKey", "DevicePrivateKey"
    };

    private static readonly HashSet<string> OwnershipTransferMethods = new(StringComparer.Ordinal)
    {
        "ActivateKeys", "ActivateKeysAsync", "CacheDek", "CacheDeviceKey"
    };

    /// <summary>Methods/properties that return a "borrowed" reference — caller does not own it.</summary>
    private static readonly HashSet<string> BorrowedReferenceSources = new(StringComparer.Ordinal)
    {
        "GetDek", "GetDeviceKey", "CurrentKek", "CurrentMasterKey",
        "TryGetDek", "RemoveDek",
        "GetDekForSeed", "GetDekFromLockService"
    };

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var localDecl = (LocalDeclarationStatementSyntax)context.Node;

        // Skip `using` declarations (C# 8: `using var x = ...`)
        if (localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            return;

        // Skip inside UsingStatement (`using (var x = ...) { ... }`)
        for (var ancestor = localDecl.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is UsingStatementSyntax) return;
        }

        foreach (var declarator in localDecl.Declaration.Variables)
        {
            if (declarator.Initializer == null) continue;

            var typeInfo = context.SemanticModel.GetTypeInfo(localDecl.Declaration.Type).Type;
            if (typeInfo == null)
            {
                var initType = context.SemanticModel.GetTypeInfo(declarator.Initializer.Value).Type;
                if (initType != null && IsSecureKeyType(initType))
                    typeInfo = initType;
            }

            if (typeInfo == null || !IsSecureKeyType(typeInfo))
                continue;

            var varName = declarator.Identifier.ValueText;
            var initializer = declarator.Initializer.Value;

            // Suppress: borrowed reference (from _lock.GetDek, _lock.CurrentKek, etc.)
            if (IsBorrowedReference(initializer))
                continue;

            var methodBody = localDecl.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
            if (methodBody == null) continue;

            // Suppress: ownership transferred (field assignment, ActivateKeys/CacheDek/CacheDeviceKey, return)
            if (IsOwnershipTransferred(methodBody, varName))
                continue;

            // Suppress: explicitly disposed later in the method
            if (IsExplicitlyDisposed(methodBody, varName))
                continue;

            // Suppress: stored in a local collection for later caching
            if (IsStoredInLocalCollection(methodBody, varName))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule,
                declarator.GetLocation(), varName, typeInfo.Name));
        }
    }

    private static bool IsSecureKeyType(ITypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current != null; current = current.BaseType)
            if (SecureKeyTypeNames.Contains(current.Name))
                return true;
        return false;
    }

    /// <summary>Check if the initializer gets a borrowed reference from lock service.</summary>
    private static bool IsBorrowedReference(ExpressionSyntax initializer)
    {
        // _lock.GetDek(...), _lock.GetDeviceKey(...), _lock.CurrentKek, etc.
        if (initializer is InvocationExpressionSyntax invocation)
        {
            var name = GetMemberAccessName(invocation.Expression);
            if (BorrowedReferenceSources.Contains(name))
                return true;
        }

        // _lock.CurrentKek, _lock.CurrentMasterKey (property access)
        if (initializer is MemberAccessExpressionSyntax memberAccess)
        {
            if (BorrowedReferenceSources.Contains(memberAccess.Name.Identifier.ValueText))
                return true;
        }

        // Conditional expression: _lock.CurrentKek ?? throw ...
        if (initializer is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            if (IsBorrowedReference(binary.Left))
                return true;
        }

        // Ternary: condition ? GetDekFromLockService(...) : throw ...
        if (initializer is ConditionalExpressionSyntax conditional)
        {
            if (IsBorrowedReference(conditional.WhenTrue))
                return true;
            if (IsBorrowedReference(conditional.WhenFalse))
                return true;
        }

        return false;
    }

    private static string GetMemberAccessName(ExpressionSyntax? expr)
    {
        if (expr is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.ValueText;
        if (expr is IdentifierNameSyntax idName)
            return idName.Identifier.ValueText;
        return "";
    }

    private static bool IsOwnershipTransferred(BaseMethodDeclarationSyntax methodBody, string varName)
    {
        foreach (var node in methodBody.DescendantNodes())
        {
            // Field assignment: _field = varName
            if (node is AssignmentExpressionSyntax assign &&
                assign.Right is IdentifierNameSyntax rightId &&
                rightId.Identifier.ValueText == varName &&
                assign.Left is IdentifierNameSyntax leftId &&
                leftId.Identifier.ValueText.StartsWith("_", StringComparison.Ordinal))
            {
                return true;
            }

            // Ownership transfer method call
            if (node is InvocationExpressionSyntax invocation)
            {
                var methodName = GetMemberAccessName(invocation.Expression);
                if (OwnershipTransferMethods.Contains(methodName))
                {
                    foreach (var arg in invocation.ArgumentList.Arguments)
                    {
                        if (arg.Expression is IdentifierNameSyntax argId &&
                            argId.Identifier.ValueText == varName)
                            return true;
                    }
                }
            }

            // Return statement
            if (node is ReturnStatementSyntax ret &&
                ret.Expression is IdentifierNameSyntax retId &&
                retId.Identifier.ValueText == varName)
                return true;
        }

        return false;
    }

    /// <summary>Check if the variable is explicitly disposed via .Dispose() call.</summary>
    private static bool IsExplicitlyDisposed(BaseMethodDeclarationSyntax methodBody, string varName)
    {
        foreach (var node in methodBody.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.ValueText == "Dispose" &&
                    memberAccess.Expression is IdentifierNameSyntax idName &&
                    idName.Identifier.ValueText == varName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Check if the variable is stored in a local collection (dict/list) for later use.</summary>
    private static bool IsStoredInLocalCollection(BaseMethodDeclarationSyntax methodBody, string varName)
    {
        foreach (var node in methodBody.DescendantNodes())
        {
            // collection[key] = varName  or  collection.Add(varName)
            if (node is AssignmentExpressionSyntax assign &&
                assign.Right is IdentifierNameSyntax rightId &&
                rightId.Identifier.ValueText == varName &&
                assign.Left is ElementAccessExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
