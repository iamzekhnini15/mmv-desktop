using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente une ordonnance ophtalmologique d'un client.
/// </summary>
public class Prescription
{
    /// <summary>
    /// Identifiant unique de l'ordonnance.
    /// </summary>
    public long PrescriptionId { get; set; }

    /// <summary>
    /// Identifiant du client propriétaire de l'ordonnance.
    /// </summary>
    public long CustomerId { get; set; }

    /// <summary>
    /// Date d'émission de l'ordonnance.
    /// </summary>
    public DateTime IssueDate { get; set; }

    /// <summary>
    /// Nom du médecin prescripteur.
    /// </summary>
    public string? DoctorName { get; set; }

    // ========== ŒIL DROIT (OD) ==========
    /// <summary>
    /// Sphère OD (dioptrie, valeur négative pour myopie, positive pour hypermétropie).
    /// </summary>
    public double? OdSphere { get; set; }

    /// <summary>
    /// Cylindre OD (correction de l'astigmatisme).
    /// </summary>
    public double? OdCylinder { get; set; }

    /// <summary>
    /// Axe OD (orientation de l'astigmatisme, 0-180 degrés).
    /// </summary>
    public int? OdAxis { get; set; }

    /// <summary>
    /// Addition OD (correction pour la presbytie).
    /// </summary>
    public double? OdAddition { get; set; }

    /// <summary>
    /// Valeur du prisme OD (en dioptries prismatiques).
    /// </summary>
    public double? OdPrismValue { get; set; }

    /// <summary>
    /// Base du prisme OD (orientation).
    /// </summary>
    public PrismBase? OdPrismBase { get; set; }

    /// <summary>
    /// Acuité visuelle OD (format: 10/10, 8/10, etc.).
    /// </summary>
    public string? OdVisualAcuity { get; set; }

    // ========== ŒIL GAUCHE (OG) ==========
    /// <summary>
    /// Sphère OG (dioptrie, valeur négative pour myopie, positive pour hypermétropie).
    /// </summary>
    public double? OgSphere { get; set; }

    /// <summary>
    /// Cylindre OG (correction de l'astigmatisme).
    /// </summary>
    public double? OgCylinder { get; set; }

    /// <summary>
    /// Axe OG (orientation de l'astigmatisme, 0-180 degrés).
    /// </summary>
    public int? OgAxis { get; set; }

    /// <summary>
    /// Addition OG (correction pour la presbytie).
    /// </summary>
    public double? OgAddition { get; set; }

    /// <summary>
    /// Valeur du prisme OG (en dioptries prismatiques).
    /// </summary>
    public double? OgPrismValue { get; set; }

    /// <summary>
    /// Base du prisme OG (orientation).
    /// </summary>
    public PrismBase? OgPrismBase { get; set; }

    /// <summary>
    /// Acuité visuelle OG (format: 10/10, 8/10, etc.).
    /// </summary>
    public string? OgVisualAcuity { get; set; }

    /// <summary>
    /// Notes complémentaires de l'ordonnance.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Date de création de l'enregistrement.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    /// <summary>
    /// Client propriétaire de cette ordonnance.
    /// </summary>
    public virtual Customer Customer { get; set; } = null!;
}
