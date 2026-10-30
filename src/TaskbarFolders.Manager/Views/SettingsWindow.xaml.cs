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
/// Modal settings window. The DI-resolved <see cref="SettingsViewModel"/> is loaded once
/// on construction; the dialog remains open until the user clicks Close.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="viewModel">View model resolved from the DI container.</param>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // The same material as the main window; the form sits straight on it.
        _ = MicaSurfaces.TryApply(this);
        ApplyTitleBarTheme();

        // Saving from this dialog is what changes the theme, so it has to repaint its own
        // caption rather than wait for a reopen.
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
}
