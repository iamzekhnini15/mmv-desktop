using MMV.Domain.Entities;
using MMV.Domain.Enums;
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
        var defaultAdmin = context.Users
            .FirstOrDefault(u => KnownDefaultAdminPasswordHashes.Contains(u.PasswordHash));

        if (options.HasBootstrapAdminPassword)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(options.BootstrapAdminPassword, BcryptWorkFactor);

            if (defaultAdmin is not null)
            {
                // Sécurise le compte par défaut existant (réutilise l'identité, ne casse aucune FK).
                defaultAdmin.Username = options.BootstrapAdminUsername;
                defaultAdmin.PasswordHash = hash;
                defaultAdmin.IsActive = true;
                context.SaveChanges();

                return new SeedResult
                {
                    Environment = options.Environment,
                    BootstrapAdminConfigured = true,
                    Notes = "Compte administrateur bootstrap sécurisé (mot de passe fort issu du secret fourni)."
                };
            }

            if (!context.Users.Any(u => u.Role == UserRole.Admin))
            {
                // Aucun administrateur : on provisionne le compte bootstrap.
                context.Users.Add(new User
                {
                    Username = options.BootstrapAdminUsername,
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

            // Un administrateur réel existe déjà : ne rien écraser.
            return new SeedResult
            {
                Environment = options.Environment,
                Notes = "Administrateur réel déjà présent : aucun changement (compte bootstrap non écrasé)."
            };
        }

        // Pas de secret : neutraliser tout compte par défaut faible encore actif.
        if (defaultAdmin is not null && defaultAdmin.IsActive)
        {
            defaultAdmin.IsActive = false;
            context.SaveChanges();

            return new SeedResult
            {
                Environment = options.Environment,
                WeakDefaultAdminNeutralized = true,
                Notes = "Compte par défaut faible (admin/admin) désactivé : aucune connexion possible " +
                        "tant qu'un mot de passe bootstrap fort n'est pas fourni."
            };
        }

        return new SeedResult
        {
            Environment = options.Environment,
            Notes = "Aucun compte par défaut faible présent ; aucun seed de démonstration appliqué."
        };
    }
}
