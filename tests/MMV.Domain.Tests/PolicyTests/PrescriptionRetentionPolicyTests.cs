using FluentAssertions;
using MMV.Domain.Policies;
using Xunit;

namespace MMV.Domain.Tests.PolicyTests;

/// <summary>
/// P3-12 — <see cref="PrescriptionRetentionPolicy"/>, propriétaire Domain de la règle de conservation des
/// ordonnances.
///
/// <para>
/// La politique n'expose délibérément aucun prédicat : la règle est inconditionnelle. Ce qu'il reste à opposer
/// est donc la <b>stabilité</b> et la <b>nature métier</b> du message — deux propriétés dont dépendent
/// directement le use case (qui le lève) et l'UI (qui l'affiche à l'exploitant sans le reformuler).
/// </para>
/// </summary>
public sealed class PrescriptionRetentionPolicyTests
{
    [Fact]
    public void PhysicalDeletionForbiddenMessage_IsStable()
    {
        // Littéral volontairement recopié : un test qui se contenterait de comparer la constante à elle-même
        // n'opposerait rien. C'est la réécriture silencieuse du message qui doit échouer ici.
        PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage.Should().Be(
            "La suppression physique d'une ordonnance est interdite afin de préserver l'historique médical.");
    }

    [Fact]
    public void PhysicalDeletionForbiddenMessage_IsBusinessWording_WithoutTechnicalDetail()
    {
        var message = PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage;

        message.Should().NotBeNullOrWhiteSpace();

        // Un refus métier ne divulgue ni le fournisseur, ni le schéma, ni la mécanique de persistance : le
        // message part vers l'exploitant, pas vers un journal technique.
        message.Should().NotContainAny(
            "SQLite", "SQL", "EntityFramework", "DbContext", "FOREIGN KEY", "constraint", "Exception");
    }
}
