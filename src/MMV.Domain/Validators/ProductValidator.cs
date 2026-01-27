using FluentValidation;
using MMV.Domain.Entities;

namespace MMV.Domain.Validators;

/// <summary>
/// Règles de validation pour un produit.
/// </summary>
public class ProductValidator : AbstractValidator<Product>
{
    public ProductValidator()
    {
        RuleFor(p => p.Name)
            .NotEmpty().WithMessage("Le nom du produit est requis.")
            .MaximumLength(200);

        RuleFor(p => p.Reference)
            .NotEmpty().WithMessage("La référence est requise.")
            .MaximumLength(100);

        RuleFor(p => p.SalePrice)
            .GreaterThanOrEqualTo(0m);

        RuleFor(p => p.PurchasePrice)
            .GreaterThanOrEqualTo(0m);

        RuleFor(p => p.StockQuantity)
            .GreaterThanOrEqualTo(0);

        RuleFor(p => p.StockAlertThreshold)
            .GreaterThanOrEqualTo(0);
    }
}
