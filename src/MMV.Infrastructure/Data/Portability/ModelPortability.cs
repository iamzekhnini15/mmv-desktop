namespace MMV.Infrastructure.Data.Portability;

/// <summary>
/// <b>Point de sélection UNIQUE</b> des constructions du modèle EF qui dépendent du moteur de base de
/// données (P4-5C — ADR-PROD-DB-003 §5.5, ADR-PROD-DB-006 §5.2 / X2).
///
/// <para>
/// Le modèle MMV est unique et sert deux providers : SQLite (développement, tests, démonstration,
/// mono-poste local) et PostgreSQL (serveur de production V1 multi-poste, ADR-PROD-DB-002). Deux
/// constructions du modèle ne peuvent pas être écrites de la même façon des deux côtés : le
/// <b>type physique des colonnes monétaires</b> et le <b>filtre de l'index unique partiel</b> des fiches
/// atelier. Cette classe est le seul endroit où ce choix est fait.
/// </para>
///
/// <para>
/// <b>Règle d'emploi.</b> Aucune <c>IEntityTypeConfiguration</c> n'interroge le provider pour son propre
/// compte : le nom du provider est lu <b>une seule fois</b>, dans
/// <see cref="OpticDbContext.OnModelCreating"/>, et l'instance résultante est passée aux configurations qui
/// en ont besoin. C'est la transposition, au modèle, du motif déjà en production et prouvé par le spike
/// P4-1 (E5) dans <c>NotificationRepository.ActiveLowStockInsertSql</c>.
/// </para>
///
/// <para>
/// <b>Un provider inconnu lève.</b> Ni filtre deviné, ni type deviné : une forme fausse d'index partiel ne
/// casse aucun test fonctionnel — elle ne se voit qu'en concurrence, et trop tard
/// (ADR-PROD-DB-006 §5.1, §7.2).
/// </para>
/// </summary>
public sealed class ModelPortability
{
    /// <summary>Nom d'assembly du provider EF SQLite.</summary>
    public const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>Nom d'assembly du provider EF PostgreSQL (Npgsql).</summary>
    public const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Précision décidée pour toute colonne monétaire (ADR-PROD-DB-003 §5.2). Douze chiffres couvrent
    /// 9 999 999 999,99 €. Sur PostgreSQL ⇒ <c>numeric(12,2)</c>, exact en base 10.
    /// </summary>
    public const int MoneyPrecision = 12;

    /// <summary>Échelle décidée pour toute colonne monétaire : deux décimales, cohérent avec <c>Money</c>.</summary>
    public const int MoneyScale = 2;

    /// <summary>
    /// Type physique historique des 11 colonnes monétaires SQLite déclarées <c>REAL</c> avant P4-5C.
    /// Conservé <b>tel quel sur SQLite seulement</b> — voir <see cref="MoneyStoreType"/>.
    /// </summary>
    private const string SqliteRealStoreType = "REAL";

    /// <summary>Forme SQLite du filtre : SQLite n'a pas de booléen, <c>IsCurrent</c> y est un <c>INTEGER</c> 0/1.</summary>
    private const string SqliteCurrentWorkshopSheetFilter = "\"IsCurrent\" = 1";

    /// <summary>
    /// Forme PostgreSQL du filtre : <c>IsCurrent</c> y est une colonne <c>boolean</c>, et PostgreSQL refuse
    /// toute comparaison booléen ↔ entier (aucune conversion implicite). <c>"IsCurrent" = 1</c> y ferait
    /// <b>échouer la création de l'index, donc la création du schéma</b> (ADR-PROD-DB-006 §2.2).
    /// </summary>
    private const string PostgreSqlCurrentWorkshopSheetFilter = "\"IsCurrent\"";

    private readonly bool _preservesLegacySqliteStoreTypes;

    private ModelPortability(
        string providerName,
        bool preservesLegacySqliteStoreTypes,
        string currentWorkshopSheetIndexFilter)
    {
        ProviderName = providerName;
        _preservesLegacySqliteStoreTypes = preservesLegacySqliteStoreTypes;
        CurrentWorkshopSheetIndexFilter = currentWorkshopSheetIndexFilter;
    }

