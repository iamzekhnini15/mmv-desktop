using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MMV.App.Converters;

/// <summary>
/// Convertit une chaîne non vide en true, et une chaîne vide/null en false.
/// Utilisé pour afficher/masquer les messages d'erreur.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string str && !string.IsNullOrEmpty(str);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
