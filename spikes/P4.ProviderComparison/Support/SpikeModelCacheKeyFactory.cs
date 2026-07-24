using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// EF Core met le modèle en cache par TYPE de contexte. Or <see cref="AdaptedOpticDbContext"/>
/// construit un modèle DIFFÉRENT selon le provider et la variante de mapping monétaire mesurée.
///
/// Sans cette fabrique, la première variante exécutée gagnerait et toutes les suivantes
/// réutiliseraient son modèle : le rapport afficherait alors un type physique FAUX (par exemple
/// « REAL » mesuré comme numeric(18,2)). Le défaut a été observé réellement pendant le spike ;
/// cette classe le corrige en intégrant provider + mapping à la clé de cache.
/// </summary>
public sealed class SpikeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is AdaptedOpticDbContext adapted
            ? (context.GetType(), adapted.Provider, adapted.Money, designTime)
            : (object)(context.GetType(), designTime);
}
