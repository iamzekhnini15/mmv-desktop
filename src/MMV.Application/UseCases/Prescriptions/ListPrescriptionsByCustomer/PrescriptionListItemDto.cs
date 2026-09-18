using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

/// <summary>
/// DTO applicatif (lecture seule) d'une ordonnance d'un client (P2D-5). Remplace l'entité EF <c>Prescription</c> côté
/// UI pour le flux de lecture Ordonnances : liste (<c>CustomerPrescriptionsView</c>), fiche détaillée
/// (<c>PrescriptionDetailView</c> via <c>PrescriptionDetailViewModel.CurrentPrescription</c>) et pré-remplissage du
/// formulaire de modification (<c>PrescriptionFormViewModel.LoadPrescription</c>). Porte tous les champs
/// optométriques scalaires effectivement consommés par ces écrans ; aucune navigation EF (<c>Customer</c>) n'est
/// exposée.
/// </summary>
/// <remarks>
/// <see cref="OdPrismBase"/> / <see cref="OgPrismBase"/> sont des enums Domain (<c>PrismBase</c>, valeurs métier
/// stables), acceptables en DTO : ce ne sont pas des entités.
/// </remarks>
public sealed class PrescriptionListItemDto
{
    /// <summary>Identifiant de l'ordonnance (édition / suppression).</summary>
    public long PrescriptionId { get; init; }

    /// <summary>Identifiant du client propriétaire (pré-remplissage du formulaire).</summary>
    public long CustomerId { get; init; }

    /// <summary>Date d'émission — date civile (tri décroissant hérité du repository).</summary>
    /// <remarks>
    /// P4-5D : <see cref="DateOnly"/> et non <c>DateTime</c> — une date civile n'est pas un instant, et
    /// la convertir en UTC pouvait la décaler d'un jour (ADR-PROD-DB-004 §2.4, §5 décision 7).
    /// </remarks>
    public DateOnly IssueDate { get; init; }

    /// <summary>Nom du médecin prescripteur.</summary>
    public string? DoctorName { get; init; }

    // ---- Œil droit (OD) ----

    /// <summary>Sphère OD.</summary>
    public double? OdSphere { get; init; }

    /// <summary>Cylindre OD.</summary>
    public double? OdCylinder { get; init; }

    /// <summary>Axe OD (0-180°).</summary>
    public int? OdAxis { get; init; }

    /// <summary>Addition OD.</summary>
    public double? OdAddition { get; init; }

    /// <summary>Valeur du prisme OD.</summary>
    public double? OdPrismValue { get; init; }

    /// <summary>Base du prisme OD.</summary>
    public PrismBase? OdPrismBase { get; init; }

    /// <summary>Acuité visuelle OD.</summary>
    public string? OdVisualAcuity { get; init; }

    // ---- Œil gauche (OG) ----

    /// <summary>Sphère OG.</summary>
    public double? OgSphere { get; init; }

    /// <summary>Cylindre OG.</summary>
    public double? OgCylinder { get; init; }

    /// <summary>Axe OG (0-180°).</summary>
    public int? OgAxis { get; init; }

    /// <summary>Addition OG.</summary>
    public double? OgAddition { get; init; }

    /// <summary>Valeur du prisme OG.</summary>
    public double? OgPrismValue { get; init; }

    /// <summary>Base du prisme OG.</summary>
    public PrismBase? OgPrismBase { get; init; }

    /// <summary>Acuité visuelle OG.</summary>
    public string? OgVisualAcuity { get; init; }

    /// <summary>Notes complémentaires de l'ordonnance.</summary>
    public string? Notes { get; init; }
}
