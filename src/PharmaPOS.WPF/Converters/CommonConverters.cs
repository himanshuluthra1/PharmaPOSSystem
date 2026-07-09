using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PharmaPOS.WPF.Converters;

/// <summary>
/// Converts a value to <see cref="Visibility"/>. Visible when: bool is true, an int
/// is &gt; 0, or a reference value is non-null; otherwise Collapsed. This makes the
/// same converter usable for booleans, collection counts and null checks.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            bool b => b,
            int i => i > 0,
            long l => l > 0,
            _ => true
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Inverts a boolean value.</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>Non-empty string becomes Visible; empty/null becomes Collapsed.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a decimal as Indian Rupee currency.</summary>
public class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal d) return "\u20B9 " + d.ToString("N2", CultureInfo.InvariantCulture);
        return "\u20B9 0.00";
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
