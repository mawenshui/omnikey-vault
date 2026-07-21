using System.Text.Json;
using OmniKeyVault.Application;
using OmniKeyVault.Cli.Gui.Views;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;

namespace OmniKeyVault.Cli;

/// <summary>
/// Dependency container for the CLI. Builds once per process and shares
/// state across subcommand handlers (so `unlock` then `entry list` work
/// in the same process).
/// </summary>
public sealed class CliContainer : IDisposable
{
    public ICryptoProvider Crypto { get; } = new SodiumCryptoProvider();
    public IVaultFormat Format { get; } = new VaultFormat();
    public ISeedFormat SeedFormat { get; } = new SeedFormat();
    public LockService Lock { get; }
    public ProfilePayloadCodec Codec { get; } = new ProfilePayloadCodec();
    public TemplateService Templates { get; } = new TemplateService();
    public IClipboardProvider Clipboard { get; } = new ClipboardProvider();
    public IStorageProvider Storage { get; } = new FileSystemStorageProvider();
    public ILockProvider FileLocks { get; } = new LockProvider();
    public ManifestService Manifests { get; } = new ManifestService();

    /// <summary>v0.2 S4-T1: real <see cref="FileSystemWatcher"/>-backed
    /// implementation (replaces the v0.2 <see cref="InMemoryWatcherProvider"/>
    /// stub). The host (MainWindow) calls <see cref="StartWatching"/> after
    /// unlock to start receiving <c>FileChanged</c> events for the configured
    /// sync directory.</summary>
    public IWatcherProvider Watcher { get; } = new FileSystemWatcherProvider();

    /// <summary>v0.2 S7-T1: real <c>Microsoft.Win32.SystemEvents</c>-backed
    /// provider on Windows; <see cref="NoOpSystemEventProvider"/> on other
    /// platforms (the GUI is Windows-only for v1, so non-Windows hosts simply
    /// never receive session-lock / suspend events).</summary>
    public ISystemEventProvider SystemEvents { get; } = OperatingSystem.IsWindows()
        ? new OmniKeyVault.Cli.Gui.WindowsSystemEventProvider()
        : new NoOpSystemEventProvider();

    public string DeviceId { get; }

    public VaultService Vault { get; private set; }
    public EntryService Entries { get; private set; }
    public ClipboardService ClipboardSvc { get; private set; }
    public BitwardenImporter Bitwarden { get; private set; }
    public ProfileService Profiles { get; private set; }
    public TotpService Totp { get; } = new();
    public BackupService Backup { get; private set; }
    public SeedExporter SeedExport { get; private set; }
    public SeedImporter SeedImport { get; private set; }
    public SyncService Sync { get; private set; }

    /// <summary>WebDAV remote sync service: orchestrates download → merge → upload
    /// against a WebDAV server. Null when WebDAV is not configured.</summary>
    public WebDavSyncService? WebDavSync { get; private set; }
    /// <summary>v0.3 S6-T1: full-text + field-level search service.
    /// Replaces the v0.2 inline <c>MainWindow.SearchMatches</c> gap-fill so
    /// the same query engine powers the quick-filter, the advanced SearchWindow,
    /// and (later) the CLI <c>search</c> subcommand.</summary>
    public SearchService Search { get; } = new();
    /// <summary>v0.3 S5-T6: KeePass 2.x XML importer. Reads the standard
    /// &lt;KeePassFile&gt; export format produced by KeePass "File → Export
    /// → KeePass 2 XML". Binary KDBX import is tracked as a follow-up
    /// (requires the full KeePass crypto stack).</summary>
    public KeePassXmlImporter KeePassXml { get; private set; }
    /// <summary>Phase 7: folder CRUD service extracted from VaultService.</summary>
    public FolderService Folders { get; private set; }
    /// <summary>v0.3 S6-T4: encrypted blob storage for file attachments
    /// referenced by <c>file_ref</c> fields. Persists to
    /// <c>%APPDATA%/OmniKeyVault/attachments/&lt;sha256&gt;.bin</c> by default;
    /// can be reconfigured via <see cref="SettingsStore.AttachmentDirectory"/>.</summary>
    public AttachmentService Attachments { get; private set; }
    /// <summary>v1.8: local audit log service. Records all critical operations
    /// (unlock/lock/create/edit/delete/rotate/change-password/sync) to a
    /// persistent JSON-lines file at %APPDATA%/OmniKeyVault/audit.log.</summary>
    public AuditLogService AuditLog { get; } = new();

