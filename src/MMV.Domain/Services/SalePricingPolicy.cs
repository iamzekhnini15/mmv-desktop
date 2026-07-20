using MMV.Domain.Enums;
using MMV.Domain.Exceptions;

namespace MMV.Domain.Services;

/// <summary>
/// Ligne valide d'une vente, telle que soumise au calcul monétaire (P3-7). Structure d'entrée <b>pure</b> :
/// aucune dépendance à un DTO applicatif, à EF ou à une entité suivie.
/// </summary>
/// <param name="Quantity">Quantité vendue (doit être strictement positive).</param>
/// <param name="UnitPrice">Prix unitaire <b>négocié</b> (doit être ≥ 0 ; la gratuité est un geste commercial légitime).</param>
public readonly record struct SaleLinePricingInput(int Quantity, decimal UnitPrice);

/// <summary>
/// Montant calculé d'une ligne de vente (P3-7). <see cref="LineTotal"/> est la <b>seule</b> source de
/// <c>SaleItem.TotalPrice</c> : cette valeur n'est jamais fournie par l'appelant.
/// </summary>
public readonly record struct SaleLineAmount(int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>
/// Résultat <b>immuable</b> du calcul monétaire d'une vente (P3-7). Toutes les valeurs monétaires persistées par
/// <c>RegisterSaleUseCase</c> proviennent exclusivement d'ici ; aucune n'est reprise de la commande d'entrée.
/// </summary>
public sealed class SalePricing
{
    internal SalePricing(
        IReadOnlyList<SaleLineAmount> lines,
        decimal totalAmount,
        decimal discountAmount,
        decimal finalAmount,
        decimal depositAmount,
        decimal remainingAmount,
        PaymentStatus paymentStatus)
    {
        Lines = lines;
        TotalAmount = totalAmount;
        DiscountAmount = discountAmount;
        FinalAmount = finalAmount;
        DepositAmount = depositAmount;
        RemainingAmount = remainingAmount;
        PaymentStatus = paymentStatus;
    }

    /// <summary>Montant de chaque ligne, dans l'ordre exact des lignes soumises.</summary>
    public IReadOnlyList<SaleLineAmount> Lines { get; }

    /// <summary>Somme des totaux de ligne, avant remise.</summary>
    public decimal TotalAmount { get; }

    /// <summary>Remise globale retenue (bornée : 0 ≤ remise ≤ <see cref="TotalAmount"/>).</summary>
    public decimal DiscountAmount { get; }

    /// <summary>Montant dû après remise (<see cref="TotalAmount"/> − <see cref="DiscountAmount"/>), jamais négatif.</summary>
    public decimal FinalAmount { get; }

    /// <summary>Acompte retenu (borné : 0 ≤ acompte ≤ <see cref="FinalAmount"/>).</summary>
    public decimal DepositAmount { get; }

    /// <summary>Solde restant dû (<see cref="FinalAmount"/> − <see cref="DepositAmount"/>), jamais négatif.</summary>
    public decimal RemainingAmount { get; }

    /// <summary>
    /// Statut de paiement <b>explicite</b>, dérivé des montants calculés — jamais laissé au défaut EF
    /// (<see cref="PaymentStatus.Paid"/>), qui marquait « payée » toute vente à crédit.
    /// </summary>
    public PaymentStatus PaymentStatus { get; }
}

/// <summary>
/// <b>Propriétaire unique</b> des règles de calcul monétaire d'une création de vente (P3-7). Politique Domain
/// <b>pure</b> : aucun dépôt, aucun <c>UnitOfWork</c>, aucun état, aucun effet de bord.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi une source de vérité unique.</b> Avant P3-7, trois formules monétaires coexistaient — celle de
/// <c>SaleFormViewModel</c> (seule réellement exécutée), celle de <c>SaleService</c> (code mort, supprimé par
/// P3-7) et celle du seed de démonstration — tandis que le backend se contentait de <b>recopier</b> les montants
/// fournis par l'appelant. Tout appelant pouvait donc persister une vente incohérente (remise supérieure au
/// total, acompte supérieur au montant final, solde négatif). Le calcul appartient désormais au Domain, et lui
/// seul.
/// </para>
/// <para>
/// <b>Les valeurs calculées remplacent les valeurs fournies.</b> Les montants portés par la commande ne sont ni
/// lus, ni comparés aux montants calculés : aucune tolérance n'a donc à être définie vis-à-vis du stockage
/// SQLite <c>REAL</c> (cf. audit §8.1). Écraser plutôt que comparer supprime entièrement la question.
/// </para>
/// <para>
/// <b>Aucun arrondi.</b> Le calcul est exact en <see cref="decimal"/> ; aucune convention d'arrondi ni de devise
/// n'est introduite (hors périmètre P3-7, cf. audit §22 D2). Le prix unitaire reste le prix <b>négocié</b> fourni
/// par l'appelant : l'aligner sur <c>Product.SalePrice</c> serait un changement métier non prouvé (remises,
/// promotions, prix négociés).
/// </para>
/// </remarks>
public static class SalePricingPolicy
{
    // Messages métier stables, exposés en constantes pour que les tests — et une future UI — s'y réfèrent sans
    // dupliquer de littéral (patron P3-2B / P3-3B).

    public const string EmptyLinesMessage =
        "Une vente doit contenir au moins une ligne.";

    public const string QuantityMessage =
        "La quantité d'une ligne de vente doit être strictement positive.";

    public const string UnitPriceMessage =
        "Le prix unitaire d'une ligne de vente ne peut pas être négatif.";

    public const string DiscountNegativeMessage =
        "La remise ne peut pas être négative.";

    public const string DiscountAboveTotalMessage =
        "La remise ne peut pas dépasser le montant total de la vente.";

    public const string DepositNegativeMessage =
        "L'acompte ne peut pas être négatif.";

    public const string DepositAboveFinalMessage =
        "L'acompte ne peut pas dépasser le montant final de la vente.";

    public const string AmountOutOfRangeMessage =
        "Les montants de cette vente dépassent les valeurs monétaires admises.";

    /// <summary>
    /// Valide les lignes et les montants d'entrée, puis produit l'intégralité des montants d'une vente.
    /// </summary>
    /// <param name="lines">Lignes de la vente (au moins une).</param>
    /// <param name="discountAmount">Remise globale demandée (entrée métier, bornée ici).</param>
    /// <param name="depositAmount">Acompte versé (entrée métier, borné ici).</param>
    /// <exception cref="BusinessRuleException">
    /// pour toute violation de règle métier — y compris un dépassement de capacité <see cref="decimal"/>, converti
    /// en erreur métier stable afin qu'aucune <see cref="OverflowException"/> brute ne remonte à l'appelant.
    /// </exception>
    public static SalePricing Calculate(
        IReadOnlyList<SaleLinePricingInput>? lines,
        decimal discountAmount,
        decimal depositAmount)
    {
        if (lines is null || lines.Count == 0)
            throw new BusinessRuleException(EmptyLinesMessage);

        // Les bornes de signe sont opposées AVANT tout calcul : une remise négative n'a pas à être additionnée
        // pour être refusée, et le message reste celui de la règle violée (jamais un message de dépassement).
        if (discountAmount < 0)
            throw new BusinessRuleException(DiscountNegativeMessage);

        if (depositAmount < 0)
            throw new BusinessRuleException(DepositNegativeMessage);

        try
        {
            var amounts = new SaleLineAmount[lines.Count];
            decimal totalAmount = 0m;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (line.Quantity <= 0)
                    throw new BusinessRuleException(QuantityMessage);

                // Le prix ZÉRO reste autorisé (geste commercial légitime) : la borne est « ≥ 0 », jamais « > 0 ».
                if (line.UnitPrice < 0)
                    throw new BusinessRuleException(UnitPriceMessage);

                var lineTotal = line.Quantity * line.UnitPrice;
                amounts[i] = new SaleLineAmount(line.Quantity, line.UnitPrice, lineTotal);
                totalAmount += lineTotal;
            }

            if (discountAmount > totalAmount)
                throw new BusinessRuleException(DiscountAboveTotalMessage);

            var finalAmount = totalAmount - discountAmount;

            if (depositAmount > finalAmount)
                throw new BusinessRuleException(DepositAboveFinalMessage);

            var remainingAmount = finalAmount - depositAmount;

            return new SalePricing(
                amounts,
                totalAmount,
                discountAmount,
                finalAmount,
                depositAmount,
                remainingAmount,
                DerivePaymentStatus(finalAmount, depositAmount, remainingAmount));
        }
        catch (OverflowException)
        {
            // L'arithmétique decimal lève TOUJOURS en dépassement (indépendamment de checked/unchecked). Une
            // quantité et un prix extrêmes ne doivent pas remonter une exception technique : la règle métier est
            // « ce montant n'est pas un montant de vente admissible ».
            throw new BusinessRuleException(AmountOutOfRangeMessage);
        }
    }

    /// <summary>
    /// Dérive le statut de paiement des montants calculés. Explicite en toutes circonstances : le défaut EF
    /// (<see cref="PaymentStatus.Paid"/>) n'est jamais laissé décider — c'est lui qui marquait « payée » toute
    /// vente à crédit (audit §22 C10).
    /// </summary>
    /// <remarks>
    /// Une vente entièrement offerte (<paramref name="finalAmount"/> nul) est <see cref="PaymentStatus.Paid"/> :
    /// il n'y a rien à devoir. <see cref="PaymentStatus.Refunded"/> n'est jamais produit ici — aucun flux de
    /// remboursement n'existe (hors périmètre P3-7).
    /// </remarks>
    public static PaymentStatus DerivePaymentStatus(decimal finalAmount, decimal depositAmount, decimal remainingAmount)
    {
        if (finalAmount == 0m || remainingAmount == 0m)
            return PaymentStatus.Paid;

        return depositAmount == 0m ? PaymentStatus.Pending : PaymentStatus.Partial;
    }
}
