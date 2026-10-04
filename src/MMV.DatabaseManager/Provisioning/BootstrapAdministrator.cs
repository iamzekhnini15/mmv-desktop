using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
using MMV.Domain.Validators;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.DatabaseManager.Provisioning;

/// <summary>
/// Création du <b>premier administrateur applicatif</b> d'une base centrale (P4-8, ADR-PROD-DB-010, D-12) — par la
/// procédure d'installation, <b>jamais par <c>MMV.App</c></b> au démarrage.
///
/// <list type="bullet">
///   <item><b>Une seule fois</b> : sous <c>LOCK TABLE "Users" IN EXCLUSIVE MODE</c>, refus si la table contient
///   déjà un compte, quel qu'il soit. Deux exécutions concurrentes ne créent jamais deux comptes.</item>
///   <item><b>Jamais en clair</b> : le secret arrive par l'entrée standard, n'est ni un argument, ni une variable,
///   ni journalisé ; seul son hachage BCrypt, calculé par le service d'authentification du produit, est écrit.</item>
///   <item><b>Schéma vérifié d'abord</b> : baseline appliquée et aucune migration en attente ou inconnue.</item>
/// </list>
///
/// <para>
/// Exécuté avec le rôle <b>migrateur</b> (propriétaire de <c>Users</c>), au moment de l'installation seulement,
/// comme <c>migrate</c> ; le rôle applicatif des postes n'y participe pas.
/// </para>
/// </summary>
public sealed class BootstrapAdministrator
{
    public const string FirstName = "Administrateur";
    public const string LastName = "Initial";

    private readonly IMigrationJournal _trace;

    public BootstrapAdministrator(IMigrationJournal trace) =>
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));

    /// <param name="migratorConnectionString">Chaîne du rôle migrateur, déjà durcie.</param>
    public async Task<AdministrationResult> RunAsync(
        string migratorConnectionString, string username, string secret, string confirmation, string @operator,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migratorConnectionString);

        if (!string.Equals(secret, confirmation, StringComparison.Ordinal))
        {
            return Finish(MigrationExitCode.InvalidArguments, "La confirmation ne correspond pas au mot de passe saisi.");
        }

        var (isStrong, weakness) = UserValidator.ValidatePasswordPolicy(secret);
        if (!isStrong)
        {
            return Finish(MigrationExitCode.InvalidArguments, $"Mot de passe initial refusé : {weakness}");
        }

        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = migratorConnectionString
        });
        await using var context = new OpticDbContext(builder.Options);

        var administrator = new User
        {
            Username = username.Trim(),
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername(username),
            PasswordHash = new AuthenticationService(new UnitOfWork(context)).HashPassword(secret),
            FirstName = FirstName,
            LastName = LastName,
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var validation = new UserValidator().Validate(administrator);
        if (!validation.IsValid)
        {
            return Finish(MigrationExitCode.InvalidArguments, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        _trace.Write($"bootstrap-admin : ouverture — compte '{administrator.Username}' — opérateur '{@operator}'");
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reason = PostgreSqlConnectivityProbe.Classify(exception);
            return Finish(MigrationExitCode.ServerUnreachable,
                PostgreSqlConnectivityProbe.Message(reason, PostgreSqlConnectivityProbe.Describe(migratorConnectionString)));
        }

        try
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            if (applied.Count == 0)
            {
                return Finish(MigrationExitCode.BootstrapRefused,
                    "Schéma non initialisé : exécutez d'abord MMV.DatabaseManager migrate.");
            }

            if (!applied.SetEquals(known))
            {
                return Finish(MigrationExitCode.BootstrapRefused,
                    "Le schéma de la base ne correspond pas à cet outil (migrations en attente ou inconnues).");
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("LOCK TABLE \"Users\" IN EXCLUSIVE MODE", cancellationToken);
            if (await context.Users.AnyAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Finish(MigrationExitCode.BootstrapRefused,
                    "Base déjà initialisée : un compte existe. Le premier administrateur ne se crée qu'une fois.");
            }

            context.Users.Add(administrator);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return Finish(MigrationExitCode.ProvisioningFailed,
                $"création interrompue ({ServerCommand.Describe(exception)}) ; aucun compte n'a été créé.");
        }

        return Finish(MigrationExitCode.Success,
            $"Premier administrateur '{administrator.Username}' créé. Il doit remplacer ce mot de passe initial à sa " +
            "première connexion (Mon profil → Changer le mot de passe) ; il n'est conservé nulle part en clair.");
    }

    private AdministrationResult Finish(MigrationExitCode code, string message)
    {
        _trace.Write($"bootstrap-admin : {(code == MigrationExitCode.Success ? "succès" : $"échec code {(int)code}")} — {message}");
        return new AdministrationResult(code, message);
    }
}
