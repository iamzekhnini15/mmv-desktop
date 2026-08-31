using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace MMV.Domain.Tests.Persistence;

/// <summary>
/// P4-4A1 — Classification PostgreSQL de <see cref="PersistenceErrorMapper"/>, <b>sans serveur</b>.
///
/// <para>
/// Aucun serveur PostgreSQL, aucun conteneur, aucune variable d'environnement, aucun test ignoré :
/// toutes les formes d'exception sont FABRIQUÉES EN MÉMOIRE avec la vraie API Npgsql 8.0.6. Ces
/// tests appartiennent à <c>MMV.sln</c> et s'exécutent partout, y compris en CI.
/// </para>
///
/// <para>
/// Les formes reproduites ici sont exactement celles MESURÉES en P4-4A0 sur un vrai serveur
/// (spikes E19/E20, hors solution) : <c>DbUpdateException -&gt; PostgresException</c>,
/// <c>InvalidOperationException -&gt; DbUpdateException -&gt; NpgsqlException -&gt; TimeoutException</c>,
/// <c>NpgsqlException -&gt; IOException -&gt; SocketException</c>, etc. Le constat central de P4-4A0
/// est que <b>le type externe ne porte aucune information de classification</b> : ces tests
/// verrouillent le parcours complet de la chaîne <c>InnerException</c>.
/// </para>
///
/// <para>
/// ANTI-PARSING — plusieurs messages synthétiques sont délibérément TROMPEURS (par exemple un
/// <c>NpgsqlException</c> dont le message contient « 23505 duplicate » alors que la cause réelle est
/// un délai d'attente). Si le mapper venait un jour à lire <see cref="Exception.Message"/>, ces
/// tests échoueraient.
/// </para>
/// </summary>
public sealed class PersistenceErrorMapperPostgreSqlTests
{
    // ------------------------------------------------------------------
    // Fabriques : vraie API Npgsql 8.0.6.
    //   PostgresException(messageText, severity, invariantSeverity, sqlState, …, constraintName, …)
    //   NpgsqlException(message, innerException)
    // PostgresException est SCELLÉE et DÉRIVE de NpgsqlException : elle ne prend pas d'inner ;
    // l'imbrication se fait donc par les enveloppes (DbUpdateException, InvalidOperationException…).
    // ------------------------------------------------------------------

    private static PostgresException NewPostgres(
        string sqlState,
        string messageText = "message synthétique non contractuel",
        string? constraintName = null)
        => new(
            messageText,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            constraintName: constraintName);

    private static PersistenceException MapToPersistence(Exception exception)
    {
        var mapped = PersistenceErrorMapper.Map(exception);

        mapped.Should().BeOfType<PersistenceException>(
            "une erreur de persistance provider doit être transformée en erreur contrôlée");
        mapped.InnerException.Should().BeSameAs(exception,
            "l'exception d'origine est conservée : la chaîne technique reste disponible au diagnostic");

        return (PersistenceException)mapped;
    }

    private static T? FindInChain<T>(Exception exception) where T : Exception
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    // ==================================================================
    // PostgreSQL — classification par SqlState structuré
    // ==================================================================

    /// <summary>P1 — 23505 unique_violation.</summary>
    [Fact]
    public void P01_Postgres23505_UniqueViolation_MapsToUniqueConstraint()
    {
        var postgres = NewPostgres("23505", constraintName: "ix_synthetique_unicite");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
    }

