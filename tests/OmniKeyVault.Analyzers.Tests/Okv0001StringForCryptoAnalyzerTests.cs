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
/// P3-T10: unit tests for <see cref="Okv0001StringForCryptoAnalyzer"/>.
/// Covers: positive (string password on ICryptoProvider method → fires),
/// negative (byte[] password → no fire; string non-sensitive name → no fire),
/// and edge case (implementation class also covered).
/// </summary>
public class Okv0001StringForCryptoAnalyzerTests
{
    [Fact]
    public async Task StringPasswordOnICryptoProvider_FiresOkv0001()
    {
        // The analyzer should fire on the 'string password' parameter.
        var source = @"
namespace OmniKeyVault.Contracts {
    public interface ICryptoProvider {
        void DeriveMasterKey(string password, byte[] salt);
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().Contain(d => d.Id == OkvRuleIds.Okv0001);
    }

    [Fact]
    public async Task ByteArrayPasswordOnICryptoProvider_DoesNotFire()
    {
        var source = @"
namespace OmniKeyVault.Contracts {
    public interface ICryptoProvider {
        void DeriveMasterKey(byte[] password, byte[] salt);
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0001);
    }

    [Fact]
    public async Task StringNonSensitiveName_DoesNotFire()
    {
        // 'name' is not in the sensitive-keyword list → no fire even though it's string.
        var source = @"
namespace OmniKeyVault.Contracts {
    public interface ICryptoProvider {
        void SetProviderName(string name);
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0001);
    }

    [Fact]
    public async Task ImplementationClassWithStringSecretParam_FiresOkv0001()
    {
        // The analyzer also checks implementation classes, not just the interface.
        // The interface declares a COMPLIANT byte[] signature; the implementation
        // adds an EXTRA string-param method (a real-world regression scenario).
        var source = @"
namespace OmniKeyVault.Contracts {
    public interface ICryptoProvider {
        void DeriveMasterKey(byte[] password, byte[] salt);
    }
}
namespace OmniKeyVault.Infrastructure {
    public class SodiumCryptoProvider : OmniKeyVault.Contracts.ICryptoProvider {
        // Interface impl (compliant)
        public void DeriveMasterKey(byte[] password, byte[] salt) { }
        // Extra method with string param (violation — on a type implementing ICryptoProvider)
        public void DeriveMasterKeyFromString(string password, byte[] salt) { }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().Contain(d => d.Id == OkvRuleIds.Okv0001);
    }

    [Fact]
    public async Task ReadOnlySpanPassword_DoesNotFire()
    {
        // ReadOnlySpan<byte> is the recommended type per INV-01.
        var source = @"
namespace OmniKeyVault.Contracts {
    public interface ICryptoProvider {
        void DeriveMasterKey(System.ReadOnlySpan<byte> password, System.ReadOnlySpan<byte> salt);
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0001);
    }

    /// <summary>Compiles the source with the OKV0001 analyzer and returns diagnostics.</summary>
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

        var analyzer = new Okv0001StringForCryptoAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
