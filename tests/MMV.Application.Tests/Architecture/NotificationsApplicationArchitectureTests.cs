using System.Reflection;
using FluentAssertions;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using Xunit;

namespace MMV.Application.Tests.Architecture;

/// <summary>
/// P3-8 — Garde-fous d'architecture de la couche Application pour le domaine Notifications. Complète
/// <c>NotificationsArchitectureTests</c> (Domain), qui ne peut pas référencer cet assembly.
/// </summary>
public sealed class NotificationsApplicationArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(GenerateLowStockNotificationsUseCase).Assembly;

    [Fact]
    public void LApplication_NeReferenceNiEfCoreNiSqlite()
    {
        // La réconciliation orchestre des primitives ensemblistes exposées par le PORT : elle ne doit jamais
        // acquérir de connaissance EF, sans quoi « ExecuteUpdate » et « ON CONFLICT » remonteraient dans le métier.
        var referenced = ApplicationAssembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

        referenced.Should().NotContain(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        referenced.Should().NotContain(n => n.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void AucunUseCaseDeSuppressionOuDePurgeDeNotification_NExiste()
    {
        // L'historique est une exigence : la résolution est un MARQUAGE, jamais une suppression.
        ApplicationAssembly.GetTypes()
            .Where(t => t.Name.Contains("Notification", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Should().NotContain(n => n.Contains("Delete", StringComparison.Ordinal)
                                      || n.Contains("Purge", StringComparison.Ordinal)
                                      || n.Contains("Remove", StringComparison.Ordinal));
    }

    [Fact]
    public void LaReconciliation_EstEnveloppeeDansUneFrontiereTransactionnelle()
    {
        // Fermeture et ouverture doivent rester tout-ou-rien : la dépendance au runner est structurelle, pas
        // décorative.
        typeof(GenerateLowStockNotificationsUseCase)
            .GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType.Name)
            .Should().Contain("ITransactionRunner");
    }

    [Fact]
    public void LeDtoDeLecture_ExposeLaResolution()
    {
        var properties = typeof(NotificationListItemDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        properties.Should().Contain("ResolvedAt");
        properties.Should().Contain("IsResolved");
        properties.Should().Contain("IsRead", "les deux axes restent lisibles séparément");
    }
}
