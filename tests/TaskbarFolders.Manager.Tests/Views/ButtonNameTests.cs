using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Views;

/// <summary>
/// Every button in the Manager's and the popup's windows has a name UI Automation can read.
/// </summary>
/// <remarks>
/// A button whose content is an icon, or an icon and a label in a panel, has no name of its own:
/// WPF names a button only from string content or <c>AutomationProperties.Name</c>, and a tooltip
/// is not a name. Screen readers announced eight such buttons as just "button" — Settings, Pin to
/// taskbar and every popup tile among them — until the release check for v0.5.0 found it.
/// Read from the XAML rather than from a live window so every template's buttons count, including
/// the ones that only exist per row or per tile.
/// </remarks>
public class ButtonNameTests
{
    private static readonly XNamespace _wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EveryButton_HasTextContentOrAnAutomationName()
    {
        var src = Path.Combine(RepoRoot.Path, "src");
        var views = Directory.GetFiles(src, "*.xaml", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var nameless = views
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Where(e => e.Name == _wpf + "Button" || e.Name == _wpf + "ToggleButton")
                .Where(e => e.Attribute("Content") is null
                            && !e.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.Name"))
                .Select(e => $"{Path.GetFileName(file)}: {(string?)e.Attribute(_xaml + "Name") ?? (string?)e.Attribute("Style") ?? "(unnamed)"}"))
            .ToList();

        views.Should().NotBeEmpty("the test must actually read the windows");
        nameless.Should().BeEmpty("a button with an icon or a panel as its content needs AutomationProperties.Name");
    }

    [Fact]
    public void NoManagerButton_DrawsItsIconAsAPlainCharacter()
    {
        // Icons are Segoe Fluent glyphs beside the label, as on "Add apps…"; a "+" typed into the
        // label is a different weight and size from every other icon in the window. The name UI
        // Automation reads must then not carry the character either.
        var views = Directory.GetFiles(Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Manager", "Views"), "*.xaml");
        char[] symbols = ['+', '✓', '✕', '×', '⚙', '➕'];

        var typed = views
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Where(e => e.Name == _wpf + "Button" || e.Name == _wpf + "ToggleButton")
                .SelectMany(e => new[] { (string?)e.Attribute("Content"), e.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.Name")?.Value })
                .Where(text => !string.IsNullOrWhiteSpace(text) && symbols.Contains(text.TrimStart()[0]))
                .Select(text => $"{Path.GetFileName(file)}: \"{text}\""))
            .ToList();

        typed.Should().BeEmpty("an icon is a Fluent glyph next to the label, not a character in it");
    }
}
