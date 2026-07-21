using FluentAssertions;
using MMV.Domain.Entities;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ValidatorTests;

/// <summary>
/// P3-9 — Règles de validation du fournisseur. Avant P3-9, <see cref="Supplier"/> n'avait <b>aucun</b> validateur :
/// chacun des cas « refusé » ci-dessous était persisté sans le moindre filet (audit §16, vérifié empiriquement sur
/// une vraie base SQLite). Ces tests verrouillent la règle, ses messages stables, et — tout aussi important — ce
/// que la règle <b>n'impose pas</b>.
/// </summary>
public sealed class SupplierValidatorTests
{
    private static readonly SupplierValidator Validator = new();

    private static Supplier Valid() => new()
    {
        Name = "Essilor France",
        ContactEmail = "contact@essilor.fr",
        Phone = "0102030405",
        Address = "12 rue de la Paix, 75002 Paris",
        ReferenceCode = "VP001",
    };

    /// <summary>E-mail syntaxiquement valide d'une longueur exacte donnée (partie locale ajustée).</summary>
    private static string EmailOfLength(int totalLength)
    {
        const string domain = "@example.com";
        return new string('a', totalLength - domain.Length) + domain;
    }

    // ------------------------------------------------------------------
    // Nominal
    // ------------------------------------------------------------------

    [Fact]
    public void FournisseurNominal_EstValide()
        => Validator.Validate(Valid()).IsValid.Should().BeTrue();

    // ------------------------------------------------------------------
    // Nom — obligatoire et borné
    // ------------------------------------------------------------------

    [Fact]
    public void NomVide_EstRefuse()
    {
        var result = Validator.Validate(new Supplier { Name = string.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.NameRequiredMessage);
    }

    [Fact]
    public void NomEspacesSeuls_UneFoisNormalise_EstRefuse()
    {
        // Le normaliseur (couche Application) transforme "   " en "" ; c'est cette valeur-là qui arrive ici. La
        // chaîne d'espaces satisfaisait NOT NULL en base et était réellement persistée avant P3-9.
        var result = Validator.Validate(new Supplier { Name = "   ".Trim() });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.NameRequiredMessage);
    }

    [Fact]
    public void NomDe200Caracteres_EstAccepte()
    {
        var supplier = Valid();
        supplier.Name = new string('N', 200);

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void NomDe201Caracteres_EstRefuse()
    {
        // HasMaxLength(200) n'impose RIEN en SQLite (colonne TEXT sans CHECK) : cette limite devient réellement
        // effective pour la première fois, côté Application.
        var supplier = Valid();
        supplier.Name = new string('N', 201);

        var result = Validator.Validate(supplier);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.NameTooLongMessage);
    }

    // ------------------------------------------------------------------
    // E-mail — facultatif, mais valide s'il est renseigné
    // ------------------------------------------------------------------

    [Fact]
    public void EmailNull_EstAccepte()
    {
        var supplier = Valid();
        supplier.ContactEmail = null;

        Validator.Validate(supplier).IsValid.Should().BeTrue("l'e-mail est facultatif : la colonne est nullable");
    }

