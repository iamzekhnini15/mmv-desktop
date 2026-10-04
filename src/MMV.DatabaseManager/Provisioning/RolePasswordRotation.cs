using System.Data.Common;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Provisioning;

/// <summary>Demande de rotation du secret d'un rôle MMV. <see cref="ToString"/> ne restitue aucun secret.</summary>
public sealed class RoleRotationRequest
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? RootCertificatePath { get; init; }
    public required string AdminUser { get; init; }
    public required string AdminPassword { get; init; }
    public required string AdminDatabase { get; init; }
    public required string Database { get; init; }
    public required string Role { get; init; }
    public required string NewPassword { get; init; }
    public required string Operator { get; init; }

    public override string ToString() => $"rôle '{Role}' sur {Host}:{Port}/{Database}";
}

/// <summary>
/// Rotation du secret d'un rôle MMV (P4-8, D-14) avec l'identifiant administrateur : nouveau vérificateur
/// SCRAM-SHA-256 calculé par l'outil, puis <b>preuve</b> que le nouveau secret ouvre une session sur la base.
/// Refuse tout rôle privilégié (superutilisateur, création, réplication) et l'administrateur lui-même : l'outil ne
/// touche qu'aux identités qu'il a provisionnées. Côté poste, la rotation se termine par
/// <c>configure-workstation</c> (fichier DPAPI réécrit).
/// </summary>
public sealed class RolePasswordRotation
{
    private const string TargetSql =
        "SELECT r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls " +
        "FROM pg_catalog.pg_roles r WHERE r.rolname = @role";

    private readonly IMigrationJournal _trace;

    public RolePasswordRotation(IMigrationJournal trace) =>
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));

    public async Task<AdministrationResult> RunAsync(RoleRotationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RoleSecretPolicy.IsValid(request.NewPassword))
        {
            return Finish(MigrationExitCode.InvalidArguments, RoleSecretPolicy.Requirement);
        }

        if (request.Role == request.AdminUser || request.Role.StartsWith("pg_", StringComparison.OrdinalIgnoreCase))
        {
            return Finish(MigrationExitCode.InvalidArguments, $"Rôle '{request.Role}' hors du périmètre de la rotation MMV.");
        }

        _trace.Write($"rotate-role-password : ouverture — {request} — opérateur '{request.Operator}'");
        try
        {
            await using var admin = PostgreSqlConnectionSecurity.CreateConnection(Connection(request, request.AdminDatabase,
                request.AdminUser, request.AdminPassword));
            var unavailable = await PostgreSqlProvisioner.OpenAsync(admin, cancellationToken);
            if (unavailable is not null)
            {
                return Finish(MigrationExitCode.ServerUnreachable, unavailable);
            }

            switch (await ServerCommand.ScalarAsync(admin, TargetSql, cancellationToken, ("role", request.Role)))
            {
                case null or DBNull:
                    return Finish(MigrationExitCode.InvalidArguments, $"Rôle '{request.Role}' inexistant.");
                case true:
                    return Finish(MigrationExitCode.SecurityRefused, $"Rôle '{request.Role}' privilégié : rotation refusée.");
            }

            await ServerCommand.ExecuteServerFormattedAsync(admin,
                "SELECT format('ALTER ROLE %I WITH PASSWORD %L', @role, @verifier)", cancellationToken,
                ("role", request.Role), ("verifier", ScramSha256Verifier.Create(request.NewPassword)));

            await using var proof = PostgreSqlConnectionSecurity.CreateConnection(
                Connection(request, request.Database, request.Role, request.NewPassword));
            var failure = await PostgreSqlProvisioner.OpenAsync(proof, cancellationToken);
            return failure is null
                ? Finish(MigrationExitCode.Success,
                    $"{request} : secret remplacé et vérifié. Reconfigurez chaque poste (configure-workstation).")
                : Finish(MigrationExitCode.SecurityRefused, $"secret remplacé mais connexion impossible : {failure}");
        }
        catch (DatabaseConfigurationException exception)
        {
            return Finish(MigrationExitCode.InvalidArguments, exception.Message);
        }
        catch (DbException exception)
        {
            return Finish(MigrationExitCode.ProvisioningFailed, $"rotation interrompue ({ServerCommand.Describe(exception)}).");
        }
    }

    private static string Connection(RoleRotationRequest request, string database, string user, string secret) =>
        PostgreSqlConnectionSecurity.BuildConnectionString(
            new PostgreSqlConnectionSettings(request.Host, request.Port, database, user, secret, request.RootCertificatePath));

    private AdministrationResult Finish(MigrationExitCode code, string message)
    {
        _trace.Write($"rotate-role-password : {(code == MigrationExitCode.Success ? "succès" : $"échec code {(int)code}")} — {message}");
        return new AdministrationResult(code, message);
    }
}
