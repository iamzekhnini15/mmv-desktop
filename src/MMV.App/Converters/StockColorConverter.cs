using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MMV.App.Converters;

public class StockColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && 
            values[0] is int stockQuantity && 
            values[1] is int alertThreshold)
        {
            return stockQuantity <= alertThreshold 
                ? new SolidColorBrush(Color.Parse("#FF453A"))  // Red
                : new SolidColorBrush(Color.Parse("#FFFFFF"));  // White
        }
        
        return new SolidColorBrush(Color.Parse("#FFFFFF"));
    }
}
