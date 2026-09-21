using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MMV.Infrastructure.Data.Time;

/// <summary>Ce qu'une reprise a fait — ou aurait fait — sur une colonne.</summary>
public sealed class CivilDateColumnRepair
{
    public required string Table { get; init; }
    public required string Column { get; init; }

    /// <summary>Lignes réécrites par troncature (format historique <c>DateTime</c> ⇒ date civile).</summary>
    public int TruncatedRows { get; init; }

    /// <summary>Lignes remises en forme sans changement de valeur.</summary>
    public int NormalizedRows { get; init; }

    /// <summary>Lignes laissées intactes faute de règle sûre.</summary>
    public int UnrepairableRows { get; init; }

    /// <summary>Total réécrit sur la colonne.</summary>
    public int RewrittenRows => TruncatedRows + NormalizedRows;
}

/// <summary>Rapport de reprise, destiné au journal de migration et au support.</summary>
public sealed class CivilDateRepairReport
{
    /// <summary>Vrai si la reprise a été simulée sans rien écrire.</summary>
    public bool DryRun { get; init; }

    /// <summary>Vrai si au moins une écriture a été réalisée (toujours faux en simulation).</summary>
    public bool Applied { get; init; }

    /// <summary>Audit réalisé avant toute écriture — l'état constaté, pas l'état supposé.</summary>
    public required CivilDateFormatAudit AuditBefore { get; init; }

    public required IReadOnlyList<CivilDateColumnRepair> Columns { get; init; }

    public int TotalRewrittenRows => Columns.Sum(c => c.RewrittenRows);
    public int TotalTruncatedRows => Columns.Sum(c => c.TruncatedRows);
    public int TotalNormalizedRows => Columns.Sum(c => c.NormalizedRows);
    public int TotalUnrepairableRows => Columns.Sum(c => c.UnrepairableRows);

    /// <summary>Les valeurs non reprises, nommément, pour arbitrage humain.</summary>
    public IReadOnlyList<CivilDateOffendingValue> UnrepairableValues => AuditBefore.UnrepairableValues;

    /// <summary>Résumé sur une ligne, tel qu'inscrit au journal de migration.</summary>
    public string Summary =>
        $"civil dates: rewritten={TotalRewrittenRows} (truncated={TotalTruncatedRows}, normalized={TotalNormalizedRows}), " +
        $"unrepairable={TotalUnrepairableRows}, dryRun={DryRun}, applied={Applied}";
}

/// <summary>
/// P4-5D-R — <b>reprise</b> du format des dates civiles d'une base SQLite antérieure à P4-5D, là où EF ne
/// peut rien : le type de colonne n'a pas changé, donc <b>aucune migration EF n'existe ni ne peut exister</b>
/// (ADR-PROD-DB-004 §7.2). La correction porte sur les <b>valeurs</b>, pas sur le schéma.
///
/// <para><b>Propriétés tenues.</b></para>
/// <list type="bullet">
/// <item><b>Idempotente</b> — la deuxième exécution ne réécrit rien : une valeur déjà canonique n'est plus
/// candidate. La réexécution est donc sans effet, et sans risque.</item>
/// <item><b>Transactionnelle</b> — tout est écrit, ou rien. Une erreur en cours de route laisse la base
/// exactement dans son état initial.</item>
/// <item><b>Ligne à ligne, jamais en masse.</b> C'est une décision de sûreté, pas de style : un
/// <c>UPDATE … SET x = substr(x,1,10) WHERE length(x) &gt; 10</c> global <b>corromprait</b> les valeurs
/// lisibles non canoniques — <c>« ␣1985-03-15 »</c> (onze caractères) deviendrait <c>« ␣1985-03-1 »</c>,
/// illisible. On ne réécrit donc que les lignes dont la classification a établi que la réécriture est
/// sûre, avec la valeur calculée pour elles.</item>
/// <item><b>Ne devine jamais.</b> Une valeur non réparable est laissée intacte et signalée. Aucune date
/// n'est inventée, aucun fuseau n'est appliqué.</item>
/// <item><b>Simulable</b> — <c>dryRun</c> produit le rapport complet sans écrire une seule ligne.</item>
/// </list>
///
/// <para>
/// <b>Sauvegarde.</b> Ce service agit dans la base ; il ne copie pas le fichier. La sauvegarde de fichier
/// est garantie par <see cref="SqliteDatabaseManager.PrepareDatabase"/>, qui sauvegarde <b>avant toute
/// mutation</b> et reste le point d'entrée normal de la reprise. Une exécution manuelle hors de ce chemin
/// doit être précédée de <see cref="SqliteDatabaseManager.Backup"/>.
/// </para>
/// </summary>
public sealed class SqliteCivilDateRepairService
{
    private readonly SqliteCivilDateFormatVerifier _verifier;

