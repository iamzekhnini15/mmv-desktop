using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MMV.App.Converters;

/// <summary>
/// Convertit un écart (différence) en couleur.
/// 0 = blanc
/// Négatif = rouge (manquant)
/// Positif = vert (surplus)
/// </summary>
public class DifferenceColorConverter : IValueConverter
{
    public static readonly DifferenceColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int difference)
        {
            if (difference < 0)
            {
                return new SolidColorBrush(Color.Parse("#FF453A")); // Red for negative (missing)
            }
            else if (difference > 0)
            {
                return new SolidColorBrush(Color.Parse("#27AE60")); // Green for positive (surplus)
            }
        }
        return new SolidColorBrush(Color.Parse("#FFFFFF")); // White for zero or unknown
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
