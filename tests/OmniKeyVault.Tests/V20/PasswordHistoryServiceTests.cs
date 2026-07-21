using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for PasswordHistoryService: record, retrieve, clear, and max-20-versions cap.
/// </summary>
public class PasswordHistoryServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public PasswordHistoryServiceTests(TempVaultDir dir) => _dir = dir;

    private (PasswordHistoryService svc, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        return (new PasswordHistoryService(vs, _crypto), vs, ls);
    }

    [Fact]
    public async Task RecordChange_ThenGetHistory_ReturnsRecordedValue()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "history-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entryId = Guid.NewGuid();
            svc.RecordChange(entryId, "Test Entry", "password", "old-password-1", "new-password-1");

            var history = svc.GetHistory(entryId, "password");
            history.Should().HaveCount(1);
            history[0].Value.Should().Be("old-password-1");
        }
    }

    [Fact]
    public async Task RecordChange_MultipleChanges_AllRecorded()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "multi-history-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entryId = Guid.NewGuid();
            svc.RecordChange(entryId, "Entry", "pwd", "v1", "v2");
            svc.RecordChange(entryId, "Entry", "pwd", "v2", "v3");
            svc.RecordChange(entryId, "Entry", "pwd", "v3", "v4");

            var history = svc.GetHistory(entryId, "pwd");
            history.Should().HaveCount(3);
            history.Select(h => h.Value).Should().ContainInOrder("v1", "v2", "v3");
        }
    }

    [Fact]
    public async Task RecordChange_Max20Versions_OldestDropped()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "max-versions-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entryId = Guid.NewGuid();
            for (int i = 0; i < 25; i++)
            {
                svc.RecordChange(entryId, "Entry", "pwd", $"old-{i}", $"new-{i}");
            }

            var history = svc.GetHistory(entryId, "pwd");
            history.Should().HaveCount(20, "max 20 versions should be retained");
            // The oldest 5 should be dropped
            history.Should().NotContain(h => h.Value == "old-0");
            history.Should().NotContain(h => h.Value == "old-4");
            history.Should().Contain(h => h.Value == "old-5");
            history.Should().Contain(h => h.Value == "old-24");
        }
    }

    [Fact]
    public async Task GetEntryHistory_MultipleFields_ReturnsAll()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "multi-field-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entryId = Guid.NewGuid();
            svc.RecordChange(entryId, "Entry", "password", "old-pwd", "new-pwd");
            svc.RecordChange(entryId, "Entry", "api_key", "old-key", "new-key");

            var history = svc.GetEntryHistory(entryId);
            history.Should().HaveCount(2, "should have history for 2 fields");
            history.Should().Contain(h => h.FieldKey == "password");
            history.Should().Contain(h => h.FieldKey == "api_key");
        }
    }

    [Fact]
    public async Task ClearHistory_RemovesAllForEntry()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "clear-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entryId = Guid.NewGuid();
            svc.RecordChange(entryId, "Entry", "pwd", "old1", "new1");
            svc.RecordChange(entryId, "Entry", "pwd", "new1", "new2");

            svc.GetHistory(entryId, "pwd").Should().HaveCount(2);

            svc.ClearHistory(entryId);

            svc.GetHistory(entryId, "pwd").Should().BeEmpty();
            svc.GetEntryHistory(entryId).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task GetHistory_NoHistory_ReturnsEmpty()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "empty-history-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var history = svc.GetHistory(Guid.NewGuid(), "password");
            history.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task RecordChange_EntryNameUpdated_WhenEntryRenamed()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "rename-test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var entryId = Guid.NewGuid();
            svc.RecordChange(entryId, "Old Name", "pwd", "old", "new");
            svc.RecordChange(entryId, "New Name", "pwd", "new", "newer");

            var history = svc.GetEntryHistory(entryId);
            history.First().EntryName.Should().Be("New Name", "entry name should be updated");
        }
    }
}
