using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Data.Time;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D — l'invariant temporel <b>tel que la base l'applique réellement</b>, sur SQLite, sans serveur
/// (ADR-PROD-DB-004 §5, invariant 1 et décision 4).
///
/// <para>
/// <see cref="UtcDateTimeConverterTests"/> vérifie ce que le modèle <i>déclare</i> ; ces tests-ci
/// vérifient ce qui se passe <i>à l'écriture et à la relecture</i>. C'est la distinction que P4-5C posait
/// déjà : un modèle correct n'est pas une base correcte.
/// </para>
///
/// <para>
/// <b>Ce que ces tests NE prouvent PAS.</b> Ils ne prouvent rien de PostgreSQL. La fidélité réelle contre
/// Npgsql — écriture, relecture, <c>Kind</c>, ordre chronologique — est l'obligation <b>T7</b>, portée par
/// la suite d'intégration de <b>P4-5F</b> (ADR-PROD-DB-008). Leur valeur est ailleurs : ils montrent que
/// la faute que PostgreSQL refuserait en production est <b>déjà refusée ici</b>.
/// </para>
/// </summary>
public sealed class UtcPersistenceRoundTripTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpticDbContext> _options;

    public UtcPersistenceRoundTripTests()
    {
        // Connexion maintenue ouverte : une base SQLite en mémoire disparaît avec sa dernière connexion.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OpticDbContext>().UseSqlite(_connection).Options;

        using var context = new OpticDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private OpticDbContext NewContext() => new(_options);

    // ---------------------------------------------------------------------- écriture : ce qui est refusé

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task UneEcritureNonUtc_EchoueExplicitement(DateTimeKind kind)
    {
        using var context = NewContext();
        context.Notifications.Add(new Notification
        {
            Type = "LowStock",
            Title = "Titre",
            Message = "Message",
            CreatedAt = new DateTime(2026, 9, 18, 14, 30, 0, kind)
        });

        var act = () => context.SaveChangesAsync();

        // EF enveloppe l'erreur du convertisseur dans DbUpdateException : la CAUSE doit rester lisible,
        // sinon l'échec ne désigne pas le défaut qu'il révèle.
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerExceptionExactly<NonUtcDateTimeException>();
    }

    [Fact]
    public async Task UneEcritureNonUtc_NePersisteRien()
    {
        // Le refus doit être TOTAL. Une ligne à moitié écrite serait pire que la valeur fausse qu'on refuse.
        using (var context = NewContext())
        {
            context.Notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Refusée",
                Message = "Message",
                CreatedAt = new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Local)
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        using var verify = NewContext();
        (await verify.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UnParametreDeRequeteNonUtc_EchoueAussi()
    {
        // Cas que l'ADR désigne nommément (§2.2) : la comparaison d'OrderRepository est TRADUITE EN SQL.
        // Le convertisseur s'applique donc aussi aux PARAMÈTRES — un filtre en heure locale contre une
        // colonne UTC ne « marche presque » plus, il échoue.
        using var context = NewContext();
        var localBound = new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Local);

        var act = () => context.Orders.Where(o => o.OrderDate < localBound).ToListAsync();

        await act.Should().ThrowAsync<NonUtcDateTimeException>();
    }

    // ---------------------------------------------------------------------- écriture : ce qui est accepté

    [Fact]
    public async Task UnInstantUtc_EstAccepte_EtRelituIdentique_AvecKindUtc()
    {
        var instant = new DateTime(2026, 9, 18, 14, 30, 45, DateTimeKind.Utc);

        using (var context = NewContext())
        {
            context.Notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Acceptée",
                Message = "Message",
                CreatedAt = instant
            });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        var stored = await verify.Notifications.SingleAsync();

        stored.CreatedAt.Should().Be(instant);
        stored.CreatedAt.Kind.Should().Be(DateTimeKind.Utc,
            "SQLite rendrait Unspecified sans la branche lecture du convertisseur — c'est précisément la "
            + "divergence de Kind entre providers que l'ADR supprime");
    }

    [Fact]
    public async Task UnInstantNullable_SuitLaMemeRegle()
    {
        // Les 8 colonnes d'instants nullables sont couvertes par le MÊME convertisseur : EF applique un
        // convertisseur déclaré pour T aux propriétés T? — vérifié plutôt que supposé.
        var resolved = new DateTime(2026, 9, 18, 16, 0, 0, DateTimeKind.Utc);

        using (var context = NewContext())
        {
            context.Notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Résolue",
                Message = "Message",
                CreatedAt = resolved.AddHours(-2),
                ResolvedAt = resolved
            });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        var stored = await verify.Notifications.SingleAsync();

        stored.ResolvedAt!.Value.Should().Be(resolved);
        stored.ResolvedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task UnInstantNull_ResteNull()
    {
        using (var context = NewContext())
        {
            context.Notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Active",
                Message = "Message",
                CreatedAt = new DateTime(2026, 9, 18, 14, 0, 0, DateTimeKind.Utc)
            });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        (await verify.Notifications.SingleAsync()).ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task LOrdreChronologique_EstPreserveEnBase()
    {
        // Le motif métier que le lot protège : dans une base partagée, l'ordre des faits doit survivre à
        // l'aller-retour. Avec des instants tous UTC, le tri SQL est celui du temps réel.
        var reference = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        using (var context = NewContext())
        {
            context.Notifications.AddRange(
                Notification("Troisième", reference.AddHours(2)),
                Notification("Première", reference),
                Notification("Deuxième", reference.AddHours(1)));
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        var titles = await verify.Notifications
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Title)
            .ToListAsync();

        titles.Should().Equal("Première", "Deuxième", "Troisième");

        static Notification Notification(string title, DateTime createdAt) => new()
        {
            Type = "LowStock",
            Title = title,
            Message = "Message",
            CreatedAt = createdAt
        };
    }

    // ---------------------------------------------------------------------- dates civiles

    [Fact]
    public async Task UneDateCivile_EstStockeeEtReluteSansDecalage()
    {
        // Le bogue corrigé par la décision 7 : en DateTime, cette date pouvait devenir le 14 après un
        // aller-retour par UTC. En DateOnly, aucune conversion n'est possible — donc aucun décalage.
        var birth = new DateOnly(1985, 3, 15);

        using (var context = NewContext())
        {
            context.Customers.Add(new Customer { FirstName = "Marie", LastName = "Dupont", BirthDate = birth });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        (await verify.Customers.SingleAsync()).BirthDate.Should().Be(birth);
    }

    [Fact]
    public async Task UneDateCivile_EstStockeeAuFormatYyyyMmDd()
    {
        // Constat de FORMAT, lu en SQL brut. C'est ce format qui change pour les bases existantes — le type
        // de colonne SQLite, lui, reste TEXT — et c'est pour cela qu'EF ne génère aucune migration alors
        // qu'une reprise de données est indispensable (ADR-PROD-DB-004 §7.2, obligations T5/T8).
        using (var context = NewContext())
        {
            context.Customers.Add(new Customer
            {
                FirstName = "Marie",
                LastName = "Dupont",
                BirthDate = new DateOnly(1985, 3, 15)
            });
            await context.SaveChangesAsync();
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT \"BirthDate\" FROM \"Customers\"";

        ((string)command.ExecuteScalar()!).Should().Be("1985-03-15");
    }

    [Fact]
    public async Task UneDateCivileNullable_PeutResterNulle()
    {
        using (var context = NewContext())
        {
            context.Customers.Add(new Customer { FirstName = "Sans", LastName = "Naissance" });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        (await verify.Customers.SingleAsync()).BirthDate.Should().BeNull();
    }

    [Fact]
    public async Task UneOrdonnance_ConserveSaDateCivile_EtTrieCorrectement()
    {
        using (var context = NewContext())
        {
            var customer = new Customer { FirstName = "Jean", LastName = "Martin" };
            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            context.Prescriptions.AddRange(
                new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 6, 1) },
                new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 1, 15) },
                new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 12, 3) });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        var dates = await verify.Prescriptions
            .OrderByDescending(p => p.IssueDate)
            .Select(p => p.IssueDate)
            .ToListAsync();

        dates.Should().Equal(
            new DateOnly(2026, 12, 3),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 1, 15));
    }

    [Fact]
    public async Task UnFiltreDeDatesCiviles_EstTraduitEnSql_BornesIncluses()
    {
        using (var context = NewContext())
        {
            var customer = new Customer { FirstName = "Jean", LastName = "Martin" };
            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            context.Prescriptions.AddRange(
                new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 1, 1) },
                new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 6, 15) },
                new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 12, 31) });
            await context.SaveChangesAsync();
        }

        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 6, 15);

        using var verify = NewContext();
        var found = await verify.Prescriptions
            .Where(p => p.IssueDate >= start && p.IssueDate <= end)
            .CountAsync();

        // En DateTime, la borne haute portait une heure implicite (minuit) : l'ordonnance du 15 juin aurait
        // été exclue d'un filtre « jusqu'au 15 juin ». La borne civile inclut le jour entier.
        found.Should().Be(2);
    }

    // ------------------------------------------------- horodatage obligatoire (revue architecte P4-5D)

    [Fact]
    public void LHorodatageDUneNotification_EstObligatoireALaCompilation()
    {
        // Avant la revue, l'entité portait « = DateTime.UtcNow » : un défaut correct, mais une lecture
        // directe de l'horloge dans le Domain — donc un précédent. `required` remplace ce défaut par une
        // obligation vérifiée par le COMPILATEUR : un chemin de création qui oublie l'instant ne compile
        // pas. C'est la seule garantie qu'aucun test ne peut contourner.
        typeof(Notification).GetProperty(nameof(Notification.CreatedAt))!
            .GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false)
            .Should().NotBeEmpty("CreatedAt doit rester `required` : c'est ce qui interdit de créer une notification sans instant");
    }

    [Fact]
    public void AucunDefautDHorloge_NeSubsisteSurNotification()
    {
        // `required` n'est vérifié qu'à la compilation : la réflexion le contourne — c'est d'ailleurs
        // ainsi qu'EF matérialise l'entité en relecture. Ce contournement délibéré permet d'observer ce
        // que l'entité vaut SANS horodatage fourni. Si quelqu'un ré-introduisait « = DateTime.UtcNow »,
        // ce test tomberait : c'est lui qui fige la décision d'architecture, pas seulement sa forme.
        var sansHorodatage = (Notification)Activator.CreateInstance(typeof(Notification))!;

        sansHorodatage.CreatedAt.Should().Be(default);
    }

    [Fact]
    public async Task UneNotificationHorodateeParSonCreateur_EstPersistableEtRelueEnUtc()
    {
        // Le pendant du test précédent : l'instant vient de l'appelant (en production, de IClock), et
        // l'aller-retour le restitue à l'identique, en Kind = Utc. L'entité n'a plus besoin d'horloge.
        var instant = new DateTime(2026, 9, 18, 10, 30, 0, DateTimeKind.Utc);

        using (var context = NewContext())
        {
            context.Notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Horodatée par l'appelant",
                Message = "m",
                CreatedAt = instant
            });

            (await context.SaveChangesAsync()).Should().Be(1);
        }

        using var relecture = NewContext();
        var relue = await relecture.Notifications.AsNoTracking().SingleAsync();

        relue.CreatedAt.Should().Be(instant);
        relue.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task LeSeedDeDocumentSequence_EstRelutEnUtc()
    {
        // Les données de seed traversent le convertisseur comme n'importe quelle écriture : un horodatage
        // de seed non-UTC ferait échouer la création même du schéma, donc TOUTE la suite. Le fait que ce
        // test s'exécute prouve déjà l'écriture ; l'assertion couvre la relecture.
        using var context = NewContext();

        var sequences = await context.DocumentSequences.ToListAsync();

        sequences.Should().NotBeEmpty("EnsureCreated applique les compteurs de documents seedés (P2A-1E)");
        sequences.Should().OnlyContain(s => s.UpdatedAt.Kind == DateTimeKind.Utc);
    }

    // ---------------------------------------------------------------------- non-régression fonctionnelle

    [Fact]
    public async Task UnStockMovement_ConserveSonInstantEtSonSigne()
    {
        // Non-régression d'un flux existant non lié au temps : le lot ne doit rien changer d'autre.
        var createdAt = new DateTime(2026, 9, 18, 9, 15, 0, DateTimeKind.Utc);

        using (var context = NewContext())
        {
            var category = new ProductCategory { Name = "Montures" };
            var supplier = new Supplier { Name = "Fournisseur" };
            context.ProductCategories.Add(category);
            context.Suppliers.Add(supplier);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Reference = "REF-1",
                Name = "Monture",
                CategoryId = category.CategoryId,
                SupplierId = supplier.SupplierId,
                EntryDate = createdAt.AddDays(-10)
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.StockMovements.Add(new StockMovement
            {
                ProductId = product.ProductId,
                MovementType = StockMovementType.Out,
                Quantity = -2,
                Reason = "Vente",
                CreatedAt = createdAt
            });
            await context.SaveChangesAsync();
        }

        using var verify = NewContext();
        var movement = await verify.StockMovements.SingleAsync();

        movement.Quantity.Should().Be(-2);
        movement.CreatedAt.Should().Be(createdAt);
        movement.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }
}
