using MMV.Domain.Interfaces.Time;

namespace MMV.Infrastructure.Services;

/// <summary>
/// Implémentation système d'<see cref="IClock"/> (P4-5D, obligation T1 d'<c>ADR-PROD-DB-004</c> §5.2).
///
/// <para>
/// <b>Point unique d'accès à l'horloge du système d'exploitation dans tout <c>src/**</c>.</b> Après P4-5D,
/// <c>DateTime.Now</c> n'existe plus nulle part ailleurs : ce fichier est l'<b>unique entrée de la liste
/// blanche</b> du test d'architecture <c>AmbientTimeArchitectureTests</c> (ADR-PROD-DB-004 §5 décision 5).
/// C'est la traduction littérale de la décision « <c>DateTime.UtcNow</c> n'est plus appelé que dans
/// l'implémentation ».
/// </para>
///
/// <para>
/// <b>Sans état, donc partageable.</b> Aucun champ, aucune allocation par appel : l'instance est
/// enregistrée en <c>Singleton</c> par le composition root, contrairement aux repositories et use cases
/// qui suivent la portée du <c>DbContext</c>.
/// </para>
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>
    /// Instance partagée, utilisable là où l'injection n'est pas disponible (code-behind Avalonia créé par
    /// le framework, valeurs par défaut de constructeur de ViewModel). Le composition root enregistre
    /// <b>cette même instance</b> : il n'existe donc jamais deux horloges différentes dans le processus.
    /// </summary>
    public static readonly SystemClock Instance = new();

    /// <inheritdoc />
    /// <remarks>
    /// <c>DateTime.UtcNow</c> porte par construction <see cref="DateTimeKind.Utc"/> : le contrat de
    /// <see cref="IClock.UtcNow"/> est donc satisfait sans aucune normalisation — et surtout sans
    /// « correction » d'un <c>Kind</c>, ce que l'ADR refuse explicitement (§4.2, option D1 rejetée).
    /// </remarks>
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    /// <remarks>
    /// <b>Seul appel à <c>DateTime.Now</c> de tout <c>src/**</c>, et il est ici légitime</b> : une date
    /// civile est par définition celle du calendrier de l'opérateur. Passer par <c>UtcNow</c> donnerait un
    /// jour faux pendant les deux heures d'écart de l'heure d'été française — exactement le décalage que
    /// l'ADR §2.4 décrit comme « fonctionnellement faux » sur une date de naissance ou d'ordonnance.
    /// La valeur ne franchit jamais la frontière de persistance en tant qu'instant : elle est un
    /// <see cref="DateOnly"/>, stocké tel quel (<c>date</c> PostgreSQL / <c>TEXT yyyy-MM-dd</c> SQLite).
    /// </remarks>
    public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.Now);
}
