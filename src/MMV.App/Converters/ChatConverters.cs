using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Globalization;

namespace MMV.App.Converters;

public class BoolToAlignmentConverter : IValueConverter
{
    public static readonly BoolToAlignmentConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Si c'est l'utilisateur (true) -> Droite, Sinon (false) -> Gauche
        return (bool)value! ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToColumnConverter : IValueConverter
{
    public static readonly BoolToColumnConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Utilisateur -> Colonne 2 (Droite), Assistant -> Colonne 0 (Gauche)
        return (bool)value! ? 2 : 0;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToCornerConverter : IValueConverter
{
    public static readonly BoolToCornerConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Bulle User : Coin bas-droit pointu
        // Bulle Assistant : Coin bas-gauche pointu
        return (bool)value! 
            ? new CornerRadius(18, 18, 4, 18) 
            : new CornerRadius(18, 18, 18, 4);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToBackgroundConverter : IValueConverter
{
    public static readonly BoolToBackgroundConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Couleurs hardcodées pour matcher ton thème Dark
        // User = Accent (Bleu), Assistant = SurfaceLight (Gris foncé)
        return (bool)value! 
            ? SolidColorBrush.Parse("#0A84FF") 
            : SolidColorBrush.Parse("#2C2C30");
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}