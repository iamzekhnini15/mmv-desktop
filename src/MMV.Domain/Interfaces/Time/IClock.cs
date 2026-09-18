namespace MMV.Domain.Interfaces.Time;

/// <summary>
/// Horloge injectable — <b>seule source de temps autorisée</b> dans le Domain et l'Application
/// (P4-5D, obligation T1 d'<c>ADR-PROD-DB-004</c> §5.2).
///
/// <para>
/// <b>Pourquoi une abstraction.</b> Deux raisons distinctes, toutes deux mesurées :
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <b>Multi-poste.</b> Avant P4-5D, 17 sites appelaient <c>DateTime.Now</c>, qui produit un
///     <see cref="System.DateTimeKind.Local"/> dépendant du fuseau du poste. Dans une base centrale
///     partagée, deux postes réglés différemment produisaient un <b>ordre chronologique faux</b>
///     (mouvements de stock, alertes, transitions de commande, traçabilité d'ordonnance) —
///     et Npgsql <b>refuse</b> purement et simplement d'écrire un <c>Local</c> dans un
///     <c>timestamptz</c> (ADR-PROD-DB-004 §2.1).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Testabilité.</b> Tant que le temps est lu depuis une propriété statique, un scénario daté
///     dépend de l'heure réelle d'exécution. L'horloge injectée rend ces scénarios déterministes.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// <b>Ce que cette interface ne fait pas.</b> Elle ne corrige pas une horloge de poste fausse : elle
/// supprime la moitié « fuseau » du problème, la dérive résiduelle relevant de l'exigence NTP
/// (ADR-PROD-DB-004 §5 décision 9, lots P4-8 / P4-9).
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>
    /// Instant courant. <b>Contrat : la valeur renvoyée porte toujours
    /// <see cref="System.DateTimeKind.Utc"/>.</b>
    ///
    /// <para>
    /// C'est la seule valeur admissible pour un <see cref="System.DateTime"/> destiné à la persistance :
    /// le convertisseur validant du modèle EF <b>lève</b> sur tout autre <c>Kind</c>
    /// (ADR-PROD-DB-004 §4.2, option D3).
    /// </para>
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Date civile du jour <b>dans le fuseau du poste</b>.
    ///
    /// <para>
    /// Distincte de <see cref="UtcNow"/> par nature, pas par commodité : une date civile
    /// (<c>Customer.BirthDate</c>, <c>Prescription.IssueDate</c>) n'est <b>pas un instant</b>. La traiter
    /// comme tel décale le jour — une ordonnance saisie le 3 à 00:30 en heure d'été française deviendrait
    /// le 2 en UTC (ADR-PROD-DB-004 §2.4). Ces champs sont donc des <see cref="DateOnly"/>, et
    /// <b>ne subissent ni conversion UTC ni convertisseur d'instants</b>.
    /// </para>
    ///
    /// <para>
    /// Le fuseau retenu est celui du poste, conformément à ADR-PROD-DB-004 §6 : aucun fuseau de magasin
    /// configurable n'est décidé pour V1.
    /// </para>
    /// </summary>
    DateOnly LocalToday { get; }
}
