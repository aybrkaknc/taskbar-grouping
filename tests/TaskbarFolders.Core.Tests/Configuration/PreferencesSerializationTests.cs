using System.Text.Json;
using FluentAssertions;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Core.Tests.Configuration;

public class PreferencesSerializationTests
{
    // --- Theme + PopupPosition serialised as camelCase strings ----------------------

    [Theory]
    [InlineData(ThemePreference.System, "\"system\"")]
    [InlineData(ThemePreference.Light, "\"light\"")]
    [InlineData(ThemePreference.Dark, "\"dark\"")]
    public void AppSettings_Theme_SerialisesAsCamelCase(ThemePreference value, string expectedFragment)
    {
        var settings = new AppSettings { Theme = value };

        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);

        json.Should().Contain($"\"theme\": {expectedFragment}");
    }

    [Theory]
    [InlineData(PopupPositionPreference.Auto, "\"auto\"")]
    [InlineData(PopupPositionPreference.Above, "\"above\"")]
    [InlineData(PopupPositionPreference.Below, "\"below\"")]
    public void AppSettings_PopupPosition_SerialisesAsCamelCase(PopupPositionPreference value, string expectedFragment)
    {
        var settings = new AppSettings { PopupPosition = value };

        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);

        json.Should().Contain($"\"popupPosition\": {expectedFragment}");
    }

    [Theory]
    [InlineData("system", ThemePreference.System)]
    [InlineData("SYSTEM", ThemePreference.System)]
    [InlineData("Light", ThemePreference.Light)]
    [InlineData("dark", ThemePreference.Dark)]
    public void AppSettings_Theme_DeserialisesCaseInsensitively(string written, ThemePreference expected)
    {
        var json = $"{{\"theme\":\"{written}\"}}";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default);

        settings.Should().NotBeNull();
        settings!.Theme.Should().Be(expected);
    }

    // --- GroupConfig's per-group "theme" was never read and is gone (upstream #30) -----

    [Fact]
    public void GroupConfig_FromAFileThatStillHasATheme_LoadsAndDropsIt()
    {
        // Every group file written before the field was removed carries it.
        var json = "{\"id\":\"x\",\"groupName\":\"n\",\"columns\":4,\"theme\":\"dark\",\"apps\":[]}";

        var config = JsonSerializer.Deserialize<GroupConfig>(json, JsonOptions.Default);
        var rewritten = JsonSerializer.Serialize(config, JsonOptions.Default);

        config.Should().NotBeNull();
        config!.Columns.Should().Be(4, "the rest of the file still applies");
        rewritten.Should().NotContain("\"theme\"", "the next save leaves the field out");
    }

    // --- GroupConfig.Columns clamps to [MinColumns..MaxColumns] ---------------------

    [Theory]
    [InlineData(0, GroupConfig.MinColumns)]
    [InlineData(-5, GroupConfig.MinColumns)]
    [InlineData(GroupConfig.MaxColumns + 1, GroupConfig.MaxColumns)]
    [InlineData(999, GroupConfig.MaxColumns)]
    public void Columns_IsClampedOnAssignment(int input, int expected)
    {
        var config = new GroupConfig { Id = "g", GroupName = "n", Columns = input };

        config.Columns.Should().Be(expected);
    }

    [Fact]
    public void Columns_DeserialisedOutOfRangeValueIsClamped()
    {
        var json = "{\"id\":\"x\",\"groupName\":\"n\",\"columns\":99}";

        var config = JsonSerializer.Deserialize<GroupConfig>(json, JsonOptions.Default);

        config.Should().NotBeNull();
        config!.Columns.Should().Be(GroupConfig.MaxColumns);
    }

    // --- GroupConfig.IconSize snaps to a preset; ShowNames defaults on --------------

    [Theory]
    [InlineData(32, 32)]
    [InlineData(48, 48)]
    [InlineData(64, 64)]
    [InlineData(80, 80)]
    [InlineData(0, 32)]
    [InlineData(40, 32)]   // halfway: the smaller preset
    [InlineData(41, 48)]
    [InlineData(70, 64)]
    [InlineData(500, 80)]
    public void IconSize_SnapsToTheNearestPreset(int input, int expected)
    {
        var config = new GroupConfig { Id = "g", GroupName = "n", IconSize = input };

        config.IconSize.Should().Be(expected);
    }

    [Fact]
    public void GroupConfig_FromAFileWrittenBeforeTheGridSettings_GetsTheDefaults()
    {
        var json = "{\"id\":\"x\",\"groupName\":\"n\",\"columns\":4,\"apps\":[]}";

        var config = JsonSerializer.Deserialize<GroupConfig>(json, JsonOptions.Default);

        config.Should().NotBeNull();
        config!.IconSize.Should().Be(GroupConfig.DefaultIconSize);
        config.ShowNames.Should().BeTrue("a group that never chose otherwise keeps its names");
        config.TileSpacing.Should().Be(0, "a group saved before spacing existed keeps its tiles touching");
    }

    [Fact]
    public void GroupConfig_GridSettings_RoundTrip()
    {
        var config = new GroupConfig { Id = "g", GroupName = "n", Columns = 5, IconSize = 64, ShowNames = false, TileSpacing = 8 };

        var json = JsonSerializer.Serialize(config, JsonOptions.Default);
        var back = JsonSerializer.Deserialize<GroupConfig>(json, JsonOptions.Default);

        json.Should().Contain("\"iconSize\": 64").And.Contain("\"showNames\": false").And.Contain("\"tileSpacing\": 8");
        back!.Columns.Should().Be(5);
        back.IconSize.Should().Be(64);
        back.ShowNames.Should().BeFalse();
        back.TileSpacing.Should().Be(8);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(12, 12)]
    [InlineData(-5, 0)]
    [InlineData(2, 0)]     // halfway: the smaller step
    [InlineData(3, 4)]
    [InlineData(10, 8)]    // halfway: the smaller step
    [InlineData(11, 12)]
    [InlineData(100, 12)]
    public void TileSpacing_SnapsToTheNearestStep(int input, int expected)
    {
        var config = new GroupConfig { Id = "g", GroupName = "n", TileSpacing = input };

        config.TileSpacing.Should().Be(expected);
        GroupConfig.TileSpacings.Should().Equal(0, 4, 8, 12);
    }

    [Fact]
    public void IconSize_DeserialisedOffPresetValueIsSnapped()
    {
        var json = "{\"id\":\"x\",\"groupName\":\"n\",\"iconSize\":1000}";

        var config = JsonSerializer.Deserialize<GroupConfig>(json, JsonOptions.Default);

        config!.IconSize.Should().Be(80);
    }

    // --- AppEntry.Arguments null is omitted on write --------------------------------

    [Fact]
    public void AppEntry_NullArguments_AreOmittedFromOutputJson()
    {
        var entry = new AppEntry { Name = "x", Path = "C:/x.exe", Arguments = null };

        var json = JsonSerializer.Serialize(entry, JsonOptions.Default);

        json.Should().NotContain("\"arguments\"");
    }

    [Fact]
    public void AppEntry_NonNullArguments_ArePreservedOnRoundtrip()
    {
        var entry = new AppEntry { Name = "x", Path = "C:/x.exe", Arguments = "--flag" };

        var json = JsonSerializer.Serialize(entry, JsonOptions.Default);
        var back = JsonSerializer.Deserialize<AppEntry>(json, JsonOptions.Default);

        back.Should().NotBeNull();
        back!.Arguments.Should().Be("--flag");
    }
}
