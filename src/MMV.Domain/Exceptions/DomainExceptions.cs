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
