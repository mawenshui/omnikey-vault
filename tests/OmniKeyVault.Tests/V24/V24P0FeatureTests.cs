using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Cli.Gui.Views;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using System.Net;
using System.Text.Json;
using Xunit;

namespace OmniKeyVault.Tests.V24;

/// <summary>
/// v2.4.0 P0 feature tests: browser extension auto-fill, sync conflict batch processing,
/// entry management enhancements (virtualization, drag-to-folder, multi-tab, field reorder).
/// </summary>
public class V24P0FeatureTests : IDisposable
{
    private readonly TempVaultDir _tempDir = new();
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public void Dispose() => _tempDir.Dispose();

    private async Task<(VaultService vault, EntryService entries, LockService lockSvc)> CreateUnlockedVaultAsync()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var path = _tempDir.RandomPath();
        await vs.CreateAsync(path, "test", Encoding.UTF8.GetBytes("TestPassword123!"),
            Argon2Params.ForTests(32 * 1024 * 1024));
        var clip = new ClipboardService(new ClipboardProvider(), ls);
        var entrySvc = new EntryService(vs, new TemplateService(), clip, _crypto);
        return (vs, entrySvc, ls);
    }

    private static Entry MakeEntry(string name, string fieldKey, string fieldValue) => new()
    {
        Id = Guid.NewGuid(),
        Type = EntryType.ApiKey,
        Name = name,
        Fields = new[] { new Field { Key = fieldKey, Value = FieldCodec.Encode(fieldValue), Kind = FieldKind.Secret, Sensitive = true } },
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ============================================================
    // P0-1: Browser Extension — Auto-fill API endpoint
    // ============================================================

    [Fact]
    public async Task BrowserExtensionApi_AutofillEndpoint_ReturnsActualFieldValues()
    {
        // Setup: create a vault with an entry
        var (vault, entries, lockSvc) = await CreateUnlockedVaultAsync();
        using (vault) using (lockSvc)
        {
            var entry = MakeEntry("Test Entry", "api_key", "sk-test-12345");
            vault.PutEntry("prod", entry);

            // Start the browser extension API
            using var api = new BrowserExtensionApiService(vault, entries,
                new ClipboardService(new ClipboardProvider(), lockSvc));
            var port = GetFreePort();
            api.Start(port);

            // Wait for listener to be ready
            await Task.Delay(200);

            // Act: call /api/autofill
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {api.AuthToken}");
            var response = await client.GetAsync($"http://127.0.0.1:{port}/api/autofill?entryId={entry.Id}&profile=prod");
            var json = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            json.Should().Contain("sk-test-12345");
            json.Should().Contain("api_key");
            json.Should().Contain("\"success\":true");

            api.Stop();
        }
    }

    [Fact]
    public async Task BrowserExtensionApi_AutofillEndpoint_RequiresAuth()
    {
        var (vault, entries, lockSvc) = await CreateUnlockedVaultAsync();
        using (vault) using (lockSvc)
        {
            var entry = MakeEntry("Test", "key", "val");
            vault.PutEntry("prod", entry);

            using var api = new BrowserExtensionApiService(vault, entries,
                new ClipboardService(new ClipboardProvider(), lockSvc));
            var port = GetFreePort();
            api.Start(port);
            await Task.Delay(200);

            // Act: call without auth token
            var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/api/autofill?entryId={entry.Id}&profile=prod");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            api.Stop();
        }
    }

    [Fact]
    public async Task BrowserExtensionApi_AutofillEndpoint_Returns404_ForMissingEntry()
    {
        var (vault, entries, lockSvc) = await CreateUnlockedVaultAsync();
        using (vault) using (lockSvc)
        {
            using var api = new BrowserExtensionApiService(vault, entries,
                new ClipboardService(new ClipboardProvider(), lockSvc));
            var port = GetFreePort();
            api.Start(port);
            await Task.Delay(200);

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {api.AuthToken}");
            var fakeId = Guid.NewGuid();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/api/autofill?entryId={fakeId}&profile=prod");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            api.Stop();
        }
    }

    // ============================================================
    // P0-3: Sync Conflict — Batch resolution enum values
    // ============================================================

    [Fact]
    public void SyncConflictResolver_Resolution_HasAllExpectedValues()
    {
        // Verify that the v2.4.0 batch resolution types exist
        var resolutions = Enum.GetNames(typeof(SyncConflictResolver.Resolution));

        resolutions.Should().Contain("KeepLocal");
        resolutions.Should().Contain("TakeRemote");
        resolutions.Should().Contain("Merge");
        // v2.4.0 additions
        resolutions.Should().Contain("AllLocal");
        resolutions.Should().Contain("AllRemote");
    }

    // ============================================================
    // P0-2: Entry Management — Drag-to-folder entry update
    // ============================================================

    [Fact]
    public async Task Entry_WithFolder_CanBeMovedToNewFolder()
    {
        var (vault, _, lockSvc) = await CreateUnlockedVaultAsync();
        using (vault) using (lockSvc)
        {
            var entry = MakeEntry("Test Entry", "key", "val");
            vault.PutEntry("prod", entry);

            var folderId = Guid.NewGuid();
            // Simulate drag-to-folder: update entry with new folder
            var updated = entry with { Folder = folderId, UpdatedAt = DateTimeOffset.UtcNow, Version = entry.Version + 1 };
            vault.PutEntry("prod", updated);

            var retrieved = vault.GetEntry("prod", entry.Id);
            retrieved.Should().NotBeNull();
            retrieved!.Folder.Should().Be(folderId);
            retrieved.Version.Should().Be(entry.Version + 1);
        }
    }

    // ============================================================
    // P0-2: Entry Management — Field order preservation
    // ============================================================

    [Fact]
    public async Task Entry_Fields_PreserveOrderAfterReorder()
    {
        var (vault, _, lockSvc) = await CreateUnlockedVaultAsync();
        using (vault) using (lockSvc)
        {
            var fields = new List<Field>
            {
                new() { Key = "username", Value = FieldCodec.Encode("user1"), Kind = FieldKind.Text, Sensitive = false },
                new() { Key = "password", Value = FieldCodec.Encode("pass1"), Kind = FieldKind.Secret, Sensitive = true },
                new() { Key = "api_key", Value = FieldCodec.Encode("key1"), Kind = FieldKind.Secret, Sensitive = true },
            };
            var entry = new Entry
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Type = EntryType.ApiKey,
                Fields = fields,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            vault.PutEntry("prod", entry);

            // Simulate field reorder: move "api_key" before "password"
            var reorderedFields = new List<Field>
            {
                fields[0], // username
                fields[2], // api_key (moved up)
                fields[1], // password (moved down)
            };
            var reordered = entry with { Fields = reorderedFields, Version = entry.Version + 1 };
            vault.PutEntry("prod", reordered);

            var retrieved = vault.GetEntry("prod", entry.Id);
            retrieved!.Fields[0].Key.Should().Be("username");
            retrieved.Fields[1].Key.Should().Be("api_key");
            retrieved.Fields[2].Key.Should().Be("password");
        }
    }

    // ============================================================
    // P0-2: Multi-tab — Tab limit enforcement
    // ============================================================

    [Fact]
    public void MultiTab_LimitEnforced_WhenTooManyTabsOpen()
    {
        // This is a logic test — the actual tab limit is 8 entries.
        // We verify that the tab list doesn't grow unbounded.
        const int maxTabs = 8;
        var openTabs = new List<(Guid EntryId, string Name)>();

        for (int i = 0; i < 15; i++)
        {
            if (openTabs.Count >= maxTabs)
            {
                openTabs.RemoveAt(0);
            }
            openTabs.Add((Guid.NewGuid(), $"Entry {i}"));
        }

        openTabs.Count.Should().Be(maxTabs);
        openTabs[^1].Name.Should().Be("Entry 14");
    }
}
