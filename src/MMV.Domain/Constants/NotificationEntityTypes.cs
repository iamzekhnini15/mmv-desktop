namespace MMV.Domain.Constants;

/// <summary>
/// Types d'entité corrélée à une notification (P3-8). <c>Notification.EntityId</c> est <b>polymorphe</b> et ne porte
/// aucune clé étrangère : <c>EntityId = 42</c> désigne le produit 42 <b>ou</b> la commande 42 selon cette valeur.
/// <c>Notification.EntityType</c> est donc une composante <b>indispensable</b> de la clé métier, alors que
/// l'anti-doublon d'origine l'ignorait.
/// </summary>
public static class NotificationEntityTypes
{
    /// <summary>Produit du catalogue — cible des alertes <see cref="NotificationTypes.LowStock"/>.</summary>
    public const string Product = "Product";

    /// <summary>Commande — cible des faits <c>OrderStatusChanged</c> et <c>PaymentReceived</c>.</summary>
    public const string Order = "Order";
}
