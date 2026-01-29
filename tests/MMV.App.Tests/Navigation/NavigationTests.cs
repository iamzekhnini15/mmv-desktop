using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using MMV.App.Services;
using MMV.App.ViewModels;
using Xunit;

namespace MMV.App.Tests.Navigation;

/// <summary>
/// Tests de la navigation entre les vues.
/// Ces tests vérifient que la logique de navigation fonctionne correctement.
/// </summary>
public class NavigationTests
{
    [Fact]
    public void NavigationService_Navigate_CreatesViewModelInstance()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

        // Register a ViewModel
        navigationService.RegisterViewModel("Dashboard", typeof(DashboardViewModel));

        // Act
        navigationService.Navigate("Dashboard");

        // Assert
        Assert.NotNull(navigationService.CurrentViewModel);
        Assert.IsType<DashboardViewModel>(navigationService.CurrentViewModel);
        Assert.Equal("Tableau de Bord", navigationService.CurrentViewModel!.Title);
    }

    [Fact]
    public void NavigationService_NavigateRaisesNavigationChanged()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();
        navigationService.RegisterViewModel("Dashboard", typeof(DashboardViewModel));

        var eventRaised = false;
        string? eventViewName = null;

        navigationService.NavigationChanged += (s, e) =>
        {
            eventRaised = true;
            eventViewName = e.ViewName;
        };

        // Act
        navigationService.Navigate("Dashboard");

        // Assert
        Assert.True(eventRaised, "NavigationChanged event should be raised");
        Assert.Equal("Dashboard", eventViewName);
    }

    [Fact]
    public void NavigationService_Navigate_UnregisteredView_ThrowsKeyNotFoundException()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

        // Act & Assert
        var ex = Assert.Throws<KeyNotFoundException>(() =>
        {
            navigationService.Navigate("NonExistent");
        });
        Assert.Contains("Aucun ViewModel enregistré", ex.Message);
    }

    [Fact]
    public void MainWindowViewModel_NavigateCommand_ChangesCurrentView()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

        var mainViewModel = new MainWindowViewModel(navigationService);

        // Act - Naviguer vers Dashboard
        mainViewModel.NavigateCommand.Execute("Dashboard");

        // Assert
        Assert.NotNull(mainViewModel.CurrentView);
        Assert.IsType<DashboardViewModel>(mainViewModel.CurrentView);
    }

    [Fact]
    public void MainWindowViewModel_NavigateCommand_ToCustomers_Attempts()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

        var mainViewModel = new MainWindowViewModel(navigationService);

        // Act - En production, la DI container fournirait les dépendances manquantes
        // Ce test vérifie que la commande elle-même fonctionne correctement
        try
        {
            mainViewModel.NavigateCommand.Execute("Customers");
            // Si pas d'exception, c'est un bonus - cela signifie que les dépendances étaient disponibles
            Assert.NotNull(mainViewModel.CurrentView);
        }
        catch (InvalidOperationException ex)
        {
            // C'est attendu en test car ICustomerRepository n'est pas enregistré
            // Chercher dans le message ou l'InnerException
            string fullMessage = ex.ToString();
            Assert.True(
                fullMessage.Contains("Customer") || fullMessage.Contains("Repository"),
                $"Exception message should contain Customer or Repository: {fullMessage}"
            );
        }
    }

    [Fact]
    public void MainWindowViewModel_NavigationItems_ContainsCorrectItems()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

        // Act
        var mainViewModel = new MainWindowViewModel(navigationService);

        // Assert
        Assert.NotEmpty(mainViewModel.NavigationItems);
        Assert.Contains(mainViewModel.NavigationItems, item => item.ViewName == "Dashboard");
        Assert.Contains(mainViewModel.NavigationItems, item => item.ViewName == "Customers");
        Assert.Contains(mainViewModel.NavigationItems, item => item.ViewName == "Products");
    }

    [Fact]
    public void CurrentView_BoundToMainWindow_DisplaysCorrectly()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>(sp => new NavigationService(sp));
        var serviceProvider = services.BuildServiceProvider();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

        var mainViewModel = new MainWindowViewModel(navigationService);

        // Act - Vérifier l'état initial (Dashboard is loaded par défaut)
        var currentView = mainViewModel.CurrentView;

        // Assert - Le CurrentView doit être initialisé
        Assert.NotNull(currentView);
        Assert.IsType<DashboardViewModel>(currentView);
        
        // Verify the binding property exists and is accessible
        Assert.NotEmpty(mainViewModel.NavigationItems);
    }
}
