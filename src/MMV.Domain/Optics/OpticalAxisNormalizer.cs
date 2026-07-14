namespace MMV.Domain.Optics;

/// <summary>
/// Normalisation <b>pure</b> de l'axe d'astigmatisme (P3-3B). Produit la valeur <b>canonique</b> d'un axe avant
/// persistance, sans dépôt, sans <c>UnitOfWork</c>, sans état et sans effet de bord.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convention produit.</b> En optique, un axe de <c>0°</c> et un axe de <c>180°</c> désignent la <b>même</b>
/// orientation. Le dépôt a déjà tranché l'écriture canonique — les
/// <c>docs/domain/P3-workshop-sheet-and-transposition-notes.md</c> fixent « si axe = 0 → normaliser à 180 » : le
/// domaine utile est donc <c>]0, 180]</c>. Figer cette convention <b>maintenant</b> évite que la transposition
/// (P3-6B) hérite de deux écritures pour un même axe.
/// </para>
/// <para>
/// <b>Séparation des responsabilités.</b> <c>PrescriptionValidator</c> <b>valide</b> (un axe de <c>0</c> est accepté
/// en saisie) ; la valeur canonique est produite <b>ici</b>, appelée par les use cases <b>après</b> la validation et
/// <b>avant</b> le mapping vers l'entité. FluentValidation ne mute jamais rien.
/// </para>
/// <para>
/// <b>Aucune donnée historique n'est réécrite</b> : la normalisation s'applique à l'écriture. Une ligne existante à
/// <c>0</c> le reste jusqu'à son prochain enregistrement.
/// </para>
/// </remarks>
public static class OpticalAxisNormalizer
{
    /// <summary>Écriture canonique de l'axe <c>0°</c>, équivalent de <c>180°</c>.</summary>
    private const int CanonicalZeroAxis = 180;

    /// <summary>
    /// Renvoie l'axe canonique : <c>null</c> reste <c>null</c> (pas d'axe = pas de valeur inventée), <c>0</c> devient
    /// <c>180</c>, et toute autre valeur est renvoyée inchangée.
    /// </summary>
    /// <remarks>
    /// Aucune borne n'est <b>appliquée</b> ici : le contrôle de plage <c>[0, 180]</c> appartient au validateur, qui
    /// s'exécute en amont. Une valeur hors plage ne parvient donc jamais jusqu'ici par les use cases.
    /// </remarks>
    public static int? NormalizeAxis(int? axis) => axis == 0 ? CanonicalZeroAxis : axis;
}
