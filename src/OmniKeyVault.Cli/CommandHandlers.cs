using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Cli.Gui.Views;

namespace OmniKeyVault.Cli;

/// <summary>
/// Subcommand handlers per CLI_SPEC.md. Each handler returns the exit code
/// (0 = success, 2 = arg error, 3 = vault locked, etc.).
/// </summary>
public sealed class CommandHandlers
{
    private readonly CliContainer _c;
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly Func<string, string> _readPassword;
    private readonly Func<string?> _readStdinLine;

    public CommandHandlers(CliContainer container, TextWriter stdout, TextWriter stderr, Func<string, string> readPassword, Func<string?> readStdinLine)
    {
        _c = container;
        _out = stdout;
        _err = stderr;
        _readPassword = readPassword;
        _readStdinLine = readStdinLine;
    }

    public async Task<int> HandleAsync(CliParseResult args)
    {
        _currentArgs = args;
        try
        {
            return args.Command switch
            {
                "help" => HandleHelp(args.Subcommand),
                "version" => HandleVersion(),
                "vault" => await HandleVaultAsync(args),
                "entry" => await HandleEntryAsync(args),
                "template" => HandleTemplate(args),
                "import" => await HandleImportAsync(args),
                "export" => await HandleExportAsync(args),
                "profile" => await HandleProfileAsync(args),
                "sync" => await HandleSyncAsync(args),
                "config" => HandleConfig(args),
                _ => UnknownCommand(args.Command),
            };
        }
        catch (VaultLockedException ex) { _err.WriteLine($"[3] Vault locked: {ex.Message}"); return ExitCodes.VaultLocked; }
        catch (CryptoException ex) { _err.WriteLine($"[4] Crypto error: {ex.Message}"); return ExitCodes.CryptoError; }
        catch (ProfileNotFoundException ex) { _err.WriteLine($"[5] {ex.Message}"); return ExitCodes.ProfileNotFound; }
        catch (FileNotFoundException ex) { _err.WriteLine($"[6] {ex.Message}"); return ExitCodes.IoError; }
        // P4-T8: All I/O-related exceptions return exit code 6 per INTERNAL.md §3.
        catch (DirectoryNotFoundException ex) { _err.WriteLine($"[6] {ex.Message}"); return ExitCodes.IoError; }
        catch (UnauthorizedAccessException ex) { _err.WriteLine($"[6] {ex.Message}"); return ExitCodes.IoError; }
        catch (PathTooLongException ex) { _err.WriteLine($"[6] {ex.Message}"); return ExitCodes.IoError; }
        catch (IOException ex) { _err.WriteLine($"[6] {ex.Message}"); return ExitCodes.IoError; }
        catch (FormatUnsupportedException ex) { _err.WriteLine($"[12] {ex.Message}"); return ExitCodes.FormatUnsupported; }
        catch (EntryNotFoundException ex) { _err.WriteLine($"[7] {ex.Message}"); return ExitCodes.EntryNotFound; }
        catch (FieldNotFoundException ex) { _err.WriteLine($"[8] {ex.Message}"); return ExitCodes.FieldNotFound; }
        catch (NameConflictException ex) { _err.WriteLine($"[9] {ex.Message}"); return ExitCodes.NameConflict; }
        catch (FileCorruptException ex) { _err.WriteLine($"[13] {ex.Message}"); return ExitCodes.FileCorrupt; }
        catch (ValidationException ex) { _err.WriteLine($"[2] {ex.Message}"); return ExitCodes.ArgumentError; }
        catch (Exception ex) { _err.WriteLine($"[1] Internal error: {ex.Message}"); return ExitCodes.InternalError; }
    }

    // ---- version / help ----
    private int HandleVersion()
    {
        // A-T8: Version string is derived from the assembly version (Directory.Build.props).
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        _out.WriteLine($"OmniKey Vault v{ver?.ToString(3) ?? "1.1.0"}");
        _out.WriteLine($"Build: {BitConverter.ToString(_c.Format.ComputeBuildHash()).Replace("-", "").ToLowerInvariant()}");
        _out.WriteLine($"Runtime: .NET {Environment.Version}");
        _out.WriteLine($"libsodium: {Sodium.SodiumCore.SodiumVersionString()}");
        return 0;
    }

    private int HandleHelp(string? subcommand)
    {
        if (string.IsNullOrEmpty(subcommand))
        {
            _out.WriteLine(HelpText.Root);
        }
        else
        {
            _out.WriteLine(HelpText.For(subcommand));
        }
        return 0;
    }

