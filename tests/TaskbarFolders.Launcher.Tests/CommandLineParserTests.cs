using FluentAssertions;
using TaskbarFolders.Launcher.Configuration;
using Xunit;

namespace TaskbarFolders.Launcher.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void TryParseGroupId_ReturnsValue_WhenArgumentPresent()
    {
        var args = new[] { "--group-id", "abc-123" };

        CommandLineParser.TryParseGroupId(args).Should().Be("abc-123");
    }

    [Fact]
    public void TryParseGroupId_IsCaseInsensitive_ForArgumentName()
    {
        var args = new[] { "--Group-Id", "value" };

        CommandLineParser.TryParseGroupId(args).Should().Be("value");
    }

    [Fact]
    public void TryParseGroupId_ReturnsNull_WhenArgumentMissing()
    {
        var args = new[] { "--other", "value" };

        CommandLineParser.TryParseGroupId(args).Should().BeNull();
    }

    [Fact]
    public void TryParseGroupId_ReturnsNull_WhenArgumentIsLast()
    {
        // No value following the flag — must not crash with IndexOutOfRange.
        var args = new[] { "--group-id" };

        CommandLineParser.TryParseGroupId(args).Should().BeNull();
    }

    [Fact]
    public void TryParseGroupId_ReturnsNull_WhenArrayEmpty()
    {
        CommandLineParser.TryParseGroupId(System.Array.Empty<string>()).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void TryParseGroupId_ReturnsNull_ForBlankValue(string blank)
    {
        var args = new[] { "--group-id", blank };

        CommandLineParser.TryParseGroupId(args).Should().BeNull();
    }

    [Fact]
    public void HasPinMode_ReturnsTrue_WhenFlagPresent()
    {
        var args = new[] { "--pin-mode", "--group-id", "abc" };

        CommandLineParser.HasPinMode(args).Should().BeTrue();
    }

    [Fact]
    public void HasPinMode_IsCaseInsensitive()
    {
        var args = new[] { "--Pin-Mode" };

        CommandLineParser.HasPinMode(args).Should().BeTrue();
    }

    [Fact]
    public void HasPinMode_ReturnsFalse_WhenFlagAbsent()
    {
        var args = new[] { "--group-id", "abc" };

        CommandLineParser.HasPinMode(args).Should().BeFalse();
    }

    [Fact]
    public void HasPinMode_ReturnsFalse_OnEmptyArgs()
    {
        CommandLineParser.HasPinMode(System.Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public void ParseFilesToAdd_ReturnsThePositionalArguments()
    {
        // What Explorer's menu verb produces: --group-id "<id>" "%1".
        var args = new[] { "--group-id", "abc", @"C:\Tools\App.lnk" };

        CommandLineParser.ParseFilesToAdd(args).Should().Equal(@"C:\Tools\App.lnk");
    }

    [Fact]
    public void ParseFilesToAdd_FindsFilesOnEitherSideOfTheFlags()
    {
        var args = new[] { @"C:\a.exe", "--group-id", "abc", "--some-flag", @"C:\b.lnk" };

        CommandLineParser.ParseFilesToAdd(args).Should().Equal(@"C:\a.exe", @"C:\b.lnk");
    }

    [Theory]
    [InlineData("")]
    [InlineData("--group-id abc")]
    [InlineData("--pin-mode --group-id abc")]
    public void ParseFilesToAdd_ReturnsNothing_ForAPlainLaunch(string commandLine)
    {
        var args = commandLine.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

        CommandLineParser.ParseFilesToAdd(args).Should().BeEmpty("the group id is not a file and flags are not files");
    }

    [Fact]
    public void ParseFilesToAdd_IgnoresBlankArguments()
    {
        CommandLineParser.ParseFilesToAdd(["--group-id", "abc", "  "]).Should().BeEmpty();
    }
}
