using System.Windows.Controls;

namespace TaskbarFolders.Manager.Views;

/// <summary>
/// The preview of a group's popup on the Appearance tab. Layout only; its DataContext is a
/// <see cref="ViewModels.PopupPreviewViewModel"/>.
/// </summary>
public partial class PopupPreview : UserControl
{
    /// <summary>Initializes a new instance.</summary>
    public PopupPreview()
    {
        InitializeComponent();
    }
}
