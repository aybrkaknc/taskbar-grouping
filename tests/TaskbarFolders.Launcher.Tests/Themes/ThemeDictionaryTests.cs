using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Themes;

/// <summary>
/// Guards the popup's Light/Dark dictionaries against drifting apart. <c>LauncherThemeApplier.Apply</c>
/// merges whichever dictionary matches the resolved theme, so a key present in one file and
/// missing from the other leaves the popup with an unresolved brush or effect in exactly one
/// theme — and nothing in the headless suite renders the popup to catch it.
/// </summary>
public sealed class ThemeDictionaryTests
{
    private static readonly XNamespace _xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LightAndDark_DefineExactlyTheSameKeys()
    {
        var light = KeysIn("Light.xaml");
        var dark = KeysIn("Dark.xaml");

        dark.Except(light).Should().BeEmpty("every key in Dark.xaml needs a Light.xaml counterpart");
        light.Except(dark).Should().BeEmpty("every key in Light.xaml needs a Dark.xaml counterpart");
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void EveryThemeDictionary_DefinesTheKeysThePopupConsumes(string themeFile)
    {
        var defined = KeysIn(themeFile);
        var referenced = DynamicResourceKeysIn(
            Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Launcher", "Views", "PopupWindow.xaml"));

        referenced.Should().NotBeEmpty("the popup is expected to resolve themed brushes");
        referenced.Except(defined).Should().BeEmpty($"{themeFile} must define every resource the popup resolves");
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void EveryThemeDictionary_DefinesTheLabelHalo(string themeFile)
    {
        // The popup is fully transparent, so the labels render onto the wallpaper. The halo
        // is what keeps them legible when the wallpaper's brightness matches the text.
        KeysIn(themeFile).Should().Contain("LabelShadow");
    }

    private static IReadOnlyCollection<string> KeysIn(string fileName) =>
        XDocument.Load(Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Launcher", "Themes", fileName))
            .Descendants()
            .Select(e => e.Attribute(_xamlNamespace + "Key")?.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyCollection<string> DynamicResourceKeysIn(string path) =>
        System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(path), @"\{DynamicResource\s+([A-Za-z0-9_]+)\s*\}")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
}
