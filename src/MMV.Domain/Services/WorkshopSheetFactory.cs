using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Optics;

namespace MMV.Domain.Services;

/// <summary>
/// Construction <b>pure</b> d'une version de fiche atelier (P3-6B) à partir d'une commande et de ses lignes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sans état, sans dépôt, sans EF.</b> La fabrique ne lit rien et n'écrit rien : elle <i>projette</i> une
/// <see cref="Order"/> déjà chargée vers un agrégat <see cref="WorkshopSheet"/> + <see cref="WorkshopSheetItem"/>
/// entièrement <b>copié par valeur</b>. La persistance et le versionnement atomique appartiennent à
/// l'Infrastructure ; la légalité (statut de commande) appartient à l'Application.
/// </para>
/// <para>
/// <b>Source jamais modifiée.</b> Aucune propriété de <see cref="Order"/>, <see cref="OrderItem"/>,
/// <see cref="Product"/>, <c>Sale</c> ou <c>Customer</c> n'est écrite : seules des <i>lectures</i> sont
/// effectuées, et la notation transposée est produite par <see cref="OpticalTranspositionService"/>, qui ne
/// manipule qu'un value object immuable.
/// </para>
/// <para>
/// <b>Minimisation.</b> Seul le nom du porteur est copié. Ni téléphone, ni email, ni adresse, ni montant.
/// </para>
/// <para>
/// <b>Robustesse aux données incomplètes.</b> Une ligne dont le produit est absent (FK nullable, produit non
/// chargé) est <b>conservée</b> avec ses données optiques et des valeurs produit neutres — jamais une
/// <see cref="NullReferenceException"/>. Un porteur inconnu reçoit un libellé neutre stable plutôt qu'une
/// identité inventée.
/// </para>
/// </remarks>
public static class WorkshopSheetFactory
{
    /// <summary>
    /// Libellé neutre utilisé lorsqu'aucun nom de porteur n'est disponible (vente au comptoir sans client,
    /// vente non chargée). N'invente aucune identité.
    /// </summary>
    public const string UnknownCustomerLabel = "Client non renseigné";

    /// <summary>
    /// Construit la version <paramref name="version"/> de la fiche de <paramref name="order"/>, marquée
    /// courante et en attente de contrôle qualité.
    /// </summary>
    /// <param name="order">Commande source, <b>avec</b> ses lignes (et, si possible, leurs produits chargés).</param>
    /// <param name="version">Numéro de version à attribuer (≥ 1), calculé par la couche de persistance.</param>
    /// <param name="createdAt">Horodatage de création de la version.</param>
    /// <exception cref="ArgumentNullException">si <paramref name="order"/> est <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">si <paramref name="version"/> est &lt; 1.</exception>
    public static WorkshopSheet Create(Order order, int version, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "La version d'une fiche atelier commence à 1.");
        }

        var sheet = new WorkshopSheet
        {
            OrderId = order.OrderId,
            Version = version,
            IsCurrent = true,
            CreatedAt = createdAt,
            TechnicalFingerprint = WorkshopSheetFingerprint.Compute(order),
            OrderNumberSnapshot = order.OrderNumber ?? string.Empty,
            OrderDateSnapshot = order.OrderDate,
            EstimatedDeliverySnapshot = order.EstimatedDelivery,
            CustomerNameSnapshot = ResolveCustomerName(order),
            InstructionsSnapshot = order.Notes,
            QcStatus = WorkshopSheetQcStatus.Pending,
            QcComment = null,
            QcCompletedAt = null
        };

        // Même ordre déterministe que l'empreinte : la position figée reflète l'ordre métier du bon d'atelier,
        // jamais l'ordre de restitution de la base.
        var orderedItems = (order.OrderItems ?? new List<OrderItem>())
            .OrderBy(i => i.OrderItemId)
            .ThenBy(i => (int)i.ItemType)
            .ThenBy(i => i.ProductId ?? long.MinValue)
            .ThenBy(i => i.Quantity)
            .ToList();

        for (var position = 0; position < orderedItems.Count; position++)
        {
            sheet.Items.Add(CreateItem(orderedItems[position], position));
        }

        return sheet;
    }

    /// <summary>
    /// Projette une ligne de commande vers une ligne snapshot, en conservant les <b>deux</b> notations optiques
    /// (source et atelier transposée) et en recopiant inchangés les champs hors portée de la transposition.
    /// </summary>
    private static WorkshopSheetItem CreateItem(OrderItem item, int position)
    {
        var transposition = OpticalTranspositionService.Transpose(
            new OpticalCorrection(item.Sphere, item.Cylinder, item.Axis));

        // Produit facultatif : on ne déréférence jamais une navigation potentiellement non chargée.
        var product = item.Product;

        return new WorkshopSheetItem
        {
            Position = position,
            ItemType = item.ItemType,
            SourceProductId = item.ProductId,
            ProductReferenceSnapshot = product?.Reference,
            ProductNameSnapshot = product?.Name,
            ProductCategorySnapshot = product?.Category.ToString(),
            Quantity = item.Quantity,

            // Copiés inchangés : hors portée de la transposition.
            UsageType = item.UsageType,
            Addition = item.Addition,
            PrismValue = item.PrismValue,
            PrismBase = item.PrismBase,
            VisualAcuity = item.VisualAcuity,

            // Notation source, conservée telle quelle.
            SourceSphere = item.Sphere,
            SourceCylinder = item.Cylinder,
            SourceAxis = item.Axis,

            // Notation atelier : renseignée uniquement si une transposition significative a été produite.
            TransposedSphere = transposition.IsTransposed ? transposition.Correction.Sphere : null,
            TransposedCylinder = transposition.IsTransposed ? transposition.Correction.Cylinder : null,
            TransposedAxis = transposition.IsTransposed ? transposition.Correction.Axis : null,
            HasTransposition = transposition.IsTransposed
        };
    }

    /// <summary>
    /// Nom du porteur, lu via la vente parente uniquement. Renvoie <see cref="UnknownCustomerLabel"/> si la
    /// vente, le client ou le nom sont absents — sans jamais inventer d'identité.
    /// </summary>
    private static string ResolveCustomerName(Order order)
    {
        var customer = order.Sale?.Customer;
        if (customer is null)
        {
            return UnknownCustomerLabel;
        }

        var fullName = $"{customer.FirstName} {customer.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? UnknownCustomerLabel : fullName;
    }
}
