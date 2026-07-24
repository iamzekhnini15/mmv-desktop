using Microsoft.Data.SqlClient;
using Npgsql;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Extraction des FAITS d'erreur provider (type, code/état SQL, contrainte nommée), sans jamais
/// exposer de secret ni de chaîne de connexion.
///
/// Sert de base à E6 : la classification actuelle de MMV repose sur <c>SqliteException</c> et ses
/// codes (P4-0 §16). Aucune branche existante ne reconnaîtrait ces exceptions serveur — c'est
/// précisément ce que cette collecte doit rendre mesurable. <c>PersistenceErrorMapper</c> n'est
/// PAS modifié.
/// </summary>
public static class ErrorFacts
{
    public static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException pg:
                    return $"Npgsql.PostgresException | SqlState={pg.SqlState} | contrainte={pg.ConstraintName ?? "(aucune)"} | table={pg.TableName ?? "(n/a)"} | colonne={pg.ColumnName ?? "(n/a)"}";

                case NpgsqlException npg:
                    return $"{npg.GetType().Name} | SqlState=(n/a) | message={Short(npg.Message)}";

                case SqlException sql:
                    var constraint = ExtractSqlServerConstraint(sql.Message);
                    return $"Microsoft.Data.SqlClient.SqlException | Number={sql.Number} | State={sql.State} | Class={sql.Class} | contrainte={constraint}";
            }
        }

        return $"{exception.GetType().Name} (aucune exception provider imbriquée) | {Short(exception.Message)}";
    }

    /// <summary>
    /// Catégorie MÉTIER CANDIDATE, proposée seulement. Aucun mapping n'est décidé ici : ce champ
    /// alimente la colonne « classification P4 candidate » du rapport.
    /// </summary>
    public static string CandidateCategory(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException pg:
                    return pg.SqlState switch
                    {
                        "23505" => "Duplicate (violation d'unicité)",
                        "23503" => "ConstraintViolation (clé étrangère)",
                        "23502" => "ConstraintViolation (NOT NULL)",
                        "23514" => "ConstraintViolation (CHECK)",
                        "40001" => "Concurrency (serialization failure)",
                        "40P01" => "Concurrency (deadlock detected)",
                        "57014" => "Timeout (query canceled)",
                        "3D000" => "DatabaseUnavailable (base inexistante)",
                        "28P01" => "DatabaseUnavailable (authentification)",
                        _ => $"Unknown (SqlState={pg.SqlState})"
                    };

                case SqlException sql:
                    return sql.Number switch
                    {
                        2627 or 2601 => "Duplicate (violation d'unicité)",
                        547 => "ConstraintViolation (FK/CHECK)",
                        515 => "ConstraintViolation (NOT NULL)",
                        1205 => "Concurrency (deadlock victim)",
                        -2 => "Timeout (expiration côté client)",
                        4060 or 18456 => "DatabaseUnavailable (accès/authentification)",
                        53 or 10060 or 10061 => "DatabaseUnavailable (connexion refusée)",
                        _ => $"Unknown (Number={sql.Number})"
                    };
            }
        }

        return "Unknown (aucune exception provider)";
    }

    private static string ExtractSqlServerConstraint(string message)
    {
        // SQL Server ne fournit PAS de champ structuré équivalent à PostgresException.ConstraintName :
        // le nom n'existe que dans le TEXTE du message, et sa position dépend du type d'erreur.
        //   2601 : "...in object 'dbo.T' with unique index 'idx_x'"   -> 2e littéral
        //   2627 : "Violation of UNIQUE KEY constraint 'idx_x'. ...object 'dbo.T'" -> 1er littéral
        //   547  : "...conflicted with the FOREIGN KEY constraint 'FK_x'. ..."     -> 1er littéral
        // On privilégie donc le littéral qui suit le mot « index » ou « constraint ».
        var literals = new List<string>();
        for (var i = message.IndexOf('\''); i >= 0; i = message.IndexOf('\'', i + 1))
        {
            var end = message.IndexOf('\'', i + 1);
            if (end < 0)
            {
                break;
            }

            literals.Add(message[(i + 1)..end]);
            i = end;
        }

        if (literals.Count == 0)
        {
            return "(non extraite)";
        }

        // Un nom d'index/contrainte ne porte pas de préfixe de schéma « dbo. ».
        var named = literals.FirstOrDefault(l => !l.Contains('.', StringComparison.Ordinal));
        return named ?? literals[0];
    }

    private static string Short(string message)
    {
        var single = message.Replace(Environment.NewLine, " ").Replace('\n', ' ');
        return single.Length <= 160 ? single : single[..160] + "…";
    }
}