    /// <summary>Nom d'assembly du provider EF pour lequel ce modèle est construit.</summary>
    public string ProviderName { get; }

    /// <summary>
    /// Filtre de l'index unique partiel <c>idx_workshop_sheets_current_unique</c> — « au plus UNE version
    /// courante par commande ». La garantie reste arbitrée par la base sur les deux moteurs : seule son
    /// écriture change (ADR-PROD-DB-006 §5.1, §5.3).
    /// </summary>
    public string CurrentWorkshopSheetIndexFilter { get; }

    /// <summary>
    /// Construit le point de sélection pour le provider courant.
    /// </summary>
    /// <param name="providerName">
    /// Valeur de <c>DbContext.Database.ProviderName</c>, lue une seule fois dans <c>OnModelCreating</c>.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// Si le provider n'est pas reconnu. Le modèle n'est alors <b>pas</b> construit avec des valeurs
    /// devinées : mieux vaut un échec explicite au démarrage qu'un index partiel silencieusement faux ou
    /// un montant silencieusement dégradé.
    /// </exception>
    public static ModelPortability For(string? providerName) => providerName switch
    {
        SqliteProviderName => new ModelPortability(
            SqliteProviderName,
            preservesLegacySqliteStoreTypes: true,
            SqliteCurrentWorkshopSheetFilter),

        PostgreSqlProviderName => new ModelPortability(
            PostgreSqlProviderName,
            preservesLegacySqliteStoreTypes: false,
            PostgreSqlCurrentWorkshopSheetFilter),

        _ => throw new NotSupportedException(
            $"Modèle EF MMV : aucune règle de portabilité connue pour le provider " +
            $"« {providerName ?? "(aucun)"} ». Déclarer explicitement le type monétaire ET le filtre " +
            "d'index partiel de ce provider avant de l'utiliser — un filtre partiel deviné ne fait " +
            "échouer aucun test fonctionnel, il ne se voit qu'en concurrence.")
    };

    /// <summary>
    /// Type physique à imposer à une colonne monétaire, ou <c>null</c> pour laisser le provider appliquer
    /// son mapping par défaut de <c>decimal</c> — c'est-à-dire, avec la précision déclarée par
    /// <see cref="MoneyMappingExtensions.HasMoneyMapping"/>, <c>numeric(12,2)</c> sur PostgreSQL.
    ///
    /// <para>
    /// <b>Sur SQLite, le type physique d'aujourd'hui est reconduit à l'identique</b> — et non aligné sur
    /// PostgreSQL. Deux raisons mesurées (ADR-PROD-DB-003 §2.3, §5.4) : (a) basculer les colonnes
    /// concernées sur <c>TEXT</c> transformerait la primitive CAS <c>"RemainingAmount" &gt; 0</c>
    /// (SaleRepository) en <b>comparaison lexicographique</b>, où <c>'0.00' &gt; '0'</c> — soit
    /// l'autorisation silencieuse d'un second règlement d'un solde déjà réglé ; (b) cela imposerait un
    /// rebuild de table à toutes les bases SQLite déjà déployées, pour un provider qui n'est pas celui de
    /// la production. La dette monétaire SQLite est <b>enregistrée et assumée</b>, pas résolue
    /// (ADR-PROD-DB-003 §5.6, §7.2).
    /// </para>
    /// </summary>
    /// <param name="legacySqliteStoreType">
    /// Type physique que la colonne porte <b>aujourd'hui sur SQLite</b>. C'est un constat du dépôt
    /// (ADR-PROD-DB-003 §2.1), déclaré au site d'appel ; la décision de l'appliquer ou non appartient à
    /// cette méthode, et à elle seule.
    /// </param>
    public string? MoneyStoreType(LegacySqliteMoneyStoreType legacySqliteStoreType)
        => _preservesLegacySqliteStoreTypes && legacySqliteStoreType == LegacySqliteMoneyStoreType.Real
            ? SqliteRealStoreType
            : null;
}
