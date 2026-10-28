using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Themes;

/// <summary>
/// Parses <c>Themes/Controls.xaml</c> and applies it to real control instances.
/// </summary>
/// <remarks>
/// A malformed control template is not a build error — XAML in a resource dictionary is
/// parsed when the style is first applied, which happens the first time the affected window
/// is shown. The Settings window is the only place the ComboBox styles are used, so a broken
/// template there would surface as "the Settings dialog does not open" at runtime, with
/// nothing failing in CI. These tests parse the dictionary and instantiate the templates so
/// that failure lands in the build instead.
///
/// WPF objects require an STA thread; xUnit runs MTA, so each test body is marshalled onto a
/// short-lived STA thread.
/// </remarks>
public sealed class ControlStyleTests
{
    [Fact]
    public void ControlsDictionary_Parses()
    {
        var keys = StaThread.Run(() =>
        {
            var dictionary = LoadControls();
            var found = new List<string>();
            foreach (var key in dictionary.Keys)
            {
                found.Add(key.ToString() ?? string.Empty);
            }
            return found;
        });

        keys.Should().NotBeEmpty("the dictionary defines styles and shared values");
    }

    [Theory]
    [InlineData(typeof(Button))]
    [InlineData(typeof(TextBox))]
    [InlineData(typeof(ListBox))]
    [InlineData(typeof(ListBoxItem))]
    [InlineData(typeof(CheckBox))]
    [InlineData(typeof(ComboBox))]
    [InlineData(typeof(ComboBoxItem))]
    [InlineData(typeof(MenuItem))]
    [InlineData(typeof(ContextMenu))]
    [InlineData(typeof(ToolTip))]
    [InlineData(typeof(ScrollBar))]
    [InlineData(typeof(TabControl))]
    [InlineData(typeof(TabItem))]
    public void EveryStyledControl_HasAnImplicitStyleThatApplies(Type controlType)
    {
        StaThread.Run(() =>
        {
            var dictionary = LoadControls();

            dictionary.Contains(controlType).Should().BeTrue(
                $"Controls.xaml is expected to carry an implicit style for {controlType.Name}");

            var style = (Style)dictionary[controlType];
            var control = (FrameworkElement)Activator.CreateInstance(controlType)!;

            // Assigning the style validates TargetType compatibility, and touching the
            // template forces the ControlTemplate's own XAML to be realised.
            var act = () =>
            {
                control.Style = style;
                if (control is Control c && c.Template is { } template)
                {
                    template.LoadContent();
                }
            };

            act.Should().NotThrow($"the {controlType.Name} style and its template must be valid XAML");
            return 0;
        });
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void ContextMenu_IsPaintedInTheThemeColour_DownItsLeftEdge(string themeFile)
    {
        // WPF's own ContextMenu template draws a light icon gutter down the left edge in fixed
        // colours; in dark mode the header's "more" menu showed it as a white strip.
        var (gutter, expected) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(theme);
            resources.MergedDictionaries.Add(LoadControls());

            var menu = new ContextMenu { Resources = resources };
            menu.Items.Add(new MenuItem { Header = "Show shortcut…" });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Delete group…" });
            menu.Measure(new Size(400, 400));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();

            var width = (int)Math.Ceiling(menu.ActualWidth);
            var height = (int)Math.Ceiling(menu.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(menu);

            // Inside the border and padding, level with the first item, where the gutter was.
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(10, 12, 1, 1), pixel, 4, 0);
            return (Color.FromRgb(pixel[2], pixel[1], pixel[0]), ((SolidColorBrush)theme["ControlBackground"]).Color);
        });

        gutter.Should().Be(expected, $"the menu's left edge must take ControlBackground from {themeFile}");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void TabControl_DrawsNoFaceOfItsOwn_AndMarksTheSelectedTab(string themeFile)
    {
        // WPF's own TabControl template paints white tab faces and a white content box in
        // fixed colours. The editor's Apps/Appearance tabs sit on the window background.
        var (contentArea, tabFace, indicatorShown, accent, underline, hasContentHost) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(theme);
            resources.MergedDictionaries.Add(LoadControls());

            var tabs = new TabControl { Resources = resources, Width = 300, Height = 120 };
            tabs.Items.Add(new TabItem { Header = "Apps", Content = new Grid() });
            tabs.Items.Add(new TabItem { Header = "Appearance", Content = new Grid() });
            tabs.SelectedIndex = 0;
            tabs.Measure(new Size(300, 120));
            tabs.Arrange(new Rect(0, 0, 300, 120));
            tabs.UpdateLayout();

            var bitmap = new RenderTargetBitmap(300, 120, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(tabs);
            byte[] Pixel(int x, int y)
            {
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                return pixel;
            }

            var selected = (TabItem)tabs.Items[0];
            var indicator = (FrameworkElement)selected.Template.FindName("Indicator", selected);
            var accentColor = ((SolidColorBrush)theme["AccentBrush"]).Color;

            // Somewhere in the tab strip, a fully opaque pixel in the accent colour: the bar.
            var drawnInAccent = false;
            for (var y = 0; y < 50 && !drawnInAccent; y++)
            {
                for (var x = 0; x < 150 && !drawnInAccent; x++)
                {
                    var p = Pixel(x, y);
                    drawnInAccent = p[3] == 255 && Color.FromRgb(p[2], p[1], p[0]) == accentColor;
                }
            }

            return (
                Pixel(150, 100)[3],
                Pixel(2, 2)[3],
                // IsVisible needs a window; this control is rendered on its own.
                indicator.Visibility == Visibility.Visible,
                accentColor,
                drawnInAccent ? accentColor : Colors.Transparent,
                tabs.Template.FindName("PART_SelectedContentHost", tabs) is ContentPresenter);
        });

