using System.Data.Common;
using System.Security.Cryptography;
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
/// détruire. Trois phases (audit P4-8, condition M3) :
/// </para>
/// <list type="number">
///   <item><b>A — préflight, lecture seule</b> : tout refus prévisible est prononcé ici, <b>avant toute
///   écriture</b> — serveur non conforme (<see cref="ServerSecurityAudit"/>), rôle existant privilégié, membre d'un
///   rôle, expiré ou fermé, <b>rôle ayant des membres</b> (migrateur, applicatif, sauvegarde, administrateur :
///   condition M1), base d'un autre propriétaire ou fermée aux connexions, schéma au nom d'un rôle MMV (D-08), rôle
///   que <c>pg_hba</c> n'admettrait pas (<see cref="AdmissionAsync"/>).</item>
///   <item><b>B — écritures</b>, en trois unités atomiques : transaction des rôles ; <c>CREATE DATABASE</c> (que
///   PostgreSQL refuse dans une transaction) ; transaction des droits et de l'historique EF dans la base.</item>
///   <item><b>C — preuve</b> : chaque rôle ouvre réellement une session (TLS VerifyFull, SCRAM-SHA-256). Un échec
///   ici n'est <b>pas</b> un refus : les écritures sont faites, le code est
///   <see cref="MigrationExitCode.ProvisioningFailed"/> et la relance est idempotente.</item>
/// </list>
/// </summary>
public sealed class PostgreSqlProvisioner
{
    private const string SafeRoleAttributes =
        "LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS";

    private const string RoleStateSql =
        "SELECT r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls " +
        "OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m WHERE m.member = r.oid) " +
        "OR COALESCE(r.rolvaliduntil < now(), false) OR r.rolconnlimit = 0 " +
        "FROM pg_catalog.pg_roles r WHERE r.rolname = @role";

    /// <summary>
    /// M1 : tout rôle <b>membre</b> d'un rôle MMV ou de l'administrateur hériterait de ses droits (INHERIT), pourrait
    /// l'endosser (SET ROLE) ou en distribuer l'appartenance (ADMIN). Colonnes d'options : PostgreSQL 16+.
    /// </summary>
    private const string MembersSql =
        "SELECT g.rolname, u.rolname, m.inherit_option, m.set_option, m.admin_option " +
        "FROM pg_catalog.pg_auth_members m " +
        "JOIN pg_catalog.pg_roles g ON g.oid = m.roleid JOIN pg_catalog.pg_roles u ON u.oid = m.member " +
        "WHERE g.rolname = ANY(@roles) ORDER BY g.rolname, u.rolname";

    private const string DatabaseStateSql =
        "SELECT pg_catalog.pg_get_userbyid(d.datdba), d.datallowconn AND d.datconnlimit <> 0 " +
        "FROM pg_catalog.pg_database d WHERE d.datname = @database";

    /// <summary>SQLSTATE d'un secret refusé : <c>pg_hba</c> a admis la connexion et demandé l'authentification.</summary>
    private const string InvalidPassword = "28P01";

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

