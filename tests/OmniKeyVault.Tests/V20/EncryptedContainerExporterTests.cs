using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for EncryptedContainerExporter: export/import roundtrip,
/// wrong password rejection, invalid file detection.
/// </summary>
public class EncryptedContainerExporterTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();

    public EncryptedContainerExporterTests(TempVaultDir dir) => _dir = dir;

    private List<Entry> CreateTestEntries()
    {
        var now = DateTimeOffset.UtcNow;
        return new List<Entry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Type = EntryType.ApiKey,
                Name = "OpenAI Key",
                PlatformId = "openai",
                Tags = new List<string> { "ai", "production" },
                Fields = new List<Field>
                {
                    new() { Key = "api_key", Value = FieldCodec.Encode("sk-test-key-12345"), Kind = FieldKind.Secret, Sensitive = true },
                    new() { Key = "url", Value = FieldCodec.Encode("https://api.openai.com"), Kind = FieldKind.Url, Sensitive = false },
                },
                Notes = "Production API key",
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = EntryType.Custom,
                Name = "GitHub Token",
                PlatformId = "github",
                Tags = new List<string> { "dev" },
                Fields = new List<Field>
                {
                    new() { Key = "token", Value = FieldCodec.Encode("ghp_testToken123"), Kind = FieldKind.Secret, Sensitive = true },
                },
                Notes = null,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            }
        };
    }

    [Fact]
    public async Task Export_Import_Roundtrip_PreservesData()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var outputPath = Path.Combine(_dir.Root, "export.okvx");
        var password = "export-password-123";

        var entries = CreateTestEntries();
        await exporter.ExportAsync(entries, outputPath, password);

        File.Exists(outputPath).Should().BeTrue("export file should be created");

        var imported = await exporter.ImportAsync(outputPath, password);

        imported.Should().HaveCount(2);
        imported[0].Name.Should().Be("OpenAI Key");
        imported[0].Type.Should().Be(EntryType.ApiKey);
        imported[0].PlatformId.Should().Be("openai");
        imported[0].Tags.Should().Contain("ai").And.Contain("production");
        imported[0].Fields.Should().Contain(f => f.Key == "api_key" && f.ValueString == "sk-test-key-12345");
        imported[0].Fields.Should().Contain(f => f.Key == "url" && f.ValueString == "https://api.openai.com");
        imported[0].Notes.Should().Be("Production API key");

        imported[1].Name.Should().Be("GitHub Token");
        imported[1].Fields.Should().Contain(f => f.Key == "token" && f.ValueString == "ghp_testToken123");
    }

    [Fact]
    public async Task Import_WrongPassword_ThrowsValidation()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var outputPath = Path.Combine(_dir.Root, "wrong-pw.okvx");

        await exporter.ExportAsync(CreateTestEntries(), outputPath, "correct-password");

        var act = async () => await exporter.ImportAsync(outputPath, "wrong-password");
        await act.Should().ThrowAsync<ValidationException>("wrong password should be rejected");
    }

    [Fact]
    public async Task Import_NonExistentFile_Throws()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var act = async () => await exporter.ImportAsync(Path.Combine(_dir.Root, "nonexistent.okvx"), "password");
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Import_InvalidMagic_Throws()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var fakePath = Path.Combine(_dir.Root, "fake.okvx");
        await File.WriteAllBytesAsync(fakePath, Encoding.UTF8.GetBytes("NOT_AN_OKV_FILE_CONTENT_HERE!!!"));

        var act = async () => await exporter.ImportAsync(fakePath, "password");
        await act.Should().ThrowAsync<ValidationException>("invalid file format should be rejected");
    }

    [Fact]
    public async Task Export_EmptyList_ImportsEmpty()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var outputPath = Path.Combine(_dir.Root, "empty.okvx");

        await exporter.ExportAsync(new List<Entry>(), outputPath, "password");
        var imported = await exporter.ImportAsync(outputPath, "password");

        imported.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_SingleEntry_ImportsCorrectly()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var outputPath = Path.Combine(_dir.Root, "single.okvx");
        var now = DateTimeOffset.UtcNow;

        var entry = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.Note,
            Name = "My Note",
            PlatformId = null,
            Tags = new List<string>(),
            Fields = new List<Field>
            {
                new() { Key = "content", Value = FieldCodec.Encode("secret note content"), Kind = FieldKind.Secret, Sensitive = true },
            },
            Notes = "Note description",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        await exporter.ExportAsync(new[] { entry }, outputPath, "pw");
        var imported = await exporter.ImportAsync(outputPath, "pw");

        imported.Should().HaveCount(1);
        imported[0].Name.Should().Be("My Note");
        imported[0].Type.Should().Be(EntryType.Note);
        imported[0].Fields.Should().Contain(f => f.Key == "content" && f.ValueString == "secret note content");
    }

    [Fact]
    public async Task Export_FileStartsWithMagic()
    {
        var exporter = new EncryptedContainerExporter(_crypto);
        var outputPath = Path.Combine(_dir.Root, "magic-check.okvx");

        await exporter.ExportAsync(CreateTestEntries(), outputPath, "password");

        var bytes = await File.ReadAllBytesAsync(outputPath);
        var magic = Encoding.UTF8.GetString(bytes, 0, 7);
        magic.Should().Be("OKVEXP1", "file should start with magic header");
    }
}
