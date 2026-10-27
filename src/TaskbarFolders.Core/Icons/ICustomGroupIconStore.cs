using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// The image a user chose as a group's icon. While one is stored, sync uses it in place of
/// the composite of the group's app icons.
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
}