    /// <summary>v1.9: global hotkey + auto-fill service.</summary>
    public HotkeyService Hotkey { get; private set; }
    /// <summary>v1.9: read-only HTTP API for browser extension.</summary>
    public BrowserExtensionApiService BrowserApi { get; private set; }
    /// <summary>v1.9.1: checks GitHub releases for updates.</summary>
    public UpdateService UpdateChecker { get; } = new();

    // ---- v2.0 services ----
    /// <summary>v2.0: Cryptographically secure password generator.</summary>
    public PasswordGeneratorService PasswordGenerator { get; } = new();
    /// <summary>v2.0: CSV importer for LastPass/Chrome/Edge/Firefox.</summary>
    public CsvImporter CsvImport { get; private set; }
    /// <summary>v2.0: 1Password CSV importer.</summary>
    public OnePasswordCsvImporter OnePasswordImport { get; private set; }
    /// <summary>v2.0: .env file import/export.</summary>
    public EnvFileService EnvFile { get; private set; }
    /// <summary>v2.0: Credential leak detection (HIBP).</summary>
    public CredentialLeakService CredentialLeakChecker { get; } = new();
    /// <summary>v2.0: X.509 certificate management.</summary>
    public CertificateService Certificates { get; } = new();
    /// <summary>v2.0: SSH Agent integration.</summary>
    public SshAgentService SshAgent { get; } = new();
    /// <summary>v2.0: S3-compatible storage sync.</summary>
    public S3SyncService S3Sync { get; } = new();
    /// <summary>v2.0: Encrypted container export/import.</summary>
    public EncryptedContainerExporter ContainerExporter { get; private set; }
    /// <summary>v2.0: Password history tracking.</summary>
    public PasswordHistoryService PasswordHistory { get; private set; }
    /// <summary>v2.0: WebAuthn/FIDO2 (Windows Hello) integration.</summary>
    public WebAuthnService WebAuthn { get; } = new();
    /// <summary>v2.0: Community template contribution mechanism.</summary>
    public CommunityTemplateService CommunityTemplates { get; private set; }
    /// <summary>v2.1: 1Password .1pux native format importer.</summary>
    public OnePuxImporter OnePuxImportNative { get; private set; }
    /// <summary>v2.1: KeePass KDBX binary format importer.</summary>
    public KeePassKdbxImporter KeePassKdbx { get; private set; }
    /// <summary>v2.1: EnPass JSON importer.</summary>
    public EnPassImporter EnPassImport { get; private set; }

    /// <summary>v0.4 S8-T1: platform-specific credential rotators. Each
    /// rotator knows the platform's API + auth flow; the EditorWindow calls
    /// <c>RotateAsync</c> and stores the new value in the entry's field.</summary>
    public IReadOnlyDictionary<string, IPlatformRotator> Rotators { get; private set; }
        = new Dictionary<string, IPlatformRotator>();

