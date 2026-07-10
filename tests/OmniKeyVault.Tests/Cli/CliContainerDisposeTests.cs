using FluentAssertions;
using OmniKeyVault.Cli;
using Xunit;

namespace OmniKeyVault.Tests.Cli;

/// <summary>
/// P2-T1: verifies <see cref="CliContainer.Dispose"/> is idempotent so that
/// <c>AppDomain.ProcessExit</c> + <c>Console.CancelKeyPress</c> + the
/// <c>using</c> statement can all call Dispose without double-disposing
/// VaultService / LockService (which would throw ObjectDisposedException
/// or, worse, zero already-zeroed memory a second time).
/// </summary>
public class CliContainerDisposeTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;

    public CliContainerDisposeTests(TempVaultDir dir) { _dir = dir; }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var container = new CliContainer("test-device-dispose-1");
        container.Dispose();
        // Second call must be a no-op (idempotent guard via Interlocked.Exchange).
        var act = () => container.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var container = new CliContainer("test-device-dispose-2");
        container.Dispose();
        container.Dispose();
        container.Dispose();
        container.Dispose();
        // No assertion needed — if this doesn't throw, the test passes.
    }
}
