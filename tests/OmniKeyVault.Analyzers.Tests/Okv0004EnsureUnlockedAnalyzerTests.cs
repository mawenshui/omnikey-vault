using System.Collections.Immutable;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using OmniKeyVault.Analyzers;
using Xunit;

namespace OmniKeyVault.Analyzers.Tests;

/// <summary>
/// P3-T10: unit tests for OKV0004 EnsureUnlockedAnalyzer.
/// Covers: service method with EnsureUnlocked (compliant), service method
/// without EnsureUnlocked (violation), NoLockRequired class (compliant),
/// exempt method names (compliant).
/// </summary>
public class Okv0004EnsureUnlockedAnalyzerTests
{
    [Fact]
    public async Task MethodWithEnsureUnlocked_DoesNotFire()
    {
        var source = @"
namespace Test {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OmniKeyVaultServiceAttribute : System.Attribute {
        public bool NoLockRequired { get; set; }
    }

    [OmniKeyVaultService]
    public class VaultService {
        private object _lock = new();
        public void EnsureUnlocked() { }
        public void PutEntry(string name) {
            EnsureUnlocked();
            // do work
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0004);
    }

    [Fact]
    public async Task MethodWithoutEnsureUnlocked_FiresOkv0004()
    {
        var source = @"
namespace Test {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OmniKeyVaultServiceAttribute : System.Attribute {
        public bool NoLockRequired { get; set; }
    }

    [OmniKeyVaultService]
    public class VaultService {
        private object _lock = new();
        public void EnsureUnlocked() { }
        public void DeleteEntry(string name) {
            // forgot to call EnsureUnlocked!
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().Contain(d => d.Id == OkvRuleIds.Okv0004);
    }

    [Fact]
    public async Task NoLockRequiredClass_DoesNotFire()
    {
        var source = @"
namespace Test {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OmniKeyVaultServiceAttribute : System.Attribute {
        public bool NoLockRequired { get; set; }
    }

    [OmniKeyVaultService(NoLockRequired = true)]
    public class TemplateService {
        public void LoadFromDirectory(string path) {
            // no lock needed
        }
        public void ListAll() {
            // no lock needed
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0004);
    }

    [Fact]
    public async Task ExemptMethodName_DoesNotFire()
    {
        var source = @"
namespace Test {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OmniKeyVaultServiceAttribute : System.Attribute {
        public bool NoLockRequired { get; set; }
    }

    [OmniKeyVaultService]
    public class VaultService {
        public void EnsureUnlocked() { }
        public void CreateAsync() { }  // exempt by name
        public void UnlockAsync() { }  // exempt by name
        public void Lock() { }         // exempt by name
        public void Dispose() { }      // exempt by name
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0004);
    }

    [Fact]
    public async Task PrivateMethod_DoesNotFire()
    {
        var source = @"
namespace Test {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OmniKeyVaultServiceAttribute : System.Attribute {
        public bool NoLockRequired { get; set; }
    }

    [OmniKeyVaultService]
    public class VaultService {
        public void EnsureUnlocked() { }
        private void HelperMethod() {
            // private — not checked
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0004);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Okv0004EnsureUnlockedAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