    // ---- vault ----
    private async Task<int> HandleVaultAsync(CliParseResult args)
    {
        var sub = args.Subcommand ?? throw new ValidationException("vault requires a subcommand (create / unlock / lock / info).");
        var path = ResolveVaultPath(args);
        switch (sub)
        {
            case "create":
                {
                    if (File.Exists(path) && !args.Yes)
                        throw new ValidationException($"Vault file already exists: {path}. Use --yes to overwrite (NOT recommended �?this destroys existing data).");
                    var name = args.OptionOr("name", Path.GetFileNameWithoutExtension(path));
                    var pw = ReadPassword("Choose master password: ", confirm: true);
                    if (pw.Length < 8) throw new ValidationException("Master password must be at least 8 characters.");
                    var argon = ShouldUseWeakArgonForTests() ? Argon2Params.ForTests(32 * 1024 * 1024) : Argon2Params.Default;
                    var result = await _c.Vault.CreateAsync(path, name, Encoding.UTF8.GetBytes(pw), argon);
                    _c.Templates.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "templates"));
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(new {
                            vault_path = path,
                            uuid = result.VaultUuid,
                            recovery_key = result.RecoveryKey,
                            profiles = result.Profiles
                        }, JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"Vault created: {path}");
                        _out.WriteLine($"UUID: {result.VaultUuid}");
                        _out.WriteLine($"Recovery Key: {result.RecoveryKey}");
                        _out.WriteLine("  (Save this offline. Losing it means losing access if you forget the master password.)");
                        _out.WriteLine($"Profiles: {string.Join(", ", result.Profiles)}");
                    }
                    return 0;
                }
            case "unlock":
                {
                    var pw = ReadPassword("Master password: ", confirm: false);
                    var r = await _c.Vault.UnlockAsync(path, Encoding.UTF8.GetBytes(pw));
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(new {
                            uuid = r.VaultUuid,
                            profiles = r.Profiles,
                            vector_clock = r.VectorClock.Counters
                        }, JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"Vault unlocked: {r.VaultUuid}");
                        _out.WriteLine($"Profiles: {string.Join(", ", r.Profiles)}");
                        _out.WriteLine($"Vector clock: {r.VectorClock}");
                    }
                    return 0;
                }
            case "lock":
                {
                    _c.Vault.Lock();
                    _out.WriteLine("Vault locked.");
                    return 0;
                }
            case "info":
                {
                    if (!File.Exists(path)) throw new ValidationException($"Vault file not found: {path}");
                    // v0.1 MVP: read the file with a temporary crypto provider to get header info
                    // without unlocking (no password needed for header inspection per CLI_SPEC §3.5).
                    var fmt = new Infrastructure.VaultFormat();
                    var record = await fmt.ReadAsync(path);
                    _out.WriteLine($"Vault UUID: {record.VaultUuid}");
                    _out.WriteLine($"Format: OKV1, Header 1.0, Schema 1");
                    _out.WriteLine($"Argon2id: t={record.Argon2Params.Time}, m={record.Argon2Params.Memory / (1024*1024)}MiB, p={record.Argon2Params.Parallelism}");
                    _out.WriteLine($"Salt: {Convert.ToHexString(record.Salt).Substring(0, 16)}...");
                    _out.WriteLine($"Device public key: {Convert.ToHexString(record.DevicePublicKey.Bytes).Substring(0, 16)}...");
                    _out.WriteLine($"Profiles: {string.Join(", ", record.Profiles.Select(p => p.Name))}");
                    _out.WriteLine($"Vector clock: {record.VectorClock}");
                    return 0;
                }
            case "change-password":
                {
                    // P1-T3: Implement documented `vault change-password` subcommand per INTERNAL.md §5.1.
                    // Reads old + new passwords from env vars (never CLI args, per SECURITY.md §11.6).
                    var oldEnv = args.Option("old-password-env") ?? throw new ValidationException("--old-password-env <var> is required (current master password).");
                    var newEnv = args.Option("new-password-env") ?? throw new ValidationException("--new-password-env <var> is required (new master password).");
                    var oldPw = Environment.GetEnvironmentVariable(oldEnv) ?? throw new ValidationException($"Env var '{oldEnv}' not set.");
                    var newPw = Environment.GetEnvironmentVariable(newEnv) ?? throw new ValidationException($"Env var '{newEnv}' not set.");
                    if (newPw.Length < 8) throw new ValidationException("New master password must be at least 8 characters.");
                    // Unlock first if needed (ChangePasswordAsync requires an unlocked vault).
                    if (!_c.Vault.IsUnlocked)
                    {
                        await _c.Vault.UnlockAsync(path, Encoding.UTF8.GetBytes(oldPw));
                    }
                    await _c.Vault.ChangePasswordAsync(Encoding.UTF8.GetBytes(oldPw), Encoding.UTF8.GetBytes(newPw));
                    _out.WriteLine("Master password changed. All profile DEKs re-wrapped under the new KEK.");
                    return 0;
                }
            default:
                throw new ValidationException($"Unknown vault subcommand '{sub}'. See 'okv help vault'.");
            }
        }

    // ---- entry ----
    private async Task<int> HandleEntryAsync(CliParseResult args)
    {
        var sub = args.Subcommand ?? throw new ValidationException("entry requires a subcommand (list / get / set / delete).");
        var profile = args.Profile ?? "prod";
        switch (sub)
        {
            case "list":
                {
                    EnsureUnlocked();
                    var tag = args.Option("tag");
                    var platform = args.Option("platform");
                    var search = args.Option("search");
                    var entries = _c.Entries.List(profile, tag, platform, search);
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(entries.Select(e => new {
                            id = e.Id,
                            name = e.Name,
                            type = e.Type.ToString(),
                            platform = e.PlatformId,
                            tags = e.Tags,
                            updated_at = e.UpdatedAt.ToLocalTime(),
                            version = e.Version
                        }), JsonOpts));
                    }
                    else
                    {
                        if (entries.Count == 0) { _out.WriteLine("(no entries)"); return 0; }
                        _out.WriteLine($"{"ID",-38}  {"NAME",-30}  {"PLATFORM",-12}  {"TAGS",-20}  {"UPDATED",-12}");
                        foreach (var e in entries)
                        {
                            _out.WriteLine($"{e.Id,-38}  {Trunc(e.Name, 30),-30}  {Trunc(e.PlatformId ?? "-", 12),-12}  {Trunc(string.Join(",", e.Tags), 20),-20}  {e.UpdatedAt.LocalDateTime:yyyy-MM-dd}");
                        }
                    }
                    return 0;
                }
            case "get":
                {
                    EnsureUnlocked();
                    var idStr = args.Option("id") ?? throw new ValidationException("--id <entry-id> is required.");
                    if (!Guid.TryParse(idStr, out var id)) throw new ValidationException("--id must be a valid UUID.");
                    var fieldKey = args.Option("field");
                    var reveal = args.HasOption("reveal");
                    if (fieldKey != null)
                    {
                        var value = _c.Entries.GetField(profile, id, fieldKey);
                        // raw: no trailing newline; text/json: include newline
                        // P4-T6: Use SecureStdout for raw output to enable 30-second zeroing
                        if (args.Format == "raw")
                        {
                            if (_secureStdout != null)
                                _secureStdout.WriteRaw(value);
                            else
                                _out.Write(value);
                        }
                        else if (args.Format == "json") _out.WriteLine(JsonSerializer.Serialize(new { field = fieldKey, value = value }, JsonOpts));
                        else _out.WriteLine(value);
                    }
                    else
                    {
                        var entry = _c.Vault.GetEntry(profile, id) ?? throw new EntryNotFoundException(id);
                        if (args.Format == "json")
                        {
                            _out.WriteLine(JsonSerializer.Serialize(EntryDto(entry, reveal), JsonOpts));
                        }
                        else
                        {
                            PrintEntryDetail(entry, reveal);
                        }
                    }
                    return 0;
                }
            case "set":
                {
                    EnsureUnlocked();
                    var idStr = args.Option("id");
                    var fieldKey = args.Option("field");
                    if (idStr != null && fieldKey != null)
                    {
                        // Update single field from stdin
                        if (!Guid.TryParse(idStr, out var id)) throw new ValidationException("--id must be a valid UUID.");
                        var value = _readStdinLine() ?? throw new ValidationException("Expected field value on stdin (CLI-SEC-01: values never from command line).");
                        var updated = _c.Entries.SetField(profile, id, fieldKey, value);
                        await _c.Vault.SaveAsync();
                        _out.WriteLine($"Updated entry {updated.Id} field '{fieldKey}' (version {updated.Version}).");
                    }
                    else
                    {
                        // Create new entry from template
                        var name = args.Option("name") ?? throw new ValidationException("--name <entry-name> is required for new entries.");
                        var templateId = args.Option("template") ?? throw new ValidationException("--template <template-id> is required for new entries (see 'okv template list').");
                        if (!_c.Templates.TryGet(templateId, out var def)) throw new ValidationException($"Template '{templateId}' not found.");
                        var entry = _c.Entries.CreateFromTemplate(profile, templateId, name);
                        _c.Vault.PutEntry(profile, entry);
                        await _c.Vault.SaveAsync();
                        _out.WriteLine($"Created entry {entry.Id} from template '{templateId}' (profile: {profile}).");
                        _out.WriteLine($"  Fill fields with: okv entry set --id {entry.Id} --field <key> (value on stdin)");
                    }
                    return 0;
                }
            case "delete":
                {
                    EnsureUnlocked();
                    var idStr = args.Option("id") ?? throw new ValidationException("--id <entry-id> is required.");
                    if (!Guid.TryParse(idStr, out var id)) throw new ValidationException("--id must be a valid UUID.");
                    if (!args.Yes)
                    {
                        var entry = _c.Vault.GetEntry(profile, id) ?? throw new EntryNotFoundException(id);
                        _out.Write($"Delete entry '{entry.Name}' ({entry.Id})? [yes/N] ");
                        var ans = _readStdinLine() ?? string.Empty;
                        if (!string.Equals(ans.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                        {
                            _out.WriteLine("Cancelled.");
                            return 0;
                        }
                    }
                    _c.Entries.Delete(profile, id);
                    await _c.Vault.SaveAsync();
                    _out.WriteLine($"Deleted entry {id}.");
                    return 0;
                }
            case "search":
                {
                    // v0.3 S6-T1: full-text + field-level search. Default profile
                    // = args.Profile, but --profile is also valid.
                    EnsureUnlocked();
                    var query = args.Option("query") ?? args.Option("q")
                        ?? throw new ValidationException("--query <q> is required (e.g. 'tags:ai AND field:api_key:sk-*').");
                    var allEntries = _c.Vault.ListEntries(profile);
                    var hits = _c.Search.Search(query, allEntries);
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(hits.Select(h => new {
                            id = h.Entry.Id,
                            name = h.Entry.Name,
                            platform = h.Entry.PlatformId,
                            score = h.Score,
                            field_hits = h.FieldHits.Select(fh => new { field = fh.FieldKey, matched = fh.MatchedValue })
                        }), JsonOpts));
                    }
                    else
                    {
                        if (hits.Count == 0) { _out.WriteLine("(no matches)"); return 0; }
                        _out.WriteLine($"{"ID",-38}  {"NAME",-30}  {"PLATFORM",-12}  SCORE  MATCHED FIELDS");
                        foreach (var h in hits)
                        {
                            var matchedFields = h.FieldHits.Count == 0 ? "(name/notes)"
                                : string.Join(",", h.FieldHits.Select(fh => fh.FieldKey).Distinct());
                            _out.WriteLine($"{h.Entry.Id,-38}  {Trunc(h.Entry.Name, 30),-30}  {Trunc(h.Entry.PlatformId ?? "-", 12),-12}  {h.Score,-6:0.0}  {matchedFields}");
                        }
                    }
                    return 0;
                }
            case "rotate":
                {
                    // v0.4 S8-T1 / S8-T2 / S8-T3: one-click platform rotation.
                    // Looks up entry by id, finds a registered rotator matching
                    // the entry's platform_id, calls the rotator, writes the
                    // new value via the standard SetField path (so the entry
                    // version + history snapshot are updated automatically),
                    // and reports the result. Old value is captured in the
                    // entry's history by Entries.SetField + the post-save
                    // Backup.Capture (the MainWindow's flow does the same).
                    EnsureUnlocked();
                    var idStr = args.Option("id") ?? throw new ValidationException("--id <entry-id> is required.");
                    if (!Guid.TryParse(idStr, out var id)) throw new ValidationException("--id must be a valid UUID.");
                    var entry = _c.Vault.GetEntry(profile, id) ?? throw new EntryNotFoundException(id);
                    if (string.IsNullOrEmpty(entry.PlatformId))
                        throw new ValidationException($"Entry '{entry.Name}' has no platform_id; cannot rotate.");
                    if (!_c.Rotators.TryGetValue(entry.PlatformId, out var rotator))
                        throw new ValidationException($"No rotator registered for platform '{entry.PlatformId}' (supported: {string.Join(", ", _c.Rotators.Keys)}).");
                    var currentValue = _c.Entries.GetField(profile, id, rotator.FieldKey);
                    var result = await rotator.RotateAsync(currentValue);
                    // Write new value (this bumps entry.Version + persists a
                    // snapshot of the previous value via SetField's normal
                    // path). The CLI does NOT pop a confirmation prompt �?the
                    // `--yes` flag in the global options skips any UX gate
                    // (consistent with how the GUI's "Rotate" button behaves).
                    _c.Entries.SetField(profile, id, rotator.FieldKey, result.NewValue);
                    await _c.Vault.SaveAsync();
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(new {
                            platform = rotator.PlatformId,
                            field = rotator.FieldKey,
                            note = result.Note,
                            old_revoked = result.OldValueRevoked
                            // NewValue / OldValue intentionally NOT serialized
                            // to JSON to avoid leaking secrets via CI logs.
                        }, JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"Rotated '{entry.Name}' on platform '{rotator.PlatformId}' (field: {rotator.FieldKey}).");
                        if (!string.IsNullOrEmpty(result.Note)) _out.WriteLine($"  Note: {result.Note}");
                        _out.WriteLine($"  Old value revoked: {(result.OldValueRevoked ? "yes" : "no")}");
                        _out.WriteLine($"  New value written to entry field (version bumped).");
                        _out.WriteLine($"  Old value archived in history; use 'okv entry history --id {id}' to inspect.");
                    }
                    return 0;
                }
            case "history":
                {
                    // v0.4 S7-T6: list an entry's history snapshots, or
                    // --restore <version> to revert to a previous version.
                    EnsureUnlocked();
                    var idStr = args.Option("id") ?? throw new ValidationException("--id <entry-id> is required.");
                    if (!Guid.TryParse(idStr, out var id)) throw new ValidationException("--id must be a valid UUID.");
                    var restoreVer = args.Option("restore");
                    if (restoreVer != null)
                    {
                        if (!uint.TryParse(restoreVer, out var ver)) throw new ValidationException("--restore <version> must be a positive integer.");
                        if (!args.Yes)
                        {
                            _out.Write($"Restore entry {id} to version {ver} (current will be archived)? [yes/N] ");
                            var ans = _readStdinLine() ?? string.Empty;
                            if (!string.Equals(ans.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                            {
                                _out.WriteLine("Cancelled.");
                                return 0;
                            }
                        }
                        var restored = _c.Backup.Restore(profile, id, ver);
                        await _c.Vault.SaveAsync();
                        _out.WriteLine($"Restored entry {id} to version {ver} (new current version: {restored.Version}).");
                        return 0;
                    }
                    var history = _c.Backup.ListHistory(profile, id);
                    if (history.Count == 0)
                    {
                        _out.WriteLine($"No history for entry {id} (profile: {profile}).");
                        return 0;
                    }
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(history.Select(s => new {
                            version = s.Version,
                            captured_at = s.CapturedAt,
                            reason = s.Reason,
                            device_id = s.DeviceId
                        }), JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"{"VER",-5}  {"CAPTURED",-20}  DEVICE  REASON");
                        foreach (var s in history.OrderByDescending(s => s.Version))
                            _out.WriteLine($"{s.Version,-5}  {s.CapturedAt:yyyy-MM-dd HH:mm:ss}  {Trunc(s.DeviceId, 16),-16}  {Trunc(s.Reason ?? "-", 50)}");
                    }
                    return 0;
                }
            default:
                throw new ValidationException($"Unknown entry subcommand '{sub}'. See 'okv help entry'.");
        }
    }

    // ---- template ----
    private int HandleTemplate(CliParseResult args)
    {
        var sub = args.Subcommand ?? throw new ValidationException("template requires a subcommand (list / show / apply).");
        switch (sub)
        {
            case "list":
                {
                    var mvpOnly = args.HasOption("mvp-only");
                    var category = args.Option("category");
                    var search = args.Option("search");
                    IEnumerable<TemplateDefinition> tpls = mvpOnly ? _c.Templates.ListMvp() : _c.Templates.ListAll();
                    if (category != null) tpls = tpls.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
                    if (search != null) tpls = tpls.Where(t => t.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
                    if (args.Format == "json")  // null = text (default)
                    {
                        _out.WriteLine(JsonSerializer.Serialize(tpls.Select(t => new {
                            id = t.Id, name = t.Name, platform_id = t.PlatformId, category = t.Category,
                            fields = t.Fields.Count, mvp = t.MvpIncluded, introduced_in = t.IntroducedIn
                        }), JsonOpts));
                    }
                    else
                    {
                        if (!tpls.Any()) { _out.WriteLine("(no templates)"); return 0; }
                        _out.WriteLine($"{"ID",-25}  {"NAME",-30}  {"CATEGORY",-15}  {"FIELDS",-7}  {"MVP",-4}  INTRODUCED");
                        foreach (var t in tpls)
                        {
                            _out.WriteLine($"{Trunc(t.Id, 25),-25}  {Trunc(t.Name, 30),-30}  {Trunc(t.Category, 15),-15}  {t.Fields.Count,-7}  {(t.MvpIncluded ? "Y":"-"),-4}  {t.IntroducedIn}");
                        }
                    }
                    return 0;
                }
            case "show":
                {
                    var id = args.Option("id") ?? throw new ValidationException("--id <template-id> is required.");
                    var tpl = _c.Templates.Get(id);
                    if (args.Format == "json")
                        _out.WriteLine(JsonSerializer.Serialize(tpl, JsonOpts));
                    else
                    {
                        _out.WriteLine($"Template: {tpl.Id}");
                        _out.WriteLine($"Name: {tpl.Name}");
                        _out.WriteLine($"Platform: {tpl.PlatformId}");
                        _out.WriteLine($"Category: {tpl.Category}");
                        _out.WriteLine($"MVP: {(tpl.MvpIncluded ? "Yes" : "No")} (introduced in {tpl.IntroducedIn})");
                        _out.WriteLine($"Official docs: {tpl.OfficialDocsUrl}");
                        _out.WriteLine($"Auth header: {tpl.AuthHeader}");
                        _out.WriteLine($"Rotation supported: {tpl.RotationSupported}");
                        _out.WriteLine($"Fields ({tpl.Fields.Count}):");
                        foreach (var f in tpl.Fields)
                        {
                            _out.WriteLine($"  - {f.Key} ({f.Kind}) [{(f.Required ? "required" : "optional")}{(f.Sensitive ? ", sensitive" : "")}]");
                            _out.WriteLine($"      label: {f.Label}");
                            if (f.Validation != null) _out.WriteLine($"      regex: {f.Validation.Regex}");
                            if (f.Validation?.Hint != null) _out.WriteLine($"      hint: {f.Validation.Hint}");
                            if (!string.IsNullOrEmpty(f.Description)) _out.WriteLine($"      description: {f.Description}");
                        }
                    }
                    return 0;
                }
            case "apply":
                {
                    EnsureUnlocked();
                    var id = args.Option("id") ?? throw new ValidationException("--id <template-id> is required.");
                    var name = args.Option("name") ?? throw new ValidationException("--name <entry-name> is required.");
                    var profile = args.Profile ?? "prod";
                    var entry = _c.Entries.CreateFromTemplate(profile, id, name);
                    _c.Vault.PutEntry(profile, entry);
                    _c.Vault.SaveAsync().GetAwaiter().GetResult();
                    _out.WriteLine($"Created entry {entry.Id} from template '{id}' (profile: {profile}).");
                    return 0;
                }
            default:
                throw new ValidationException($"Unknown template subcommand '{sub}'. See 'okv help template'.");
        }
    }

    // ---- import ----
    private async Task<int> HandleImportAsync(CliParseResult args)
    {
        var input = args.Option("input") ?? throw new ValidationException("--input <path> is required.");
        // --format is parsed as a global option, so it lives on args.Format (not args.Options).
        var format = args.Format ?? "bitwarden-json";
        // Validate format BEFORE auto-unlock so unsupported formats get exit 12, not exit 5/3.
        if (format != "bitwarden-json" && format != "okv-dev" && format != "kdbx-xml")
            throw new FormatUnsupportedException(format);
        EnsureUnlocked();
        var targetProfile = args.Profile ?? "prod";
        switch (format)
        {
            case "bitwarden-json":
                {
                    var count = await _c.Bitwarden.ImportAsync(targetProfile, input);
                    await _c.Vault.SaveAsync();
                    _out.WriteLine($"Imported {count} item(s) from Bitwarden JSON into profile '{targetProfile}'.");
                    return 0;
                }
            case "okv-dev":
                {
                    var result = await _c.SeedImport.ImportAsync(input, targetProfile);
                    if (result.Warnings.Count > 0)
                    {
                        _err.WriteLine("Warnings:");
                        foreach (var w in result.Warnings) _err.WriteLine("  " + w);
                    }
                    _out.WriteLine($"Imported {result.EntriesImported} entry(s) from {input} into profile '{targetProfile}'.");
                    _out.WriteLine($"Seed UUID: {result.SeedUuid}");
                    return 0;
                }
            case "kdbx-xml":
                {
                    // v0.3 S5-T6: KeePass 2.x XML export (Title/UserName/Password/URL/Notes
                    // + CustomProperties). Binary KDBX (encrypted) is not supported �?                    // only the plaintext XML export shape that KeePass produces from
                    // File �?Export �?KeePass 2 XML.
                    var result = await _c.KeePassXml.ImportAsync(targetProfile, input);
                    await _c.Vault.SaveAsync();
                    _out.WriteLine($"Imported {result.EntriesImported} entry(s) from KeePass XML into profile '{targetProfile}'.");
                    return 0;
                }
            default:
                throw new FormatUnsupportedException(format);
        }
    }

    // ---- export ----
    private async Task<int> HandleExportAsync(CliParseResult args)
    {
        var output = args.Option("output") ?? throw new ValidationException("--output <path> is required.");
        // --format is a global option, so it lives on args.Format (not args.Options).
        var format = args.Format ?? "okv-dev";
        // Validate format BEFORE auto-unlock so unsupported formats get exit 12, not exit 5/3.
        if (format != "okv-dev")
            throw new FormatUnsupportedException(format);
        EnsureUnlocked();
        switch (format)
        {
            case "okv-dev":
                {
                    var sourceProfile = args.OptionOr("source-profile", "dev");
                    _c.SeedExport.StripSecrets = args.HasOption("strip-secrets");
                    _c.SeedExport.AllowProdProfile = args.HasOption("allow-prod-profile");
                    await _c.SeedExport.ExportAsync(sourceProfile, output);
                    var warnings = new List<string>();
                    if (_c.SeedExport.StripSecrets)
                        warnings.Add("Sensitive fields were redacted with 'REDACTED-***'.");
                    if (_c.SeedExport.AllowProdProfile && sourceProfile == "prod")
                        warnings.Add("Exported prod profile (overrode the safety guard).");
                    _out.WriteLine($"Exported profile '{sourceProfile}' to {output} (format: okv-dev).");
                    if (warnings.Count > 0)
                    {
                        _err.WriteLine("Warnings:");
                        foreach (var w in warnings) _err.WriteLine("  " + w);
                    }
                    return 0;
                }
            default:
                throw new FormatUnsupportedException(format);
        }
    }

    // ---- profile ----
    private async Task<int> HandleProfileAsync(CliParseResult args)
    {
        var sub = args.Subcommand ?? throw new ValidationException("profile requires a subcommand (list / create / switch / delete / info).");
        switch (sub)
        {
            case "list":
                {
                    EnsureUnlocked();
                    var list = _c.Profiles.List();
                    if (list.Count == 0) { _out.WriteLine("(no profiles)"); return 0; }
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(list.Select(p => new
                        {
                            name = p.Name,
                            color = p.Color,
                            entries = p.EntryCount,
                            sync = p.ParticipateInSync,
                            idle_lock_min = p.IdleLockMinutes
                        }), JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"{"NAME",-12}  {"COLOR",-8}  {"ENTRIES",-8}  {"SYNC",-5}  {"IDLE-LOCK",-10}");
                        foreach (var p in list)
                            _out.WriteLine($"{p.Name,-12}  {p.Color,-8}  {p.EntryCount,-8}  {(p.ParticipateInSync ? "yes" : "no"),-5}  {p.IdleLockMinutes + "min",-10}");
                    }
                    return 0;
                }
            case "create":
                {
                    EnsureUnlocked();
                    var name = args.Option("name") ?? throw new ValidationException("--name <profile-name> is required.");
                    var colorStr = args.OptionOr("color", name == "dev" || name == "test" ? "yellow" : "green").ToLowerInvariant();
                    var color = colorStr switch
                    {
                        "green" => ProfileColor.Green,
                        "yellow" => ProfileColor.Yellow,
                        "blue" => ProfileColor.Blue,
                        "red" => ProfileColor.Red,
                        "purple" => ProfileColor.Purple,
                        _ => throw new ValidationException($"Unknown color '{colorStr}'. Use: green/yellow/blue/red/purple.")
                    };
                    var partSync = !args.HasOption("no-sync");  // default true
                    var idleMin = int.TryParse(args.OptionOr("idle-lock-min", "15"), out var im) ? im : 15;
                    var settings = new ProfileSettings
                    {
                        ParticipateInSync = partSync,
                        AutoLockOnSwitch = false,
                        IdleLockMinutes = idleMin
                    };
                    var p = await _c.Profiles.CreateAsync(name, color, settings);
                    _out.WriteLine($"Created profile '{p.Name}' (color: {p.Color}, sync: {partSync}, idle-lock: {idleMin}min).");
                    return 0;
                }
            case "switch":
                {
                    // For CLI, "switch" sets the default profile for subsequent commands
                    // in the current process. Since each CLI invocation is independent,
                    // we just print a confirmation.
                    EnsureUnlocked();
                    var name = args.Option("name") ?? throw new ValidationException("--name <profile-name> is required.");
                    if (!_c.Vault.Profiles.ContainsKey(name))
                        throw new ProfileNotFoundException(name);
                    _out.WriteLine($"Switched to profile '{name}' (session-only; pass --profile {name} on the CLI for persistence).");
                    return 0;
                }
            case "delete":
                {
                    EnsureUnlocked();
                    var name = args.Option("name") ?? throw new ValidationException("--name <profile-name> is required.");
                    if (!args.Yes)
                    {
                        _out.Write($"Delete profile '{name}'? [yes/N] ");
                        var ans = _readStdinLine() ?? string.Empty;
                        if (!string.Equals(ans.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                        {
                            _out.WriteLine("Cancelled.");
                            return 0;
                        }
                    }
                    await _c.Profiles.DeleteAsync(name);
                    _out.WriteLine($"Deleted profile '{name}'.");
                    return 0;
                }
            case "info":
                {
                    EnsureUnlocked();
                    var name = args.Option("name") ?? args.Profile ?? "prod";
                    var info = _c.Profiles.GetInfo(name);
                    if (info == null) throw new ProfileNotFoundException(name);
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(new
                        {
                            name = info.Name,
                            color = info.Color,
                            entries = info.EntryCount,
                            sync = info.ParticipateInSync,
                            idle_lock_min = info.IdleLockMinutes
                        }, JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"Profile: {info.Name}");
                        _out.WriteLine($"Color: {info.Color}");
                        _out.WriteLine($"Entries: {info.EntryCount}");
                        _out.WriteLine($"Sync: {(info.ParticipateInSync ? "yes" : "no")}");
                        _out.WriteLine($"Idle lock: {info.IdleLockMinutes}min");
                    }
                    return 0;
                }
            default:
                throw new ValidationException($"Unknown profile subcommand '{sub}'. See 'okv help profile'.");
        }
    }

    // ---- sync ----
    private async Task<int> HandleSyncAsync(CliParseResult args)
    {
        var sub = args.Subcommand ?? throw new ValidationException("sync requires a subcommand (status / force).");
        switch (sub)
        {
            case "status":
                {
                    EnsureUnlocked();
                    var path = ResolveVaultPath(args);
                    var manifest = await _c.Sync.GetOrCreateLocalManifestAsync(path);
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(new
                        {
                            vault_uuid = manifest.VaultUuid,
                            device_id = manifest.DeviceId,
                            last_modified = manifest.LastModified,
                            profiles = manifest.Profiles,
                            vector_clock = manifest.VectorClock.Counters
                        }, JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"Vault UUID: {manifest.VaultUuid}");
                        _out.WriteLine($"Device: {manifest.DeviceId}");
                        _out.WriteLine($"Last modified: {manifest.LastModified:yyyy-MM-ddTHH:mm:ssZ} by {manifest.LastModifiedBy}");
                        _out.WriteLine($"Profiles: {string.Join(", ", manifest.Profiles)}");
                        _out.WriteLine($"Vector clock: {manifest.VectorClock}");
                    }
                    return 0;
                }
            case "force":
                {
                    EnsureUnlocked();
                    var path = ResolveVaultPath(args);
                    var remote = args.Option("remote") ?? throw new ValidationException("--remote <vault.okv> is required for 'sync force'.");
                    if (SyncPauseState.IsPaused)
                    {
                        _out.WriteLine("Sync is currently paused (use 'okv sync resume' to re-enable).");
                    }
                    var r = await _c.Sync.SyncAsync(path, remote);
                    if (r.Outcome == SyncOutcome.FailedRemoteUnreadable)
                    {
                        _err.WriteLine(r.Message);
                        return ExitCodes.FileCorrupt;
                    }
                    if (r.Outcome == SyncOutcome.FailedConflict)
                    {
                        _err.WriteLine(r.Message);
                        return ExitCodes.SyncConflict;
                    }
                    if (r.Outcome == SyncOutcome.RemoteVaultMismatch)
                    {
                        _err.WriteLine(r.Message);
                        return ExitCodes.SyncConflict;
                    }
                    _out.WriteLine($"Sync {r.Outcome}: {r.Message}");
                    return 0;
                }
            case "pause":
                {
                    SyncPauseState.IsPaused = true;
                    _out.WriteLine("Sync paused. The GUI's FileSystemWatcher will not trigger auto-sync until 'okv sync resume'.");
                    return 0;
                }
            case "resume":
                {
                    SyncPauseState.IsPaused = false;
                    _out.WriteLine("Sync resumed.");
                    return 0;
                }
            default:
                throw new ValidationException($"Unknown sync subcommand '{sub}'. See 'okv help sync'.");
        }
    }

    // ---- config ----
    private int HandleConfig(CliParseResult args)
    {
        var sub = args.Subcommand ?? throw new ValidationException("config requires a subcommand (get / set / list).");
        switch (sub)
        {
            case "get":
                {
                    var key = args.Option("key") ?? throw new ValidationException("--key <config-key> is required.");
                    var value = ConfigKeys.Read(key);
                    if (value == null)
                    {
                        _err.WriteLine($"Unknown config key '{key}'. Run 'okv config list' for available keys.");
                        return ExitCodes.ArgumentError;
                    }
                    if (args.Format == "json")
                        _out.WriteLine(JsonSerializer.Serialize(new { key, value }, JsonOpts));
                    else
                        _out.WriteLine($"{key} = {value}");
                    return 0;
                }
            case "set":
                {
                    var key = args.Option("key") ?? throw new ValidationException("--key <config-key> is required.");
                    var value = args.Option("value") ?? throw new ValidationException("--value <config-value> is required.");
                    if (!ConfigKeys.TryWrite(key, value, out var err))
                    {
                        _err.WriteLine(err);
                        return ExitCodes.ArgumentError;
                    }
                    _out.WriteLine($"{key} = {value}");
                    return 0;
                }
            case "list":
                {
                    var all = ConfigKeys.All();
                    if (args.Format == "json")
                    {
                        _out.WriteLine(JsonSerializer.Serialize(all.Select(kv => new { key = kv.Key, value = kv.Value }), JsonOpts));
                    }
                    else
                    {
                        _out.WriteLine($"{"KEY",-30}  VALUE");
                        foreach (var kv in all)
                            _out.WriteLine($"{Trunc(kv.Key, 30),-30}  {kv.Value}");
                    }
                    return 0;
                }
            default:
                throw new ValidationException($"Unknown config subcommand '{sub}'. See 'okv help config'.");
        }
    }

    // ---- helpers ----
    private CliParseResult? _currentArgs;

    private void EnsureUnlocked()
    {
        if (_c.Vault.IsUnlocked) return;
        // Auto-load + unlock the vault on first need (CLI_SPEC.md §1.2: "单次 CLI 调用通常自动解锁").
        var path = ResolveVaultPath(_currentArgs);
        if (!File.Exists(path))
            throw new ValidationException($"Vault file not found: {path}");
        var pw = ReadPassword("Master password: ", confirm: false);
        try
        {
            _c.Vault.UnlockAsync(path, Encoding.UTF8.GetBytes(pw)).GetAwaiter().GetResult();
        }
        catch (VaultLockedException) { throw; }
    }

    private static string ResolveVaultPath(CliParseResult? args)
    {
        if (args?.VaultPath != null) return args.VaultPath;
        if (args?.Option("vault") != null) return args.Option("vault")!;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OmniKeyVault", "vault.okv");
    }

    private string ReadPassword(string prompt, bool confirm)
    {
        if (!string.IsNullOrEmpty(args_PasswordFile)) {
            // P4-T5: Check password file permissions before reading.
            CheckPasswordFilePermissions(args_PasswordFile);
            var pw = File.ReadAllText(args_PasswordFile); args_PasswordFile = null; return pw.TrimEnd('\n', '\r'); }
        if (!string.IsNullOrEmpty(args_PasswordEnv))
        {
            var envName = args_PasswordEnv;
            var pw = Environment.GetEnvironmentVariable(envName);
            args_PasswordEnv = null;
            if (pw == null) throw new ValidationException($"Env var '{envName}' not set.");
            return pw;
        }
        if (args_PasswordStdin) { var pw = _readStdinLine(); args_PasswordStdin = false; return pw ?? throw new ValidationException("No password on stdin."); }
        var first = _readPassword(prompt);
        if (confirm)
        {
            var second = _readPassword("Confirm master password: ");
            if (first != second) throw new ValidationException("Passwords do not match.");
        }
        return first;
    }

    // these are reset to null after read; keep them as mutable state on the handler
    private string? args_PasswordFile;
    private string? args_PasswordEnv;
    private bool args_PasswordStdin;
    // P4-T6: Secure stdout for raw secret output (30-second zeroing)
    private SecureStdout? _secureStdout;

    /// <summary>P4-T6: Activates secure stdout mode. Called by the CLI entry
    /// point so that raw secret output is zeroed after 30 seconds.</summary>
    public void EnableSecureStdout()
    {
        _secureStdout = new SecureStdout(_out);
    }

    public void SetPasswordSources(string? file, string? env, bool stdin)
    {
        args_PasswordFile = file;
        args_PasswordEnv = env;
        args_PasswordStdin = stdin;
    }

    /// <summary>P4-T5: Check that the password file is not world-readable.
    /// On Windows, verifies via ACL that no group other than the owner has
    /// read access. On Linux/macOS, verifies the file mode is 600 or stricter.
    /// If permissions are too open, warns and requires --yes to proceed.</summary>
    private void CheckPasswordFilePermissions(string path)
    {
        if (!File.Exists(path)) return; // Let the normal read fail with FileNotFoundException

        bool tooOpen = false;
        string detail = "";

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var security = new FileSecurity(path, AccessControlSections.Access);
                var rules = security.GetAccessRules(true, true, typeof(NTAccount));
                var currentUser = WindowsIdentity.GetCurrent().User;

                foreach (FileSystemAccessRule rule in rules)
                {
                    if ((rule.FileSystemRights & FileSystemRights.Read) == 0) continue;
                    if (rule.AccessControlType == AccessControlType.Deny) continue;

                    // Allow if the rule is for the current user (owner)
                    var sid = (SecurityIdentifier)rule.IdentityReference.Translate(typeof(SecurityIdentifier));
                    if (currentUser != null && sid == currentUser) continue;

                    // Allow if the rule is for SYSTEM or Administrators
                    if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)) continue;
                    if (sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)) continue;

                    // Any other identity with read access = too open
                    tooOpen = true;
                    detail = $"File is readable by '{rule.IdentityReference.Value}'.";
                    break;
                }
            }
            catch
            {
                // If we can't check ACLs (e.g. non-NTFS), skip the check
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                // Use stat to get the octal file mode
                var psi = new System.Diagnostics.ProcessStartInfo("stat", $"-c %a \"{path}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (int.TryParse(output, out var mode))
                {
                    // Check if group or others have read access (mode & 044)
                    if ((mode & 044) != 0)
                    {
                        tooOpen = true;
                        detail = $"File mode is {output}, should be 600.";
                    }
                }
            }
            catch
            {
                // If stat fails, skip the check
            }
        }

        if (tooOpen)
        {
            _err.WriteLine($"WARNING: Password file '{path}' has overly permissive permissions. {detail}");
            if (_currentArgs?.Yes != true)
            {
                throw new ValidationException($"Password file permissions too open. Use 'chmod 600 {path}' (Linux/macOS) or restrict ACL (Windows), or pass --yes to proceed anyway.");
            }
            _err.WriteLine("  Proceeding anyway (--yes specified).");
        }
    }

