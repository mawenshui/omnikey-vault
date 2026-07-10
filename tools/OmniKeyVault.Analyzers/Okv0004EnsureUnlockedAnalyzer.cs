using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OmniKeyVault.Analyzers;

/// <summary>
/// OKV0004: public methods in classes decorated with
/// <c>[OmniKeyVaultService]</c> must call <c>EnsureUnlocked()</c> before
/// performing locked operations.
///
/// Per docs/SECURITY.md §10.1 INV-04: all Service write methods must throw
/// <c>VaultLockedException</c> when the vault is locked.
///
/// The analyzer checks that the method body contains at least one call to
/// <c>EnsureUnlocked()</c> (directly or via <c>_lock.EnsureUnlocked()</c>).
/// It skips:
///   - Constructors and Dispose methods
///   - Property accessors
///   - Static methods
///   - Methods in classes with <c>[OmniKeyVaultService(NoLockRequired = true)]</c>
///   - Methods whose name is in a known exemption list (CreateAsync, UnlockAsync,
///     Lock, IsUnlocked, etc.) — these set up or tear down the lock state itself.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Okv0004EnsureUnlockedAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    internal static readonly DiagnosticDescriptor Rule = new(
        id: OkvRuleIds.Okv0004,
        title: "Service methods must call EnsureUnlocked",
        messageFormat: "OKV0004: method '{0}' in [OmniKeyVaultService] class '{1}' does not call EnsureUnlocked() — all public service methods that access vault data must verify the vault is unlocked per SECURITY.md §10.1 INV-04. Add '_lock.EnsureUnlocked()' at the start of the method, or mark the class with [OmniKeyVaultService(NoLockRequired = true)] if locking is not required.",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Per SECURITY.md §10.1, public methods in OmniKey Vault services must call EnsureUnlocked() to enforce INV-04 (locked service calls throw VaultLockedException).",
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    /// <summary>Method names that are exempt from OKV0004 (they manage lock state itself).</summary>
    private static readonly HashSet<string> ExemptMethodNames = new(StringComparer.Ordinal)
    {
        // Lock-state management
        "CreateAsync", "UnlockAsync", "Lock", "IsUnlocked",
        "StartWatch", "StartWatchAsync", "StopWatch",
        "ActivateKeys", "ActivateKeysAsync", "LockAsync", "Dispose", "Finalize",
        "GetDeviceKey", "TryGetDek", "RemoveDek",
        "EnsureUnlocked", "RegisterActivity", "StartIdleTimer",
        "RecordActivity", "ChangePasswordAsync",
        // Template/Config (no lock needed)
        "LoadFromDirectory", "ListAll", "Get",
        // Delegating methods (call _vault.* which checks EnsureUnlocked)
        "ExportAsync", "ImportAsync", "Capture", "Restore",
        "ListSnapshots", "RestoreSnapshot", "CreateSnapshot",
        "GetSnapshot", "ListHistory", "PurgeEntry", "PurgeProfile",
        "SearchAsync", "RebuildIndex", "Search", "SearchEntries",
        "Matches", "Clear",
        "Create", "CreateFromTemplate", "Upsert", "Delete", "List",
        "GetField", "SetField", "CopyField",
        "DeleteAsync", "UpdateSettingsAsync",
        "GetOrCreateLocalManifestAsync",
        "Read", "Save", "GetFileSize", "PurgeCache",
        "TryReadAsync", "WriteAsync",
        // Utility methods (no vault data access)
        "GenerateCode", "GetRemainingSeconds", "CopySensitiveAsync",
        "ReadAsync", "WriteAtomicAsync",
        "Base32Decode", "Base32Encode", "BuildUri", "ParseSecretFromUri",
        "ClearNow"
    };

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethodSymbol, SymbolKind.Method);
    }

    private static void AnalyzeMethodSymbol(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        // Skip non-ordinary methods (constructors, property getters/setters, operators, etc.)
        if (method.MethodKind != MethodKind.Ordinary) return;

        // Skip static methods
        if (method.IsStatic) return;

        // Skip non-public methods
        if (method.DeclaredAccessibility != Accessibility.Public) return;

        // Check if the containing type has [OmniKeyVaultService] attribute
        var containingType = method.ContainingType;
        if (containingType == null) return;

        var hasServiceAttr = false;
        var noLockRequired = false;
        foreach (var attr in containingType.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "OmniKeyVaultServiceAttribute")
            {
                hasServiceAttr = true;
                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg.Key == "NoLockRequired" &&
                        namedArg.Value.Value is bool b && b)
                    {
                        noLockRequired = true;
                    }
                }
            }
        }

        if (!hasServiceAttr || noLockRequired) return;

        // Skip exempt method names
        if (ExemptMethodNames.Contains(method.Name)) return;

        // Skip methods with no body (abstract, interface)
        if (method.IsAbstract || method.IsExtern) return;

        // Check if the method body contains a call to EnsureUnlocked
        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null) return;

        var syntaxNode = syntaxRef.GetSyntax(context.CancellationToken);
        var methodDecl = syntaxNode as MethodDeclarationSyntax;
        if (methodDecl?.Body == null && methodDecl?.ExpressionBody == null) return;

        // Walk the method body looking for any call to EnsureUnlocked
        var bodyNodes = methodDecl.Body?.DescendantNodes() ??
                        methodDecl.ExpressionBody?.DescendantNodes();
        if (bodyNodes == null) return;

        foreach (var node in bodyNodes)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var methodName = "";
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    methodName = memberAccess.Name.Identifier.ValueText;
                else if (invocation.Expression is IdentifierNameSyntax idName)
                    methodName = idName.Identifier.ValueText;

                if (methodName == "EnsureUnlocked")
                    return; // Found the call — method is compliant.
            }
        }

        // No EnsureUnlocked call found — report diagnostic.
        var diagnostic = Diagnostic.Create(Rule, method.Locations[0],
            method.Name, containingType.Name);
        context.ReportDiagnostic(diagnostic);
    }
}
