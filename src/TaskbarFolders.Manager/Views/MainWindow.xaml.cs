using System;
using System.Windows;
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
        Closed += OnClosed;
    }

    // The popup can add or remove a group's apps while the Manager sits in the background;
    // coming back to the window is when that has to show up, before anything is saved.
    private async void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RefreshFromStoreAsync().ConfigureAwait(true);
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
                vm.CommitRenameCommand.Execute(null);
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

    private void Editor_Drop(object sender, DragEventArgs e)
    {
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
