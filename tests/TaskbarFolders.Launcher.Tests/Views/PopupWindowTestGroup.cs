using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// xUnit collection for every test class that constructs a real <c>PopupWindow</c>, so those
/// classes run one after another instead of in parallel.
/// </summary>
/// <remarks>
/// On Windows 11 22H2+ the popup attaches a <c>WindowChrome</c> in its constructor, and
/// WPF's <c>WindowChromeWorker</c> registers a property-changed handler through
/// <c>DependencyPropertyDescriptor.AddValueChanged</c>, which writes to a process-wide
/// dictionary without locking. The launcher itself has a single UI thread, so it never
/// races; the test host does, because each test builds its window on its own STA thread and
/// xUnit runs test classes in parallel. Two windows constructed at the same moment corrupted
/// that dictionary and failed whichever test came first, at random.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PopupWindowTestGroup
{
    /// <summary>Collection name shared by the popup window test classes.</summary>
    public const string Name = "Popup windows";
}
