namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Détails monture/accessoire d'un produit (P2D-7D), reflet plat de <c>MMV.Domain.Entities.AccessoryDetail</c>
/// (aucune navigation EF), consommé par <c>ProductDetailViewModel</c> (fiche) et <c>ProductFormViewModel</c>
/// (pré-remplissage édition).
/// </summary>
public sealed class ProductAccessoryDetailsDto
{
    /// <summary>Couleur du produit.</summary>
    public string? Color { get; init; }

    /// <summary>Taille/calibre.</summary>
    public string? Size { get; init; }

    /// <summary>Matière du produit.</summary>
    public string? Material { get; init; }
}
