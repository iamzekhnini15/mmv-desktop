using System.Data;
using System.Data.Common;
using MMV.Infrastructure.Data;

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
/// métadonnée assurée, aucun droit sur le journal. Le portage des invariants physiques est P4-6C.
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

        return SchemaVerificationResult.Success;
    }
}
