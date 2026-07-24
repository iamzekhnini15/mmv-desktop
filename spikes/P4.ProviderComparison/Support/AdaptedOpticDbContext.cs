using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MMV.Infrastructure.Data;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Variante de mapping monétaire testée par le spike. AUCUNE de ces variantes n'est un choix :
/// elles servent uniquement à mesurer l'écart entre le mapping ACTUEL et un mapping candidat.
/// </summary>
public enum MoneyMapping
{
    /// <summary>Modèle de production tel quel : <c>HasColumnType("REAL")</c>.</summary>
    AsIs,

    /// <summary>Candidat exact décimal, à titre de comparaison seulement.</summary>
    ExactDecimal
}

/// <summary>
/// Contexte de SPIKE dérivé du VRAI <see cref="OpticDbContext"/>.
///
/// IMPORTANT — portée de la preuve : ce type ne modifie PAS <c>src/**</c>. Il applique, APRÈS
/// <c>base.OnModelCreating</c>, le minimum d'adaptations nécessaires pour qu'un provider serveur
/// accepte le modèle. Chaque adaptation appliquée est donc, littéralement, une MESURE : c'est le
/// travail que P4-2 devra faire dans le modèle de production.
///
/// Les adaptations réellement appliquées sont exposées par <see cref="AppliedAdaptations"/> afin
/// d'être consignées dans le rapport.
/// </summary>
public class AdaptedOpticDbContext : OpticDbContext
{
    private static readonly List<string> Applied = new();

    private readonly ProviderKind _provider;
    private readonly MoneyMapping _money;

    public AdaptedOpticDbContext(DbContextOptions<OpticDbContext> options, ProviderKind provider, MoneyMapping money)
        : base(options)
    {
        _provider = provider;
        _money = money;
    }

    /// <summary>Exposé pour la clé de cache du modèle (<see cref="SpikeModelCacheKeyFactory"/>).</summary>
    public ProviderKind Provider => _provider;

    /// <summary>Exposé pour la clé de cache du modèle (<see cref="SpikeModelCacheKeyFactory"/>).</summary>
    public MoneyMapping Money => _money;

    /// <summary>
    /// Adaptations réellement appliquées POUR CE PROVIDER. Le filtrage par provider est
    /// indispensable : sans lui, une adaptation PostgreSQL serait faussement attribuée à
    /// SQL Server dans le rapport (les deux tests partagent le même processus).
    /// </summary>
    public static IReadOnlyList<string> AdaptationsFor(ProviderKind provider)
    {
        var prefix = provider + "|";
        lock (Applied)
        {
            return Applied
                .Where(a => a.StartsWith(prefix, StringComparison.Ordinal))
                .Select(a => a[prefix.Length..])
                .Distinct()
                .ToList();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        AdaptFilteredIndexes(modelBuilder);
        AdaptMoney(modelBuilder);
    }

    /// <summary>
    /// Les filtres d'index sont du SQL LITTÉRAL écrit pour SQLite (P4-0 §11). Sur PostgreSQL, le
    /// booléen est un type natif : <c>"IsCurrent" = 1</c> échoue en 42883
    /// (<c>operator does not exist: boolean = integer</c>).
    /// </summary>
    private void AdaptFilteredIndexes(ModelBuilder modelBuilder)
    {
        if (_provider != ProviderKind.Postgres)
        {
            return;
        }

        foreach (var index in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetIndexes()))
        {
            var filter = index.GetFilter();
            if (string.IsNullOrWhiteSpace(filter))
            {
                continue;
            }

            var adapted = filter
                .Replace("\"IsCurrent\" = 1", "\"IsCurrent\"")
                .Replace("\"IsRead\" = 1", "\"IsRead\"")
                .Replace("\"IsActive\" = 1", "\"IsActive\"")
                .Replace("\"IsArchived\" = 1", "\"IsArchived\"");

            if (!string.Equals(adapted, filter, StringComparison.Ordinal))
            {
                index.SetFilter(adapted);
                Record($"Index '{index.GetDatabaseName()}' : filtre booléen réécrit « {filter} » -> « {adapted} »");
            }
        }
    }

    /// <summary>
    /// Applique la variante de mapping monétaire mesurée par E2. En <see cref="MoneyMapping.AsIs"/>,
    /// RIEN n'est changé : le modèle de production est exercé tel quel.
    /// </summary>
    private void AdaptMoney(ModelBuilder modelBuilder)
    {
        if (_money != MoneyMapping.ExactDecimal)
        {
            return;
        }

        var exactType = _provider == ProviderKind.Postgres ? "numeric(18,2)" : "decimal(18,2)";

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clr != typeof(decimal))
                {
                    continue;
                }

                if (string.Equals(property.GetColumnType(), "REAL", StringComparison.OrdinalIgnoreCase))
                {
                    property.SetColumnType(exactType);
                    Record($"Montant '{entity.ShortName()}.{property.Name}' : REAL -> {exactType} (variante de mesure)");
                }
            }
        }
    }

    private void Record(string message)
    {
        lock (Applied)
        {
            Applied.Add($"{_provider}|{message}");
        }
    }

    /// <summary>
    /// Fabrique un contexte adapté sur la base jetable fournie.
    /// </summary>
    public static AdaptedOpticDbContext Create(SpikeDatabase database, MoneyMapping money)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();

        if (database.Provider == ProviderKind.Postgres)
        {
            builder.UseNpgsql(database.ConnectionString);
        }
        else
        {
            builder.UseSqlServer(database.ConnectionString);
        }

        // Indispensable : sans clé de cache distincte, les variantes de mapping partageraient un
        // seul modèle et les mesures seraient fausses.
        builder.ReplaceService<IModelCacheKeyFactory, SpikeModelCacheKeyFactory>();

        return new AdaptedOpticDbContext(builder.Options, database.Provider, money);
    }
}
