using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using TaskbarFolders.Core.Interop;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.Views;

/// <summary>
/// Main window of the TaskbarFolders Manager application. Code-behind contains only
/// input-event routing (Enter → command, drag-and-drop → command, file picker → command);
/// all business logic lives in the view models.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class
    /// and assigns the supplied view model as its data context.
    /// </summary>
    /// <param name="viewModel">View model resolved from the DI container.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        viewModel.RenameRequested += OnRenameRequested;
        SourceInitialized += OnSourceInitialized;
        Activated += OnActivated;
        PreviewMouseDown += OnPreviewMouseDown;
        Closed += OnClosed;
    }

    // The popup can add or remove a group's apps while the Manager sits in the background;
    // coming back to the window is when that has to show up, before anything is saved.
    private async void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RefreshFromStoreAsync().ConfigureAwait(true);

            // A tile pinned or unpinned by hand meanwhile turns Pin to taskbar on or off.
            vm.Editor.RefreshPinState();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Enable Mica on the title-bar area for the Win11-native look (Settings / File
        // Explorer wear Mica too). Pre-22H2 Windows return non-zero HRESULT and we keep
        // the themed solid brushes — no visual regression on older systems.
        var hwnd = new WindowInteropHelper(this).Handle;
        _ = WindowBackdrop.TryApply(hwnd, WindowBackdropKind.Mica);

        ApplyTitleBarTheme();

        // The caption is drawn by DWM, so no resource-dictionary swap can reach it. Follow
        // the theme service instead, which also covers a live Windows theme switch while
        // the preference is System.
        if (ThemeService is { } themeService)
        {
            themeService.ThemeChanged += OnThemeChanged;
        }

        SourceInitialized -= OnSourceInitialized;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBarTheme();

    private void OnClosed(object? sender, EventArgs e)
    {
        if (ThemeService is { } themeService)
        {
            themeService.ThemeChanged -= OnThemeChanged;
        }
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RenameRequested -= OnRenameRequested;
        }
        Activated -= OnActivated;
        PreviewMouseDown -= OnPreviewMouseDown;
        Closed -= OnClosed;
    }

    private void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = ThemeService?.EffectiveTheme == ThemePreference.Dark;
        _ = WindowBackdrop.TrySetDarkTitleBar(hwnd, dark);
    }

    private static IThemeService? ThemeService =>
        Application.Current is App { Services: { } services }
            ? services.GetService<IThemeService>()
            : null;

    // The editor header may only just have been bound to a new group, so wait for layout
    // before moving the caret into it.
    private void OnRenameRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            GroupNameBox.Focus();
            GroupNameBox.SelectAll();
        });

    private void GroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // Saved on Enter; the focus then leaves the box, so its focus border goes and the
                // user can see the name was accepted (as Escape does).
                vm.CommitRenameCommand.Execute(null);
                GroupList.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CancelRenameCommand.Execute(null);
                GroupList.Focus();
                e.Handled = true;
                break;
        }
    }

    private void GroupName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CommitRenameCommand.Execute(null);
        }
    }

    // The icon and "more" buttons open their menu on a left click as well as a right one.
    private void OpenOwnMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void CopyGroupId_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { SelectedGroup: { } group })
        {
            Clipboard.SetText(group.Id);
        }
    }

    private void Editor_PreviewDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AppsTab_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    // DragLeave also fires when the pointer merely moves from one child of the tab onto
    // another, so the outline goes only once the pointer is outside the tab itself.
    private void AppsTab_DragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(AppsTab);
        if (position.X < 0 || position.Y < 0 || position.X >= AppsTab.ActualWidth || position.Y >= AppsTab.ActualHeight)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Editor_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            vm.Editor.AddAppsCommand.Execute(paths);
            e.Handled = true;
        }
    }

    private void ChooseGroupIcon_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.ico;*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            Title = "Choose the group's icon",
        };

        if (dialog.ShowDialog(this) == true)
        {
            vm.Editor.ChooseGroupIconCommand.Execute(dialog.FileName);
        }
    }

    // Del removes the selected app and Escape closes its settings — but not while the key
    // belongs to a text box in the row, where Del edits text and Escape undoes the edit.
    private void AppList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Delete && AppList.SelectedItem is AppEntryViewModel app)
        {
            vm.Editor.RemoveAppCommand.Execute(app);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.Editor.Apps.Any(a => a.IsExpanded))
        {
            vm.Editor.CollapseAppDetailsCommand.Execute(null);
            e.Handled = true;
        }
    }

    // A click on the row itself toggles its settings; clicks on its buttons and inside the
    // settings belong to those.
    private void AppRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not FrameworkElement { DataContext: AppEntryViewModel app } row)
        {
            return;
        }

        for (var node = e.OriginalSource as DependencyObject; node is not null && node != row; node = VisualOrLogicalParent(node))
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or TextBox ||
                (node is FrameworkElement { Name: "Details" }))
            {
                return;
            }
        }

        vm.Editor.ToggleAppDetailsCommand.Execute(app);
    }

    // Open settings close on a click anywhere outside their row. A text box being edited is
    // saved first: once its row collapses it could no longer lose focus the usual way.
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Editor.Apps.FirstOrDefault(a => a.IsExpanded) is not { } open)
        {
            return;
        }

        if (AppList.ItemContainerGenerator.ContainerFromItem(open) is UIElement { IsMouseOver: true })
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox box && box.DataContext == open)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
        vm.Editor.CollapseAppDetailsCommand.Execute(null);
    }

    private static DependencyObject? VisualOrLogicalParent(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    // Enter saves the box at once (it saves on focus loss anyway); Escape puts the saved
    // value back.
    private void AppSetting_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.GetBindingExpression(TextBox.TextProperty) is not { } binding)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                binding.UpdateSource();
                e.Handled = true;
                break;
            case Key.Escape:
                binding.UpdateTarget();
                e.Handled = true;
                break;
        }
    }

    private void ChooseAppIcon_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not FrameworkElement { DataContext: AppEntryViewModel app })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.ico;*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            Title = $"Choose an icon for {app.Name}",
        };

        if (dialog.ShowDialog(this) == true)
        {
            vm.Editor.ChooseAppIconCommand.Execute(new AppIconChoice(app, dialog.FileName));
        }
    }

    private void AddAppPicker_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Applications and shortcuts|*.exe;*.lnk|Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk",
            Multiselect = true,
            Title = "Add apps to group",
        };

        if (dialog.ShowDialog(this) == true && dialog.FileNames.Length > 0)
        {
            vm.Editor.AddAppsCommand.Execute(dialog.FileNames);
        }
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var settingsVm = app.Services.GetRequiredService<SettingsViewModel>();
        await settingsVm.LoadAsync().ConfigureAwait(true);

        var window = app.Services.GetRequiredService<SettingsWindow>();
        window.Owner = this;
        window.ShowDialog();
    }
}
