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

        RuleFor(p => p.PurchasePrice)
            .GreaterThanOrEqualTo(0m).WithMessage("Le prix d'achat ne peut pas être négatif.");

        RuleFor(p => p.SalePrice)
            .GreaterThanOrEqualTo(0m).WithMessage("Le prix de vente ne peut pas être négatif.");

        // Règle métier déjà appliquée par l'UI (P3-4A) : le prix de vente ne descend jamais sous le prix d'achat.
        RuleFor(p => p.SalePrice)
            .GreaterThanOrEqualTo(p => p.PurchasePrice)
            .WithMessage("Le prix de vente ne peut pas être inférieur au prix d'achat.");

        // Prix conseillé négatif refusé uniquement lorsqu'il est renseigné (champ optionnel).
        RuleFor(p => p.RecommendedPrice)
            .GreaterThanOrEqualTo(0m).WithMessage("Le prix conseillé ne peut pas être négatif.")
            .When(p => p.RecommendedPrice.HasValue);

        RuleFor(p => p.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Le stock ne peut pas être négatif.");

        RuleFor(p => p.StockAlertThreshold)
            .GreaterThanOrEqualTo(0);
    }
}
