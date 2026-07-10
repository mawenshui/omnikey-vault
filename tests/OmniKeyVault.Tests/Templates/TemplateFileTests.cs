using FluentAssertions;
using System.Text;
using OmniKeyVault.Application;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Templates;

/// <summary>
/// Tests for the 5 platform template JSON files (templates/*.json) per PLATFORM_TEMPLATES.md 搂3.
/// Validates: structural conformance, kind=sensitive invariant, regex compiles,
/// all 5 templates are loadable.
/// </summary>
public class TemplateFileTests
{
    [Theory]
    [InlineData("github")]
    [InlineData("openai")]
    [InlineData("aws_iam_long_term")]
    [InlineData("stripe")]
    [InlineData("supabase")]
    [InlineData("anthropic")]
    [InlineData("gcp_service_account")]
    [InlineData("azure_service_principal")]
    [InlineData("aws_sts_temporary")]
    [InlineData("aliyun_ram_user")]
    [InlineData("slack")]
    public void TemplateFile_IsValidJson(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "templates", id + ".json");
        File.Exists(path).Should().BeTrue($"template file should exist: {path}");
        var svc = new TemplateService();
        var act = () => svc.LoadFromJson(File.ReadAllText(path));
        act.Should().NotThrow();
        svc.Templates.Should().ContainKey(id);
    }

    [Theory]
    [InlineData("github")]
    [InlineData("openai")]
    [InlineData("aws_iam_long_term")]
    [InlineData("stripe")]
    [InlineData("supabase")]
    [InlineData("anthropic")]
    [InlineData("gcp_service_account")]
    [InlineData("azure_service_principal")]
    [InlineData("aws_sts_temporary")]
    [InlineData("aliyun_ram_user")]
    [InlineData("slack")]
    public void TemplateFile_AllFieldsHaveRequiredAttributes(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "templates", id + ".json");
        var svc = new TemplateService();
        svc.LoadFromJson(File.ReadAllText(path));
        var def = svc.Get(id);

        def.Fields.Should().NotBeEmpty();
        foreach (var f in def.Fields)
        {
            f.Key.Should().NotBeNullOrEmpty();
            f.Label.Should().NotBeNullOrEmpty();
            f.Kind.Should().NotBeNullOrEmpty();
        }
    }

    [Theory]
    [InlineData("github")]
    [InlineData("openai")]
    [InlineData("aws_iam_long_term")]
    [InlineData("stripe")]
    [InlineData("supabase")]
    [InlineData("anthropic")]
    [InlineData("gcp_service_account")]
    [InlineData("azure_service_principal")]
    [InlineData("aws_sts_temporary")]
    [InlineData("aliyun_ram_user")]
    [InlineData("slack")]
    public void TemplateFile_KindSecretRequiresSensitiveTrue(string id)
    {
        // Per PLATFORM_TEMPLATES.md 搂2.3 invariant.
        var path = Path.Combine(AppContext.BaseDirectory, "templates", id + ".json");
        var svc = new TemplateService();
        svc.LoadFromJson(File.ReadAllText(path));
        var def = svc.Get(id);

        foreach (var f in def.Fields)
        {
            if (f.Kind == "secret")
                f.Sensitive.Should().BeTrue($"field '{f.Key}' has kind=secret but sensitive=false");
        }
    }

    [Theory]
    [InlineData("github", "pat_fine_grained", "^github_pat_[A-Za-z0-9_]{82}$")]
    [InlineData("openai", "api_key", "^sk-proj-[A-Za-z0-9_-]{20,}$")]
    [InlineData("aws_iam_long_term", "access_key_id", "^AKIA[A-Z0-9]{16}$")]
    [InlineData("aws_iam_long_term", "secret_access_key", "^[A-Za-z0-9/+=]{40}$")]
    [InlineData("stripe", "secret_key", "^sk_(live|test)_[A-Za-z0-9]{24,}$")]
    [InlineData("supabase", "secret_key", "^sb_secret_[A-Za-z0-9_-]{40,}$")]
    [InlineData("anthropic", "api_key", "^sk-ant-api03-[A-Za-z0-9_-]{20,}$")]
    [InlineData("gcp_service_account", "project_id", "^[a-z][-a-z0-9]{4,28}[a-z0-9]$")]
    [InlineData("azure_service_principal", "tenant_id", "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    [InlineData("aws_sts_temporary", "access_key_id", "^ASIA[0-9A-Z]{16}$")]
    [InlineData("aws_sts_temporary", "secret_access_key", "^[A-Za-z0-9/+=]{40}$")]
    [InlineData("aliyun_ram_user", "access_key_id", "^LTAI[a-zA-Z0-9]{16}$")]
    [InlineData("aliyun_ram_user", "access_key_secret", "^[A-Za-z0-9]{30}$")]
    [InlineData("slack", "bot_token", "^xoxb-[0-9]+-[0-9]+-[A-Za-z0-9]+$")]
    public void TemplateFile_RegexIsValid(string templateId, string fieldKey, string expectedRegex)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "templates", templateId + ".json");
        var svc = new TemplateService();
        svc.LoadFromJson(File.ReadAllText(path));
        var def = svc.Get(templateId);
        var field = def.Fields.First(f => f.Key == fieldKey);
        field.Validation.Should().NotBeNull();
        field.Validation!.Regex.Should().Be(expectedRegex);
        // Verify the regex is valid by trying to use it
        var act = () => new System.Text.RegularExpressions.Regex(expectedRegex);
        act.Should().NotThrow();
    }

    // The 5 v0.1 MVP templates + 6 v0.2 extensions per PRD §5.3.1, §5.3.2.
    public static IEnumerable<object[]> AllTemplateIds() => new List<object[]>
    {
        new object[] { "github", true, "v0.1" },
        new object[] { "openai", true, "v0.1" },
        new object[] { "aws_iam_long_term", true, "v0.1" },
        new object[] { "stripe", true, "v0.1" },
        new object[] { "supabase", true, "v0.1" },
        new object[] { "anthropic", false, "v0.2" },
        new object[] { "gcp_service_account", false, "v0.2" },
        new object[] { "azure_service_principal", false, "v0.2" },
        new object[] { "aws_sts_temporary", false, "v0.2" },
        new object[] { "aliyun_ram_user", false, "v0.2" },
        new object[] { "slack", false, "v0.2" }
    };

    [Theory]
    [MemberData(nameof(AllTemplateIds))]
    public void TemplateFile_IntroducedInMatches(string id, bool mvpIncluded, string introducedIn)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "templates", id + ".json");
        var svc = new TemplateService();
        svc.LoadFromJson(File.ReadAllText(path));
        var def = svc.Get(id);
        def.MvpIncluded.Should().Be(mvpIncluded);
        def.IntroducedIn.Should().Be(introducedIn);
    }

    [Theory]
    [InlineData("github", "pat_fine_grained", "^github_pat_[A-Za-z0-9_]{82}$")]
    [InlineData("openai", "api_key", "^sk-proj-[A-Za-z0-9_-]{20,}$")]
    [InlineData("aws_iam_long_term", "access_key_id", "^AKIA[A-Z0-9]{16}$")]
    [InlineData("aws_iam_long_term", "secret_access_key", "^[A-Za-z0-9/+=]{40}$")]
    [InlineData("stripe", "secret_key", "^sk_(live|test)_[A-Za-z0-9]{24,}$")]
    [InlineData("supabase", "secret_key", "^sb_secret_[A-Za-z0-9_-]{40,}$")]
    [InlineData("anthropic", "api_key", "^sk-ant-api03-[A-Za-z0-9_-]{20,}$")]
    [InlineData("aws_sts_temporary", "access_key_id", "^ASIA[0-9A-Z]{16}$")]
    [InlineData("aws_sts_temporary", "secret_access_key", "^[A-Za-z0-9/+=]{40}$")]
    [InlineData("aliyun_ram_user", "access_key_id", "^LTAI[a-zA-Z0-9]{16}$")]
    [InlineData("azure_service_principal", "tenant_id", "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    [InlineData("slack", "bot_token", "^xoxb-[0-9]+-[0-9]+-[A-Za-z0-9]+$")]
    public void TemplateFile_V02RegexIsValid(string templateId, string fieldKey, string expectedRegex)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "templates", templateId + ".json");
        var svc = new TemplateService();
        svc.LoadFromJson(File.ReadAllText(path));
        var def = svc.Get(templateId);
        var field = def.Fields.First(f => f.Key == fieldKey);
        field.Validation.Should().NotBeNull();
        field.Validation!.Regex.Should().Be(expectedRegex);
        var act = () => new System.Text.RegularExpressions.Regex(expectedRegex);
        act.Should().NotThrow();
    }

    [Fact]
    public void TemplateFiles_All5MvpTemplatesLoad()
    {
        var svc = new TemplateService();
        foreach (var id in new[] { "github", "openai", "aws_iam_long_term", "stripe", "supabase" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "templates", id + ".json");
            svc.LoadFromJson(File.ReadAllText(path));
        }
        svc.ListMvp().Should().HaveCount(5);
    }

    [Fact]
    public void TemplateFiles_V02_All6ExtensionsLoad()
    {
        // Per PRD §5.3.2, v0.2 adds 6 templates beyond the 5 MVP ones.
        var svc = new TemplateService();
        foreach (var id in new[] { "anthropic", "gcp_service_account", "azure_service_principal",
                                    "aws_sts_temporary", "aliyun_ram_user", "slack" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "templates", id + ".json");
            svc.LoadFromJson(File.ReadAllText(path));
        }
        // Just the 6 v0.2 templates (this test uses a fresh service, not the 5 MVP ones).
        svc.Templates.Should().HaveCount(6);
        svc.ListMvp().Should().BeEmpty();
        svc.ListAll().Should().HaveCount(6);
    }
}
