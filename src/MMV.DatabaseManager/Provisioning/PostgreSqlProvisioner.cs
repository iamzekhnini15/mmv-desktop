using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Provisioning;

/// <summary>
/// Demande de provisioning (P4-8, D-09, DP-5). <b>Tous les noms sont des paramètres</b> : aucun nom de rôle ni de
/// base n'existe dans le code. <see cref="ToString"/> ne restitue aucun secret.
/// </summary>
public sealed class ProvisioningRequest
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? RootCertificatePath { get; init; }
    public required string AdminUser { get; init; }
    public required string AdminPassword { get; init; }
    public required string AdminDatabase { get; init; }
    public required string Database { get; init; }
    public required string MigratorRole { get; init; }
    public required string MigratorPassword { get; init; }
    public required string AppRole { get; init; }
    public required string AppPassword { get; init; }
    public required string BackupRole { get; init; }
    public required string BackupPassword { get; init; }
    public required string Operator { get; init; }

    public override string ToString() =>
        $"base '{Database}' sur {Host}:{Port} — migrateur '{MigratorRole}', applicatif '{AppRole}', sauvegarde '{BackupRole}'";
}

/// <summary>Issue d'une opération d'administration : code de sortie et message sans secret.</summary>
public sealed record AdministrationResult(MigrationExitCode ExitCode, string Message)
{
    public bool Succeeded => ExitCode == MigrationExitCode.Success;
}

/// <summary>
/// Provisioning de la base centrale (P4-8, ADR-PROD-DB-010 ; D-09, DP-5 RL-3 amendé, D-14) — exécuté
/// <b>explicitement</b> par un administrateur, avec l'identifiant superutilisateur de l'installation. Jamais par
/// <c>MMV.App</c>, jamais implicitement par <c>migrate</c>.
///
/// <para><b>Ce qu'il établit</b> (et rien d'autre) :</para>
/// <list type="bullet">
///   <item>trois rôles <c>LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS</c>, membres
///   d'aucun rôle, secret stocké en <b>vérificateur SCRAM-SHA-256 calculé par l'outil</b> ;</item>
///   <item>la base, <b>propriété du migrateur</b>, fermée à <c>PUBLIC</c> ; <c>CONNECT</c> pour l'applicatif et la
///   sauvegarde ; <c>search_path = public</c> pour l'applicatif et le migrateur (P4-5E-B D-08) ;</item>
///   <item>schéma <c>public</c> fermé à <c>PUBLIC</c>, <c>USAGE</c> pour l'applicatif et la sauvegarde ;</item>
///   <item>privilèges par défaut des objets du migrateur : DML sur les tables de <c>public</c> pour l'applicatif
///   (aucun DDL), lecture de toutes les tables, séquences et schémas pour la sauvegarde ;</item>
///   <item>l'historique EF créé <b>vide</b> par le migrateur, avec le script d'EF lui-même, puis réduit à la
///   <b>lecture seule</b> pour l'applicatif (DP-5) — la baseline reste appliquée par <c>migrate</c> (DP-1) ;</item>
///   <item>la preuve que chaque rôle s'authentifie, en TLS VerifyFull et SCRAM-SHA-256.</item>
/// </list>
///
/// <para>
/// <b>Idempotent</b> : une relance fait converger (rôles réalignés, secrets reposés, droits rejoués) sans rien
/// détruire. <b>Refus</b> avant toute écriture si le serveur n'est pas conforme (<see cref="ServerSecurityAudit"/>),
/// si un rôle existant est privilégié ou membre d'un autre rôle, ou si la base existe sous un autre propriétaire.
/// </para>
/// </summary>
public sealed class PostgreSqlProvisioner
{
    private const string SafeRoleAttributes =
        "LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS";

    private const string RoleStateSql =
        "SELECT r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls " +
        "OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m WHERE m.member = r.oid) " +
        "FROM pg_catalog.pg_roles r WHERE r.rolname = @role";

