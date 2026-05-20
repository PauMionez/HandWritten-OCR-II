using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HandWritten_OCR.Converters;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool IsInverted { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNotNull = value is not null;
        return isNotNull ^ IsInverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
