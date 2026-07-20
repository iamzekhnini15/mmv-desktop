namespace MMV.Domain.Constants;

/// <summary>
/// Types canoniques de notification (P3-8). <b>Constantes <c>string</c>, délibérément pas un <c>enum</c></b> : la
/// colonne <c>Notifications.Type</c> est un <c>TEXT</c> historique alimenté depuis P2 ; la convertir imposerait une
/// migration de données sans bénéfice pour le périmètre P3-8 (report explicite R9 de l'audit).
///
/// <para>
/// Deux familles sémantiquement distinctes cohabitent :
/// <list type="bullet">
///   <item><b>Alerte à condition persistante</b> — <see cref="LowStock"/> : la notification décrit une condition
///   qui reste vraie tant que le stock n'est pas reconstitué. Elle possède un cycle de vie (ouverture / résolution)
///   porté par <c>Notification.ResolvedAt</c> et protégée par l'index unique filtré.</item>
///   <item><b>Fait historique horodaté</b> — <see cref="OrderStatusChanged"/>, <see cref="PaymentReceived"/>,
///   <see cref="Info"/> et le type de seed <see cref="StockOut"/> : ils ne « cessent » jamais d'être vrais, ne sont
///   donc jamais résolus, et <b>ne doivent jamais entrer dans une contrainte d'unicité</b> (une commande traverse
///   plusieurs transitions, toutes légitimes).</item>
/// </list>
/// </para>
/// </summary>
public static class NotificationTypes
{
    /// <summary>Alerte de stock bas — <b>seul type à condition persistante</b>, donc seul type résoluble.</summary>
    public const string LowStock = "LowStock";

    /// <summary>
    /// Rupture de stock. <b>Type orphelin</b> : produit uniquement par le seed de démonstration, jamais par le
    /// runtime (un stock à zéro satisfait <c>0 &lt;= seuil</c> et produit un <see cref="LowStock"/>). Conservé pour
    /// que les bases de démonstration existantes restent lisibles.
    /// </summary>
    public const string StockOut = "StockOut";

    /// <summary>Transition de statut de commande — fait historique (P3-6, unicité acquise par le CAS de statut).</summary>
    public const string OrderStatusChanged = "OrderStatusChanged";

    /// <summary>Encaissement du solde — fait historique (P3-7, unicité acquise par la prise atomique).</summary>
    public const string PaymentReceived = "PaymentReceived";

    /// <summary>Information générale, sans entité liée.</summary>
    public const string Info = "Info";
}
