# P2A-1F — Environnements, configuration et seeds — Rapport

> **Programme 1 — Stabiliser le socle existant.** Phase **P2A-1F**. Branche `phase2a-stabilization`.
> Date : 12 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> Aucune couche Application, aucune fiscalité, aucune règle nationale, aucun modèle monétaire,
> aucune organisation/magasin, aucune migration.

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2A-1F` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

**Objectif strict** : séparer Development / Test / Demonstration / Production ; supprimer le seed démo et le
compte `admin/admin` en production ; configurer explicitement seeds et mode d'environnement ; préserver SQLite
(P2A-1A/1B), le stock (P2A-1D) et la numérotation (P2A-1E) ; corriger le warning `xUnit1031` ; rapport complet.

---

## 2. Prérequis

**P2A-1E = GO définitif** (préalable satisfait) :

| Élément | Valeur |
|---|---|
| Verdict | GO définitif |
| Commit | `813d115` (feat) / `c3f1f55` (docs CI verte) |
| Branche | `phase2a-stabilization` |
| GitHub Actions | success — run `27402041800`, run #13 |
| Tests | 293/293 |
| `has-pending-model-changes` | false |

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → phase2a-stabilization
git log -5 --oneline      → c3f1f55 docs(P2A-1E)… / 813d115 feat(P2A-1E)… / bf690d2… / 94ff998… / 6f1a030…
git diff --stat           → (vide)
```

Dépôt **propre**, tests **verts**, `has-pending=false` ⇒ démarrage autorisé.

---

