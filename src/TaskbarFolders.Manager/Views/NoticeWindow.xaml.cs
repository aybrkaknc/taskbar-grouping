using System;
using System.Runtime.Versioning;
using System.Windows;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Manager.Views;

/// <summary>
/// A one-button notice in the Manager's own theme. It is shown modally, so the caller returns when
/// it is dismissed, exactly as with the message box it replaces.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class NoticeWindow : Window
{
    /// <summary>Initializes a new instance with the given caption and message.</summary>
    /// <param name="caption">Heading shown above the message.</param>
    /// <param name="message">The body text, as written; line breaks are kept.</param>
    public NoticeWindow(string caption, string message)
    {
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(message);

        InitializeComponent();
        Title = caption;
        CaptionText.Text = caption;
        MessageText.Text = message;
        SourceInitialized += OnSourceInitialized;
    }

    // The same material as the Settings dialog and the main window; the message sits straight on it.
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _ = MicaSurfaces.TryApply(this);
        ApplyTitleBarTheme();
    }

    // The title bar follows the app's theme, as the Settings dialog's does.
    private void ApplyTitleBarTheme()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _ = TitleBar.Apply(hwnd, IsDarkTheme());
    }

    private static bool IsDarkTheme() =>
        Application.Current?.Resources["WindowBackground"] is System.Windows.Media.SolidColorBrush brush
        && (0.299 * brush.Color.R + 0.587 * brush.Color.G + 0.114 * brush.Color.B) < 128;

    private void OnOkClicked(object sender, RoutedEventArgs e) => Close();
}
