﻿using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.Integration;

/// <summary> Tests for the ProfilePayloadCodec 鈥?the inner payload serialization
/// within each encrypted profile section per OKV_FORMAT.md 搂5. </summary>
public class ProfilePayloadCodecTests
{
    private readonly ProfilePayloadCodec _codec = new();

    [Fact]
    public void Encode_Decode_EmptyPayload_Roundtrip()
    {
        var encoded = _codec.Encode(Array.Empty<Entry>(), Array.Empty<Folder>(), Array.Empty<string>(), Array.Empty<Template>());
        var (entries, folders, tags, templates) = _codec.Decode(encoded);
        entries.Should().BeEmpty();
        folders.Should().BeEmpty();
        tags.Should().BeEmpty();
        templates.Should().BeEmpty();
    }

    [Fact]
    public void Encode_Decode_Tags_Roundtrip()
    {
        var tags = new[] { "ai", "cloud", "production" };
        var encoded = _codec.Encode(Array.Empty<Entry>(), Array.Empty<Folder>(), tags, Array.Empty<Template>());
        var (_, _, decodedTags, _) = _codec.Decode(encoded);
        decodedTags.Should().Equal(tags);
    }

    [Fact]
    public void Encode_Decode_Folders_Roundtrip()
    {
        var parentId = Guid.NewGuid();
        var folders = new[]
        {
            new Folder { Id = Guid.NewGuid(), Name = "AI", ParentId = null },
            new Folder { Id = Guid.NewGuid(), Name = "GPT-4", ParentId = parentId }
        };
        var encoded = _codec.Encode(Array.Empty<Entry>(), folders, Array.Empty<string>(), Array.Empty<Template>());
        var (_, decodedFolders, _, _) = _codec.Decode(encoded);
        decodedFolders.Should().HaveCount(2);
        decodedFolders[0].Name.Should().Be("AI");
        decodedFolders[0].ParentId.Should().BeNull();
        decodedFolders[1].ParentId.Should().Be(parentId);
    }

    [Fact]
    public void Encode_Decode_Templates_Roundtrip()
    {
        var tpl = new Template
        {
            Id = "openai",
            PlatformId = "openai",
            Fields = new[]
            {
                new TemplateField { Key = "api_key", Kind = FieldKind.Secret, Sensitive = true, Required = true, DefaultMask = "sk-proj-••••••••", Validation = new FieldValidation { Regex = "^sk-", Hint = "starts with sk-" } },
                new TemplateField { Key = "url", Kind = FieldKind.Url, Sensitive = false, Required = false }
            }
        };
        var encoded = _codec.Encode(Array.Empty<Entry>(), Array.Empty<Folder>(), Array.Empty<string>(), new[] { tpl });
        var (_, _, _, decodedTpls) = _codec.Decode(encoded);
        decodedTpls.Should().ContainSingle();
        decodedTpls[0].Id.Should().Be("openai");
        decodedTpls[0].Fields.Should().HaveCount(2);
        decodedTpls[0].Fields[0].Kind.Should().Be(FieldKind.Secret);
        decodedTpls[0].Fields[0].Sensitive.Should().BeTrue();
        decodedTpls[0].Fields[0].DefaultMask.Should().Be("sk-proj-••••••••");
        decodedTpls[0].Fields[0].Validation!.Regex.Should().Be("^sk-");
    }

    [Fact]
    public void Encode_Decode_EntryWithFields_Roundtrip()
    {
        var entry = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "OpenAI prod",
            PlatformId = "openai",
            Tags = new[] { "ai", "work" },
            Folder = Guid.NewGuid(),
            Fields = new[]
            {
                new Field { Key = "api_key", Value = FieldCodec.Encode("sk-proj-test"), Kind = FieldKind.Secret, Sensitive = true },
                new Field { Key = "url", Value = FieldCodec.Encode("https://api.openai.com"), Kind = FieldKind.Url, Sensitive = false },
                new Field { Key = "notes", Value = FieldCodec.Encode("Internal use only"), Kind = FieldKind.Text, Sensitive = true }
            },
            Notes = "Test notes",
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1718000000),
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(1718100000),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1800000000),
            Version = 5
        };
        var encoded = _codec.Encode(new[] { entry }, Array.Empty<Folder>(), new[] { "ai", "work" }, Array.Empty<Template>());
        var (decodedEntries, _, _, _) = _codec.Decode(encoded);
        decodedEntries.Should().ContainSingle();
        var e = decodedEntries[0];
        e.Id.Should().Be(entry.Id);
        e.Name.Should().Be("OpenAI prod");
        e.PlatformId.Should().Be("openai");
        e.Tags.Should().Equal("ai", "work");
        e.Folder.Should().Be(entry.Folder);
        e.Notes.Should().Be("Test notes");
        e.ExpiresAt.Should().Be(entry.ExpiresAt);
        e.Version.Should().Be(5);
        e.Fields.Should().HaveCount(3);
        e.Fields[0].ValueString.Should().Be("sk-proj-test");
        e.Fields[1].ValueString.Should().Be("https://api.openai.com");
    }

    [Fact]
    public void Encode_Decode_EntryWithoutExpiry_Roundtrip()
    {
        var entry = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "no-expiry",
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        var encoded = _codec.Encode(new[] { entry }, Array.Empty<Folder>(), Array.Empty<string>(), Array.Empty<Template>());
        var (decodedEntries, _, _, _) = _codec.Decode(encoded);
        decodedEntries[0].ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Encode_Decode_UnicodeNamesAndValues_Roundtrip()
    {
        var entry = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "涓枃鏉＄洰-馃攼-脡mojiTest",
            Fields = new[] { new Field { Key = "x", Value = FieldCodec.Encode("鍖呭惈 Unicode 鐨勫€?\u2022 special"), Kind = FieldKind.Secret, Sensitive = true } },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        var encoded = _codec.Encode(new[] { entry }, Array.Empty<Folder>(), Array.Empty<string>(), Array.Empty<Template>());
        var (decodedEntries, _, _, _) = _codec.Decode(encoded);
        decodedEntries[0].Name.Should().Be("涓枃鏉＄洰-馃攼-脡mojiTest");
        decodedEntries[0].Fields[0].ValueString.Should().Be("鍖呭惈 Unicode 鐨勫€?\u2022 special");
    }

    [Fact]
    public void Encode_Decode_LargeNumberOfEntries_Roundtrip()
    {
        var entries = Enumerable.Range(0, 200).Select(i => new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = $"entry-{i}",
            Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode($"v{i}"), Kind = FieldKind.Secret, Sensitive = true } },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        }).ToArray();
        var encoded = _codec.Encode(entries, Array.Empty<Folder>(), Array.Empty<string>(), Array.Empty<Template>());
        var (decodedEntries, _, _, _) = _codec.Decode(encoded);
        decodedEntries.Should().HaveCount(200);
    }
}
