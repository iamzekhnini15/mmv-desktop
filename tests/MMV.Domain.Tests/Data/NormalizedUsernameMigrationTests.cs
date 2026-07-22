using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P3-10 — Migration <c>AddNormalizedUsernameAndSecureLocalUsers</c> sur de <b>vraies</b> bases SQLite jetables :
/// base neuve, adoption d'une base historique valide, et refus <b>sûr</b> des données héritées que la
/// normalisation ne peut pas reproduire à l'identique.
/// </summary>
/// <remarks>
/// Principe repris de P3-4B : <b>exact ou échec</b>. Le backfill doit produire exactement
/// <c>UserIdentityPolicy.NormalizeUsername</c> pour chaque login hérité, sinon avorter sans rien modifier —
/// aucune fusion de comptes, aucun renommage inventé, aucune ligne supprimée, aucune inscription dans
/// <c>__EFMigrationsHistory</c>.
/// </remarks>
public sealed class NormalizedUsernameMigrationTests : IDisposable
{
    private const string NormalizedIndexName = "idx_users_normalized_username_unique";
    private const string LegacyIndexName = "idx_users_username_unique";

    private readonly string _workDirectory;

    public NormalizedUsernameMigrationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p310-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
        => new(new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options);

    // ------------------------------------------------------------------ Outils d'inspection physique

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string databasePath, string sql)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static List<(string Name, bool NotNull)> Columns(string databasePath, string table)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        var columns = new List<(string, bool)>();
        while (reader.Read()) columns.Add((reader.GetString(1), reader.GetInt32(3) != 0));
        return columns;
    }

    /// <summary>
    /// Valeur physique de <c>dflt_value</c> (PRAGMA table_info, colonne 4) pour une colonne donnée. <c>null</c>
    /// signifie « aucun défaut » — c'est la seule preuve fiable ; le SQL généré par la migration ne suffit pas,
    /// car SQLite peut conserver un DEFAULT transitoire dans le schéma physique final.
    /// </summary>
    private static string? ColumnDefault(string databasePath, string table, string column)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column)
            {
                return reader.IsDBNull(4) ? null : reader.GetString(4);
            }
        }
        throw new InvalidOperationException($"Colonne '{column}' introuvable sur '{table}'.");
    }

    private static (bool Exists, bool Unique, string Sql) IndexInfo(string databasePath, string indexName)
    {
        var sql = Scalar(databasePath, $"SELECT sql FROM sqlite_master WHERE type='index' AND name='{indexName}'") as string;
        if (string.IsNullOrEmpty(sql)) return (false, false, string.Empty);
        return (true, sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase), sql);
    }

    /// <summary>
    /// Migrations inscrites. Une base dont l'adoption a été refusée n'a <b>aucune</b> table d'historique : ce cas
    /// est traité comme « aucune migration inscrite », qui est précisément la garantie recherchée.
    /// </summary>
    private static List<string> AppliedMigrations(string databasePath)
    {
        using var connection = Open(databasePath);

        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
            if (Convert.ToInt64(exists.ExecuteScalar()!) == 0) return new List<string>();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    private static long UserCount(string databasePath)
        => Convert.ToInt64(Scalar(databasePath, "SELECT COUNT(*) FROM \"Users\"")!);

    /// <summary>
    /// Construit une base au niveau <b>P3-9</b> (juste avant P3-10) : toutes les migrations sauf la dernière,
    /// appliquées réellement. C'est le point de départ honnête pour éprouver l'adoption et le backfill.
    /// </summary>
    private string CreateDatabaseAtP39(string fileName)
    {
        var dbPath = PathFor(fileName);
        using (var context = CreateContext(dbPath))
        {
            var all = context.Database.GetMigrations().ToList();
            var previous = all[^2];
            all[^1].Should().EndWith("_AddNormalizedUsernameAndSecureLocalUsers",
                "ce test suppose que P3-10 est la dernière migration");

            context.GetService<IMigrator>().Migrate(previous);
        }
        SqliteConnection.ClearAllPools();
        return dbPath;
    }

    private static void InsertLegacyUser(SqliteConnection connection, string username)
        => Exec(connection,
            "INSERT INTO \"Users\" (\"Username\", \"PasswordHash\", \"FirstName\", \"LastName\", \"Role\", \"IsActive\", \"CreatedAt\") " +
            $"VALUES ('{username}', 'hash', 'F', 'L', 'Optician', 1, '2026-01-01 00:00:00')");

    private static Exception? TryMigrate(string dbPath)
    {
        try
        {
            using var context = CreateContext(dbPath);
            context.Database.Migrate();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    // ================================================================== Base neuve

    [Fact]
    public void FreshDatabase_HasNormalizedColumn_AndUniqueIndex_AndNoLegacyUniqueIndex()
    {
        var dbPath = PathFor("fresh.db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        var columns = Columns(dbPath, "Users");
        columns.Should().Contain(c => c.Name == "NormalizedUsername");
        columns.Single(c => c.Name == "NormalizedUsername").NotNull
            .Should().BeTrue("la colonne est obligatoire");
        columns.Single(c => c.Name == "Username").NotNull.Should().BeTrue();

        var index = IndexInfo(dbPath, NormalizedIndexName);
        index.Exists.Should().BeTrue();
        index.Unique.Should().BeTrue();
        index.Sql.Should().Contain("NormalizedUsername");
        index.Sql.Should().NotContainEquivalentOf("WHERE", "l'unicité ne doit pas être partielle");

        IndexInfo(dbPath, LegacyIndexName).Exists
            .Should().BeFalse("l'ancien index sensible à la casse n'est plus la clé métier");

        using var verify = CreateContext(dbPath);
        verify.Database.GetPendingMigrations().Should().BeEmpty();
        verify.Database.HasPendingModelChanges().Should().BeFalse("le modèle EF est aligné sur le schéma");
    }

    /// <summary>
    /// Le défaut vide utilisé pour satisfaire <c>ALTER TABLE ADD COLUMN NOT NULL</c> sous SQLite est
    /// TRANSITOIRE : il ne doit subsister nulle part dans le schéma physique final. Un défaut permanent
    /// transformerait une omission applicative de la colonne en chaîne vide silencieusement acceptée, au lieu
    /// d'un refus <c>NOT NULL</c> — exactement le défaut que la contrainte est censée empêcher.
    /// </summary>
    [Fact]
    public void FreshDatabase_NormalizedUsername_HasNoPermanentDefault()
    {
        var dbPath = PathFor("fresh-no-default.db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        ColumnDefault(dbPath, "Users", "NormalizedUsername").Should().BeNull(
            "le défaut '' de la migration est transitoire (exigence ALTER TABLE SQLite), pas un schéma final");
    }

    /// <summary>
    /// Preuve par SQL brut : une insertion omettant <c>NormalizedUsername</c> doit échouer <c>NOT NULL</c>,
    /// pas être silencieusement acceptée avec une chaîne vide. Un défaut physique résiduel rendrait ce test
    /// faux (aucune exception, une ligne à clé métier vide serait créée).
    /// </summary>
    [Fact]
    public void FreshDatabase_RawInsertOmittingNormalizedUsername_FailsNotNull()
    {
        var dbPath = PathFor("fresh-raw-insert.db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        using var connection = Open(dbPath);
        var act = () => Exec(connection,
            "INSERT INTO \"Users\" (\"Username\", \"PasswordHash\", \"FirstName\", \"LastName\", \"Role\", \"IsActive\", \"CreatedAt\") " +
            "VALUES ('raw.insert', 'hash', 'F', 'L', 'Optician', 1, '2026-01-01 00:00:00')");

        act.Should().Throw<SqliteException>("NormalizedUsername est NOT NULL sans défaut : l'omettre doit échouer");

        Convert.ToInt64(new SqliteCommand("SELECT COUNT(*) FROM \"Users\" WHERE \"Username\" = 'raw.insert'", connection)
            .ExecuteScalar()).Should().Be(0, "aucune ligne ne doit être créée");
    }

    // ================================================================== Adoption historique valide

    [Fact]
    public void HistoricalDatabase_WithMixedCaseLogins_IsBackfilledExactly_WithoutDataLoss()
    {
        var dbPath = CreateDatabaseAtP39("adopt-valid.db");

        using (var connection = Open(dbPath))
        {
            InsertLegacyUser(connection, "admin");
            InsertLegacyUser(connection, "Marie.Optic");
            InsertLegacyUser(connection, "PIERRE.TECH");
        }
        SqliteConnection.ClearAllPools();

        TryMigrate(dbPath).Should().BeNull("aucune collision, aucun caractère hors jeu : la migration passe");

        using var verify = CreateContext(dbPath);
        var users = verify.Users.AsNoTracking().OrderBy(u => u.UserId).ToList();

        users.Should().HaveCount(3, "aucune ligne n'est supprimée ni fusionnée");
        // La forme AFFICHABLE est conservée telle quelle ; seule la clé normalisée est calculée.
        users.Select(u => u.Username).Should()
            .Equal(new[] { "admin", "Marie.Optic", "PIERRE.TECH" });
        users.Select(u => u.NormalizedUsername).Should()
            .Equal(new[] { "admin", "marie.optic", "pierre.tech" });

        // Le backfill SQL reproduit EXACTEMENT la règle applicative.
        foreach (var user in users)
        {
            user.NormalizedUsername.Should().Be(UserIdentityPolicy.NormalizeUsername(user.Username));
        }

        AppliedMigrations(dbPath).Should()
            .Contain(m => m.EndsWith("_AddNormalizedUsernameAndSecureLocalUsers", StringComparison.Ordinal));
        IndexInfo(dbPath, NormalizedIndexName).Unique.Should().BeTrue();
    }

    // ================================================================== Collision historique

    /// <summary>
    /// Deux comptes qui deviendraient le même login : la migration doit ÉCHOUER et ne rien changer. Fusionner
    /// détruirait un historique (ventes, mouvements de stock), renommer inventerait un identifiant que personne
    /// n'a choisi. L'exploitant tranche, puis rejoue.
    /// </summary>
    [Theory]
    [InlineData("admin", "Admin")]
    [InlineData("admin", "ADMIN")]
    [InlineData("Marie.Optic", "marie.optic")]
    public void HistoricalDatabase_WithCaseInsensitiveCollision_FailsSafely(string first, string second)
    {
        var dbPath = CreateDatabaseAtP39($"collision-{first}-{second}.db".Replace('.', '_'));

        using (var connection = Open(dbPath))
        {
            InsertLegacyUser(connection, first);
            InsertLegacyUser(connection, second);
        }
        SqliteConnection.ClearAllPools();

        TryMigrate(dbPath).Should().NotBeNull("une collision insensible à la casse doit être refusée");

        UserCount(dbPath).Should().Be(2, "aucune ligne supprimée, aucune fusion");
        Columns(dbPath, "Users").Should().NotContain(c => c.Name == "NormalizedUsername",
            "la transaction de migration est entièrement annulée");
        IndexInfo(dbPath, NormalizedIndexName).Exists.Should().BeFalse();
        AppliedMigrations(dbPath).Should()
            .NotContain(m => m.EndsWith("_AddNormalizedUsernameAndSecureLocalUsers", StringComparison.Ordinal),
                "une migration échouée ne doit jamais être inscrite");

        // Les valeurs affichables sont intactes : aucune n'a été arbitrairement renommée.
        using var connection2 = Open(dbPath);
        using var command = connection2.CreateCommand();
        command.CommandText = "SELECT \"Username\" FROM \"Users\" ORDER BY \"UserId\"";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        names.Should().Equal(new[] { first, second });
    }

    // ================================================================== Donnée historique invalide

    /// <summary>
    /// Logins que la normalisation SQL ne peut pas reproduire à l'identique, ou qui violent les bornes du
    /// validateur : échec sûr, jamais une adoption silencieuse avec une valeur « approximativement » normalisée.
    /// </summary>
    [Theory]
    [InlineData("", "vide")]
    [InlineData("   ", "espaces seuls")]
    [InlineData("ab", "trop court (< 3)")]
    [InlineData("marie optic", "espace interne")]
    [InlineData("marie@optic", "caractère hors du jeu autorisé")]
    [InlineData("MARIE.ÉLÈVE", "non-ASCII : lower() SQLite ne replierait pas comme ToLowerInvariant")]
    public void HistoricalDatabase_WithInvalidLogin_FailsSafely(string username, string reason)
    {
        var dbPath = CreateDatabaseAtP39($"invalid-{Math.Abs(username.GetHashCode())}.db");

        using (var connection = Open(dbPath))
        {
            InsertLegacyUser(connection, username);
        }
        SqliteConnection.ClearAllPools();

        TryMigrate(dbPath).Should().NotBeNull($"login invalide ({reason}) : la migration doit échouer");

        UserCount(dbPath).Should().Be(1, "aucune ligne supprimée");
        Columns(dbPath, "Users").Should().NotContain(c => c.Name == "NormalizedUsername");
        AppliedMigrations(dbPath).Should()
            .NotContain(m => m.EndsWith("_AddNormalizedUsernameAndSecureLocalUsers", StringComparison.Ordinal));
    }

    [Fact]
    public void HistoricalDatabase_WithTooLongLogin_FailsSafely()
    {
        var dbPath = CreateDatabaseAtP39("invalid-long.db");
        var tooLong = new string('a', UserIdentityPolicy.UsernameMaxLength + 1);

        using (var connection = Open(dbPath))
        {
            InsertLegacyUser(connection, tooLong);
        }
        SqliteConnection.ClearAllPools();

        TryMigrate(dbPath).Should().NotBeNull("SQLite n'impose pas HasMaxLength : la garde doit le faire");

        UserCount(dbPath).Should().Be(1);
        Columns(dbPath, "Users").Should().NotContain(c => c.Name == "NormalizedUsername");
    }

    /// <summary>
    /// Les espaces PÉRIPHÉRIQUES restent acceptés : <c>trim()</c> SQLite et <c>Trim()</c> .NET les retirent
    /// identiquement (U+0020), donc la normalisation reste exacte. Distinguer ce cas de l'espace INTERNE (refusé
    /// ci-dessus) est important : refuser les deux serait inutilement destructeur.
    /// </summary>
    [Fact]
    public void HistoricalDatabase_WithSurroundingSpaces_IsBackfilledExactly()
    {
        var dbPath = CreateDatabaseAtP39("padded.db");

        using (var connection = Open(dbPath))
        {
            InsertLegacyUser(connection, "  Admin  ");
        }
        SqliteConnection.ClearAllPools();

        TryMigrate(dbPath).Should().BeNull();

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.NormalizedUsername.Should().Be("admin");
        user.NormalizedUsername.Should().Be(UserIdentityPolicy.NormalizeUsername(user.Username));
    }

    // ================================================================== Unicité effective après migration

    [Fact]
    public void AfterMigration_UniqueIndex_RejectsCaseVariantInsert()
    {
        var dbPath = CreateDatabaseAtP39("enforce.db");

        using (var connection = Open(dbPath))
        {
            InsertLegacyUser(connection, "admin");
        }
        SqliteConnection.ClearAllPools();

        TryMigrate(dbPath).Should().BeNull();

        using var connection2 = Open(dbPath);
        var act = () => Exec(connection2,
            "INSERT INTO \"Users\" (\"Username\", \"NormalizedUsername\", \"PasswordHash\", \"FirstName\", \"LastName\", \"Role\", \"IsActive\", \"CreatedAt\") " +
            "VALUES ('Admin', 'admin', 'h', 'F', 'L', 'Admin', 1, '2026-01-01 00:00:00')");

        act.Should().Throw<SqliteException>("l'index unique protège réellement l'unicité entre postes");
    }

    // ================================================================== États partiels (adoption)

    /// <summary>
    /// Construit une base <b>historique</b> (schéma du modèle courant, sans <c>__EFMigrationsHistory</c>), puis
    /// applique la mutation demandée pour fabriquer un état partiel.
    /// </summary>
    private string CreateHistoricalDatabase(string fileName, Action<SqliteConnection> mutate)
    {
        var dbPath = PathFor(fileName);
        using (var context = CreateContext(dbPath))
        {
            context.Database.EnsureCreated();
        }
        SqliteConnection.ClearAllPools();

        using (var connection = Open(dbPath))
        {
            mutate(connection);
        }
        SqliteConnection.ClearAllPools();

        return dbPath;
    }

    private static Exception? TryPrepare(string dbPath)
    {
        try
        {
            using var context = CreateContext(dbPath);
            new SqliteDatabaseManager(new MigrationJournal()).PrepareDatabase(context);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Colonne absente / index absent / historique absent : état cohérent d'une base antérieure à P3-10. La
    /// migration doit être <b>exécutée</b> (et non baselinée), sinon l'historique affirmerait une garantie
    /// d'unicité inexistante et le backfill n'aurait jamais lieu.
    /// </summary>
    [Fact]
    public void Adoption_ColumnAndIndexAndHistoryAbsent_ExecutesMigration_AndBackfills()
    {
        var dbPath = CreateHistoricalDatabase("partial-all-absent.db", connection =>
        {
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\"");
            Exec(connection, "ALTER TABLE \"Users\" DROP COLUMN \"NormalizedUsername\"");
            Exec(connection, $"CREATE UNIQUE INDEX \"{LegacyIndexName}\" ON \"Users\" (\"Username\")");
            InsertLegacyUser(connection, "Marie.Optic");
        });

        var journal = new MigrationJournal();
        using (var context = CreateContext(dbPath))
        {
            var result = new SqliteDatabaseManager(journal).PrepareDatabase(context);
            result.WasAdopted.Should().BeTrue();
        }
        SqliteConnection.ClearAllPools();

        journal.Entries.Should().Contain(e => e.Contains("AddNormalizedUsernameAndSecureLocalUsers will be executed"));

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().NormalizedUsername.Should().Be("marie.optic");
        IndexInfo(dbPath, NormalizedIndexName).Unique.Should().BeTrue();
    }

    /// <summary>
    /// Colonne et index exacts présents, historique cohérent : baseline normal, sans rejouer l'ajout de colonne.
    /// </summary>
    [Fact]
    public void Adoption_ColumnAndIndexPresent_BaselinesWithoutReplayingAddColumn()
    {
        var dbPath = CreateHistoricalDatabase("partial-complete.db", _ => { });

        using (var context = CreateContext(dbPath))
        {
            new SqliteDatabaseManager(new MigrationJournal()).PrepareDatabase(context)
                .WasAdopted.Should().BeTrue();
        }
        SqliteConnection.ClearAllPools();

        AppliedMigrations(dbPath).Should()
            .Contain(m => m.EndsWith("_AddNormalizedUsernameAndSecureLocalUsers", StringComparison.Ordinal));
        Columns(dbPath, "Users").Should().Contain(c => c.Name == "NormalizedUsername");
    }

    /// <summary>
    /// Colonne présente mais index absent : état impossible en exécution normale (la migration crée les deux
    /// ensemble). Baseliner marquerait comme acquise une unicité qui n'existe pas physiquement — refus.
    /// </summary>
    [Fact]
    public void Adoption_ColumnPresentButIndexAbsent_IsRefused()
    {
        var dbPath = CreateHistoricalDatabase("partial-no-index.db", connection =>
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\""));

        TryPrepare(dbPath).Should().BeOfType<DatabaseMigrationException>();

        AppliedMigrations(dbPath).Should().BeEmpty("aucune inscription d'historique sur un refus");
    }

    /// <summary>
    /// Index homonyme mal défini (NON unique) : accepté, il laisserait deux comptes équivalents coexister.
    /// </summary>
    [Fact]
    public void Adoption_HomonymIndexNotUnique_IsRefused()
    {
        var dbPath = CreateHistoricalDatabase("partial-homonym.db", connection =>
        {
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\"");
            Exec(connection, $"CREATE INDEX \"{NormalizedIndexName}\" ON \"Users\" (\"NormalizedUsername\")");
        });

        TryPrepare(dbPath).Should().BeOfType<DatabaseMigrationException>();
        AppliedMigrations(dbPath).Should().BeEmpty();
    }

    /// <summary>
    /// Index homonyme sur la mauvaise colonne : le nom attendu ne suffit pas, la définition est vérifiée.
    /// </summary>
    [Fact]
    public void Adoption_HomonymIndexOnWrongColumn_IsRefused()
    {
        var dbPath = CreateHistoricalDatabase("partial-wrong-col.db", connection =>
        {
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\"");
            Exec(connection, $"CREATE UNIQUE INDEX \"{NormalizedIndexName}\" ON \"Users\" (\"Username\")");
        });

        TryPrepare(dbPath).Should().BeOfType<DatabaseMigrationException>();
        AppliedMigrations(dbPath).Should().BeEmpty();
    }

    /// <summary>
    /// Index présent alors que la colonne qu'il protège est absente : index résiduel ou homonyme — refus.
    /// </summary>
    [Fact]
    public void Adoption_IndexPresentWithoutColumn_IsRefused()
    {
        var dbPath = CreateHistoricalDatabase("partial-index-only.db", connection =>
        {
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\"");
            Exec(connection, "ALTER TABLE \"Users\" DROP COLUMN \"NormalizedUsername\"");
            // Index homonyme laissé sur une autre colonne, sans la colonne normalisée.
            Exec(connection, $"CREATE UNIQUE INDEX \"{NormalizedIndexName}\" ON \"Users\" (\"Username\")");
        });

        TryPrepare(dbPath).Should().BeOfType<DatabaseMigrationException>();
        AppliedMigrations(dbPath).Should().BeEmpty();
    }

    /// <summary>
    /// Base <b>déjà gérée</b> par migrations dont le schéma a été altéré après coup : l'historique affirme P3-10
    /// appliquée, mais l'index a disparu. La vérification post-préparation est inconditionnelle — un historique
    /// mensonger n'est jamais accepté silencieusement.
    /// </summary>
    [Fact]
    public void ManagedDatabase_WithHistoryButAlteredSchema_IsRefused()
    {
        var dbPath = PathFor("managed-altered.db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        using (var connection = Open(dbPath))
        {
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\"");
        }
        SqliteConnection.ClearAllPools();

        TryPrepare(dbPath).Should().BeOfType<DatabaseMigrationException>();
    }

    /// <summary>
    /// Colonne nullable ou portant des valeurs vides : le schéma paraît conforme, mais la clé métier ne l'est
    /// pas. Le portail de compatibilité refuse la nullabilité incompatible avant toute écriture d'historique.
    /// </summary>
    [Fact]
    public void Adoption_NullableNormalizedColumn_IsRefused()
    {
        var dbPath = CreateHistoricalDatabase("partial-nullable.db", connection =>
        {
            // Recrée Users avec NormalizedUsername NULLABLE (SQLite ne permet pas d'altérer une colonne).
            Exec(connection, $"DROP INDEX \"{NormalizedIndexName}\"");
            Exec(connection, "ALTER TABLE \"Users\" RENAME TO \"Users_old\"");
            Exec(connection,
                "CREATE TABLE \"Users\" (" +
                "\"UserId\" INTEGER NOT NULL CONSTRAINT \"PK_Users\" PRIMARY KEY AUTOINCREMENT, " +
                "\"Username\" TEXT NOT NULL, \"NormalizedUsername\" TEXT NULL, \"PasswordHash\" TEXT NOT NULL, " +
                "\"FirstName\" TEXT NOT NULL, \"LastName\" TEXT NOT NULL, \"Role\" TEXT NOT NULL, " +
                "\"IsActive\" INTEGER NOT NULL DEFAULT 1, \"LastLogin\" TEXT NULL, \"CreatedAt\" TEXT NOT NULL)");
            Exec(connection, "DROP TABLE \"Users_old\"");
        });

        TryPrepare(dbPath).Should().BeOfType<DatabaseMigrationException>();
        AppliedMigrations(dbPath).Should().BeEmpty();
    }

    // ================================================================== Down puis rejeu de Up

    /// <summary>
    /// Rollback HONNÊTE sur une base P3-10 valide : sous un schéma intact, l'index unique sur
    /// <c>NormalizedUsername</c> interdit structurellement les comptes ne différant que par la casse — le
    /// <c>Down</c> peut donc TOUJOURS restaurer l'ancien index pour toute donnée créée sous P3-10. Un rollback
    /// normal est réversible ; seule une altération manuelle du schéma pourrait le faire échouer (hors périmètre
    /// d'un rollback normal, jamais exercée ici).
    /// </summary>
    [Fact]
    public void Down_OnValidDatabase_RestoresLegacyIndex_KeepsAllData_ThenUpReproducesNormalizedKeys()
    {
        var dbPath = PathFor("down-then-up.db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        // Base déjà à jour (P3-10 appliquée) : NormalizedUsername est NOT NULL sans défaut (revue ciblée §3),
        // donc l'insertion brute doit la fournir explicitement — contrairement à InsertLegacyUser (P3-9).
        using (var connection = Open(dbPath))
        {
            foreach (var username in new[] { "admin", "Marie.Optic", "pierre-tech" })
            {
                Exec(connection,
                    "INSERT INTO \"Users\" (\"Username\", \"NormalizedUsername\", \"PasswordHash\", \"FirstName\", \"LastName\", \"Role\", \"IsActive\", \"CreatedAt\") " +
                    $"VALUES ('{username}', '{username.Trim().ToLowerInvariant()}', 'hash', 'F', 'L', 'Optician', 1, '2026-01-01 00:00:00')");
            }
        }
        SqliteConnection.ClearAllPools();

        // --- Down : ne cible QUE la migration P3-10, jusqu'à la précédente. ---
        string previousMigrationId;
        using (var context = CreateContext(dbPath))
        {
            var all = context.Database.GetMigrations().ToList();
            previousMigrationId = all[^2];
            context.GetService<IMigrator>().Migrate(previousMigrationId);
        }
        SqliteConnection.ClearAllPools();

        Columns(dbPath, "Users").Should().NotContain(c => c.Name == "NormalizedUsername",
            "Down supprime la colonne normalisée");
        IndexInfo(dbPath, NormalizedIndexName).Exists.Should().BeFalse();
        IndexInfo(dbPath, LegacyIndexName).Exists.Should().BeTrue("Down restaure l'ancien index sensible à la casse");
        IndexInfo(dbPath, LegacyIndexName).Unique.Should().BeTrue();

        UserCount(dbPath).Should().Be(3, "aucune ligne supprimée par le rollback");
        using (var connection = Open(dbPath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"Username\" FROM \"Users\" ORDER BY \"UserId\"";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            names.Should().Equal(new[] { "admin", "Marie.Optic", "pierre-tech" },
                "les formes affichables ne sont jamais modifiées par Up ni par Down");
        }

        AppliedMigrations(dbPath).Should().NotContain(
            m => m.EndsWith("_AddNormalizedUsernameAndSecureLocalUsers", StringComparison.Ordinal),
            "Down retire la migration de l'historique");

        // --- Rejeu de Up : les clés normalisées sont reproduites À L'IDENTIQUE. ---
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        var users = verify.Users.AsNoTracking().OrderBy(u => u.UserId).ToList();
        users.Select(u => u.Username).Should().Equal(new[] { "admin", "Marie.Optic", "pierre-tech" });
        users.Select(u => u.NormalizedUsername).Should().Equal(new[] { "admin", "marie.optic", "pierre-tech" });
        foreach (var user in users)
        {
            user.NormalizedUsername.Should().Be(UserIdentityPolicy.NormalizeUsername(user.Username));
        }

        IndexInfo(dbPath, NormalizedIndexName).Unique.Should().BeTrue();
        IndexInfo(dbPath, LegacyIndexName).Exists.Should().BeFalse();
        ColumnDefault(dbPath, "Users", "NormalizedUsername").Should().BeNull(
            "le rejeu de Up doit reproduire le schéma final SANS défaut permanent, comme une base neuve");
    }
}
