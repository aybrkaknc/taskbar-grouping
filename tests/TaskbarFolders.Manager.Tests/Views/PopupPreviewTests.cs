using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Manager.Views;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Views;

/// <summary>
/// The preview on Appearance draws the popup's own tile (Core's PopupTile.xaml) with the
/// Manager's theme. WPF-driven on purpose: what can go wrong is a binding by name that finds
/// nothing, or a theme colour that is missing, and neither shows up in a view-model test.
/// </summary>
public sealed class PopupPreviewTests
{
    private static ResourceDictionary LoadTheme(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Manager", "Themes", fileName));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static T? Find<T>(DependencyObject root, System.Func<T, bool> match)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit && match(hit))
            {
                return hit;
            }

            if (Find(child, match) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    [Theory]
    [InlineData("Dark.xaml", 48, true, 0)]
    [InlineData("Light.xaml", 80, true, 12)]
    [InlineData("Dark.xaml", 32, false, 8)]
    public void DrawsThePopupsTiles_AtTheirSize_InTheThemesColours(string themeFile, int iconSize, bool showNames, int spacing)
    {
        var tile = TileMetrics.Compute(iconSize, showNames, spacing);
        var drawn = StaThread.Run<(double TileWidth, double TileHeight, double Gap, Color? Label, bool LabelShown, Color Expected)>(() =>
        {
            var theme = LoadTheme(themeFile);
            var preview = new PopupPreviewViewModel();
            preview.Update(
                Enumerable.Range(1, 3).Select(i => new AppEntryViewModel(new AppEntry { Name = $"App {i}", Path = $"a{i}.exe" })).ToList(),
                columns: 3, iconSize, showNames, spacing);
            var view = new PopupPreview { DataContext = preview };
            view.Resources.MergedDictionaries.Add(theme);
            view.Measure(new Size(1000, 1000));
            view.Arrange(new Rect(view.DesiredSize));
            view.UpdateLayout();

            var buttons = new[] { "App 1", "App 2" }
                .Select(n => Find<Button>(view, b => AutomationNameOf(b) == n)!)
                .ToArray();
            var roots = buttons.Select(b => (FrameworkElement)b.Parent).ToArray();
            var label = Find<TextBlock>(view, t => t.Text == "App 1")!;
            var x0 = roots[0].TranslatePoint(new Point(0, 0), view).X;
            var x1 = roots[1].TranslatePoint(new Point(0, 0), view).X;
            return (roots[0].ActualWidth, roots[0].ActualHeight, x1 - x0 - roots[0].ActualWidth,
                // Visibility, not IsVisible: the view is not in a window, and IsVisible is false
                // for anything outside one.
                (label.Foreground as SolidColorBrush)?.Color, label.Visibility == Visibility.Visible, ((SolidColorBrush)theme["ForegroundDefault"]).Color);
        });

        drawn.TileWidth.Should().Be(tile.Width);
        drawn.TileHeight.Should().Be(tile.Height);
        drawn.Gap.Should().BeApproximately(spacing, 0.01);
        drawn.LabelShown.Should().Be(showNames);
        drawn.Label.Should().Be(drawn.Expected, "the tile takes its text colour from the theme, as in the popup");
    }

    private static string AutomationNameOf(DependencyObject element) =>
        System.Windows.Automation.AutomationProperties.GetName(element);
}
