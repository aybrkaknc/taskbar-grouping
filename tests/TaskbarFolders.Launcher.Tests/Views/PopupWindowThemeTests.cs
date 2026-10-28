using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// Checks the colours the popup actually paints with, against the real theme dictionaries.
/// </summary>
/// <remarks>
/// The app tiles are <see cref="Button"/>s, and WPF's built-in Button theme style sets
/// <see cref="Control.Foreground"/> to the system control-text colour (black). A theme-style
/// setter outranks property inheritance, so the theme's <c>ForegroundDefault</c> set on the
/// popup root never reached the labels inside the tiles: they rendered black in the dark
/// theme as well, over a black halo. The same precedence rule hid Manager text until v0.4.7.
/// Only the realised visual tree shows the effective value, so these tests build the real
/// popup, as <see cref="PopupWindowSizingTests"/> does, on an STA thread.
/// </remarks>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupWindowThemeTests
{
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void PlaceholderIcon_ShowsOnlyForAnAppWithoutAnIcon_InTheSubtleThemeForeground(string themeFile)
    {
        var (visible, hidden, stroke, expected) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var window = CreatePopup(theme, firstIconMissing: true);
            try
            {
                var placeholders = FindDescendants<System.Windows.Shapes.Path>(window)
                    .Where(p => ReferenceEquals(p.Data, PlaceholderIcon.Shape))
                    .ToList();
                return (
                    placeholders.Count(p => p.IsVisible),
                    placeholders.Count(p => !p.IsVisible),
                    ((SolidColorBrush)placeholders.First(p => p.IsVisible).Stroke).Color,
                    ((SolidColorBrush)theme["ForegroundSubtle"]).Color);
            }
            finally
            {
                window.Close();
            }
        });

        visible.Should().Be(1, "only the app marked as having no icon shows the placeholder");
        hidden.Should().Be(1, "a tile still loading its icon shows nothing yet");
        stroke.Should().Be(expected, $"the placeholder must take ForegroundSubtle from {themeFile}");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void TileLabels_UseTheThemeForeground(string themeFile)
    {
        var (label, expected) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var window = CreatePopup(theme);
            try
            {
                var text = FindDescendants<TextBlock>(window).First(t => t.Text == "App 1");
                return (((SolidColorBrush)text.Foreground).Color, ((SolidColorBrush)theme["ForegroundDefault"]).Color);
            }
            finally
            {
                window.Close();
            }
        });

        label.Should().Be(expected, $"the tile labels must take ForegroundDefault from {themeFile}");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Header_ShowsTheGroupName_AndTheCloseButtonUsesTheThemeForeground(string themeFile)
    {
        var (name, closeColor, expected) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var window = CreatePopup(theme);
            try
            {
                var header = (TextBlock)window.FindName("GroupNameText");
                var close = (Button)window.FindName("CloseButton");
                return (header.Text, ((SolidColorBrush)close.Foreground).Color, ((SolidColorBrush)theme["ForegroundDefault"]).Color);
            }
            finally
            {
                window.Close();
            }
        });

        name.Should().Be("Tools");
        closeColor.Should().Be(expected, "the close glyph is a Button too, so the same theme-style precedence applies");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void RemoveBadges_AppearOnlyInEditMode_InTheThemeForeground(string themeFile)
    {
        var (hiddenBefore, visibleInEdit, hiddenAfter, badgeColor, editColor, expected) = StaThread.Run(() =>
        {
            var theme = LoadTheme(themeFile);
            var window = CreatePopup(theme);
            try
            {
                var viewModel = (PopupViewModel)window.DataContext;
                var badges = FindDescendants<Button>(window).Where(b => b.Name == "RemoveBadge").ToList();
                var before = badges.All(b => b.Visibility == Visibility.Collapsed);

                viewModel.ToggleEditCommand.Execute(null);
                window.UpdateLayout();
                var during = badges.Count == 2 && badges.All(b => b.Visibility == Visibility.Visible);

                viewModel.ToggleEditCommand.Execute(null);
                var after = badges.All(b => b.Visibility == Visibility.Collapsed);

                var edit = (Button)window.FindName("EditButton");
                return (before, during, after,
                    ((SolidColorBrush)badges[0].Foreground).Color,
                    ((SolidColorBrush)edit.Foreground).Color,
                    ((SolidColorBrush)theme["ForegroundDefault"]).Color);
            }
            finally
            {
                window.Close();
            }
        });

        hiddenBefore.Should().BeTrue("tiles are for launching until edit mode is switched on");
        visibleInEdit.Should().BeTrue("every tile gets a remove badge in edit mode");
        hiddenAfter.Should().BeTrue();
        badgeColor.Should().Be(expected, "the badge is a Button, so it must set the theme foreground itself");
        editColor.Should().Be(expected);
    }

    private static ResourceDictionary LoadTheme(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(
            RepoRoot.Path, "src", "TaskbarFolders.Launcher", "Themes", fileName));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static PopupWindow CreatePopup(ResourceDictionary theme, bool firstIconMissing = false) =>
        TestPopup.Show(appCount: 2, theme: theme, beforeShow: vm => vm.Apps[0].IconMissing = firstIconMissing).Window;

    private static System.Collections.Generic.IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
