using System.Data.Common;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Permissions;

/// <summary>Port des droits du rôle applicatif sur la métadonnée (Q-23).</summary>
public interface IApplicationRoleGrants
{
    /// <summary>Le rôle existe-t-il sur le serveur (<c>pg_roles</c>) ?</summary>
    Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken = default);

    /// <summary>Accorde, idempotemment, la lecture de la métadonnée — et rien d'autre.</summary>
    Task GrantAsync(string role, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>GRANT</c> idempotents au rôle <c>--app-role</c> (P4-6B, décision Q-23 ; §4.5 du plan) :
/// <c>USAGE</c> sur le schéma <c>mmv_meta</c> et <c>SELECT</c> sur <c>schema_compatibility</c>, <b>aucun</b>
/// droit sur <c>migration_run</c>. Aucun nom de rôle n'est codé ici : P4-8 possède les rôles et leurs noms.
///
/// <para>
/// <b>Entrée hostile.</b> Un identifiant ne peut pas être un paramètre lié dans un <c>GRANT</c>. Il n'est donc
/// <b>jamais concaténé</b> côté client : le texte est produit par le serveur, <c>format('… %I', @role)</c>, avec
/// le rôle en paramètre lié, puis exécuté tel quel. L'existence du rôle est vérifiée avant toute écriture par
/// une requête paramétrée.
/// </para>
/// </summary>
public sealed class ApplicationRoleGrants : IApplicationRoleGrants
{
    private const string UsageOnSchema = "SELECT format('GRANT USAGE ON SCHEMA %I TO %I', @schema, @role)";
    private const string SelectOnCompatibility = "SELECT format('GRANT SELECT ON TABLE %I.%I TO %I', @schema, @table, @role)";

    private readonly DbConnection _connection;

    /// <param name="connection">Connexion de contrôle, ouverte, tenant le verrou de session.</param>
    public ApplicationRoleGrants(DbConnection connection) =>
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public async Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return await ServerCommand.ScalarAsync(_connection,
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = @role)",
            cancellationToken, ("role", role)) is true;
    }

    public async Task GrantAsync(string role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        await ExecuteServerFormattedAsync(UsageOnSchema, cancellationToken,
            ("schema", ServerCompatibilityMetadataNames.Schema), ("role", role));
        await ExecuteServerFormattedAsync(SelectOnCompatibility, cancellationToken,
            ("schema", ServerCompatibilityMetadataNames.Schema),
            ("table", ServerCompatibilityMetadataNames.CompatibilityTable),
            ("role", role));
    }

    private async Task ExecuteServerFormattedAsync(
        string formatSql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        var statement = await ServerCommand.ScalarAsync(_connection, formatSql, cancellationToken, parameters) as string
                        ?? throw new InvalidOperationException("Le serveur n'a pas produit l'instruction GRANT.");

        await ServerCommand.NonQueryAsync(_connection, statement, cancellationToken);
    }
}
