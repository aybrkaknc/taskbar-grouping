using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
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
        var keys = OnStaThread(() =>
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
    public void EveryStyledControl_HasAnImplicitStyleThatApplies(Type controlType)
    {
        OnStaThread(() =>
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
        var (gutter, expected) = OnStaThread(() =>
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

    private static ResourceDictionary LoadTheme(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(
            FindRepoRoot(), "src", "TaskbarFolders.Manager", "Themes", fileName));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static ResourceDictionary LoadControls()
    {
        using var stream = File.OpenRead(Path.Combine(
            FindRepoRoot(), "src", "TaskbarFolders.Manager", "Themes", "Controls.xaml"));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static T OnStaThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("STA test body failed.", failure);
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskbarFolders.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate TaskbarFolders.sln above the test assembly.");
    }
}
