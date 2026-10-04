using FluentAssertions;
using MMV.Domain.Entities;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Search;

/// <summary>
/// N8 — divergence <c>lower()</c> verrouillée côté PostgreSQL (ADR-PROD-DB-006 §5.9, X6). SQLite ne replie que
/// l'ASCII ; PostgreSQL replie selon le <c>LC_CTYPE</c> de la base, donc aussi les majuscules accentuées.
/// La divergence est ACTÉE : la recherche remonte davantage de résultats sur PostgreSQL. Elle reste sensible
/// aux accents (aucun <c>unaccent</c>). Le résultat dépend de la locale du serveur : elle est relevée et citée.
/// </summary>
public class LowerSearchTests : PostgreSqlTestBase
{
    [PostgreSqlFact]
    public async Task N8_customer_search_folds_ascii_and_accented_capitals_but_not_accents()
    {
        await using (var context = NewContext())
        {
            context.Customers.AddRange(
                new Customer { FirstName = "ÉLODIE", LastName = "DUPONT" },
                new Customer { FirstName = "Elodie", LastName = "Martin" });
            await context.SaveChangesAsync();
        }

        var ctype = await DatabaseCtypeAsync();
        await using var read = NewContext();
        var customers = new CustomerRepository(read);

        (await customers.SearchByNameAsync("dupont")).Select(c => c.LastName).Should().Equal("DUPONT");
        (await customers.SearchByNameAsync("élodie")).Select(c => c.LastName)
            .Should().Equal(new[] { "DUPONT" }, $"lower('É') = 'é' sous LC_CTYPE={ctype} (SQLite ne le replierait pas)");
        (await customers.SearchByNameAsync("elodie")).Select(c => c.LastName)
            .Should().Equal(new[] { "Martin" }, "aucune insensibilité aux accents n'est promise");
    }

    [PostgreSqlFact]
    public async Task N8_product_search_folds_accented_capitals_on_active_products()
    {
        await using (var context = NewContext())
        {
            var supplierId = await Arrange.SupplierAsync(context);
            await Arrange.ProductAsync(context, supplierId, reference: "A", name: "MONTURE ÉTÉ");
            await Arrange.ProductAsync(context, supplierId, reference: "B", name: "Monture hiver");
        }

        var ctype = await DatabaseCtypeAsync();
        await using var read = NewContext();

        (await new ProductRepository(read).SearchByNameAsync("été")).Select(p => p.Name)
            .Should().Equal(new[] { "MONTURE ÉTÉ" }, $"LC_CTYPE={ctype}");
    }

    private async Task<string> DatabaseCtypeAsync() =>
        (await SchemaCatalog.QueryAsync(Database, "SELECT datctype FROM pg_database WHERE datname = current_database()")).Single();
}
