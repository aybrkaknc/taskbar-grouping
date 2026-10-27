using System;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Default <see cref="IGroupIconReader"/>: decodes the newest generated <c>.ico</c> of the
/// group, so the list shows exactly what sync gave the pinned tile — the composite or the
/// chosen image.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GroupIconReader : IGroupIconReader
{
    /// <summary>Frame size read from the .ico; the list draws it at 24 DIP, sharp up to 200 %.</summary>
    public const int Size = 48;

    private readonly IAppDataPathProvider _paths;
    private readonly IIconExtractor _extractor;

    /// <summary>Initializes a new instance.</summary>
    public GroupIconReader(IAppDataPathProvider paths, IIconExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(extractor);

        _paths = paths;
        _extractor = extractor;
    }

    /// <inheritdoc/>
    public BitmapSource? TryRead(string groupId)
    {
        var file = GroupIconFiles.FindCurrent(_paths, groupId);
        return file is null ? null : _extractor.ExtractIcon(file, Size);
    }
}
