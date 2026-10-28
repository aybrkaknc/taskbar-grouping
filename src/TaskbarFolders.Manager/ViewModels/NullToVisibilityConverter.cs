using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound value is <see langword="null"/>,
/// otherwise <see cref="Visibility.Collapsed"/>. Used for empty-state placeholders.
/// Exposes a static <see cref="Instance"/> so XAML can use <c>{x:Static}</c> without resource declarations.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NullToVisibilityConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Inverse of <see cref="NullToVisibilityConverter"/> — visible when the bound value is not null.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NotNullToVisibilityConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}


/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound boolean is <see langword="true"/>.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly BoolToVisibilityConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
