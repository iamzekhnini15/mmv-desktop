namespace MMV.Domain.Interfaces.Persistence;

/// <summary>
/// Résultat d'un ajustement d'inventaire concurrent-safe (P3-5) : porte la quantité <b>avant</b> et
/// <b>après</b> correction afin que le use case appelant puisse enregistrer le <b>delta signé réel</b>
/// (<see cref="Delta"/>) dans le mouvement de stock (<c>Adjustment</c>) sans jamais recalculer à partir
/// d'une valeur lue en mémoire (potentiellement obsolète).
/// </summary>
/// <param name="ProductId">Identifiant du produit ajusté.</param>
/// <param name="PreviousQuantity">Stock lu (et confirmé par la mise à jour conditionnelle) juste avant l'écriture.</param>
/// <param name="NewQuantity">Stock cible effectivement écrit (valeur comptée).</param>
public sealed record StockAdjustmentResult(long ProductId, int PreviousQuantity, int NewQuantity)
{
    /// <summary>Delta signé réel appliqué au stock (<see cref="NewQuantity"/> − <see cref="PreviousQuantity"/>).</summary>
    public int Delta => NewQuantity - PreviousQuantity;
}
