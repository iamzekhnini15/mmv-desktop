using System;
using MMV.Domain.Enums;

namespace MMV.Domain.Services;

/// <summary>
/// <b>Propriétaire unique</b> de la classification du <b>flux de consommation du stock</b> d'une ligne (P3-11).
/// Politique Domain <b>pure</b> : aucun dépôt, aucun état, aucun effet de bord.
/// </summary>
/// <remarks>
/// <para>
/// <b>Défaut corrigé.</b> Avant P3-11, deux clés indépendantes décidaient du sort d'une même ligne, sans jamais
/// être croisées : le <see cref="OrderItemType"/> de la ligne décidait la création de la commande de fabrication
/// et le versement de la ligne dans cette commande, tandis que la catégorie du produit (<c>Product.Category</c>)
/// décidait le saut du décrément à la vente. Une ligne dont les deux clés se contredisent était <b>acceptée</b> et
/// produisait soit un <b>double décrément</b> (ligne verre sur produit non-verre : décrémenté à la vente, puis à
/// la fabrication), soit <b>aucun décrément</b> (ligne non-verre sur produit verre : exclu de la vente par sa
/// catégorie, absent de la commande faute d'<c>ItemType</c> verre). Le défaut a été découvert par exécution en
/// recette P3-11 : il n'apparaît qu'en traversant la vente <b>puis</b> la fabrication.
/// </para>
/// <para>
/// <b>Périmètre volontairement minimal.</b> Cette politique ne décrit <b>pas</b> une taxonomie commerciale : elle
/// ne prétend ni que <see cref="OrderItemType.Frame"/> désigne exactement
/// <see cref="ProductCategoryEnum.MONTURE"/>, ni que <see cref="OrderItemType.Accessory"/> désigne exactement
/// <see cref="ProductCategoryEnum.CLIPS"/>. Elle n'oppose qu'une seule question, celle qui décide réellement d'une
/// mutation de stock : <b>ce produit sort-il de l'inventaire à la vente, ou plus tard à la fabrication ?</b> Une
/// monture vendue sur une ligne <c>Accessory</c> reste donc acceptée — les deux se consomment immédiatement, et
/// aucun écart d'inventaire n'en résulte.
/// </para>
/// <para>
/// <b>Invariant garanti.</b> Pour toute ligne acceptée, il existe <b>exactement un</b> chemin de consommation du
/// stock : jamais zéro, jamais deux.
/// </para>
/// </remarks>
public static class SaleLineStockFlowPolicy
{
    /// <summary>
    /// Message métier stable opposé à une ligne dont le type contredit la catégorie du produit référencé. Aucun
    /// détail technique, aucun nom d'enum, aucun message provider : le texte est directement affichable.
    /// </summary>
    public const string SaleLineProductCategoryMismatchMessage =
        "Le type de ligne ne correspond pas à la catégorie du produit.";

    /// <summary>
    /// Indique si le type de ligne désigne un article <b>commandé au fournisseur puis consommé à la fabrication</b>
    /// (consommation <b>différée</b>), par opposition à un article consommé immédiatement à la vente.
    /// </summary>
    /// <remarks>
    /// Une valeur d'énumération inconnue n'est <b>jamais</b> traitée comme un article de fabrication : elle ne
    /// bénéficie d'aucun repli silencieux, et <see cref="IsCompatible"/> la refuse de toute façon.
    /// </remarks>
    public static bool IsFabricationItemType(OrderItemType itemType) => itemType switch
    {
        OrderItemType.LensOd or OrderItemType.LensOg => true,
        OrderItemType.Frame or OrderItemType.Accessory => false,
        _ => false
    };

    /// <summary>
    /// Indique si la catégorie du produit désigne un article à consommation de stock <b>différée</b> (sorti à la
    /// fabrication), par opposition aux catégories consommées immédiatement à la vente.
    /// </summary>
    /// <remarks>
    /// Reproduit à l'identique la condition qui pilotait déjà le saut du décrément à la vente
    /// (<c>VERRE</c> ou <c>LENTILLE</c>) : P3-11 ne déplace aucune frontière métier, il en fait le
    /// <b>propriétaire unique</b>. Une valeur d'énumération inconnue n'est jamais traitée comme différée.
    /// </remarks>
    public static bool IsDeferredStockCategory(ProductCategoryEnum category) => category switch
    {
        ProductCategoryEnum.VERRE or ProductCategoryEnum.LENTILLE => true,
        ProductCategoryEnum.CLIPS
            or ProductCategoryEnum.PLASTIC
            or ProductCategoryEnum.MONTURE
            or ProductCategoryEnum.SOLAIRE => false,
        _ => false
    };

    /// <summary>
    /// Indique si le type de ligne et la catégorie du produit s'accordent sur <b>un seul et même</b> moment de
    /// consommation du stock.
    /// </summary>
    /// <remarks>
    /// <b>Aucune valeur inconnue n'est compatible.</b> Les deux énumérations sont vérifiées comme réellement
    /// définies <b>avant</b> toute comparaison : sans cela, une valeur castée hors domaine retomberait sur
    /// <c>false</c> dans les deux classifications et serait déclarée compatible avec une catégorie immédiate —
    /// exactement le repli silencieux que cette politique doit interdire.
    /// </remarks>
    public static bool IsCompatible(OrderItemType itemType, ProductCategoryEnum category)
    {
        if (!Enum.IsDefined(itemType) || !Enum.IsDefined(category))
            return false;

        return IsFabricationItemType(itemType) == IsDeferredStockCategory(category);
    }
}
