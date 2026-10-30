using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TaskbarFolders.Core.Apps;
using Xunit;

namespace TaskbarFolders.Core.Tests.Apps;

/// <summary>
/// Reads the real <c>shell:AppsFolder</c>: the COM declarations are positional, so only a real
/// call shows a wrong one. Every Windows has the folder and some apps in it; Calculator is checked
/// where it is installed.
/// </summary>
public class ShellInstalledAppsProviderTests
{
    private const string CalculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    [Fact]
    public async Task ListAsync_ReadsTheInstalledApps_ListedOnly_SortedByName()
    {
        var apps = await new ShellInstalledAppsProvider().ListAsync();

        apps.Should().NotBeEmpty("every Windows lists some apps");
        apps.Should().OnlyContain(a => a.IsListed && !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Aumid));
        apps.Select(a => a.Name).Should().BeInAscendingOrder(System.StringComparer.CurrentCultureIgnoreCase);
        apps.Select(a => a.Aumid).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ListAsync_ReadsAPackagedApp_WithItsPackage_AndNoTarget()
    {
        var apps = await new ShellInstalledAppsProvider().ListAsync();
        var calculator = apps.FirstOrDefault(a => a.Aumid == CalculatorAumid);
        if (calculator is null)
        {
            return; // Not installed on this machine (Windows Server images).
        }

        calculator.Target.Should().BeNull();
        calculator.PackageFullName.Should().StartWith("Microsoft.WindowsCalculator_");
    }

    [Fact]
    public async Task ListAsync_ReadsAProgramsTarget()
    {
        var apps = await new ShellInstalledAppsProvider().ListAsync();

        apps.Should().Contain(a => a.Target != null && a.Target.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase),
            "Windows' own programs are in the list with their targets");
    }
}