## 4. Baseline (avant modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | `8.0.417` |
| `dotnet restore MMV.sln` | ✅ à jour |
| `dotnet build … -c Debug` | ✅ succès — **2 avertissements** (`xUnit1031` [cible 1F] + `CS1998` [préexistant, hors périmètre]) |
| `dotnet test … --no-build` | ✅ **293** (Domain 196 + App 97) |
| `dotnet list package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** |
| `dotnet ef migrations list` | **9** migrations (jusqu'à `20260612071633_AddDocumentSequences`) |
| `dotnet ef migrations has-pending-model-changes` | ✅ **false** |

---

## 5. Inventaire configuration / seeds

Recherche : `DbInitializer`, `DbSeeder`, `Seed`, `admin`, `admin/admin`, `Password`, `Default`, `Demo`,
`Development`, `Production`, `Environment`, `appsettings`, `EnsureCreated`, `Migrate`, `UseSqlite`,
`ConnectionString`, `DateTime.UtcNow/Now`, `Random`.

| # | Fichier | Comportement actuel | Risque en production | Décision P2A-1F |
|---|---|---|---|---|
| 1 | [`App.axaml.cs`](../../src/MMV.App/App.axaml.cs) | `DbInitializer.Initialize` appelé **inconditionnellement** au démarrage | données démo + compte faible (R-16/R-17) | **Remplacé** par `DatabaseSeeder.Seed(ctx, SeedOptions)` gouverné par l'environnement |
| 2 | [`DbInitializer.cs`](../../src/MMV.Infrastructure/Data/DbInitializer.cs) | seed démo (50 clients, 100 produits, 60 ventes) + `admin/admin` (hash `$2a$11$QA85…`) | R-16/R-17 | **Conservé** comme **jeu de démonstration** (explicite, hors prod) ; rendu **idempotent** sur `Username` |
| 3 | [`InitialCreate`](../../src/MMV.Infrastructure/Migrations/20260127184542_InitialCreate.cs#L324) | `InsertData Users` ⇒ `admin` (UserId=1, hash `$2a$11$dXJ3…`) lors de `Migrate()` | compte faible via migration | **Déjà neutralisé** : [`AddCounterSaleFieldsToOrder`](../../src/MMV.Infrastructure/Migrations/20260212164646_AddCounterSaleFieldsToOrder.cs#L14) **supprime** la ligne (`DeleteData UserId=1`). Base migrée tête = **0 utilisateur**. `DatabaseSeeder` neutralise tout résidu |
| 4 | [`DbContextTests.cs`](../../tests/MMV.Domain.Tests/Data/DbContextTests.cs) | exerce `DbInitializer` (seed admin) en isolation | n/a (test) | **Conservé** |
| 5 | [`SqliteDatabasePathResolver.cs`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) | chemin BD : `MMV_DATABASE_PATH` → config → défaut `%LOCALAPPDATA%` | aucun (unifié P2A-1A) | **Réutilisé** : convention `MMV_*` étendue à l'environnement |
| 6 | `appsettings*.json` | **absents** du dépôt | aucun | **Inchangé** : configuration par **variable d'environnement** |
| 7 | `EnsureCreated()` ([DbInitializer.cs:16](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16)) | no-op en prod (base déjà migrée) | aucun | **Inchangé** |
| 8 | `DependencyInjection.cs` / `OpticDbContext.OnConfiguring` | `UseSqlite` + résolution de chemin unique | aucun | **Inchangé** |
| 9 | `DateTime.UtcNow` (DbInitializer) / `Random` (DbInitializer, `UserValidator.GenerateStrongPassword`) | données démo / mot de passe | hors flux prod | **Inchangé** (R-18 différé) |

**Constat clé.** Le compte faible *via migration* (#3) est **déjà** supprimé par une migration existante : une
base à jour ne contient **aucun** utilisateur. Les seuls vecteurs réels restants sont l'**appel inconditionnel**
du seed démo (#1) et le **jeu de démonstration**/bases historiques (#2). P2A-1F les traite **sans toucher aux
migrations**.

---

## 6. ADR et décision

ADR créé : [`docs/architecture/adr-environments-seeding.md`](../architecture/adr-environments-seeding.md)
(**ACCEPTÉ**, portée P2A-1F). Options comparées : (1) seed toujours actif — rejeté ; (2) seed auto en
Development — partiel ; (3) **mode démonstration explicite — retenu** ; (4) **production minimale sans compte
faible — retenu** ; (5) configuration par `appsettings` — différée ; (6) **configuration par variable
d'environnement — retenue**.

**Décision = 3 + 4 + 6** :

| Environnement | Données démo | Compte admin |
|---|---|---|
| **Production** (défaut sûr) | **jamais** | bootstrap **sécurisé** si secret, sinon compte faible résiduel **désactivé** ; **pas de `admin/admin`** |
| **Development** | si `EnableDemoSeed` (explicite) | démo (dont `admin/admin`, non sensible) ou bootstrap |
| **Demonstration** | si `EnableDemoSeed` (explicite) | idem Development |
| **Test** | **jamais** au démarrage | contrôlé par les tests |

---

## 7. Stratégie retenue

1. **Détection d'environnement** par `MMV_ENVIRONMENT` (alias `prod`/`dev`/`demo`/`test`, insensible à la
   casse). **Absent/non reconnu ⇒ Production** (« configuration absente/ambiguë : comportement sûr »).
2. **Options typées** `SeedOptions` résolues par `SeedOptionsResolver` (source injectable pour tests).
3. **Point de décision unique** `DatabaseSeeder.Seed` remplaçant l'appel inconditionnel à `DbInitializer`.
4. **Démo explicite** : `DbInitializer` n'est appelé qu'en Development/Demonstration **et** `MMV_ENABLE_DEMO_SEED`
   activé. **Défense en profondeur** : jamais en production, même drapeau activé.
5. **Compte bootstrap sécurisé** : `MMV_BOOTSTRAP_ADMIN_PASSWORD` (secret hors dépôt), nom via
   `MMV_BOOTSTRAP_ADMIN_USERNAME` (défaut `admin`). Secret valide ⇒ admin fort (réécrit/créé) ; secret absent
   ⇒ compte faible résiduel **désactivé** ; secret **invalide** ⇒ **démarrage bloqué** (`SeedConfigurationException`).
6. **Idempotence** : détection du compte faible par **hash par défaut connu** ⇒ un admin dont le mot de passe a
   déjà été changé n'est **jamais** touché. `DbInitializer` rendu idempotent sur `Username`.
7. **Logs non sensibles** : `SeedResult` ne contient aucun secret/hash.
8. **Validation au démarrage** : `SeedOptionsResolver.Resolve()` appelé **avant** la préparation de la base ;
   une configuration invalide bloque avant toute mutation.

---

## 8. Fichiers modifiés / créés

**Créés (production)** — [`src/MMV.Infrastructure/Configuration/`](../../src/MMV.Infrastructure/Configuration/) :

| Fichier | Rôle |
|---|---|
| `ApplicationEnvironment.cs` | enum Production/Development/Demonstration/Test |
| `SeedOptions.cs` | options typées + `ShouldSeedDemoData` (gating) |
| `SeedOptionsResolver.cs` | résolution depuis l'environnement, défaut sûr, validation |
| `SeedConfigurationException.cs` | blocage explicite d'une config invalide |
| `SeedResult.cs` | résultat non sensible (journal/tests) |
| `DatabaseSeeder.cs` | point de décision unique du seeding |

**Modifiés (production)** :

| Fichier | Changement |
|---|---|
| [`src/MMV.App/App.axaml.cs`](../../src/MMV.App/App.axaml.cs) | résolution `SeedOptions` **avant** la préparation BD ; remplacement de `DbInitializer.Initialize` par `DatabaseSeeder.Seed` ; log non sensible |
| [`src/MMV.Infrastructure/Data/DbInitializer.cs`](../../src/MMV.Infrastructure/Data/DbInitializer.cs) | seed utilisateurs **idempotent** sur `Username` ; références FK via utilisateurs persistés (`performingUser`/`staffUsers`) |

**Créés (tests)** — [`tests/MMV.Domain.Tests/Configuration/`](../../tests/MMV.Domain.Tests/Configuration/) :
`SeedOptionsResolverTests.cs`, `DatabaseSeederTests.cs`.

**Modifié (tests)** : [`tests/MMV.Domain.Tests/Data/DocumentSequencesAdoptionTests.cs`](../../tests/MMV.Domain.Tests/Data/DocumentSequencesAdoptionTests.cs)
— correction `xUnit1031` (méthode `async Task` + `await`, plus de `.GetAwaiter().GetResult()`).

**Créé (doc)** : [`docs/architecture/adr-environments-seeding.md`](../architecture/adr-environments-seeding.md).

---

## 9. Migration créée ou non

**Aucune migration créée.** Le `DatabaseSeeder` agit uniquement sur les **données** (aucune DDL). Le modèle EF
est inchangé (aucune entité, aucune `HasData`, aucune configuration ajoutée) ⇒ `has-pending-model-changes`
reste **false**. Les 9 migrations existantes sont **intactes** (aucune édition d'une migration appliquée).

---

## 10. Tests ajoutés / adaptés

**+27 tests** (293 → **320**).

`SeedOptionsResolverTests` (**17**) : mapping/alias d'environnement et défaut sûr (théorie, 11 cas) ; défaut sans
config = Production sans seed ; démo honorée en Development ; démo **ignorée** en Production (défense en
profondeur) ; mot de passe bootstrap faible ⇒ **exception** ; mot de passe fort accepté ; nom bootstrap vide ⇒ défaut.

`DatabaseSeederTests` (**10**, vraie base SQLite migrée) :

1. Production fraîche ⇒ **0 donnée démo, 0 compte** ;
2. Production + compte faible résiduel ⇒ **désactivé** (`IsActive=false`) ;
3. Production + secret (base fraîche) ⇒ **admin fort** créé (BCrypt vérifié) ;
4. Production + secret + compte faible ⇒ **sécurisé** (mot de passe faible invalidé) ;
5. Development + démo explicite ⇒ **50 clients** seedés ;
6. Démo + admin préexistant ⇒ **pas de collision** `Username` (un seul `admin`) ;
7. Production + démo demandée ⇒ **pas de démo** (défense en profondeur) ;
8. Test + compte faible ⇒ pas de démo, compte **désactivé** ;
9. Production rejouée ⇒ **idempotent** ;
10. Après `Migrate`+seed ⇒ **SQLite cohérent**, `has-pending=false`, **`DocumentSequences` présentes** (SALE/ORDER).

**Correction `xUnit1031`** : `DocumentSequencesAdoptionTests.Adopt_PreNumberingHistoricalDatabase…` passe en
`async Task` et `await` l'allocation de numéro (plus d'appel bloquant).

---

## 11. Commandes exécutées (contrôles finaux)

```
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet ef migrations list --project src/MMV.Infrastructure --no-build --no-connect
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
git status --short ; git diff --stat
```

---

## 12. Résultats

| Contrôle | Résultat |
|---|---|
| `restore` | ✅ à jour |
| `build -c Debug` | ✅ succès — **1 avertissement** (`CS1998`, préexistant, hors périmètre) ; `xUnit1031` **corrigé** |
| `test --no-build` | ✅ **320** (Domain 223 + App 97), 0 échec, 0 ignoré |
| `list package --vulnerable` | ✅ **0 vulnérabilité** |
| `migrations list` | **9** migrations (inchangé) |
| `has-pending-model-changes` | ✅ **false** |

---

## 13. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les 5 projets, transitifs inclus. Aucun paquet NuGet ajouté
(`BCrypt.Net-Next`, `Microsoft.Extensions.Configuration.Abstractions` déjà présents). Pin `FluentAssertions`
maintenu à **6.12.0** (6.x).

---

## 14. Warnings résiduels

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` | [`OrderFormViewModel.cs:458`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458) | **Préexistant**, hors périmètre P2A-1F (couche App, non lié au seed/config ni à P2A-1E). Non modifié |
| `xUnit1031` | `DocumentSequencesAdoptionTests.cs` | **Corrigé** (disparu du build) |

