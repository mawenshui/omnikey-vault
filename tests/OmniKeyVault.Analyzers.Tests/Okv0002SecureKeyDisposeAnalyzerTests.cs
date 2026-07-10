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
/// P3-T10: unit tests for OKV0002 SecureKeyDisposeAnalyzer.
/// Covers: using declaration (compliant), bare local (violation),
/// field assignment (ownership transfer, compliant).
/// </summary>
public class Okv0002SecureKeyDisposeAnalyzerTests
{
    [Fact]
    public async Task UsingDeclaration_DoesNotFire()
    {
        var source = @"
namespace Test {
    public class MasterKey : System.IDisposable {
        public void Dispose() { }
        public static MasterKey From(byte[] b) => new();
    }
    public class Service {
        public void Method() {
            using var mk = MasterKey.From(new byte[32]);
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0002);
    }

    [Fact]
    public async Task UsingStatement_DoesNotFire()
    {
        var source = @"
namespace Test {
    public class MasterKey : System.IDisposable {
        public void Dispose() { }
        public static MasterKey From(byte[] b) => new();
    }
    public class Service {
        public void Method() {
            using (var mk = MasterKey.From(new byte[32])) {
                // use mk
            }
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0002);
    }

    [Fact]
    public async Task BareLocal_FiresOkv0002()
    {
        var source = @"
namespace Test {
    public class MasterKey : System.IDisposable {
        public void Dispose() { }
        public static MasterKey From(byte[] b) => new();
    }
    public class Service {
        public void Method() {
            var mk = MasterKey.From(new byte[32]);
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().Contain(d => d.Id == OkvRuleIds.Okv0002);
    }

    [Fact]
    public async Task FieldAssignment_DoesNotFire()
    {
        // Ownership transferred to a field — the field's owner is responsible.
        var source = @"
namespace Test {
    public class MasterKey : System.IDisposable {
        public void Dispose() { }
        public static MasterKey From(byte[] b) => new();
    }
    public class Service {
        private MasterKey? _mk;
        public void Method() {
            var mk = MasterKey.From(new byte[32]);
            _mk = mk;
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0002);
    }

    [Fact]
    public async Task ActivateKeysCall_DoesNotFire()
    {
        // Ownership transferred via ActivateKeys call.
        var source = @"
namespace Test {
    public class MasterKey : System.IDisposable {
        public void Dispose() { }
        public static MasterKey From(byte[] b) => new();
    }
    public class KeyEncryptionKey : System.IDisposable {
        public void Dispose() { }
        public static KeyEncryptionKey From(byte[] b) => new();
    }
    public class Service {
        public void ActivateKeys(MasterKey mk, KeyEncryptionKey kek) { }
        public void Method() {
            var mk = MasterKey.From(new byte[32]);
            var kek = KeyEncryptionKey.From(new byte[32]);
            ActivateKeys(mk, kek);
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0002);
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

        var analyzer = new Okv0002SecureKeyDisposeAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
