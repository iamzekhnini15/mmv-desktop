using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Stock.CreateStockMovement;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreateStockMovementUseCase"/> — « Créer un mouvement manuel de stock »
/// (quatrième vertical slice, P2B-2F). Contient uniquement les données nécessaires d'<b>une seule</b> ligne de
/// mouvement provenant de <c>StockMovementFormViewModel</c> ; la ViewModel transforme chaque ligne valide de son
/// formulaire multi-produits en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel du flux de création de mouvement
/// manuel auparavant porté par <c>StockMovementFormViewModel.SaveAsync</c> : le use case n'introduit aucun
/// nouveau concept (aucun <c>Money</c>, aucune devise, aucune règle pays/fiscalité).
/// <para>
/// Le <see cref="MovementType"/> est déjà résolu (parsé) par la ViewModel à partir du choix d'interface
/// (« In » / « Out » / « Adjustment »). La <see cref="Reason"/> est déjà résolue par la ViewModel (note saisie,
/// ou libellé par défaut « Mouvement {type} ») afin de préserver, au caractère près, le motif enregistré par le
/// flux d'origine. La <see cref="Quantity"/> est la quantité saisie (toujours strictement positive, validée côté
/// UI) ; le <b>sens</b> de l'effet sur le stock découle du <see cref="MovementType"/> (entrée = incrément, sortie
/// = décrément sûr, ajustement = valeur absolue), exactement comme avant.
/// </para>
/// </remarks>
public sealed class CreateStockMovementCommand
{
    /// <summary>Identifiant du produit concerné par le mouvement.</summary>
    public long ProductId { get; init; }

    /// <summary>Type de mouvement résolu par la ViewModel (Entrée, Sortie, Ajustement).</summary>
    public StockMovementType MovementType { get; init; }

    /// <summary>Quantité saisie (strictement positive, validée côté UI). Le sens découle du type de mouvement.</summary>
    public int Quantity { get; init; }

    /// <summary>Motif déjà résolu par la ViewModel (note saisie ou libellé par défaut), repris tel quel.</summary>
    public string? Reason { get; init; }
}
