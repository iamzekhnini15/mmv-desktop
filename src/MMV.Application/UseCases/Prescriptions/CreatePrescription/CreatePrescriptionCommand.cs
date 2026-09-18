using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Prescriptions.CreatePrescription;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreatePrescriptionUseCase"/> — « Créer une ordonnance » (P2C-4). Porte les
/// champs saisis dans <c>PrescriptionFormViewModel</c> ; la ViewModel transforme son état (propriétés du formulaire)
/// en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de
/// <c>PrescriptionFormViewModel.SavePrescriptionAsync</c> (qui construisait une <c>Prescription</c> puis appelait
/// directement <c>IPrescriptionRepository.CreateAsync</c> + <c>IUnitOfWork.CommitAsync</c>). Les champs optionnels
/// ne sont <b>pas</b> normalisés (le flux d'origine ne le faisait pas).
/// </remarks>
public sealed class CreatePrescriptionCommand
{
    /// <summary>Identifiant du client propriétaire de l'ordonnance.</summary>
    public long CustomerId { get; init; }

    /// <summary>Date d'émission de l'ordonnance — date civile.</summary>
    /// <remarks>
    /// P4-5D : le commentaire d'origine disait « déjà convertie en UTC par la ViewModel ». C'était
    /// précisément la faute — <c>DateTimeOffset.UtcDateTime</c> sur une date choisie dans un sélecteur
    /// local reculait la date d'un jour dès que l'heure locale était en avance sur UTC
    /// (ADR-PROD-DB-004 §2.4). La ViewModel transmet désormais la date civile telle quelle.
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
