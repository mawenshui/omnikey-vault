using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>
/// Phase 10: Tests for SearchService index optimization (Phase 9).
/// Verifies that the cached index produces identical results to
/// the non-indexed path, and that InvalidateIndex works correctly.
/// </summary>
public class SearchServiceIndexTests
{
    private readonly SearchService _search = new();

    private static List<Entry> BuildTestEntries() => new()
    {
        new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "OpenAI Production",
            PlatformId = "openai",
            Tags = new List<string> { "ai", "prod" },
            Fields = new List<Field>
            {
                new() { Key = "api_key", Value = FieldCodec.Encode("sk-prod-1234567890"), Kind = FieldKind.Secret, Sensitive = true },
                new() { Key = "org_id", Value = FieldCodec.Encode("org-xyz"), Kind = FieldKind.Text, Sensitive = false },
            },
            Notes = "Main production key",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        },
        new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "GitHub PAT",
            PlatformId = "github",
            Tags = new List<string> { "dev" },
            Fields = new List<Field>
            {
                new() { Key = "pat", Value = FieldCodec.Encode("ghp_abcdef123456"), Kind = FieldKind.Secret, Sensitive = true },
            },
            Notes = "Personal access token",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        },
        new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "AWS Credentials",
            PlatformId = "aws",
            Tags = new List<string> { "prod", "cloud" },
            Fields = new List<Field>
            {
                new() { Key = "access_key", Value = FieldCodec.Encode("AKIAIOSFODNN7EXAMPLE"), Kind = FieldKind.Secret, Sensitive = true },
                new() { Key = "secret_key", Value = FieldCodec.Encode("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"), Kind = FieldKind.Secret, Sensitive = true },
            },
            Notes = "IAM user credentials",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        },
    };

    [Fact]
    public void Index_SearchByName_ReturnsCorrectResults()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("name:OpenAI", entries);
        hits.Should().HaveCount(1);
        hits[0].Entry.Name.Should().Be("OpenAI Production");
    }

    [Fact]
    public void Index_SearchByPlatform_ReturnsCorrectResults()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("platform:github", entries);
        hits.Should().HaveCount(1);
        hits[0].Entry.PlatformId.Should().Be("github");
    }

    [Fact]
    public void Index_SearchByFieldValue_ReturnsCorrectResults()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("field:api_key:sk-prod", entries);
        hits.Should().HaveCount(1);
        hits[0].Entry.Name.Should().Be("OpenAI Production");
    }

    [Fact]
    public void Index_SearchByTag_ReturnsCorrectResults()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("tags:prod", entries);
        hits.Should().HaveCount(2);
        hits.Should().Contain(h => h.Entry.Name == "OpenAI Production");
        hits.Should().Contain(h => h.Entry.Name == "AWS Credentials");
    }

    [Fact]
    public void Index_FreeTextSearch_MatchesAcrossFields()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("production", entries);
        hits.Should().NotBeEmpty();
        hits.Should().Contain(h => h.Entry.Name == "OpenAI Production");
    }

    [Fact]
    public void Index_AndCombination_BothMustMatch()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("tags:prod AND platform:aws", entries);
        hits.Should().HaveCount(1);
        hits[0].Entry.Name.Should().Be("AWS Credentials");
    }

    [Fact]
    public void Index_OrCombination_EitherMatches()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("platform:github OR platform:aws", entries);
        hits.Should().HaveCount(2);
    }

    [Fact]
    public void InvalidateIndex_ForcesRebuildOnNextSearch()
    {
        var entries = BuildTestEntries();
        // First search builds the index
        _search.Search("name:OpenAI", entries);
        // Invalidate
        _search.InvalidateIndex();
        // Second search should rebuild and still work
        var hits = _search.Search("name:OpenAI", entries);
        hits.Should().HaveCount(1);
    }

    [Fact]
    public void Index_RepeatedSearchSameResults()
    {
        var entries = BuildTestEntries();
        var hits1 = _search.Search("tags:prod", entries);
        var hits2 = _search.Search("tags:prod", entries);
        hits1.Should().HaveCount(hits2.Count);
        for (int i = 0; i < hits1.Count; i++)
            hits1[i].Entry.Id.Should().Be(hits2[i].Entry.Id);
    }

    [Fact]
    public void Index_EmptyQuery_ReturnsEmpty()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("", entries);
        hits.Should().BeEmpty();
    }

    [Fact]
    public void Index_NoMatches_ReturnsEmpty()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("nonexistent-term-xyz", entries);
        hits.Should().BeEmpty();
    }

    [Fact]
    public void Index_FieldKeyOnly_EntriesWithField()
    {
        var entries = BuildTestEntries();
        var hits = _search.Search("field:api_key", entries);
        hits.Should().HaveCount(1);
        hits[0].Entry.Name.Should().Be("OpenAI Production");
    }

    [Fact]
    public void Matches_SingleEntry_ReturnsCorrectBool()
    {
        var entry = BuildTestEntries()[0];
        _search.Matches(entry, "name:OpenAI").Should().BeTrue();
        _search.Matches(entry, "name:GitHub").Should().BeFalse();
        _search.Matches(entry, "").Should().BeTrue();
    }
}