---

## 15. Risques résiduels

1. **Provisioning bootstrap** : une production vierge **sans** `MMV_BOOTSTRAP_ADMIN_PASSWORD` n'a **aucun
   compte** (choix sûr). À documenter pour l'installeur/exploitation. *Suite : P2J-10A, P2H-8B.*
2. **`Random` mot de passe (R-18)** : `UserValidator.GenerateStrongPassword`. *Suite : `RandomNumberGenerator` (P2H-8B).*
3. **Compte faible désactivé, non supprimé** : préserve FK/audit ; suppression définitive = migration de données ultérieure si décidé.
4. **Pas de « changement au 1er login »** : nécessiterait une colonne ⇒ migration (hors périmètre).
5. **Configuration par `appsettings`** : différée (mécanisme variable d'environnement suffisant et sans secret commité).

---

## 16. État Git final

```
 M src/MMV.App/App.axaml.cs
 M src/MMV.Infrastructure/Data/DbInitializer.cs
 M tests/MMV.Domain.Tests/Data/DocumentSequencesAdoptionTests.cs
?? docs/architecture/adr-environments-seeding.md
?? docs/implementation/P2A-1F-report.md
?? src/MMV.Infrastructure/Configuration/
?? tests/MMV.Domain.Tests/Configuration/
```

`git diff --stat` (suivis) : `App.axaml.cs` (+/-), `DbInitializer.cs` (+/-), `DocumentSequencesAdoptionTests.cs` (4 lignes).
**Aucun commit, aucun push** (conforme `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`).

---

## 17. Verdict

### ✅ **P2A-1F = GO** (validation locale)

| Critère d'acceptation | Statut |
|---|---|
| Build vert | ✅ |
| Tests verts (**320** > 293) | ✅ |
| Aucune vulnérabilité High/Critical | ✅ (0) |
| `has-pending-model-changes = false` | ✅ |
| Production ne crée pas de données démo automatiquement | ✅ (testé) |
| Production ne crée pas `admin/admin` | ✅ (0 compte ; résidu faible désactivé — testé) |
| Mode dev/démo explicite permet les seeds | ✅ (testé) |
| SQLite lifecycle reste fonctionnel | ✅ (testé) |
| `DocumentSequences` reste opérationnel | ✅ (testé) |
| Aucune phase Application Layer commencée | ✅ |
| `xUnit1031` corrigé | ✅ |
| Rapport complet | ✅ |

> **Validation distante (CI GitHub Actions) requise** avant verdict *définitif* — non exécutée ici
> (`ALLOW_PUSH=false`).

---

## 18. Prochaine étape candidate (NON exécutée)

**`P2B-2A` — ADR et frontières de couches** (Programme 2) : valider les responsabilités Domain / Application /
Infrastructure / UI, le style applicatif (services ou CQRS léger), validation, transactions, mapping et erreurs
typées — **sans implémentation massive**. Alternative de stabilisation : résorber le `CS1998` préexistant
(`OrderFormViewModel`) si une micro-phase d'hygiène est décidée. **Ne pas démarrer** sans paramètres de phase
explicites et revue humaine du présent rapport.
