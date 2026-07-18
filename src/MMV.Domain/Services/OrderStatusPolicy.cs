using MMV.Domain.Enums;

namespace MMV.Domain.Services;

/// <summary>
/// Politique métier (P3-6) — <b>unique source de vérité</b> des transitions de statut d'une commande.
/// Règle pure, sans état ni dépendance (Domain) : elle décrit le workflow atelier <b>linéaire strict</b> et
/// refuse toute autre transition (saut d'étape, retour arrière, même statut vers même statut, sortie de
/// <see cref="OrderStatus.Delivered"/> qui est terminal).
/// </summary>
/// <remarks>
/// <para>
/// Séquence autorisée (une seule successeur par statut) :
/// <c>New → ToFabricate → InProgress → QualityCheck → Ready → Delivered</c>.
/// <see cref="OrderStatus.Delivered"/> est terminal (aucune transition).
/// </para>
/// <para>
/// <b>Séparation des responsabilités.</b> Cette politique couvre la <i>légalité</i> métier de la paire
/// (matrice). Elle ne remplace <b>pas</b> la prise atomique conditionnelle
/// (<c>IOrderRepository.TryTransitionStatusAsync</c>, P3-5) qui couvre le <i>conflit</i> de concurrence
/// multi-poste (statut réellement stocké ≠ statut attendu). Les deux gardes restent nécessaires et
/// complémentaires.
/// </para>
/// <para>
/// <b>Matrice unique.</b> <see cref="IsAllowed"/> est <i>dérivée</i> de <see cref="GetNext"/> : il n'existe
/// donc qu'une seule matrice codée en dur dans tout le code vivant (l'ancien <c>OrderService</c> et sa
/// seconde matrice morte ont été supprimés en P3-6).
/// </para>
/// </remarks>
public static class OrderStatusPolicy
{
    /// <summary>
    /// Retourne l'unique statut successeur autorisé pour <paramref name="current"/>, ou <c>null</c> si le
    /// statut est terminal (<see cref="OrderStatus.Delivered"/>) ou hors séquence.
    /// </summary>
    public static OrderStatus? GetNext(OrderStatus current) => current switch
    {
        OrderStatus.New => OrderStatus.ToFabricate,
        OrderStatus.ToFabricate => OrderStatus.InProgress,
        OrderStatus.InProgress => OrderStatus.QualityCheck,
        OrderStatus.QualityCheck => OrderStatus.Ready,
        OrderStatus.Ready => OrderStatus.Delivered,
        _ => null // Delivered (terminal) et toute valeur hors séquence.
    };

    /// <summary>
    /// Indique si la transition <paramref name="current"/> → <paramref name="next"/> est autorisée par le
    /// workflow linéaire strict. Dérivée de <see cref="GetNext"/> pour garantir une source de vérité unique.
    /// </summary>
    public static bool IsAllowed(OrderStatus current, OrderStatus next)
        => GetNext(current) is { } allowed && allowed == next;
}
