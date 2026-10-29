using System;
using System.IO;
using FluentAssertions;
using TaskbarFolders.Manager.Services;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

/// <summary>Telling an installed copy, a portable copy and a development build apart.</summary>
public sealed class InstallLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tf-install-" + Guid.NewGuid().ToString("N"));

    public InstallLocationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void ACopyWithManagerAndLauncherFolders_IsPortable()
    {
        Touch(@"Manager\TaskbarFolders.Manager.exe");
        Touch(@"Launcher\TaskbarFolders.Launcher.exe");

        var sut = new InstallLocation(_root);

        sut.IsPortable.Should().BeTrue();
        sut.PortableManagerPath.Should().Be(Path.Combine(_root, "Manager", "TaskbarFolders.Manager.exe"));
        sut.FindUninstaller().Should().BeNull();
    }

    [Fact]
    public void ADevelopmentBuild_IsNotPortable()
    {
        Touch(@"net8.0-windows\TaskbarFolders.Manager.exe");

        new InstallLocation(_root).IsPortable.Should().BeFalse("deleting its folders would delete the build output");
    }

    [Fact]
    public void AnInstalledCopy_HasTheInstallersUninstaller_AndIsNotPortable()
    {
        Touch(@"Manager\TaskbarFolders.Manager.exe");
        Touch(@"Launcher\TaskbarFolders.Launcher.exe");
        Touch("unins000.exe");

        var sut = new InstallLocation(_root);

        sut.FindUninstaller().Should().Be(Path.Combine(_root, "unins000.exe"));
        sut.IsPortable.Should().BeFalse("Inno's uninstaller deletes the folders");
    }

    [Fact]
    public void AStrayUninstallCmd_IsNoLongerAnUninstaller()
    {
        Touch("Uninstall.cmd");

        new InstallLocation(_root).FindUninstaller().Should().BeNull();
    }
}
