using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Persistence;

/// <summary>
/// Implémentation EF Core 8 / SQLite de <see cref="INumberSequenceService"/> (P2A-1E, R-03 / ADR-006).
///
/// <para>
/// L'attribution d'un numéro repose sur un <b>incrément atomique conditionnel</b> (compare-and-swap) du
/// compteur <see cref="MMV.Domain.Entities.DocumentSequence"/> :
/// <list type="number">
///   <item>lecture de la valeur courante <c>V</c> (sans suivi) ;</item>
///   <item><c>UPDATE DocumentSequences SET CurrentValue = V+1 WHERE SequenceName = @name AND CurrentValue = V</c>
///         via <c>ExecuteUpdateAsync</c> ;</item>
///   <item><b>1 ligne affectée</b> ⇒ le numéro <c>V+1</c> est à nous ; <b>0 ligne</b> ⇒ un autre appel a
///         incrémenté entre-temps ⇒ on retente avec la nouvelle valeur.</item>
/// </list>
/// La clause <c>WHERE CurrentValue = V</c> rend impossible l'attribution du même numéro à deux appels
/// concurrents (le second voit <c>0</c> ligne et relit la valeur incrémentée), <b>sans</b> verrou applicatif.
/// </para>
///
/// <para>
/// Partage le <see cref="OpticDbContext"/> de la portée DI : l'<c>UPDATE</c> s'exécute sur la même
/// connexion et donc dans la <b>transaction courante</b> ouverte par <c>EfTransactionRunner</c> lorsqu'il y
/// en a une (flux de vente). Conséquence voulue : si la vente/commande est annulée, l'incrément est annulé
/// avec elle — le numéro n'est <b>pas</b> consommé. Hors transaction (génération à l'ouverture d'un
/// formulaire de commande), chaque <c>UPDATE</c> s'auto-valide ; le compare-and-swap garantit l'unicité.
/// </para>
///
/// <para>
/// Limite SQLite assumée : écrivain unique (les écritures sont sérialisées) ; <c>Microsoft.Data.Sqlite</c>
/// re-tente sur <c>SQLITE_BUSY</c> jusqu'au délai de commande. <c>ExecuteUpdateAsync</c> contourne le change
/// tracker : l'écriture est directe en base, sans entité suivie obsolète.
/// </para>
/// </summary>
public sealed class EfNumberSequenceService : INumberSequenceService
{
    /// <summary>Largeur du zéro-padding de la partie numérique (ex. <c>D6</c> ⇒ <c>VTE-000001</c>).</summary>
    private const int NumberPadding = 6;

    /// <summary>Borne de re-tentatives du compare-and-swap en cas de contention (jamais atteinte en pratique).</summary>
    private const int MaxAttempts = 50;

    private readonly OpticDbContext _context;

    public EfNumberSequenceService(OpticDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<string> NextNumberAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            throw new ArgumentException("Le nom de séquence est obligatoire.", nameof(sequenceName));
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // Lecture fraîche (sans suivi) de l'état courant : valeur déjà attribuée + préfixe d'affichage.
            var current = await _context.DocumentSequences
                .AsNoTracking()
                .Where(s => s.SequenceName == sequenceName)
                .Select(s => new { s.CurrentValue, s.Prefix })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (current is null)
            {
                throw new NumberSequenceException(
                    sequenceName,
                    $"La séquence de numérotation « {sequenceName} » est introuvable (non initialisée).");
            }

            var nextValue = current.CurrentValue + 1;

            // Incrément atomique conditionnel : n'affecte la ligne que si personne ne l'a changée depuis
            // la lecture. Participe à la transaction courante si une est ouverte (flux de vente).
            var rowsAffected = await _context.DocumentSequences
                .Where(s => s.SequenceName == sequenceName && s.CurrentValue == current.CurrentValue)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.CurrentValue, nextValue)
                        .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            if (rowsAffected == 1)
            {
                return Format(current.Prefix, nextValue);
            }

            // rowsAffected == 0 : un autre appel a incrémenté entre la lecture et l'écriture → on retente.
        }

        throw new NumberSequenceException(
            sequenceName,
            $"Impossible d'attribuer un numéro pour la séquence « {sequenceName} » après {MaxAttempts} tentatives (contention).");
    }

    private static string Format(string prefix, long value) =>
        $"{prefix}-{value.ToString("D" + NumberPadding, CultureInfo.InvariantCulture)}";
}
