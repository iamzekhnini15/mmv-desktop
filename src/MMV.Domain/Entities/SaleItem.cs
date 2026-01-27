namespace MMV.Domain.Entities;

/// <summary>
/// Représente un article dans une vente.
/// </summary>
public class SaleItem
{
    /// <summary>
    /// Identifiant unique de l'article de vente.
    /// </summary>
    public long SaleItemId { get; set; }

    /// <summary>
    /// Identifiant de la vente parente.
    /// </summary>
    public long SaleId { get; set; }

    /// <summary>
    /// Identifiant du produit vendu (optionnel).
    /// </summary>
    public long? ProductId { get; set; }

    /// <summary>
    /// Quantité vendue.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Prix unitaire de l'article (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Prix total pour cette ligne (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal TotalPrice { get; set; }

    // Navigation Properties
    /// <summary>
    /// Vente parente à laquelle appartient cet article.
    /// </summary>
    public virtual Sale Sale { get; set; } = null!;

    /// <summary>
    /// Produit vendu.
    /// </summary>
    public virtual Product? Product { get; set; }
}
