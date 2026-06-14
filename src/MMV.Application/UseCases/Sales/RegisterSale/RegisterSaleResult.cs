using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Sortie (DTO) du use case <see cref="RegisterSaleUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (notification, rafraîchissement, affichage) sans accéder aux détails de persistance.
/// </summary>
public sealed class RegisterSaleResult
{
    /// <summary>Identifiant de la vente créée.</summary>
    public long SaleId { get; init; }

    /// <summary>Numéro de vente attribué (séquence <c>SALE</c>), ex. <c>VTE-000001</c>.</summary>
    public string SaleNumber { get; init; } = string.Empty;

    /// <summary>Identifiant de la commande fournisseur créée (si la vente contient des verres), sinon <c>null</c>.</summary>
    public long? OrderId { get; init; }

    /// <summary>Numéro de commande fournisseur attribué (séquence <c>ORDER</c>) si verres, sinon <c>null</c>.</summary>
    public string? OrderNumber { get; init; }

    /// <summary>Statut de la vente (<c>Delivered</c> comptoir / <c>AwaitingLenses</c> fabrication).</summary>
    public SaleStatus Status { get; init; }

    /// <summary>Montant final après remise.</summary>
    public decimal FinalAmount { get; init; }

    /// <summary>Montant restant à payer.</summary>
    public decimal RemainingAmount { get; init; }

    /// <summary>
    /// Entité vente persistée. Exposée <b>pendant la migration</b> (transit toléré par l'ADR frontières §10 :
    /// aucune fuite de <c>DbContext</c>/<c>IQueryable</c>) uniquement pour préserver, à l'identique, l'événement
    /// <c>SaleFormViewModel.OrderSaved</c> (<c>EventHandler&lt;Sale&gt;</c>). À retirer quand cet événement
    /// sera lui-même migré.
    /// </summary>
    public Sale Sale { get; init; } = null!;
}
