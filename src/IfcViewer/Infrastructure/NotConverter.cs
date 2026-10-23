using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IfcViewer.Infrastructure
{
    /// <summary>
    /// XAML converter that inverts a bool: IsEnabled="{Binding IsBusy, Converter={StaticResource Not}}".
    /// With ConverterParameter=Visibility the result is a Visibility instead (false → Visible, true → Collapsed).
    /// </summary>
    public class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var inverted = !(value is bool b && b);
            if (Equals(parameter, "Visibility")) return inverted ? Visibility.Visible : Visibility.Collapsed;
            return inverted;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value is bool b && b);
    }
}