        // ── A. Préflight, lecture seule : tout refus prévisible est prononcé ici, avant toute écriture. ──
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
                    findings.Add($"le rôle existant '{role}' est privilégié, membre d'un autre rôle (élévation implicite), " +
                                 "expiré ou fermé aux connexions");
                }
            }
        }

        findings.AddRange(await MembersAsync(admin, roles.Append(request.AdminUser).ToArray(), cancellationToken));

        var (owner, acceptsConnections) = await DatabaseStateAsync(admin, request.Database, cancellationToken);
        if (owner is not null && owner != request.MigratorRole)
        {
            findings.Add($"la base '{request.Database}' existe et appartient à '{owner}', pas au migrateur");
        }

        if (owner is not null && !acceptsConnections)
        {
            findings.Add($"la base '{request.Database}' refuse les connexions (datallowconn ou datconnlimit)");
        }
        else if (owner is not null)
        {
            var capturingSchema = await CapturingSchemasAsync(request, roles, cancellationToken);
            if (capturingSchema is not null)
            {
                findings.Add($"schéma(s) au nom d'un rôle MMV ({capturingSchema}) : il capterait les tables non qualifiées (D-08)");
            }
        }

        if (findings.Count == 0)
        {
            foreach (var role in roles)
            {
                var refusal = await AdmissionAsync(request, role, cancellationToken);
                if (refusal is not null)
                {
                    findings.Add(refusal);
                }
            }
        }

        if (findings.Count > 0)
        {
            return Finish(request, MigrationExitCode.SecurityRefused,
                "serveur ou état non conforme, aucune écriture : " + string.Join(" ; ", findings));
        }

        // ── B. Écritures : trois unités atomiques. ──
        // B1. Rôles (créés, ou réalignés et secret reposé), une transaction.
        await using (var transaction = await admin.BeginTransactionAsync(cancellationToken))
        {
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

            await transaction.CommitAsync(cancellationToken);
        }

        // B2. Base, propriété du migrateur — CREATE DATABASE ne s'exécute jamais dans une transaction (PostgreSQL).
        if (owner is null)
        {
            await ServerCommand.ExecuteServerFormattedAsync(admin,
                "SELECT format('CREATE DATABASE %I WITH OWNER %I TEMPLATE template0 ENCODING %L', @database, @owner, 'UTF8')",
                cancellationToken, ("database", request.Database), ("owner", request.MigratorRole));
        }

        // B3. Dans la base, une transaction : droits sur la base, schéma public, privilèges par défaut, objets
        //     existants (relance), historique EF.
        await using (var database = PostgreSqlConnectionSecurity.CreateConnection(
                         ConnectionString(request, request.Database, request.AdminUser, request.AdminPassword)))
        {
            await database.OpenAsync(cancellationToken);
            await using var transaction = await database.BeginTransactionAsync(cancellationToken);

            await Formatted(database, cancellationToken, "REVOKE ALL ON DATABASE %I FROM PUBLIC", request.Database);
            await Formatted(database, cancellationToken, "GRANT CONNECT ON DATABASE %I TO %I", request.Database, request.AppRole);
            await Formatted(database, cancellationToken, "GRANT CONNECT ON DATABASE %I TO %I", request.Database, request.BackupRole);
            foreach (var role in new[] { request.AppRole, request.MigratorRole })
            {
                await Formatted(database, cancellationToken, "ALTER ROLE %I IN DATABASE %I SET search_path = public", role, request.Database);
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

            // Historique EF, créé vide AU NOM du migrateur (propriétaire) avec le script d'EF, dans la même
            // transaction, puis réduit à la lecture seule pour l'applicatif (DP-5).
            await Formatted(database, cancellationToken, "SET LOCAL ROLE %I", request.MigratorRole);
            await ServerCommand.NonQueryAsync(database, "SET LOCAL search_path = public", cancellationToken);
            await ServerCommand.NonQueryAsync(database, HistoryCreationScript(request), cancellationToken);
            await ServerCommand.NonQueryAsync(database, "RESET ROLE", cancellationToken);
            await Formatted(database, cancellationToken,
                "REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON TABLE public.%I FROM %I",
                HistoryRepository.DefaultTableName, request.AppRole);
            await Formatted(database, cancellationToken, "GRANT SELECT ON TABLE public.%I TO %I",
                HistoryRepository.DefaultTableName, request.AppRole);

            await transaction.CommitAsync(cancellationToken);
        }

        // ── C. Preuve : chaque rôle s'authentifie réellement (TLS VerifyFull, SCRAM-SHA-256). ──
        // Un échec ici n'est pas un refus : les écritures sont faites ; la relance est idempotente.
        foreach (var (role, secret) in new[]
                 {
                     (request.MigratorRole, request.MigratorPassword),
                     (request.AppRole, request.AppPassword),
                     (request.BackupRole, request.BackupPassword)
                 })
        {
            await using var probe = PostgreSqlConnectionSecurity.CreateConnection(ConnectionString(request, request.Database, role, secret));
            var failure = await OpenAsync(probe, cancellationToken);
            if (failure is not null)
            {
                return Finish(request, MigrationExitCode.ProvisioningFailed,
                    $"écritures appliquées, mais le rôle '{role}' ne peut pas se connecter : {failure} " +
                    "Corrigez la cause puis relancez (idempotent).");
            }
        }

        return Finish(request, MigrationExitCode.Success,
            $"{request} : provisionnée. Étape suivante : MMV.DatabaseManager migrate (rôle migrateur), puis bootstrap-admin.");
    }

    /// <summary>
    /// Preuve d'<b>admission</b> par <c>pg_hba</c>, sans écriture : PostgreSQL choisit la règle <c>pg_hba</c>
    /// <b>avant</b> d'authentifier et <b>avant</b> de vérifier l'existence de la base. Un secret délibérément faux
    /// reçoit donc <c>28P01</c> dès qu'une règle admet le rôle — même inexistant : PostgreSQL simule alors l'échange
    /// SCRAM (anti-énumération) — et <c>28000</c> si <c>pg_hba</c> rejette la connexion. Trace côté serveur : une
    /// tentative d'authentification échouée par rôle.
    /// </summary>
    private static async Task<string?> AdmissionAsync(ProvisioningRequest request, string role, CancellationToken cancellationToken)
    {
        var decoy = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var probe = PostgreSqlConnectionSecurity.CreateConnection(ConnectionString(request, request.Database, role, decoy));
        try
        {
            await probe.OpenAsync(cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var state = SqlState(exception);
            if (state == InvalidPassword)
            {
                return null;
            }

            if (state == "28000")
            {
                return $"pg_hba n'admet pas le rôle '{role}' sur la base '{request.Database}' depuis ce poste";
            }

            var reason = PostgreSqlConnectivityProbe.Classify(exception);
            return $"admission du rôle '{role}' non prouvée : " +
                   PostgreSqlConnectivityProbe.Message(reason, PostgreSqlConnectivityProbe.Describe(probe.ConnectionString));
        }
    }

    private static string? SqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } state })
            {
                return state;
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> MembersAsync(DbConnection admin, string[] roles, CancellationToken cancellationToken)
    {
        var findings = new List<string>();
        await using var command = ServerCommand.Create(admin, MembersSql, ("roles", roles));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var options = new[] { (reader.GetBoolean(2), "INHERIT"), (reader.GetBoolean(3), "SET"), (reader.GetBoolean(4), "ADMIN") }
                .Where(o => o.Item1).Select(o => o.Item2).ToList();
            findings.Add($"le rôle '{reader.GetString(1)}' est membre de '{reader.GetString(0)}' " +
                         $"({(options.Count == 0 ? "aucune option" : string.Join(", ", options))}) : il hériterait de ses droits, " +
                         "pourrait l'endosser ou en transmettre l'appartenance — retirez cette appartenance, l'outil n'en retire aucune (M1)");
        }

        return findings;
    }

    private static async Task<(string? Owner, bool AcceptsConnections)> DatabaseStateAsync(
        DbConnection admin, string database, CancellationToken cancellationToken)
    {
        await using var command = ServerCommand.Create(admin, DatabaseStateSql, ("database", database));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetBoolean(1)) : (null, true);
    }

    /// <summary>Schémas de la base existante portant le nom d'un rôle MMV (D-08) — lecture seule.</summary>
    private static async Task<string?> CapturingSchemasAsync(ProvisioningRequest request, string[] roles, CancellationToken cancellationToken)
    {
        await using var database = PostgreSqlConnectionSecurity.CreateConnection(
            ConnectionString(request, request.Database, request.AdminUser, request.AdminPassword));
        await database.OpenAsync(cancellationToken);
        return await ServerCommand.ScalarAsync(database,
            "SELECT string_agg(nspname, ', ') FROM pg_catalog.pg_namespace WHERE nspname = ANY(@roles)",
            cancellationToken, ("roles", roles)) as string;
    }

    /// <summary>Script de création de l'historique produit par EF lui-même (aucune connexion ouverte).</summary>
    private static string HistoryCreationScript(ProvisioningRequest request)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = ConnectionString(request, request.Database, request.MigratorRole, request.MigratorPassword)
        });

        using var context = new OpticDbContext(builder.Options);
        return context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript();
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