    public CliContainer(string deviceId)
    {
        DeviceId = deviceId;
        Lock = new LockService(Crypto);
        Vault = new VaultService(Crypto, Format, Lock, Codec, deviceId);
        Entries = new EntryService(Vault, Templates, new ClipboardService(Clipboard, Lock), Crypto);
        ClipboardSvc = new ClipboardService(Clipboard, Lock);
        Bitwarden = new BitwardenImporter(Entries, Vault, Crypto);
        Profiles = new ProfileService(Vault, Crypto, Lock);
        Backup = new BackupService(Vault, deviceId, crypto: Crypto,
            snapshotDir: ResolveSnapshotDirectory(), keystore: Vault is { } v ? new DeviceKeystore() : null);
        SeedExport = new SeedExporter(Vault, Crypto, Codec, SeedFormat, deviceId);
        SeedImport = new SeedImporter(Vault, Crypto, Codec, SeedFormat, deviceId);
        Sync = new SyncService(Vault, Lock, Crypto, Format, Codec, Manifests, deviceId, Watcher);
        WebDavSync = new WebDavSyncService(Sync, CreateWebDavProvider);
        Hotkey = new HotkeyService(ClipboardSvc);
        Hotkey.LoadConfig();
        BrowserApi = new BrowserExtensionApiService(Vault, Entries, ClipboardSvc);
        // v0.3 import + attachment services. KeePassXml is stateless (no
        // caching); Attachments holds an in-memory LRU of recently-decrypted
        // blobs to keep the GUI snappy when re-opening a file_ref preview.
        // The AttachmentService reads the KEK from LockService.CurrentKek on
        // every Save/Read call so the caller's lock action (which disposes
        // the KEK) automatically invalidates the cache.
        KeePassXml = new KeePassXmlImporter(Entries, Vault, Crypto);
        Folders = new FolderService(Vault, Lock);
        Attachments = new AttachmentService(Crypto, ResolveAttachmentDirectory(), GetCurrentKekCopy);
        // v0.4 rotation services: register each platform with its rotator.
        // Add new platforms by registering another IPlatformRotator in this
        // dictionary; the EditorWindow picks them up automatically.
        Rotators = new Dictionary<string, IPlatformRotator>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = new OpenAiRotator(),
            ["github"] = new GitHubPatRotator(),
        };
        // v2.0 services
        CsvImport = new CsvImporter(Entries, Vault, Crypto);
        OnePasswordImport = new OnePasswordCsvImporter(CsvImport);
        EnvFile = new EnvFileService(Entries, Vault, Crypto);
        ContainerExporter = new EncryptedContainerExporter(Crypto);
        PasswordHistory = new PasswordHistoryService(Vault, Crypto);
        CommunityTemplates = new CommunityTemplateService(Templates);
        // v2.1 importers
        OnePuxImportNative = new OnePuxImporter(Entries, Vault, Crypto);
        KeePassKdbx = new KeePassKdbxImporter(KeePassXml);
        EnPassImport = new EnPassImporter(Entries, Vault, Crypto);
    }

    /// <summary>Creates a WebDavSyncProvider from current SettingsStore values.
    /// Returns null when WebDAV is not enabled.</summary>
    private IRemoteSyncProvider? CreateWebDavProvider()
    {
        if (!SettingsStore.WebDavEnabled) return null;
        if (string.IsNullOrWhiteSpace(SettingsStore.WebDavServerUrl)) return null;
        var config = new RemoteSyncConfig
        {
            ServerUrl = SettingsStore.WebDavServerUrl!,
            Username = SettingsStore.WebDavUsername ?? "",
            Password = SettingsStore.WebDavPassword ?? "",
            RemoteFilePath = string.IsNullOrWhiteSpace(SettingsStore.WebDavRemoteFilePath)
                ? "vault.okv" : SettingsStore.WebDavRemoteFilePath!,
            Enabled = SettingsStore.WebDavEnabled,
            AutoSync = SettingsStore.WebDavAutoSync,
        };
        return new WebDavSyncProvider(config);
    }

    /// <summary>P2-T1: idempotent flag so ProcessExit + CancelKeyPress hooks
    /// + the <c>using</c> statement can all call <see cref="Dispose"/> without
    /// double-disposing <see cref="Vault"/> / <see cref="Lock"/> / etc.</summary>
    private int _disposed;

    /// <summary>P5-T1: Default snapshot storage directory:
    /// <c>%APPDATA%/OmniKeyVault/.okv.snapshots/</c>.</summary>
    private static string ResolveSnapshotDirectory()
    {
        return System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", ".okv.snapshots");
    }

    /// <summary>Default attachment storage directory: <c>%APPDATA%/OmniKeyVault/attachments/</c>.
    /// Can be overridden at runtime by setting <see cref="SettingsStore.AttachmentDirectory"/>
    /// before the first call to <c>Attachments.SaveAsync</c> (subsequent calls
    /// use the updated path).</summary>
    private static string ResolveAttachmentDirectory()
    {
        var fromSettings = SettingsStore.AttachmentDirectory;
        if (!string.IsNullOrEmpty(fromSettings)) return fromSettings;
        return System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "attachments");
    }

    /// <summary>Returns a fresh <see cref="KeyEncryptionKey"/> wrapping the
    /// current KEK bytes, or null if the vault is locked. The caller is
    /// responsible for disposing the returned key. Used by AttachmentService
    /// so it can operate on its own copy without holding a reference to the
    /// live LockService.CurrentKek (which is disposed on Lock()).</summary>
    private KeyEncryptionKey? GetCurrentKekCopy()
    {
        var current = Lock.CurrentKek;
        if (current == null) return null;
        // ToArray() copies the bytes; we wrap them in a new KEK that the
        // AttachmentService will Dispose after each call. The LockService
        // keeps its own copy alive for the duration of the unlock session.
        return KeyEncryptionKey.From(current.ToArray());
    }

    /// <summary>Loads templates from the templates/ directory next to the executable, then from %APPDATA%/OmniKeyVault/templates/.</summary>
    public int LoadTemplates()
    {
        var count = 0;
        // Built-in templates next to the executable
        var exeDir = AppContext.BaseDirectory;
        var builtin = Path.Combine(exeDir, "templates");
        if (Directory.Exists(builtin)) count += Templates.LoadFromDirectory(builtin);
        // User overrides
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniKeyVault", "templates");
        if (Directory.Exists(user)) count += Templates.LoadFromDirectory(user);
        return count;
    }

    /// <summary>v0.2 S4-T1: start watching a directory for <c>*.okv</c> changes.
    /// Idempotent — calling twice on the same path is a no-op (the prior
    /// subscription is kept). The host should call this after a successful
    /// unlock pointing at the configured sync directory
    /// (<see cref="SettingsStore.SyncDirectory"/>); if no sync directory is
    /// configured, fall back to the vault file's parent directory so any
    /// out-of-band replacement still triggers a toast.</summary>
    /// <returns><c>true</c> if a new subscription was added; <c>false</c> if
    /// the path was already being watched or does not exist on disk.</returns>
    public bool StartWatching(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath)) return false;
        if (!Directory.Exists(directoryPath)) return false;
        Watcher.Watch(directoryPath);
        return true;
    }

    /// <summary>Stop watching a directory (called on lock + dispose).</summary>
    public void StopWatching(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath)) return;
        Watcher.Unwatch(directoryPath);
    }

    public void Dispose()
    {
        // P2-T1: idempotent guard so ProcessExit + CancelKeyPress + `using` can all call Dispose.
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { SystemEvents.Stop(); } catch { /* best-effort */ }
        try { SystemEvents.Dispose(); } catch { /* best-effort */ }
        try { Vault.Dispose(); } catch { /* best-effort: avoid masking other Dispose errors */ }
        try { Lock.Dispose(); } catch { /* best-effort */ }
        try { Clipboard.Dispose(); } catch { /* best-effort */ }
        try { CredentialLeakChecker.Dispose(); } catch { /* best-effort */ }
        try { S3Sync.Dispose(); } catch { /* best-effort */ }
        try { AuditLog.FlushAsync().GetAwaiter().GetResult(); } catch { /* best-effort */ }
    }
}
