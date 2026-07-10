using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>v0.3 S6-T6: Locale tests verifying all keys in the default
/// (zh-CN) localizer have a counterpart in en-US. The test enforces
/// translation parity so a missing English string is caught at build time
/// instead of leaking as a Chinese fallback to a US-locale user.</summary>
public class LocaleTests
{
    [Fact]
    public void DefaultLocale_IsZhCn()
    {
        Locales.Default.Should().Be(Locales.ZhCn);
        Locales.IsValid(Locales.ZhCn).Should().BeTrue();
        Locales.IsValid(Locales.EnUs).Should().BeTrue();
        Locales.IsValid("xx-XX").Should().BeFalse();
    }

    [Fact]
    public void AllZhCnKeys_HaveEnUsCounterpart()
    {
        var zh = new ZhCnLocalizer();
        var en = new EnUsLocalizer();
        var probeKeys = new[]
        {
            "common.save", "common.cancel", "common.close", "common.delete",
            "common.confirm", "common.back", "common.search", "common.refresh",
            "common.yes", "common.no", "common.ok", "common.retry",
            "common.import", "common.export", "common.rotate", "common.download",
            "common.upload", "common.attach", "common.attachments",
            "unlock.title", "unlock.master_password", "unlock.unlock_button",
            "unlock.invalid_password", "unlock.empty_password", "unlock.vault_not_found",
            "unlock.browse", "unlock.tagline", "unlock.footer",
            "main.search_watermark", "main.list_title_all", "main.empty_title",
            "main.empty_sub", "main.empty_vault", "main.empty_vault_sub",
            "main.detail_empty", "main.new_entry", "main.import", "main.export",
            "main.copy_main", "main.copy_all", "main.edit", "main.duplicate",
            "main.history", "main.delete", "main.copied", "main.clipboard_cleared",
            "main.vault_unlocked", "main.vault_locked", "main.switched_profile",
            "main.sync_synced", "main.sync_complete", "main.sync_no_change",
            "main.sync_failed", "main.export_seed", "main.import_seed", "main.sync",
            "main.never_synced", "main.lock_countdown_fmt",
            "main.profile_banner_dev", "main.profile_banner_test",
            "main.import_kdbx", "main.import_bitwarden", "main.search_advanced",
            "main.search_hint",
            "editor.title_new", "editor.title_edit", "editor.name", "editor.name_hint",
            "editor.template", "editor.platform_id", "editor.type", "editor.folder",
            "editor.expires", "editor.tags", "editor.tags_hint", "editor.fields",
            "editor.add_totp", "editor.add_file_ref", "editor.add_field",
            "editor.field_key_hint", "editor.field_value_hint", "editor.notes",
            "editor.name_required", "editor.vault_locked", "editor.save_failed",
            "editor.rotate", "editor.rotate_title", "editor.rotate_confirm",
            "editor.rotate_success", "editor.rotate_failed",
            "editor.rotate_unsupported", "editor.rotate_in_progress",
            "settings.title", "settings.general", "settings.security",
            "settings.sync", "settings.profiles", "settings.devices",
            "settings.danger", "settings.about", "settings.language",
            "settings.theme", "settings.theme_system", "settings.theme_dark",
            "settings.theme_light", "settings.auto_lock", "settings.clipboard",
            "settings.lock_on_session", "settings.lock_on_suspend",
            "settings.idle_minutes", "settings.sync_dir", "settings.sync_dir_hint",
            "settings.watcher_enabled", "settings.change_password",
            "settings.view_recovery", "settings.view_recovery_hint",
            "settings.language_changed", "settings.theme_changed",
            "settings.watcher_changed", "settings.sync_dir_set",
            "settings.device_revoked", "settings.device_revoke_failed",
            "settings.attachment_dir", "settings.attachment_dir_hint",
            "profile.switcher_title", "profile.new", "profile.delete_confirm",
            "profile.delete_failed", "profile.create_failed", "profile.name_required",
            "profile.name_exists", "profile.name_hint", "profile.color",
            "profile.create", "profile.local_only", "profile.synced",
            "profile.count_fmt",
            "seed.export_title", "seed.export_hint", "seed.export_profile",
            "seed.export_path", "seed.export_default", "seed.export_strip",
            "seed.export_strip_hint", "seed.export_full_hint", "seed.export_button",
            "seed.import_title", "seed.import_hint", "seed.import_path",
            "seed.import_recent", "seed.import_target", "seed.import_prod_confirm",
            "seed.import_prod_required", "seed.import_button", "seed.import_new_profile",
            "keepass.import_title", "keepass.import_hint", "keepass.import_path",
            "keepass.import_button", "keepass.import_success_fmt",
            "keepass.import_failed", "keepass.parse_error",
            "search.title", "search.placeholder", "search.syntax_help",
            "search.results_fmt", "search.no_results", "search.clear",
            "search.field_highlighted",
            "attachment.upload", "attachment.download", "attachment.replace",
            "attachment.delete", "attachment.size_fmt", "attachment.confirm_delete",
            "attachment.upload_failed", "attachment.not_found", "attachment.kind",
            "attachment.uploaded",
            "sync.conflict_title", "sync.conflict_summary_fmt",
            "sync.conflict_local_vector", "sync.conflict_choice",
            "sync.conflict_keep_local", "sync.conflict_keep_local_sub",
            "sync.conflict_take_remote", "sync.conflict_take_remote_sub",
            "sync.conflict_merge", "sync.conflict_merge_sub", "sync.conflict_cancel",
            "sync.conflict_local_kept",
            "trust.title", "trust.body", "trust.device_id", "trust.public_key",
            "trust.reason", "trust.choice", "trust.trust", "trust.trust_once",
            "trust.reject",
            "history.title", "history.snapshots", "history.empty",
            "history.restore_button", "history.restore_confirm_fmt",
            "history.disk_persisted", "history.in_memory",
            "recovery.title", "recovery.body", "recovery.print",
            "recovery.save_pdf", "recovery.copy", "recovery.saved_check",
            "recovery.confirm", "recovery.copied", "recovery.printed",
            "recovery.saved_pdf_fmt",
            "autolock.warning_fmt", "autolock.session_locked",
            "autolock.system_suspend", "autolock.cancelled",
            "rotation.title", "rotation.progress_fmt", "rotation.old_archived",
            "rotation.platform_unsupported",
        };

        var missing = new List<string>();
        foreach (var k in probeKeys)
        {
            var zhVal = zh.Get(k);
            var enVal = en.Get(k);
            // "falls back to key" = missing
            if (enVal == k || string.IsNullOrEmpty(enVal))
                missing.Add(k);
        }
        missing.Should().BeEmpty(
            $"the following keys are missing en-US translations: {string.Join(", ", missing)}");
    }

    [Fact]
    public void SetActive_SwitchesLocale()
    {
        var original = LocaleRegistry.Active.Locale;
        try
        {
            LocaleRegistry.SetActive(Locales.EnUs);
            LocaleRegistry.Active.Locale.Should().Be(Locales.EnUs);
            LocaleRegistry.Active.Get("common.save").Should().Be("Save");

            LocaleRegistry.SetActive(Locales.ZhCn);
            LocaleRegistry.Active.Locale.Should().Be(Locales.ZhCn);
            LocaleRegistry.Active.Get("common.save").Should().Be("保存");
        }
        finally
        {
            LocaleRegistry.SetActive(original);
        }
    }

    [Fact]
    public void Format_HandlesArgs()
    {
        LocaleRegistry.SetActive(Locales.EnUs);
        LocaleRegistry.Active.Format("profile.count_fmt", 42).Should().Be("42 entries");

        LocaleRegistry.SetActive(Locales.ZhCn);
        LocaleRegistry.Active.Format("profile.count_fmt", 42).Should().Be("42 个条目");
    }

    [Fact]
    public void UnknownKey_FallsBackToKeyItself()
    {
        LocaleRegistry.Active.Get("definitely.not.a.key").Should().Be("definitely.not.a.key");
    }
}
