using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Stock.CreateStockMovement;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateStockMovementUseCase"/>. Permet à la ViewModel de poursuivre son
/// flux (comptage des succès / erreurs, message de synthèse, rafraîchissement de la liste) sans accéder aux
/// détails de persistance.
/// </summary>
/// <remarks>
/// Lorsque le produit est introuvable, <see cref="ProductFound"/> vaut <c>false</c> : la ViewModel compte alors
/// la ligne comme une erreur et passe à la suivante, exactement comme le flux d'origine (qui faisait
/// <c>continue</c> sans lever d'exception). Les refus métier (stock insuffisant) et erreurs techniques restent,
/// eux, propagés sous forme d'exceptions (<see cref="MMV.Domain.Exceptions.InsufficientStockException"/>,
/// <see cref="MMV.Domain.Exceptions.PersistenceException"/>) et sont traités par la ViewModel, à l'identique.
/// </remarks>
public sealed class CreateStockMovementResult
{
    /// <summary>Indique si le produit a été retrouvé (sinon : ligne comptée en erreur, aucune écriture).</summary>
    public bool ProductFound { get; init; }

    /// <summary>Identifiant du mouvement de stock créé (0 si produit introuvable).</summary>
    public long StockMovementId { get; init; }

    /// <summary>Identifiant du produit concerné.</summary>
    public long ProductId { get; init; }

    /// <summary>Type de mouvement appliqué.</summary>
    public StockMovementType MovementType { get; init; }

    /// <summary>Quantité du mouvement (telle que saisie, toujours positive).</summary>
    public int Quantity { get; init; }

    /// <summary>Nouveau stock du produit après application du mouvement.</summary>
    public int NewStockQuantity { get; init; }
}
