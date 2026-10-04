using System.Reflection;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Simule un AUTRE POSTE qui écrit entre la garde d'un use case et son écriture : enveloppe un repository de
/// production et exécute <c>interleave</c> une seule fois, juste avant le premier appel de la méthode visée,
/// puis délègue à l'implémentation réelle. Seul l'ordre des événements est piloté ; aucune logique n'est simulée.
/// </summary>
public class InterleavedCall<T> : DispatchProxy where T : class
{
    private T _inner = null!;
    private string _methodName = null!;
    private Func<Task> _interleave = null!;
    private bool _done;

    public static T Before(T inner, string methodName, Func<Task> interleave)
    {
        var proxy = Create<T, InterleavedCall<T>>();
        var self = (InterleavedCall<T>)(object)proxy;
        self._inner = inner;
        self._methodName = methodName;
        self._interleave = interleave;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (!_done && targetMethod.Name == _methodName)
        {
            _done = true;
            _interleave().GetAwaiter().GetResult();
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }
}
