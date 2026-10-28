using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// The images a user chose as icons: one per group, used by sync in place of the composite of
/// the group's app icons, and any number per group for individual apps
/// (<see cref="Shared.Models.AppEntry.IconPath"/>).
/// </summary>
public interface ICustomGroupIconStore
{
    /// <summary>Whether the group has a chosen icon.</summary>
    /// <param name="groupId">Group identifier.</param>
    bool Exists(string groupId);

    /// <summary>
    /// Reads the group's chosen icon as a frozen bitmap, or returns <see langword="null"/> when
    /// it has none or the stored file cannot be read.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    BitmapSource? TryLoad(string groupId);

    /// <summary>
    /// Stores a copy of <paramref name="sourcePath"/> — any image WPF can decode, an
    /// <c>.ico</c> contributing its largest frame — fitted into a square, as the group's icon.
    /// The source file itself is not referenced afterwards.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    /// <param name="sourcePath">Image chosen by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when the file could not be read as an image; nothing is stored then.</returns>
    Task<bool> TryImportAsync(string groupId, string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Removes the group's chosen icon, if any, so sync composes one again.</summary>
    /// <param name="groupId">Group identifier.</param>
    void Delete(string groupId);

    /// <summary>
    /// Stores a copy of <paramref name="sourcePath"/>, fitted into a square, as the icon of an
    /// app in the group, and returns where — the value for <c>AppEntry.IconPath</c>.
    /// </summary>
    /// <param name="groupId">Group the app belongs to.</param>
    /// <param name="sourcePath">Image chosen by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored file, or <see langword="null"/> when the source is not a readable image.</returns>
    Task<string?> TryImportAppIconAsync(string groupId, string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the group's stored app icons that none of <paramref name="inUse"/> refers to —
    /// after an app's icon was replaced or reset, an app removed, or (with nothing in use) the
    /// group deleted.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    /// <param name="inUse">The <c>IconPath</c> values the group's apps still carry.</param>
    void PruneAppIcons(string groupId, IEnumerable<string?> inUse);
}
