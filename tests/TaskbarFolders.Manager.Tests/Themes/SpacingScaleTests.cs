using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Themes;

/// <summary>
/// Every literal margin and padding in the Manager's markup sits on one spacing scale: Fluent's
/// 4 DIP steps, plus 1 and 2 for hairlines (the gap between list rows, a subtitle under its
/// heading). The values had drifted to 3, 5, 6, 10 and 14 before C6, which is what makes a
/// layout look slightly off without anyone being able to say why. Bound values are not checked.
/// </summary>
public sealed class SpacingScaleTests
{
    private static readonly int[] _scale = [0, 1, 2, 4, 8, 12, 16, 20, 24, 28, 32];

    [Fact]
    public void EveryMarginAndPadding_IsOnTheScale()
    {
        var root = Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Manager");
        var files = Directory.GetFiles(Path.Combine(root, "Views"), "*.xaml")
            .Append(Path.Combine(root, "Themes", "Controls.xaml"));

        var offScale = files
            .SelectMany(file => File.ReadAllLines(file).Select((line, i) => (file, line, number: i + 1)))
            .SelectMany(l => Values(l.line).Select(v => (l.file, l.number, v)))
            .Where(x => !_scale.Contains(x.v))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.number} uses {x.v}")
            .ToList();

        offScale.Should().BeEmpty("margins and paddings use the spacing scale {0}", string.Join(", ", _scale));
    }

    [Fact]
    public void TheCheck_FindsAValueOffTheScale()
    {
        // The gate must be able to fail: both ways a value is written are read.
        Values("<Border Margin=\"16,10,12,8\" />").Should().Contain(10);
        Values("<Setter Property=\"Padding\" Value=\"6,3\" />").Should().Contain([6, 3]);
        Values("<Border Margin=\"{Binding Gap}\" />").Should().BeEmpty("bound values are not literals");
    }

    private static IEnumerable<int> Values(string line)
    {
        var literals = Regex.Matches(line, @"\b(?:Margin|Padding)=""([^""{]+)""").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(line, @"Property=""(?:Margin|Padding)""\s+Value=""([^""{]+)""").Select(m => m.Groups[1].Value));
        return literals.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries)).Select(v => Math.Abs((int)double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