    /// <summary>P2 — 23503 foreign_key_violation.</summary>
    [Fact]
    public void P02_Postgres23503_ForeignKeyViolation_MapsToConstraintViolation()
    {
        var postgres = NewPostgres("23503");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.ConstraintViolation);
    }

    /// <summary>P3 — 23502 not_null_violation.</summary>
    [Fact]
    public void P03_Postgres23502_NotNullViolation_MapsToConstraintViolation()
    {
        var postgres = NewPostgres("23502");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.ConstraintViolation);
    }

    /// <summary>P4 — 23514 check_violation (réellement mesuré en E19).</summary>
    [Fact]
    public void P04_Postgres23514_CheckViolation_MapsToConstraintViolation()
    {
        var postgres = NewPostgres("23514");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.ConstraintViolation);
    }

    /// <summary>P5 — 3D000 invalid_catalog_name : la base visée n'existe pas.</summary>
    [Fact]
    public void P05_Postgres3D000_InvalidCatalogName_MapsToConnectionFailure()
    {
        var postgres = NewPostgres("3D000");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>P6 — 40P01 deadlock_detected. Message volontairement trompeur.</summary>
    [Fact]
    public void P06_Postgres40P01_DeadlockDetected_MapsToConcurrency()
    {
        var postgres = NewPostgres("40P01", messageText: "duplicate key value violates unique constraint");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.Concurrency);
    }

    /// <summary>P7 — 55P03 lock_not_available.</summary>
    [Fact]
    public void P07_Postgres55P03_LockNotAvailable_MapsToDatabaseBusy()
    {
        var postgres = NewPostgres("55P03");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
    }

    /// <summary>
    /// P8 — 57014 query_canceled DIRECT, sans annulation cliente demandée : annulation SUBIE côté
    /// serveur, donc une indisponibilité momentanée et non une annulation utilisateur.
    /// </summary>
    [Fact]
    public void P08_Postgres57014_WithoutRequestedCancellation_MapsToDatabaseBusy()
    {
        var postgres = NewPostgres("57014");

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
    }

    /// <summary>
    /// P9 — 25P02 in_failed_sql_transaction. CONSÉQUENCE d'une erreur antérieure, jamais une cause :
    /// ne doit jamais être classée en Concurrency.
    /// </summary>
    [Fact]
    public void P09_Postgres25P02_InFailedSqlTransaction_MapsToUnknown()
    {
        var postgres = NewPostgres("25P02");

        var mapped = MapToPersistence(postgres);

        mapped.Category.Should().Be(PersistenceErrorCategory.Unknown);
        mapped.Category.Should().NotBe(PersistenceErrorCategory.Concurrency,
            "25P02 est la conséquence d'une erreur précédente, pas un conflit de concurrence");
    }

    /// <summary>P10 — SqlState non mesuré : aucune catégorie attribuée sur hypothèse.</summary>
    [Fact]
    public void P10_PostgresUnmeasuredSqlState_MapsToUnknown()
    {
        var postgres = NewPostgres("42883"); // undefined_function : hors périmètre P4-4A0

        MapToPersistence(postgres).Category.Should().Be(PersistenceErrorCategory.Unknown);
    }

    // ==================================================================
    // Annulation cliente — garde prioritaire
    // ==================================================================

    /// <summary>
    /// P11 — Forme réelle d'une annulation utilisateur :
    /// <c>OperationCanceledException -&gt; PostgresException 57014</c> avec un jeton RÉELLEMENT
    /// annulé. L'annulation doit être relayée INCHANGÉE (même instance), jamais convertie.
    /// </summary>
    [Fact]
    public void P11_RequestedCancellationWrapping57014_IsRelayedUnchanged()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var canceled = new OperationCanceledException(
            "operation was canceled",
            NewPostgres("57014"),
            cts.Token);

        var mapped = PersistenceErrorMapper.Map(canceled);

        mapped.Should().BeSameAs(canceled,
            "une annulation demandée par le client n'est pas une panne de persistance");
        mapped.Should().NotBeOfType<PersistenceException>();
    }

    // ==================================================================
    // PostgreSQL sans SqlState — classification par forme structurelle
    // ==================================================================

    /// <summary>P12 — Connexion refusée. Message volontairement trompeur.</summary>
    [Fact]
    public void P12_NpgsqlOverSocketException_MapsToConnectionFailure()
    {
        var npgsql = new NpgsqlException(
            "23505 duplicate key value",
            new SocketException((int)SocketError.ConnectionRefused));

        MapToPersistence(npgsql).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>P13 — Perte de connexion : <c>NpgsqlException -&gt; IOException -&gt; SocketException</c>.</summary>
    [Fact]
    public void P13_NpgsqlOverIoOverSocketException_MapsToConnectionFailure()
    {
        var npgsql = new NpgsqlException(
            "exception during reading from stream",
            new IOException("stream error", new SocketException((int)SocketError.ConnectionReset)));

        MapToPersistence(npgsql).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>
    /// P14 — Perte de connexion, seconde forme mesurée :
    /// <c>NpgsqlException -&gt; EndOfStreamException</c> (qui dérive d'<see cref="IOException"/>).
    /// </summary>
    [Fact]
    public void P14_NpgsqlOverEndOfStreamException_MapsToConnectionFailure()
    {
        var npgsql = new NpgsqlException(
            "exception during reading from stream",
            new EndOfStreamException("unexpected end of stream"));

        MapToPersistence(npgsql).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>P15 — Délai d'attente client : <c>NpgsqlException -&gt; TimeoutException</c>.</summary>
    [Fact]
    public void P15_NpgsqlOverTimeoutException_MapsToDatabaseBusy()
    {
        var npgsql = new NpgsqlException(
            "exception while writing to stream",
            new TimeoutException("timeout during writing attempt"));

        MapToPersistence(npgsql).Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
    }

    /// <summary>
    /// P16 — Parcours PROFOND mesuré en E20 :
    /// <c>InvalidOperationException -&gt; DbUpdateException -&gt; NpgsqlException -&gt; TimeoutException</c>.
    /// Verrouille le fait que la branche <c>DbUpdateException</c> générique ne court-circuite jamais
    /// le signal provider situé PLUS BAS dans la chaîne.
    /// </summary>
    [Fact]
    public void P16_InvalidOperationOverDbUpdateOverNpgsqlOverTimeout_MapsToDatabaseBusy()
    {
        var deep = new InvalidOperationException(
            "an exception occurred while saving",
            new DbUpdateException(
                "an error occurred while saving the entity changes",
                new NpgsqlException(
                    "exception while writing to stream",
                    new TimeoutException("timeout during writing attempt"))));

        var mapped = MapToPersistence(deep);

        mapped.Category.Should().Be(PersistenceErrorCategory.DatabaseBusy,
            "le signal provider profond prime sur l'enveloppe DbUpdateException");
        mapped.Category.Should().NotBe(PersistenceErrorCategory.Unknown);
    }

    /// <summary>
    /// P17 — <c>InvalidOperationException -&gt; NpgsqlException -&gt; SocketException</c> :
    /// le type externe ne dit rien de la nature de la panne.
    /// </summary>
    [Fact]
    public void P17_InvalidOperationOverNpgsqlOverSocket_MapsToConnectionFailure()
    {
        var deep = new InvalidOperationException(
            "an exception occurred while establishing a connection",
            new NpgsqlException(
                "failed to connect",
                new SocketException((int)SocketError.ConnectionRefused)));

        MapToPersistence(deep).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>P18 — <see cref="NpgsqlException"/> nue : aucun signal structuré exploitable.</summary>
    [Fact]
    public void P18_BareNpgsqlException_MapsToUnknown()
    {
        var npgsql = new NpgsqlException("échec sans cause structurée");

        MapToPersistence(npgsql).Category.Should().Be(PersistenceErrorCategory.Unknown);
    }

    // ==================================================================
    // Le type externe n'est jamais la classification
    // ==================================================================

    /// <summary>
    /// P19 — Forme EF Core mesurée : <c>DbUpdateException -&gt; PostgresException 23505</c>.
    /// Doit produire UniqueConstraint, et non le fourre-tout Unknown de la branche
    /// <c>DbUpdateException</c>.
    /// </summary>
    [Fact]
    public void P19_DbUpdateWrapping23505_MapsToUniqueConstraint_NotUnknown()
    {
        var dbUpdate = new DbUpdateException(
            "an error occurred while saving the entity changes",
            NewPostgres("23505", constraintName: "ix_synthetique_unicite"));

        var mapped = MapToPersistence(dbUpdate);

        mapped.Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
        mapped.Category.Should().NotBe(PersistenceErrorCategory.Unknown,
            "la branche DbUpdateException générique ne doit pas masquer le SqlState");
    }

    /// <summary>
    /// P20 — Même cause, enveloppe plus profonde :
    /// <c>InvalidOperationException -&gt; DbUpdateException -&gt; PostgresException 23505</c>.
    /// La classification est identique à P19 : le type externe ne la change pas.
    /// </summary>
    [Fact]
    public void P20_OuterExceptionTypeDoesNotChangeClassification()
    {
        var shallow = new DbUpdateException("saving failed", NewPostgres("23505"));
        var deep = new InvalidOperationException(
            "an exception occurred while saving",
            new DbUpdateException("saving failed", NewPostgres("23505")));

        var shallowCategory = MapToPersistence(shallow).Category;
        var deepCategory = MapToPersistence(deep).Category;

        deepCategory.Should().Be(shallowCategory);
        deepCategory.Should().Be(PersistenceErrorCategory.UniqueConstraint);
    }

    /// <summary>
    /// P21 — Le détail structuré reste exploitable au diagnostic : le <c>ConstraintName</c> du 23505
    /// est toujours retrouvable dans la chaîne <c>InnerException</c> de la
    /// <see cref="PersistenceException"/> produite, alors qu'il n'apparaît JAMAIS dans le message
    /// utilisateur.
    /// </summary>
    [Fact]
    public void P21_ConstraintNameRemainsReachableInInnerChain_ButNeverInUserMessage()
    {
        const string constraintName = "ix_synthetique_unicite_p21";
        var dbUpdate = new DbUpdateException("saving failed", NewPostgres("23505", constraintName: constraintName));

        var mapped = MapToPersistence(dbUpdate);

        FindInChain<PostgresException>(mapped)!.ConstraintName.Should().Be(constraintName,
            "le diagnostic technique doit rester possible depuis l'exception contrôlée");
        mapped.Message.Should().NotContain(constraintName);
        mapped.Message.Should().NotContain("23505");
    }

    // ==================================================================
    // Exceptions non liées au provider — préservées inchangées
    // ==================================================================

    /// <summary>
    /// P22 — <see cref="TimeoutException"/> SEULE, sans <see cref="NpgsqlException"/> : n'appartient
    /// pas à la persistance, donc jamais transformée.
    /// </summary>
    [Fact]
    public void P22_TimeoutExceptionWithoutNpgsql_IsLeftUnchanged()
    {
        var timeout = new TimeoutException("délai dépassé dans un composant applicatif");

        PersistenceErrorMapper.Map(timeout).Should().BeSameAs(timeout);
    }

    /// <summary>P23 — <see cref="IOException"/> SEULE, sans <see cref="NpgsqlException"/>.</summary>
    [Fact]
    public void P23_IoExceptionWithoutNpgsql_IsLeftUnchanged()
    {
        var io = new IOException("échec d'écriture d'un fichier applicatif");

        PersistenceErrorMapper.Map(io).Should().BeSameAs(io);
    }

    /// <summary>
    /// P24 — INTERDICTION DU PARSING DE MESSAGE : une exception quelconque dont le message contient
    /// tous les mots-clés de panne reste INCHANGÉE.
    /// </summary>
    [Fact]
    public void P24_GenericExceptionWithMisleadingMessage_IsLeftUnchanged()
    {
        var generic = new Exception("23505 duplicate timeout socket connection refused");

        PersistenceErrorMapper.Map(generic).Should().BeSameAs(generic,
            "aucune classification ne peut reposer sur le texte d'un message");
    }

    /// <summary>
    /// P25 — INTERDICTION DU PARSING DE MESSAGE, côté provider : un
    /// <see cref="NpgsqlException"/> dont le message annonce « 23505 duplicate » mais dont la cause
    /// réelle est un délai d'attente doit être classé DatabaseBusy, jamais UniqueConstraint.
    /// </summary>
    [Fact]
    public void P25_NpgsqlMessageClaims23505ButCauseIsTimeout_MapsToDatabaseBusy()
    {
        var misleading = new NpgsqlException(
            "23505 duplicate key value violates unique constraint",
            new TimeoutException("timeout during writing attempt"));

        var mapped = MapToPersistence(misleading);

        mapped.Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
        mapped.Category.Should().NotBe(PersistenceErrorCategory.UniqueConstraint,
            "le message ment : seule la forme structurée fait foi");
    }

    // ==================================================================
    // Non-régression SQLite — branches sans test direct préexistant
    // (les branches 2067 unique, BUSY 5, DbUpdateException->SqliteException et
    //  « exception générique inchangée » sont déjà couvertes par EfTransactionRunnerTests)
    // ==================================================================

    /// <summary>S1 — SQLITE_CONSTRAINT_PRIMARYKEY (1555) : doublon de clé primaire.</summary>
    [Fact]
    public void S1_SqliteExtendedPrimaryKey1555_MapsToUniqueConstraint()
    {
        var sqlite = new SqliteException("PRIMARY KEY constraint failed", 19, 1555);

        MapToPersistence(sqlite).Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
    }

    /// <summary>S2 — SQLITE_CONSTRAINT (19) générique via un code étendu non-unicité (FK 787).</summary>
    [Fact]
    public void S2_SqlitePrimaryConstraint19_MapsToConstraintViolation()
    {
        var sqlite = new SqliteException("FOREIGN KEY constraint failed", 19, 787);

        MapToPersistence(sqlite).Category.Should().Be(PersistenceErrorCategory.ConstraintViolation);
    }

    /// <summary>S3 — SQLITE_LOCKED (6).</summary>
    [Fact]
    public void S3_SqliteLocked6_MapsToDatabaseBusy()
    {
        var sqlite = new SqliteException("database table is locked", 6, 6);

        MapToPersistence(sqlite).Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
    }

    /// <summary>S4 — SQLITE_IOERR (10).</summary>
    [Fact]
    public void S4_SqliteIoErr10_MapsToConnectionFailure()
    {
        var sqlite = new SqliteException("disk I/O error", 10, 10);

        MapToPersistence(sqlite).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>S5 — SQLITE_CANTOPEN (14).</summary>
    [Fact]
    public void S5_SqliteCantOpen14_MapsToConnectionFailure()
    {
        var sqlite = new SqliteException("unable to open database file", 14, 14);

        MapToPersistence(sqlite).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
    }

    /// <summary>S6 — Tout autre code SQLite reste Unknown.</summary>
    [Fact]
    public void S6_SqliteOtherErrorCode_MapsToUnknown()
    {
        var sqlite = new SqliteException("SQL logic error", 1, 1);

        MapToPersistence(sqlite).Category.Should().Be(PersistenceErrorCategory.Unknown);
    }

    /// <summary>
    /// S7 — <see cref="DbUpdateException"/> SANS aucune exception provider : dernier filet, jamais
    /// assimilé à une violation de contrainte.
    /// </summary>
    [Fact]
    public void S7_DbUpdateExceptionWithoutProviderInner_MapsToUnknown()
    {
        var dbUpdate = new DbUpdateException("an error occurred while saving the entity changes");

        var mapped = MapToPersistence(dbUpdate);

        mapped.Category.Should().Be(PersistenceErrorCategory.Unknown);
        mapped.Category.Should().NotBe(PersistenceErrorCategory.ConstraintViolation);
        mapped.Category.Should().NotBe(PersistenceErrorCategory.UniqueConstraint);
    }

    /// <summary>S8 — Une <see cref="PersistenceException"/> déjà mappée traverse le mapper telle quelle.</summary>
    [Fact]
    public void S8_AlreadyMappedPersistenceException_IsReturnedAsSameInstance()
    {
        var already = new PersistenceException(
            "message déjà assaini",
            PersistenceErrorCategory.UniqueConstraint,
            new SqliteException("UNIQUE constraint failed", 19, 2067));

        var mapped = PersistenceErrorMapper.Map(already);

        mapped.Should().BeSameAs(already, "une erreur déjà contrôlée n'est jamais ré-enveloppée");
        ((PersistenceException)mapped).Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
    }
}
