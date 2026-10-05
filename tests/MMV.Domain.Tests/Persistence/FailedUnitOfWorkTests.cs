using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using Xunit;

namespace MMV.Domain.Tests.Persistence;

/// <summary>
/// P4-10 — l'application compose <b>un seul</b> <see cref="OpticDbContext"/> pour toute la session (fournisseur racine,
/// <c>App.axaml.cs</c>). Une unité de travail en échec — annulée en base — ne doit donc jamais rester suivie : sinon le
/// <c>SaveChanges</c> suivant, sans rapport, l'écrirait en silence (vente fantôme, double saisie après « aucune
/// modification n'a été conservée »). Précédent : <c>CreateUserUseCase</c> détachait déjà son entité rejetée.
/// La preuve en panne réseau réelle est dans <c>Resilience/NetworkFaultTests</c> (intégration PostgreSQL).
/// </summary>
public sealed class FailedUnitOfWorkTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly OpticDbContext _context;

    public FailedUnitOfWorkTests()
    {
        _connection.Open();
        _context = new OpticDbContext(new DbContextOptionsBuilder<OpticDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Business_failure_inside_a_transaction_never_resurfaces_in_a_later_unrelated_save()
    {
        var act = () => new EfTransactionRunner(_context).RunAsync(async ct =>
        {
            _context.Sales.Add(new Sale { SaleNumber = "VTE-FANTOME", FinalAmount = 60m });
            await _context.SaveChangesAsync(ct);
            throw new InsufficientStockException(1, 1, 0);
        });
        await act.Should().ThrowAsync<InsufficientStockException>();

        _context.Suppliers.Add(new Supplier { Name = "Opération suivante, sans rapport" });
        await _context.SaveChangesAsync();

        (await _context.Sales.AsNoTracking().CountAsync()).Should().Be(0, "la vente annulée ne doit jamais être réécrite");
        _context.ChangeTracker.Entries<Sale>().Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_standalone_save_never_resurfaces_in_a_later_unrelated_save()
    {
        _context.Products.Add(new Product { Reference = "ORPHELIN", Name = "Sans fournisseur valide", SupplierId = 999_999 });
        var act = () => _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("clé étrangère violée");

        _context.Suppliers.Add(new Supplier { Name = "Opération suivante, sans rapport" });
        await _context.SaveChangesAsync();

        (await _context.Suppliers.AsNoTracking().CountAsync()).Should().Be(1);
        _context.ChangeTracker.Entries<Product>().Should().BeEmpty("l'écriture refusée est abandonnée, pas retentée");
    }

    [Fact]
    public async Task Successful_work_stays_tracked_as_before()
    {
        var supplier = new Supplier { Name = "Conservé" };
        await new EfTransactionRunner(_context).RunAsync(async ct =>
        {
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync(ct);
        });

        _context.Entry(supplier).State.Should().Be(EntityState.Unchanged, "le comportement nominal du suivi est inchangé");
    }
}
