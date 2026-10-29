using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Themes;

/// <summary>
/// The popup's tile look lives once, in Core, and is drawn by the popup and by the Manager's
/// preview. Its colours are dynamic resources, so each app's themes have to define them: a key
/// missing from one theme draws nothing, silently, in exactly that theme.
/// </summary>
public sealed partial class PopupTileResourcesTests
{
    private static readonly string _sharedTiles =
        Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Core", "Themes", "PopupTile.xaml");

    [GeneratedRegex(@"\{DynamicResource\s+(\w+)\}")]
    private static partial Regex DynamicResource();

    [Theory]
    [InlineData("TaskbarFolders.Manager", "Dark.xaml")]
    [InlineData("TaskbarFolders.Manager", "Light.xaml")]
    [InlineData("TaskbarFolders.Launcher", "Dark.xaml")]
    [InlineData("TaskbarFolders.Launcher", "Light.xaml")]
    public void EveryColourTheSharedTileUses_IsDefinedByTheTheme(string project, string theme)
    {
        var used = DynamicResource().Matches(File.ReadAllText(_sharedTiles)).Select(m => m.Groups[1].Value).Distinct().ToList();
        var themeText = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", project, "Themes", theme));

        used.Should().NotBeEmpty("the tile takes its colours from the theme");
        used.Should().OnlyContain(key => themeText.Contains($"x:Key=\"{key}\""), $"{project}/{theme} must define every brush the tile draws with");
    }
}
