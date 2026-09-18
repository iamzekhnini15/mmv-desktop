using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Prescriptions.UpdatePrescription;

/// <summary>
/// Entrée (DTO) du use case <see cref="UpdatePrescriptionUseCase"/> — « Modifier une ordonnance » (P2C-4). Porte
/// l'identifiant de l'ordonnance à modifier ainsi que les champs éditables saisis dans
/// <c>PrescriptionFormViewModel</c> (mode édition).
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Les champs optionnels ne sont <b>pas</b> normalisés (cohérent
/// avec le flux de saisie d'origine).
/// </remarks>
public sealed class UpdatePrescriptionCommand
{
    /// <summary>Identifiant de l'ordonnance à modifier.</summary>
    public long PrescriptionId { get; init; }

    /// <summary>Date d'émission de l'ordonnance — date civile.</summary>
    /// <remarks>
    /// P4-5D : <see cref="DateOnly"/> et non <c>DateTime</c> — une date civile n'est pas un instant, et
    /// la convertir en UTC pouvait la décaler d'un jour (ADR-PROD-DB-004 §2.4, §5 décision 7).
    /// </remarks>
    public DateOnly IssueDate { get; init; }

    /// <summary>Nom du médecin prescripteur.</summary>
    public string? DoctorName { get; init; }

    // ---- Œil droit (OD) ----
    public double? OdSphere { get; init; }
    public double? OdCylinder { get; init; }
    public int? OdAxis { get; init; }
    public double? OdAddition { get; init; }
    public double? OdPrismValue { get; init; }
    public PrismBase? OdPrismBase { get; init; }
    public string? OdVisualAcuity { get; init; }

    // ---- Œil gauche (OG) ----
    public double? OgSphere { get; init; }
    public double? OgCylinder { get; init; }
    public int? OgAxis { get; init; }
    public double? OgAddition { get; init; }
    public double? OgPrismValue { get; init; }
    public PrismBase? OgPrismBase { get; init; }
    public string? OgVisualAcuity { get; init; }

    /// <summary>Notes complémentaires.</summary>
    public string? Notes { get; init; }
}
