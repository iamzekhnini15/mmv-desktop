using Microsoft.Data.SqlClient;
using Npgsql;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>Décision de rejeu pour une erreur donnée, avec son MOTIF explicite.</summary>
public sealed record RetryDecision(bool Retryable, string Reason);

/// <summary>
/// Politique de retry EXPÉRIMENTALE — **strictement dans le harness**.
/// <c>EfTransactionRunner</c> et le produit ne sont PAS modifiés (P4-0 §14 : aucun retry n'existe
/// aujourd'hui dans MMV). Cette politique sert uniquement à MESURER ce qu'un retry pourrait faire.
///
/// Règles appliquées, dans cet ordre :
///   1. une violation de CONTRAINTE n'est JAMAIS rejouée (la base a tranché : rejouer la reproduit) ;
///   2. une erreur MÉTIER n'est JAMAIS rejouée (la décision est fonctionnelle, pas technique) ;
///   3. un DEADLOCK, un TIMEOUT ou une INDISPONIBILITÉ sont rejouables — mais seulement si
///      l'opération est idempotente ou protégée par un compare-and-swap (cf. E15) ;
///   4. sur PostgreSQL, une transaction AVORTÉE (25P02) n'est rejouable qu'en RECOMMENÇANT la
///      transaction entière : rejouer la seule commande échoue toujours ;
///   5. le nombre d'essais est BORNÉ et l'échec final n'est jamais masqué.
/// </summary>
public static class SpikeRetryPolicy
{
    /// <summary>Borne stricte du nombre d'essais : jamais de boucle non bornée.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Délai fixe et court entre deux essais. Calibré par mesure (E14) : SQL Server met environ
    /// 5 s à redevenir joignable après un redémarrage, PostgreSQL moins d'une seconde.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    public static RetryDecision Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException pg:
                    return pg.SqlState switch
                    {
                        "40P01" => new(true, "deadlock detecte — la transaction peut etre rejouee entierement"),
                        "40001" => new(true, "echec de serialisation — rejeu de la transaction entiere"),
                        "57014" => new(true, "requete annulee/expiree — rejouable si l'operation est idempotente"),
                        "25P02" => new(true, "transaction avortee — rejouable UNIQUEMENT en recommencant la transaction"),
                        "23505" => new(false, "violation d'unicite — la base a tranche, un rejeu la reproduirait"),
                        "23503" => new(false, "violation de cle etrangere — donnee invalide, jamais rejouable"),
                        "23502" => new(false, "violation NOT NULL — donnee invalide, jamais rejouable"),
                        "23514" => new(false, "violation CHECK — donnee invalide, jamais rejouable"),
                        "3D000" => new(false, "base inexistante — erreur de configuration, pas transitoire"),
                        "28P01" => new(false, "authentification refusee — erreur de configuration, pas transitoire"),
                        _ => new(false, $"SqlState={pg.SqlState} non classifie — refus par defaut (jamais de rejeu aveugle)")
                    };

                case SqlException sql:
                    return sql.Number switch
                    {
                        1205 => new(true, "victime de deadlock — la transaction peut etre rejouee entierement"),
                        -2 => new(true, "expiration cote client — rejouable si l'operation est idempotente"),
                        53 or 10060 or 10061 => new(true, "serveur injoignable — indisponibilite potentiellement transitoire"),
                        // 10053/10054 DÉCOUVERTS PAR MESURE (E15) : un arrêt serveur alors qu'une
                        // connexion est déjà établie ne rend PAS 10061 (« refusée ») mais
                        // « connexion existante fermée par l'hôte distant ». Une politique fondée sur
                        // le seul 10061 laisserait donc passer le cas le plus fréquent en exploitation.
                        10053 or 10054 => new(true, "connexion etablie fermee par l'hote — indisponibilite transitoire"),
                        2601 or 2627 => new(false, "violation d'unicite — la base a tranche, un rejeu la reproduirait"),
                        547 => new(false, "violation FK/CHECK — donnee invalide, jamais rejouable"),
                        515 => new(false, "violation NOT NULL — donnee invalide, jamais rejouable"),
                        4060 or 18456 => new(false, "acces/authentification refuses — erreur de configuration"),
                        _ => new(false, $"Number={sql.Number} non classifie — refus par defaut (jamais de rejeu aveugle)")
                    };

                case NpgsqlException:
                    // Npgsql n'expose AUCUN SqlState pour l'expiration et la connexion refusee (E13).
                    // Une politique reelle devrait donc se fonder sur le TYPE et l'etat de connexion.
                    return new(true, "NpgsqlException sans SqlState (expiration ou connexion) — indisponibilite presumee transitoire");
            }
        }

        // Toute exception non provider est consideree METIER : jamais rejouee.
        return new(false, $"{exception.GetType().Name} — erreur metier ou applicative, jamais rejouable");
    }

    /// <summary>
    /// Exécute <paramref name="operation"/> avec rejeu borné. Chaque tentative est journalisée.
    /// L'échec final est TOUJOURS propagé : la politique ne masque jamais une erreur.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, Task<T>> operation,
        string label,
        string experiment,
        string provider)
    {
        Exception? last = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var result = await operation(attempt);
                SpikeLog.Write(experiment, provider, $"RETRY[{label}] tentative {attempt}/{MaxAttempts} : SUCCES");
                return result;
            }
            catch (Exception ex)
            {
                last = ex;
                var decision = Classify(ex);
                SpikeLog.Write(experiment, provider,
                    $"RETRY[{label}] tentative {attempt}/{MaxAttempts} : ECHEC | {ErrorFacts.Describe(ex)} | " +
                    $"rejouable={decision.Retryable} | motif={decision.Reason}");

                if (!decision.Retryable)
                {
                    throw;
                }

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(Delay);
                }
            }
        }

        SpikeLog.Write(experiment, provider,
            $"RETRY[{label}] ABANDON apres {MaxAttempts} tentatives — l'echec final est propage, jamais masque");
        throw last!;
    }
}
