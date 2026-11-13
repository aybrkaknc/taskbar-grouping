using FluentAssertions;
using TaskbarFolders.Shared.Hosting;
using Xunit;

namespace TaskbarFolders.Core.Tests.Hosting;

public sealed class HostRequestTests
{
    [Fact]
    public void ToLine_ThenTryParse_RoundTripsTheRequest()
    {
        var request = new HostRequest("group-1", 1234, -56, [@"C:\Apps\one.exe", @"C:\My Apps\two.lnk"]);

        var parsed = HostRequest.TryParse(request.ToLine(), out var result);

        parsed.Should().BeTrue();
        result.Should().NotBeNull();
        result!.GroupId.Should().Be("group-1");
        result.X.Should().Be(1234);
        result.Y.Should().Be(-56);
        result.Files.Should().Equal(@"C:\Apps\one.exe", @"C:\My Apps\two.lnk");
    }

    [Fact]
    public void ToLine_WithoutFiles_HasFourFields()
    {
        var line = new HostRequest("g", 1, 2, []).ToLine();

        line.Split('\t').Should().Equal("v1", "g", "1", "2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("v1\tg\t1")]
    [InlineData("v2\tg\t1\t2")]
    [InlineData("v1\tg\tx\t2")]
    [InlineData("v1\tg\t1\ty")]
    public void TryParse_RejectsMalformedLines(string? line)
    {
        HostRequest.TryParse(line, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a b")]
    [InlineData("")]
    public void TryParse_RejectsAGroupIdThatCouldCarryAPath(string groupId)
    {
        HostRequest.TryParse($"v1\t{groupId}\t1\t2", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_RejectsAGroupIdLongerThan96Characters()
    {
        HostRequest.TryParse($"v1\t{new string('a', 97)}\t1\t2", out _).Should().BeFalse();
        HostRequest.TryParse($"v1\t{new string('a', 96)}\t1\t2", out _).Should().BeTrue();
    }

    [Fact]
    public void TryParse_SkipsEmptyFileFields()
    {
        HostRequest.TryParse("v1\tg\t1\t2\t\t" + @"C:\a.exe", out var result).Should().BeTrue();

        result!.Files.Should().Equal(@"C:\a.exe");
    }
}
