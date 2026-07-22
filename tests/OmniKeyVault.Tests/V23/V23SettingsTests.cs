using Xunit;
using FluentAssertions;
using OmniKeyVault.Cli;

namespace OmniKeyVault.Tests.V23;

/// <summary>
/// v2.3: Tests for the new UX optimization features — SettingsStore properties,
/// NotificationItem, search history, tool group collapse state, etc.
/// </summary>
public class V23SettingsTests
{
    [Fact]
    public void SettingsStore_HasV23Properties()
    {
        // Verify v2.3 settings exist and have expected defaults
        SettingsStore.SidebarWidth.Should().Be(220);
        SettingsStore.DetailPanelWidth.Should().Be(380);
        SettingsStore.SearchHistory.Should().NotBeNull();
        SettingsStore.SearchHistory.Should().BeEmpty();
        SettingsStore.FontSizeScale.Should().Be("medium");
        SettingsStore.ListDensity.Should().Be("standard");
        SettingsStore.HighContrastMode.Should().BeFalse();
        SettingsStore.DetailPanelHidden.Should().BeFalse();
        SettingsStore.FirstUseGuideCompleted.Should().BeFalse();
        SettingsStore.Notifications.Should().NotBeNull();
    }

    [Fact]
    public void NotificationItem_CanBeCreated()
    {
        var item = new NotificationItem
        {
            Title = "Test",
            Message = "Test message",
            Level = NotificationLevel.Warning,
        };
        item.Title.Should().Be("Test");
        item.Message.Should().Be("Test message");
        item.Level.Should().Be(NotificationLevel.Warning);
    }

    [Fact]
    public void NotificationLevel_HasAllValues()
    {
        var levels = Enum.GetValues<NotificationLevel>();
        levels.Should().HaveCount(4);
        levels.Should().Contain(NotificationLevel.Info);
        levels.Should().Contain(NotificationLevel.Warning);
        levels.Should().Contain(NotificationLevel.Error);
        levels.Should().Contain(NotificationLevel.Success);
    }

    [Fact]
    public void SettingsStore_CanModifySearchHistory()
    {
        var original = SettingsStore.SearchHistory.ToList();
        try
        {
            SettingsStore.SearchHistory = new List<string> { "github", "openai", "aws" };
            SettingsStore.SearchHistory.Should().HaveCount(3);
            SettingsStore.SearchHistory[0].Should().Be("github");
        }
        finally
        {
            SettingsStore.SearchHistory = original;
        }
    }

    [Fact]
    public void SettingsStore_CanModifyNotifications()
    {
        var original = SettingsStore.Notifications.ToList();
        try
        {
            SettingsStore.Notifications.Add(new NotificationItem
            {
                Title = "Expired",
                Message = "Entry X has expired",
                Level = NotificationLevel.Warning,
            });
            SettingsStore.Notifications.Should().HaveCount(original.Count + 1);
            SettingsStore.Notifications[0].Title.Should().Be("Expired");
        }
        finally
        {
            SettingsStore.Notifications = original;
        }
    }

    [Fact]
    public void SettingsStore_FontSizeScale_AcceptsValidValues()
    {
        var original = SettingsStore.FontSizeScale;
        try
        {
            SettingsStore.FontSizeScale = "small";
            SettingsStore.FontSizeScale.Should().Be("small");
            SettingsStore.FontSizeScale = "large";
            SettingsStore.FontSizeScale.Should().Be("large");
            SettingsStore.FontSizeScale = "medium";
            SettingsStore.FontSizeScale.Should().Be("medium");
        }
        finally
        {
            SettingsStore.FontSizeScale = original;
        }
    }

    [Fact]
    public void SettingsStore_ListDensity_AcceptsValidValues()
    {
        var original = SettingsStore.ListDensity;
        try
        {
            SettingsStore.ListDensity = "compact";
            SettingsStore.ListDensity.Should().Be("compact");
            SettingsStore.ListDensity = "comfortable";
            SettingsStore.ListDensity.Should().Be("comfortable");
            SettingsStore.ListDensity = "standard";
            SettingsStore.ListDensity.Should().Be("standard");
        }
        finally
        {
            SettingsStore.ListDensity = original;
        }
    }

    [Fact]
    public void SettingsStore_PanelWidths_CanBeModified()
    {
        var origSidebar = SettingsStore.SidebarWidth;
        var origDetail = SettingsStore.DetailPanelWidth;
        try
        {
            SettingsStore.SidebarWidth = 280;
            SettingsStore.DetailPanelWidth = 420;
            SettingsStore.SidebarWidth.Should().Be(280);
            SettingsStore.DetailPanelWidth.Should().Be(420);
        }
        finally
        {
            SettingsStore.SidebarWidth = origSidebar;
            SettingsStore.DetailPanelWidth = origDetail;
        }
    }

    [Fact]
    public void SettingsStore_DetailPanelHidden_TogglesCorrectly()
    {
        var original = SettingsStore.DetailPanelHidden;
        try
        {
            SettingsStore.DetailPanelHidden = true;
            SettingsStore.DetailPanelHidden.Should().BeTrue();
            SettingsStore.DetailPanelHidden = false;
            SettingsStore.DetailPanelHidden.Should().BeFalse();
        }
        finally
        {
            SettingsStore.DetailPanelHidden = original;
        }
    }

    [Fact]
    public void SettingsStore_FirstUseGuideCompleted_TogglesCorrectly()
    {
        var original = SettingsStore.FirstUseGuideCompleted;
        try
        {
            SettingsStore.FirstUseGuideCompleted = true;
            SettingsStore.FirstUseGuideCompleted.Should().BeTrue();
        }
        finally
        {
            SettingsStore.FirstUseGuideCompleted = original;
        }
    }

    [Fact]
    public void SettingsStore_HighContrastMode_TogglesCorrectly()
    {
        var original = SettingsStore.HighContrastMode;
        try
        {
            SettingsStore.HighContrastMode = true;
            SettingsStore.HighContrastMode.Should().BeTrue();
        }
        finally
        {
            SettingsStore.HighContrastMode = original;
        }
    }

    [Fact]
    public void SettingsStore_CollapsedToolGroups_CanBeNull()
    {
        var original = SettingsStore.CollapsedToolGroups;
        try
        {
            SettingsStore.CollapsedToolGroups = "import-export|true;sync|false";
            SettingsStore.CollapsedToolGroups.Should().NotBeNull();
            SettingsStore.CollapsedToolGroups.Should().Contain("import-export|true");
        }
        finally
        {
            SettingsStore.CollapsedToolGroups = original;
        }
    }
}
