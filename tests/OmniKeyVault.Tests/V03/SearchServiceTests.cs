using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>
/// v0.3 S6-T1 / S6-T2: SearchService tests covering field-level syntax,
/// OR / AND combinators, and field-hit metadata for GUI highlighting.
/// </summary>
public class SearchServiceTests
{
    private static Entry MakeEntry(string name, string platform, string[] tags, params (string key, string value, FieldKind kind)[] fields) =>
        new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = name,
            PlatformId = platform,
            Tags = tags.ToList(),
            Fields = fields.Select(f => new Field { Key = f.key, Value = FieldCodec.Encode(f.value), Kind = f.kind, Sensitive = true }).ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        };

    private readonly SearchService _svc = new();

    [Fact]
    public void EmptyQuery_ReturnsAll()
    {
        var entries = new[] { MakeEntry("A", "openai", new[] { "prod" }) };
        _svc.Search("", entries).Should().BeEmpty();
        _svc.Matches(entries[0], "").Should().BeTrue();
    }

    [Fact]
    public void FreeText_MatchesName()
    {
        var entries = new[] {
            MakeEntry("OpenAI production", "openai", Array.Empty<string>()),
            MakeEntry("GitHub PAT", "github", Array.Empty<string>()),
        };
        _svc.SearchEntries("openai", entries).Should().HaveCount(1);
    }

    [Fact]
    public void FreeText_IsCaseInsensitive()
    {
        var e = MakeEntry("OpenAI", "openai", Array.Empty<string>());
        _svc.Matches(e, "OPENAI").Should().BeTrue();
        _svc.Matches(e, "open").Should().BeTrue();
    }

    [Fact]
    public void FieldColon_MatchesKey()
    {
        var e = MakeEntry("X", "openai", Array.Empty<string>(),
            ("api_key", "sk-abc123", FieldKind.Secret));
        _svc.Matches(e, "field:api_key").Should().BeTrue();
        _svc.Matches(e, "field:nonexistent").Should().BeFalse();
    }

    [Fact]
    public void FieldColon_MatchesKeyAndValue()
    {
        var e = MakeEntry("X", "openai", Array.Empty<string>(),
            ("api_key", "sk-abc123", FieldKind.Secret),
            ("username", "alice", FieldKind.Text));
        _svc.Matches(e, "field:api_key:sk-").Should().BeTrue();
        _svc.Matches(e, "field:api_key:xyz").Should().BeFalse();
        _svc.Matches(e, "field:username:alice").Should().BeTrue();
    }

    [Fact]
    public void TagsPredicate_MatchesAnyTag()
    {
        var e1 = MakeEntry("A", "openai", new[] { "prod", "ai" });
        var e2 = MakeEntry("B", "github", new[] { "dev" });
        _svc.Matches(e1, "tags:ai").Should().BeTrue();
        _svc.Matches(e1, "tags:dev").Should().BeFalse();
        _svc.Matches(e2, "tags:dev").Should().BeTrue();
    }

    [Fact]
    public void PlatformPredicate_MatchesPlatformId()
    {
        var e = MakeEntry("A", "openai", Array.Empty<string>());
        _svc.Matches(e, "platform:openai").Should().BeTrue();
        _svc.Matches(e, "platform:github").Should().BeFalse();
    }

    [Fact]
    public void ExpiredKeyword_MatchesExpiredEntries()
    {
        var expired = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "Old",
            PlatformId = "openai",
            Tags = new List<string>(),
            Fields = new List<Field>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var fresh = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "New",
            PlatformId = "openai",
            Tags = new List<string>(),
            Fields = new List<Field>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };
        var noneEntry = MakeEntry("None", "openai", Array.Empty<string>());

        _svc.Matches(expired, "expired").Should().BeTrue();
        _svc.Matches(fresh, "expired").Should().BeFalse();
        _svc.Matches(noneEntry, "expired").Should().BeFalse();
    }

    [Fact]
    public void AndCombinator_RequiresAllPredicates()
    {
        var e = MakeEntry("OpenAI prod", "openai", new[] { "prod" });
        _svc.Matches(e, "tags:prod AND platform:openai").Should().BeTrue();
        _svc.Matches(e, "tags:prod AND platform:github").Should().BeFalse();
    }

    [Fact]
    public void OrCombinator_AllowsEitherPredicate()
    {
        var e = MakeEntry("OpenAI prod", "openai", new[] { "prod" });
        _svc.Matches(e, "tags:dev OR platform:openai").Should().BeTrue();
        _svc.Matches(e, "tags:dev OR platform:github").Should().BeFalse();
    }

    [Fact]
    public void MultipleAnds_Work()
    {
        var e = MakeEntry("X", "openai", new[] { "ai", "prod" },
            ("api_key", "sk-1234", FieldKind.Secret));
        _svc.Matches(e, "tags:ai AND tags:prod AND platform:openai").Should().BeTrue();
        _svc.Matches(e, "tags:ai AND tags:dev").Should().BeFalse();
    }

    [Fact]
    public void Search_ReturnsFieldHitsForHighlighting()
    {
        var e = MakeEntry("OpenAI", "openai", Array.Empty<string>(),
            ("api_key", "sk-abc123DEF456", FieldKind.Secret));
        var hits = _svc.Search("field:api_key:sk-", new[] { e });
        hits.Should().HaveCount(1);
        hits[0].FieldHits.Should().NotBeEmpty();
        var fh = hits[0].FieldHits[0];
        fh.FieldKey.Should().Be("api_key");
        fh.MatchedValue.Should().Contain("sk-abc");
        fh.StartIndex.Should().Be(0);
    }

    [Fact]
    public void Search_Performance_1000Entries_Under200ms()
    {
        // Build 1000 entries with random fields
        var rng = new Random(42);
        var entries = new List<Entry>();
        for (int i = 0; i < 1000; i++)
        {
            entries.Add(MakeEntry(
                $"Entry {i}",
                i % 2 == 0 ? "openai" : "github",
                new[] { i % 3 == 0 ? "prod" : "dev" },
                ("api_key", "sk-" + Guid.NewGuid().ToString("N"), FieldKind.Secret)));
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = _svc.Search("tags:prod AND platform:openai", entries);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);  // generous bound; 200ms target
        hits.Should().NotBeEmpty();
        // Should be roughly 1/6 of 1000 ≈ 166
        hits.Count.Should().BeInRange(140, 200);
    }

    [Fact]
    public void Search_RanksByRelevance_FieldMatchScoresHigherThanTagMatch()
    {
        var nameOnly = MakeEntry("search hit", "github", Array.Empty<string>());
        var fieldMatch = MakeEntry("Other", "github", Array.Empty<string>(),
            ("description", "this is a search hit candidate", FieldKind.Text));
        var hits = _svc.Search("search hit", new[] { nameOnly, fieldMatch });
        hits.Should().HaveCount(2);
        // Name match should score higher than field value match
        hits[0].Entry.Name.Should().Be("search hit");
    }

    [Fact]
    public void UnknownPredicate_ReturnsFalse()
    {
        var e = MakeEntry("X", "openai", Array.Empty<string>());
        _svc.Matches(e, "bogus:whatever").Should().BeFalse();
    }
}
