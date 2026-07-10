using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using OmniKeyVault.Application;
using OmniKeyVault.Cli.Gui.Views;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Owns the CliContainer (DI / service layer) and brokers the
/// UnlockWindow ↔ MainWindow transition. Lives for the duration of the process.
///
/// Demo mode: setting <c>OKV_GUI_DEMO_DEV=1</c> creates a throwaway vault at
/// <c>%TEMP%\okv-demo.okv</c> with master password <c>demo</c>, auto-unlocks it,
/// forces the active profile to <c>dev</c>, and shows MainWindow directly. Useful
/// for design review and screenshot capture without going through the password
/// flow every time. Not used in production (no flag → normal unlock path).
/// </summary>
public sealed class GuiShell
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly CliContainer _container;
    private string _vaultPath;
    private UnlockWindow? _unlock;
    private MainWindow? _main;

    public GuiShell()
    {
        _desktop = (IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
        var deviceId = System.Environment.MachineName + "-" + System.Environment.ProcessId;
        _container = new CliContainer(deviceId);
        _container.LoadTemplates();
        _vaultPath = ResolveDefaultVaultPath();
        // v0.3 S6-T6: initialize the i18n localizer from the persisted
        // SettingsStore.Language. Subsequent UIStrings.Get(...) calls return
        // the chosen locale's strings; the MainWindow re-renders entries +
        // status bar on its next refresh.
        if (Locales.IsValid(SettingsStore.Language))
        {
            LocaleRegistry.SetActive(SettingsStore.Language);
        }
    }

    /// <summary>Returns the vault path to unlock on startup. Per UI_UX_SPEC §4.2
    /// the app should re-open the most recently used vault; falls back to
    /// <c>%USERPROFILE%/OmniKeyVault/vault.okv</c> when no history exists.</summary>
    public static string ResolveDefaultVaultPath()
    {
        var remembered = TryLoadLastVaultPath();
        if (!string.IsNullOrEmpty(remembered) && File.Exists(remembered))
            return remembered;
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "OmniKeyVault", "vault.okv");
    }

    /// <summary>Per-user path to the "last opened vault" marker file. Lives under
    /// <c>%APPDATA%/OmniKeyVault/</c> so it survives across uninstall/reinstall
    /// (the .okv file itself lives in <c>%USERPROFILE%/OmniKeyVault/</c>).</summary>
    private static string LastVaultMarkerPath()
    {
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "OmniKeyVault", "last-vault.txt");
    }

    /// <summary>Persist the canonical "last opened vault" path so the next launch
    /// re-points at the same file even if the user moves the binary or runs from
    /// a different working directory. Called from the create-wizard on success
    /// and from the unlock window's "browse" fallback.</summary>
    public static void SaveLastVaultPath(string vaultPath)
    {
        try
        {
            if (string.IsNullOrEmpty(vaultPath)) return;
            var marker = LastVaultMarkerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, vaultPath);
        }
        catch
        {
            // best-effort — never block the user-facing flow on a config write
        }
    }

    private static string? TryLoadLastVaultPath()
    {
        try
        {
            var marker = LastVaultMarkerPath();
            if (!File.Exists(marker)) return null;
            var path = File.ReadAllText(marker).Trim();
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    public void ShowUnlock()
    {
        App.Log("ShowUnlock: start");
        // Same ShowMain-first ordering as ShowMain: open the new window first,
        // then close the old one. Otherwise the lock→unlock transition (the
        // user clicks the lock button in MainWindow) crashes with the same
        // OnLastWindowClose symptom.
        try
        {
            App.Log("ShowUnlock: about to construct UnlockWindow, _vaultPath=" + (_vaultPath ?? "<null>"));
            _unlock = new UnlockWindow(_container, _vaultPath ?? ResolveDefaultVaultPath())
            {
                Title = "OmniKey Vault — 解锁",
            };
            _unlock.UnlockSucceeded += (_, _) => ShowMain();
            _unlock.CreateVaultRequested += (_, _) => ShowCreateVault();
            App.Log("ShowUnlock: about to call _unlock.Show()");
            _unlock.Show();
            App.Log("ShowUnlock: _unlock.Show() returned, IsVisible=" + _unlock.IsVisible);
        }
        catch (Exception ex)
        {
            App.Log("ShowUnlock THREW: " + ex);
            throw;
        }
        // Now close the old window — unlock is up and counted.
        try
        {
            _main?.Close();
            App.Log("ShowUnlock: main window closed");
        }
        catch (Exception ex)
        {
            App.Log("ShowUnlock: main.Close() threw (non-fatal): " + ex.Message);
        }
        _main = null;
    }

    public void ShowMain()
    {
        App.Log("ShowMain: start");
        // CRITICAL ordering: open MainWindow FIRST, then close the previous window.
        // Avalonia's default IClassicDesktopStyleApplicationLifetime uses
        // ShutdownMode.OnLastWindowClose, so closing the previously-active
        // window (unlock OR wizard) when it's the only visible window would
        // otherwise trigger an immediate process exit before MainWindow.Show()
        // can replace it. This was the silent "unlock existing vault crashes
        // the process" symptom — the same root cause as the wizard flow that
        // was already fixed in ShowCreateVault.
        try
        {
            _main = new MainWindow(_container)
            {
                Title = "OmniKey Vault",
            };
            _main.Locked += (_, _) => { _container.Sync.StopWatch(); ShowUnlock(); };
            // P5-T7: Start watcher for auto-sync after unlock
            if (_container.Vault.CurrentVaultPath != null)
                _ = _container.Sync.StartWatchAsync(_container.Vault.CurrentVaultPath);
            App.Log("ShowMain: MainWindow constructed, calling Show()");
            _main.Show();
            App.Log("ShowMain: MainWindow.Show() returned, IsVisible=" + _main.IsVisible);
        }
        catch (Exception ex)
        {
            App.Log("ShowMain THREW while building/showing MainWindow: " + ex);
            throw;
        }
        // Now safe to close the previous window — MainWindow is up and counted
        // as an open window by the lifetime, so closing the old one doesn't
        // drop us to zero and trigger shutdown.
        try
        {
            _unlock?.Close();
            App.Log("ShowMain: unlock window closed");
        }
        catch (Exception ex)
        {
            App.Log("ShowMain: unlock.Close() threw (non-fatal): " + ex.Message);
        }
        _unlock = null;
        App.Log("ShowMain: end");
    }

    public void ShowCreateVault()
    {
        // Hide unlock window while wizard is open so user focuses on the create flow
        _unlock?.Hide();
        var wiz = new CreateVaultWizard(_container, _vaultPath)
        {
            Title = "OmniKey Vault — 创建新金库",
        };
        wiz.VaultCreated += (_, _) =>
        {
            // Wizard created + unlocked the vault — jump straight to MainWindow.
            // CRITICAL ordering: open MainWindow FIRST, then close the wizard.
            // Avalonia's default IClassicDesktopStyleApplicationLifetime uses
            // ShutdownMode.OnLastWindowClose, so closing the wizard (the only
            // open window) would otherwise trigger an immediate process exit
            // before ShowMain() can replace it. Showing MainWindow first keeps
            // the process alive across the wizard→main transition.
            try { ShowMain(); } catch (Exception ex) { CreateVaultWizard.LogCrash("ShowMain() after VaultCreated threw", ex); throw; }
            try { wiz.Close(); } catch (Exception ex) { CreateVaultWizard.LogCrash("wiz.Close() after VaultCreated threw", ex); }
        };
        wiz.Closed += (_, _) =>
        {
            // If the wizard was closed without success, re-show unlock
            if (!_container.Lock.IsUnlocked && _unlock != null)
                _unlock.Show();
        };
        wiz.Show();
    }

    /// <summary>Demo entry: create / unlock a throwaway vault on dev profile.</summary>
    public async System.Threading.Tasks.Task ShowDemoDevAsync()
    {
        _unlock?.Close();
        _unlock = null;

        var demoPath = Path.Combine(Path.GetTempPath(), "okv-demo.okv");
        var pw = "demo"u8.ToArray();
        try
        {
            if (!File.Exists(demoPath))
            {
                // Use ForTests(64 MiB) — meets INV-06 (>= 32 MiB) but fast enough for demo.
                var args = OmniKeyVault.Domain.Argon2Params.ForTests(64 * 1024 * 1024);
                await _container.Vault.CreateAsync(demoPath, "demo-vault", pw, args);
            }
            await _container.Vault.UnlockAsync(demoPath, pw);

            // Seed a few demo entries if the vault is empty
            try
            {
                var existing = _container.Entries.List("prod", null, null, null);
                if (existing.Count == 0)
                {
                    var templates = _container.Templates.ListAll().ToList();
                    var openaiTpl = templates.FirstOrDefault(t => t.Id == "openai");
                    if (openaiTpl != null)
                    {
                        var e1 = _container.Entries.CreateFromTemplate("prod", openaiTpl.Id, "OpenAI 生产环境");
                        _container.Vault.PutEntry("prod", e1);
                    }
                    var githubTpl = templates.FirstOrDefault(t => t.Id == "github");
                    if (githubTpl != null)
                    {
                        var e2 = _container.Entries.CreateFromTemplate("prod", githubTpl.Id, "GitHub PAT");
                        _container.Vault.PutEntry("prod", e2);
                    }
                    await _container.Vault.SaveAsync();
                }
            }
            catch { /* seed is best-effort */ }
        }
        catch
        {
            // If anything fails, fall back to normal unlock screen
            ShowUnlock();
            return;
        }

        // Show MainWindow on dev profile so banner + watermark are visible
            // Temporarily disable auto-lock for demo mode so the window stays
            // open for screenshot capture.
            var savedAutoLock = SettingsStore.AutoLockMinutes;
            SettingsStore.AutoLockMinutes = 9999;
            _main = new MainWindow(_container, initialProfile: "dev")
            {
                Title = "OmniKey Vault — [DEMO: dev profile]",
            };
            SettingsStore.AutoLockMinutes = savedAutoLock;
            _main.Locked += (_, _) => { _container.Sync.StopWatch(); ShowUnlock(); };
            // P5-T7: Start watcher for auto-sync after demo unlock
            if (_container.Vault.CurrentVaultPath != null)
                _ = _container.Sync.StartWatchAsync(_container.Vault.CurrentVaultPath);
            _main.Show();
    }

    /// <summary>Demo entry: open RecoveryKeyWindow standalone for screenshot.</summary>
    public void ShowRecoveryDemo()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;

        // Deterministic 192-char key (matches the algorithm used in
        // UnlockWindow.SampleRecoveryKey so the demo screenshots line up).
        const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var seed = "OKV1-RECOVERY-DEMO";
        var sb = new StringBuilder(192);
        int x = 0;
        foreach (var c in seed) x = (x * 31 + c) & 0x7fffffff;
        for (int i = 0; i < 192; i++)
        {
            x = (x * 1103515245 + 12345) & 0x7fffffff;
            sb.Append(alpha[x % alpha.Length]);
        }
        var dlg = new RecoveryKeyWindow(sb.ToString())
        {
            Title = "OmniKey Vault — 恢复密钥 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open SettingsWindow standalone.</summary>
    public async System.Threading.Tasks.Task ShowSettingsDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        var dlg = new SettingsWindow(_container)
        {
            Title = "OmniKey Vault — 设置 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open CreateVaultWizard standalone (for testing the strength meter fix).</summary>
    public void ShowCreateDemo()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        var wiz = new CreateVaultWizard(_container, _vaultPath)
        {
            Title = "OmniKey Vault — 创建金库 [DEMO]",
        };
        // Auto-progress to step 2 + simulate typing a password to trigger the
        // UpdateStrength() handler that previously crashed (IndexOutOfRange from
        // LogicalChildren reflection). If the wizard stays alive for 2 s after
        // the simulated keystrokes, the fix is verified.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            wiz.ShowStep(2);
            wiz.PasswordBox.Text = "MySecureP@ssw0rd123";
            wiz.ConfirmBox.Text = "MySecureP@ssw0rd123";
        });
        wiz.VaultCreated += (_, _) => ShowMain();
        wiz.Show();
    }

    /// <summary>Demo entry: open the EditorWindow standalone with one seed
    /// entry. Useful for design review of the editor layout without having
    /// to go through CreateVault + MainWindow first.</summary>
    public async System.Threading.Tasks.Task ShowEditorDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        // Seed one entry so the demo shows the "Edit existing" branch
        try
        {
            var existing = _container.Entries.List("prod", null, null, null);
            if (existing.Count == 0 && _container.Templates.TryGet("github", out var tpl) && tpl != null)
            {
                var e = _container.Entries.CreateFromTemplate("prod", tpl.Id, "GitHub PAT (demo)");
                _container.Vault.PutEntry("prod", e);
                await _container.Vault.SaveAsync();
                existing = _container.Entries.List("prod", null, null, null);
            }
            var entry = existing.FirstOrDefault();
            var dlg = new EditorWindow(_container, "prod", entry)
            {
                Title = "OmniKey Vault — 条目编辑器 [DEMO]",
            };
            dlg.Show();
        }
        catch (Exception ex)
        {
            App.Log("ShowEditorDemo failed: " + ex);
        }
    }

    /// <summary>Demo entry: open the SearchWindow standalone (v0.3 S6-T3).
    /// Creates a throwaway vault with a few entries so the search has
    /// something to find.</summary>
    public async System.Threading.Tasks.Task ShowSearchDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        // Seed a few entries across different platforms
        try
        {
            var existing = _container.Entries.List("prod", null, null, null);
            if (existing.Count == 0)
            {
                foreach (var tplId in new[] { "github", "openai", "aws", "stripe", "supabase", "anthropic" })
                {
                    if (_container.Templates.TryGet(tplId, out var tpl) && tpl != null)
                    {
                        var e = _container.Entries.CreateFromTemplate("prod", tpl.Id, $"{tplId}-demo");
                        _container.Vault.PutEntry("prod", e);
                    }
                }
                await _container.Vault.SaveAsync();
            }
        }
        catch (Exception ex) { App.Log("ShowSearchDemo seed failed: " + ex); }

        var dlg = new SearchWindow(_container, "prod")
        {
            Title = "OmniKey Vault — 搜索 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open the HistoryWindow standalone (v0.4 S7-T4).
    /// Edits an entry twice to produce 2 history snapshots, then shows the
    /// history view.</summary>
    public async System.Threading.Tasks.Task ShowHistoryDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        try
        {
            var existing = _container.Entries.List("prod", null, null, null);
            if (existing.Count == 0 && _container.Templates.TryGet("github", out var tpl) && tpl != null)
            {
                var e = _container.Entries.CreateFromTemplate("prod", tpl.Id, "History-demo");
                _container.Vault.PutEntry("prod", e);
                await _container.Vault.SaveAsync();
                existing = _container.Entries.List("prod", null, null, null);
            }
            if (existing.Count == 0) return;
            var entry = existing[0];
            var dlg = new HistoryWindow(_container, "prod", entry)
            {
                Title = "OmniKey Vault — 历史快照 [DEMO]",
            };
            dlg.Show();
        }
        catch (Exception ex) { App.Log("ShowHistoryDemo failed: " + ex); }
    }

    /// <summary>Demo entry: open the ProfileSwitcherWindow standalone
    /// (v0.2 S3-T2). Creates a dev profile first so the switcher has more
    /// than one option to display.</summary>
    public async System.Threading.Tasks.Task ShowProfileSwitcherDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        try
        {
            if (!_container.Vault.Profiles.ContainsKey("dev"))
            {
                await _container.Profiles.CreateAsync("dev", ProfileColor.Yellow,
                    new ProfileSettings { ParticipateInSync = true, AutoLockOnSwitch = false, IdleLockMinutes = 15 }
                );
            }
        }
        catch (Exception ex) { App.Log("ShowProfileSwitcherDemo profile create failed: " + ex); }

        var dlg = new ProfileSwitcherWindow(_container, "prod")
        {
            Title = "OmniKey Vault — 配置文件切换 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open the SyncConflictResolver standalone
    /// (v0.2 S4-T5). Synthesizes a representative SyncResult for design
    /// review (no real concurrent edit; just a representative conflict
    /// shape). The result has 2 simulated conflicts across 2 manifests.</summary>
    public async System.Threading.Tasks.Task ShowSyncConflictDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        try
        {
            // Synthesize a representative conflict: local has 3 entries, remote
            // has 3 entries with 2 conflicts (same id, different content).
            // We don't need real entries here — the resolver only shows
            // metadata (vector clock + count); the user picks a resolution.
            var vaultUuid = Guid.NewGuid();
            var localManifest = new Manifest
            {
                VaultUuid = vaultUuid,
                DeviceId = "this-laptop",
                LastModified = DateTimeOffset.UtcNow.AddMinutes(-3),
                LastModifiedBy = "this-laptop",
                Profiles = new List<string> { "prod", "dev" },
                VectorClock = new VectorClock(new Dictionary<string, long> { ["this-laptop"] = 5, ["other-laptop"] = 4 }),
                SchemaVersion = 1,
                OkvFormatVersion = "1.0",
                DevicePublicKeys = new Dictionary<string, string>(),
            };
            var remoteManifest = new Manifest
            {
                VaultUuid = vaultUuid,
                DeviceId = "other-laptop",
                LastModified = DateTimeOffset.UtcNow.AddMinutes(-1),
                LastModifiedBy = "other-laptop",
                Profiles = new List<string> { "prod", "dev" },
                VectorClock = new VectorClock(new Dictionary<string, long> { ["this-laptop"] = 4, ["other-laptop"] = 6 }),
                SchemaVersion = 1,
                OkvFormatVersion = "1.0",
                DevicePublicKeys = new Dictionary<string, string>(),
            };
            var result = new SyncResult(
                Outcome: SyncOutcome.Merged,
                LocalManifest: localManifest,
                RemoteManifest: remoteManifest,
                EntriesMerged: 3,
                ConflictsDetected: 2,
                Message: "2 conflicts require manual resolution."
            );
            var dlg = new SyncConflictResolver(result)
            {
                Title = "OmniKey Vault — 同步冲突解决 [DEMO]",
            };
            dlg.Show();
        }
        catch (Exception ex) { App.Log("ShowSyncConflictDemo failed: " + ex); }
    }

    /// <summary>Demo entry: open the DeviceTrustDialog standalone (v0.2 S4-T8).
    /// Synthesizes a fake device + public key for design review.</summary>
    public void ShowDeviceTrustDemo()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        var fakeDeviceId = "unknown-laptop-2026";
        var fakePubKey = new byte[32];
        new Random(42).NextBytes(fakePubKey);
        var dlg = new DeviceTrustDialog(fakeDeviceId, fakePubKey,
            "This device has signed a sync update but its public key is not in the trusted set.")
        {
            Title = "OmniKey Vault — 设备信任 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open the SeedExportWindow standalone (v0.2 S3-T4).
    /// No vault needed — the export dialog reads from the active profile.</summary>
    public async System.Threading.Tasks.Task ShowSeedExportDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        try
        {
            if (!_container.Vault.Profiles.ContainsKey("dev"))
            {
                await _container.Profiles.CreateAsync("dev", ProfileColor.Yellow,
                    new ProfileSettings { ParticipateInSync = true, AutoLockOnSwitch = false, IdleLockMinutes = 15 }
                );
            }
        }
        catch { /* best effort */ }
        var dlg = new SeedExportWindow(_container)
        {
            Title = "OmniKey Vault — 导出种子 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open the SeedImportWindow standalone (v0.2 S3-T4).</summary>
    public async System.Threading.Tasks.Task ShowSeedImportDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        var dlg = new SeedImportWindow(_container)
        {
            Title = "OmniKey Vault — 导入种子 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Demo entry: open the KeePassImportWindow standalone (v0.3 S5-T6).</summary>
    public async System.Threading.Tasks.Task ShowKeePassImportDemoAsync()
    {
        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        await EnsureVaultUnlockedForDemoAsync();
        var dlg = new KeePassImportWindow(_container, "prod")
        {
            Title = "OmniKey Vault — KeePass 导入 [DEMO]",
        };
        dlg.Show();
    }

    /// <summary>Helper used by all the per-window demos: ensures there's a
    /// throwaway unlocked vault so the demo windows have something to operate
    /// on. Uses <c>%TEMP%/okv-demo-windows.okv</c> with master password
    /// <c>demo-windows</c>. Idempotent across calls.</summary>
    private async System.Threading.Tasks.Task EnsureVaultUnlockedForDemoAsync()
    {
        try
        {
            if (_container.Vault.IsUnlocked) return;
            var demoPath = Path.Combine(Path.GetTempPath(), "okv-demo-windows.okv");
            var pw = "demo-windows"u8.ToArray();
            if (!File.Exists(demoPath))
            {
                await _container.Vault.CreateAsync(demoPath, "demo-windows", pw,
                    OmniKeyVault.Domain.Argon2Params.ForTests(64 * 1024 * 1024));
            }
            await _container.Vault.UnlockAsync(demoPath, pw);
        }
        catch (Exception ex)
        {
            App.Log("EnsureVaultUnlockedForDemoAsync failed: " + ex);
        }
    }

    /// <summary>Demo entry: end-to-end unlock flow against an existing vault.
    /// OKV_GUI_DEMO_UNLOCK=1 — points the unlock window at
    /// <c>%TEMP%/okv-unlock-demo.okv</c> (creating it first if needed with a
    /// known password), auto-fills the password field, clicks "Unlock", and
    /// verifies that MainWindow opens without crashing. Used to regression-test
    /// the ShowMain first / window-close-second ordering fix for the
    /// "unlock existing vault crashes" symptom.</summary>
    public async System.Threading.Tasks.Task ShowUnlockDemoAsync()
    {
        var demoPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okv-unlock-demo.okv");
        const string demoPw = "UnlockDemoP@ss-2026";
        try { if (System.IO.File.Exists(demoPath)) System.IO.File.Delete(demoPath); } catch { }
        // Create a real vault via the service layer (same code path the
        // production create flow uses, just without the wizard UI).
        try
        {
            var pw = System.Text.Encoding.UTF8.GetBytes(demoPw);
            await _container.Vault.CreateAsync(demoPath, "unlock-demo", pw,
                OmniKeyVault.Domain.Argon2Params.ForTests(64 * 1024 * 1024));
            _container.Vault.Lock();
        }
        catch (Exception ex)
        {
            App.Log("ShowUnlockDemoAsync: failed to create demo vault: " + ex);
            return;
        }
        SaveLastVaultPath(demoPath);
        _vaultPath = demoPath;

        // Re-show unlock pointing at the demo vault, then drive it.
        ShowUnlock();
        await System.Threading.Tasks.Task.Delay(500);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            _unlock!.PasswordBox.Text = demoPw;
        });
        await System.Threading.Tasks.Task.Delay(200);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            _unlock!.UnlockButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
        // Wait for Argon2id + ShowMain transition (ForTests 64 MiB is fast, but
        // give it plenty of headroom for slow hardware).
        for (int i = 0; i < 20; i++)
        {
            await System.Threading.Tasks.Task.Delay(1000);
            if (_main != null && _main.IsVisible) { App.Log($"ShowUnlockDemoAsync: MainWindow visible at i={i}s"); break; }
        }
        if (_main == null || !_main.IsVisible) App.Log("ShowUnlockDemoAsync: MainWindow did NOT appear within 20s");
    }

    /// <summary>Demo entry: full end-to-end create flow.
    /// OKV_GUI_DEMO_CREATEFULL=1 — auto-runs the entire 4-step wizard with a
    /// real Argon2id KDF + actual file write, then jumps to MainWindow. Used to
    /// verify the "create vault crashes after step 4" regression is gone.</summary>
    public async System.Threading.Tasks.Task ShowCreateFullDemoAsync()
    {
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okv-demo-createfull.log");
        void Log(string msg)
        {
            try { System.IO.File.AppendAllText(logPath, $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
            System.Console.Error.WriteLine($"[DEMO] {msg}");
        }
        try { if (System.IO.File.Exists(logPath)) System.IO.File.Delete(logPath); } catch { }
        Log("=== ShowCreateFullDemoAsync start ===");

        _main?.Close(); _main = null;
        _unlock?.Close(); _unlock = null;
        var demoPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okv-createfull-demo.okv");
        try { if (System.IO.File.Exists(demoPath)) System.IO.File.Delete(demoPath); } catch { }
        Log($"demo vault path = {demoPath}");

        var wiz = new CreateVaultWizard(_container, demoPath)
        {
            Title = "OmniKey Vault — 创建金库 [DEMO: full flow]",
        };
        wiz.VaultCreated += (_, _) =>
        {
            Log("VaultCreated event fired");
            // ShowMain FIRST, then close the wizard. Avalonia's default
            // ShutdownMode.OnLastWindowClose would otherwise kill the process
            // the instant the wizard (the only open window) closes.
            try { ShowMain(); Log("ShowMain() OK"); } catch (Exception ex) { Log("ShowMain() threw: " + ex); }
            try { wiz.Close(); Log("wizard.Close() OK"); } catch (Exception ex) { Log("wiz.Close() threw: " + ex.Message); }
        };
        wiz.Closed += (_, _) =>
        {
            Log($"wizard.Closed — vault.IsUnlocked={_container.Lock.IsUnlocked}");
            if (!_container.Lock.IsUnlocked)
            {
                Log("[DEMO] wizard closed without success; falling back to unlock");
                ShowUnlock();
            }
        };
        wiz.Show();
        Log("wizard shown");

        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { wiz.ShowStep(2); Log("ShowStep(2)"); });
            await System.Threading.Tasks.Task.Delay(200);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                wiz.PasswordBox.Text = "DemoP@ssw0rd-2026";
                wiz.ConfirmBox.Text = "DemoP@ssw0rd-2026";
                Log("passwords set");
            });
            await System.Threading.Tasks.Task.Delay(200);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { wiz.NextButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Log("clicked Next (step 2→3, triggers preview)"); });
            await System.Threading.Tasks.Task.Delay(3000);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { wiz.SavedCheck.IsChecked = true; Log("checked saved"); });
            await System.Threading.Tasks.Task.Delay(200);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { wiz.NextButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Log("clicked Next (step 3→create)"); });
            // Wait for the real create + KDF + unlock. Argon2id 256 MiB can take 5-15 s.
            for (int i = 0; i < 30; i++)
            {
                await System.Threading.Tasks.Task.Delay(1000);
                if (!wiz.IsVisible) { Log($"wizard closed at i={i}s"); break; }
                if (i % 5 == 0) Log($"still waiting at i={i}s, wiz.IsVisible={wiz.IsVisible}");
            }
            if (wiz.IsVisible) Log($"final: wiz.IsVisible={wiz.IsVisible}");
        }
        catch (Exception ex)
        {
            Log("demo driver threw: " + ex);
        }
        Log("=== ShowCreateFullDemoAsync end ===");
    }
}
