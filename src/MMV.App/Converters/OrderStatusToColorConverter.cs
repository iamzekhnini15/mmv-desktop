using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MMV.Domain.Enums;

namespace MMV.App.Converters;

/// <summary>
/// Convertit un <see cref="OrderStatus"/> en <see cref="SolidColorBrush"/> pour les badges.
/// </summary>
public class OrderStatusToColorConverter : IValueConverter
{
    public static readonly OrderStatusToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OrderStatus status)
        {
            var color = status switch
            {
                OrderStatus.New => Color.Parse("#0A84FF"),
                OrderStatus.ToFabricate => Color.Parse("#FF9500"),
                OrderStatus.InProgress => Color.Parse("#AF52DE"),
                OrderStatus.QualityCheck => Color.Parse("#FFD60A"),
                OrderStatus.Ready => Color.Parse("#30D158"),
                OrderStatus.Delivered => Color.Parse("#98989F"),
                _ => Color.Parse("#98989F")
            };

            // Si le paramètre est "bg", retourner une couleur atténuée pour le fond
            if (parameter is string p && p == "bg")
            {
                return new SolidColorBrush(color, 0.15);
            }

            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