    private const string DatabaseOwnerSql =
        "SELECT pg_catalog.pg_get_userbyid(d.datdba) FROM pg_catalog.pg_database d WHERE d.datname = @database";

    private readonly IMigrationJournal _trace;

    public PostgreSqlProvisioner(IMigrationJournal trace) =>
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));

    /// <summary>Contrôles locaux, sans serveur : message d'erreur, ou <c>null</c>.</summary>
    public static string? Validate(ProvisioningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var names = new[] { request.MigratorRole, request.AppRole, request.BackupRole };
        foreach (var name in names.Append(request.Database))
        {
            if (!PostgreSqlConnectionSettings.IsUsableIdentifier(name))
            {
                return "Nom de rôle ou de base vide, invalide ou trop long.";
            }

            if (name.StartsWith("pg_", StringComparison.OrdinalIgnoreCase) || name.Equals("public", StringComparison.OrdinalIgnoreCase))
            {
                return $"Nom réservé par PostgreSQL refusé : '{name}'.";
            }
        }

        if (names.Append(request.AdminUser).Distinct(StringComparer.Ordinal).Count() != 4)
        {
            return "Les trois rôles MMV et l'administrateur doivent être quatre identités distinctes (RL-3).";
        }

        if (!RoleSecretPolicy.IsValid(request.MigratorPassword) || !RoleSecretPolicy.IsValid(request.AppPassword)
            || !RoleSecretPolicy.IsValid(request.BackupPassword))
        {
            return RoleSecretPolicy.Requirement;
        }

        if (new[] { request.MigratorPassword, request.AppPassword, request.BackupPassword }.Distinct(StringComparer.Ordinal).Count() != 3)
        {
            return "Chaque rôle doit avoir son propre secret.";
        }

        return string.IsNullOrEmpty(request.AdminPassword) ? "Secret administrateur absent." : null;
    }

    public async Task<AdministrationResult> RunAsync(ProvisioningRequest request, CancellationToken cancellationToken = default)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return Finish(request, MigrationExitCode.InvalidArguments, invalid);
        }

        _trace.Write($"provision : ouverture — {request} — opérateur '{request.Operator}'");
        try
        {
            return await ProvisionAsync(request, cancellationToken);
        }
        catch (DatabaseConfigurationException exception)
        {
            return Finish(request, MigrationExitCode.InvalidArguments, exception.Message);
        }
        catch (DbException exception)
        {
            return Finish(request, MigrationExitCode.ProvisioningFailed,
                $"provisioning interrompu ({ServerCommand.Describe(exception)}) ; la relance est idempotente.");
        }
    }

    private async Task<AdministrationResult> ProvisionAsync(ProvisioningRequest request, CancellationToken cancellationToken)
    {
        await using var admin = PostgreSqlConnectionSecurity.CreateConnection(
            ConnectionString(request, request.AdminDatabase, request.AdminUser, request.AdminPassword));
        var unavailable = await OpenAsync(admin, cancellationToken);
        if (unavailable is not null)
        {
            return Finish(request, MigrationExitCode.ServerUnreachable, unavailable);
        }

        // 1. Audit, avant toute écriture.
        var roles = new[] { request.MigratorRole, request.AppRole, request.BackupRole };
        var findings = ServerSecurityAudit.Evaluate(
            await ServerSecurityAudit.ReadAsync(admin, cancellationToken), request.Database, roles).ToList();

        var existingRoles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var unsafeRole = await ServerCommand.ScalarAsync(admin, RoleStateSql, cancellationToken, ("role", role));
            if (unsafeRole is bool privileged)
            {
                existingRoles.Add(role);
                if (privileged)
                {
                    findings.Add($"le rôle existant '{role}' est privilégié ou membre d'un autre rôle (élévation implicite)");
                }
            }
        }

        var owner = await ServerCommand.ScalarAsync(admin, DatabaseOwnerSql, cancellationToken, ("database", request.Database)) as string;
        if (owner is not null && owner != request.MigratorRole)
        {
            findings.Add($"la base '{request.Database}' existe et appartient à '{owner}', pas au migrateur");
        }

        if (findings.Count > 0)
        {
            return Finish(request, MigrationExitCode.SecurityRefused,
                "serveur ou état non conforme, aucune écriture : " + string.Join(" ; ", findings));
        }

        // 2. Rôles (créés, ou réalignés et secret reposé).
        foreach (var (role, secret) in new[]
                 {
                     (request.MigratorRole, request.MigratorPassword),
                     (request.AppRole, request.AppPassword),
                     (request.BackupRole, request.BackupPassword)
                 })
        {
            var verb = existingRoles.Contains(role) ? "ALTER" : "CREATE";
            await ServerCommand.ExecuteServerFormattedAsync(admin,
                $"SELECT format('{verb} ROLE %I WITH {SafeRoleAttributes} PASSWORD %L', @role, @verifier)",
                cancellationToken, ("role", role), ("verifier", ScramSha256Verifier.Create(secret)));
        }

        // 3. Base, propriété du migrateur, fermée à PUBLIC.
        if (owner is null)
        {
            await ServerCommand.ExecuteServerFormattedAsync(admin,
                "SELECT format('CREATE DATABASE %I WITH OWNER %I TEMPLATE template0 ENCODING %L', @database, @owner, 'UTF8')",
                cancellationToken, ("database", request.Database), ("owner", request.MigratorRole));
        }

        await Formatted(admin, cancellationToken, "REVOKE ALL ON DATABASE %I FROM PUBLIC", request.Database);
        await Formatted(admin, cancellationToken, "GRANT CONNECT ON DATABASE %I TO %I", request.Database, request.AppRole);
        await Formatted(admin, cancellationToken, "GRANT CONNECT ON DATABASE %I TO %I", request.Database, request.BackupRole);
        foreach (var role in new[] { request.AppRole, request.MigratorRole })
        {
            await Formatted(admin, cancellationToken, "ALTER ROLE %I IN DATABASE %I SET search_path = public", role, request.Database);
        }

        // 4. Dans la base : schéma public, privilèges par défaut, objets existants (relance).
        await using (var database = PostgreSqlConnectionSecurity.CreateConnection(
                         ConnectionString(request, request.Database, request.AdminUser, request.AdminPassword)))
        {
            await database.OpenAsync(cancellationToken);

            var capturingSchema = await ServerCommand.ScalarAsync(database,
                "SELECT string_agg(nspname, ', ') FROM pg_catalog.pg_namespace WHERE nspname = ANY(@roles)",
                cancellationToken, ("roles", roles)) as string;
            if (capturingSchema is not null)
            {
                return Finish(request, MigrationExitCode.SecurityRefused,
                    $"schéma(s) au nom d'un rôle MMV ({capturingSchema}) : il capterait les tables non qualifiées (D-08)");
            }

            await Formatted(database, cancellationToken, "REVOKE ALL ON SCHEMA public FROM PUBLIC");
            await Formatted(database, cancellationToken, "GRANT USAGE ON SCHEMA public TO %I, %I", request.AppRole, request.BackupRole);
            await Formatted(database, cancellationToken,
                "ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I",
                request.MigratorRole, request.AppRole);
            await Formatted(database, cancellationToken,
                "ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO %I",
                request.MigratorRole, request.AppRole);
            await Formatted(database, cancellationToken,
                "ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT SELECT ON TABLES TO %I", request.MigratorRole, request.BackupRole);
            await Formatted(database, cancellationToken,
                "ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT SELECT ON SEQUENCES TO %I", request.MigratorRole, request.BackupRole);
            await Formatted(database, cancellationToken,
                "ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT USAGE ON SCHEMAS TO %I", request.MigratorRole, request.BackupRole);

            // Objets déjà présents (relance après migrations) : mêmes droits que les privilèges par défaut.
            await Formatted(database, cancellationToken,
                "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO %I", request.AppRole);
            await Formatted(database, cancellationToken,
                "GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO %I", request.AppRole);
            foreach (var schema in await MigratorSchemasAsync(database, request.MigratorRole, cancellationToken))
            {
                await Formatted(database, cancellationToken, "GRANT USAGE ON SCHEMA %I TO %I", schema, request.BackupRole);
                await Formatted(database, cancellationToken, "GRANT SELECT ON ALL TABLES IN SCHEMA %I TO %I", schema, request.BackupRole);
                await Formatted(database, cancellationToken, "GRANT SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I", schema, request.BackupRole);
            }

            // 5. Historique EF, créé vide par le migrateur avec le script d'EF, puis lecture seule pour l'applicatif.
            await CreateHistoryTableAsMigratorAsync(request, cancellationToken);
            await Formatted(database, cancellationToken,
                "REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON TABLE public.%I FROM %I",
                HistoryRepository.DefaultTableName, request.AppRole);
            await Formatted(database, cancellationToken, "GRANT SELECT ON TABLE public.%I TO %I",
                HistoryRepository.DefaultTableName, request.AppRole);
        }

        // 6. Chaque rôle s'authentifie réellement (TLS VerifyFull, SCRAM-SHA-256).
        foreach (var (role, secret) in new[] { (request.AppRole, request.AppPassword), (request.BackupRole, request.BackupPassword) })
        {
            await using var probe = PostgreSqlConnectionSecurity.CreateConnection(ConnectionString(request, request.Database, role, secret));
            var failure = await OpenAsync(probe, cancellationToken);
            if (failure is not null)
            {
                return Finish(request, MigrationExitCode.SecurityRefused, $"le rôle '{role}' ne peut pas se connecter : {failure}");
            }
        }

        return Finish(request, MigrationExitCode.Success,
            $"{request} : provisionnée. Étape suivante : MMV.DatabaseManager migrate (rôle migrateur), puis bootstrap-admin.");
    }

    private async Task CreateHistoryTableAsMigratorAsync(ProvisioningRequest request, CancellationToken cancellationToken)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = ConnectionString(request, request.Database, request.MigratorRole, request.MigratorPassword)
        });

        await using var context = new OpticDbContext(builder.Options);
        var script = context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript();
        await context.Database.ExecuteSqlRawAsync(script, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> MigratorSchemasAsync(DbConnection connection, string migrator, CancellationToken cancellationToken)
    {
        var schemas = new List<string>();
        await using var command = ServerCommand.Create(connection,
            "SELECT nspname FROM pg_catalog.pg_namespace WHERE nspowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @role) " +
            "OR nspname = 'public' ORDER BY nspname", ("role", migrator));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }

    /// <summary>Texte produit par le serveur à partir d'un gabarit à <c>%I</c> positionnels (au plus quatre).</summary>
    private static Task Formatted(DbConnection connection, CancellationToken cancellationToken, string template, params string[] identifiers)
    {
        var placeholders = string.Concat(identifiers.Select((_, i) => $", @p{i}"));
        return ServerCommand.ExecuteServerFormattedAsync(connection,
            $"SELECT format('{template}'{placeholders})", cancellationToken,
            identifiers.Select((value, i) => ($"p{i}", (object?)value)).ToArray());
    }

    /// <summary>Ouvre ; renvoie un message d'indisponibilité classé (D-15), ou <c>null</c>.</summary>
    internal static async Task<string?> OpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.OpenAsync(cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reason = PostgreSqlConnectivityProbe.Classify(exception);
            return PostgreSqlConnectivityProbe.Message(reason, PostgreSqlConnectivityProbe.Describe(connection.ConnectionString));
        }
    }

    internal static string ConnectionString(ProvisioningRequest request, string database, string user, string secret) =>
        PostgreSqlConnectionSecurity.BuildConnectionString(
            new PostgreSqlConnectionSettings(request.Host, request.Port, database, user, secret, request.RootCertificatePath));

    private AdministrationResult Finish(ProvisioningRequest request, MigrationExitCode code, string message)
    {
        _trace.Write($"provision : {(code == MigrationExitCode.Success ? "succès" : $"échec code {(int)code}")} — {message}");
        return new AdministrationResult(code, message);
    }
}
