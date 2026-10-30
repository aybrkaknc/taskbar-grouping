using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using TaskbarFolders.Core.Interop;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// The popup's backdrop is the group's own choice if it made one, else the default in Settings.
/// Mica, Mica Alt and Acrylic are DWM materials; Off paints an opaque theme panel and asks DWM
/// for none. WPF-driven in part on purpose: what is checked is the panel the window realises.
/// </summary>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupBackdropTests
{
    [Theory]
    [InlineData(null, PopupBackdrop.Mica, PopupBackdrop.Mica)]
    [InlineData(null, PopupBackdrop.Off, PopupBackdrop.Off)]
    [InlineData(PopupBackdrop.Acrylic, PopupBackdrop.Mica, PopupBackdrop.Acrylic)]
    [InlineData(PopupBackdrop.Off, PopupBackdrop.MicaAlt, PopupBackdrop.Off)]
    public void Resolve_PrefersTheGroupsChoice_ThenTheDefault(PopupBackdrop? group, PopupBackdrop settings, PopupBackdrop expected) =>
        PopupBackdropChoice.Resolve(group, settings).Should().Be(expected);

    [Theory]
    [InlineData(PopupBackdrop.Mica, WindowBackdropKind.Mica)]
    [InlineData(PopupBackdrop.MicaAlt, WindowBackdropKind.MicaAlt)]
    [InlineData(PopupBackdrop.Acrylic, WindowBackdropKind.Acrylic)]
    [InlineData(PopupBackdrop.Off, WindowBackdropKind.None)]
    public void KindFor_MapsEveryChoiceToADwmMaterial(PopupBackdrop choice, WindowBackdropKind kind) =>
        PopupBackdropChoice.KindFor(choice).Should().Be(kind);

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Off_PaintsTheOpaqueThemePanel(string themeFile)
    {
        StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var (window, _) = TestPopup.Show(theme: theme, groupBackdrop: PopupBackdrop.Off);
            try
            {
                var chrome = (Border)window.FindName("ChromeRoot");
                var panel = chrome.Background.Should().BeOfType<SolidColorBrush>().Subject;

                panel.Color.Should().Be(((SolidColorBrush)theme["PopupSolidBackground"]).Color);
                panel.Color.A.Should().Be(255, "Off means nothing shows through");
            }
            finally
            {
                TestPopup.Close(window);
            }

            return 0;
        });
    }

    [Fact]
    public void AMaterial_LeavesThePanelClear_WhereWindowsDrawsIt()
    {
        if (!WindowBackdrop.IsSupported)
        {
            return; // Older Windows paint the fallback panel for every choice.
        }

        StaThread.Run(() =>
        {
            var (window, _) = TestPopup.Show(theme: LoadTheme("Dark.xaml"), settingsBackdrop: PopupBackdrop.MicaAlt);
            try
            {
                var chrome = (Border)window.FindName("ChromeRoot");
                (chrome.Background is null || (chrome.Background is SolidColorBrush b && b.Color.A == 0))
                    .Should().BeTrue("DWM draws the material behind a clear panel");
            }
            finally
            {
                TestPopup.Close(window);
            }

            return 0;
        });
    }

    private static ResourceDictionary LoadTheme(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(
            RepoRoot.Path, "src", "TaskbarFolders.Launcher", "Themes", fileName));
        return (ResourceDictionary)XamlReader.Load(stream);
    }
}
