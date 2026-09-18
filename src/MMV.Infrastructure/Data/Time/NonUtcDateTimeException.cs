namespace MMV.Infrastructure.Data.Time;

/// <summary>
/// Un <see cref="DateTime"/> dont le <see cref="DateTimeKind"/> n'est pas <see cref="DateTimeKind.Utc"/>
/// a tenté de franchir la frontière de persistance (P4-5D, obligation T3 d'<c>ADR-PROD-DB-004</c> §5.4).
///
/// <para>
/// <b>C'est un échec volontaire, pas un accident.</b> L'ADR a explicitement <b>rejeté</b> le convertisseur
/// silencieux qui aurait appelé <c>ToUniversalTime()</c> (§4.2, option D1) : sur un
/// <see cref="DateTimeKind.Unspecified"/>, « convertir » revient à <b>supposer</b> un fuseau, et sur un
/// <see cref="DateTimeKind.Local"/>, cela masquerait un <c>DateTime.Now</c> oublié au lieu de le révéler.
/// L'option retenue (D3) <b>refuse</b> et localise la faute.
/// </para>
///
/// <para>
/// <b>Pourquoi ce n'est pas une <c>PersistenceException</c>.</b> <c>PersistenceException</c> décrit une
/// erreur d'exploitation qu'un opérateur peut rencontrer et qu'une ViewModel présente (doublon, base
/// occupée). Ceci est un <b>défaut de programmation</b> : aucun message utilisateur ne s'applique, aucun
/// rattrapage n'est souhaitable, et l'exception doit remonter jusqu'au test qui l'a provoquée.
/// </para>
///
/// <para>
/// Le mécanisme s'applique <b>aux deux providers</b>. C'est le point décisif : PostgreSQL refuserait de
/// lui-même un <c>Local</c> (§2.1), mais SQLite l'accepterait silencieusement. En posant le refus dans le
/// modèle EF, la suite de tests SQLite détecte la faute <b>sans serveur</b>.
/// </para>
/// </summary>
public sealed class NonUtcDateTimeException : InvalidOperationException
{
    internal NonUtcDateTimeException(DateTime offendingValue)
        : base(BuildMessage(offendingValue))
    {
        OffendingValue = offendingValue;
    }

    /// <summary>Valeur refusée, telle qu'elle a été présentée à la persistance.</summary>
    public DateTime OffendingValue { get; }

    /// <summary><see cref="DateTimeKind"/> effectivement porté par la valeur refusée.</summary>
    public DateTimeKind OffendingKind => OffendingValue.Kind;

    private static string BuildMessage(DateTime value) =>
        $"Instant refusé par la persistance : DateTimeKind = {value.Kind}, valeur « {value:O} ». " +
        "Tout DateTime persisté par MMV doit porter DateTimeKind.Utc (ADR-PROD-DB-004 §5, invariant 1). " +
        "Corriger la SOURCE de la valeur — injecter IClock et lire IClock.UtcNow, ou construire le " +
        "DateTime avec DateTimeKind.Utc. Ne PAS appeler ToUniversalTime() sur place : sur un Kind " +
        "Unspecified, cela suppose un fuseau au lieu de le connaître. Si la valeur est une DATE CIVILE " +
        "(date de naissance, date d'ordonnance), elle n'a rien à faire dans une colonne d'instant : son " +
        "type est DateOnly (ADR-PROD-DB-004 §5, décision 7).";
}
