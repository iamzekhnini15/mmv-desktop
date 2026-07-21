using FluentValidation;
using MMV.Domain.Entities;

namespace MMV.Domain.Validators;

/// <summary>
/// Règles de validation d'un fournisseur (P3-9). Jusqu'ici <see cref="Supplier"/> était la seule entité de
/// référence de P3 <b>sans aucun validateur</b> : un nom vide, un nom de 5 000 caractères ou un e-mail
/// syntaxiquement absurde étaient persistés sans le moindre filet (audit P3-9 §6, §16 — vérifié empiriquement sur
/// une vraie base SQLite).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi la longueur est une vraie règle ici.</b> <c>HasMaxLength(200)</c> dans
/// <c>SupplierConfiguration</c> n'impose <b>rien</b> en SQLite : la colonne générée est <c>TEXT</c>, sans
/// <c>CHECK</c> ni <c>VARCHAR(n)</c>. Ces longueurs n'étaient donc que de la documentation. Les
/// <c>MaximumLength</c> ci-dessous les rendent réellement effectives pour la première fois, côté Application —
/// plus strictes que la base, jamais plus permissives, donc compatibles avec un futur provider serveur
/// (cf. <c>adr-prod-db-001</c>).
/// </para>
/// <para>
/// <b>E-mail facultatif mais valide s'il est renseigné.</b> Calque exact du précédent
/// <see cref="CustomerValidator"/> : <c>EmailAddress()</c> de FluentValidation, aucune regex maison. Portée réelle
/// énoncée sans la surestimer : le mode par défaut (<c>AspNetCoreCompatible</c>) vérifie la présence d'un unique
/// <c>@</c> non terminal — il attrape les fautes de frappe grossières, pas les adresses exotiques. C'est
/// exactement le niveau de garantie déjà accepté pour <see cref="Customer"/>.
/// </para>
/// <para>
/// <b>Aucune unicité</b> (nom, e-mail, téléphone, code de référence) : aucune preuve métier n'existe dans le
/// dépôt, et l'inventer bloquerait des cas légitimes (deux agences d'un même groupe, un standard partagé). Report
/// explicite — audit P3-9 §10.
/// </para>
/// <para>
/// Ce validateur suppose une entrée <b>déjà normalisée</b> (<c>SupplierInputNormalizer</c>, couche Application).
/// Précision, car l'audit P3-9 §9.5 se trompait sur ce point : <c>" a@b.fr "</c> n'échoue <b>pas</b>
/// <c>EmailAddress()</c> — le mode <c>AspNetCoreCompatible</c> tolère les espaces environnants (prouvé par
/// <c>SupplierValidatorTests</c>). La normalisation n'est donc pas nécessaire pour éviter un faux rejet ; elle
/// l'est pour ne pas persister d'espaces parasites, pour unifier <c>""</c> et <c>null</c>, et pour que les bornes
/// de longueur portent sur le contenu réel.
/// </para>
/// </remarks>
public class SupplierValidator : AbstractValidator<Supplier>
{
    /// <summary>Message stable : nom absent (y compris espaces seuls, qui deviennent vides après normalisation).</summary>
    public const string NameRequiredMessage = "Le nom du fournisseur est obligatoire.";

    /// <summary>Message stable : nom au-delà de la longueur déclarée par le schéma.</summary>
    public const string NameTooLongMessage = "Le nom du fournisseur ne peut pas dépasser 200 caractères.";

    /// <summary>Message stable : e-mail renseigné mais syntaxiquement invalide.</summary>
    public const string EmailInvalidMessage = "L'adresse e-mail du fournisseur n'est pas valide.";

    /// <summary>Message stable : e-mail au-delà de la longueur maximale RFC 5321.</summary>
    public const string EmailTooLongMessage = "L'adresse e-mail du fournisseur ne peut pas dépasser 254 caractères.";

    /// <summary>Message stable : téléphone trop long (aucun format n'est imposé — audit §20 🟡-4).</summary>
    public const string PhoneTooLongMessage = "Le téléphone du fournisseur ne peut pas dépasser 20 caractères.";

    /// <summary>Message stable : adresse trop longue (chaîne libre, non structurée).</summary>
    public const string AddressTooLongMessage = "L'adresse du fournisseur ne peut pas dépasser 500 caractères.";

    /// <summary>Message stable : code de référence trop long (aucune unicité, aucune normalisation de casse).</summary>
    public const string ReferenceCodeTooLongMessage = "Le code de référence du fournisseur ne peut pas dépasser 50 caractères.";

    public SupplierValidator()
    {
        RuleFor(s => s.Name)
            .NotEmpty().WithMessage(NameRequiredMessage)
            .MaximumLength(200).WithMessage(NameTooLongMessage);

        RuleFor(s => s.ContactEmail)
            .EmailAddress().WithMessage(EmailInvalidMessage)
            .MaximumLength(254).WithMessage(EmailTooLongMessage)
            .When(s => !string.IsNullOrWhiteSpace(s.ContactEmail));

        RuleFor(s => s.Phone)
            .MaximumLength(20).WithMessage(PhoneTooLongMessage)
            .When(s => !string.IsNullOrWhiteSpace(s.Phone));

        RuleFor(s => s.Address)
            .MaximumLength(500).WithMessage(AddressTooLongMessage)
            .When(s => !string.IsNullOrWhiteSpace(s.Address));

        RuleFor(s => s.ReferenceCode)
            .MaximumLength(50).WithMessage(ReferenceCodeTooLongMessage)
            .When(s => !string.IsNullOrWhiteSpace(s.ReferenceCode));
    }
}
