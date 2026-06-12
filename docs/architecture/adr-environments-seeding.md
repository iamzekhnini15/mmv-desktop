# ADR — Environnements, configuration et seeds (P2A-1F)

> **Statut : ACCEPTÉ (portée P2A-1F, solution minimale et sûre).**
> Décision *implémentée* pour cette phase, **volontairement réduite** et compatible avec la future couche
> Application / vertical slice et l'évolution multi-magasin/SaaS (cf.
> [migration-roadmap §Étape 1F](migration-roadmap.md), [risk-register R-16/R-17](risk-register.md),
> [MMV-master-professionalization-roadmap §P2A-1F](../roadmap/MMV-master-professionalization-roadmap.md)).
> **Aucune couche Application, aucune fiscalité, aucune règle nationale (BE/MA), aucun modèle monétaire,
> aucune organisation/magasin** n'est traité ici. Préserve le cycle de vie SQLite (P2A-1A/1B), le décrément
> de stock atomique (P2A-1D) et la numérotation transactionnelle (P2A-1E). Aucune migration créée.

Date : 12 juin 2026. Branche : `phase2a-stabilization`.

---

## 1. Contexte

L'audit Phase 1 a classé deux risques de **données/sécurité** sur le seeding au démarrage :

- **R-16 (MEDIUM, Données)** — *« Seed de démo (50 clients / 100 produits / 60 ventes) exécuté en
  production »* → données factices en base réelle. Source : [`DbInitializer.cs`](../../src/MMV.Infrastructure/Data/DbInitializer.cs),
  lancé inconditionnellement par [`App.axaml.cs`](../../src/MMV.App/App.axaml.cs).
- **R-17 (MEDIUM, Sécurité)** — *« Mot de passe admin par défaut « admin » seedé (hash partagé) »* → compte
  par défaut faible. Source : [`DbInitializer.cs:93-108`](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L93-L108).

### 1.1 Inventaire réel du seeding et de la configuration (recensement du dépôt)

Recherche : `DbInitializer`, `DbSeeder`, `Seed`, `admin`, `Password`, `Demo`, `Development`, `Production`,
`Environment`, `appsettings`, `EnsureCreated`, `Migrate`, `UseSqlite`, `ConnectionString`, `Random`,
`DateTime.UtcNow/Now`.

