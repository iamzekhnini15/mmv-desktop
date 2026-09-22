using Microsoft.EntityFrameworkCore.Design;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.PostgreSQL.Migrations;

/// <summary>
/// Point d'entrée design-time de la chaîne PostgreSQL (P4-5E-C, décision Q1 option A : seule exception
/// à D-01.2).
///
/// <para>
/// Quand ce projet est à la fois cible et projet de démarrage (D-04), EF Core 8 ne découvre le type de
/// contexte que parmi les types de ces deux assemblys, ou via l'attribut <c>[DbContext]</c> de leurs
/// migrations. Sans cette classe, <c>OpticDbContext</c> reste introuvable tant que la chaîne est vide :
/// la baseline, ou sa régénération avant fusion (D-03.5), ne peut pas être produite depuis le dépôt.
/// </para>
///
/// <para>
/// <b>Délégation pure</b> vers <see cref="OpticDbContextFactory"/>, qui reste l'unique chemin logique de
/// génération : le choix de la chaîne dépend toujours de <c>MMV_DESIGNTIME_DATABASE_PROVIDER</c> (D-05).
/// Cette classe ne contient ni logique PostgreSQL, ni chaîne de connexion, ni exécution de migration. Un
/// test le vérifie sur l'IL compilé.
/// </para>
/// </summary>
internal sealed class OpticDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OpticDbContext>
{
    public OpticDbContext CreateDbContext(string[] args) => new OpticDbContextFactory().CreateDbContext(args);
}
