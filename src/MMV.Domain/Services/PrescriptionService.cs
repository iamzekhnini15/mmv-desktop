using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Domain.Services;

/// <summary>
/// Service métier pour la gestion des ordonnances.
/// </summary>
public interface IPrescriptionService
{
    Task<Prescription?> GetPrescriptionAsync(long prescriptionId, CancellationToken cancellationToken = default);
    Task<IList<Prescription>> GetCustomerPrescriptionsAsync(long customerId, CancellationToken cancellationToken = default);
    Task<Prescription?> GetLatestPrescriptionAsync(long customerId, CancellationToken cancellationToken = default);
    Task<Prescription> CreatePrescriptionAsync(Prescription prescription, CancellationToken cancellationToken = default);
    Task UpdatePrescriptionAsync(Prescription prescription, CancellationToken cancellationToken = default);
}

public class PrescriptionService : IPrescriptionService
{
    private readonly IUnitOfWork _unitOfWork;

    public PrescriptionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Prescription?> GetPrescriptionAsync(long prescriptionId, CancellationToken cancellationToken = default)
    {
        if (prescriptionId <= 0) throw new ArgumentException("ID ordonnance invalide.", nameof(prescriptionId));
        return await _unitOfWork.Prescriptions.GetByIdAsync(prescriptionId, cancellationToken);
    }

    public async Task<IList<Prescription>> GetCustomerPrescriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentException("ID client invalide.", nameof(customerId));
        return await _unitOfWork.Prescriptions.GetByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<Prescription?> GetLatestPrescriptionAsync(long customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentException("ID client invalide.", nameof(customerId));
        return await _unitOfWork.Prescriptions.GetLatestByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<Prescription> CreatePrescriptionAsync(Prescription prescription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        
        if (prescription.IssueDate > DateTime.UtcNow.AddDays(1))
            throw new Exceptions.BusinessRuleException("La date d'ordonnance ne peut pas être future.");
        
        await _unitOfWork.Prescriptions.CreateAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return prescription;
    }

    public async Task UpdatePrescriptionAsync(Prescription prescription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        
        var existing = await GetPrescriptionAsync(prescription.PrescriptionId, cancellationToken);
        if (existing == null) throw new Exceptions.EntityNotFoundException(nameof(Prescription), prescription.PrescriptionId);
        
        await _unitOfWork.Prescriptions.UpdateAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
