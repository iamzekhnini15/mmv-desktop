using Microsoft.Extensions.DependencyInjection;

namespace MMV.Application;

/// <summary>
/// Enregistrement DI de la couche Application. Appelée par le composition root unique
/// (<c>MMV.App</c> — <see cref="object">App.ConfigureServices</see>).
/// </summary>
/// <remarks>
/// <para>
/// P2B-2B : la couche Application est créée <b>à vide</b> (squelette). <see cref="AddApplication"/>
/// n'enregistre <b>aucun</b> use case métier réel pour l'instant.
/// </para>
/// <para>
/// Les use cases applicatifs (premier cible : <c>EnregistrerVente</c> / <c>RegisterSaleUseCase</c>)
/// seront enregistrés ici en <b>P2B-2C</b> lors du premier vertical slice, en portée <c>Scoped</c> —
/// la même portée que <c>OpticDbContext</c>, les repositories et le <c>ITransactionRunner</c> —
/// afin de partager la transaction (cf. plan de migration §4). La signature de cette méthode
/// reste stable : seuls des enregistrements s'y ajouteront.
/// </para>
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les services de la couche Application dans le conteneur d'injection de dépendances.
    /// Squelette neutre en P2B-2B (aucun use case réel) ; point d'extension pour P2B-2C+.
    /// </summary>
    /// <param name="services">Collection de services du composition root.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Aucun enregistrement métier en P2B-2B (couche Application créée à vide).
        // Les use cases seront ajoutés ici en P2B-2C, sans modifier cette signature.
        return services;
    }
}
