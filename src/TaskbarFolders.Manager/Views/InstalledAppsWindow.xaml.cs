using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using TaskbarFolders.Core.Interop;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.Views;

/// <summary>
/// Modal picker over Windows' installed-apps list (ADR-009). The caller loads the view model,
/// shows the window, and on <see langword="true"/> adds <see cref="InstalledAppsPickerViewModel.Selected"/>.
/// The window disposes its view model when it closes, which stops the icon loading.
/// </summary>
public partial class InstalledAppsWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="InstalledAppsWindow"/> class.</summary>
    /// <param name="viewModel">View model resolved from the DI container.</param>
    public InstalledAppsWindow(InstalledAppsPickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>The picker this window shows.</summary>
    public InstalledAppsPickerViewModel ViewModel { get; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // The same material and caption as every Manager window.
        _ = MicaSurfaces.TryApply(this);
        var dark = Application.Current is App { Services: { } services }
            && services.GetService<IThemeService>()?.EffectiveTheme == ThemePreference.Dark;
        _ = WindowBackdrop.TrySetDarkTitleBar(new WindowInteropHelper(this).Handle, dark);
        SourceInitialized -= OnSourceInitialized;
    }

    // Closing is the view's business; what to add is read from the view model by the caller.
    private void Add_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    // Closed outright: IsCancel alone closes only a window shown as a dialog, and ShowDialog then
    // reports no result, which the caller reads as "add nothing".
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