    [Fact]
    public void EmailVide_EstTraiteCommeAbsent()
    {
        // Le normaliseur produit null ; la garde .When(!IsNullOrWhiteSpace) rend la règle robuste même si une
        // chaîne vide atteignait le validateur par un autre chemin.
        var supplier = Valid();
        supplier.ContactEmail = string.Empty;

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmailValide_EstAccepte()
    {
        var supplier = Valid();
        supplier.ContactEmail = "achats@zeiss.fr";

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmailInvalide_EstRefuse()
    {
        var supplier = Valid();
        supplier.ContactEmail = "pas-un-email";

        var result = Validator.Validate(supplier);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.EmailInvalidMessage);
    }

    [Fact]
    public void EmailEntoureDEspaces_EstAccepteAvantCommeApresNormalisation()
    {
        // CORRECTION D'UNE AFFIRMATION DE L'AUDIT (§9.5). L'audit prédisait que, sans Trim préalable,
        // " a@b.fr " ÉCHOUERAIT EmailAddress() et qu'une saisie bénigne deviendrait donc une erreur bloquante.
        // C'est FAUX pour ce dépôt, et ce test le prouve : FluentValidation 11 utilise par défaut le mode
        // EmailValidationMode.AspNetCoreCompatible, qui se réduit à « exactement un @, ni en première ni en
        // dernière position ». Les espaces environnants ne le gênent pas.
        //
        // La normalisation reste néanmoins nécessaire — mais pour les VRAIES raisons, pas celle-là : ne pas
        // persister d'espaces parasites, unifier "" et null en une seule représentation de « non renseigné », et
        // mesurer les bornes de longueur sur le contenu réel.
        var supplier = Valid();

        supplier.ContactEmail = " contact@essilor.fr ";
        Validator.Validate(supplier).IsValid.Should().BeTrue("le mode AspNetCoreCompatible est permissif");

        supplier.ContactEmail = " contact@essilor.fr ".Trim();
        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void LaPorteeReelleDeEmailAddress_EstEnonceeSansLaSurestimer()
    {
        // Honnêteté sur le niveau de garantie réellement obtenu : la règle attrape les fautes de frappe
        // grossières (aucun @), pas les adresses syntaxiquement exotiques. C'est exactement le niveau déjà
        // accepté pour Customer depuis P3-1 — prétendre davantage surestimerait la protection.
        var supplier = Valid();

        supplier.ContactEmail = "pas-un-email";
        Validator.Validate(supplier).IsValid.Should().BeFalse("une adresse sans @ est bien rejetée");

        supplier.ContactEmail = "a@b";
        Validator.Validate(supplier).IsValid.Should().BeTrue("aucun contrôle de domaine ou de TLD n'est effectué");
    }

    [Fact]
    public void EmailDe254Caracteres_ValideSyntaxiquement_EstAccepte()
    {
        var supplier = Valid();
        supplier.ContactEmail = EmailOfLength(254);

        supplier.ContactEmail.Should().HaveLength(254);
        Validator.Validate(supplier).IsValid.Should().BeTrue("254 est la longueur maximale RFC 5321");
    }

    [Fact]
    public void EmailDe255Caracteres_EstRefuse()
    {
        var supplier = Valid();
        supplier.ContactEmail = EmailOfLength(255);

        var result = Validator.Validate(supplier);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.EmailTooLongMessage);
    }

    // ------------------------------------------------------------------
    // Téléphone / adresse / code de référence — bornes seules
    // ------------------------------------------------------------------

    [Fact]
    public void TelephoneDe20Caracteres_EstAccepte()
    {
        var supplier = Valid();
        supplier.Phone = new string('0', 20);

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TelephoneDe21Caracteres_EstRefuse()
    {
        var supplier = Valid();
        supplier.Phone = new string('0', 21);

        var result = Validator.Validate(supplier);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.PhoneTooLongMessage);
    }

    [Fact]
    public void AucunFormatDeTelephone_NEstImpose()
    {
        // Aucune règle métier ni pays de référence n'est documenté dans le dépôt : imposer un format serait une
        // invention. Report explicite (audit §20 🟡-4, R8).
        var supplier = Valid();
        supplier.Phone = "n'importe quoi";

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AdresseDe500Caracteres_EstAcceptee()
    {
        var supplier = Valid();
        supplier.Address = new string('A', 500);

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AdresseDe501Caracteres_EstRefusee()
    {
        var supplier = Valid();
        supplier.Address = new string('A', 501);

        var result = Validator.Validate(supplier);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.AddressTooLongMessage);
    }

    [Fact]
    public void CodeDeReferenceDe50Caracteres_EstAccepte()
    {
        var supplier = Valid();
        supplier.ReferenceCode = new string('R', 50);

        Validator.Validate(supplier).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CodeDeReferenceDe51Caracteres_EstRefuse()
    {
        var supplier = Valid();
        supplier.ReferenceCode = new string('R', 51);

        var result = Validator.Validate(supplier);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == SupplierValidator.ReferenceCodeTooLongMessage);
    }

    [Fact]
    public void ChampsOptionnelsTousNulls_SontAcceptes()
    {
        var result = Validator.Validate(new Supplier { Name = "Minimal" });

        result.IsValid.Should().BeTrue("seul le nom est obligatoire");
    }

    // ------------------------------------------------------------------
    // Ce que la règle n'impose PAS — verrouillé aussi
    // ------------------------------------------------------------------

    [Fact]
    public void AucuneRegleDUnicite_NEstImposeeParLeValidateur()
    {
        // Deux fournisseurs strictement identiques (nom, e-mail, téléphone, code) sont tous deux VALIDES. Aucune
        // preuve métier n'autorise à bloquer un nom partagé (deux agences d'un groupe) ni un standard commun ;
        // l'unicité serait inventée. Report explicite — audit §10.7-8, R3.
        var first = Valid();
        var second = Valid();

        Validator.Validate(first).IsValid.Should().BeTrue();
        Validator.Validate(second).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AucuneNormalisationDeCasse_NEstAppliquee()
    {
        // Le validateur ne mute jamais l'entité, et aucun ToUpperInvariant n'est appliqué au code de référence ni
        // à l'e-mail : la saisie de l'utilisateur est conservée telle quelle.
        var supplier = Valid();
        supplier.ContactEmail = "Contact@Essilor.FR";
        supplier.ReferenceCode = "vp001";

        Validator.Validate(supplier).IsValid.Should().BeTrue();
        supplier.ContactEmail.Should().Be("Contact@Essilor.FR");
        supplier.ReferenceCode.Should().Be("vp001");
    }
}
