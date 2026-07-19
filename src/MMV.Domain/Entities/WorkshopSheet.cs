using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// <b>Fiche atelier de montage</b> (bon de travaux technicien) — P3-6B. Version <b>immuable</b> et
/// <b>horodatée</b> d'un ordre de fabrication, dérivée d'une <see cref="Order"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot, pas référence vivante.</b> Tout ce qui est affiché sur la fiche est <b>copié par valeur</b> au
/// moment de la génération (numéro/date de commande, nom du porteur, instructions, et — sur
/// <see cref="WorkshopSheetItem"/> — les données produit et optiques). Une fiche ancienne ne change donc
/// <b>jamais</b> quand le client est renommé, le catalogue modifié ou un produit désactivé : c'est un document
/// d'atelier historique, pas une vue.
/// </para>
/// <para>
/// <b>Versionnement.</b> Une commande possède 0..N versions. Au plus <b>une seule</b> est courante
/// (<see cref="IsCurrent"/>), garantie en base par un index unique filtré ; le couple
/// <c>(OrderId, Version)</c> est unique. Une évolution ne modifie jamais une version existante : elle en crée
/// une nouvelle, reconstruite depuis la commande, qui repart <see cref="WorkshopSheetQcStatus.Pending"/>.
/// </para>
/// <para>
/// <b>Immutabilité.</b> Après création, seuls les champs de contrôle qualité évoluent, et une seule fois
/// (<c>Pending → Passed</c> ou <c>Pending → Failed</c>). Les données snapshot ne sont jamais réécrites.
/// </para>
/// <para>
/// <b>Minimisation des données personnelles.</b> Seul le <b>nom</b> du porteur est copié. Aucun téléphone,
/// email ou adresse ; aucun montant, acompte ou reste à payer (ce n'est ni une facture ni un devis).
/// </para>
/// <para>
/// <b>Ordonnance source intacte.</b> La fiche ne modifie jamais <see cref="Prescription"/>,
/// <see cref="SaleItem"/> ni <see cref="OrderItem"/> ; la notation transposée est une valeur <b>dérivée</b>
/// stockée à côté de la notation source, jamais à sa place.
/// </para>
/// </remarks>
public class WorkshopSheet
{
    /// <summary>Identifiant unique de la fiche (une ligne = une version).</summary>
    public long WorkshopSheetId { get; set; }

    /// <summary>Identifiant de la commande dont cette fiche dérive.</summary>
    public long OrderId { get; set; }

    /// <summary>Numéro de version, à partir de <c>1</c>, unique par commande.</summary>
    public int Version { get; set; }

    /// <summary>
    /// Vrai si cette version est la version <b>courante</b> (autoritaire) de la commande. Au plus une seule
    /// version courante par commande — garanti en base par un index unique filtré.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>Horodatage de création de la version.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Empreinte <b>déterministe</b> des données techniques de la commande au moment du snapshot
    /// (cf. <c>WorkshopSheetFingerprint</c>). Recalculée à la lecture pour détecter qu'une fiche est devenue
    /// <b>obsolète</b> parce que la commande a changé depuis.
    /// </summary>
    public string TechnicalFingerprint { get; set; } = string.Empty;

    /// <summary>Numéro de commande figé au moment du snapshot.</summary>
    public string OrderNumberSnapshot { get; set; } = string.Empty;

    /// <summary>Date de commande figée au moment du snapshot.</summary>
    public DateTime OrderDateSnapshot { get; set; }

    /// <summary>Date de livraison estimée figée au moment du snapshot (facultative).</summary>
    public DateTime? EstimatedDeliverySnapshot { get; set; }

    /// <summary>
    /// Nom du porteur figé au moment du snapshot — <b>seule</b> donnée personnelle conservée (minimisation).
    /// </summary>
    public string CustomerNameSnapshot { get; set; } = string.Empty;

    /// <summary>Instructions/notes atelier figées au moment du snapshot.</summary>
    public string? InstructionsSnapshot { get; set; }

    /// <summary>Résultat du contrôle qualité atelier de cette version.</summary>
    public WorkshopSheetQcStatus QcStatus { get; set; } = WorkshopSheetQcStatus.Pending;

    /// <summary>
    /// Commentaire de contrôle : <b>obligatoire</b> pour <see cref="WorkshopSheetQcStatus.Failed"/>,
    /// facultatif pour <see cref="WorkshopSheetQcStatus.Passed"/>.
    /// </summary>
    public string? QcComment { get; set; }

    /// <summary>Horodatage de la décision de contrôle qualité (<c>null</c> tant que <c>Pending</c>).</summary>
    public DateTime? QcCompletedAt { get; set; }

    // Navigation Properties
    /// <summary>Commande dont cette fiche dérive (suppression <b>Restrict</b> : jamais de cascade).</summary>
    public virtual Order Order { get; set; } = null!;

    /// <summary>Lignes snapshot de la fiche (monture, verres OD/OG, accessoires…), dans l'ordre du bon.</summary>
    public virtual ICollection<WorkshopSheetItem> Items { get; set; } = new List<WorkshopSheetItem>();
}
