using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Point de décision <b>unique</b> du seeding au démarrage (P2A-1F). Remplace l'appel direct et
/// inconditionnel à <see cref="DbInitializer.Initialize"/> par une politique gouvernée par
/// <see cref="SeedOptions"/> :
///
/// <list type="bullet">
///   <item><b>Production</b> : aucune donnée de démonstration ; le compte par défaut faible
///   (<c>admin/admin</c> seedé par la migration <c>InitialCreate</c> ou le jeu de démonstration) est soit
///   <b>sécurisé</b> via le mot de passe bootstrap fourni, soit <b>neutralisé</b> (désactivé).</item>
///   <item><b>Development/Demonstration</b> avec <see cref="SeedOptions.EnableDemoSeed"/> : le jeu de
///   démonstration (<see cref="DbInitializer"/>) est appliqué explicitement.</item>
///   <item><b>Test</b> et autres : pas de seed de démonstration ; bootstrap appliqué si un secret est fourni.</item>
/// </list>
///
/// Ne crée aucune migration et ne modifie pas le schéma : agit uniquement sur les données. N'expose
/// jamais de secret dans son <see cref="SeedResult"/>.
/// </summary>
public sealed class DatabaseSeeder
{
    /// <summary>
    /// Hash BCrypt des comptes <c>admin</c> par défaut connus (faibles) : celui inséré par la migration
    /// <c>InitialCreate</c> (<see cref="DbInitializer"/>-indépendant, appliqué par <c>Migrate()</c>) et celui
    /// du jeu de démonstration. Sert à détecter un compte par défaut <b>non modifié</b> à neutraliser ou à
    /// sécuriser, sans jamais toucher un compte dont le mot de passe a déjà été changé par l'exploitant.
    /// </summary>
    private static readonly string[] KnownDefaultAdminPasswordHashes =
    {
        // Migration InitialCreate (InsertData Users, UserId = 1).
        "$2a$11$dXJ3SW6G7P50eS6xFJwFHeJ/hbtjiZlyCloO/sURR8EZ4/nqXJcOy",
        // Jeu de démonstration DbInitializer.CreateUsers (mot de passe "admin").
        "$2a$11$QA85M79Q7bLajCAGRrVS1ONdstKLbV0S/vX6OKtN3CsCF3MWSNMoi"
    };

    private const int BcryptWorkFactor = 11;

    /// <summary>
    /// Applique la politique de seeding correspondant aux <paramref name="options"/> sur le contexte fourni
    /// (base déjà préparée/migrée par <see cref="SqliteDatabaseManager"/>).
    /// </summary>
    public SeedResult Seed(OpticDbContext context, SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ShouldSeedDemoData)
        {
            DbInitializer.Initialize(context);
            return new SeedResult
            {
                Environment = options.Environment,
                DemoSeedApplied = true,
                Notes = $"Jeu de démonstration appliqué (environnement {options.Environment})."
            };
        }