private static bool ShouldUseWeakArgonForTests()
{
#if DEBUG
    return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OKV_TEST_MODE"));
#else
    // Release 构建中禁止使用弱化 KDF，防止安全降级攻击
    return false;
#endif
}

    private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";

    private static object EntryDto(Entry e, bool reveal)
        => new {
            id = e.Id,
            name = e.Name,
            type = e.Type.ToString(),
            platform = e.PlatformId,
            tags = e.Tags,
            notes = e.Notes,
            created_at = e.CreatedAt.ToLocalTime(),
            updated_at = e.UpdatedAt.ToLocalTime(),
            expires_at = e.ExpiresAt,
            version = e.Version,
            fields = e.Fields.Select(f => new {
                key = f.Key,
                kind = f.Kind.ToString(),
                sensitive = f.Sensitive,
                value = OmniKeyVault.Cli.Gui.EntryDisplayFormatter.GetDisplayValue(f, reveal)
            })
        };

    private void PrintEntryDetail(Entry e, bool reveal)
    {
        _out.WriteLine($"Entry: {e.Name}");
        _out.WriteLine($"ID: {e.Id}");
        _out.WriteLine($"Type: {e.Type}");
        if (e.PlatformId != null) _out.WriteLine($"Platform: {e.PlatformId}");
        if (e.Tags.Any()) _out.WriteLine($"Tags: {string.Join(", ", e.Tags)}");
        _out.WriteLine($"Version: {e.Version}  Created: {e.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}  Updated: {e.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
        if (e.ExpiresAt.HasValue) _out.WriteLine($"Expires: {e.ExpiresAt:yyyy-MM-dd HH:mm:ss}Z");
        _out.WriteLine($"Fields:");
        foreach (var f in e.Fields)
        {
            var value = OmniKeyVault.Cli.Gui.EntryDisplayFormatter.GetDisplayValue(f, reveal);
            _out.WriteLine($"  {f.Key} [{f.Kind}{(f.Sensitive ? ", sensitive" : "")}]: {value}");
        }
        if (!string.IsNullOrEmpty(e.Notes)) _out.WriteLine($"Notes: {e.Notes}");
    }

    private int UnknownCommand(string cmd)
    {
        _err.WriteLine($"Unknown command '{cmd}'. Run 'okv help' for usage.");
        return ExitCodes.ArgumentError;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

