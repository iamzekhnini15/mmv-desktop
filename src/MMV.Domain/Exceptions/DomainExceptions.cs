using MMV.Domain.Enums;

namespace MMV.Domain.Exceptions;

/// <summary>
/// Exception de base pour tous les erreurs métier.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Levée quand une entité n'est pas trouvée.
/// </summary>
public class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, object id)
        : base($"L'entité {entityName} avec l'ID {id} n'a pas été trouvée.") { }
}

/// <summary>
/// Levée quand une validation métier échoue.
/// </summary>
public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>
/// Levée quand une entité existe déjà (exemple: username dupliqué).
/// </summary>
public class DuplicateEntityException : DomainException
{
    public DuplicateEntityException(string entityName, string field, object value)
        : base($"Une entité {entityName} avec {field} = '{value}' existe déjà.") { }
}

/// <summary>
/// Erreur <b>métier contrôlée</b> (P2A-1D) : une sortie de stock a été refusée parce que le stock
/// disponible était insuffisant — <b>ou</b> avait été modifié de façon concurrente entre l'affichage et
/// la validation — pour honorer la quantité demandée. Le décrément atomique conditionnel
/// (<c>UPDATE … WHERE StockQuantity &gt;= quantity</c>) n'a affecté <b>aucune ligne</b> : aucune
/// modification n'a été persistée et la transaction englobante est annulée (aucune vente partielle).
///
/// <para>
/// Définie dans le domaine (sans dépendance EF/SQLite) afin que les ViewModels actuels — et la future
/// couche Application — puissent l'attraper de façon uniforme et afficher le message assaini exposé par
/// <see cref="System.Exception.Message"/>. Ce n'est <b>pas</b> une erreur de persistance technique : elle
/// est propagée <b>inchangée</b> par <c>ITransactionRunner</c> (cf.
/// <see cref="MMV.Domain.Exceptions.PersistenceException"/> pour les erreurs techniques).
/// </para>
/// </summary>
public class InsufficientStockException : DomainException
{
    /// <summary>Identifiant du produit concerné.</summary>
    public long ProductId { get; }

    /// <summary>Quantité demandée (sortie souhaitée).</summary>
    public int RequestedQuantity { get; }

    /// <summary>Stock réellement disponible au moment du refus (jamais négatif).</summary>
    public int AvailableQuantity { get; }

    public InsufficientStockException(long productId, int requestedQuantity, int availableQuantity)
        : base($"Stock insuffisant pour ce produit : {requestedQuantity} demandé(s), " +
               $"{availableQuantity} disponible(s). Aucune modification n'a été conservée.")
    {
        ProductId = productId;
        RequestedQuantity = requestedQuantity;
        AvailableQuantity = availableQuantity;
    }
}

/// <summary>
/// Erreur <b>métier contrôlée</b> (P3-5) : un <b>ajustement d'inventaire</b> a été refusé parce que le stock du
/// produit a été modifié de façon <b>concurrente</b> entre la lecture de la valeur courante et l'écriture
/// conditionnelle de la valeur comptée. La mise à jour conditionnelle
/// (<c>UPDATE … WHERE StockQuantity = valeur lue</c>) n'a affecté <b>aucune ligne</b> : aucune modification n'a
/// été persistée et la transaction englobante est annulée. Refuse explicitement le <i>last-write-wins</i> :
/// l'ajustement de l'autre poste n'est jamais écrasé silencieusement.
///
/// <para>
/// Définie dans le domaine (sans dépendance EF/SQLite) afin que les ViewModels actuels — et la couche
/// Application — puissent l'attraper de façon uniforme et afficher le message assaini. Propagée <b>inchangée</b>
/// par <c>ITransactionRunner</c> (ce n'est pas une erreur de persistance technique).
/// </para>
/// </summary>
public class StockConcurrencyConflictException : DomainException
{
    /// <summary>Identifiant du produit concerné.</summary>
    public long ProductId { get; }

    /// <summary>Quantité lue avant l'écriture (valeur attendue par la mise à jour conditionnelle).</summary>
    public int ExpectedQuantity { get; }

    public StockConcurrencyConflictException(long productId, int expectedQuantity)
        : base("Le stock de ce produit a été modifié par une autre opération pendant l'ajustement " +
               $"(valeur attendue : {expectedQuantity}). Aucune modification n'a été conservée ; " +
               "veuillez recompter et réessayer.")
    {
        ProductId = productId;
        ExpectedQuantity = expectedQuantity;
    }
}

/// <summary>
/// Erreur <b>métier contrôlée</b> (P3-5) : une <b>transition de statut de commande</b> a été refusée parce que le
/// statut réellement stocké n'était plus le statut attendu (<see cref="ExpectedStatus"/>) au moment de la prise
/// atomique. Deux causes typiques : un autre poste a déjà fait avancer la commande (concurrence), ou la même
/// transition est rejouée (répétition). La prise conditionnelle
/// (<c>UPDATE … WHERE Status = statut attendu</c>) n'a affecté <b>aucune ligne</b> : aucun décrément ni mouvement
/// de fabrication n'est produit, garantissant l'<b>idempotence</b> du décrément (pas de double sortie de stock).
///
/// <para>
/// Définie dans le domaine (sans dépendance EF/SQLite). Propagée <b>inchangée</b> par <c>ITransactionRunner</c>
/// ⇒ rollback complet (statut non avancé, aucun mouvement, aucune notification). Ne constitue <b>pas</b> une
/// matrice de transitions (P3-6) : c'est uniquement la garde d'idempotence de décrément de P3-5.
/// </para>
/// </summary>
public class OrderStatusConflictException : DomainException
{
    /// <summary>Identifiant de la commande concernée.</summary>
    public long OrderId { get; }

    /// <summary>Statut attendu (celui affiché à l'appelant) qui ne correspondait plus au statut stocké.</summary>
    public OrderStatus ExpectedStatus { get; }

    public OrderStatusConflictException(long orderId, OrderStatus expectedStatus)
        : base($"Le statut de la commande a changé depuis l'affichage (statut attendu : {expectedStatus}). " +
               "La transition a été refusée pour éviter un double mouvement de stock. Aucune modification n'a " +
               "été conservée ; veuillez rafraîchir la commande.")
    {
        OrderId = orderId;
        ExpectedStatus = expectedStatus;
    }
}

/// <summary>
/// Erreur <b>métier contrôlée</b> (P2A-1E, R-03) liée à la <b>numérotation fiable</b> des documents : la
/// séquence demandée est introuvable (compteur non initialisé) ou aucun numéro n'a pu être attribué malgré
/// les tentatives (contention persistante). Aucun numéro n'est consommé.
///
/// <para>
/// Définie dans le domaine (sans dépendance EF/SQLite) afin que les ViewModels actuels — et la future
/// couche Application — puissent l'attraper de façon uniforme. Ce n'est <b>pas</b> une erreur de
/// persistance technique : elle est propagée <b>inchangée</b> par <c>ITransactionRunner</c>, ce qui annule
/// la vente/commande englobante (cf. <see cref="PersistenceException"/> pour les erreurs techniques).
/// </para>
/// </summary>
public class NumberSequenceException : DomainException
{
    /// <summary>Nom logique de la séquence concernée (ex. <c>SALE</c>, <c>ORDER</c>).</summary>
    public string SequenceName { get; }

    public NumberSequenceException(string sequenceName, string message) : base(message)
    {
        SequenceName = sequenceName;
    }
}