    public SqliteCivilDateRepairService(SqliteCivilDateFormatVerifier? verifier = null)
        => _verifier = verifier ?? new SqliteCivilDateFormatVerifier();

    /// <summary>
    /// Reprend les dates civiles de la base associée au contexte.
    /// </summary>
    /// <param name="context">Contexte SQLite ciblant la base à reprendre.</param>
    /// <param name="dryRun">Si vrai, aucune écriture n'est réalisée : seul le rapport est produit.</param>
    /// <remarks>
    /// Les valeurs non réparables ne font <b>pas</b> échouer ce service : il rend compte, il n'arbitre pas.
    /// La décision de refuser un démarrage appartient à <see cref="SqliteDatabaseManager"/>.
    /// </remarks>
    public CivilDateRepairReport Repair(OpticDbContext context, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        var audit = _verifier.Audit(context);

        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

            // Rien à faire : on ne touche pas à la base, on n'ouvre même pas de transaction.
            if (!audit.RequiresRepair)
            {
                return new CivilDateRepairReport
                {
                    DryRun = dryRun,
                    Applied = false,
                    AuditBefore = audit,
                    Columns = audit.Columns
                        .Select(c => new CivilDateColumnRepair
                        {
                            Table = c.Table,
                            Column = c.Column,
                            UnrepairableRows = c.UnrepairableCount
                        })
                        .ToList()
                };
            }

            if (dryRun)
            {
                return new CivilDateRepairReport
                {
                    DryRun = true,
                    Applied = false,
                    AuditBefore = audit,
                    Columns = audit.Columns
                        .Select(c => new CivilDateColumnRepair
                        {
                            Table = c.Table,
                            Column = c.Column,
                            TruncatedRows = c.RepairableCount,
                            NormalizedRows = c.NormalizableCount,
                            UnrepairableRows = c.UnrepairableCount
                        })
                        .ToList()
                };
            }

            using var transaction = connection.BeginTransaction();
            var repairs = new List<CivilDateColumnRepair>();

            foreach (var (table, column, keyColumn) in SqliteCivilDateFormatVerifier.CivilDateColumns)
            {
                var columnAudit = audit.Columns.Single(c => c.Table == table && c.Column == column);
                if (!columnAudit.Exists)
                {
                    repairs.Add(new CivilDateColumnRepair { Table = table, Column = column });
                    continue;
                }

                repairs.Add(RepairColumn(connection, transaction, table, column, keyColumn, columnAudit.UnrepairableCount));
            }

            transaction.Commit();

            return new CivilDateRepairReport
            {
                DryRun = false,
                Applied = repairs.Sum(r => r.RewrittenRows) > 0,
                AuditBefore = audit,
                Columns = repairs
            };
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static CivilDateColumnRepair RepairColumn(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string column,
        string keyColumn,
        int unrepairableCount)
    {
        // 1) Relever les lignes à réécrire ET la valeur exacte à leur écrire. La lecture est complète avant
        //    toute écriture : on ne modifie pas ce que l'on est en train de parcourir.
        var truncations = new List<(long RowId, string Value)>();
        var normalizations = new List<(long RowId, string Value)>();

        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT \"{keyColumn}\", CAST(\"{column}\" AS TEXT) FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1))
                {
                    continue;
                }

                var state = CivilDateFormat.Classify(reader.GetString(1), out var repaired);
                if (state == CivilDateFormatState.RepairableLegacyDateTime)
                {
                    truncations.Add((reader.GetInt64(0), repaired));
                }
                else if (state == CivilDateFormatState.NormalizableReadable)
                {
                    normalizations.Add((reader.GetInt64(0), repaired));
                }
            }
        }

        // 2) Réécrire, ligne par ligne, avec la valeur calculée pour cette ligne — jamais une expression SQL
        //    appliquée en masse (voir la remarque de sûreté sur la documentation du service).
        ApplyRewrites(connection, transaction, table, column, keyColumn, truncations);
        ApplyRewrites(connection, transaction, table, column, keyColumn, normalizations);

        return new CivilDateColumnRepair
        {
            Table = table,
            Column = column,
            TruncatedRows = truncations.Count,
            NormalizedRows = normalizations.Count,
            UnrepairableRows = unrepairableCount
        };
    }

    private static void ApplyRewrites(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string column,
        string keyColumn,
        IReadOnlyList<(long RowId, string Value)> rewrites)
    {
        if (rewrites.Count == 0)
        {
            return;
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = $value WHERE \"{keyColumn}\" = $id";

        var valueParameter = update.CreateParameter();
        valueParameter.ParameterName = "$value";
        update.Parameters.Add(valueParameter);

        var idParameter = update.CreateParameter();
        idParameter.ParameterName = "$id";
        update.Parameters.Add(idParameter);

        foreach (var (rowId, value) in rewrites)
        {
            valueParameter.Value = value;
            idParameter.Value = rowId;
            update.ExecuteNonQuery();
        }
    }
}
