using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.Migrations.Migrations;

namespace MMV.DatabaseManager;

/// <summary>Issue de la vérification post-migration.</summary>
public sealed record SchemaVerificationResult(bool Succeeded, string? Failure)
{
    public static SchemaVerificationResult Success { get; } = new(true, null);

    public static SchemaVerificationResult Failed(string failure) => new(false, failure);
}

/// <summary>Port de la vérification post-migration (étape 9).</summary>
public interface IServerSchemaVerification
{
    Task<SchemaVerificationResult> VerifyAsync(string appRole, CancellationToken cancellationToken = default);
}

/// <summary>
/// Vérification post-migration, périmètre restreint de P4-6B (§6, §9 du plan) : aucune migration en attente,
/// Appliquées ⊆ Connues, historique lisible — et <b>droits du rôle applicatif</b> (Q-23) : lecture de la
/// métadonnée assurée, aucun droit sur le journal — et, depuis P4-6C, les <b>invariants physiques</b> que
/// <c>SqliteDatabaseManager.VerifyAfterPreparation</c> garantit côté SQLite (colonnes et index uniques P3-8, P3-10).
/// Une migration n'est jamais <c>success</c> si cette vérification échoue.
/// </summary>
public sealed class ServerSchemaVerification : IServerSchemaVerification
{
    private readonly ISchemaMigrator _migrator;
    private readonly DbConnection _connection;

    /// <param name="migrator">Source de Connues / Appliquées.</param>
    /// <param name="connection">Connexion de contrôle, ouverte.</param>
    public ServerSchemaVerification(ISchemaMigrator migrator, DbConnection connection)
    {
        _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async Task<SchemaVerificationResult> VerifyAsync(string appRole, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appRole);

        var applied = await _migrator.GetAppliedAsync(cancellationToken);
        var known = _migrator.KnownMigrations;

        var pending = known.Except(applied, StringComparer.Ordinal).ToArray();
        if (pending.Length > 0)
        {
            return SchemaVerificationResult.Failed($"migrations en attente : {string.Join(", ", pending)}");
        }

        var unknown = applied.Except(known, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            return SchemaVerificationResult.Failed($"migrations appliquées inconnues de l'outil : {string.Join(", ", unknown)}");
        }

        await using var command = ServerCommand.Create(_connection,
            "SELECT has_schema_privilege(@role, @schema, 'USAGE'), " +
            "has_table_privilege(@role, @compatibility, 'SELECT'), " +
            "has_table_privilege(@role, @journal, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')",
            ("role", appRole),
            ("schema", ServerCompatibilityMetadataNames.Schema),
            ("compatibility", ServerCompatibilityMetadataNames.QualifiedCompatibilityTable),
            ("journal", ServerCompatibilityMetadataNames.QualifiedJournalTable));
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        await reader.ReadAsync(cancellationToken);

        if (!reader.GetBoolean(0) || !reader.GetBoolean(1))
        {
            return SchemaVerificationResult.Failed(
                $"droits du rôle applicatif non assurés (Q-23) : USAGE sur {ServerCompatibilityMetadataNames.Schema} " +
                $"et SELECT sur {ServerCompatibilityMetadataNames.QualifiedCompatibilityTable} exigés");
        }

        if (reader.GetBoolean(2))
        {
            return SchemaVerificationResult.Failed(
                $"le rôle applicatif détient un droit sur {ServerCompatibilityMetadataNames.QualifiedJournalTable} (DP-5, DP-8)");
        }

        await reader.DisposeAsync();

        // P4-6C : invariants physiques (portage de VerifyAfterPreparation, P3-8 / P3-10) — dès que la baseline de
        // production, qui les crée, est inscrite. Un historique qui l'affirme sans le schéma réel n'est jamais accepté.
        if (applied.Contains(ProductionBaselineId, StringComparer.Ordinal))
        {
            var invariant = await PhysicalInvariantFailureAsync(cancellationToken);
            if (invariant is not null)
            {
                return SchemaVerificationResult.Failed(invariant);
            }
        }

        return SchemaVerificationResult.Success;
    }

    /// <summary>Identifiant de la baseline PostgreSQL de production, lu sur la migration elle-même.</summary>
    public static readonly string ProductionBaselineId =
        typeof(InitialPostgreSqlBaseline).GetCustomAttribute<MigrationAttribute>()!.Id;

    private const string ActiveLowStockIndex = "idx_notifications_active_low_stock_unique";
    private const string NormalizedUsernameIndex = "idx_users_normalized_username_unique";

    /// <summary>Termes du prédicat de l'index filtré, tels que PostgreSQL les restitue (<c>pg_get_expr</c>).</summary>
    private static readonly string[] ActiveLowStockPredicateTerms =
        ["\"Type\"", "'LowStock'", "\"EntityType\"", "'Product'", "\"EntityId\" IS NOT NULL", "\"ResolvedAt\" IS NULL"];

    private async Task<string?> PhysicalInvariantFailureAsync(CancellationToken cancellationToken)
    {
        // Lisibilité des agrégats principaux par le schéma réel.
        foreach (var table in new[] { "Users", "Customers", "Products" })
        {
            await ServerCommand.ScalarAsync(_connection, $"SELECT 1 FROM public.\"{table}\" LIMIT 1", cancellationToken);
        }

        if (!await ColumnExistsAsync("Notifications", "ResolvedAt", cancellationToken))
        {
            return "invariant physique absent : colonne Notifications.ResolvedAt (P3-8)";
        }

        var lowStock = await IndexAsync(ActiveLowStockIndex, cancellationToken);
        if (lowStock is not { Unique: true } index
            || !index.Columns.SequenceEqual(["Type", "EntityType", "EntityId"], StringComparer.Ordinal)
            || index.Predicate is not { } predicate
            || !ActiveLowStockPredicateTerms.All(t => predicate.Contains(t, StringComparison.Ordinal)))
        {
            return $"invariant physique absent ou mal défini : index unique filtré {ActiveLowStockIndex} (P3-8)";
        }

        if (!await ColumnExistsAsync("Users", "NormalizedUsername", cancellationToken))
        {
            return "invariant physique absent : colonne Users.NormalizedUsername (P3-10)";
        }

        var normalized = await IndexAsync(NormalizedUsernameIndex, cancellationToken);
        if (normalized is not { Unique: true, Predicate: null } usernameIndex
            || !usernameIndex.Columns.SequenceEqual(["NormalizedUsername"], StringComparer.Ordinal))
        {
            return $"invariant physique absent ou mal défini : index unique {NormalizedUsernameIndex} (P3-10)";
        }

        return null;
    }

    private async Task<bool> ColumnExistsAsync(string table, string column, CancellationToken cancellationToken) =>
        await ServerCommand.ScalarAsync(_connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = @table AND column_name = @column)",
            cancellationToken, ("table", table), ("column", column)) is true;

    private async Task<(bool Unique, string[] Columns, string? Predicate)?> IndexAsync(string name, CancellationToken cancellationToken)
    {
        await using var command = ServerCommand.Create(_connection,
            "SELECT i.indisunique, pg_catalog.pg_get_expr(i.indpred, i.indrelid), " +
            "ARRAY(SELECT a.attname::text FROM unnest(i.indkey) WITH ORDINALITY AS k(attnum, position) " +
            "JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum ORDER BY k.position) " +
            "FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid " +
            "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname = @name",
            ("name", name));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetBoolean(0), reader.GetFieldValue<string[]>(2), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
