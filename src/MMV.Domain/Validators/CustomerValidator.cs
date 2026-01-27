using FluentValidation;
using MMV.Domain.Entities;

namespace MMV.Domain.Validators;

/// <summary>
/// Règles de validation pour un client.
/// </summary>
public class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(c => c.FirstName)
            .NotEmpty().WithMessage("Le prénom est requis.")
            .MaximumLength(100);

        RuleFor(c => c.LastName)
            .NotEmpty().WithMessage("Le nom est requis.")
            .MaximumLength(100);

        RuleFor(c => c.Email)
            .EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email))
            .MaximumLength(200);

        RuleFor(c => c.Phone)
            .MaximumLength(20)
            .When(c => !string.IsNullOrWhiteSpace(c.Phone));

        RuleFor(c => c.SocialSecurityNumber)
            .MaximumLength(30);
    }
}
