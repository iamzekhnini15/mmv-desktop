using System.Data.Common;
using MMV.Infrastructure.Configuration;

namespace MMV.Infrastructure.Data;

/// <summary>Écran de blocage d'un poste (P4-6C, DP-4.2) : ce qui ne va pas, et ce qu'il faut faire.</summary>
/// <param name="Title">Titre court.</param>
/// <param name="Message">Constat (sans secret).</param>
/// <param name="Action">Ce que l'utilisateur ou l'opérateur doit faire.</param>
public sealed record ServerStartupBlock(string Title, string Message, string Action);

/// <summary>
/// Contrôle de démarrage d'un poste PostgreSQL (P4-6C ; ADR-PROD-DB-009 DP-4, ADR-PROD-DB-010 D-15) — une seule
/// séquence, en lecture seule, <b>avant</b> toute autre opération :
/// <list type="number">
///   <item>disponibilité, une tentative bornée, identité non privilégiée (<see cref="PostgreSqlConnectivityProbe"/>) ;</item>
///   <item>garde de compatibilité (<see cref="ServerSchemaCompatibilityGuard"/>) avec les migrations connues de cette
///   build et sa version.</item>
/// </list>
/// Rend <c>null</c> si le poste peut démarrer (E1, E3a), sinon l'écran de blocage. Aucune erreur serveur n'est
/// avalée (K-13) : chacune devient un blocage affiché. Aucune nouvelle tentative (D-15), aucune écriture, aucune
/// migration, aucun seed (DP-4.1, D-12.3).
/// </summary>
public static class ServerStartupCheck
{
    public static ServerStartupBlock? Run(string connectionString, Func<OpticDbContext> createContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(createContext);

        try
        {
            PostgreSqlConnectivityProbe.EnsureAvailable(connectionString);
        }
        catch (DatabaseUnavailableException exception)
        {
            return Unavailable(exception);
        }

        ServerSchemaVerdict verdict;
        try
        {
            // Hors du contexte de synchronisation de l'interface : aucune continuation ne l'attend.
            verdict = Task.Run(async () =>
            {
                await using var context = createContext();
                return await ServerSchemaCompatibilityGuard.EvaluateAsync(context);
            }).GetAwaiter().GetResult();
        }
        catch (DbException exception)
        {
            return new ServerStartupBlock("Base centrale illisible",
                $"L'état de la base n'a pas pu être lu ({exception.GetType().Name}{(exception.SqlState is { } state ? " " + state : string.Empty)}).",
                "Prévenez l'opérateur : les droits du rôle applicatif ou l'installation de la base sont à vérifier. " +
                "Aucune donnée n'a été modifiée.");
        }

        return For(verdict);
    }

    /// <summary>Écran de blocage d'un verdict ; <c>null</c> si le poste peut démarrer.</summary>
    public static ServerStartupBlock? For(ServerSchemaVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        if (verdict.CanStart)
        {
            return null;
        }

        return verdict.State switch
        {
            ServerSchemaState.E2 => new("Mise à jour de la base requise", verdict.Message,
                "Ce poste est plus récent que la base. L'opérateur doit mettre la base à jour " +
                "(MMV.DatabaseManager migrate, après une sauvegarde vérifiée), puis relancez MMV."),
            ServerSchemaState.E3b => new("Mise à jour du poste requise", verdict.Message,
                "Ce poste est trop ancien pour cette base. Installez la version de MMV en service sur les autres " +
                "postes, puis relancez MMV."),
            ServerSchemaState.E3c => new("Version incompatible", verdict.Message,
                "Ce poste et la base ne proviennent pas de la même suite de versions. Prévenez le support MMV ; " +
                "n'utilisez pas ce poste."),
            ServerSchemaState.E4 => new("Base non installée", verdict.Message,
                "L'opérateur doit installer le schéma (MMV.DatabaseManager migrate), puis relancez MMV."),
            ServerSchemaState.E5 => new(verdict.InitializationIncomplete ? "Installation de la base inachevée" : "Maintenance en cours",
                verdict.Message,
                verdict.InitializationIncomplete
                    ? "L'opérateur doit relancer MMV.DatabaseManager pour terminer l'installation, puis relancez MMV."
                    : "Une mise à jour de la base est en cours. Relancez MMV lorsque l'opérateur l'aura terminée."),
            ServerSchemaState.E7 => new("Base non reconnue", verdict.Message,
                "Cette base n'a pas été installée par MMV. Prévenez l'opérateur ; aucune donnée n'a été modifiée."),
            _ => new("Démarrage bloqué", verdict.Message, "Prévenez l'opérateur.")
        };
    }

    private static ServerStartupBlock Unavailable(DatabaseUnavailableException exception) => new(
        exception.Reason == DatabaseUnavailableReason.PrivilegedIdentity ? "Configuration du poste refusée" : "Base centrale indisponible",
        exception.Message,
        exception.Reason == DatabaseUnavailableReason.PrivilegedIdentity
            ? "Ce poste ne doit utiliser que le rôle applicatif : reconfigurez-le (MMV.DatabaseManager configure-workstation)."
            : "Vérifiez le réseau et le serveur, ou prévenez l'opérateur, puis relancez MMV. Aucune nouvelle tentative " +
              "n'est faite automatiquement.");
}
