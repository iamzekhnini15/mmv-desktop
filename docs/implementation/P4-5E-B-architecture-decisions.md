# P4-5E-B PostgreSQL Migration Architecture Decisions

> **Statut : DÉCISIONS — NON-CODE.** Aucun fichier `.cs`, test, migration EF, `.csproj`, solution, workflow CI,
> configuration ou ADR existant n'a été modifié. Aucun build, aucune restauration, aucun test n'a été exécuté.
> Seules des commandes **en lecture** (`git status`, `git rev-parse`, `grep`, `sed`, `ls`) ont été lancées pour
> vérifier les références citées.
>
> Date : 21 septembre 2026 · Branche : `p4-multi-poste` · HEAD : `440ddcb352b12e0c6900cfd5d6df550e234856c5`
> (P4-5D-R CLOSED).
> Entrée principale : [audit P4-5E-A](P4-5E-A-postgresql-migration-architecture-audit-report.md) (non suivi à ce
> jour). Cadre : [ADR-PROD-DB-005](../architecture/adr-prod-db-005-migration-architecture.md) ·
> [ADR-PROD-DB-006](../architecture/adr-prod-db-006-index-and-model-portability.md) ·
> [ADR-PROD-DB-007](../architecture/adr-prod-db-007-schema-drift-prevention.md) ·
> [ADR-PROD-DB-008](../architecture/adr-prod-db-008-postgresql-integration-testing.md) ·
> [roadmap P4](../architecture/P4-multi-poste-roadmap.md).
> **Le dépôt réel prime toujours sur ce document.**

