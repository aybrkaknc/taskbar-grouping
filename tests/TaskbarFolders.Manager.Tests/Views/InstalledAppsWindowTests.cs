using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Apps;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Manager.Views;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Views;

/// <summary>
/// The installed-apps picker as WPF realises it: a row per app with its name and tick, the group's
/// own apps ticked off and marked, and Cancel closing it without a result. WPF-driven on purpose:
/// what can go wrong is a binding by name that finds nothing, which no view-model test sees. Shown
/// without activation, so it takes no focus from whoever runs the suite.
/// </summary>
public sealed class InstalledAppsWindowTests
{
    [Fact]
    public void ListsEveryApp_MarksTheGroupsOwn_AndCancelClosesWithoutAResult()
    {
        var (rows, inGroupEnabled, inGroupMarked, otherEnabled, closedByCancel, closedWith) = StaThread.Run(() =>
        {
            var provider = new Mock<IInstalledAppsProvider>();
            provider.Setup(p => p.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
            {
                new InstalledApp("Calculator", "Calc!App", null, null, "pkg"),
                new InstalledApp("Notepad", "Notepad", @"C:\Windows\System32\notepad.exe", null, null),
            });
            var cache = new Mock<IIconCache>();
            BitmapSource? none;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out none)).Returns(false);
            var vm = new InstalledAppsPickerViewModel(provider.Object, Mock.Of<IIconExtractor>(), cache.Object);
            var window = new InstalledAppsWindow(vm)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
            };
            bool? result = null;
            window.Closed += (_, _) => result = window.DialogResult;

            vm.LoadAsync([@"C:\Windows\System32\notepad.exe"]).GetAwaiter().GetResult();
            window.Show();
            StaThread.PumpUntil(() => Find<CheckBox>(window).Count() == 2, System.TimeSpan.FromSeconds(2));

            var boxes = Find<CheckBox>(window).ToList();
            var notepad = boxes.Single(b => Find<TextBlock>(b).Any(t => t.Text == "Notepad"));
            var calculator = boxes.Single(b => Find<TextBlock>(b).Any(t => t.Text == "Calculator"));
            var marked = Find<TextBlock>(notepad).Any(t => t.Text == "In group" && t.Visibility == Visibility.Visible);

            var cancel = Find<Button>(window).Single(b => (b.Content as string) == "Cancel");
            cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            StaThread.PumpUntil(() => !window.IsVisible, System.TimeSpan.FromSeconds(2));
            var closed = !window.IsVisible;
            if (window.IsVisible)
            {
                window.Close();
            }

            return (boxes.Count, notepad.IsEnabled, marked, calculator.IsEnabled, closed, result);
        });

        rows.Should().Be(2, "one row per app the provider lists");
        inGroupEnabled.Should().BeFalse("an app the group holds cannot be ticked again");
        inGroupMarked.Should().BeTrue("and says why");
        otherEnabled.Should().BeTrue();
        closedByCancel.Should().BeTrue("Cancel closes the picker");
        closedWith.Should().NotBe(true, "and adds nothing");
    }

    private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var deeper in Find<T>(child))
            {
                yield return deeper;
            }
        }
    }
}
