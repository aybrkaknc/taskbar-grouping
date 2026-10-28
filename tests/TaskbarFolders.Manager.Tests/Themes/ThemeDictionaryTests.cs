using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Themes;

/// <summary>
/// Guards the Light/Dark resource dictionaries against drifting apart. A key defined in one
/// theme and forgotten in the other resolves to nothing after a swap, and the affected
/// control silently falls back to the WPF default — which in dark mode means black text on
/// a dark surface, the exact class of defect v0.4.7 fixed.
/// </summary>
/// <remarks>
/// The dictionaries are read from disk as XML rather than loaded as WPF resources: the test
/// suite is headless and never creates an <c>Application</c>, so pack URIs are not resolvable.
/// Key parity is a structural property of the markup, so XML is the right level to assert it.
/// </remarks>
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
    public void EveryThemeDictionary_DefinesTheKeysControlsXamlConsumes(string themeFile)
    {
        // Controls.xaml is merged for both themes, so every brush it references dynamically
        // has to exist in whichever dictionary is active.
        var defined = KeysIn(themeFile);
        var referenced = DynamicResourceKeysIn("Controls.xaml");

        referenced.Should().NotBeEmpty("the control styles are expected to reference theme brushes");
        referenced.Except(defined)
            .Except(KeysIn("Controls.xaml")) // keys Controls.xaml defines for itself
            .Should().BeEmpty($"{themeFile} must define every brush the control styles resolve");
    }

    [Fact]
    public void ThemeDictionaries_DefineNoDuplicateKeys()
    {
        foreach (var file in new[] { "Light.xaml", "Dark.xaml", "Controls.xaml" })
        {
            var keys = RawKeysIn(file);
            keys.Should().OnlyHaveUniqueItems($"{file} must not define a key twice");
        }
    }

    private static IReadOnlyCollection<string> KeysIn(string fileName) =>
        RawKeysIn(fileName).ToHashSet(StringComparer.Ordinal);

    private static List<string> RawKeysIn(string fileName) =>
        XDocument.Load(ThemePath(fileName))
            .Descendants()
            .Select(e => e.Attribute(_xamlNamespace + "Key")?.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();

    private static IReadOnlyCollection<string> DynamicResourceKeysIn(string fileName)
    {
        var markup = File.ReadAllText(ThemePath(fileName));
        return System.Text.RegularExpressions.Regex
            .Matches(markup, @"\{DynamicResource\s+([A-Za-z0-9_]+)\s*\}")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ThemePath(string fileName) =>
        Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Manager", "Themes", fileName);
}
