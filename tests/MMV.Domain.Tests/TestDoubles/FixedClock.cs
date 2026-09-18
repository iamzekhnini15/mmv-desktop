using MMV.Domain.Interfaces.Time;

namespace MMV.Domain.Tests.TestDoubles;

/// <summary>
/// Horloge de test à valeur <b>fixe</b> (P4-5D — ADR-PROD-DB-004 T1).
///
/// <para>
/// C'est la contrepartie concrète de la deuxième raison d'être d'<see cref="IClock"/> : tant que le temps
/// était lu depuis <c>DateTime.UtcNow</c>, un scénario daté ne pouvait être asserté qu'à une tolérance
/// près, et dépendait de sa propre durée d'exécution. Ici, deux lectures successives renvoient
/// <b>exactement</b> la même valeur, ce qui rend l'égalité stricte assertable.
/// </para>
///
/// <para>
/// <b>Le contrat d'<see cref="IClock.UtcNow"/> est vérifié à la construction</b> : une horloge de test qui
/// renverrait un <see cref="DateTimeKind.Local"/> ou <see cref="DateTimeKind.Unspecified"/> ferait échouer
/// le test <b>au convertisseur validant</b>, loin de sa cause. L'échec est donc déplacé ici, au point où
/// la valeur est choisie.
/// </para>
///
/// <para>
/// Ce double est <b>dupliqué</b> dans les projets de test qui en ont besoin : la solution ne comporte
/// aucun projet de support de test partagé, et en créer un dépasserait le périmètre de P4-5D.
/// </para>
/// </summary>
internal sealed class FixedClock : IClock
{
    private readonly DateTime _utcNow;
    private readonly DateOnly? _localToday;

    public FixedClock(DateTime utcNow, DateOnly? localToday = null)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                $"Une horloge de test doit renvoyer un instant UTC ; reçu Kind = {utcNow.Kind}.",
                nameof(utcNow));
        }

        _utcNow = utcNow;
        _localToday = localToday;
    }

    /// <inheritdoc />
    public DateTime UtcNow => _utcNow;

    /// <summary>
    /// Date civile fixe. À défaut de valeur explicite, la date civile de <see cref="UtcNow"/> — suffisant
    /// pour les scénarios qui n'exercent pas la distinction instant / date civile, et explicite pour ceux
    /// qui l'exercent (un poste dont la date locale précède ou suit celle d'UTC).
    /// </summary>
    public DateOnly LocalToday => _localToday ?? DateOnly.FromDateTime(_utcNow);
}
