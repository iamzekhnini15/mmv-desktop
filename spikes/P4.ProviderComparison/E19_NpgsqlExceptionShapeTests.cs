using System.Data.Common;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E19 (P4-4A0) — FORME STRUCTURELLE des échecs Npgsql.
///
/// <para>
/// P4-1 a mesuré quatre cas PostgreSQL SANS <c>SqlState</c> : timeout client, timeout de verrou,
/// connexion refusée, perte de connexion — tous de la même famille <c>NpgsqlException</c>, avec
/// <c>connection.State = Open</c>. Tant qu'aucun signal TYPÉ ne les sépare, la classification
/// PostgreSQL de <c>PersistenceErrorMapper</c> (lot P4-4A) ne peut pas être écrite.
/// </para>
///
/// <para>
/// Cette expérimentation ne CLASSE rien : elle RELÈVE, via <see cref="ExceptionShape"/>, la chaîne
/// <c>InnerException</c> complète et les propriétés structurées (SqlState, SocketErrorCode,
/// TimeoutException, annulation, IsTransient). Aucun message n'est lu.
/// <c>PersistenceErrorMapper</c> et <c>PersistenceErrorCategory</c> ne sont PAS modifiés.
/// </para>
///
/// <para>
/// Les scénarios A, B, C, E et la sonde 23514 n'exigent qu'une base jetable. Le scénario D exige en
/// plus le conteneur jetable <c>mmv-p4-</c> (cf. <see cref="DockerControl"/>) et vit donc dans un
/// test séparé — l'absence de conteneur ne doit pas priver le rapport des autres mesures.
/// </para>
/// </summary>
[Collection(SpikeSerialCollection.Name)]
public class E19_NpgsqlExceptionShapeTests
{
    private const string Experiment = "E19-npgsql-exception-shape";

    private const ProviderKind Provider = ProviderKind.Postgres;

    private const string Name = nameof(ProviderKind.Postgres);

    /// <summary>Commande volontairement longue : l'échec mesuré doit venir du timeout, jamais de la fin normale.</summary>
    private const string LongSleep = "SELECT pg_sleep(30);";

