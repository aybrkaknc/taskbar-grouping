using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Apps;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

/// <summary>
/// The installed-apps picker: the list as the provider gives it, a search over the names, many
/// apps picked at once, and the group's own apps shown but not offered again.
/// </summary>
public class InstalledAppsPickerViewModelTests
{
    private static readonly InstalledApp _calculator = new("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null, null, "Microsoft.WindowsCalculator_1_x64__8wekyb3d8bbwe");
    private static readonly InstalledApp _gitBash = new("Git Bash", "Git.Bash", @"C:\Program Files\Git\git-bash.exe", "--cd-to-home", null);
    private static readonly InstalledApp _notepad = new("Notepad", "Notepad.Id", @"C:\Windows\System32\notepad.exe", null, null);

    private static InstalledAppsPickerViewModel Create(out Mock<IIconExtractor> extractor, params InstalledApp[] apps)
    {
        var provider = new Mock<IInstalledAppsProvider>();
        provider.Setup(p => p.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(apps);
        extractor = new Mock<IIconExtractor>();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns(TestImages.Solid(System.Windows.Media.Colors.Teal));
        var cache = new Mock<IIconCache>();
        BitmapSource? none;
        cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out none)).Returns(false);
        return new InstalledAppsPickerViewModel(provider.Object, extractor.Object, cache.Object);
    }

    [Fact]
    public async Task LoadAsync_ShowsEveryApp_AndLoadsTheirIcons()
    {
        using var sut = Create(out var extractor, _calculator, _gitBash, _notepad);
        sut.IsLoading.Should().BeFalse();

        await sut.LoadAsync([]);
        await sut.WhenIconsLoadedAsync();

        sut.IsLoading.Should().BeFalse();
        sut.Visible.Select(i => i.Name).Should().Equal("Calculator", "Git Bash", "Notepad");
        sut.Visible.Should().OnlyContain(i => i.Icon != null && !i.IconMissing);
        extractor.Verify(e => e.ExtractIcon(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", It.IsAny<int>()), Times.Once,
            "an app is drawn from what it will be stored as");
        extractor.Verify(e => e.ExtractIcon(@"C:\Windows\System32\notepad.exe", It.IsAny<int>()), Times.Once);
    }

    [Theory]
    [InlineData("calc", new[] { "Calculator" })]
    [InlineData("  NOTE ", new[] { "Notepad" })]
    [InlineData("a", new[] { "Calculator", "Git Bash", "Notepad" })]
    [InlineData("zzz", new string[0])]
    [InlineData("", new[] { "Calculator", "Git Bash", "Notepad" })]
    public async Task Search_KeepsTheAppsWhoseNameContainsIt_IgnoringCase(string search, string[] expected)
    {
        using var sut = Create(out _, _calculator, _gitBash, _notepad);
        await sut.LoadAsync([]);

        sut.SearchText = search;

        sut.Visible.Select(i => i.Name).Should().Equal(expected);
        sut.IsEmpty.Should().Be(expected.Length == 0);
    }

    [Fact]
    public async Task ManyApps_CanBePicked_AndAreReturnedInListOrder_EvenWhenSearchHidesSome()
    {
        using var sut = Create(out _, _calculator, _gitBash, _notepad);
        await sut.LoadAsync([]);

        sut.Visible[2].IsSelected = true;
        sut.Visible[0].IsSelected = true;
        sut.SearchText = "git";

        sut.SelectedCount.Should().Be(2);
        sut.CanAdd.Should().BeTrue();
        sut.Selected.Should().Equal(_calculator, _notepad);
    }

    [Fact]
    public async Task TheGroupsOwnApps_AreShownAsInTheGroup_AndCannotBePickedAgain()
    {
        using var sut = Create(out _, _calculator, _gitBash, _notepad);

        await sut.LoadAsync([@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"c:\windows\system32\NOTEPAD.exe"]);

        sut.Visible.Where(i => i.IsInGroup).Select(i => i.Name).Should().Equal("Calculator", "Notepad");
        sut.Visible[0].IsSelected = true;
        sut.Visible[0].IsSelected.Should().BeFalse();
        sut.SelectedCount.Should().Be(0);
        sut.CanAdd.Should().BeFalse();
    }

    [Fact]
    public async Task AListThatCannotBeRead_SaysSo_InsteadOfShowingNothing()
    {
        var provider = new Mock<IInstalledAppsProvider>();
        provider.Setup(p => p.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("shell busy"));
        using var sut = new InstalledAppsPickerViewModel(provider.Object, Mock.Of<IIconExtractor>(), Mock.Of<IIconCache>());

        await sut.LoadAsync([]);

        sut.IsLoading.Should().BeFalse();
        sut.LoadError.Should().NotBeNullOrWhiteSpace();
        sut.Visible.Should().BeEmpty();
    }
}
