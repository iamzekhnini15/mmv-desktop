using Avalonia.Data.Converters;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using System;
using System.Globalization;

namespace MMV.App.Converters;

/// <summary>
/// Converter to display category-specific detail information for products.
/// </summary>
public class CategoryToDetailConverter : IValueConverter
{
    public static readonly CategoryToDetailConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is not Product product)
            return null;

        return product.Category switch
        {
            ProductCategoryEnum.VERRE when product.GlassDetail != null =>
                $"{product.GlassDetail.Material} | {product.GlassDetail.GlassType} | Ø{product.GlassDetail.Diameter}",

            ProductCategoryEnum.LENTILLE when product.LensDetail != null =>
                $"{product.LensDetail.Brand} | {product.LensDetail.LensType} | {product.LensDetail.Duration}",

            ProductCategoryEnum.MONTURE or ProductCategoryEnum.CLIPS or ProductCategoryEnum.PLASTIC or ProductCategoryEnum.SOLAIRE 
                when product.AccessoryDetail != null =>
                $"{product.AccessoryDetail.Color} | {product.AccessoryDetail.Size} | {product.AccessoryDetail.Material}",

            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}

