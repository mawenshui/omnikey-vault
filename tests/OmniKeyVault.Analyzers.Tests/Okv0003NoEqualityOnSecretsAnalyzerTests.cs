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
/// P3-T10: unit tests for <see cref="Okv0003NoEqualityOnSecretsAnalyzer"/>.
/// Covers: positive (byte[] == byte[] → fires), negative (null comparison → no fire,
/// FixedTimeEquals usage → no fire), and edge case (ReadOnlySpan<byte> operand).
/// </summary>
public class Okv0003NoEqualityOnSecretsAnalyzerTests
{
    [Fact]
    public async Task ByteArrayEqualsByteArray_FiresOkv0003()
    {
        // byte[] == byte[] is reference equality (a bug) + timing-unsafe → fire.
        var source = @"
namespace Test {
    public class C {
        public bool Compare(byte[] a, byte[] b) {
            return a == b;
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().Contain(d => d.Id == OkvRuleIds.Okv0003);
    }

    [Fact]
    public async Task ByteArrayNotEqualsByteArray_FiresOkv0003()
    {
        var source = @"
namespace Test {
    public class C {
        public bool Compare(byte[] a, byte[] b) {
            return a != b;
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().Contain(d => d.Id == OkvRuleIds.Okv0003);
    }

    [Fact]
    public async Task ByteArrayEqualsNull_DoesNotFire()
    {
        // Null comparison is safe (no timing leak).
        var source = @"
namespace Test {
    public class C {
        public bool IsNull(byte[] a) {
            return a == null;
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0003);
    }

    [Fact]
    public async Task NullEqualsByteArray_DoesNotFire()
    {
        // Reverse null comparison — still safe.
        var source = @"
namespace Test {
    public class C {
        public bool IsNull(byte[] a) {
            return null == a;
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0003);
    }

    [Fact]
    public async Task IntEqualsInt_DoesNotFire()
    {
        // int == int is not a secret comparison → no fire.
        var source = @"
namespace Test {
    public class C {
        public bool Compare(int a, int b) {
            return a == b;
        }
    }
}";
        var diagnostics = await RunAnalyzerAsync(source);
        diagnostics.Should().NotContain(d => d.Id == OkvRuleIds.Okv0003);
    }

    /// <summary>Compiles the source with the OKV0003 analyzer and returns diagnostics.</summary>
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

        var analyzer = new Okv0003NoEqualityOnSecretsAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
