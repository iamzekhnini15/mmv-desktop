using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MMV.App.Converters
{
    public class IsZeroConverter : IValueConverter
    {
        public static readonly IsZeroConverter Instance = new IsZeroConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return true;
            if (value is int i) return i == 0;
            if (value is long l) return l == 0L;
            if (value is double d) return Math.Abs(d) < double.Epsilon;
            if (value is float f) return Math.Abs(f) < float.Epsilon;
            // fallback: try parse
            if (int.TryParse(value.ToString(), out var parsed)) return parsed == 0;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