        // Production / Test / Development|Demonstration sans demo : aucune donnée de démonstration.
        return SecureBootstrap(context, options);
    }

    /// <summary>
    /// Garantit l'absence de compte <c>admin/admin</c> exploitable. Si un secret bootstrap est fourni, le
    /// compte par défaut est sécurisé (ou créé) ; sinon, le compte par défaut faible est désactivé.
    /// Idempotent : ne touche jamais un compte administrateur réel (mot de passe déjà changé).
    /// </summary>
    private SeedResult SecureBootstrap(OpticDbContext context, SeedOptions options)
    {
        // P3-10 (R4) — TOUS les comptes portant un hash faible connu sont traités, pas seulement le premier.
        //
        // La version antérieure prenait un FirstOrDefault : elle suffisait pour une base neuve (un seul compte
        // admin issu de la migration InitialCreate), mais laissait ACTIFS marie.optic, pierre.tech et
        // sophie.optic — qui portent le même hash « admin » — sur une base autrefois seedée en démonstration puis
        // exploitée en production. Trois identifiants faibles connus publiquement y restaient utilisables (audit
        // P3-10 §24, R4). L'ensemble est donc matérialisé et traité en bloc.
        var weakAccounts = context.Users
            .Where(u => KnownDefaultAdminPasswordHashes.Contains(u.PasswordHash))
            .ToList();

        // Compte canonique à sécuriser : identifié par sa forme NORMALISÉE (P3-10), afin qu'un « Admin » hérité
        // soit reconnu comme le compte bootstrap et non traité comme un compte faible anonyme de plus.
        var bootstrapNormalizedUsername = UserIdentityPolicy.NormalizeUsername(options.BootstrapAdminUsername);

        // Le login bootstrap est-il déjà occupé par un compte qui n'est PAS un compte faible connu ? Si oui,
        // aucun compte faible ne peut être renommé vers ce login : depuis P3-10, l'index unique sur
        // NormalizedUsername rejetterait l'écriture et le seed ferait échouer le DÉMARRAGE de l'application.
        var bootstrapLoginTakenByRealAccount = context.Users.Any(u =>
            u.NormalizedUsername == bootstrapNormalizedUsername
            && !KnownDefaultAdminPasswordHashes.Contains(u.PasswordHash));

        // Le repli « premier compte faible venu » ne s'applique que si le login bootstrap est LIBRE. Sans cette
        // garde, une base portant un administrateur réel sous « admin » et un compte faible sous un autre login
        // voyait ce dernier renommé en « admin » — collision d'unicité au démarrage, et tentative d'écrasement
        // de l'identité d'un compte réel. Le renommage n'est légitime que vers un login que personne n'occupe.
        var defaultAdmin = weakAccounts.FirstOrDefault(u => u.NormalizedUsername == bootstrapNormalizedUsername);
        if (defaultAdmin is null && !bootstrapLoginTakenByRealAccount)
        {
            defaultAdmin = weakAccounts.FirstOrDefault();
        }

        if (options.HasBootstrapAdminPassword)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(options.BootstrapAdminPassword, BcryptWorkFactor);

            if (defaultAdmin is not null)
            {
                // Sécurise le compte par défaut existant (réutilise l'identité, ne casse aucune FK).
                defaultAdmin.Username = options.BootstrapAdminUsername;
                defaultAdmin.NormalizedUsername = bootstrapNormalizedUsername;
                defaultAdmin.PasswordHash = hash;
                defaultAdmin.IsActive = true;

                // Les AUTRES comptes faibles ne sont pas sécurisés (aucun secret ne leur correspond) : ils sont
                // désactivés. Fournir un secret bootstrap ne doit jamais laisser subsister un second identifiant
                // faible exploitable à côté du compte administrateur sécurisé.
                var alsoNeutralized = NeutralizeWeakAccounts(weakAccounts, except: defaultAdmin);

                context.SaveChanges();

                return new SeedResult
                {
                    Environment = options.Environment,
                    BootstrapAdminConfigured = true,
                    WeakDefaultAdminNeutralized = alsoNeutralized > 0,
                    Notes = "Compte administrateur bootstrap sécurisé (mot de passe fort issu du secret fourni)."
                            + (alsoNeutralized > 0
                                ? $" {alsoNeutralized} autre(s) compte(s) par défaut faible(s) désactivé(s)."
                                : string.Empty)
                };
            }

            if (!context.Users.Any(u => u.Role == UserRole.Admin))
            {
                // Aucun administrateur : on provisionne le compte bootstrap.
                context.Users.Add(new User
                {
                    Username = options.BootstrapAdminUsername,
                    NormalizedUsername = bootstrapNormalizedUsername,
                    PasswordHash = hash,
                    FirstName = "Administrateur",
                    LastName = "Bootstrap",
                    Role = UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                context.SaveChanges();

                return new SeedResult
                {
                    Environment = options.Environment,
                    BootstrapAdminConfigured = true,
                    Notes = "Compte administrateur bootstrap créé (mot de passe fort issu du secret fourni)."
                };
            }

            // Un administrateur réel existe déjà : ne rien écraser — mais les comptes faibles restants doivent
            // TOUJOURS être neutralisés. Ce chemin retournait auparavant sans les désactiver : une base portant
            // un administrateur réel ET des comptes de démonstration faibles gardait ces derniers ACTIFS en
            // production, avec des mots de passe publiquement connus (R4 non couvert sur cette branche).
            var neutralizedBesideRealAdmin = NeutralizeWeakAccounts(weakAccounts, except: null);
            if (neutralizedBesideRealAdmin > 0)
            {
                context.SaveChanges();
            }

            return new SeedResult
            {
                Environment = options.Environment,
                WeakDefaultAdminNeutralized = neutralizedBesideRealAdmin > 0,
                Notes = "Administrateur réel déjà présent : aucun changement (compte bootstrap non écrasé)."
                        + (neutralizedBesideRealAdmin > 0
                            ? $" {neutralizedBesideRealAdmin} compte(s) par défaut faible(s) désactivé(s)."
                            : string.Empty)
            };
        }

        // Pas de secret : neutraliser TOUS les comptes par défaut faibles encore actifs (P3-10 R4).
        var neutralized = NeutralizeWeakAccounts(weakAccounts, except: null);
        if (neutralized > 0)
        {
            context.SaveChanges();

            return new SeedResult
            {
                Environment = options.Environment,
                WeakDefaultAdminNeutralized = true,
                Notes = $"{neutralized} compte(s) par défaut faible(s) désactivé(s) : aucune connexion possible " +
                        "tant qu'un mot de passe bootstrap fort n'est pas fourni."
            };
        }

        return new SeedResult
        {
            Environment = options.Environment,
            Notes = "Aucun compte par défaut faible présent ; aucun seed de démonstration appliqué."
        };
    }

    /// <summary>
    /// Désactive les comptes portant un hash faible connu, en épargnant éventuellement le compte bootstrap qui
    /// vient d'être sécurisé. Idempotent : un compte déjà inactif n'est pas recompté, de sorte qu'un second
    /// passage du seed ne signale aucun changement et n'écrit rien.
    /// </summary>
    /// <returns>Nombre de comptes réellement désactivés par cet appel.</returns>
    private static int NeutralizeWeakAccounts(IEnumerable<User> weakAccounts, User? except)
    {
        var count = 0;
        foreach (var account in weakAccounts)
        {
            if (ReferenceEquals(account, except) || !account.IsActive)
                continue;

            account.IsActive = false;
            count++;
        }

        return count;
    }
}
