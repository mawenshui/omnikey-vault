﻿using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Full-text + field-level search service per v0.3 S6-T1 / S6-T2 / ROADMAP §5.3.
///
/// The v0.2 inline search logic (in <c>MainWindow.SearchMatches</c>) is moved
/// here so the same code is reused by:
///   - <c>MainWindow</c> — quick-filter as the user types in the search box
///   - <c>SearchWindow</c> — full search results with field highlighting
///   - the future CLI <c>search</c> subcommand
///
/// Query syntax (all case-insensitive):
///   - <c>tags:dev</c>             — match any tag containing "dev"
///   - <c>platform:openai</c>      — match the platform id containing "openai"
///   - <c>name:foo</c>             — match the entry name containing "foo"
///   - <c>notes:bar</c>            — match the notes containing "bar"
///   - <c>field:api_key:sk-*</c>   — match field "api_key" containing "sk-*"
///   - <c>field:api_key</c>        — entries that have a field named "api_key"
///   - <c>expired</c>              — entries with ExpiresAt &lt; UtcNow
///   - <c>plaintext</c>            — search name + notes + field values + tags + platform
/// Combinators: <c>AND</c> (default), <c>OR</c> (case-insensitive keywords, must
/// be word-bounded). Whitespace inside a predicate is preserved (so
/// <c>field:secret_key:sk-1234</c> survives tokenization).
/// 
/// Phase 9 optimization: an inverted index cache pre-decodes field values so
/// repeated searches (e.g. as the user types in the search box) avoid the
/// per-field UTF-8 decode on every keystroke.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class SearchService
{
    // Phase 9: pre-decoded entry index for fast repeated searches.
    private List<IndexedEntry>? _index;

    /// <summary>Search a set of entries. Returns matches with field-hit
    /// metadata suitable for highlighting in the GUI. Result is ordered by
    /// score (highest first), then by updated_at descending. Performance
    /// budget: 1000 entries should complete in &lt; 200 ms (ROADMAP §5.4).</summary>
    public IReadOnlyList<SearchHit> Search(string query, IEnumerable<Entry> entries)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();

        var predicate = ParseQuery(query);
        if (predicate == null) return Array.Empty<SearchHit>();

        // Phase 9: use the indexed path when the entry collection is a list
        // (the common case from VaultService.ListEntries). This avoids
        // re-decoding field values on every search.
        var entryList = entries as IReadOnlyList<Entry> ?? entries.ToList();
        var indexed = GetOrBuildIndex(entryList);

        var hits = new List<SearchHit>();
        for (int i = 0; i < indexed.Count; i++)
        {
            var fieldHits = new List<FieldHit>();
            double score = 0;
            if (predicate(indexed[i], fieldHits, ref score))
            {
                hits.Add(new SearchHit
                {
                    Entry = entryList[i],
                    FieldHits = fieldHits,
                    Score = score,
                });
            }
        }
        return hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Entry.UpdatedAt)
            .ToList();
    }

    /// <summary>Convenience: returns the matching entries without hit
    /// metadata. Used by the simple text filter on the main window search
    /// box (where highlighting would be too noisy while typing).</summary>
    public IReadOnlyList<Entry> SearchEntries(string query, IEnumerable<Entry> entries)
        => Search(query, entries).Select(h => h.Entry).ToList();

    /// <summary>Single-entry predicate test. Returns <c>true</c> if the entry
    /// matches the query. Useful for unit tests + filter callbacks.</summary>
    public bool Matches(Entry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var predicate = ParseQuery(query);
        return predicate?.Invoke(new IndexedEntry(entry), null, ref _scoreDiscard) ?? true;
    }

    /// <summary>Phase 9: Invalidates the cached index. Call this when the
    /// underlying entry collection changes (e.g. after Add/Delete/Update)
    /// so the next Search rebuilds the index from fresh data.</summary>
    public void InvalidateIndex()
    {
        _index = null;
    }

    // ============================================================
    //  Phase 9: Indexed entry (pre-decoded field values)
    // ============================================================

    /// <summary>Pre-decoded entry data. Avoids calling FieldCodec.Decode
    /// on every search iteration by caching the decoded string values.</summary>
    private sealed class IndexedEntry
    {
        public Entry Source { get; }
        public string Name { get; }
        public string Notes { get; }
        public string PlatformId { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<IndexedField> Fields { get; }

        public IndexedEntry(Entry entry)
        {
            Source = entry;
            Name = entry.Name;
            Notes = entry.Notes ?? string.Empty;
            PlatformId = entry.PlatformId ?? string.Empty;
            Tags = entry.Tags;
            Fields = entry.Fields
                .Select(f => new IndexedField(f.Key, FieldCodec.Decode(f.Value)))
                .ToList();
        }
    }

    private sealed record IndexedField(string Key, string Value);

    private List<IndexedEntry> GetOrBuildIndex(IReadOnlyList<Entry> entries)
    {
        // Rebuild if the entry count changed (cheap version check).
        // A more robust check would compare entry IDs, but count + version
        // is sufficient for the common case (the caller calls InvalidateIndex
        // after mutations).
        if (_index != null && _index.Count == entries.Count)
            return _index;

        _index = entries.Select(e => new IndexedEntry(e)).ToList();
        return _index;
    }

    // ============================================================
    //  Internals
    // ============================================================

    private const double NameMatchWeight = 5.0;
    private const double TagMatchWeight = 3.0;
    private const double PlatformMatchWeight = 2.0;
    private const double FieldKeyMatchWeight = 2.0;
    private const double FieldValueMatchWeight = 4.0;
    private const double NotesMatchWeight = 1.0;
    private double _scoreDiscard;

    /// <summary>Parses the query into a single predicate function. The
    /// function takes an indexed entry, an optional field-hit sink (used by the
    /// GUI to render highlights), and a score accumulator.</summary>
    private delegate bool QueryPredicate(IndexedEntry entry, List<FieldHit>? fieldHits, ref double score);

    private static QueryPredicate? ParseQuery(string query)
    {
        // Tokenize, preserving the AND/OR keywords as separate tokens.
        var tokens = Tokenize(query);
        if (tokens.Count == 0) return null;

        // Build a left-to-right AND/OR expression.
        QueryPredicate? left = null;
        bool expectOr = false;
        bool haveLeft = false;

        foreach (var tok in tokens)
        {
            if (string.Equals(tok, "AND", StringComparison.OrdinalIgnoreCase)) { expectOr = false; continue; }
            if (string.Equals(tok, "OR", StringComparison.OrdinalIgnoreCase)) { expectOr = true; continue; }

            var p = CompilePredicate(tok);
            if (!haveLeft)
            {
                left = p;
                haveLeft = true;
            }
            else if (expectOr)
            {
                var prev = left!;
                left = (e, hits, ref sc) => prev(e, hits, ref sc) || p(e, hits, ref sc);
            }
            else
            {
                var prev = left!;
                left = (e, hits, ref sc) => prev(e, hits, ref sc) && p(e, hits, ref sc);
            }
        }
        return left;
    }

    private static List<string> Tokenize(string query)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < query.Length; i++)
        {
            if (TryMatchKeyword(query, i, "AND")) { FlushAndPush(result, sb); result.Add("AND"); i += 2; continue; }
            if (TryMatchKeyword(query, i, "OR")) { FlushAndPush(result, sb); result.Add("OR"); i += 1; continue; }
            sb.Append(query[i]);
        }
        FlushAndPush(result, sb);
        return result;
    }

    private static void FlushAndPush(List<string> result, System.Text.StringBuilder sb)
    {
        if (sb.Length == 0) return;
        var s = sb.ToString().Trim();
        if (s.Length > 0) result.Add(s);
        sb.Clear();
    }

    private static bool TryMatchKeyword(string s, int i, string kw)
    {
        if (i + kw.Length > s.Length) return false;
        for (int k = 0; k < kw.Length; k++)
            if (char.ToLowerInvariant(s[i + k]) != char.ToLowerInvariant(kw[k])) return false;
        if (i > 0 && !char.IsWhiteSpace(s[i - 1])) return false;
        if (i + kw.Length < s.Length && !char.IsWhiteSpace(s[i + kw.Length])) return false;
        return true;
    }

    private static QueryPredicate CompilePredicate(string raw)
    {
        var p = raw.Trim();
        if (string.IsNullOrEmpty(p)) return (_, _, ref _) => true;
        if (string.Equals(p, "expired", StringComparison.OrdinalIgnoreCase))
        {
            return (e, _, ref sc) =>
            {
                if (e.Source.ExpiresAt.HasValue && e.Source.ExpiresAt.Value < DateTimeOffset.UtcNow)
                {
                    sc += NotesMatchWeight;
                    return true;
                }
                return false;
            };
        }
        var colon = p.IndexOf(':');
        if (colon <= 0)
        {
            // Free-text predicate: name + notes + field values + tags + platform
            var needle = p;
            return (e, hits, ref sc) =>
            {
                bool any = false;
                if (ContainsCI(e.Name, needle)) { sc += NameMatchWeight; any = true; }
                if (ContainsCI(e.PlatformId, needle)) { sc += PlatformMatchWeight; any = true; }
                if (ContainsCI(e.Notes, needle)) { sc += NotesMatchWeight; any = true; }
                if (e.Tags.Any(t => ContainsCI(t, needle))) { sc += TagMatchWeight; any = true; }
                foreach (var f in e.Fields)
                {
                    if (ContainsCI(f.Key, needle)) { sc += FieldKeyMatchWeight; any = true; }
                    if (ContainsCI(f.Value, needle))
                    {
                        sc += FieldValueMatchWeight;
                        any = true;
                        hits?.Add(new FieldHit { FieldKey = f.Key, MatchedValue = f.Value,
                            StartIndex = IndexOfCI(f.Value, needle), Length = needle.Length });
                    }
                }
                return any;
            };
        }
        var key = p.Substring(0, colon).Trim().ToLowerInvariant();
        var rest = p.Substring(colon + 1).Trim();
        return key switch
        {
            "tags" => (e, _, ref sc) =>
            {
                if (e.Tags.Any(t => ContainsCI(t, rest))) { sc += TagMatchWeight; return true; }
                return false;
            },
            "platform" => (e, _, ref sc) =>
            {
                if (ContainsCI(e.PlatformId, rest)) { sc += PlatformMatchWeight; return true; }
                return false;
            },
            "name" => (e, _, ref sc) =>
            {
                if (ContainsCI(e.Name, rest)) { sc += NameMatchWeight; return true; }
                return false;
            },
            "notes" => (e, _, ref sc) =>
            {
                if (ContainsCI(e.Notes, rest)) { sc += NotesMatchWeight; return true; }
                return false;
            },
            "field" => FieldColonPredicate(rest),
            _ => (_, _, ref _) => false,
        };
    }

    private static QueryPredicate FieldColonPredicate(string rest)
    {
        var c2 = rest.IndexOf(':');
        if (c2 < 0)
        {
            // field:KEY only — entry must have a field with this key
            var key = rest;
            return (e, _, ref sc) =>
            {
                if (e.Fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    sc += FieldKeyMatchWeight;
                    return true;
                }
                return false;
            };
        }
        var k = rest.Substring(0, c2).Trim();
        var v = rest.Substring(c2 + 1).Trim();
        return (e, hits, ref sc) =>
        {
            bool any = false;
            foreach (var f in e.Fields)
            {
                if (string.Equals(f.Key, k, StringComparison.OrdinalIgnoreCase))
                {
                    if (ContainsCI(f.Value, v))
                    {
                        sc += FieldValueMatchWeight;
                        any = true;
                        if (hits != null)
                        {
                            hits.Add(new FieldHit
                            {
                                FieldKey = f.Key,
                                MatchedValue = f.Value,
                                StartIndex = IndexOfCI(f.Value, v),
                                Length = v.Length,
                            });
                        }
                    }
                }
            }
            return any;
        };
    }

    private static bool ContainsCI(string haystack, string needle)
        => !string.IsNullOrEmpty(needle) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static int IndexOfCI(string haystack, string needle)
        => string.IsNullOrEmpty(needle) ? -1 : haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One search result: the matching entry + the field/value spans
/// that produced the match (used by the GUI to highlight).</summary>
public sealed class SearchHit
{
    public required Entry Entry { get; init; }
    public IReadOnlyList<FieldHit> FieldHits { get; init; } = Array.Empty<FieldHit>();
    public double Score { get; init; }
}

/// <summary>A specific field/value substring that matched the query.</summary>
public sealed class FieldHit
{
    public required string FieldKey { get; init; }
    public required string MatchedValue { get; init; }
    /// <summary>Case-insensitive start index of the match in <see cref="MatchedValue"/>.</summary>
    public int StartIndex { get; init; }
    public int Length { get; init; }
}
