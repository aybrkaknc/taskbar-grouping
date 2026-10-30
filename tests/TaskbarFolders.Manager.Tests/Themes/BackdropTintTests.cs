using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Themes;

/// <summary>
/// Where the Manager window shows Mica, the window is transparent and the sidebar and the content
/// area are painted with translucent tints instead of the opaque surface brushes, so the material
/// shows through — the sidebar straight on Mica, the content on a light layer, as Windows' own
/// apps lay out a navigation pane. The
/// tints must stay translucent (an opaque one hides Mica again, which is how it went unseen until
/// C6), and text must stay readable on them: contrast is measured on the tint laid over Mica's
/// own base colour, which the material keeps close to by design.
/// </summary>
public sealed class BackdropTintTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<string, string, string> Tints => new()
    {
        { "Dark.xaml", "ContentBackdropTint", "#202020" },
        { "Dark.xaml", "SidebarBackdropTint", "#202020" },
        { "Light.xaml", "ContentBackdropTint", "#F3F3F3" },
        { "Light.xaml", "SidebarBackdropTint", "#F3F3F3" },
    };

    [Theory]
    [MemberData(nameof(Tints))]
    public void Tint_IsTranslucent_SoMicaShowsThrough(string theme, string key, string micaBase)
    {
        _ = micaBase;
        ColorOf(theme, key).A.Should().BeLessThan(255, $"{key} in {theme} must let the backdrop through");
    }

    [Theory]
    [MemberData(nameof(Tints))]
    public void Text_IsReadable_OnTheTintOverMica(string theme, string key, string micaBase)
    {
        var surface = Over(ColorOf(theme, key), (Color)ColorConverter.ConvertFromString(micaBase));

        Contrast(ColorOf(theme, "ForegroundDefault"), surface).Should().BeGreaterThanOrEqualTo(7, "body text, WCAG AAA");
        Contrast(ColorOf(theme, "ForegroundSubtle"), surface).Should().BeGreaterThanOrEqualTo(4.5, "secondary text, WCAG AA");
    }

    private static Color ColorOf(string theme, string key)
    {
        var element = XDocument.Load(Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Manager", "Themes", theme))
            .Descendants()
            .Single(e => e.Attribute(_xaml + "Key")?.Value == key);
        return (Color)ColorConverter.ConvertFromString(element.Attribute("Color")!.Value);
    }

    private static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }
}
