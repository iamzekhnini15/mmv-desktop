using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MMV.DatabaseManager.Import;

/// <summary>Famille de conversion d'une colonne, déduite de son type physique PostgreSQL.</summary>
public enum ImportColumnKind
{
    SmallInt,
    Integer,
    BigInt,
    Boolean,
    Numeric,
    Double,
    Text,
    Instant,
    CivilDate
}

/// <summary>Colonne cible : nom, type physique exact, nullabilité, bornes et énumération éventuelle.</summary>
public sealed record ImportColumn(
    string Name,
    string StoreType,
    ImportColumnKind Kind,
    bool IsNullable,
    int? MaxLength,
    int Precision,
    int Scale,
    Type? EnumType);

/// <summary>
/// Table cible : colonnes, clé primaire (ordre de lecture et de comparaison) et lignes de données initiales
/// (<c>HasData</c>) que la migration a déjà insérées — O11 : présentes <b>des deux côtés</b>.
/// </summary>
public sealed record ImportTable(
    string Name,
    IReadOnlyList<ImportColumn> Columns,
    IReadOnlyList<int> KeyOrdinals,
    IReadOnlyList<string> SeedKeys)
{
    public IEnumerable<ImportColumn> KeyColumns => KeyOrdinals.Select(i => Columns[i]);
}

/// <summary>
/// Plan d'import (P4-7, O11) : tables applicatives du modèle EF <b>PostgreSQL</b> dans l'ordre des clés étrangères
/// (parent avant enfant). L'historique EF n'est pas une entité du modèle : il n'y figure jamais (ADR-005 §5.8, K-10).
/// Tout type physique inconnu fait lever — aucun type n'est deviné.
/// </summary>
public sealed class ImportPlan
{
    private static readonly Regex Numeric = new(@"^numeric\((\d+),(\d+)\)$", RegexOptions.CultureInvariant);
    private static readonly Regex Varchar = new(@"^character varying\((\d+)\)$", RegexOptions.CultureInvariant);

    private ImportPlan(IReadOnlyList<ImportTable> tables) => Tables = tables;

    /// <summary>Tables dans l'ordre d'insertion (parents d'abord).</summary>
    public IReadOnlyList<ImportTable> Tables { get; }

    /// <param name="designTimeModel">Modèle de conception du contexte configuré pour PostgreSQL.</param>
    public static ImportPlan From(IModel designTimeModel)
    {
        ArgumentNullException.ThrowIfNull(designTimeModel);

        var relational = designTimeModel.GetRelationalModel();
        var tables = relational.Tables.Where(t => t.Schema is null or "public").ToList();
        var byName = tables.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var ordered = new List<ITable>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);

        void Visit(ITable table)
        {
            if (done.Contains(table.Name))
            {
                return;
            }

            if (!visiting.Add(table.Name))
            {
                throw new NotSupportedException($"Cycle de clés étrangères via '{table.Name}' : ordre d'import indéterminable.");
            }

            foreach (var principal in table.ForeignKeyConstraints.Select(fk => fk.PrincipalTable)
                         .Where(p => p.Name != table.Name)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                Visit(byName[principal.Name]);
            }

            if (table.ForeignKeyConstraints.Any(fk => fk.PrincipalTable.Name == table.Name))
            {
                throw new NotSupportedException($"Clé étrangère réflexive sur '{table.Name}' : non prise en charge.");
            }

            visiting.Remove(table.Name);
            done.Add(table.Name);
            ordered.Add(table);
        }

        foreach (var table in tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            Visit(table);
        }

        return new ImportPlan(ordered.Select(t => Build(t, designTimeModel)).ToArray());
    }

    private static ImportTable Build(ITable table, IModel model)
    {
        var columns = table.Columns.OrderBy(c => c.Name, StringComparer.Ordinal).Select(Column).ToArray();
        var key = table.PrimaryKey ?? throw new NotSupportedException($"Table '{table.Name}' sans clé primaire.");
        var ordinals = key.Columns.Select(k => Array.FindIndex(columns, c => c.Name == k.Name)).ToArray();

        var seedKeys = new List<string>();
        foreach (var mapping in table.EntityTypeMappings)
        {
            var entity = model.FindEntityType(mapping.TypeBase.Name)
                         ?? throw new InvalidOperationException($"Entité '{mapping.TypeBase.Name}' absente du modèle.");
            foreach (var seed in entity.GetSeedData())
            {
                seedKeys.Add(KeyText(ordinals.Select(i =>
                {
                    var property = entity.GetProperties().Single(p => p.GetColumnName() == columns[i].Name);
                    return ImportValues.Canonical(columns[i], seed[property.Name]);
                })));
            }
        }

        return new ImportTable(table.Name, columns, ordinals, seedKeys);
    }

    /// <summary>Texte d'une clé (composite ou non), stable et non ambigu.</summary>
    public static string KeyText(IEnumerable<string> parts) => string.Join("|", parts.Select(p => p.Replace("|", "||")));

    private static ImportColumn Column(IColumn column)
    {
        var storeType = column.StoreType;
        var property = column.PropertyMappings.First().Property;
        var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        var enumType = clr.IsEnum ? clr : null;

        if (Numeric.Match(storeType) is { Success: true } numeric)
        {
            return new ImportColumn(column.Name, storeType, ImportColumnKind.Numeric, column.IsNullable, null,
                int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture), null);
        }

        if (Varchar.Match(storeType) is { Success: true } varchar)
        {
            return new ImportColumn(column.Name, storeType, ImportColumnKind.Text, column.IsNullable,
                int.Parse(varchar.Groups[1].Value, CultureInfo.InvariantCulture), 0, 0, enumType);
        }

        var kind = storeType.ToLowerInvariant() switch
        {
            "smallint" => ImportColumnKind.SmallInt,
            "integer" => ImportColumnKind.Integer,
            "bigint" => ImportColumnKind.BigInt,
            "boolean" => ImportColumnKind.Boolean,
            "double precision" => ImportColumnKind.Double,
            "text" => ImportColumnKind.Text,
            "timestamp with time zone" => ImportColumnKind.Instant,
            "date" => ImportColumnKind.CivilDate,
            _ => throw new NotSupportedException(
                $"Type physique '{storeType}' de {column.Table.Name}.{column.Name} non pris en charge par l'import.")
        };

        return new ImportColumn(column.Name, storeType, kind, column.IsNullable, null, 0, 0, enumType);
    }
}
