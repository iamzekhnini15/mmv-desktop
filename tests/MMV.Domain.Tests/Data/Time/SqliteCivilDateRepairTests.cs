using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Data.Time;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D-R — <b>la reprise appliquée à une vraie base</b>, du diagnostic à la relecture par EF.
///
/// <para>
/// Les tests de <see cref="CivilDateFormatTests"/> prouvent la <i>règle</i> ; ceux-ci prouvent la
/// <i>reprise</i> : qu'elle détecte une base ancienne, qu'elle la répare, qu'elle ne touche à rien d'autre,
/// qu'elle est rejouable, et qu'après elle le modèle courant lit ce qu'il ne savait plus lire.
/// </para>
///
/// <para>
/// Chaque base est construite <b>en SQL brut</b>, sous le modèle EF : c'est le seul moyen d'y écrire le
/// format historique, que le modèle courant refuse précisément d'écrire.
/// <b>Aucune migration EF n'est créée ni modifiée.</b>
/// </para>
/// </summary>
public sealed class SqliteCivilDateRepairTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpticDbContext> _options;

    public SqliteCivilDateRepairTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OpticDbContext>().UseSqlite(_connection).Options;

        using var context = new OpticDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private OpticDbContext CreateContext() => new(_options);

    /// <summary>Insère un client avec une date de naissance au format brut voulu (null accepté).</summary>
    private long InsertCustomer(string lastName, string? birthDateLiteral)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"BirthDate\", \"IsArchived\", " +
            "\"CreatedAt\", \"UpdatedAt\") VALUES ('Jean', $lastName, $birthDate, 0, $ts, $ts); " +
            "SELECT last_insert_rowid()";
        command.Parameters.AddWithValue("$lastName", lastName);
        command.Parameters.AddWithValue("$birthDate", (object?)birthDateLiteral ?? DBNull.Value);
        command.Parameters.AddWithValue("$ts", "2026-01-01 00:00:00");
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Insère une ordonnance avec une date d'émission au format brut voulu (colonne NOT NULL).</summary>
    private long InsertPrescription(long customerId, string issueDateLiteral)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"Prescriptions\" (\"CustomerId\", \"IssueDate\", \"CreatedAt\") " +
            "VALUES ($customerId, $issueDate, $ts); SELECT last_insert_rowid()";
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$issueDate", issueDateLiteral);
        command.Parameters.AddWithValue("$ts", "2026-01-01 00:00:00");
        return (long)command.ExecuteScalar()!;
    }

    private string? ReadRawBirthDate(long customerId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT CAST(\"BirthDate\" AS TEXT) FROM \"Customers\" WHERE \"CustomerId\" = $id";
        command.Parameters.AddWithValue("$id", customerId);
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : (string)value;
    }

    // =====================================================================
    // (1) Détection — comment sait-on qu'une base doit être reprise ?
    // =====================================================================

    [Fact]
    public void UneBaseNeuve_NeNecessiteAucuneReprise()
    {
        using var context = CreateContext();

        var audit = new SqliteCivilDateFormatVerifier().Audit(context);

        audit.RequiresRepair.Should().BeFalse("une base créée par le modèle courant écrit déjà « yyyy-MM-dd »");
        audit.HasUnrepairableValues.Should().BeFalse();
        audit.Columns.Should().HaveCount(2).And.OnlyContain(c => c.Exists);
    }

    [Fact]
    public void UneBaseAnterieureAP45D_EstDetectee_SansLeverNiRienModifier()
    {
        // Le signal de détection, sur les DEUX colonnes concernées. Le diagnostic doit fonctionner là où EF
        // échoue : il lit le TEXT brut, sous le modèle.
        var customerId = InsertCustomer("Historique", "1985-03-15 00:00:00");
        InsertPrescription(customerId, "2026-01-20 14:05:00.1234567");

        using var context = CreateContext();
        var audit = new SqliteCivilDateFormatVerifier().Audit(context);

        audit.RequiresRepair.Should().BeTrue();
        audit.TotalRewriteCount.Should().Be(2);
        audit.TotalUnreadableCount.Should().Be(2, "les deux valeurs sont illisibles par le modèle courant");
        audit.HasUnrepairableValues.Should().BeFalse();

        ReadRawBirthDate(customerId).Should().Be("1985-03-15 00:00:00", "le diagnostic ne modifie rien");
    }

    [Fact]
    public void LeDiagnostic_DistingueChaqueEtat()
    {
        // Le rapport doit être exploitable par un humain : il ne suffit pas de dire « il y a un problème ».
        InsertCustomer("Canonique", "1985-03-15");
        InsertCustomer("Historique", "1985-03-15 00:00:00");
        InsertCustomer("Iso", "1985-03-15T08:00:00");
        InsertCustomer("Intacte", " 1985-03-15");
        InsertCustomer("Sans", null);

        using var context = CreateContext();
        var customers = new SqliteCivilDateFormatVerifier().Audit(context)
            .Columns.Single(c => c.Table == "Customers");

        customers.TotalRows.Should().Be(5);
        customers.NullCount.Should().Be(1);
        customers.CanonicalCount.Should().Be(1);
        customers.RepairableCount.Should().Be(1);
        customers.NormalizableCount.Should().Be(1);
        customers.ReadableLeftAsIsCount.Should().Be(1);
        customers.UnrepairableCount.Should().Be(0);
    }

    // =====================================================================
    // (2) Reprise — de l'échec de lecture à la lecture correcte
    // =====================================================================

    [Fact]
    public void ApresReprise_LeModeleCourantRelitCeQuIlNeSavaitPlusLire()
    {
        // Le test qui ferme la boucle ouverte par P4-5D : avant, EF lève ; après, EF lit la bonne date.
        var customerId = InsertCustomer("Historique", "1985-03-15 00:00:00");

        using (var before = CreateContext())
        {
            var act = () => before.Customers.ToList();
            act.Should().Throw<FormatException>("c'est l'échec constaté par P4-5D");
        }

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context);
        }

        using var after = CreateContext();
        after.Customers.Single(c => c.CustomerId == customerId)
            .BirthDate.Should().Be(new DateOnly(1985, 3, 15));
    }

    [Theory]
    [InlineData("1985-03-15 00:00:00", 1985, 3, 15)]
    [InlineData("1985-03-15 12:30:45", 1985, 3, 15)]
    [InlineData("1985-03-15 12:30:45.1234567", 1985, 3, 15)]
    [InlineData("1985-03-15 23:59:59", 1985, 3, 15)]     // pas de bascule au 16.
    [InlineData("1985-03-15 00:00:00.0000000", 1985, 3, 15)]
    [InlineData("1900-01-01 00:00:00", 1900, 1, 1)]
    [InlineData("2099-06-01 00:00:00", 2099, 6, 1)]
    [InlineData("2024-02-29 00:00:00", 2024, 2, 29)]
    public void ChaqueFormatHistorique_EstRepris_SansDecalageDeJour(string legacy, int y, int m, int d)
    {
        var customerId = InsertCustomer("Reprise", legacy);

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context).TotalTruncatedRows.Should().Be(1);
        }

        ReadRawBirthDate(customerId).Should().Be(new DateOnly(y, m, d).ToString("yyyy-MM-dd"));

        using var after = CreateContext();
        after.Customers.Single(c => c.CustomerId == customerId)
            .BirthDate.Should().Be(new DateOnly(y, m, d));
    }

    [Fact]
    public void LesOrdonnances_SontReprises_CommeLesClients()
    {
        // IssueDate est NOT NULL et porte une donnée de santé horodatée : elle relève du même traitement.
        var customerId = InsertCustomer("Porteur", "1985-03-15");
        var prescriptionId = InsertPrescription(customerId, "2026-01-20 14:05:00");

        using (var context = CreateContext())
        {
            var report = new SqliteCivilDateRepairService().Repair(context);
            report.Columns.Single(c => c.Table == "Prescriptions").TruncatedRows.Should().Be(1);
        }

        using var after = CreateContext();
        after.Prescriptions.Single(p => p.PrescriptionId == prescriptionId)
            .IssueDate.Should().Be(new DateOnly(2026, 1, 20));
    }

    [Fact]
    public void LaReprise_NeToucheNiAuxNullsNiAuxValeursDejaSaines()
    {
        // Périmètre : une reprise qui « corrigerait » un NULL inventerait une date de naissance.
        var nullId = InsertCustomer("Sans", null);
        var canonicalId = InsertCustomer("Canonique", "1985-03-15");
        var leftAsIsId = InsertCustomer("Intacte", " 1985-03-15");
        InsertCustomer("Historique", "1970-07-04 00:00:00");

        using (var context = CreateContext())
        {
            var report = new SqliteCivilDateRepairService().Repair(context);
            report.TotalRewrittenRows.Should().Be(1, "une seule ligne méritait d'être réécrite");
        }

        ReadRawBirthDate(nullId).Should().BeNull();
        ReadRawBirthDate(canonicalId).Should().Be("1985-03-15");
        ReadRawBirthDate(leftAsIsId).Should().Be(" 1985-03-15",
            "une valeur lisible que la troncature changerait de sens n'est jamais réécrite");
    }

    [Fact]
    public void LaReprise_NeToucheAucuneColonneDInstant()
    {
        // Contraste essentiel : les 19 colonnes d'instants gardent leur format. La reprise ne concerne QUE
        // les deux dates civiles ; déborder serait une régression de P4-5D.
        var customerId = InsertCustomer("Historique", "1985-03-15 00:00:00");

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context);
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT CAST(\"CreatedAt\" AS TEXT) FROM \"Customers\" WHERE \"CustomerId\" = $id";
        command.Parameters.AddWithValue("$id", customerId);
        command.ExecuteScalar().Should().Be("2026-01-01 00:00:00",
            "un instant n'est pas une date civile : son format stocké est inchangé");
    }

    [Fact]
    public void UneValeurLisibleNonCanonique_EstRemiseEnForme_SansChangerDeDate()
    {
        var customerId = InsertCustomer("Iso", "1985-03-15T08:30:00");

        using (var context = CreateContext())
        {
            var report = new SqliteCivilDateRepairService().Repair(context);
            report.TotalNormalizedRows.Should().Be(1);
            report.TotalTruncatedRows.Should().Be(0, "cette valeur n'était pas cassée, seulement non canonique");
        }

        ReadRawBirthDate(customerId).Should().Be("1985-03-15");
    }

    // =====================================================================
    // (3) Valeurs non réparables — signalées, jamais devinées
    // =====================================================================

    [Fact]
    public void UneValeurIncomprehensible_EstSignaleeNommement_EtLaisseeIntacte()
    {
        var badId = InsertCustomer("Illisible", "pas-une-date");
        var goodId = InsertCustomer("Historique", "1985-03-15 00:00:00");

        using var context = CreateContext();
        var audit = new SqliteCivilDateFormatVerifier().Audit(context);

        audit.HasUnrepairableValues.Should().BeTrue();
        var offender = audit.UnrepairableValues.Single();
        offender.Table.Should().Be("Customers");
        offender.Column.Should().Be("BirthDate");
        offender.RowId.Should().Be(badId, "le support doit pouvoir retrouver la ligne exacte");
        offender.RawValue.Should().Be("pas-une-date");

        // Le service rend compte ; il n'arbitre pas et ne bloque pas de lui-même.
        var report = new SqliteCivilDateRepairService().Repair(context);
        report.TotalUnrepairableRows.Should().Be(1);
        ReadRawBirthDate(badId).Should().Be("pas-une-date", "jamais transformée");
        ReadRawBirthDate(goodId).Should().Be("1985-03-15");
    }

    [Fact]
    public void UneDateInexistante_NEstJamaisRecalee_EnBase()
    {
        // Le garde-fou contre date() de SQLite, vérifié de bout en bout : '2026-02-30' resterait tel quel,
        // et ne deviendrait JAMAIS '2026-03-02' en base.
        var badId = InsertCustomer("Impossible", "2026-02-30");

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context)
                .TotalUnrepairableRows.Should().Be(1);
        }

        ReadRawBirthDate(badId).Should().Be("2026-02-30");
    }

    // =====================================================================
    // (4) Idempotence et rejouabilité
    // =====================================================================

    [Fact]
    public void DeuxExecutionsSuccessives_DonnentLeMemeResultat()
    {
        var customerId = InsertCustomer("Historique", "1985-03-15 12:30:45");
        var prescriptionCustomer = InsertCustomer("Porteur", "1970-07-04 00:00:00");
        InsertPrescription(prescriptionCustomer, "2026-01-20 14:05:00");

        using (var context = CreateContext())
        {
            var first = new SqliteCivilDateRepairService().Repair(context);
            first.Applied.Should().BeTrue();
            first.TotalRewrittenRows.Should().Be(3);
        }

        var afterFirstRun = ReadRawBirthDate(customerId);

        using (var context = CreateContext())
        {
            var second = new SqliteCivilDateRepairService().Repair(context);
            second.Applied.Should().BeFalse("la deuxième exécution n'a plus rien à réécrire");
            second.TotalRewrittenRows.Should().Be(0);
            second.AuditBefore.RequiresRepair.Should().BeFalse();
        }

        ReadRawBirthDate(customerId).Should().Be(afterFirstRun).And.Be("1985-03-15");

        // Troisième passage : toujours stable. La reprise peut donc être rejouée sans précaution.
        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context).TotalRewrittenRows.Should().Be(0);
        }

        ReadRawBirthDate(customerId).Should().Be("1985-03-15");
    }

    [Fact]
    public void UneSimulation_NEcritRien_MaisRendLeMemeCompte()
    {
        // Permet au support de mesurer l'ampleur d'une reprise avant de l'autoriser.
        var customerId = InsertCustomer("Historique", "1985-03-15 00:00:00");

        using (var context = CreateContext())
        {
            var dryRun = new SqliteCivilDateRepairService().Repair(context, dryRun: true);
            dryRun.DryRun.Should().BeTrue();
            dryRun.Applied.Should().BeFalse();
            dryRun.TotalRewrittenRows.Should().Be(1, "le compte annoncé est celui qui sera réalisé");
        }

        ReadRawBirthDate(customerId).Should().Be("1985-03-15 00:00:00", "rien n'a été écrit");

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context).TotalRewrittenRows.Should().Be(1);
        }

        ReadRawBirthDate(customerId).Should().Be("1985-03-15");
    }

    [Fact]
    public void UneBaseVolumineuse_EstReprise_IntegralementEtSansMelange()
    {
        // Vérifie que la reprise ligne à ligne associe bien À CHAQUE ligne sa propre valeur — une erreur
        // d'appariement donnerait à tous les clients la même date de naissance.
        // L'heure varie avec le jour, mais reste une heure que DateTime a pu écrire (Q-C2) : « day % 24 ».
        var expected = new Dictionary<long, string>();
        for (var day = 1; day <= 28; day++)
        {
            var id = InsertCustomer($"Client{day}", $"1985-03-{day:D2} {day % 24:D2}:15:00");
            expected[id] = $"1985-03-{day:D2}";
        }

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context).TotalTruncatedRows.Should().Be(28);
        }

        foreach (var (id, value) in expected)
        {
            ReadRawBirthDate(id).Should().Be(value);
        }

        using var after = CreateContext();
        after.Customers.Should().HaveCount(28);
    }

    // =====================================================================
    // (5) P4-5D-R Phase C — D-B1 appliquée à une vraie base
    // =====================================================================

    /// <summary>Photographie brute (TEXT) de toutes les colonnes d'une table, sauf une, ligne par ligne.</summary>
    private List<string> SnapshotAllColumnsExcept(string table, string excludedColumn)
    {
        var columns = new List<string>();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                if (!string.Equals(name, excludedColumn, StringComparison.Ordinal))
                {
                    columns.Add(name);
                }
            }
        }

        using var select = _connection.CreateCommand();
        select.CommandText =
            $"SELECT {string.Join(", ", columns.Select(c => $"CAST(\"{c}\" AS TEXT)"))} FROM \"{table}\" ORDER BY rowid";
        using var rows = select.ExecuteReader();
        var snapshot = new List<string>();
        while (rows.Read())
        {
            snapshot.Add(string.Join(" | ", columns.Select((c, i) => $"{c}={(rows.IsDBNull(i) ? "<null>" : rows.GetString(i))}")));
        }

        return snapshot;
    }

    private static string Sha256(string value)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public void LesValeursAvecDecalage_SontReprisesEnBase_SansConversion()
    {
        // T-B2 de bout en bout, sur les deux colonnes : écrit en base, relu par EF, jamais décalé.
        var utcId = InsertCustomer("Utc", "1985-03-15T12:30:45Z");
        var eastId = InsertCustomer("Est", "1985-03-15T12:30:45+01:00");
        var westId = InsertCustomer("Ouest", "1985-03-15T23:30:00-05:00");
        var prescriptionId = InsertPrescription(utcId, "2026-01-20T23:30:00-05:00");

        using (var context = CreateContext())
        {
            var report = new SqliteCivilDateRepairService().Repair(context);
            report.TotalTruncatedRows.Should().Be(4);
            report.TotalUnrepairableRows.Should().Be(0);
        }

        ReadRawBirthDate(utcId).Should().Be("1985-03-15");
        ReadRawBirthDate(eastId).Should().Be("1985-03-15");
        ReadRawBirthDate(westId).Should().Be("1985-03-15", "jamais le 16, jour UTC de cet instant");

        using var after = CreateContext();
        after.Customers.Select(c => c.BirthDate).ToList()
            .Should().HaveCount(3).And.OnlyContain(d => d == new DateOnly(1985, 3, 15));
        after.Prescriptions.Single(p => p.PrescriptionId == prescriptionId)
            .IssueDate.Should().Be(new DateOnly(2026, 1, 20), "jamais le 21");
    }

    [Fact]
    public void UnSuffixeInconnu_EstSignaleEnBase_EtJamaisTronque()
    {
        // T-B3 de bout en bout. Avant D-B1, « 1985-03-15garbage » devenait silencieusement « 1985-03-15 ».
        var garbageId = InsertCustomer("Suffixe", "1985-03-15garbage");
        var noSecondsId = InsertCustomer("SansSecondes", "1985-03-15 12:30");
        var legacyId = InsertCustomer("Historique", "1985-03-15 12:30:45");

        using var context = CreateContext();
        var audit = new SqliteCivilDateFormatVerifier().Audit(context);
        audit.UnrepairableValues.Select(v => v.RowId).Should().BeEquivalentTo(new[] { garbageId, noSecondsId });

        var report = new SqliteCivilDateRepairService().Repair(context);
        report.TotalUnrepairableRows.Should().Be(2);
        report.TotalTruncatedRows.Should().Be(1);

        ReadRawBirthDate(garbageId).Should().Be("1985-03-15garbage", "une valeur d'origine inconnue n'est jamais transformée");
        ReadRawBirthDate(noSecondsId).Should().Be("1985-03-15 12:30");
        ReadRawBirthDate(legacyId).Should().Be("1985-03-15", "la forme historique voisine reste reprise");
    }

    [Fact]
    public void LaRepriseDesFormesAvecDecalage_EstIdempotente_EtLesRefusStables()
    {
        var offsetId = InsertCustomer("Utc", "1985-03-15T12:30:45.1234567Z");
        var garbageId = InsertCustomer("Suffixe", "1985-03-15garbage");

        using (var context = CreateContext())
        {
            var first = new SqliteCivilDateRepairService().Repair(context);
            first.TotalTruncatedRows.Should().Be(1);
            first.TotalUnrepairableRows.Should().Be(1);
        }

        using (var context = CreateContext())
        {
            var second = new SqliteCivilDateRepairService().Repair(context);
            second.TotalRewrittenRows.Should().Be(0, "la deuxième exécution n'a plus rien à réécrire");
            second.Applied.Should().BeFalse();
            second.TotalUnrepairableRows.Should().Be(1, "le refus est le même à chaque passage");
        }

        ReadRawBirthDate(offsetId).Should().Be("1985-03-15");
        ReadRawBirthDate(garbageId).Should().Be("1985-03-15garbage");
    }

    [Fact]
    public void LaReprise_NeModifieAucuneAutreColonne_NiAucunInstant()
    {
        // Les colonnes d'instants portent ici les MÊMES formes que celles que D-B1 autorise pour les dates
        // civiles (suffixe Z, décalage, fractions) : si la reprise débordait de son périmètre, c'est ici qu'elle
        // les tronquerait. Toutes les colonnes autres que BirthDate / IssueDate doivent rester identiques.
        long customerId;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"BirthDate\", \"IsArchived\", " +
                "\"CreatedAt\", \"UpdatedAt\") VALUES ('Jean', 'Instants', '1985-03-15T12:30:45Z', 0, " +
                "'2026-01-01T10:00:00Z', '2026-01-02 11:30:00.1234567+01:00'); SELECT last_insert_rowid()";
            customerId = (long)command.ExecuteScalar()!;
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO \"Prescriptions\" (\"CustomerId\", \"IssueDate\", \"CreatedAt\") " +
                "VALUES ($customerId, '2026-01-20 14:05:00Z', '2026-01-20T23:30:00-05:00')";
            command.Parameters.AddWithValue("$customerId", customerId);
            command.ExecuteNonQuery();
        }

        var customersBefore = SnapshotAllColumnsExcept("Customers", "BirthDate");
        var prescriptionsBefore = SnapshotAllColumnsExcept("Prescriptions", "IssueDate");

        using (var context = CreateContext())
        {
            new SqliteCivilDateRepairService().Repair(context).TotalTruncatedRows.Should().Be(2);
        }

        SnapshotAllColumnsExcept("Customers", "BirthDate").Should().Equal(customersBefore);
        SnapshotAllColumnsExcept("Prescriptions", "IssueDate").Should().Equal(prescriptionsBefore);
        customersBefore.Single().Should().Contain("CreatedAt=2026-01-01T10:00:00Z")
            .And.Contain("UpdatedAt=2026-01-02 11:30:00.1234567+01:00");
        prescriptionsBefore.Single().Should().Contain("CreatedAt=2026-01-20T23:30:00-05:00");
    }

    // =====================================================================
    // (6) P4-5D-R Phase C — D-B3 : une valeur refusée se désigne, elle ne s'écrit pas
    // =====================================================================

    [Fact]
    public void UneValeurNonReparable_SeJournalise_SansSaValeurBrute()
    {
        var badCustomerId = InsertCustomer("Illisible", "15/03/1985");
        var holderId = InsertCustomer("Porteur", "1985-03-15");
        var badPrescriptionId = InsertPrescription(holderId, "");

        using var context = CreateContext();
        var offenders = new SqliteCivilDateFormatVerifier().Audit(context).UnrepairableValues;

        var customer = offenders.Single(o => o.Table == "Customers");
        customer.ToString().Should().Be(
            $"Customers.BirthDate CustomerId={badCustomerId} BirthDateHash={Sha256("15/03/1985")}");
        customer.ToString().Should().NotContain("15/03/1985");
        customer.RawValue.Should().Be("15/03/1985",
            "la valeur brute reste disponible EN MÉMOIRE pour le diagnostic par code (dryRun)");

        // Vecteur de référence publié du SHA-256 de la chaîne vide : l'empreinte est reproductible par le support
        // avec n'importe quel outil standard, pas seulement par ce code.
        var prescription = offenders.Single(o => o.Table == "Prescriptions");
        prescription.RawValueHash.Should().Be(
            "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        prescription.ToString().Should().Be(
            $"Prescriptions.IssueDate PrescriptionId={badPrescriptionId} IssueDateHash={prescription.RawValueHash}");
    }

    // =====================================================================
    // (7) P4-5D-R Phase C2 — Q-C2 appliquée à une vraie base
    // =====================================================================

    [Fact]
    public void UneHeureHorsBornes_EstSignaleeEnBase_EtJamaisTronquee()
    {
        // Q-C2 de bout en bout, sur les deux colonnes. Ni le vérificateur ni le service de reprise n'ont été modifiés :
        // ils consomment Classify, et la règle leur parvient sans second moteur.
        var midnightNextDayId = InsertCustomer("VingtQuatre", "1985-03-15 24:00:00");
        var badMinuteId = InsertCustomer("Minute", "1985-03-15 12:60:00");
        var lateId = InsertCustomer("Soir", "1985-03-15 23:59:59");
        var eastId = InsertCustomer("Est", "1985-03-15 00:00:00+01:00");
        var badPrescriptionId = InsertPrescription(lateId, "2026-01-20T12:00:60Z");
        var goodPrescriptionId = InsertPrescription(lateId, "2026-01-20T23:59:59-05:00");

        using (var context = CreateContext())
        {
            var audit = new SqliteCivilDateFormatVerifier().Audit(context);
            audit.UnrepairableValues.Select(v => (v.Table, v.RowId)).Should().BeEquivalentTo(new[]
            {
                ("Customers", midnightNextDayId), ("Customers", badMinuteId), ("Prescriptions", badPrescriptionId),
            });
            audit.UnrepairableValues.Should().OnlyContain(v => !v.ToString().Contains(v.RawValue),
                "D-B3 vaut aussi pour ce nouveau motif de refus");

            var report = new SqliteCivilDateRepairService().Repair(context);
            report.TotalUnrepairableRows.Should().Be(3);
            report.TotalTruncatedRows.Should().Be(3);
        }

        ReadRawBirthDate(midnightNextDayId).Should().Be("1985-03-15 24:00:00", "une heure impossible n'est jamais tronquée");
        ReadRawBirthDate(badMinuteId).Should().Be("1985-03-15 12:60:00");
        ReadRawBirthDate(lateId).Should().Be("1985-03-15", "23:59:59 reste repris, et reste le 15");
        ReadRawBirthDate(eastId).Should().Be("1985-03-15", "jamais le 14, jour UTC de cet instant");

        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT \"PrescriptionId\", \"IssueDate\" FROM \"Prescriptions\" ORDER BY \"PrescriptionId\"";
        using var reader = command.ExecuteReader();
        var issueDates = new Dictionary<long, string>();
        while (reader.Read())
        {
            issueDates[reader.GetInt64(0)] = reader.GetString(1);
        }

        issueDates[badPrescriptionId].Should().Be("2026-01-20T12:00:60Z");
        issueDates[goodPrescriptionId].Should().Be("2026-01-20", "jamais le 21");
    }
}
