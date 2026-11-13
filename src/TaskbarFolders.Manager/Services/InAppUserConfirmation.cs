using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using TaskbarFolders.Manager.Views;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// The Manager's <see cref="IUserConfirmation"/>. Information is shown in the Manager's own notice
/// window, styled with its theme; a question that needs a yes or no is still asked with the system
/// message box, because the answer decides what happens next.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InAppUserConfirmation : IUserConfirmation
{
    private readonly MessageBoxUserConfirmation _questions = new();

    /// <inheritdoc/>
    public bool Confirm(string caption, string message) => _questions.Confirm(caption, message);

    /// <inheritdoc/>
    public void Notify(string caption, string message)
    {
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(message);

        var app = Application.Current;
        if (app is null || !app.Dispatcher.CheckAccess())
        {
            // Not on the UI thread (a background call, or no application): a message box still
            // delivers the text, so nothing is lost.
            _questions.Notify(caption, message);
            return;
        }

        var notice = new NoticeWindow(caption, message);
        var owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != notice)
                    ?? app.MainWindow;
        if (owner is not null && owner.IsVisible)
        {
            notice.Owner = owner;
        }

        notice.ShowDialog();
    }
}
