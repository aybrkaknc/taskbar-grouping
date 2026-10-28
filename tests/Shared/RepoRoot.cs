using System.IO;

namespace TaskbarFolders.TestSupport;

/// <summary>The repository root, for tests that read XAML or assets from the source tree.</summary>
internal static class RepoRoot
{
    /// <summary>The folder holding <c>TaskbarFolders.sln</c>, found above the test assembly.</summary>
    public static string Path { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "TaskbarFolders.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate TaskbarFolders.sln above the test assembly.");
    }
}
