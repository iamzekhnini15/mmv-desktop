# P4-6 Transition Audit Report

> **Note d'enregistrement (22 septembre 2026).** Ce rapport est **enregistré au dépôt** par le commit documentaire
> de la [revue d'acceptation P4-6A](P4-6A-adr-acceptance-report.md). Les mentions « non commité », « aucun commit »
> ou « non suivi » ci-dessous décrivent l'état **au moment de sa rédaction** : elles restent exactes pour le lot
> qu'elles décrivent, qui n'a produit **aucun commit de code**.

> **Statut : AUDIT DE TRANSITION — PRÉ-REVUE UNIQUEMENT.** Aucun code, aucun test, aucune migration, aucune ADR,
> aucune CI ni aucune roadmap n'a été modifié. Aucun commit, aucun push. Seul ce rapport est créé, et il n'est
> pas commité.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> HEAD audité : **`ccbeb3259444d26264e20323058764dd59318e1c`** (`docs(P4-5E-C9): align EF migration documentation`).
> **Le dépôt réel prime sur ce document, et sur la roadmap.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `GIT` | constaté par `git log`, `git show`, `git ls-remote` |
| `CODE` | constaté par lecture du code à HEAD |
| `DOC` | énoncé d'un document du dépôt (ADR, roadmap, rapport) |
| `NON VÉRIFIABLE` | affirmé par le brief, sans moyen de vérification depuis ce poste |
| `UNKNOWN` | absent du code et de la documentation : rien n'est inventé |

**Commandes exécutées** (lecture seule) : `git rev-parse`, `git status`, `git log`, `git show --stat`,
`git branch -vv`, `git ls-remote --heads origin`, `grep`, `sed`, `find`. **Aucun build ni test relancé** : le
code n'a pas changé depuis la vérification finale P4-5 (1953 / 1953, même HEAD). **CI non interrogée** : `gh`
n'est pas installé sur le poste.

---

## 0. Résumé exécutif

1. **P4-5 n'est pas terminée au sens de la roadmap.** P4-5A à P4-5E sont commités et poussés. **P4-5F**
   (tests d'intégration PostgreSQL) et **P4-5G** (préparation serveur et levée du garde-fou) **n'ont aucun
   commit**. Le critère de sortie de P4-5, « base serveur neuve créée par migrations, contraintes prouvées »,
   **n'est pas atteint** : la baseline PostgreSQL n'a jamais été exécutée contre un serveur. Si l'architecte
   considère P4-5 close à P4-5E, **ce re-découpage n'est enregistré nulle part**.
2. **La roadmap est périmée.** Son dernier commit est `440ddcb`. Elle donne encore P4-5C, P4-5D et P4-5E en
   `NOT STARTED`. Les corrections C-01 … C-22 de la réconciliation P4-5 ne sont pas appliquées, et le rapport
   de clôture P4-5E prévu par P4-5E-B (étape C9) n'existe pas.
3. **Le périmètre documenté de P4-6 est étroit et précis** : obligation **O8** d'ADR-002, soit (a) sérialiser
   l'application des migrations sur la base centrale, (b) bloquer proprement un client dont la version ne
   correspond pas au schéma. Cinq décisions reportées (D-10, D-11, D-16, D-17, D-18) et quatre manques (U-1,
   U-2, U-4, U-5) lui sont affectés.
4. **Les autres thèmes demandés ne relèvent pas de P4-6 selon les documents.** Sessions, droits applicatifs,
   synchronisation entre postes, conflits d'édition et audit métier ne sont affectés à **aucun** lot P4. Leur
   statut V1 est `UNKNOWN` et demande une décision (§3.3).
5. **P4-5G et P4-6 dépendent l'une de l'autre.** Les décisions D-10 et D-11 appartiennent à P4-6 mais doivent
   être tranchées **avant** P4-5G. Lever le garde-fou sans O8 laisserait chaque poste appeler `Migrate()`
   sans verrou (U-2) et sans garde de version (U-1). **Recommandation : intégrer P4-5G à P4-6** (§5).
6. **P4-6 est d'abord du backend.** Une UI **minimale** est nécessaire pour qu'un blocage soit « propre ». Le
   démarrage actuel échoue sans message visible, ou avale l'erreur (§4). **Claude Design n'est pas requis.**

```
HEAD                        = ccbeb32 — origin/p4-multi-poste = ccbeb32 (GIT, ls-remote)
CI ccbeb32                  = GREEN SELON LE BRIEF — NON VÉRIFIABLE — RUN ID NON ENREGISTRÉ DANS LE DÉPÔT
TESTS                       = 1953 (Domain 1065 · Application 628 · App 260) — mesure locale P4-5 final verification
P4-5A … P4-5E               = COMMITTED + PUSHED
P4-5F · P4-5G               = NOT STARTED — AUCUN COMMIT
P4-5 EXIT CRITERION         = NOT MET (baseline jamais exécutée sur serveur)
ROADMAP                     = STALE SINCE 440ddcb
P4-6                        = NOT STARTED — PÉRIMÈTRE DOCUMENTÉ = O8 + D-10/11/16/17/18 + U-1/2/4/5 + S7/G7 + D-B4
P4-5G ↔ P4-6                = INTERDÉPENDANCE — FUSION RECOMMANDÉE
POSTGRESQL CLEAN START      = BLOCKED (App.axaml.cs:205)
V1 MULTI-POSTE              = NOT GO
```

---

## 1. Partie 1 — État réel de P4

### 1.1 Tableau des lots

| Lot | Statut roadmap | Statut réel | Preuve |
|---|---|---|---|
| **P4-0** — audit initial | `CLOSE` | **CLOSE** | `GIT` `8cf0919` ; `DOC` CI `30045502517` verte (roadmap §1) |
| **P4-1** — spike providers | `COMPLÈTE ET ENREGISTRÉE` | **CLOSE** | `GIT` `0039e5c`, `f9df67a`, `6f63460`, `e8d0546` ; `DOC` CI `30215445242`, 14 / 14 primitives |
| **P4-2** — ADR provider | `ACCEPTED — CLOSE` | **CLOSE** — PostgreSQL retenu, SQL Server non éliminé | `GIT` `37812c7`, `724ffec` ; `DOC` ADR-002 §4.1 |
| **P4-3** — fondation multi-provider | `CLOSE` | **CLOSE** | `GIT` `b312a6c` ; `DOC` CI `30823399148`, 1529 tests |
| **P4-4** — erreurs et primitives | `IN PROGRESS — NOT CLOSE` | **IN PROGRESS — NOT CLOSE**. O5 fait (A1). O6 satisfaite par construction, en statique seulement (P4-5A §6). **O12**, la re-preuve des 14 primitives, attend P4-5F (corpus N6). O2, O3 et O4 ont été exécutées en P4-5 | `GIT` `b84c7e3`, `dc4c492`, `d5a3656`, `2976401`, `b49f2ff` ; `DOC` P4-5B R-5B-6. CI de `d5a3656` et `2976401` **non enregistrées** |
| **P4-5A** — audit schéma | `COMPLETE — DECISIONS REQUIRED` | **COMPLETED** (documentaire). Décisions prises en P4-5B | `GIT` `c9f37bc` |
| **P4-5B** — décisions | `COMPLETE — ADRs ACCEPTED` | **COMPLETED** — ADR 003 à 008 | `GIT` `9247497` |
| **P4-5C** — portabilité du modèle | **`NOT STARTED`** ❌ | **COMPLETED** — `numeric(12,2)`, filtre d'index par provider | `GIT` `b5c2073` ; `CODE` `ModelPortability.cs` |
| **P4-5D** — stratégie temporelle | **`NOT STARTED`** ❌ | **COMPLETED** — `IClock`, UTC, convertisseur validant, `DateOnly` | `GIT` `023048f` ; rapport final **non suivi** |
| **P4-5D-R** — reprise des dates civiles | `COMPLETED` | **COMPLETED** — **RR4 ouvert** : un `dryRun` sur une copie de base réelle est requis avant tout déploiement | `GIT` `45f67a2`, `440ddcb` |
| **P4-5E** — chaîne de migrations PostgreSQL | **`NOT STARTED`** ❌ | **COMPLETED** — assembly dédiée, baseline `20260922001219_InitialPostgreSqlBaseline`, double contrôle de dérive en CI, `CONTRIBUTING.md` | `GIT` `a06c19f`, `ccbeb32` ; `DOC` vérification finale P4-5 |
| **P4-5F** — tests d'intégration PostgreSQL | `NOT STARTED` | **NOT STARTED**. Aucun projet d'intégration dans `MMV.sln` (8 projets, dont 3 de tests unitaires). Aucun job CI avec serveur | `CODE` `MMV.sln` ; [ci.yml](../../.github/workflows/ci.yml) : un seul job, sans service |
| **P4-5G** — levée du garde-fou | `NOT STARTED` | **NOT STARTED**. Garde-fou actif | `CODE` [App.axaml.cs:205](../../src/MMV.App/App.axaml.cs#L205) ; test `ServerStartupGuardTests` |
| **P4-5** (global) | `IN PROGRESS — NOT CLOSE` | **IN PROGRESS — NOT CLOSE**. Critère de sortie non atteint | `DOC` roadmap §P4-5 « Sortie (GO) » ; P4-5E-B, « Ce que P4-5E ne prouvera pas » |
| **P4-6** — migrations en multi-poste | `NOT STARTED` (§6) ; trajectoire « réévaluable » | **NOT STARTED**. Aucun code, aucun document dédié. Des décisions lui sont déjà affectées (§2.1) | `GIT` aucun commit `P4-6` ; `DOC` ADR-002 O8, ADR-005 G7, ADR-007 S7, P4-5E-B *Deferred Decisions* |
| P4-7 … P4-12 | `NOT STARTED` | **NOT STARTED** | `GIT` aucun commit |

### 1.2 Trajectoire des tests (mesures locales)

| Point | Total | Source |
|---|---|---|
| `2976401` (P4-4B1) | 1569 | rapport P4-5C |
| `b5c2073` (P4-5C) | 1680 | rapport P4-5C |
| `023048f` (P4-5D) | 1758 | rapport P4-5D |
| `45f67a2` (P4-5D-R) | 1915 | rapport P4-5D-R |
| `ccbeb32` (P4-5E) | **1953** | vérification finale P4-5 |

### 1.3 Écarts entre la roadmap et le dépôt

| # | Écart | Conséquence |
|---|---|---|
| E-1 | P4-5C, P4-5D et P4-5E sont en `NOT STARTED` dans la roadmap (l. 600, 601, 603, 817) | la roadmap sous-estime l'état réel |
| E-2 | O2, O3 et O4 sont suivies deux fois (`OPEN` sous P4-4, `DECIDED — NOT IMPLEMENTED` sous P4-5) | voir réconciliation P4-5, I-08 |
| E-3 | Le rapport de clôture P4-5E, prévu à l'étape C9 de P4-5E-B, n'existe pas. La roadmap n'a pas été mise à jour | G6 et S6 ne sont pas formellement fermées ([C9 §7](P4-5E-C9-pre-review.md)) |
| E-4 | Aucun document n'arbitre l'inversion de dépendance P4-4 ↔ P4-5 (R-5B-6, G-2, R-2) | P4-4 ne peut pas être close avant P4-5F |
| E-5 | Les 7 commits intermédiaires (`b49f2ff` … `a06c19f`) n'ont pas de run CI propre : seul le SHA de tête en a un | règle « CI verte sur le SHA exact » non remplie pour ces lots (réconciliation P4-5, D-1) |
| E-6 | [P5-product-completion-roadmap.md](../architecture/P5-product-completion-roadmap.md) (**non suivi**) prévoit après P5 « P6 Sécurité & Gestion utilisateurs », « P7 Backup / Restore / Logs » et « P8 Mise à jour applicative » | recoupe P4-9 (sauvegarde), P4-6 (montée de version) et des sujets non affectés (§3.3). À réconcilier avant de figer P4-6 |

**Recommandation** : appliquer la réconciliation (lot RECON-B) **avant** d'ouvrir P4-6, pour que P4-6 parte
d'une roadmap exacte.

---

## 2. Partie 2 — Objectif de P4-6

### 2.1 Mandat documenté (sources exhaustives)

| Source | Ce qui est affecté à P4-6 |
|---|---|
| Roadmap §P4-6 | **sérialiser** l'application des migrations ; compatibilité version app ↔ schéma. **Autorisé** : verrou, poste désigné, phase de maintenance, garde de version. **Interdit** : migration automatique concurrente. **Tests** : migration pendant qu'un poste est connecté ; client obsolète bloqué proprement |
| ADR-002 **O8** (bloquante) | sérialiser l'application des migrations en multi-poste **et** bloquer proprement un client trop ancien face à un schéma trop récent |
| ADR-005 §5.10 et **G7** | `Migrate()` par poste est inadapté à une base centrale. Le sujet reste entier et appartient à P4-6 |
| ADR-007 §5.7 et **S7** | dérive D-C (base modifiée à la main) traitée au démarrage : une base dont les migrations ne sont pas toutes appliquées, ou dont le schéma est plus récent que l'application, doit être bloquée proprement |
| P4-5E-A et P4-5E-B | **D-10** droits PostgreSQL (partagé avec P4-8) · **D-11** qui applique la baseline au premier démarrage · **D-16** version de la base · **D-17** numérotation de MMV (partagé avec P4-8) · **D-18** montée de version d'une base existante (partagé avec P4-9) · **U-1** schéma plus récent non détecté · **U-2** aucune sérialisation · **U-4** droits DDL au runtime (partagé avec P4-8) · **U-5** fenêtre de versions mixtes (partagé avec P4-10) |
| P4-5D-R, **D-B4** / **RR6** | sauvegarde SQLite non atomique si un autre poste écrit pendant le premier démarrage. D'ici P4-6, la procédure impose tous les postes fermés |
| P4-5E-C9, **Q18** | passages obsolètes d'`ARCHITECTURE.md` (`"AutoMigrate": true`) : lot documentaire proposé **après** la décision P4-6 |

### 2.2 État du code sur le chemin concerné

| Constat | Preuve |
|---|---|
| Le démarrage applique `Migrate()` **par poste** (chemin SQLite) | `CODE` [App.axaml.cs:251](../../src/MMV.App/App.axaml.cs#L251) → [SqliteDatabaseManager.cs:74](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L74), appels `Migrate()` l. 316, 334, 596 |
| Seul `GetPendingMigrations()` est consulté : un schéma **plus récent** que l'application n'est pas détecté (U-1) | `CODE` SqliteDatabaseManager.cs l. 331, 593, 770 |
| **Aucun verrou** de migration, aucun `pg_advisory_lock` | `CODE` `grep` : 0 occurrence |
| **Aucune version applicative** (ni `<Version>` ni `AssemblyVersion`) | `CODE` [Directory.Build.props](../../Directory.Build.props), `.csproj` |
| La branche PostgreSQL désigne déjà la bonne chaîne de migrations. Elle n'exécute rien | `CODE` [DatabaseProviderResolver.cs:133](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L133) |
| Le garde-fou bloque tout démarrage PostgreSQL **avant** le `try` | `CODE` [App.axaml.cs:205-214](../../src/MMV.App/App.axaml.cs#L205-L214) |
| `DatabaseMigrationException` est relancée **sans aucun affichage** (seulement `Debug.WriteLine`) | `CODE` [App.axaml.cs:267](../../src/MMV.App/App.axaml.cs#L267) |
| **Tout autre échec de préparation est avalé** : l'application continue | `CODE` [App.axaml.cs:274](../../src/MMV.App/App.axaml.cs#L274) |

**Contrainte de conception qui en découle** : une garde de version P4-6 ne doit **jamais** passer par le
`catch (Exception)` générique de la l. 274, pour la même raison que le garde-fou P4-3 est placé hors du `try`.

### 2.3 Analyse des thèmes demandés

| Thème | État du code | Lot propriétaire documenté | Dans P4-6 ? |
|---|---|---|---|
| **Multi-utilisateur** | comptes locaux, 3 rôles, login normalisé unique (P3-10). `IsActive` n'est vérifié **qu'au login** ([AuthenticationService.cs:44](../../src/MMV.Infrastructure/Services/AuthenticationService.cs#L44)) | P3-10 reporte l'autorisation par acteur, le dernier administrateur et l'onboarding. **Aucun lot P4** | **Non** |
| **Concurrence PostgreSQL** | 14 primitives à CAS ou update conditionnel, prouvées en spike (P4-1). Aucune re-preuve runtime | **O12** → P4-5F (N6) ; **O13** → P4-10 | **Non**, sauf la concurrence **des migrations** (U-2, U-5) |
| **Transactions** | `EfTransactionRunner` : transaction explicite, rollback sûr, niveau d'isolation **par défaut** (aucun `IsolationLevel`) ([EfTransactionRunner.cs:64](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs#L64)) | O6 : statique fait, runtime → P4-5F (N7). Isolation explicite : « autorisée » en P4-4, **jamais décidée** | **Non**, sauf l'échec partiel de migration (U-6 → P4-5G) |
| **Conflits** | **aucun jeton de concurrence** (`RowVersion`, `IsConcurrencyToken`, `xmin` : 0 occurrence). Le *lost update* est documenté comme dette transverse ([UpdateSupplierUseCase.cs:28](../../src/MMV.Application/UseCases/Suppliers/UpdateSupplier/UpdateSupplierUseCase.cs#L28)) | risk-register **R-09** ; P3-9 🟠-3 ; P3-10 R5. **Aucun lot P4** : `UNKNOWN` | **Non** |
| **Sessions utilisateurs** | `SessionService` singleton **en mémoire, par processus**, expiration après 30 min d'inactivité ([SessionService.cs:20](../../src/MMV.App/Services/SessionService.cs#L20)). Aucune session côté serveur. Une désactivation sur un poste n'affecte pas une session ouverte sur un autre | P3-10 R8 (« invalidation de session — UI »), reportée. **Aucun lot P4** : `UNKNOWN` | **Non** |
| **Droits** | applicatifs : `PermissionService` dans **`MMV.App` seulement**. Aucune garde dans Application ni Domain. Base : aucun modèle de rôles PostgreSQL | applicatifs : P3-10, reportés, **aucun lot P4**. Base : **D-10** (P4-6 / P4-8), **O10** (P4-8), **U-4** | **Oui, pour la seule séparation des rôles DDL / DML** (D-10, U-4) |
| **Synchronisation** | aucun mécanisme : ni polling, ni `LISTEN/NOTIFY`, ni rafraîchissement périodique. Le seul timer est celui de la session | **aucun document** : `UNKNOWN` | **Non** |
| **Erreurs réseau** | `PersistenceErrorMapper` classe `SocketException` et `TimeoutException` d'une chaîne Npgsql en erreur de connexion ([PersistenceErrorMapper.cs:249](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs#L249)). **Aucun retry**, par décision : O13 interdit tout retry sans clé d'idempotence | politique de démarrage **D-15** → P4-5G / P4-8 ; tests → P4-10 | **Non**, sauf un serveur indisponible **pendant une migration** |
| **Audit métier** | traces partielles : `StockMovement.PerformedByUserId`, `Sale.StaffId`, `User.LastLogin`. Aucun journal d'audit | P3-10 reporte l'« audit trail complet » ; P3-11 reporte l'auteur du QC au redesign UI. **Aucun lot P4** : `UNKNOWN` | **Non**. Seule la **journalisation des migrations** est dans le périmètre (`MigrationJournal` existe pour SQLite) |

### 2.4 Définition proposée de P4-6

> **Objectif.** Garantir qu'une base PostgreSQL centrale n'est migrée **que par un acteur autorisé, un seul à
> la fois**, et qu'**aucun poste ne travaille sur un schéma qui ne correspond pas à sa version**.

**Dans le périmètre**

1. **Qui migre** (D-11, D-18) : premier poste sous verrou, poste désigné ou outil distinct.
2. **Sérialisation** (U-2) : un mécanisme qui empêche deux applications concurrentes du DDL. EF Core 8 n'a
   pas de verrou natif (P4-5E-A, `EF_BEHAVIOR — À CONFIRMER`).
3. **Garde de version au démarrage** (U-1, S7, D-16), pour les deux chaînes :
   - migrations appliquées ⊆ migrations connues, sinon blocage ;
   - migrations en attente et poste non autorisé à migrer : blocage.
4. **Version de MMV** (D-17) : visible et journalisée. C'est un prérequis de D-16 si l'option « table de
   compatibilité » est retenue.
5. **Séparation des rôles PostgreSQL** (D-10, U-4) : un rôle migrateur (DDL) et un rôle applicatif (DML), ou
   un rôle unique justifié. La mise en place du provisioning reste en P4-8.
6. **Fenêtre de maintenance** (U-5) : comportement des postes connectés pendant une migration.
7. **D-B4 / RR6** : décider si la règle « tous postes fermés » devient une garde technique ou reste une
   procédure.

**Hors périmètre** : création de la base et provisioning (D-09, P4-8), secrets et TLS (D-13, D-14, P4-8),
sauvegarde avant migration et retour arrière (U-3, U-7, P4-9), import SQLite (P4-7), tests de résilience
généraux (P4-10), ainsi que tous les thèmes marqués **Non** au §2.3.

**Critères de sortie proposés** (dérivés de la roadmap et d'O8)

| # | Critère | Preuve |
|---|---|---|
| S-1 | deux postes démarrant en même temps sur une base en retard : **une seule** application du DDL | test d'intégration PostgreSQL |
| S-2 | client plus ancien que le schéma : **bloqué**, avec un message clair, sans écriture | test d'intégration PostgreSQL |
| S-3 | migration pendant qu'un poste est connecté : comportement **conforme** à la décision U-5 | test d'intégration PostgreSQL |
| S-4 | poste non autorisé face à des migrations en attente : **bloqué**, sans migrer | test d'intégration PostgreSQL |
| S-5 | chemin SQLite **sans régression**. Les 14 migrations restent inchangées (G4) | suite existante + contrôle de dérive |
| S-6 | aucune garde de version n'est avalée par le `catch` générique | test d'architecture ou de démarrage |
| S-7 | Domain et Application restent provider-neutres (O14) | `ProviderNeutralityArchitectureTests` |

S-1 à S-4 exigent un **serveur PostgreSQL en CI** : **P4-6 dépend de l'infrastructure de P4-5F** (ADR-008).

### 2.5 Décisions à trancher avant tout code P4-6

| # | Décision | Options documentées | Source |
|---|---|---|---|
| **D-11** | qui applique la baseline au premier démarrage | (a) premier poste sous verrou · (b) poste désigné · (c) outil distinct | P4-5E-A |
| **D-18** | montée de version d'une base existante | (a) `Migrate()` sous verrou · (b) outil explicite (bundle ou script idempotent) en fenêtre de maintenance · (c) (b) + sauvegarde obligatoire | P4-5E-A |
| **D-16** | version de la base, et réaction à un schéma plus récent | (a) `__EFMigrationsHistory` seul · (b) table de compatibilité · (c) (a) + discipline étendre / contracter | P4-5E-A |
| **D-17** | numérotation de MMV | version sémantique dans `Directory.Build.props` | P4-5E-A |
| **D-10** | rôles PostgreSQL | (a) rôle propriétaire unique · (b) migrateur + applicatif | P4-5E-A |
| **A-1** | **fusion de P4-5G dans P4-6**, ou ordre strict P4-6A → P4-5G | voir §5 | ce rapport |
| **A-2** | arbitrage de l'inversion P4-4 ↔ P4-5 (R-5B-6) : P4-4 est close à P4-5F vert, ou reste ouverte jusqu'à P4-10 | — | P4-5B |
| **A-3** | EF Core 8 et verrou de migration : implémenter un verrou, ou choisir D-11 (c) et D-18 (b), qui rendent le verrou inutile côté application | — | P4-5E-A U-2 |

Choisir **D-11 (c)** et **D-18 (b)**, un outil distinct, retire tout DDL de l'application. Cela résout U-2 et
U-4 **par construction**, et réduit P4-6 à une garde de version en lecture seule côté poste. C'est la voie la
plus simple à prouver. **La décision appartient à l'architecte** : ce rapport ne la tranche pas.

---

## 3. Partie 3 — Dépendances V1

> **Ambiguïté de vocabulaire, à lever.** « V1 » désigne trois jalons distincts dans les documents :
> `V1 MULTI-POSTE = GO` (P4-12, roadmap P4 §4), « V1 Beta — Professional Testing Ready » (sortie de P5) et
> « P10 — Release V1 » (P5, **non suivi**). Les tableaux ci-dessous visent **`V1 MULTI-POSTE = GO`**, seule
> définition vérifiable, avec 18 critères.

### 3.1 Obligatoire avant V1 multi-poste

| Élément | Lot | Source (critère §4 / obligation) |
|---|---|---|
| migrations PostgreSQL reproductibles, base neuve migrée **deux fois à l'identique** (G5, N1, N2, S3 à S5) | P4-5F | critère 4 ; ADR-005 G5 |
| **re-preuve des 14 primitives** (N6) | P4-5F → clôt P4-4 | critère 5 ; **O12** |
| fidélité monétaire, `DateTime` et index sur serveur (M4 à M6, T7, X5, X6) ; rollback et `25P02` (N7) | P4-5F | O2, O3, O4, O6 |
| classification des erreurs testée sur serveur | P4-5F | critère 6 ; O5 |
| préparation serveur et **levée du garde-fou** | P4-5G (ou P4-6, §5) | critère 3 |
| **sérialisation des migrations + garde de version** | **P4-6** | **O8** |
| import SQLite → PostgreSQL, **ou** procédure d'échec sûre (M7, T8) | P4-7 | critère 7 ; **O11** |
| **RR4** : `dryRun` sur une copie de base réelle | avant tout déploiement | P4-5D-R |
| secrets hors dépôt, échec clair sans configuration, rôles réels (D-09, D-12 à D-15), NTP (T9) | P4-8 | critère 14 ; **O10** |
| sauvegarde planifiée, rétention, restauration testée, documentation opérateur | P4-9 | critères 11, 12, 13 ; **O9** |
| tests multi-processus, perte réseau, reconnexion | P4-10 | critères 8, 9, 10 ; **O13** |
| recette sur plusieurs postes | P4-11 | critère 18 |
| provider-neutralité, build et tests verts, aucune vulnérabilité | transverse | critères 15, 16, 17 ; **O14** |
| roadmap réconciliée (RECON-B) | gouvernance | prérequis de l'audit final P4-12 |

### 3.2 Peut attendre V2 / V3 (explicitement documenté)

| Élément | Source |
|---|---|
| SQL Server Express (rejeté pour V1, non éliminé) ; son dialecte reste conservé | ADR-002 §19 ; O15 non bloquante |
| SaaS, multi-tenant, multi-magasin, cloud, API publique, application web | roadmap P4 §3 |
| redesign UI global, rendez-vous, dashboard fonctionnel, recherche globale, exports | roadmap P4 §3 (rattachés à P5) |
| `IModelCacheKeyFactory` personnalisé | P4-5E-B D-07 (réexamen conditionnel seulement) |
| schéma PostgreSQL dédié | P4-5E-B D-08 (réexamen conditionnel) |
| nettoyage d'`AddInfrastructure` inerte (C-4, R-E17) | P4-5E-B, « ultérieur » |
| MFA, SSO / OAuth / OIDC, récupération par e-mail, rotation des mots de passe | P3-10 §34, reportés sans échéance ; **aucun document ne les exige pour V1** |
| nettoyage documentaire Q18 / Q19 (`ARCHITECTURE.md`, `README.md`) | P4-5E-C9 : **après** la décision P4-6, non bloquant |

### 3.3 Statut V1 `UNKNOWN` — décision de l'architecte requise

Ces sujets **deviennent réels en multi-poste**, mais **aucun document** ne les affecte à un lot P4 ni ne les
classe V1 ou V2. Ce rapport ne les classe pas.

| Sujet | Pourquoi le multi-poste le rend concret | Trace existante |
|---|---|---|
| **Lost update** sur les éditions ordinaires (clients, produits, fournisseurs, utilisateurs) | deux postes éditent la même fiche : le dernier écrase l'autre, sans avertissement | R-09 ; P3-9 🟠-3 ; P3-10 R5 |
| **Invalidation de session** après désactivation ou changement de rôle | un compte désactivé sur le poste A reste actif sur le poste B jusqu'à 30 min d'inactivité ou la déconnexion | P3-10 R8 |
| **Autorisation par acteur** dans Application | les droits ne sont vérifiés que par l'UI | P3-10 §34 |
| **Fraîcheur des données entre postes** | un poste affiche un état que l'autre a modifié. Les primitives CAS protègent l'écriture, pas l'affichage | aucune |
| **Audit trail métier** | plusieurs opérateurs simultanés : qui a fait quoi | P3-10 §34 ; P3-11 item 9 |
| **Niveau d'isolation explicite** | « autorisé » en P4-4, jamais décidé ; défaut du provider | roadmap §P4-4 |
| **P5 « P6 Sécurité », « P7 Backup », « P8 Mise à jour »** | recoupent P4-9 et P4-6 ; document non suivi | P5 roadmap §15 |

**Recommandation** : trancher ces sept points **dans P4-6A** (décision documentaire), sans les implémenter en
P4-6, pour qu'aucun ne disparaisse entre P4 et P5.

---

## 4. Partie 4 — Relation avec l'UI

| Zone | P4-6 doit-elle la toucher ? | Justification |
|---|---|---|
| **Backend** (Infrastructure, composition root de `MMV.App`) | **Oui — l'essentiel** | garde de version, sérialisation et rôles vivent dans Infrastructure ; le branchement se fait dans `App.axaml.cs`, comme le garde-fou P4-3 et `PrepareDatabase`. Domain et Application restent intacts (O14) |
| **UI** (`MMV.App/Views`, ViewModels) | **Oui — minimal, sur décision explicite** | un blocage n'est « propre » que si l'utilisateur voit pourquoi. Aujourd'hui, `DatabaseMigrationException` termine le processus sans message ([App.axaml.cs:267](../../src/MMV.App/App.axaml.cs#L267)), et les autres échecs sont avalés (l. 274). La roadmap P4 §3 autorise une UI minimale **uniquement** pour la configuration de connexion, l'indisponibilité serveur et une opération de migration décidée. Trois états suffisent : client obsolète, maintenance en cours, poste non autorisé à migrer |
| **Claude Design** | **Non** | la roadmap P4 §3 exclut le redesign UI global. Claude Design est une **entrée de P5** (P5-0, P5-1), document non suivi. Un écran de blocage peut réutiliser le mécanisme existant (fenêtre simple, comme la fenêtre de login, ou `DialogService`) et sera restylé en P5. Qu'un écran d'erreur ou de maintenance existe dans les livrables Claude Design est **`UNKNOWN`** : ces livrables ne sont pas dans le dépôt |

**Risque à noter** : si le choix D-11 (c) / D-18 (b), un outil distinct, est retenu, l'**outil d'administration**
lui-même est une interface. Sa forme (console, script ou fenêtre) est à décider en P4-6A. Une console suffit en
V1 et n'appelle aucun design.

---

## 5. Partie 5 — Proposition de découpage

### 5.1 Constat qui motive le découpage

- **P4-5G a besoin de P4-6** : D-10, D-11 et D-15 doivent être tranchées « avant P4-5G » (P4-5E-B). Lever le
  garde-fou sans O8 revient à laisser chaque poste migrer librement.
- **P4-6 a besoin de P4-5F** : ses critères S-1 à S-4 exigent un serveur PostgreSQL en CI (ADR-008).
- **Les décisions P4-6 n'ont besoin de rien** : elles peuvent être prises dès maintenant, en parallèle de
  P4-5F.

### 5.2 Découpage proposé

| Lot | Nature | Contenu | Dépend de | Porte de sortie |
|---|---|---|---|---|
| **P4-6A** — Décisions | **documentaire**, aucun code | ADR-PROD-DB-009 « Application des migrations et compatibilité de version » : D-10, D-11, D-16, D-17, D-18, U-5, D-B4. Arbitrages A-1, A-2 et A-3. Classement V1 des sept points du §3.3. Forme de l'UI minimale | réconciliation de la roadmap (RECON-B) | ADR acceptée + commit + CI |
| **P4-6B** — Mécanisme | code Infrastructure, **garde-fou toujours actif** | version de MMV ; garde de version pour les deux chaînes ; mécanisme de sérialisation, ou outil distinct, selon D-11 et D-18 ; séparation des rôles selon D-10 ; journalisation des migrations serveur ; tests unitaires et d'intégration S-1, S-2, S-4, S-5 et S-7 | P4-6A, **P4-5F** | CI verte, job PostgreSQL inclus |
| **P4-6C** — Activation | code App + UI minimale | **absorbe P4-5G** : gestionnaire de préparation serveur, **levée du garde-fou** ([App.axaml.cs:205](../../src/MMV.App/App.axaml.cs#L205)), sortie du `catch` générique (S-6), écran de blocage minimal, test S-3 (migration pendant qu'un poste est connecté). Qualification de `EnsureCreated()` du seed de démonstration (C-1) | P4-6B ; décisions P4-8 **D-09** et **D-15** | CI verte + preuve manuelle sur le laboratoire `MMV-SRV` / `MMV-CLI` (P4-1 Lot C) |

**Ordre global recommandé**

```
RECON-B (roadmap) ─┬─► P4-6A (décisions) ─────────────┐
                   └─► P4-5F (tests d'intégration PG) ─┴─► P4-6B ─► P4-6C (= P4-5G + activation) ─► P4-7 / P4-8 / P4-9 / P4-10
```

### 5.3 Pourquoi trois lots, et pas moins

- **A et B ne fusionnent pas** : A se fait sans serveur et dès maintenant ; B attend P4-5F. Les fusionner
  bloquerait les décisions derrière l'infrastructure de test.
- **B et C ne fusionnent pas** : C lève le garde-fou, seul changement de P4 qui rend PostgreSQL utilisable en
  production. La roadmap exige qu'il n'intervienne qu'après une preuve verte (« uniquement après P4-5F
  vert »). Le séparer de B garde un point d'arrêt net.
- **Pas de lot supplémentaire** : l'outil d'administration éventuel, la journalisation et la version de MMV
  appartiennent à B ; l'UI minimale appartient à C.

**Alternative, si l'architecte refuse la fusion A-1** : garder P4-5G distinct et l'exécuter **entre** P4-6B et
P4-6C. P4-6C se réduit alors à l'UI minimale et au test S-3. C'est une micro-phase : cette alternative n'est
pas recommandée.

---

## 6. Conclusion

```
P4-6 TRANSITION AUDIT        = COMPLETE — PRE-REVIEW ONLY
HEAD AUDITED                 = ccbeb3259444d26264e20323058764dd59318e1c (= origin/p4-multi-poste)
P4-0 … P4-3                  = CLOSE
P4-4                         = IN PROGRESS — O12 BLOCKED BY P4-5F — R-5B-6 NOT ARBITRATED
P4-5                         = IN PROGRESS — A…E DONE — F, G NOT STARTED — EXIT CRITERION NOT MET
ROADMAP                      = STALE (P4-5C/D/E SHOWN NOT STARTED)
P4-6 DOCUMENTED SCOPE        = O8 — SERIALIZED MIGRATIONS + VERSION GUARD (D-10/11/16/17/18, U-1/2/4/5, S7, G7, D-B4)
P4-6 OUT OF SCOPE            = SESSIONS · APP RIGHTS · SYNC · EDIT CONFLICTS · BUSINESS AUDIT · NETWORK RESILIENCE
UNASSIGNED MULTI-POSTE GAPS  = 7 (§3.3) — V1 STATUS UNKNOWN — ARCHITECT DECISION REQUIRED
P4-6 UI                      = BACKEND FIRST — MINIMAL BLOCKING UI — NO CLAUDE DESIGN
PROPOSED SPLIT               = P4-6A DECISIONS · P4-6B MECHANISM · P4-6C ACTIVATION (ABSORBS P4-5G)
PREREQUISITES                = RECON-B · P4-5F (FOR 6B) · P4-8 D-09/D-15 (FOR 6C)
CODE / ROADMAP / ADR CHANGED = NONE
COMMIT / PUSH                = NONE — THIS REPORT IS UNCOMMITTED
V1 MULTI-POSTE               = NOT GO
```