| # | Élément | Comportement AVANT | Risque production | Décision P2A-1F |
|---|---|---|---|---|
| 1 | [`App.axaml.cs`](../../src/MMV.App/App.axaml.cs) → `DbInitializer.Initialize` | **inconditionnel** au démarrage, tous environnements | données démo + compte faible en prod (R-16/R-17) | **Remplacé** par `DatabaseSeeder.Seed(context, SeedOptions)` gouverné par l'environnement |
| 2 | [`DbInitializer.cs`](../../src/MMV.Infrastructure/Data/DbInitializer.cs) | seed démo (50 clients…) + 4 comptes dont `admin/admin` | R-16/R-17 | **Conservé** comme **jeu de démonstration** (Development/Demonstration **explicite**). Rendu **idempotent** sur `Username` (pas de collision avec un admin préexistant) |
| 3 | [`InitialCreate`](../../src/MMV.Infrastructure/Migrations/20260127184542_InitialCreate.cs#L324) `InsertData Users` | insère `admin` (UserId=1) lors de `Migrate()` | compte faible via migration | **Neutralisé en aval** : la migration [`AddCounterSaleFieldsToOrder`](../../src/MMV.Infrastructure/Migrations/20260212164646_AddCounterSaleFieldsToOrder.cs#L14) **supprime** déjà cette ligne (`DeleteData UserId=1`). Une base migrée jusqu'à la tête **ne contient aucun utilisateur**. `DatabaseSeeder` neutralise par sécurité tout compte faible **résiduel** (base historique) |
| 4 | [`DbContextTests`](../../tests/MMV.Domain.Tests/Data/DbContextTests.cs) | exerce `DbInitializer` (test isolé) | n/a (test) | **Conservé** (seed de test contrôlé) |
| 5 | Configuration BD | [`SqliteDatabasePathResolver`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) : var. env. `MMV_DATABASE_PATH` → config → défaut `%LOCALAPPDATA%` | aucun (déjà unifié P2A-1A) | **Réutilisé** comme **convention** (`MMV_*`) pour la configuration d'environnement |
| 6 | `appsettings*.json` | **absents** du dépôt | aucun | **Inchangé** : configuration par **variable d'environnement** (cf. §3, option 6) |
| 7 | `EnsureCreated` ([`DbInitializer.cs:16`](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16)) | no-op en prod (base déjà migrée, P2A-1A) | aucun | **Inchangé** |
| 8 | `DateTime.UtcNow` (DbInitializer) / `Random` (DbInitializer, `UserValidator.GenerateStrongPassword`) | données de **démo** / génération mot de passe | hors flux prod | **Inchangé** (hors périmètre : R-18 traité plus tard) |

**Constat clé.** Le risque « compte faible via migration » (chaîne #3) est **déjà neutralisé** par la
migration existante `AddCounterSaleFieldsToOrder` (qui supprime l'`admin` inséré par `InitialCreate`). Le
seul vecteur réel restant d'un compte `admin/admin` est le **jeu de démonstration** (#2) ou une **base
historique** ; et le seul vecteur de **données démo en prod** est l'**appel inconditionnel** (#1). P2A-1F
traite ces deux vecteurs sans toucher aux migrations.

---

## 2. Décision

**Environnement par défaut = `Production` (le plus restrictif).** Le seeding au démarrage est gouverné par un
point de décision **unique** ([`DatabaseSeeder`](../../src/MMV.Infrastructure/Configuration/DatabaseSeeder.cs))
à partir d'options **typées** ([`SeedOptions`](../../src/MMV.Infrastructure/Configuration/SeedOptions.cs))
résolues depuis l'environnement ([`SeedOptionsResolver`](../../src/MMV.Infrastructure/Configuration/SeedOptionsResolver.cs)) :

| Environnement | Données de démonstration | Compte administrateur |
|---|---|---|
| **Production** | **jamais** (même si `EnableDemoSeed=true` : défense en profondeur) | bootstrap **sécurisé** si secret fourni, sinon tout compte faible résiduel est **désactivé** ; aucun `admin/admin` créé |
| **Development** | **oui, si `EnableDemoSeed=true`** (explicite) | jeu de démonstration (dont `admin/admin`, non sensible) ou bootstrap |
| **Demonstration** | **oui, si `EnableDemoSeed=true`** (explicite) | idem Development |
| **Test** | **jamais** au démarrage (les tests appellent le seeder/`DbInitializer` explicitement) | bootstrap si secret fourni, sinon compte faible résiduel désactivé |

### 2.1 Détection de l'environnement

Par **variable d'environnement** `MMV_ENVIRONMENT` (`Production`/`Development`/`Demonstration`/`Test`, alias
`prod`/`dev`/`demo`/`test`, insensible à la casse). **Absente ou non reconnue ⇒ `Production`** (défaut sûr).
Convention cohérente avec `MMV_DATABASE_PATH` (P2A-1A).

### 2.2 Seeds autorisés

- **Démonstration** : `DbInitializer` (inchangé fonctionnellement), uniquement si
  `MMV_ENABLE_DEMO_SEED` ∈ {`1`,`true`,`yes`,`on`} **et** environnement Development/Demonstration.
- **Tests** : seeds appelés **explicitement** par les tests (isolés, non déclenchés au démarrage).
- **Production** : **aucun** seed de démonstration.

### 2.3 Éviter `admin/admin` en production — compte bootstrap sécurisé

Le mot de passe du compte bootstrap est un **secret hors dépôt** : variable d'environnement
`MMV_BOOTSTRAP_ADMIN_PASSWORD` (nom via `MMV_BOOTSTRAP_ADMIN_USERNAME`, défaut `admin`).

- **Secret fourni et valide** (politique forte de [`UserValidator.ValidatePasswordPolicy`](../../src/MMV.Domain/Validators/UserValidator.cs)) :
  `DatabaseSeeder` **sécurise** le compte par défaut existant (réécrit son hash, réactive) **ou** crée un
  administrateur si aucun n'existe. Jamais de `admin/admin`.
- **Secret absent** : aucun compte n'est provisionné automatiquement, et tout compte par défaut **faible
  résiduel** (hash par défaut connu) encore actif est **désactivé** (`IsActive=false` ⇒
  [`AuthenticationService`](../../src/MMV.Infrastructure/Services/AuthenticationService.cs#L37) refuse la connexion).
- **Secret fourni mais invalide** : **blocage explicite du démarrage**
  ([`SeedConfigurationException`](../../src/MMV.Infrastructure/Configuration/SeedConfigurationException.cs)) —
  « configuration invalide bloquée ». À distinguer d'une configuration **absente/ambiguë** (défaut sûr, sans exception).

La détection « compte par défaut faible » repose sur les **hash BCrypt par défaut connus** (migration
`InitialCreate` et jeu de démonstration) : un administrateur dont le mot de passe a **déjà été changé** par
l'exploitant n'est **jamais** touché (idempotence, pas de régression).

### 2.4 Logs de démarrage non sensibles

Le [`SeedResult`](../../src/MMV.Infrastructure/Configuration/SeedResult.cs) journalisé ne contient que des
booléens/notes (environnement, démo appliquée, bootstrap configuré, compte faible neutralisé) — **jamais** de
mot de passe ni de hash.

---

## 3. Options comparées

| # | Option | Avantages | Inconvénients | Retenu |
|---|---|---|---|---|
| 1 | **Seed automatique toujours actif** | simple, démarrage « prêt à l'emploi » | **R-16 + R-17 en production** ; données factices irréversibles | ❌ rejeté |
| 2 | **Seed automatique uniquement en Development** | retire la démo de prod | ne couvre pas le compte bootstrap ; binaire (pas de mode Démo dédié) | ⚠️ partiel |
| 3 | **Mode démonstration explicite** | démo claire, réservée à la présentation | nécessite un drapeau explicite | ✅ **retenu** (Development/Demonstration + `EnableDemoSeed`) |
| 4 | **Seed production minimal sans compte faible** | sûr par défaut ; pas de compte exploitable faible | impose un provisioning bootstrap explicite | ✅ **retenu** (production = aucune démo ; bootstrap via secret, sinon neutralisation) |
| 5 | **Configuration par `appsettings`** | format standard, lisible | ajoute fichiers/paquets ; **risque de secret commité** ; pas de fichier appsettings aujourd'hui | ❌ différé (compatible plus tard) |
| 6 | **Configuration par variable d'environnement** | secrets hors dépôt ; cohérent avec `MMV_DATABASE_PATH` ; testable (source injectable) | moins « découvrable » qu'un fichier | ✅ **retenu** (mécanisme primaire) |

**Décision = 3 + 4 + 6.** Production sans donnée démo ni `admin/admin` ; Development/Demonstration avec démo
**explicite** ; Test contrôlé par les tests ; configuration par **variable d'environnement** (secrets hors dépôt).

---

## 4. Compatibilité SQLite desktop

- Le `DatabaseSeeder` agit **uniquement sur les données** (aucune DDL, aucune migration créée). Il s'exécute
  **après** la préparation par [`SqliteDatabaseManager`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs)
  (sauvegarde + migration/adoption P2A-1A/1B), donc sur une base **déjà cohérente**.
- `has-pending-model-changes` reste **false** (modèle EF inchangé : aucune entité, aucune `HasData`, aucune config ajoutée).
- La table de numérotation `DocumentSequences` (P2A-1E) et ses compteurs restent **intacts** (prouvé par test).
- L'idempotence du seed utilisateurs ([`DbInitializer`](../../src/MMV.Infrastructure/Data/DbInitializer.cs)) évite
  toute collision sur l'index unique `Username` lorsqu'un compte préexiste.

---

## 5. Conséquences

**Positives** : plus de données de démonstration en production (R-16) ; plus de `admin/admin` exploitable en
production (R-17) ; compte bootstrap **sécurisé** par secret hors dépôt ; configuration **typée** et **validée**
au démarrage ; **défaut sûr** en cas de configuration absente/ambiguë ; périmètre **minimal** (aucune migration,
aucun paquet ajouté) ; SQLite/numérotation préservés.

**Négatives / dette assumée** : une production **vierge sans secret bootstrap** n'a **aucun compte** pour se
connecter tant que `MMV_BOOTSTRAP_ADMIN_PASSWORD` n'est pas fourni (choix **sûr** : pas de compte faible) — le
provisioning doit être documenté pour l'installeur/exploitation (P2J-10A). Pas de « forcer le changement au 1er
login » (nécessiterait une colonne ⇒ migration, hors périmètre). Configuration par `appsettings` différée.

---

## 6. Risques résiduels (hors périmètre, documentés)

1. **Provisioning bootstrap** — sans secret, production sans compte. *Suite : packaging/installation (P2J-10A) et
   authentification/secrets (P2H-8B), « forcer le changement au 1er login ».*
2. **`Random` mot de passe (R-18)** — `UserValidator.GenerateStrongPassword` utilise `Random`. *Suite :
   `RandomNumberGenerator` (P2H-8B).*
3. **Compte faible désactivé, non supprimé** — préserve les FK/audit ; un nettoyage définitif relève d'une
   migration de données ultérieure si décidé.
4. **Migration future SaaS/multi-magasin** — `SeedOptions`/`ApplicationEnvironment` sont des points d'extension
   (provisioning par organisation/magasin, secrets gérés). *Suite : Étapes 3/Programme 11.*
5. **Données de démonstration** — restent **non fiscales** et hors flux production ; cohérentes avec la
   numérotation P2A-1E (formats distincts, sans collision).
