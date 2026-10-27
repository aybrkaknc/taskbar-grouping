using FluentAssertions;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

public class GroupListItemViewModelTests
{
    [Fact]
    public void Constructor_ProjectsConfigFields()
    {
        var config = new GroupConfig
        {
            Id = "id-1",
            GroupName = "Tools",
            Apps =
            {
                new AppEntry { Name = "a", Path = "a.exe" },
                new AppEntry { Name = "b", Path = "b.exe" },
            },
        };

        var sut = new GroupListItemViewModel(config);

        sut.Id.Should().Be("id-1");
        sut.Name.Should().Be("Tools");
        sut.AppCount.Should().Be(2);
    }

    [Fact]
    public void Name_AssignmentPropagatesToWrappedConfig()
    {
        var config = new GroupConfig { Id = "x", GroupName = "Old" };
        var sut = new GroupListItemViewModel(config);

        sut.Name = "New";

        config.GroupName.Should().Be("New", "the wrapper writes through to its config so saves see the new name");
    }

    [Theory]
    [InlineData(0, "0 apps")]
    [InlineData(1, "1 app")]
    [InlineData(2, "2 apps")]
    public void AppCountText_SpellsTheCountOut(int count, string expected)
    {
        var config = new GroupConfig { Id = "x", GroupName = "g" };
        for (var i = 0; i < count; i++)
        {
            config.Apps.Add(new AppEntry { Name = $"a{i}", Path = $"a{i}.exe" });
        }

        new GroupListItemViewModel(config).AppCountText.Should().Be(expected);
    }

    [Fact]
    public void NotifyAppCountChanged_RefreshesTheCountText()
    {
        var config = new GroupConfig { Id = "x", GroupName = "g" };
        var sut = new GroupListItemViewModel(config);
        var changed = new System.Collections.Generic.List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        config.Apps.Add(new AppEntry { Name = "a", Path = "a.exe" });
        sut.NotifyAppCountChanged();

        changed.Should().Contain([nameof(GroupListItemViewModel.AppCount), nameof(GroupListItemViewModel.AppCountText)]);
        sut.AppCountText.Should().Be("1 app");
    }
}
