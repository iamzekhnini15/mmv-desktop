using FluentValidation;
using MMV.Domain.Entities;

namespace MMV.Domain.Validators;

/// <summary>
/// Règles de validation pour une ordonnance.
/// </summary>
public class PrescriptionValidator : AbstractValidator<Prescription>
{
    public PrescriptionValidator()
    {
        RuleFor(p => p.IssueDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1));

        RuleFor(p => p.OdSphere).InclusiveBetween(-20, 20).When(p => p.OdSphere.HasValue);
        RuleFor(p => p.OdCylinder).InclusiveBetween(-6, 6).When(p => p.OdCylinder.HasValue);
        RuleFor(p => p.OdAxis).InclusiveBetween(0, 180).When(p => p.OdAxis.HasValue);
        RuleFor(p => p.OdAddition).InclusiveBetween(0, 4).When(p => p.OdAddition.HasValue);

        RuleFor(p => p.OgSphere).InclusiveBetween(-20, 20).When(p => p.OgSphere.HasValue);
        RuleFor(p => p.OgCylinder).InclusiveBetween(-6, 6).When(p => p.OgCylinder.HasValue);
        RuleFor(p => p.OgAxis).InclusiveBetween(0, 180).When(p => p.OgAxis.HasValue);
        RuleFor(p => p.OgAddition).InclusiveBetween(0, 4).When(p => p.OgAddition.HasValue);
    }
}