    [SkippableFact]
    public async Task Failure_shapes_without_sqlstate_are_captured_structurally()
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(Provider), SpikeEnvironment.SkipReason(Provider));

        await using var database = await SpikeDatabase.CreateAsync(Provider, "e19");

        await ExecuteAsync(database, "CREATE TABLE shape_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await ExecuteAsync(database, "INSERT INTO shape_probe (id, payload) VALUES (1, 0);");

        SpikeLog.Section(Experiment, Name, "E19 — Forme structurelle des exceptions Npgsql (aucun parsing de message)");
        SpikeLog.Write(Experiment, Name,
            "| Cas | Outer | Inner 1 | Inner 2 | SqlState | SocketException | SocketErrorCode | " +
            "TimeoutException | OperationCanceledException | Connection.State | Distinguable |");

        // === A — TIMEOUT CLIENT : CommandTimeout court sur une commande longue ====================
        await MeasureAsync(database, "A. timeout-client (CommandTimeout=2s)", async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = LongSleep;
            await command.ExecuteNonQueryAsync();
        });

        // === B — TIMEOUT DE VERROU ================================================================
        // La ligne est détenue par une AUTRE transaction : le second UPDATE attend, puis expire.
        await using (var holder = await database.OpenRawAsync())
        {
            await using var holdTransaction = await holder.BeginTransactionAsync();
            await using (var hold = holder.CreateCommand())
            {
                hold.Transaction = holdTransaction;
                hold.CommandText = "UPDATE shape_probe SET payload = payload + 1 WHERE id = 1;";
                await hold.ExecuteNonQueryAsync();
            }

            // B1 — expiration décidée par le CLIENT (CommandTimeout), comme en A.
            await MeasureAsync(database, "B1. timeout-de-verrou cote CLIENT (CommandTimeout=3s)", async connection =>
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 3;
                command.CommandText = "UPDATE shape_probe SET payload = payload + 100 WHERE id = 1;";
                await command.ExecuteNonQueryAsync();
            });

            // B2 — expiration décidée par le SERVEUR (lock_timeout). Hypothèse à VÉRIFIER : le
            // serveur renvoie alors une erreur avec SqlState, donc structurellement distincte de B1.
            await MeasureAsync(database, "B2. timeout-de-verrou cote SERVEUR (lock_timeout=1s)", async connection =>
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await SetAsync(connection, transaction, "SET lock_timeout = '1000ms';");

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 30; // le client ne doit PAS être la cause
                command.CommandText = "UPDATE shape_probe SET payload = payload + 100 WHERE id = 1;";
                await command.ExecuteNonQueryAsync();
            });

            await holdTransaction.RollbackAsync();
        }

        // === C — CONNEXION REFUSÉE (port fermé) ===================================================
        var refused = SpikeConnections.Create(Provider, SpikeConnections.WithUnreachablePort(Provider));
        await using (refused)
        {
            var before = refused.State;
            try
            {
                await refused.OpenAsync();
                SpikeLog.Write(Experiment, Name, "C. connexion-refusee | AUCUNE ERREUR LEVEE (inattendu)");
            }
            catch (Exception exception)
            {
                Record("C. connexion-refusee (port ferme)", ExceptionShape.Capture(exception, before, refused.State));
            }
        }

        // === E — ANNULATION ET 57014 ==============================================================
        // E1 — annulation par CancellationToken, le timeout client étant volontairement hors de cause.
        await MeasureAsync(database, "E1. annulation-CancellationToken (2s)", async connection =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 60;
            command.CommandText = LongSleep;
            await command.ExecuteNonQueryAsync(cts.Token);
        });

        // E2 — expiration décidée par le SERVEUR (statement_timeout) : attendu 57014 SANS annulation
        //      cliente. C'est le cas qui dirait si 57014 signifie « l'utilisateur a annulé ».
        await MeasureAsync(database, "E2. statement_timeout SERVEUR (1s)", async connection =>
        {
            await SetAsync(connection, transaction: null, "SET statement_timeout = '1000ms';");

            await using var command = connection.CreateCommand();
            command.CommandTimeout = 60; // le client ne doit PAS être la cause
            command.CommandText = LongSleep;
            await command.ExecuteNonQueryAsync();
        });

        // === 23514 — VIOLATION DE CONTRAINTE CHECK (P4-4A0 §5, mesure facultative) ================
        // Table de SONDE créée dans la base jetable : le modèle de production n'est pas touché.
        await ExecuteAsync(database,
            "CREATE TABLE check_probe (id int PRIMARY KEY, qty int NOT NULL " +
            "CONSTRAINT ck_check_probe_qty CHECK (qty >= 0));");

        await MeasureAsync(database, "F. violation CHECK (23514 attendu)", async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO check_probe (id, qty) VALUES (1, -1);";
            await command.ExecuteNonQueryAsync();
        });

        Assert.True(true);
    }

    /// <summary>
    /// D — PERTE DE CONNEXION pendant une transaction. La logique d'arrêt/redémarrage d'E14 n'est pas
    /// modifiée : elle est REJOUÉE ici uniquement pour relever la FORME de l'exception, puis le
    /// serveur est remis en marche et son retour est vérifié.
    /// </summary>
    [SkippableFact]
    public async Task Connection_loss_shape_is_captured_structurally()
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(Provider), SpikeEnvironment.SkipReason(Provider));

        var container = DockerControl.ContainerFor(Provider);
        Skip.If(container is null, DockerControl.SkipReason(Provider));

        await using var database = await SpikeDatabase.CreateAsync(Provider, "e19d");
        await ExecuteAsync(database, "CREATE TABLE loss_shape_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await ExecuteAsync(database, "INSERT INTO loss_shape_probe (id, payload) VALUES (1, 100);");

        SpikeLog.Section(Experiment, Name, "E19-D — Forme structurelle de la PERTE DE CONNEXION");

        await using (var connection = await database.OpenRawAsync())
        {
            var transaction = await connection.BeginTransactionAsync();

            await using (var write = connection.CreateCommand())
            {
                write.Transaction = transaction;
                write.CommandText = "UPDATE loss_shape_probe SET payload = 777 WHERE id = 1;";
                await write.ExecuteNonQueryAsync();
            }

            var before = connection.State;
            var stopped = await DockerControl.StopAsync(container!);
            SpikeLog.Write(Experiment, Name, $"D. arret du conteneur pendant la transaction : {stopped}");

            try
            {
                await using var write2 = connection.CreateCommand();
                write2.Transaction = transaction;
                write2.CommandTimeout = 15;
                write2.CommandText = "UPDATE loss_shape_probe SET payload = 888 WHERE id = 1;";
                await write2.ExecuteNonQueryAsync();
                SpikeLog.Write(Experiment, Name, "D. perte-de-connexion | AUCUNE ERREUR LEVEE (inattendu)");
            }
            catch (Exception exception)
            {
                Record("D. perte-de-connexion (serveur arrete en transaction)",
                    ExceptionShape.Capture(exception, before, connection.State));
            }

            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception exception)
            {
                // Le serveur est arrêté : le rollback CLIENT ne peut pas aboutir. Sa forme est un
                // fait utile — c'est ce que verrait un `using` de transaction pendant une panne.
                Record("D-bis. rollback pendant la panne", ExceptionShape.Capture(exception, before, connection.State));
            }
        }

        // Première reconnexion AVANT purge : une connexion morte peut être rendue par le pool.
        var restarted = await DockerControl.StartAsync(container!);
        SpikeLog.Write(Experiment, Name, $"D. redemarrage : {restarted}");

        var pooled = SpikeConnections.Create(Provider, SpikeConnections.WithShortConnectTimeout(Provider, 5));
        await using (pooled)
        {
            var before = pooled.State;
            try
            {
                await pooled.OpenAsync();
                await using var command = pooled.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync();
                SpikeLog.Write(Experiment, Name, "D. 1re reconnexion SANS purge du pool : REUSSITE");
            }
            catch (Exception exception)
            {
                Record("D-ter. 1re reconnexion SANS purge du pool",
                    ExceptionShape.Capture(exception, before, pooled.State));
            }
        }

        // Le serveur doit être rendu joignable : cette expérimentation ne laisse aucune panne derrière elle.
        SpikeConnections.ClearPools(Provider);
        var recovery = await DockerControl.WaitUntilReachableAsync(Provider, TimeSpan.FromMinutes(3));
        SpikeLog.Write(Experiment, Name,
            $"D. apres ClearAllPools : {(recovery is null ? "SERVEUR NON REVENU (limite atteinte)" : $"joignable en {recovery.Value.TotalSeconds:F1} s")}");

        Assert.True(recovery is not null, "[Postgres] Le serveur n'est pas revenu dans la limite de 3 min.");
    }

    /// <summary>
    /// Exécute une action ATTENDUE EN ÉCHEC sur une connexion neuve, et relève la forme de l'échec
    /// ainsi que l'état de connexion observé avant/après. Une connexion par cas : PostgreSQL avorte
    /// toute transaction après une erreur (25P02), ce qui polluerait la mesure suivante.
    /// </summary>
    private static async Task MeasureAsync(SpikeDatabase database, string label, Func<DbConnection, Task> action)
    {
        await using var connection = await database.OpenRawAsync();
        var before = connection.State;

        try
        {
            await action(connection);
            SpikeLog.Write(Experiment, Name, $"{label} | AUCUNE ERREUR LEVEE (resultat inattendu a consigner)");
        }
        catch (Exception exception)
        {
            Record(label, ExceptionShape.Capture(exception, before, connection.State));
        }
    }

    private static void Record(string label, ExceptionShape shape)
    {
        SpikeLog.Write(Experiment, Name, shape.ToMatrixRow(label));
        SpikeLog.Write(Experiment, Name, $"{label} | {shape.ToEvidenceLine()}");
    }

    private static async Task SetAsync(DbConnection connection, DbTransaction? transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SpikeDatabase database, string sql)
    {
        await using var connection = await database.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
