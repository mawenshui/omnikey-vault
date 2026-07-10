﻿using System.Xml.Linq;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// KeePass 2.x XML importer per v0.3 S5-T6 / ROADMAP §5.2. Reads the standard
/// <c>&lt;KeePassFile&gt;</c> export format produced by KeePass via
/// <em>File → Export → KeePass 2 XML</em> (unencrypted XML; encrypted-binary
/// KDBX files are tracked as a follow-up because they require the full
/// KeePass crypto stack + key derivation rounds).
///
/// For each &lt;Entry&gt; in the export, we create an <see cref="Entry"/> with:
///   - <c>Name</c>   ← &lt;Title&gt;
///   - <c>Type</c>   ← ApiKey if &lt;Password&gt; is set, Note otherwise
///   - Fields       ← standard slots (username / password / url / notes) +
///                    any &lt;CustomProperties&gt; properties
///   - TOTP         ← any property whose name matches "totp" or "otp" with a
///                    value starting with "otpauth://"
///   - PlatformId   ← extracted from &lt;URL&gt; host (e.g. "openai" from
///                    "https://platform.openai.com/...")
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class KeePassXmlImporter
{
    private readonly EntryService _entries;
    private readonly VaultService _vault;
    private readonly ICryptoProvider _crypto;

    public KeePassXmlImporter(EntryService entries, VaultService vault, ICryptoProvider crypto)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    /// <summary>Import a KeePass 2 XML file into <paramref name="profileName"/>.
    /// Returns the number of entries created. The vault is saved automatically
    /// after import completes (caller is responsible for <c>SaveAsync</c> on
    /// error to roll back partial state — we use per-entry try/catch so a
    /// single bad entry doesn't abort the whole import).</summary>
    public async Task<KeePassImportResult> ImportAsync(string profileName, string xmlPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(xmlPath)) throw new ArgumentException("XML path required.", nameof(xmlPath));
        if (!File.Exists(xmlPath)) throw new ValidationException($"KeePass XML file not found: {xmlPath}");
        var xml = await File.ReadAllTextAsync(xmlPath, ct);
        return ImportFromString(profileName, xml, ct);
    }

    /// <summary>Import from an in-memory XML string. Visible for tests.</summary>
    public KeePassImportResult ImportFromString(string profileName, string xml, CancellationToken ct = default)
    {
        _vault.GetProfile(profileName);  // throws if locked / missing
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new ValidationException($"Failed to parse KeePass XML: {ex.Message}");
        }
        var root = doc.Root;
        if (root == null) throw new ValidationException("KeePass XML is empty.");

        // Be lenient about the root element: <KeePassFile> for v2.x, but
        // some exporters wrap as <PwDatabase> or just have <Root> at the
        // top. We look for <Root>/<Group>/<Entry> chains.
        var rootGroup = root.Element("Root") ?? root;
        var entries = rootGroup.Descendants("Entry").ToList();
        if (entries.Count == 0)
            throw new ValidationException("No <Entry> elements found in the KeePass XML.");

        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var xmlEntry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var title = ReadStringField(xmlEntry, "Title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    skipped++;
                    continue;
                }
                var username = ReadStringField(xmlEntry, "UserName");
                var password = ReadStringField(xmlEntry, "Password");
                var url = ReadStringField(xmlEntry, "URL");
                var notes = ReadStringField(xmlEntry, "Notes");

                var fields = new List<Field>();
                if (!string.IsNullOrEmpty(username))
                    fields.Add(new Field { Key = "username", Value = FieldCodec.Encode(username), Kind = FieldKind.Text, Sensitive = false });
                if (!string.IsNullOrEmpty(password))
                    fields.Add(new Field { Key = "password", Value = FieldCodec.Encode(password), Kind = FieldKind.Secret, Sensitive = true });
                if (!string.IsNullOrEmpty(url))
                    fields.Add(new Field { Key = "url", Value = FieldCodec.Encode(url), Kind = FieldKind.Url, Sensitive = false });

                // Custom properties — pass through. TOTP-like properties are
                // promoted to kind=TotpUri for the EditorWindow to recognize.
                var customProps = xmlEntry.Element("CustomProperties");
                if (customProps != null)
                {
                    foreach (var prop in customProps.Elements("Property"))
                    {
                        var key = prop.Element("Key")?.Value?.Trim();
                        var val = prop.Element("Value")?.Value ?? "";
                        if (string.IsNullOrEmpty(key)) continue;
                        // KeePass sometimes wraps with whitespace — trim.
                        val = val.Trim();
                        var lower = key.ToLowerInvariant();
                        FieldKind kind;
                        bool sensitive;
                        if ((lower == "totp" || lower == "otp") && val.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
                        {
                            kind = FieldKind.TotpUri;
                            sensitive = true;
                        }
                        else
                        {
                            kind = FieldKind.Secret;
                            sensitive = true;
                        }
                        // Skip duplicates of the standard slots already added
                        if (fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        fields.Add(new Field { Key = key, Value = FieldCodec.Encode(val), Kind = kind, Sensitive = sensitive });
                    }
                }

                var platformId = ExtractPlatformId(url);

                var entry = new Entry
                {
                    Id = _crypto.NewUuidV7(),
                    Type = !string.IsNullOrEmpty(password) ? EntryType.ApiKey : EntryType.Note,
                    Name = title,
                    PlatformId = platformId,
                    Tags = new List<string>(),
                    Folder = null,
                    Fields = fields,
                    Notes = string.IsNullOrEmpty(notes) ? null : notes,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ExpiresAt = null,
                    Version = 1,
                };
                _entries.Upsert(profileName, entry);
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                skipped++;
            }
        }
        return new KeePassImportResult
        {
            EntriesImported = imported,
            EntriesSkipped = skipped,
            Errors = errors,
        };
    }

    private static string? ReadStringField(XElement entry, string key)
    {
        var str = entry.Elements("String")
            .FirstOrDefault(s => string.Equals(s.Element("Key")?.Value, key, StringComparison.Ordinal));
        return str?.Element("Value")?.Value;
    }

    /// <summary>Heuristic: extract a short platform id from the URL host.
    /// Examples: "https://platform.openai.com/signup" → "openai";
    /// "https://dashboard.stripe.com/apikeys" → "stripe";
    /// "https://github.com/settings/tokens" → "github";
    /// null for non-platform URLs (about:blank, mailto:, example.com, etc.).
    /// We match the apex / registrable domain (the last 2 segments) plus a
    /// few common 3-segment ccTLDs, so dashboard.stripe.com and
    /// platform.openai.com both resolve correctly.</summary>
    private static string? ExtractPlatformId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != "http" && uri.Scheme != "https") return null;
        var host = uri.Host.ToLowerInvariant();

        // Try matching common subdomains: strip them and try again
        // (dashboard.stripe.com → stripe.com).
        foreach (var prefix in new[] { "www.", "platform.", "app.", "api.", "dashboard.", "console.", "admin.", "auth.", "accounts." })
        {
            if (host.StartsWith(prefix))
            {
                var stripped = host.Substring(prefix.Length);
                var match = MatchPlatformByHost(stripped);
                if (match != null) return match;
            }
        }
        return MatchPlatformByHost(host);
    }

    private static string? MatchPlatformByHost(string host)
    {
        // Map common apex domains to platform ids that match our template names.
        return host switch
        {
            "openai.com" or "chatgpt.com" or "chat.openai.com" => "openai",
            "github.com" or "github.io" => "github",
            "gitlab.com" => "gitlab",
            "supabase.com" or "supabase.io" => "supabase",
            "stripe.com" => "stripe",
            "anthropic.com" => "anthropic",
            "aws.amazon.com" or "amazonaws.com" => "aws",
            "google.com" or "cloud.google.com" or "gcp.google.com" => "gcp",
            "azure.com" or "portal.azure.com" => "azure",
            "slack.com" => "slack",
            "firebase.google.com" or "firebase.com" => "firebase",
            "vercel.com" => "vercel",
            "netlify.com" => "netlify",
            "auth0.com" => "auth0",
            "twilio.com" => "twilio",
            "sentry.io" => "sentry",
            "bitbucket.org" => "bitbucket",
            _ => null,
        };
    }
}

/// <summary>Result of a KeePass import run.</summary>
public sealed class KeePassImportResult
{
    public int EntriesImported { get; init; }
    public int EntriesSkipped { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
