using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Contrôle physique ciblé (P2A-1R19-R2) : détecte si une base SQLite porte encore, sur le schéma RÉEL,
/// les anciens <c>DEFAULT</c> <see cref="DateTime"/> figés au build (anti-pattern R-19) sur les 7 colonnes
/// corrigées par la migration <c>FixDateTimeDefaultValues</c>.
///
/// Le portail de compatibilité <see cref="SqliteSchemaVerifier"/> compare la structure
/// (tables/colonnes/types/nullabilité/PK/FK/index) mais <b>pas</b> les valeurs <c>DEFAULT</c>. Une base
/// historique créée par <c>EnsureCreated</c> <b>avant</b> P2A-1R19 est donc structurellement « compatible »
/// tout en conservant les anciens <c>DEFAULT</c> physiques. Ce vérificateur fournit le signal manquant
/// pour éviter un baseline mensonger (marquer <c>FixDateTimeDefaultValues</c> appliquée sans l'exécuter).
///
/// <b>Lecture seule</b> : interroge <c>PRAGMA table_info</c> ; ne modifie jamais la base.
/// </summary>
public sealed class SqliteDateTimeDefaultVerifier
{
    /// <summary>
    /// Les 7 colonnes (table, colonne) dont P2A-1R19 a supprimé le <c>DEFAULT</c> <see cref="DateTime"/>
    /// figé. Après correction, aucune de ces colonnes ne doit porter de <c>dflt_value</c> en base.
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string Column)> CorrectedDateTimeColumns = new[]
    {
        ("Users", "CreatedAt"),
        ("Customers", "CreatedAt"),
        ("Customers", "UpdatedAt"),
        ("Orders", "OrderDate"),
        ("Sales", "SaleDate"),
        ("Prescriptions", "CreatedAt"),
        ("StockMovements", "CreatedAt"),
    };

    /// <summary>
    /// Renvoie, parmi les 7 colonnes corrigées par P2A-1R19, celles qui portent encore un <c>DEFAULT</c>
    /// physique (<c>dflt_value</c> non nul) — héritage R-19. Liste <b>vide</b> si la base est saine.
    /// Les colonnes/tables absentes sont ignorées (base partielle, jamais une fausse alerte).
    /// </summary>
    public IReadOnlyList<string> GetColumnsWithLegacyDefaults(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

            var existingTables = new HashSet<string>(ReadTableNames(connection), StringComparer.OrdinalIgnoreCase);
            var columnsWithDefaults = new List<string>();

            foreach (var (table, column) in CorrectedDateTimeColumns)
            {
                if (!existingTables.Contains(table))
                {
                    continue;
                }

                if (ColumnHasDefault(connection, table, column))
                {
                    columnsWithDefaults.Add($"{table}.{column}");
                }
            }

            return columnsWithDefaults;
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    /// <summary>
    /// Vrai si au moins une des 7 colonnes corrigées porte encore un <c>DEFAULT</c> <see cref="DateTime"/>
    /// figé (base antérieure à P2A-1R19 nécessitant la réparation R-19).
    /// </summary>
    public bool HasLegacyDateTimeDefaults(OpticDbContext context)
        => GetColumnsWithLegacyDefaults(context).Count > 0;

    private static bool ColumnHasDefault(DbConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);             // name
            if (!string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Colonne 4 = dflt_value : NULL si la colonne n'a aucune valeur par défaut.
            return !reader.IsDBNull(4);
        }

        return false;
    }

    private static List<string> ReadTableNames(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
