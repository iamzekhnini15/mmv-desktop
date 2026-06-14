using System;
using System.Linq;
using System.Reflection;
using MMV.App.ViewModels;
using MMV.Domain.Interfaces.Repositories;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2J — Garde-fou de nettoyage technique. Après l'extraction des flux d'avancement de statut (P2B-2E) et
/// d'encaissement du solde (P2B-2G) vers la couche Application, <see cref="OrderDetailViewModel"/> n'accède plus
/// directement à la persistance : ses dépendances mortes (<c>IOrderRepository</c>, <c>IUnitOfWork</c>,
/// <c>INotificationRepository</c>) ont été retirées de son constructeur. <see cref="OrdersViewModel"/> ne
/// transmet plus <c>INotificationRepository</c> (devenu inutile une fois la VM de détail nettoyée).
///
/// Ce test, par réflexion sur les paramètres de constructeur, verrouille l'invariant contre toute réintroduction
/// accidentelle de ces ports dans ces ViewModels. Il reste volontairement simple (un seul axe : la signature de
/// construction) et stable (aucune dépendance à l'exécution).
/// </summary>
public sealed class OrderViewModelDependencyHygieneTests
{
    private static Type[] ConstructorParameterTypes<TViewModel>()
        => typeof(TViewModel)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToArray();

    [Fact]
    public void OrderDetailViewModel_DoesNotDependOnPersistencePorts()
    {
        var parameterTypes = ConstructorParameterTypes<OrderDetailViewModel>();

        Assert.DoesNotContain(typeof(IOrderRepository), parameterTypes);
        Assert.DoesNotContain(typeof(IUnitOfWork), parameterTypes);
        Assert.DoesNotContain(typeof(INotificationRepository), parameterTypes);
    }

    [Fact]
    public void OrdersViewModel_DoesNotDependOnNotificationRepository()
    {
        var parameterTypes = ConstructorParameterTypes<OrdersViewModel>();

        Assert.DoesNotContain(typeof(INotificationRepository), parameterTypes);
    }
}
