using System;
using System.Collections.Generic;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.WorkshopSheets;

/// <summary>
/// Fiche atelier (lecture seule) — P3-6B. Projection plate d'une version de <c>WorkshopSheet</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune donnée personnelle au-delà du nom.</b> Le DTO ne porte ni téléphone, ni email, ni adresse, ni
/// montant : ces champs n'existent pas dans le modèle de fiche (minimisation par construction, pas par filtrage).
/// </para>
/// <para>
/// <see cref="IsUpToDate"/> est <b>calculé à la lecture</b> en recomparant l'empreinte technique stockée à celle
/// des données réelles de la commande : <c>false</c> signale une fiche <b>obsolète</b> (la commande a changé
/// depuis la génération).
/// </para>
/// </remarks>
public sealed class WorkshopSheetDto
{
    /// <summary>Identifiant de la version de fiche.</summary>
    public long WorkshopSheetId { get; init; }

    /// <summary>Identifiant de la commande d'origine.</summary>
    public long OrderId { get; init; }

    /// <summary>Numéro de version (à partir de 1).</summary>
    public int Version { get; init; }

    /// <summary>Vrai si cette version est la version courante (autoritaire) de la commande.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>Horodatage de création de la version.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Numéro de commande figé au snapshot.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Date de commande figée au snapshot.</summary>
    public DateTime OrderDate { get; init; }

    /// <summary>Date de livraison estimée figée au snapshot.</summary>
    public DateTime? EstimatedDelivery { get; init; }

    /// <summary>Nom du porteur figé au snapshot (seule donnée personnelle conservée).</summary>
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>Instructions d'atelier figées au snapshot.</summary>
    public string? Instructions { get; init; }

    /// <summary>Résultat du contrôle qualité de cette version.</summary>
    public WorkshopSheetQcStatus QcStatus { get; init; }

    /// <summary>Commentaire de contrôle qualité.</summary>
    public string? QcComment { get; init; }

    /// <summary>Horodatage de la décision de contrôle qualité.</summary>
    public DateTime? QcCompletedAt { get; init; }

    /// <summary>
    /// Vrai si l'empreinte technique de la fiche correspond toujours aux données réelles de la commande.
    /// <c>false</c> ⇒ fiche <b>obsolète</b> : une nouvelle version doit être générée.
    /// </summary>
    public bool IsUpToDate { get; init; }

    /// <summary>Lignes snapshot, dans l'ordre figé du bon d'atelier.</summary>
    public IReadOnlyList<WorkshopSheetItemDto> Items { get; init; } = Array.Empty<WorkshopSheetItemDto>();
}
