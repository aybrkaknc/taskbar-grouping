using System;
using FluentAssertions;
using TaskbarFolders.Core.Interop;
using Xunit;

namespace TaskbarFolders.Core.Tests.Interop;

public class WindowBackdropTests
{
    [Fact]
    public void TryApply_ReturnsFalse_ForZeroHandle()
    {
        WindowBackdrop.TryApply(IntPtr.Zero, WindowBackdropKind.Mica)
            .Should().BeFalse("zero handle is the documented sentinel for 'no window yet'");
    }

    [Theory]
    [InlineData(WindowBackdropKind.Auto)]
    [InlineData(WindowBackdropKind.None)]
    [InlineData(WindowBackdropKind.Mica)]
    [InlineData(WindowBackdropKind.Acrylic)]
    [InlineData(WindowBackdropKind.MicaAlt)]
    public void TryApply_NeverThrows_ForAnyKindWithZeroHandle(WindowBackdropKind kind)
    {
        var act = () => WindowBackdrop.TryApply(IntPtr.Zero, kind);
        act.Should().NotThrow();
    }

    [Fact]
    public void TrySetRoundedCorners_ReturnsFalse_ForZeroHandle()
    {
        WindowBackdrop.TrySetRoundedCorners(IntPtr.Zero).Should().BeFalse();
    }

    [Fact]
    public void IsSupported_MatchesTheWindows11_22H2Build()
    {
        WindowBackdrop.IsSupported.Should().Be(Environment.OSVersion.Version.Build >= 22621);
    }
}
