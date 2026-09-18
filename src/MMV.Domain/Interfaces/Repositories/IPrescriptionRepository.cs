using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des ordonnances.
/// </summary>
public interface IPrescriptionRepository : IGenericRepository<Prescription, long>
{
    /// <summary>
    /// Récupère toutes les ordonnances d'un client.
    /// </summary>
    Task<IList<Prescription>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ordonnances émises entre deux dates civiles, <b>bornes incluses</b>.
    /// </summary>
    /// <remarks>
    /// P4-5D : les bornes sont des <see cref="DateOnly"/> depuis que <c>Prescription.IssueDate</c> en est
    /// un. En <c>DateTime</c>, la borne haute portait une heure implicite — un filtre « jusqu'au 31 »
    /// excluait donc, en pratique, toutes les ordonnances du 31 après minuit. Le type supprime la
    /// question (ADR-PROD-DB-004 §5, décision 7).
    /// </remarks>
    Task<IList<Prescription>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère la dernière ordonnance d'un client.
    /// </summary>
    Task<Prescription?> GetLatestByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indique si le client possède au moins une ordonnance (P3-2B). Requête d'existence légère : aucune
    /// ordonnance n'est matérialisée, contrairement à <see cref="GetByCustomerIdAsync"/>.
    /// </summary>
    Task<bool> ExistsByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);
}