**Légende des preuves** (reprise de l'audit P4-5E-A)

| Étiquette | Sens |
|---|---|
| `STATIC_CODE_PROOF` | constaté par lecture du code ou des fichiers du dépôt à HEAD |
| `EF_BEHAVIOR — À CONFIRMER` | comportement documenté d'EF Core / Npgsql / PostgreSQL, **non exécuté ici** ; à confirmer par essai en P4-5E-C |

---

## Objective

L'audit P4-5E-A a listé huit décisions bloquantes avant tout code de P4-5E : **D-01, D-03, D-04, D-05, D-06,
D-07, D-08, D-19**. Elles ont été **approuvées**. Ce document :

1. **consigne** chacune d'elles, avec ses règles, sa justification et la manière dont P4-5E-C devra la prouver ;
2. **reporte explicitement** les décisions D-09 … D-18 vers leur lot propriétaire ;
3. **fixe la frontière** de P4-5E-C : ce qui commence, ce qui est interdit, ce qui arrête le travail ;
4. **prépare la revue architecte** en isolant les rares points encore à confirmer.

Il ne crée aucune obligation nouvelle hors du cadre des ADR 005 et 007. Il en précise l'exécution : noms,
variables, emplacement et preuves. [ADR-005 §6](../architecture/adr-prod-db-005-migration-architecture.md)
renvoie explicitement ces détails à P4-5E.

---

## Decisions Summary

| Decision | Choice | Reason | Phase |
|---|---|---|---|
| **D-01** — Assemblys de migrations | **Deux assemblys.** SQLite reste dans `MMV.Infrastructure`. PostgreSQL va dans un nouveau projet, **`MMV.Infrastructure.PostgreSQL.Migrations`**, qui ne contient que des migrations | un instantané par assembly (ADR-005 §2.3) ; les deux chaînes ne doivent jamais se mélanger à la découverte EF | P4-5E-C |
| **D-03** — Baseline et instantané PostgreSQL | **Une baseline unique, `InitialPostgreSqlBaseline`**, générée par `dotnet ef`, jamais retouchée ; son instantané devient la référence | un schéma serveur lisible en un fichier, sans aucun historique SQLite rejoué (ADR-005 §5.4) | P4-5E-C |
| **D-04** — Projet de démarrage design-time | **Le projet de migrations PostgreSQL est son propre projet de démarrage** pour `dotnet ef` | aucune dépendance design-time vers `MMV.App` ; l'UI n'est pas un outil EF | P4-5E-C |
| **D-05** — Variables design-time | **`MMV_DESIGNTIME_DATABASE_PROVIDER`**, lue par la seule factory design-time. Les variables runtime ne sont **jamais** lues pour générer | aucune génération accidentelle ; CI déterministe ; aucun secret de production (C-2, S-8, R-E2) | P4-5E-C |
| **D-06** — Assembly de migrations SQLite | **Aucun changement** : assembly, migrations, instantané et comportement identiques | P4-5E ajoute PostgreSQL sans toucher SQLite (G4) | P4-5E-C (preuve) |
| **D-07** — Cache de modèle EF (P-1) | **Option A : aucun `IModelCacheKeyFactory` personnalisé** | isolation déjà mesurée et verrouillée par un test ; aucune complexité sans besoin avéré | close — garde existante |
| **D-08** — Schéma PostgreSQL | **Schéma `public`**, sans schéma métier dédié ; table d'historique **par défaut de Npgsql** | installation simple, SQL brut existant compatible, instantané SQLite intact | P4-5E-C (baseline) |
| **D-19** — Règle de contribution | **Règle de double migration** : modèle validé → migration SQLite → migration PostgreSQL → deux chaînes testées. **Emplacement fixé pendant l'étape documentaire de P4-5E-C** | rend ADR-005 §5.7 et ADR-007 §5.6 applicables par tout contributeur (G6, S6) | P4-5E-C |

> **D-02 (un seul `DbContext`)** n'appelle aucune décision nouvelle : elle est tranchée par
> [ADR-005 §5.1](../architecture/adr-prod-db-005-migration-architecture.md) (Option 2 rejetée) et **confirmée**
> ici. `OpticDbContext` reste le seul contexte.

---

## Detailed Decisions

### D-01 — Deux assemblys de migrations

**Décision.** Deux assemblys de migrations, un seul modèle, un seul contexte.

```
SQLite                                   PostgreSQL
──────                                   ──────────
MMV.Infrastructure                       MMV.Infrastructure.PostgreSQL.Migrations
  └─ Migrations/                           └─ Migrations/
       14 migrations existantes                 InitialPostgreSqlBaseline
       OpticDbContextModelSnapshot              OpticDbContextModelSnapshot (PostgreSQL)
       (inchangés)
```

**Règles**

| # | Règle |
|---|---|
| D-01.1 | Projet `src/MMV.Infrastructure.PostgreSQL.Migrations/`. Nom d'assembly identique au nom du projet |
| D-01.2 | Le projet ne contient **que** des migrations PostgreSQL et leur instantané. Il ne contient aucune configuration d'entité, aucun repository, aucun service et aucune factory |
| D-01.3 | **Aucune** migration PostgreSQL dans `MMV.Infrastructure`. **Aucune** migration SQLite déplacée, renommée ou modifiée |
| D-01.4 | **Un instantané par assembly** : chacun ne décrit que sa propre chaîne |
| D-01.5 | EF ne doit découvrir **qu'une seule chaîne** par provider : `MigrationsAssembly("MMV.Infrastructure.PostgreSQL.Migrations")` sur la branche PostgreSQL de `DatabaseProviderResolver.Configure` ; pour SQLite, l'assembly par défaut, c'est-à-dire celle du contexte (voir D-06) |
| D-01.6 | Le nom d'assembly passé à `MigrationsAssembly` est porté par **une seule constante**. Un test la compare au nom réel de l'assembly, pour qu'une faute de frappe ne passe pas inaperçue |

**Sens des dépendances** (voir aussi D-04)

```
MMV.Domain ◄── MMV.Infrastructure ◄── MMV.Infrastructure.PostgreSQL.Migrations
                      ▲                            ▲          ▲
                      │                            │          │
                   MMV.App ────────────────────────┘          │
                                                   MMV.Domain.Tests (et tout projet de tests qui en a besoin)
```

- **Autorisé** : migrations PostgreSQL → `MMV.Infrastructure`. `MMV.App` → migrations PostgreSQL (déploiement,
  voir ci-dessous). Projets de tests → migrations PostgreSQL.
- **Interdit** : `MMV.Infrastructure` → migrations PostgreSQL (référence circulaire). `MMV.Domain` ou
  `MMV.Application` → migrations PostgreSQL.
- **Couverture automatique** : le préfixe `MMV.Infrastructure` fait entrer le nouveau projet dans la garde
  existante [ApplicationArchitectureTests.cs:35](../../tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs#L35)
  (`StartsWith("MMV.Infrastructure")`) sans qu'il faille modifier cette garde. `STATIC_CODE_PROOF`.

**Conséquence dérivée : déploiement (tension C-3 de l'audit).** `MigrationsAssembly` reçoit un **nom**,
et l'assembly est chargée à l'exécution (`EF_BEHAVIOR — À CONFIRMER`). `MMV.Infrastructure` ne peut pas la
référencer. La DLL n'arrivera donc à côté de l'exécutable que si **`MMV.App` référence le projet de
migrations**. P4-5E-C inscrit cette référence dans son périmètre. Elle reste inerte tant que le garde-fou de
démarrage est actif. Voir *Architect Review Status*, point R-3.

**Justification.** EF Core ne maintient qu'**un instantané par assembly de migrations**, et il découvre
toutes les migrations `[DbContext(typeof(OpticDbContext))]` de l'assembly configurée (`EF_BEHAVIOR — À
CONFIRMER`). Deux chaînes dans une même assembly se mélangeraient (risque R-E1, impact critique). Par
ailleurs, `SqliteDatabaseManager` reconnaît les migrations SQLite **par suffixe d'identifiant**
([l. 671-737](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L671-L737)) et écrit
`__EFMigrationsHistory` à la main ([l. 744-762](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L744-L762)).
La chaîne SQLite est donc **fixée à sa place**. `STATIC_CODE_PROOF`.

**Preuve attendue en P4-5E-C.** Contexte SQLite : `GetMigrations()` = les **14** identifiants actuels, dans
l'ordre. Contexte Npgsql construit avec une chaîne factice, sans connexion : `GetMigrations()` = la baseline
**seule**. `dotnet build MMV.sln` vert.

---

### D-03 — Baseline PostgreSQL unique

**Décision.** Une seule baseline, nommée **`InitialPostgreSqlBaseline`**. Son identifiant complet,
`<horodatage>_InitialPostgreSqlBaseline`, est fixé par l'outil au moment de la génération.

**Règles**

| # | Règle |
|---|---|
| D-03.1 | Générée **uniquement** par `dotnet ef migrations add`, avec l'outil verrouillé `dotnet-ef 8.0.27` ([.config/dotnet-tools.json](../../.config/dotnet-tools.json)), restauré par `dotnet tool restore` |
| D-03.2 | **Aucune retouche manuelle** : ni de la migration, ni de son `.Designer.cs`, ni de l'instantané. L'exception d'ADR-005 §5.4 (migration de données) ne s'applique pas à une baseline |
| D-03.3 | **Aucune migration PostgreSQL historique artificielle** : les 14 migrations SQLite ne sont ni traduites ni rejouées. La baseline décrit directement le modèle à HEAD |
| D-03.4 | L'instantané généré devient **la référence** de la chaîne PostgreSQL. Toute évolution ultérieure passe par une **nouvelle** migration |
| D-03.5 | Avant fusion, une régénération complète est permise (`migrations remove` puis `migrations add`), une retouche jamais. **Après fusion, `migrations remove` est proscrit** |
| D-03.6 | L'espace de noms et le dossier de sortie sont fixés à la génération par les options de la CLI, puis consignés dans le rapport de P4-5E. Les migrations suivantes les réutilisent |
| D-03.7 | Un script SQL de la baseline (`dotnet ef migrations script`, sans connexion — `EF_BEHAVIOR — À CONFIRMER`) est produit **comme pièce de revue** du rapport. Il n'est **jamais** une source d'exécution : l'Option 3 d'ADR-005, une baseline SQL écrite à la main, reste rejetée |

**Contenu attendu, vérifié par revue statique du DDL généré**

| Doit contenir | Ne doit pas contenir |
|---|---|
| 14 colonnes monétaires en `numeric(12,2)` ([ADR-003](../architecture/adr-prod-db-003-money-persistence.md)) | aucune colonne monétaire en `REAL` ou `TEXT` |
| filtre `"IsCurrent"` sur `idx_workshop_sheets_current_unique` ([ADR-006](../architecture/adr-prod-db-006-index-and-model-portability.md)) | aucun filtre `"IsCurrent" = 1` |
| filtre LowStock de `NotificationConfiguration` (SQL portable, identique sur les deux providers) | aucun `GLOB`, aucune table d'arrêt `CHECK` |
| `timestamp with time zone`, `date`, `boolean`, colonnes identity | aucune annotation `Sqlite:` |
| `InsertData` `DocumentSequences` `SALE` / `ORDER` (issu de `HasData`) | **aucun** `InsertData` d'utilisateur |
| clés étrangères `Restrict` là où le modèle les déclare | aucune opération `EnsureSchema` (voir D-08) |

> **Absence d'utilisateur : comportement voulu.** Le modèle ne porte aucun `HasData` utilisateur ; la baseline
> naît donc sans administrateur, comme une base SQLite migrée jusqu'à la tête
> ([adr-environments-seeding §2.3](../architecture/adr-environments-seeding.md)). Ce n'est **pas** un défaut à
> corriger à la main. Le premier administrateur relève de D-12 (P4-8).

**Justification.** Rejouer l'historique SQLite n'apporterait rien à une base neuve : 102 `AlterColumn` de
reconstruction de table, `GLOB`, filtres entiers. Cet historique n'a aucune valeur sur un serveur
([ADR-005 §2.5](../architecture/adr-prod-db-005-migration-architecture.md)). Une baseline générée reste
cohérente avec son instantané par construction. Une retouche manuelle romprait cette cohérence en silence.

---

### D-04 — Projet de démarrage EF autonome

**Décision.** `MMV.Infrastructure.PostgreSQL.Migrations` sert à la fois de **projet cible** et de **projet de
démarrage** pour toutes les commandes `dotnet ef` visant la chaîne PostgreSQL.

**Commande type**

```bash
# Chaîne PostgreSQL — la variable n'existe que pour cette commande
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql \
  dotnet ef migrations add InitialPostgreSqlBaseline \
    --project         src/MMV.Infrastructure.PostgreSQL.Migrations \
    --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
```

La chaîne SQLite garde **sa** commande actuelle, inchangée : `--project src/MMV.Infrastructure`, sans projet
de démarrage distinct et sans variable ([ci.yml:118](../../.github/workflows/ci.yml#L118)).

**Références nécessaires au projet de migrations**

| Référence | Pourquoi | Preuve |
|---|---|---|
| `ProjectReference` → `MMV.Infrastructure` | `OpticDbContext`, configurations, `OpticDbContextFactory` ; Npgsql arrive **par transitivité** | `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11` n'est pas `PrivateAssets` dans [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj) — `STATIC_CODE_PROOF` |
| `PackageReference` → `Microsoft.EntityFrameworkCore.Design` **8.0.27**, `PrivateAssets=all` | EF exige ce paquet dans le projet de démarrage (`EF_BEHAVIOR — À CONFIRMER`). Celui de `MMV.Infrastructure` est `PrivateAssets=all`, donc **non transitif** | `STATIC_CODE_PROOF` — même paquet, même version que le dépôt : **aucun paquet nouveau** |
| `TargetFramework` `net8.0` | alignement sur tous les projets du dépôt | `STATIC_CODE_PROOF` |

**Absence de dépendance inverse**

- **Aucune** référence vers `MMV.App`, `MMV.Application` ni Avalonia depuis le projet de migrations.
- **Aucune** référence de `MMV.Infrastructure` vers le projet de migrations (D-01).
- Une bibliothèque de classes `net8.0` sert déjà de projet de démarrage dans ce dépôt : la CI actuelle lance
  `dotnet ef` sur `MMV.Infrastructure` **sans** `--startup-project`. `STATIC_CODE_PROOF`.

**La factory reste unique et reste dans `MMV.Infrastructure`.** ADR-007 §5.2 impose **un seul chemin de
génération**. EF cherche une implémentation de `IDesignTimeDbContextFactory<OpticDbContext>` dans
l'assembly du contexte, même quand le projet de démarrage est un autre projet (`EF_BEHAVIOR — À CONFIRMER`).
**Si l'essai infirme ce comportement, P4-5E-C s'arrête et remonte le point.** Dupliquer la factory dans le
projet de migrations créerait un second chemin de génération. Ce serait une décision, pas un contournement.

**Cohérence cible ↔ assembly configurée.** EF refuse une génération dont le projet cible ne correspond pas à
l'assembly de migrations configurée sur le contexte (`EF_BEHAVIOR — À CONFIRMER`). Ce contrôle sert de garde
croisée : une génération PostgreSQL lancée contre `MMV.Infrastructure`, ou l'inverse, échoue au lieu
d'écrire dans la mauvaise chaîne.

---

### D-05 — Séparation complète runtime / design-time

**Décision.** Le design-time dispose de **sa propre variable** et ne lit **jamais** les variables runtime.

| Variable | Lue par | Rôle | Valeurs |
|---|---|---|---|
| **`MMV_DESIGNTIME_DATABASE_PROVIDER`** | `OpticDbContextFactory` **uniquement** | choisit la chaîne visée par `dotnet ef` | absente ou vide ⇒ **SQLite**, comportement actuel au caractère près · `postgresql` (alias `postgres`, `npgsql`, casse et espaces tolérés) ⇒ PostgreSQL · toute autre valeur ⇒ **exception explicite** |
| `MMV_DESIGNTIME_DATABASE_CONNECTION_STRING` | — | **nom réservé, non introduit en P4-5E-C** | — |
| `MMV_DATABASE_PROVIDER` / `MMV_DATABASE_CONNECTION_STRING` | runtime **uniquement** (`DatabaseProviderResolver.Resolve`) | **jamais** lues pour générer une migration | inchangées |

**Règles**

| # | Règle |
|---|---|
| D-05.1 | La factory ne lit ni `MMV_DATABASE_PROVIDER` ni `MMV_DATABASE_CONNECTION_STRING`. Le gel posé par P4-3 ([OpticDbContextFactory.cs:20-23](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L20-L23)) est **levé pour la seule variable design-time** |
| D-05.2 | Le runtime ne lit aucune variable `MMV_DESIGNTIME_*` : `DatabaseProviderResolver.Resolve` et la composition de [App.axaml.cs](../../src/MMV.App/App.axaml.cs) l'ignorent |
| D-05.3 | La valeur est interprétée avec **la même sémantique** que la variable runtime (`ParseProvider`, [DatabaseProviderResolver.cs](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs)) : aucune faute de frappe ne retombe en silence sur SQLite. Le message d'erreur **nomme la variable design-time** et **ne restitue jamais la valeur reçue** |
| D-05.4 | Branche PostgreSQL de la factory : chaîne de connexion **constante, non secrète**, sans mot de passe réel, désignant un serveur **jamais contacté**. Elle est du même type que celle déjà utilisée par [EfModelPortabilityTests.cs:107](../../tests/MMV.Domain.Tests/Data/Portability/EfModelPortabilityTests.cs#L107) |
| D-05.5 | Branche PostgreSQL de la factory : **aucun** appel à `SqliteDatabasePathResolver`, aucune création de dossier. L'effet de bord actuel ([l. 17-18](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L17-L18)) reste confiné à la branche SQLite |
| D-05.6 | En CI, la variable est posée dans le bloc `env:` **du seul step** PostgreSQL, jamais au niveau du job : le step SQLite s'exécute sans elle, donc à l'identique |
| D-05.7 | Toute commande `dotnet ef` qui se connecte (`database update`, `database drop`) est **hors procédure** pour la chaîne PostgreSQL en P4-5E. Elle échoue faute de serveur et d'identifiants valides, ce qui est le comportement voulu |

**Pourquoi la variable de chaîne de connexion n'est pas introduite.** Aucune commande de P4-5E ne se
connecte : génération, contrôle de dérive et script SQL travaillent sur le modèle
([ADR-005 §5.6](../architecture/adr-prod-db-005-migration-architecture.md), `EF_BEHAVIOR — À CONFIRMER`). Une
variable qui accepte une chaîne de connexion invite à y coller une vraie chaîne, avec son secret (S-8). La
supprimer rend la CI déterministe sans rien retirer de fonctionnel. Le nom est **réservé**. Il ne sera
introduit que sur **besoin démontré** et par décision explicite, jamais en CI et jamais avec un secret de
production.

**Tensions et risques traités.** C-2 (couplage génération ↔ environnement runtime), S-8 (secret dans un
shell ou dans la CI), R-E2 (génération sur la mauvaise chaîne), R-E4 (contrôle PostgreSQL rouge faute de
chaîne).

**Preuve attendue en P4-5E-C**

- sans aucune variable : contexte SQLite, chemin et chaîne de connexion identiques à HEAD ;
- `MMV_DATABASE_PROVIDER=postgresql` sans variable design-time : la factory produit **toujours** SQLite ;
- `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` : contexte Npgsql, assembly de migrations PostgreSQL, aucun
  fichier SQLite résolu ni créé ;
- `MMV_DESIGNTIME_DATABASE_PROVIDER=<valeur inconnue>` : exception, sans restitution de la valeur ;
- `Resolve` avec la seule variable `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` : SQLite (étanchéité runtime).

---

### D-06 — Assembly de migrations SQLite inchangée

**Décision.** Ne rien changer côté SQLite.

**Règles**

| # | Règle |
|---|---|
| D-06.1 | **Aucun** appel à `MigrationsAssembly` sur la branche SQLite : l'assembly reste implicitement celle du contexte, `MMV.Infrastructure` |
| D-06.2 | **Aucun** appel à `MigrationsHistoryTable` sur la branche SQLite |
| D-06.3 | Branche SQLite de `DatabaseProviderResolver.Configure` ([l. 103-107](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L103-L107)) **inchangée au caractère près** |
| D-06.4 | `src/MMV.Infrastructure/Migrations/` : **`git diff` vide** entre `440ddcb` et le SHA de clôture de P4-5E (G4) |
| D-06.5 | `SqliteDatabaseManager` **non modifié** : adoption par suffixe, écriture de l'historique, vérifications et reprise des dates civiles restent intactes |
| D-06.6 | Step CI SQLite ([ci.yml:118](../../.github/workflows/ci.yml#L118)) **non modifié** (S2) |

**Justification.** La chaîne SQLite est protégée par des couplages réels (D-01) et par des suites de tests qui
énumèrent `GetMigrations()` (`SqliteDatabaseManagerTests`, `NormalizedUsernameMigrationTests`,
`NotificationResolutionMigrationTests`…). Un `MigrationsAssembly` explicite ne changerait rien au runtime, mais
il ajouterait une différence à justifier sans aucun bénéfice. Avec le défaut implicite, G4 se vérifie
trivialement.

**Preuve attendue en P4-5E-C.** `git diff --stat 440ddcb..HEAD -- src/MMV.Infrastructure/Migrations/` vide ;
tests existants verts sans modification ; un test établit que les options SQLite ne portent **aucune** assembly
de migrations explicite, alors que les options PostgreSQL portent la constante de D-01.6.

---

### D-07 — Cache de modèle EF : Option A

**Décision.** Aucun `IModelCacheKeyFactory` personnalisé. La question P-1, rattachée à P4-5E par
[P4-5C](P4-5C-ef-model-portability-report.md), est **close**.

**Garde existante.**
[`EfModelPortabilityTests.BothProviderModels_CoexistInTheSameProcess_WithoutContaminatingEachOther`](../../tests/MMV.Domain.Tests/Data/Portability/EfModelPortabilityTests.cs#L267)
construit un modèle SQLite **puis** un modèle PostgreSQL dans le même processus. Il vérifie que chacun garde
son filtre d'index et son type monétaire. `STATIC_CODE_PROOF`. Ce test reste **obligatoire et inchangé**.

**Justification.** EF Core 8 donne à chaque provider son propre fournisseur de services interne, donc son
propre cache de modèle. Le comportement est mesuré et verrouillé. Une clé de cache personnalisée ajouterait
du code d'infrastructure sans corriger aucun défaut observé. L'ajout de `MigrationsAssembly` sur la seule
branche PostgreSQL ne change pas la donne : les deux modèles diffèrent déjà par leur provider.

**Conditions de réexamen**

- ce test échoue, par exemple après une montée de version majeure d'EF Core ou de Npgsql ;
- une **seconde variante de modèle pour un même provider** apparaît (schéma variable, multi-tenant) : la clé
  par défaut ne la distinguerait plus.

---

### D-08 — Schéma PostgreSQL `public`

**Décision V1.** Toutes les tables vivent dans le schéma **`public`**. Pas de schéma métier dédié.
`__EFMigrationsHistory` garde **la configuration standard de Npgsql** : nom par défaut, sans schéma explicite.

**Règles**

| # | Règle |
|---|---|
| D-08.1 | **Aucun** `HasDefaultSchema`, **aucun** `ToTable(..., schema)` dans le modèle |
| D-08.2 | **Aucun** `MigrationsHistoryTable(...)` sur la branche PostgreSQL, sauf nécessité technique **démontrée** et décidée |
| D-08.3 | Identifiants **PascalCase entre guillemets**, comme aujourd'hui. **Aucune** convention `snake_case` : elle casserait le SQL brut des repositories (par exemple `NotificationRepository.ActiveLowStockInsertSql`) |
| D-08.4 | Cette décision est **gravée dans la baseline**. La changer ensuite coûterait une migration : elle est donc fixée **avant** la génération (R-E6) |

**Justification**

- **Simplicité d'installation** : aucun schéma à créer ni à posséder, aucun `search_path` à régler par poste.
- **SQL brut existant** : ses identifiants non qualifiés se résolvent via le `search_path` par défaut, qui
  contient `public`.
- **Instantané SQLite intact** : un `HasDefaultSchema` modifierait le modèle partagé, ferait apparaître un
  changement en attente sur la chaîne SQLite et exigerait une migration SQLite, ce que D-06 interdit. Le faire
  passer par `ModelPortability` ajouterait une construction dépendante du provider sans bénéfice en V1.

**Contraintes transmises au provisioning (P4-8)** — `EF_BEHAVIOR — À CONFIRMER`

- Depuis PostgreSQL 15, le droit `CREATE` sur `public` n'est plus accordé à tous les rôles. Le rôle qui
  applique les migrations doit posséder la base ou recevoir ce droit explicitement (D-09, D-10).
- Le `search_path` par défaut est `"$user", public`. Si un schéma portant **le nom du rôle applicatif**
  existe, les tables non qualifiées y seront créées, historique compris. Le provisioning ne doit pas créer un
  tel schéma, ou doit fixer le `search_path`.

**Preuve attendue.** P4-5E-C : la revue statique du DDL ne montre ni `EnsureSchema`, ni qualification de
schéma, ni table d'historique personnalisée. P4-5F : la lecture des catalogues (`information_schema`) confirme
que les tables et l'historique sont dans `public` (S3).

**Condition de réexamen.** Une exigence réelle de cohabitation dans une base partagée, ou un modèle de droits
P4-8 qui ne peut pas être satisfait dans `public`.

---

### D-19 — Règle de double migration

**Décision.** Documenter, à destination de tout contributeur, la règle suivante.

> **Toute évolution du modèle EF exige, dans le même commit :**
>
> 1. **la validation du modèle EF** : configurations d'entités. Toute construction volontairement dépendante
>    du provider passe par `ModelPortability` ([ADR-006 §5.1](../architecture/adr-prod-db-006-index-and-model-portability.md)) ;
> 2. **la migration SQLite** : `dotnet ef migrations add <Nom> --project src/MMV.Infrastructure`, **sans**
>    variable design-time ;
> 3. **la migration PostgreSQL** : même commande, avec `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql`,
>    `--project` et `--startup-project` sur `src/MMV.Infrastructure.PostgreSQL.Migrations` (D-04) ;
> 4. **les tests des deux chaînes** : `has-pending-model-changes` vert sur chacune, suite unitaire verte, et
>    tests d'intégration PostgreSQL verts **une fois P4-5F livré**.
>
> **Exception prévue** ([ADR-005 §5.7](../architecture/adr-prod-db-005-migration-architecture.md),
> [ADR-007 §5.6](../architecture/adr-prod-db-007-schema-drift-prevention.md)) : un changement qui ne concerne
> qu'un provider produit une seule migration. Il doit alors fournir la **preuve** que l'autre chaîne est
> inchangée : son contrôle de dérive reste vert.

**Éléments à inclure dans le document de contribution**

- la **table « quelle chaîne ? »** exigée par [ADR-005 §7.2](../architecture/adr-prod-db-005-migration-architecture.md) :
  dossier, projet, commande et step CI de chaque chaîne ;
- l'obligation d'écrire **toute migration de données** (du type P4-5D-R) pour chaque provider (U-8) ;
- les interdits de D-03 : ni retouche de migration générée, ni `migrations remove` après fusion ;
- la recommandation d'utiliser **le même nom de migration** sur les deux chaînes, pour la traçabilité.

**Emplacement.** Décidé pendant l'**étape documentaire de P4-5E-C**. Aucun `CONTRIBUTING.md` n'existe à ce
jour (`STATIC_CODE_PROOF`). Candidats : un nouveau `CONTRIBUTING.md` à la racine, ou une section dans
`docs/architecture/`. Critère de choix : un contributeur qui modifie une configuration d'entité doit la trouver
sans connaître les ADR. Le rapport de clôture de P4-5E référence l'emplacement retenu, ce qui ferme G6 et S6.

---

## Deferred Decisions

Ces décisions **ne bloquent pas** P4-5E. Elles restent ouvertes et **ne doivent pas** être tranchées
implicitement par le code de P4-5E-C.

| # | Question | Lot propriétaire | Doit être tranchée avant |
|---|---|---|---|
| **D-09** | Qui crée la base : procédure de provisioning, ou l'application via `Migrate()` (qui exige `CREATEDB`) ? | **P4-8** | P4-5G |
| **D-10** | Modèle de droits : un rôle propriétaire unique, ou migrateur (DDL) + applicatif (DML) ? | **P4-6 / P4-8** | P4-5G |
| **D-11** | Premier démarrage : qui applique la baseline (premier poste sous verrou, poste désigné, outil distinct) ? | **P4-6** | P4-5G |
| **D-12** | Premier administrateur sur une base neuve, en multi-poste | **P4-8** + décision produit | ouverture d'une installation réelle |
| **D-13** | Stockage de la chaîne de connexion : variable, fichier machine, DPAPI, `pgpass`, authentification intégrée | **P4-8** | P4-5G |
| **D-14** | Protection des secrets : chiffrement au repos, **TLS exigé**, rotation, droits NTFS | **P4-8** | P4-5G |
| **D-15** | Politique d'échec au démarrage : serveur injoignable, préparation ou seed en échec (R-E8) | **P4-5G / P4-8** | levée du garde-fou |
| **D-16** | Version de la base : historique seul, table de compatibilité, discipline étendre/contracter ; blocage d'un schéma plus récent (U-1) | **P4-6** | premier déploiement multi-poste |
| **D-17** | Numérotation de version de MMV (aucune aujourd'hui) | **P4-6 / P4-8** | D-16 |
| **D-18** | Montée de version d'une base PostgreSQL existante : `Migrate()` sous verrou, ou outil explicite avec sauvegarde obligatoire | **P4-6 + P4-9** | première mise à jour en production |

**Autres points reportés, rappelés pour éviter une dérive de périmètre**

| Point | Lot |
|---|---|
| Tests PostgreSQL sur serveur réel, projet d'intégration dans `MMV.sln`, job CI avec serveur, création d'une base réelle, corpus N1 … N8 (dont N1/N2), S3, S4, S5, G5 | **P4-5F** |
| Gestionnaire de préparation serveur (équivalent de `SqliteDatabaseManager`), **levée du garde-fou** de démarrage | **P4-5G**, uniquement après P4-5F vert |
| Verrou de migration multi-poste (U-2), détection d'un schéma plus récent (U-1, S7), stratégie de montée de version | **P4-6** |
| Provisioning, rôles PostgreSQL, secrets, premier administrateur | **P4-8** |
| Sauvegarde serveur avant migration (U-3), retour arrière (U-7) | **P4-9** |
| Import d'une base SQLite existante (O11) | **P4-7** |
| C-1 — `EnsureCreated()` sur le chemin du seed de démonstration ([DbInitializer.cs:19](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L19)) | qualification architecte **avant P4-5G** |
| C-4 / R-E17 — `AddInfrastructure` inerte, `UseSqlite` en dur | ultérieur, hors P4-5E |

---

## Implementation Boundary

### Ce qui commence en P4-5E-C

P4-5E-C commence au **premier fichier non documentaire** modifié après la revue de ce document. Il couvre
**exactement** ADR-005 **G1, G2, G3, G4, G6** et ADR-007 **S1, S2, S6**, dans cet ordre :

| Étape | Contenu | Décisions | Sortie vérifiable |
|---|---|---|---|
| **C1. Projet** | Créer `src/MMV.Infrastructure.PostgreSQL.Migrations` et l'inscrire dans `MMV.sln`. Références exactement celles de D-04. Ajouter la référence `MMV.App` → projet (D-01, sous réserve de R-3), et la référence du projet de tests concerné | D-01, D-04 | `dotnet build MMV.sln` vert |
| **C2. Sélection** | Branche PostgreSQL de `DatabaseProviderResolver.Configure` : `MigrationsAssembly(<constante>)`. Branche SQLite inchangée au caractère près | D-01.5, D-01.6, D-06, D-08.2 | tests du résolveur étendus |
| **C3. Factory** | `OpticDbContextFactory` sélective par `MMV_DESIGNTIME_DATABASE_PROVIDER`, SQLite par défaut | D-05 | les cinq preuves de D-05 |
| **C4. Baseline** | `dotnet ef migrations add InitialPostgreSqlBaseline` (commande D-04). Aucune retouche. Script SQL de revue | D-03, D-08 | revue statique du DDL selon la table de D-03 |
| **C5. Intégrité SQLite** | — | D-06 | `git diff` vide sur `src/MMV.Infrastructure/Migrations/` ; contrôle SQLite vert |
| **C6. Tests unitaires, sans serveur** | (a) SQLite : 14 migrations exactes ; (b) Npgsql : la baseline seule ; (c) aucun changement de modèle en attente sur chaque chaîne, via l'API EF 8 équivalente à la CLI (`EF_BEHAVIOR — À CONFIRMER`) ; (d) Domain et Application ne référencent pas l'assembly PostgreSQL ; (e) non-régression du garde-fou de démarrage, ou preuve équivalente (R-E16) ; (f) constante = nom réel de l'assembly | D-01, D-05, D-06, D-07 | suite verte, sans test ignoré, compte mesuré (1915 à HEAD, à re-mesurer) |
| **C7. CI** | **Un seul** step ajouté à `ci.yml` : `has-pending-model-changes` sur la chaîne PostgreSQL, `--no-build`, `env:` limité au step. Rien d'autre ne change | D-05.6 | CI verte sur le SHA exact ; démonstration locale consignée que le step échoue si le modèle change sans migration PostgreSQL |
| **C8. Contribution** | Documenter la règle de D-19 à l'emplacement choisi | D-19 | document relu |
| **C9. Clôture** | Rapport P4-5E, réconciliation des chiffres de baseline de tests (C-5), mise à jour du statut dans la roadmap | — | numéro d'exécution CI référencé |

Forme attendue du step CI, **à titre indicatif** (aucun fichier CI n'est modifié par ce document) :

```yaml
- name: Check EF Core pending model changes (PostgreSQL chain)
  env:
    MMV_DESIGNTIME_DATABASE_PROVIDER: postgresql
  run: >
    dotnet ef migrations has-pending-model-changes
    --project src/MMV.Infrastructure.PostgreSQL.Migrations
    --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
    --no-build
```

### Fichiers que P4-5E-C peut toucher

| Fichier ou zone | Nature |
|---|---|
| `src/MMV.Infrastructure.PostgreSQL.Migrations/**` | **nouveau** : `.csproj` et migrations générées |
| `MMV.sln` | ajout du projet |
| `src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs` | **branche PostgreSQL uniquement**, plus la constante |
| `src/MMV.Infrastructure/Data/OpticDbContextFactory.cs` | sélection design-time |
| `src/MMV.App/MMV.App.csproj` | une `ProjectReference`, sous réserve de R-3 |
| `tests/**` | références de projet, tests nouveaux ou étendus |
| `.github/workflows/ci.yml` | **un** step ajouté |
| document de contribution (D-19), rapport de P4-5E, roadmap P4 | documentation |

### Interdits pendant P4-5E-C

- toute modification de `src/MMV.Infrastructure/Migrations/**` (G4) ;
- toute modification de `SqliteDatabaseManager` ;
- toute modification du garde-fou de démarrage ([App.axaml.cs:200-214](../../src/MMV.App/App.axaml.cs#L200-L214)) : **il reste actif** ;
- toute modification du modèle : `OpticDbContext`, configurations d'entités, `ModelPortability`,
  `UtcDateTimeConverter` ;
- toute modification d'un ADR existant ;
- toute autre modification de `ci.yml` (S2) ;
- tout nouveau paquet NuGet, ou toute nouvelle version d'un paquet existant ;
- toute chaîne de connexion réelle ou tout secret, dans le dépôt comme dans le workflow ;
- tout appel à `Migrate()`, `EnsureCreated()` ou toute connexion à un serveur PostgreSQL.

### Conditions d'arrêt

P4-5E-C **s'arrête et remonte le point à l'architecte**, sans contournement, si :

1. EF ne trouve pas la factory de `MMV.Infrastructure` quand le projet de démarrage est le projet de
   migrations (D-04) ;
2. la baseline générée contredit la table de D-03 (par exemple une colonne monétaire en `REAL` ou un filtre
   entier) ;
3. la génération ou le contrôle de dérive PostgreSQL exige une connexion à un serveur ;
4. le contrôle de dérive **SQLite** devient rouge, ou un fichier de `src/MMV.Infrastructure/Migrations/`
   apparaît dans le diff ;
5. une modification du modèle paraît nécessaire pour obtenir une baseline correcte.

### Critères de clôture de P4-5E

1. Assembly PostgreSQL présente, baseline **générée**, instantané propre, **aucune** migration SQLite modifiée.
2. `has-pending-model-changes` **vert et bloquant sur les deux chaînes**, en CI, sans serveur.
3. Suite unitaire verte, sans test ignoré, compte mesuré et publié.
4. Garde-fou de démarrage **toujours actif** et prouvé.
5. Règle de double migration documentée et référencée.
6. Aucune variable ni chaîne de connexion de production dans le dépôt ou dans le workflow.

**Ce que P4-5E ne prouvera pas** : que la baseline **s'applique** sur un vrai serveur, qu'elle est
reproductible, que le schéma créé est physiquement conforme. Ces preuves relèvent de P4-5F (N1, N2, S3, S4,
G5). La revue statique de C4 est une **lecture** du DDL généré, pas une preuve d'exécution, et le rapport de
clôture devra la présenter comme telle.

---

## Architect Review Status

**READY FOR IMPLEMENTATION REVIEW**

Les huit décisions bloquantes de l'audit P4-5E-A sont consignées. La revue doit encore confirmer les points
ci-dessous. Aucun ne remet en cause une décision approuvée.

| # | Point à confirmer | Nature | Origine |
|---|---|---|---|
| **R-1** | **G-1** — ouvrir P4-5E en `NEXT`, et confirmer que RR4 (déploiement) et les tests de spécification T-B1, T-B4 … T-B8 **ne précèdent pas** P4-5E | gouvernance | [réconciliation P4-5, D-4](P4-5-reconciliation-audit-report.md) |
| **R-2** | **G-2** — trancher l'inversion de dépendance P4-4 ↔ P4-5 (R-5B-6), toujours « constatée, non tranchée » | gouvernance | [P4-5B, R-5B-6](P4-5B-postgresql-architecture-decisions-report.md) |
| **R-3** | Référence `MMV.App` → migrations PostgreSQL **dès P4-5E-C** (proposé), plutôt qu'en P4-5G | conséquence dérivée de D-01 (C-3) | ce document, D-01 |
| **R-4** | `MMV_DESIGNTIME_DATABASE_CONNECTION_STRING` **réservée, non introduite** ; chaîne factice constante à la place | précision de D-05 (« éventuellement ») | ce document, D-05 |

La question **P-1**, citée par la réconciliation P4-5 (D-4) comme préalable à l'ouverture de P4-5E, est
**close par D-07**.

```
P4-5E-A                      = AUDIT COMPLETE
P4-5E-B                      = DECISIONS RECORDED — READY FOR IMPLEMENTATION REVIEW
DECISIONS CLOSED             = D-01, D-03, D-04, D-05, D-06, D-07, D-08, D-19 (+ D-02 CONFIRMED)
DECISIONS DEFERRED           = D-09 … D-18 (P4-5G, P4-6, P4-8, P4-9)
REVIEW ITEMS                 = R-1, R-2 (GOVERNANCE) · R-3, R-4 (DERIVED PRECISIONS)
P4-5E-C                      = NOT STARTED — BOUNDARY DEFINED
PG MIGRATION CHAIN           = ABSENT
SQLITE MIGRATION CHAIN       = 14 MIGRATIONS INTACT SINCE 8d4bf49 (P3-10)
PG STARTUP                   = BLOCKED BY DESIGN (App.axaml.cs:205) — UNCHANGED BY P4-5E
```
