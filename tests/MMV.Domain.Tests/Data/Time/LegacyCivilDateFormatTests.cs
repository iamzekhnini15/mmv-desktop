using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D — <b>la dette de reprise de données, prouvée au lieu d'être supposée</b>
/// (ADR-PROD-DB-004 §7.2 : « le point le plus dangereux de cet ADR » ; obligations T5 et T8).
///
/// <para>
/// <b>Le piège.</b> Le passage de <c>Customer.BirthDate</c> et <c>Prescription.IssueDate</c> à
/// <see cref="DateOnly"/> ne change <b>pas</b> le type de colonne SQLite : c'était <c>TEXT</c>, cela reste
/// <c>TEXT</c>. EF ne détecte donc <b>aucune</b> différence de schéma et ne génère <b>aucune</b> migration.
/// Mais le <b>format</b> des valeurs, lui, change : <c>« yyyy-MM-dd HH:mm:ss »</c> devient
/// <c>« yyyy-MM-dd »</c>. Une base déployée avant P4-5D contient donc des valeurs que le nouveau modèle ne
/// sait plus lire — et rien, dans l'outillage EF, ne le signale.
/// </para>
///
/// <para>
/// <b>Ce que ces tests font.</b> Ils transforment ce silence en <b>fait mesuré et versionné</b>. Ils
/// n'exigent pas que la reprise existe — elle est explicitement hors périmètre de P4-5D — ils exigent
/// qu'elle soit <b>connue</b>. Le jour où la migration de données de P4-7 sera écrite, c'est ici que se
/// lira la spécification de ce qu'elle doit corriger, avec la forme exacte de l'échec qu'elle supprime.
/// </para>
///
/// <para>
/// <b>Aucune migration n'est créée ni modifiée par ce fichier</b> : il lit une base construite en SQL brut.
/// </para>
/// </summary>
public sealed class LegacyCivilDateFormatTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpticDbContext> _options;

    public LegacyCivilDateFormatTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OpticDbContext>().UseSqlite(_connection).Options;

        using var context = new OpticDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Écrit une date civile au format <b>historique</b> — celui qu'un <c>DateTime</c> produisait avant
    /// P4-5D — directement en SQL, sans passer par le modèle EF.
    /// </summary>
    private void InsertLegacyCustomer(string birthDateLiteral)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"BirthDate\", \"IsArchived\", " +
            "\"CreatedAt\", \"UpdatedAt\") VALUES ('Jean', 'Historique', $birthDate, 0, $ts, $ts)";
        command.Parameters.AddWithValue("$birthDate", birthDateLiteral);
        command.Parameters.AddWithValue("$ts", "2026-01-01 00:00:00");
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task UneDateCivileAuFormatHistorique_NEstPasLisible()
    {
        // LE fait à retenir de tout ce fichier. Sans reprise de données, la lecture des clients d'une base
        // existante lève — elle ne renvoie pas une valeur fausse, elle échoue. C'est la moins mauvaise des
        // deux défaillances possibles, mais c'en est une, et elle est totale.
        InsertLegacyCustomer("1985-03-15 00:00:00");

        using var context = new OpticDbContext(_options);
        var act = () => context.Customers.ToListAsync();

        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*not specific to the DateOnly*");
    }

    [Fact]
    public async Task UneDateCivileAuFormatHistoriqueAvecFractionDeSeconde_NEstPasLisibleNonPlus()
    {
        // Le format réellement écrit par le pilote SQLite comportait sept décimales de seconde. La variante
        // compte : une reprise qui ne traiterait que « yyyy-MM-dd HH:mm:ss » laisserait ces lignes cassées.
        InsertLegacyCustomer("1985-03-15 00:00:00.0000000");

        using var context = new OpticDbContext(_options);
        var act = () => context.Customers.ToListAsync();

        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task LaMemeDateAuFormatCivil_EstLueSansPerte()
    {
        // La contrepartie : la reprise attendue est une simple TRONCATURE de la partie horaire. Ce test
        // fixe la forme cible « yyyy-MM-dd » et montre qu'elle suffit — aucune conversion de fuseau n'est
        // requise, et surtout aucune n'est permise (elle décalerait le jour, ADR-PROD-DB-004 §2.4).
        InsertLegacyCustomer("1985-03-15");

        using var context = new OpticDbContext(_options);
        var customer = await context.Customers.SingleAsync();

        customer.BirthDate.Should().Be(new DateOnly(1985, 3, 15));
    }

    [Fact]
    public void LaReprisePeutSecrireEnSqlPur()
    {
        // Preuve de faisabilité pour P4-7, pas la reprise elle-même : la transformation est exprimable en
        // SQLite standard (substr sur les dix premiers caractères), donc sans code applicatif ni relecture
        // ligne à ligne. Aucune migration n'est créée ici — la requête est exécutée sur une base de test.
        InsertLegacyCustomer("1985-03-15 00:00:00.0000000");

        using (var fix = _connection.CreateCommand())
        {
            fix.CommandText =
                "UPDATE \"Customers\" SET \"BirthDate\" = substr(\"BirthDate\", 1, 10) " +
                "WHERE \"BirthDate\" IS NOT NULL AND length(\"BirthDate\") > 10";
            fix.ExecuteNonQuery().Should().Be(1);
        }

        using var context = new OpticDbContext(_options);
        context.Customers.Single().BirthDate.Should().Be(new DateOnly(1985, 3, 15));
    }

    [Fact]
    public async Task UneDateCivileNulle_NEstPasConcerneeParLaReprise()
    {
        // Délimite le périmètre de la reprise : BirthDate est nullable, et une valeur absente n'a pas de
        // format à corriger. Une reprise qui la « corrigerait » en date par défaut inventerait une donnée.
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"IsArchived\", \"CreatedAt\", " +
                "\"UpdatedAt\") VALUES ('Sans', 'Naissance', 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')";
            command.ExecuteNonQuery();
        }

        using var context = new OpticDbContext(_options);
        (await context.Customers.SingleAsync()).BirthDate.Should().BeNull();
    }

    [Fact]
    public async Task LesInstANTS_EuxNeSontPasConcernes_ParLeChangementDeFormat()
    {
        // Contraste essentiel, et rassurant : les 19 colonnes d'INSTANTS gardent exactement le format
        // qu'elles avaient. Le convertisseur validant ne change que le Kind à la relecture, jamais la
        // représentation stockée. La reprise de P4-7 ne concerne donc QUE les deux dates civiles — plus la
        // règle d'interprétation du Kind historique fixée par la décision 8.
        InsertLegacyCustomer("1985-03-15");

        using var context = new OpticDbContext(_options);
        var customer = await context.Customers.SingleAsync();

        customer.CreatedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        customer.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }
}