        contentArea.Should().Be(0, "the content area shows the window behind it");
        hasContentHost.Should().BeTrue("UI Automation reaches a tab's content only through PART_SelectedContentHost");
        tabFace.Should().Be(0, "tabs are labels, not raised faces");
        indicatorShown.Should().BeTrue("the selected tab is marked");
        underline.Should().Be(accent, $"the mark takes AccentBrush from {themeFile}");
    }

    [Fact]
    public void TabItem_KeepsItsHeaderTypeToItself_AndShowsTheWholeLabel()
    {
        // Set on the TabItem, the header's size and weight were inherited by everything on the
        // tab; a Margin on it was taken out of the label's width by TabPanel ("Ap" for "Apps").
        var (contentWeight, contentSize, defaultSize, clipped) = StaThread.Run(() =>
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(LoadTheme("Dark.xaml"));
            resources.MergedDictionaries.Add(LoadControls());

            var body = new TextBlock { Text = "Group icon" };
            var tabs = new TabControl { Resources = resources };
            tabs.Items.Add(new TabItem { Header = "Apps", Content = body });
            tabs.Items.Add(new TabItem { Header = "Appearance", Content = new Grid() });
            tabs.SelectedIndex = 0;
            tabs.Measure(new Size(600, 200));
            tabs.Arrange(new Rect(0, 0, 600, 200));
            tabs.UpdateLayout();

            var anyClipped = false;
            foreach (TabItem tab in tabs.Items)
            {
                var header = (FrameworkElement)tab.Template.FindName("Header", tab);
                anyClipped |= header.ActualWidth + 0.5 < header.DesiredSize.Width - header.Margin.Left - header.Margin.Right;
            }

            return (body.FontWeight, body.FontSize, new TextBlock().FontSize, anyClipped);
        });

        contentWeight.Should().Be(FontWeights.Normal, "the selected tab's bold header must not reach its content");
        contentSize.Should().Be(defaultSize, "the header's font size must not reach the content");
        clipped.Should().BeFalse("every tab label is shown in full");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void SegmentedList_LaysItsChoicesOutInARow_AndFillsTheChosenOneWithTheAccent(string themeFile)
    {
        // The grid settings on the Appearance tab: a row of choices, one of them filled.
        var (sameRow, inOrder, chosenFill, chosenText, otherFill, accent, accentText) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(theme);
            resources.MergedDictionaries.Add(LoadControls());

            var list = new ListBox { Resources = resources, ItemsSource = new[] { "Small", "Medium", "Large" } };
            list.Style = (Style)resources["SegmentedList"];
            list.SelectedIndex = 1;
            list.Measure(new Size(400, 60));
            list.Arrange(new Rect(0, 0, 400, 60));
            list.UpdateLayout();

            var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var chosen = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            var firstAt = first.TranslatePoint(default, list);
            var chosenAt = chosen.TranslatePoint(default, list);
            Color Fill(ListBoxItem item) =>
                ((Border)item.Template.FindName("Root", item)).Background is SolidColorBrush b ? b.Color : Colors.Transparent;

            return (
                firstAt.Y == chosenAt.Y,
                chosenAt.X > firstAt.X,
                Fill(chosen),
                ((SolidColorBrush)chosen.Foreground).Color,
                Fill(first),
                ((SolidColorBrush)theme["AccentBrush"]).Color,
                ((SolidColorBrush)theme["AccentForeground"]).Color);
        });

        sameRow.Should().BeTrue("the choices sit side by side");
        inOrder.Should().BeTrue();
        chosenFill.Should().Be(accent, $"the chosen value takes AccentBrush from {themeFile}");
        chosenText.Should().Be(accentText, "its label must stay readable on the accent");
        otherFill.A.Should().Be(0, "the other choices are not filled");
    }

    private static ResourceDictionary LoadTheme(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(
            RepoRoot.Path, "src", "TaskbarFolders.Manager", "Themes", fileName));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static ResourceDictionary LoadControls()
    {
        using var stream = File.OpenRead(Path.Combine(
            RepoRoot.Path, "src", "TaskbarFolders.Manager", "Themes", "Controls.xaml"));
        return (ResourceDictionary)XamlReader.Load(stream);
    }
}
