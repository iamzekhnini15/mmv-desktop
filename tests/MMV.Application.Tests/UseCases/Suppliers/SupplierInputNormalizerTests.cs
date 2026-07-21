using FluentAssertions;
using MMV.Application.UseCases.Suppliers.Common;
using MMV.Domain.Entities;
using Xunit;

namespace MMV.Application.Tests.UseCases.Suppliers;

/// <summary>
/// P3-9 — Normalisation d'entrée fournisseur. Composant <b>pur</b> (aucune persistance) : ces tests n'ont besoin
/// d'aucune base. Ils verrouillent le propriétaire <b>unique</b> partagé par <c>CreateSupplierUseCase</c> et
/// <c>UpdateSupplierUseCase</c> — et surtout ce que la normalisation s'interdit de faire.
/// </summary>
public sealed class SupplierInputNormalizerTests
{
    [Fact]
    public void LeNom_EstTrimEtResteNonNullable()
    {
        var result = SupplierInputNormalizer.Normalize("  Essilor  ", null, null, null, null);

        result.Name.Should().Be("Essilor");
    }

    [Fact]
    public void UnNomNull_DevientChaineVide_JamaisNull()
    {
        // Name reste non nullable : la chaîne vide est ensuite refusée explicitement par SupplierValidator, ce qui
        // produit un message métier — bien plus lisible qu'une NullReferenceException.
        SupplierInputNormalizer.Normalize(null, null, null, null, null).Name.Should().Be(string.Empty);
    }

    [Fact]
    public void UnNomEnEspacesSeuls_DevientChaineVide()
        => SupplierInputNormalizer.Normalize("   ", null, null, null, null).Name.Should().Be(string.Empty);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void LesChampsOptionnelsVidesOuBlancs_DeviennentNull(string? blank)
    {
        var result = SupplierInputNormalizer.Normalize("Nom", blank, blank, blank, blank);

        result.ContactEmail.Should().BeNull();
        result.Phone.Should().BeNull();
        result.Address.Should().BeNull();
        result.ReferenceCode.Should().BeNull();
    }

    [Fact]
    public void LesChampsOptionnelsRenseignes_SontTrimes()
    {
        var result = SupplierInputNormalizer.Normalize(
            " Nom ", "  contact@essilor.fr  ", " 0102030405 ", "  12 rue de la Paix  ", "  VP001  ");

        result.ContactEmail.Should().Be("contact@essilor.fr");
        result.Phone.Should().Be("0102030405");
        result.Address.Should().Be("12 rue de la Paix");
        result.ReferenceCode.Should().Be("VP001");
    }

    [Fact]
    public void AucuneCasse_NEstModifiee()
    {
        // Ni ToUpperInvariant sur le code de référence, ni ToLowerInvariant sur l'e-mail : chacun supposerait une
        // règle métier que le dépôt ne prouve nulle part, et modifierait une valeur saisie intentionnellement.
        var result = SupplierInputNormalizer.Normalize("EsSiLoR", "Contact@Essilor.FR", null, null, "vP001");

        result.Name.Should().Be("EsSiLoR");
        result.ContactEmail.Should().Be("Contact@Essilor.FR");
        result.ReferenceCode.Should().Be("vP001");
    }

    [Fact]
    public void AucunNumeroDeTelephone_NEstReformate()
    {
        // Seuls les espaces de bord sont retirés : la structure interne est conservée telle quelle.
        SupplierInputNormalizer.Normalize("Nom", null, "  01 02 03 04 05  ", null, null)
            .Phone.Should().Be("01 02 03 04 05");
    }

    [Fact]
    public void LeCandidatConstruit_EstDetacheEtPorteLesValeursNormalisees()
    {
        var candidate = SupplierInputNormalizer
            .Normalize(" Essilor ", " contact@essilor.fr ", "  ", null, " VP001 ")
            .ToCandidate(supplierId: 42);

        candidate.Should().BeOfType<Supplier>();
        candidate.SupplierId.Should().Be(42);
        candidate.Name.Should().Be("Essilor");
        candidate.ContactEmail.Should().Be("contact@essilor.fr");
        candidate.Phone.Should().BeNull();
        candidate.Address.Should().BeNull();
        candidate.ReferenceCode.Should().Be("VP001");
    }

    [Fact]
    public void ApplyTo_RemplaceLesCinqChamps_SansPatchPartiel()
    {
        // Il n'existe aucune sémantique « champ non fourni = ne pas toucher » : une commande partiellement remplie
        // EFFACE bien les champs optionnels. Comportement historique conservé sciemment (audit §8.7).
        var existing = new Supplier
        {
            SupplierId = 7,
            Name = "Ancien",
            ContactEmail = "ancien@essilor.fr",
            Phone = "0102030405",
            Address = "Ancienne adresse",
            ReferenceCode = "OLD",
        };

        SupplierInputNormalizer.Normalize("Nouveau", null, null, null, null).ApplyTo(existing);

        existing.Name.Should().Be("Nouveau");
        existing.ContactEmail.Should().BeNull();
        existing.Phone.Should().BeNull();
        existing.Address.Should().BeNull();
        existing.ReferenceCode.Should().BeNull();
        existing.SupplierId.Should().Be(7, "la clé n'est jamais touchée par la normalisation");
    }

    [Fact]
    public void ApplyTo_RejetteUneEntiteNulle()
        => ((Action)(() => SupplierInputNormalizer.Normalize("N", null, null, null, null).ApplyTo(null!)))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void LesDeuxChemins_CreationEtModification_ProduisentLesMemesValeurs()
    {
        // Garantie du « propriétaire unique » : Create et Update ne peuvent pas diverger, puisqu'ils appellent
        // littéralement la même fonction.
        var forCreate = SupplierInputNormalizer.Normalize(" A ", " a@b.fr ", "  ", " Adr ", "  ");
        var forUpdate = SupplierInputNormalizer.Normalize(" A ", " a@b.fr ", "  ", " Adr ", "  ");

        forUpdate.Should().Be(forCreate, "le record est comparé par valeur");
    }
}
