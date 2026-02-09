using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MMV.Domain.Enums;

namespace MMV.App.Converters;

/// <summary>
/// Convertit un <see cref="OrderStatus"/> en libellé français.
/// </summary>
public class OrderStatusToStringConverter : IValueConverter
{
    public static readonly OrderStatusToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OrderStatus status)
        {
            return status switch
            {
                OrderStatus.New => "Nouveau",
                OrderStatus.ToFabricate => "À fabriquer",
                OrderStatus.InProgress => "En fabrication",
                OrderStatus.QualityCheck => "Contrôle qualité",
                OrderStatus.Ready => "Prêt",
                OrderStatus.Delivered => "Livré",
                _ => status.ToString()
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
