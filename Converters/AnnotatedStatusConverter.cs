using System;
using System.Globalization;
using System.Windows.Data;

namespace YoloAnnotator.Converters
{
    public class AnnotatedStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "#22c55e" : "#555";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}