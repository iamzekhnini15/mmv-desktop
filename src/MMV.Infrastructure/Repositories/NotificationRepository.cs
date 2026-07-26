using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des notifications.
/// </summary>
public class NotificationRepository : BaseRepository<Notification, long>, INotificationRepository
{
    public NotificationRepository(OpticDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        // Écriture ensembliste : une seule requête serveur, aucune matérialisation, aucun tracking. Deux postes
        // concurrents convergent vers le même état (la cible est une constante, pas une valeur relue).
        //
        // ResolvedAt n'est délibérément PAS touché : marquer comme lu n'a AUCUN effet sur le cycle de vie métier de
        // l'alerte. C'est précisément la confusion que P3-8 supprime — avant, « tout marquer comme lu » rendait
        // l'anti-doublon aveugle et la génération suivante recréait l'intégralité du jeu d'alertes.
        await _context.Notifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountUnreadAsync(CancellationToken cancellationToken = default)
    {
        // « Non lue ET non résolue » (P3-8) : le badge doit refléter des problèmes ACTIFS. Une alerte portant sur un
        // produit réapprovisionné depuis longtemps ne doit plus être comptée, même si personne ne l'a jamais ouverte.
        // Les faits historiques (transition, encaissement, information) ne sont jamais résolus : pour eux, le
        // prédicat se réduit à l'ancien comportement.
        return await GetQueryable()
            .CountAsync(n => !n.IsRead && n.ResolvedAt == null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetActiveLowStockEntityIdsAsync(CancellationToken cancellationToken = default)
    {
        // Projection minimale : seuls les identifiants traversent la frontière SQL. Le prédicat reprend exactement
        // les colonnes de l'index unique filtré, qui sert donc aussi cette lecture.
        return await GetQueryable()
            .Where(n => n.Type == NotificationTypes.LowStock
                        && n.EntityType == NotificationEntityTypes.Product
                        && n.EntityId != null
                        && n.ResolvedAt == null)
            .Select(n => n.EntityId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ResolveActiveLowStockAsync(
        IReadOnlyCollection<long> productIds,
        DateTime resolvedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        // Lot vide ⇒ aucune requête émise. Le cas est fréquent (régime permanent : rien à résoudre) et ne doit pas
        // coûter un aller-retour.
        if (productIds.Count == 0)
        {
            return 0;
        }

        var ids = productIds.ToArray();

        // Une SEULE mise à jour conditionnelle pour tout le lot. La condition « ResolvedAt IS NULL » rend l'appel
        // idempotent : une seconde réconciliation ne réécrit pas la date de résolution déjà posée.
        return await _context.Notifications
            .Where(n => n.Type == NotificationTypes.LowStock
                        && n.EntityType == NotificationEntityTypes.Product
                        && n.ResolvedAt == null
                        && n.EntityId != null
                        && ids.Contains(n.EntityId.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.ResolvedAt, resolvedAt),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateActiveLowStockAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Garde de contrat : cette primitive est spécialisée. Elle ne doit jamais servir à insérer un fait
        // historique, qui échapperait alors à l'index unique filtré et créerait l'illusion d'une protection.
        if (notification.Type != NotificationTypes.LowStock
            || notification.EntityType != NotificationEntityTypes.Product
            || notification.EntityId is null)
        {
            throw new ArgumentException(
                "TryCreateActiveLowStockAsync n'accepte qu'une alerte LowStock/Product portant un EntityId.",
                nameof(notification));
        }

        // Insertion ATOMIQUE : l'existence et l'écriture sont évaluées par la MÊME instruction. Un AnyAsync suivi
        // d'un Add laisserait une fenêtre entre la lecture et l'écriture, dans laquelle un second poste insère.
        // Les deux dialectes ci-dessous (§ ActiveLowStockInsertSql) restent, chacun, UNE SEULE instruction : la
        // décision « créer ou refuser » appartient toujours à la base, jamais à l'application.
        //
        // Les PARAMÈTRES sont fabriqués par le provider lui-même (§ CreateProviderParameter) et non par un type
        // concret : construire des `SqliteParameter` en dur faisait échouer la primitive dès l'ajout à la
        // collection de commandes d'un autre provider (InvalidCastException), avant même d'atteindre le moteur.
        var connection = _context.Database.GetDbConnection();
        using var parameterFactory = connection.CreateCommand();

        var parameters = new object[]
        {
            CreateProviderParameter(parameterFactory, "@type", notification.Type),
            CreateProviderParameter(parameterFactory, "@title", notification.Title),
            CreateProviderParameter(parameterFactory, "@message", notification.Message),
            CreateProviderParameter(parameterFactory, "@entityId", notification.EntityId.Value),
            CreateProviderParameter(parameterFactory, "@entityType", notification.EntityType),
            CreateProviderParameter(parameterFactory, "@isRead", notification.IsRead),
            CreateProviderParameter(parameterFactory, "@createdAt", notification.CreatedAt),
        };

        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
            ActiveLowStockInsertSql(_context.Database.ProviderName),
            parameters,
            cancellationToken);

        // 1 ⇒ l'alerte a été ouverte. 0 ⇒ une alerte active existait déjà pour cette clé (un autre poste a gagné la
        // course) : refus métier silencieux, jamais une erreur technique remontée à l'Application.
        return rowsAffected == 1;
    }

    /// <summary>
    /// Fabrique un paramètre AVEC LE TYPE DU PROVIDER COURANT, en le demandant à une commande créée par la
    /// connexion elle-même. C'est la seule fabrication de paramètre qui reste correcte quel que soit le provider :
    /// <c>SqliteCommand</c> rend un <c>SqliteParameter</c>, <c>NpgsqlCommand</c> un <c>NpgsqlParameter</c>,
    /// <c>SqlCommand</c> un <c>SqlParameter</c>. Aucune référence de paquet provider n'est nécessaire ici.
    ///
    /// <para><b>La valeur est affectée telle quelle, y compris <c>null</c>.</b> Ce n'est pas un oubli : un
    /// paramètre requis non renseigné doit rester une ERREUR remontée par le client ADO.NET, jamais un
    /// <c>NULL</c> silencieusement substitué qui transformerait un bug d'écriture en refus métier
    /// indiscernable d'un doublon.</para>
    /// </summary>
    private static DbParameter CreateProviderParameter(DbCommand factory, string name, object? value)
    {
        var parameter = factory.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    /// <summary>
    /// Dialecte d'insertion conditionnelle, en UNE instruction, pour le provider courant.
    ///
    /// <para><b>SQLite et PostgreSQL — <c>ON CONFLICT DO NOTHING</c>.</b> Et NON « INSERT OR IGNORE » : la forme
    /// upsert n'absorbe que les violations d'UNICITÉ. Les violations NOT NULL, CHECK et de clé étrangère
    /// continuent de lever, donc de remonter comme erreur Infrastructure normale — alors qu'« OR IGNORE » les
    /// masquerait toutes en silence et transformerait un bug d'écriture en ligne manquante indétectable. Aucun
    /// catch générique n'est employé. Le conflit visé est l'index unique filtré
    /// (Type, EntityType, EntityId) WHERE actif : sans cible explicite, DO NOTHING s'applique à toutes les
    /// contraintes d'unicité, index partiels compris.</para>
    ///
    /// <para><b>SQL Server — <c>INSERT … SELECT … WHERE NOT EXISTS</c> avec <c>UPDLOCK, HOLDLOCK</c>.</b>
    /// SQL Server ne possède PAS d'équivalent de <c>ON CONFLICT</c>. Ce n'est pas un « check-then-act » : le test
    /// d'existence et l'insertion sont évalués par la MÊME instruction, dans la MÊME transaction implicite, et les
    /// indices de verrouillage posent un verrou de plage sur la clé absente — c'est donc la base, et elle seule,
    /// qui arbitre la course. L'index unique filtré reste le dernier rempart. Cette forme exacte est celle
    /// mesurée par le spike P4-1 (E5), 10 tours concurrents sans anomalie ni interblocage.</para>
    ///
    /// <para>Un provider non reconnu lève : mieux vaut un échec explicite qu'un dialecte deviné qui perdrait
    /// silencieusement la garantie d'unicité.</para>
    /// </summary>
    private static string ActiveLowStockInsertSql(string? providerName) => providerName switch
    {
        SqliteProviderName or PostgresProviderName =>
            "INSERT INTO \"Notifications\" " +
            "(\"Type\", \"Title\", \"Message\", \"EntityId\", \"EntityType\", \"IsRead\", \"CreatedAt\", \"ResolvedAt\") " +
            "VALUES (@type, @title, @message, @entityId, @entityType, @isRead, @createdAt, NULL) " +
            "ON CONFLICT DO NOTHING;",

        SqlServerProviderName =>
            "INSERT INTO [Notifications] " +
            "([Type], [Title], [Message], [EntityId], [EntityType], [IsRead], [CreatedAt], [ResolvedAt]) " +
            "SELECT @type, @title, @message, @entityId, @entityType, @isRead, @createdAt, NULL " +
            "WHERE NOT EXISTS (SELECT 1 FROM [Notifications] WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [Type] = @type AND [EntityType] = @entityType AND [EntityId] = @entityId " +
            "AND [ResolvedAt] IS NULL);",

        _ => throw new NotSupportedException(
            $"TryCreateActiveLowStockAsync : aucun dialecte d'insertion atomique connu pour le provider " +
            $"« {providerName ?? "(aucun)"} ». Ajouter le dialecte avant d'utiliser ce provider — la garantie " +
            "d'unicité de l'alerte LowStock active ne doit jamais reposer sur un dialecte deviné.")
    };

    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
}
