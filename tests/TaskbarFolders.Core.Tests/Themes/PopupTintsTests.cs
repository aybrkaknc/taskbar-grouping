using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using FluentAssertions;
using TaskbarFolders.Core.Themes;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Core.Tests.Themes;

/// <summary>
/// A popup tint is a translucent colour laid over the popup's material. Every tint must keep the
/// popup's text readable in its theme: contrast is measured on the tint over Mica's base colour,
/// the colour the materials stay close to and the opaque panel uses.
/// </summary>
public sealed class PopupTintsTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Windows' own default accent (blue), and two that sit at the ends of what a user can pick.
    private static readonly AccentShades[] _accents =
    [
        new(Color.FromRgb(0x00, 0x3E, 0x92), Color.FromRgb(0x99, 0xEB, 0xFF)),
        new(Color.FromRgb(0x7A, 0x5A, 0x00), Color.FromRgb(0xFF, 0xE9, 0x8A)),
        new(Color.FromRgb(0x6B, 0x00, 0x00), Color.FromRgb(0xFF, 0xB3, 0xB3)),
    ];

    public static TheoryData<PopupTint, bool> EveryTintInEveryTheme()
    {
        var data = new TheoryData<PopupTint, bool>();
        foreach (var tint in Enum.GetValues<PopupTint>().Where(t => t != PopupTint.None))
        {
            data.Add(tint, true);
            data.Add(tint, false);
        }

        return data;
    }

    [Fact]
    public void None_LaysNothingOver()
    {
        PopupTints.ColorFor(PopupTint.None, dark: true, PopupTints.DefaultAccent).Should().BeNull();
        PopupTints.ColorFor(PopupTint.None, dark: false, PopupTints.DefaultAccent).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(EveryTintInEveryTheme))]
    public void EveryTint_IsTranslucent_AndKeepsTheTextReadable(PopupTint tint, bool dark)
    {
        var theme = dark ? "Dark.xaml" : "Light.xaml";
        var micaBase = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

        foreach (var accent in _accents.Prepend(PopupTints.DefaultAccent))
        {
            var colour = PopupTints.ColorFor(tint, dark, accent)!.Value;
            colour.A.Should().BeInRange((byte)1, (byte)254, "a tint colours the material, it does not replace it");

            var surface = PopupTints.Over(colour, micaBase);
            Contrast(LauncherColour(theme, "ForegroundDefault"), surface).Should().BeGreaterThanOrEqualTo(7, $"{tint} in {theme}: names, WCAG AAA");
            Contrast(LauncherColour(theme, "ForegroundSubtle"), surface).Should().BeGreaterThanOrEqualTo(4.5, $"{tint} in {theme}: secondary text, WCAG AA");
        }
    }

    [Theory]
    [InlineData("Dark.xaml", true)]
    [InlineData("Light.xaml", false)]
    public void SolidPanel_IsThePopupThemesOwn(string theme, bool dark) =>
        LauncherColour(theme, "PopupSolidBackground").Should().Be(PopupTints.SolidPanel(dark),
            "the Manager's preview paints the panel from here, the popup from its theme");

    [Fact]
    public void Accent_FollowsTheShadeWindowsMadeForTheTheme()
    {
        var accent = new AccentShades(Color.FromRgb(1, 2, 3), Color.FromRgb(4, 5, 6));

        var dark = PopupTints.ColorFor(PopupTint.Accent, dark: true, accent)!.Value;
        var light = PopupTints.ColorFor(PopupTint.Accent, dark: false, accent)!.Value;

        (dark.R, dark.G, dark.B).Should().Be(((byte)1, (byte)2, (byte)3));
        (light.R, light.G, light.B).Should().Be(((byte)4, (byte)5, (byte)6));
    }

    [Fact]
    public void Over_IsTheTopColour_OnAClearBase_AndOpaque_OnAnOpaqueOne()
    {
        var tint = Color.FromArgb(0x66, 0x10, 0x20, 0x30);

        PopupTints.Over(tint, Colors.Transparent).Should().Be(tint, "on a material the tint is painted as it is");
        var onPanel = PopupTints.Over(tint, Color.FromRgb(0x20, 0x20, 0x20));
        onPanel.A.Should().Be(255);
        onPanel.R.Should().BeInRange((byte)0x10, (byte)0x20);
    }

    [Fact]
    public void AccentPalette_IsReadFromWindowsBytes_Light2AndDark2()
    {
        // HKCU\...\Explorer\Accent\AccentPalette: eight RGBA entries, lightest first.
        var bytes = new byte[32];
        bytes[4] = 0xAA; bytes[5] = 0xBB; bytes[6] = 0xCC;   // [1] Light2
        bytes[20] = 0x11; bytes[21] = 0x22; bytes[22] = 0x33; // [5] Dark2

        var shades = AccentShades.FromPalette(bytes);

        shades.Should().Be(new AccentShades(Color.FromRgb(0x11, 0x22, 0x33), Color.FromRgb(0xAA, 0xBB, 0xCC)));
        AccentShades.FromPalette(new byte[3]).Should().Be(PopupTints.DefaultAccent, "a damaged value falls back to Windows' blue");
        AccentShades.FromPalette(null).Should().Be(PopupTints.DefaultAccent);
    }

    private static Color LauncherColour(string theme, string key)
    {
        var element = XDocument.Load(Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Launcher", "Themes", theme))
            .Descendants()
            .Single(e => e.Attribute(_xaml + "Key")?.Value == key);
        return (Color)ColorConverter.ConvertFromString(element.Attribute("Color")!.Value);
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
