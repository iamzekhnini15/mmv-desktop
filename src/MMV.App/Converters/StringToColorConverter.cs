using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace MMV.App.Converters;

/// <summary>
/// Convertit une chaîne non vide en couleur pour les bordures d'erreur.
/// Si la chaîne est vide ou null, retourne Transparent.
/// Si la chaîne contient du texte, retourne une couleur rouge pour indiquer une erreur.
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            // Si il y a un message d'erreur, retourner rouge
            return new SolidColorBrush(Color.Parse("#E74C3C"));
        }
        
        // Sinon transparent
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
