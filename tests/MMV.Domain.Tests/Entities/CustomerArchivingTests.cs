using FluentAssertions;
using MMV.Domain.Entities;
using Xunit;

namespace MMV.Domain.Tests.Entities;

/// <summary>
/// P3-2B — État d'archivage de <see cref="Customer"/>. L'archivage est la contrepartie non destructive du refus
/// de suppression d'un client porteur d'historique : il ne touche à aucune donnée, et il est réversible.
/// L'état ne se modifie que par les méthodes métier <see cref="Customer.Archive"/> /
/// <see cref="Customer.Reactivate"/> (setter privé), toutes deux idempotentes.
/// </summary>
public sealed class CustomerArchivingTests
{
    [Fact]
    public void NewCustomer_IsNotArchived()
    {
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };

        customer.IsArchived.Should().BeFalse("un client nouvellement créé est actif");
    }

    [Fact]
    public void Archive_MarksCustomerAsArchived()
    {
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };

        customer.Archive();

        customer.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Reactivate_MarksCustomerAsActive()
    {
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        customer.Archive();

        customer.Reactivate();

        customer.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Archive_IsIdempotent()
    {
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };

        customer.Archive();
        customer.Archive();

        customer.IsArchived.Should().BeTrue("archiver un client déjà archivé est sans effet");
    }

    [Fact]
    public void Reactivate_IsIdempotent()
    {
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };

        customer.Reactivate();
        customer.Reactivate();

        customer.IsArchived.Should().BeFalse("réactiver un client déjà actif est sans effet");
    }

    [Fact]
    public void ArchiveAndReactivate_KeepNavigationHistoryUntouched()
    {
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        customer.Prescriptions.Add(new Prescription { IssueDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        customer.Sales.Add(new Sale { SaleNumber = "VTE-TEST-0001" });

        customer.Archive();
        customer.Reactivate();

        customer.Prescriptions.Should().HaveCount(1, "l'archivage ne touche jamais à l'historique");
        customer.Sales.Should().HaveCount(1, "l'archivage ne touche jamais à l'historique");
    }
}
