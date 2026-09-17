# MMV Project Recovery Audit

**Date :** 2026-09-17

**Branche :** `p4-multi-poste`

**Commit (HEAD) :** `297640185fb47aa163a792913401c94c6d556ad2` — `fix(P4-4B1): make UnitOfWork rollback cancellation-safe` (2026-09-06)

**Arbre de travail :** propre (`git status --porcelain` = vide, y compris `--untracked-files=all`)

**Nature de cet audit :** lecture seule. Aucun code modifié, aucun commit, aucune branche, aucun
correctif. Toute information absente du dépôt est notée `NOT FOUND IN REPOSITORY`.

---

# Executive Summary

1. **Le projet est sain et cohérent.** L'arbre est propre, aucun travail local n'est en attente,
   l'historique est linéaire et intégralement documenté lot par lot.

2. **P4 est avancée jusqu'au milieu de P4-4.** P4-0, P4-1, P4-2 et P4-3 sont closes. **P4-4 est
   COMMENCÉE et partiellement livrée** : quatre sous-lots (`P4-4A0`, `P4-4A1`, `P4-4B0`, `P4-4B1`)
   sont **implémentés, documentés et commités**. P4-5 à P4-12 ne sont pas commencées.

3. **Écart documentaire n° 1 (le plus important).** La roadmap officielle
   [P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) déclare toujours
   **`P4-4 = READY — NOT STARTED`** et « aucune implémentation engagée » (§6). C'est **faux au regard
   du dépôt** : 4 commits de P4-4 existent, dont 2 modifient du code de production. Les rapports de
   lot le disent explicitement (« La roadmap n'a pas été modifiée par ce lot »), mais la **source
   officielle d'état n'a jamais été réconciliée**. C'est la première chose à corriger avant toute
   reprise de développement.

4. **Découpage `A0/A1/B0/B1` non gouverné.** Le découpage de P4-4 en sous-lots n'apparaît **dans aucune
   roadmap ni ADR** — uniquement dans les rapports d'implémentation et les messages de commit. La
   roadmap ne connaît que « P4-4 » comme bloc unique.

5. **Obligation O5 satisfaite, O2/O3/O4 toujours ouvertes.** `PersistenceErrorMapper` porte désormais
   une classification PostgreSQL (O5, critère de sortie n° 6). En revanche le **mapping monétaire
   `REAL`** (O2), le **filtre d'index `"IsCurrent" = 1`** (O3) et le **mapping `DateTime`** (O4) sont
   **vérifiés présents et inchangés dans le code de production**.

6. **Inversion de dépendance détectée entre P4-4 et P4-5.** Le critère de sortie de P4-4 exige la
   **re-preuve des 14 primitives sur PostgreSQL**. Or le rapport P4-4B0 §15 établit que cette re-preuve
   est **bloquée par l'absence de schéma/migrations PostgreSQL**, qui est le périmètre de **P4-5**.
   **P4-4 ne peut donc pas être close avant P4-5.** Ce point demande une décision d'architecte
   (question ouverte Q1).

7. **Le multi-poste n'est pas fonctionnel, et c'est volontaire.** Le provider PostgreSQL est
   sélectionnable par configuration, mais le **démarrage applicatif sur PostgreSQL est bloqué
   explicitement** dans le composition root. Aucune migration serveur, aucun déploiement, aucun outil
   d'import. `V1 MULTI-POSTE = NOT GO`.

8. **La suite de tests n'a pas pu être exécutée dans cet environnement** (NuGet hors ligne, voir
   *Tests Status*). Les comptages cités sont donc **statiques ou documentaires**, jamais présentés
   comme mesurés ici.

---

# Git Analysis

## Contexte

| Élément | Valeur |
|---|---|
| Branche courante | `p4-multi-poste` |
| Branche principale | `main` |
| HEAD | `297640185fb47aa163a792913401c94c6d556ad2` |
| Date HEAD | 2026-09-06 |
| Travaux locaux non commités | **aucun** — arbre propre |
| Fichiers modifiés | **aucun** |
| Fichiers non suivis | **aucun** |
| Dernier commit officiel | `2976401` (`fix(P4-4B1)`) |

> Note : les rapports P4-4A1/4B1 mentionnaient des répertoires non suivis `design-handoff/`, `design/`
> et `docs/ui/`. **Ces répertoires n'existent plus dans l'arbre de travail** à la date de cet audit.

## Historique depuis P4-0

| Commit | Message | Phase | Statut |
|---|---|---|---|
| `8cf0919` | `chore(P4-0): establish multi-poste audit and CI` | P4-0 | **CLOSE** — doc + CI `30045502517` |
| `0039e5c` | `test(P4-1): capture provider spike evidence` | P4-1 A/B | ENREGISTRÉ — 32 fichiers de spike |
| `f9df67a` | `docs(P4-1): record technical spike CI` | P4-1 B | ENREGISTRÉ |
| `6f63460` | `docs(P4-1): close Windows network Lot C` | P4-1 C | **PASS** |
| `e8d0546` | `fix(P4-1): complete active LowStock portability` | P4-1 D | **PASS** — CI `30215445242`, 14/14 primitives |
| `37812c7` | `docs(P4-2): propose production server database provider` | P4-2 | proposition ADR |
| `724ffec` | `docs(P4-2): accept production server database provider` | P4-2 | **ACCEPTED — CLOSE** |
| `b312a6c` | `feat(P4-3): add multi-provider infrastructure foundation` | P4-3 | implémentation |
| `669e48c` | `docs(P4-3): close multi-provider infrastructure foundation` | P4-3 | **CLOSE** — CI `30823399148` |
| `b84c7e3` | `test(P4-4A0): record PostgreSQL exception shape evidence` | P4-4A0 | **RECORDED** — CI `33411724068` |
| `dc4c492` | `feat(P4-4A1): add PostgreSQL persistence error mapping` | P4-4A1 | **RECORDED** — CI `33436908075` |
| `d5a3656` | `test(P4-4B0): record PostgreSQL transaction behavior evidence` | P4-4B0 | **RECORDED** — CI `NOT FOUND IN REPOSITORY` |
| `2976401` | `fix(P4-4B1): make UnitOfWork rollback cancellation-safe` | P4-4B1 | **COMMITÉ** — CI `NOT FOUND IN REPOSITORY` |

## Constats Git

- **13 commits P4**, du 2026-07-23 au 2026-09-06.
- **Les numéros de CI de `P4-4B0` et `P4-4B1` ne figurent nulle part dans le dépôt.** Le rapport 4B1
  est même encore intitulé *« IMPLEMENTED LOCALLY — PENDING HUMAN REVIEW / COMMIT / CI »* alors que le
  commit existe. **Impossible de vérifier ici** : `gh` n'est pas installé et le réseau est indisponible.
  La verdeur de la CI sur `2976401` est donc **NON VÉRIFIÉE**.
- Seuls **2 des 4 commits P4-4 touchent du code de production** : `dc4c492`
  (`PersistenceErrorMapper`) et `2976401` (`UnitOfWork`, 5 lignes).

---

# Documentation Analysis

## Volumétrie

| Répertoire | Fichiers | Remarque |
|---|---|---|
| `docs/architecture/` | 18 | roadmaps P2C→P4, 8 ADR, cartes de dépendances, registre de risques |
| `docs/implementation/` | 74 | un rapport par lot, de P2A à P4-4B1 |
| `docs/domain/` | 9 | domaine optique, multi-pays, registre réglementaire |
| `docs/security/` | 1 | `P2C-SEC-1-sqlite-cve.md` uniquement |
| `docs/roadmap/` | 1 | `MMV-master-professionalization-roadmap.md` |
| `docs/archive/` | 2 | cartographie phase 0 |
| `docs/prompts/` | 1 | exécuteur de phase contrôlé |
| racine `docs/` | 12 | notes de sprint historiques (SPRINT4→8, login, bindings) |
| `docs/reports/` | 1 | **ce document** (répertoire créé par cet audit) |

## Décisions figées (non négociables)

| # | Décision | Source |
|---|---|---|
| D1 | **La production V1 est multi-poste** — ce n'est pas une option future | ADR-PROD-DB-001 §1 |
| D2 | **PostgreSQL est le SGBD serveur retenu pour la V1** | ADR-PROD-DB-002 (`ACCEPTÉE`) |
| D3 | **SQLite sur dossier réseau est INTERDIT** en production multi-poste | ADR-PROD-DB-001 |
| D4 | **SQLite local reste supporté** pour dev / test / démo / mono-poste, et n'est pas retiré | roadmap P4 |
| D5 | **SQL Server Express n'est pas éliminé** — rejeté pour V1, réexaminable (ADR §20) | ADR-PROD-DB-002 |
| D6 | **P4 n'est ni SaaS ni multi-tenant** : un magasin, une base centrale, plusieurs postes | roadmap P4 |
| D7 | **Domain et Application doivent rester provider-neutres** (O14, critère 17) | ADR-PROD-DB-002 §15 |
| D8 | **Interdit de modifier `PersistenceException` / `PersistenceErrorCategory`** (Domain neutres) | ADR-PROD-DB-002 O5 |
| D9 | **Les 14 migrations SQLite historiques doivent rester utilisables** | roadmap P4-5 |
| D10 | **Aucun secret dans le dépôt** ; chaîne de connexion serveur par variable d'environnement | ADR-PROD-DB-002, critère 14 |
| D11 | **Aucune décision de classification d'erreur par le TEXTE** d'un message | rapport P4-4A1 §4 |
| D12 | **Aucun retry introduit** tant qu'il n'est pas prouvé idempotent | roadmap P4-4, rapport 4B0 §17 |
| D13 | **Interdit de déclarer une primitive portée sans son test serveur** | roadmap P4-4 |
| D14 | **18 critères de sortie** conditionnent `V1 MULTI-POSTE = GO` | roadmap P4 §4 |

## Décisions ouvertes

| # | Sujet | Affectation ADR | État réel dans le code |
|---|---|---|---|
| **O2** | Mapping monétaire exact en remplacement de `HasColumnType("REAL")` — type et précision **non choisis** | P4-5 (+ P4-7) | **OUVERT** — `REAL` présent dans `SaleConfiguration.cs` (5 colonnes) et `ProductConfiguration.cs` (3 colonnes) |
| **O3** | Réécriture du filtre d'index booléen `"IsCurrent" = 1` en forme PostgreSQL | P4-5 | **OUVERT** — présent dans `WorkshopSheetConfiguration.cs:62` + snapshot + 3 migrations |
| **O4** | Mapping `DateTime` : PostgreSQL **refuse** `Kind = Local` par le chemin EF. Choix entre conversion de valeur (UTC + horloge injectable) et type de colonne — **non tranché** | **P4-4 et/ou P4-5** | **OUVERT** — aucune conversion, aucune horloge injectable, aucune assertion |
| **O5** | Classification d'erreurs PostgreSQL | P4-4 | **TRAITÉ** par `P4-4A1` (`dc4c492`) |
| **O7** | Chaîne de migrations serveur | P4-5 | **NOT STARTED** |
| — | Forme de la chaîne de migrations serveur (projet distinct ? contexte distinct ?) | « suit P4-2 » | **NON TRANCHÉ** — roadmap P4-5 : « forme non figée » |
| — | Découpage de P4-5 à P4-12 | « réévaluable après P4-2 » | **NON RÉÉVALUÉ** |
| — | Niveau d'isolation explicite, politique de timeout | P4-4 « autorisé » | **NON DÉCIDÉ** — 4B0 §14.3 conclut qu'aucun n'est nécessaire *aujourd'hui* |

## Prochaine ADR nécessaire

`NOT FOUND IN REPOSITORY` — aucune ADR n'est nommément annoncée comme prochaine. `adr-candidates.md`
référence toutefois **ADR-005** (horloge injectable / UTC), que l'ADR-PROD-DB-002 O4 désigne comme une
des deux options pour trancher `DateTime`. C'est le candidat naturel si O4 est traité en P4-4.

---

# P4 Current Status

## P4 STATUS REPORT

| Lot | Objectif | Statut | Preuve |
|---|---|---|---|
| **P4-0** | Audit initial multi-poste + roadmap + CI | **CLOSE** | `8cf0919` · CI `30045502517` · [audit](../implementation/P4-0-multi-poste-initial-audit-report.md) (805 l.) |
| **P4-1 A/B** | Spike comparatif providers (modèle, monétaire, index, upsert, transactions) | **GO** | `0039e5c`, `f9df67a` · 32 fichiers `spikes/` |
| **P4-1 C** | Installation Windows native + réseau réel (2 candidats) | **PASS** | `6f63460` · [rapport](../implementation/P4-1-windows-network-lot-c-report.md) (1016 l.) |
| **P4-1 D** | Portabilité de `TryCreateActiveLowStockAsync` — 14/14 primitives | **PASS** | `e8d0546` · CI `30215445242` · 1505 tests |
| **P4-2** | ADR de choix du provider serveur | **ACCEPTED — CLOSE** | `37812c7` + `724ffec` · [ADR-PROD-DB-002](../architecture/adr-prod-db-002-server-database-provider-selection.md) |
| **P4-3** | Fondation Infrastructure multi-provider | **CLOSE** | `b312a6c` + `669e48c` · CI `30823399148` · 1529 tests |
| **P4-4A0** | Mesure de la forme des exceptions Npgsql (E19/E20) | **RECORDED** | `b84c7e3` · CI `33411724068` · [rapport](../implementation/P4-4A0-npgsql-exception-shape-report.md) (716 l.) |
| **P4-4A1** | `PersistenceErrorMapper` PostgreSQL (O5) | **RECORDED** | `dc4c492` · CI `33436908075` · +36 tests · 1529 → 1565 |
| **P4-4B0** | Mesure du comportement transactionnel PostgreSQL (E21) | **RECORDED** | `d5a3656` · [rapport](../implementation/P4-4B0-postgresql-transaction-behavior-report.md) (557 l.) · **CI non enregistrée** |
| **P4-4B1** | Rollback défensif `UnitOfWork.CommitAsync` | **COMMITÉ — CI NON ENREGISTRÉE** | `2976401` · +4 tests · 1565 → 1569 |
| **P4-4** *(bloc)* | Traduction des erreurs + portage des 14 primitives | **IN PROGRESS — NOT CLOSE** | roadmap dit encore `READY — NOT STARTED` ⚠️ |
| **P4-5** | Schéma et migrations serveur | **NOT STARTED** | aucun fichier, aucun commit |
| **P4-6** | Application sérialisée des migrations multi-poste | **NOT STARTED** | — |
| **P4-7** | Outil de migration SQLite → serveur | **NOT STARTED** | — |
| **P4-8** | Configuration, secrets, déploiement de la base centrale | **NOT STARTED** | — |
| **P4-9** | Sauvegarde / restauration centrales | **NOT STARTED** | — |
| **P4-10** | Tests multi-processus et résilience | **NOT STARTED** | — |
| **P4-11** | Recette plusieurs postes | **NOT STARTED** | — |
| **P4-12** | Audit final P4 | **NOT STARTED** | — |

## Détail par lot : documentation / code / commit / CI / tests

| Lot | Doc | Code | Commit | CI | Tests |
|---|---|---|---|---|---|
| P4-0 | ✅ roadmap + audit | ✅ workflow CI | ✅ | ✅ `30045502517` | héritage P3 |
| P4-1 | ✅ 4 rapports | ✅ spikes + `NotificationRepository` | ✅ | ✅ `30215445242` | ✅ 1505 |
| P4-2 | ✅ ADR | ➖ documentaire | ✅ | ➖ | ➖ |
| P4-3 | ✅ rapport | ✅ 5 fichiers Configuration + DI + csproj | ✅ | ✅ `30823399148` | ✅ 1529 |
| P4-4A0 | ✅ rapport | ➖ sondes hors `MMV.sln` | ✅ | ✅ `33411724068` | ✅ 10 (sonde) |
| P4-4A1 | ✅ rapport | ✅ `PersistenceErrorMapper` | ✅ | ✅ `33436908075` | ✅ +36 |
| P4-4B0 | ✅ rapport | ➖ spike E21 hors `MMV.sln` | ✅ | ❌ **non enregistrée** | ➖ hors CI |
| P4-4B1 | ✅ rapport | ✅ `UnitOfWork` (5 l.) | ✅ | ❌ **non enregistrée** | ✅ +4 |
| P4-5→P4-12 | ➖ roadmap seule | ❌ | ❌ | ❌ | ❌ |

## Écart critique à corriger

La roadmap §6 affirme, sur un dépôt qui la contredit :

> `P4-4 = READY — NOT STARTED` … « **prochaine étape officielle, non commencée, aucune implémentation
> engagée** »

**Réalité mesurée :** 4 sous-lots livrés, 2 fichiers de production modifiés, +40 tests, 2 CI vertes
enregistrées sur 4 commits. La roadmap est la **source officielle d'état** du projet : elle est
actuellement **fausse**.

---

# Architecture Status

## Structure

Architecture en couches respectée, 4 projets de production + 3 projets de tests dans `MMV.sln`,
plus un harness de spike **hors solution** (`spikes/P4.ProviderComparison`).

```
MMV.Domain          → aucune dépendance de projet
MMV.Application     → MMV.Domain
MMV.Infrastructure  → MMV.Domain  (+ EF Sqlite, Npgsql, BCrypt)
MMV.App             → Application + Infrastructure + Domain (Avalonia)
```

## Domain

| Élément | État |
|---|---|
| Entités | **22** (`Customer`, `Product`, `Prescription`, `Order`, `OrderItem`, `Sale`, `SaleItem`, `StockMovement`, `Supplier`, `User`, `Notification`, `WorkshopSheet`, `WorkshopSheetItem`, `DocumentSequence`, détails optiques…) |
| Règles métier | Répertoires dédiés `Policies/`, `Validators/`, `Services/`, `Optics/`, `ValueObjects/` |
| Ports persistance | `Interfaces/Persistence/` + `Interfaces/Repositories/` |
| Exceptions | `PersistenceException` / `PersistenceErrorCategory` — **neutres, non modifiées** (D8 respectée) |
| Dépendances | **aucune** dépendance de projet → couche pure |

Politiques métier centralisées vérifiées : `OrderStatusPolicy` (matrice de transitions unique),
`SalePricingPolicy` (formule monétaire unique), `UserIdentityPolicy` (normalisation des logins). Les
services doublons (`PrescriptionService`, `OrderService`, `SaleService`) ont été **supprimés** en P3
pour éliminer les seconds chemins d'écriture — documenté dans `DependencyInjection.cs`.

## Application

| Élément | État |
|---|---|
| Use cases | **48 répertoires** sous `UseCases/` (Customers, Prescriptions, Products, Orders, Sales, Stock, Suppliers, Users, Notifications, WorkshopSheets) |
| Abstractions | `Abstractions/` + `Common/` (dont `ValidationResult` standardisé en P3-1) |
| Neutralité provider | **verrouillée par test** — `ProviderNeutralityArchitectureTests.cs` (3 tests, ajouté par P4-4A1, O14) |

## Infrastructure

| Élément | État |
|---|---|
| `DbContext` | `OpticDbContext` + `OpticDbContextFactory` (design-time) |
| Configurations EF | **21** classes `IEntityTypeConfiguration` |
| Repositories | **11** + `BaseRepository` + `UnitOfWork` |
| Primitives de persistance | `EfTransactionRunner`, `EfStockMutationService`, `EfNumberSequenceService`, `PersistenceErrorMapper` |
| Cycle de vie SQLite | `SqliteDatabaseManager`, `SqliteSchemaVerifier`, `LegacyDatabaseRecoveryService`, `MigrationJournal`, `SqliteDateTimeDefaultVerifier` — **entièrement mono-poste et SQLite-only** |
| Sélection de provider | `DatabaseProviderResolver`, `DatabaseProvider` (enum fermé : `Sqlite`, `PostgreSql`), `DatabaseProviderOptions` |
| Paquets | `Microsoft.EntityFrameworkCore.Sqlite` 8.0.27 · `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 · `BCrypt.Net-Next` 4.0.3 · pins de sécurité `System.Text.Json` 8.0.6 et `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0 |

**Npgsql est référencé en Infrastructure uniquement** — conformité D7/O14 vérifiée.

### `PersistenceErrorMapper` (307 lignes)

Classification **structurée uniquement** : parcourt toute la chaîne `InnerException`, décide sur
`SqlState`, type d'exception et état d'annulation — **jamais sur le message**. Traite les 3 cas sans
`SqlState` (timeout client, timeout de verrou, connexion refusée) par type + état de connexion.
Branches SQLite historiques **préservées** et couvertes par 8 tests de non-régression.

### `UnitOfWork` — dette résiduelle enregistrée

`CommitAsync` effectue désormais un rollback défensif avec `CancellationToken.None` (correctif 4B1,
5 lignes). Mais :

- **`RollbackAsync` public (ligne ~73) ne fait que `_context.DisposeAsync()` — il n'annule aucune
  transaction.** Dette **connue, documentée, non corrigée** (rapport 4B1 §10).
- Le défaut corrigé était **latent** : aucune méthode transactionnelle explicite de `UnitOfWork` n'a
  d'appelant de production (le flux passe par `EfTransactionRunner`).

## App

| Élément | État |
|---|---|
| Vues | **39** fichiers `.axaml` (Clients, Products, Orders, Users + racine) |
| ViewModels | **39** fichiers |
| Contrôles réutilisables | 4 (`ActionButtons`, `BackButton`, `LoadingSpinner`, `SearchBox`) |
| Styles | `AppStyles.axaml`, `Icons.axaml` |
| Navigation | `NavigationService` (singleton) + `DialogService` |
| État UI / session | `SessionService` (singleton), `PermissionService`, `ThemeService` |
| Composition root | `App.axaml.cs` — **enregistre tout directement** ; `Infrastructure.DependencyInjection.AddInfrastructure` est **inerte et non appelée** (documenté P2B-2J) |

**Deux chemins d'enregistrement DB coexistent** : `App.axaml.cs` (multi-provider, P4-3) et
`DependencyInjection.AddInfrastructure` (**SQLite câblé en dur**, mort). Risque de dérive si un
composition root alternatif était activé sans réconciliation.

---

# Database Status

## Provider et sélection

| Capacité | État |
|---|---|
| Abstraction de provider | **EXISTS** — `DatabaseProviderResolver` (source unique), enum fermé |
| Sélection par configuration | **EXISTS** — `MMV_DATABASE_PROVIDER` (`sqlite` défaut · `postgresql`/`postgres`/`npgsql`) |
| Chaîne de connexion serveur | **EXISTS** — `MMV_DATABASE_CONNECTION_STRING`, hors dépôt, jamais restituée dans un message d'erreur |
| Défaut sûr | **EXISTS** — variable absente/vide ⇒ SQLite (comportement historique) |
| Anti-repli silencieux | **EXISTS** — valeur inconnue ⇒ `DatabaseConfigurationException` ; PostgreSQL sans chaîne ⇒ blocage |
| **Démarrage sur PostgreSQL** | **BLOQUÉ VOLONTAIREMENT** — `App.axaml.cs`, garde placée **avant** le `try` pour qu'aucun `catch` générique ne la masque |

Texte du garde-fou : *« le fournisseur est correctement sélectionné comme fournisseur EF, mais la
préparation et les migrations serveur ne sont pas encore disponibles : elles arrivent avec P4-5/P4-6 »*.

## Migrations

| Élément | État |
|---|---|
| Migrations SQLite | **14** (28 fichiers `.cs`/`.Designer.cs` + 1 snapshot) |
| Migrations PostgreSQL | **NOT FOUND IN REPOSITORY** — P4-5 non commencée |
| Contrôle CI | `dotnet ef migrations has-pending-model-changes` — modèle strictement aligné sur la dernière migration |
| Portabilité de la chaîne SQLite | **NON PORTABLE** (audit P4-0 §12 : `GLOB`, `lower(trim())`, `CHECK`-abort, ADD COLUMN NOT NULL + défaut transitoire, table-rebuild `AlterColumn`, snapshot à annotations SQLite) |

## Dettes de schéma vérifiées dans le code au HEAD

| Dette | Vérification | Fichier |
|---|---|---|
| **Montants en `REAL`** (précision monétaire — le plus grave) | **PRÉSENT** | `SaleConfiguration.cs:29-44` (5 colonnes), `ProductConfiguration.cs:39-47` (3 colonnes) |
| **Filtre d'index littéral SQLite** | **PRÉSENT** | `WorkshopSheetConfiguration.cs:62` → `HasFilter("\"IsCurrent\" = 1")` + snapshot + 3 Designer |
| Filtre d'index Notifications | **PRÉSENT**, à réévaluer en P4-5 | `NotificationConfiguration.cs:64` |
| **Mapping `DateTime`** (`Kind=Local` refusé par PostgreSQL) | **NON TRAITÉ** — aucune conversion de valeur, aucune horloge injectable | transverse |
| Cycle de vie mono-processus (`Migrate()` par poste, backup par copie de fichier, adoption PRAGMA) | **PRÉSENT** | `SqliteDatabaseManager.PrepareDatabase` |

## Transactions et erreurs

| Élément | Verdict (P4-4B0, mesuré sur PostgreSQL 17.10) |
|---|---|
| `EfTransactionRunner` | **`SAFE_AS_IS`** — rollback effectif, aucune écriture partielle, contexte réutilisable, 0 commande après erreur. **Aucun changement requis.** |
| `UnitOfWork` jeton de rollback | **`CHANGE_REQUIRED`** → **corrigé par 4B1** |
| `OrderRepository` `catch` interne | **`SAFE_AS_IS`** (preuve statique) · **`BLOCKED_BY_P4_5_SCHEMA`** (re-preuve runtime) |
| Create/Update Product post-rollback | **`SAFE_AS_IS`** — mesuré |
| Retry | **AUCUN** — ni introduit, ni recommandé. Constat P4-1 (transaction PostgreSQL perdue après violation de contrainte, à rejouer par l'appelant) **entier et non traité** |
| Isolation / timeouts de production | **AUCUN** — jugés non nécessaires par la mesure (4B0 §14.3) |

---

# Multi-poste Status

## Ce qui existe déjà

| Capacité | État | Preuve |
|---|---|---|
| **Abstraction de provider** | **EXISTS** | `DatabaseProviderResolver` + enum fermé + options typées (P4-3) |
| **Configuration PostgreSQL** | **EXISTS** | `UseNpgsql` câblé, sélection par `MMV_DATABASE_PROVIDER`, secret par variable d'environnement |
| **Décision de SGBD serveur** | **EXISTS** | ADR-PROD-DB-002 acceptée — PostgreSQL |
| **Preuve d'installation serveur réelle** | **EXISTS (spike, local)** | P4-1 Lot C `PASS` — installation Windows native + réseau réel, 2 candidats |
| **Preuve de portabilité des primitives** | **EXISTS (spike, local)** | P4-1 Lot D — 14/14 primitives, concurrence 20/20 par provider |
| **Classification d'erreurs PostgreSQL** | **EXISTS** | `PersistenceErrorMapper` (P4-4A1), 33 tests en CI |
| **Frontière transactionnelle vérifiée sur PostgreSQL** | **EXISTS (spike, local)** | P4-4B0 — `EfTransactionRunner` `SAFE_AS_IS` |
| **Garde-fou anti-démarrage prématuré** | **EXISTS** | `App.axaml.cs` — refus de démarrer sur PostgreSQL |
| **Neutralité Domain/Application** | **EXISTS** | `ProviderNeutralityArchitectureTests` |

## Ce qui manque

| # | Manque | Phase prévue |
|---|---|---|
| 1 | **Connexion serveur applicative fonctionnelle** — le démarrage sur PostgreSQL est bloqué | P4-5 / P4-6 |
| 2 | **Migrations serveur** — aucune chaîne PostgreSQL | P4-5 |
| 3 | **Mapping monétaire exact** (O2) — `REAL` toujours en place | P4-5 / P4-7 |
| 4 | **Mapping `DateTime`** (O4) — MMV ne peut pas écrire de date sur PostgreSQL par EF | P4-4 et/ou P4-5 |
| 5 | **Filtres d'index portables** (O3) | P4-5 |
| 6 | **Sérialisation de l'application des migrations** entre postes + garde de version app ↔ schéma | P4-6 |
| 7 | **Outil / procédure de migration SQLite → serveur** | P4-7 |
| 8 | **Configuration, secrets et déploiement de la base centrale** (dont TLS/`sslmode`) | P4-8 |
| 9 | **Sauvegarde et restauration centrales** | P4-9 |
| 10 | **Tests multi-processus et de résilience dans `MMV.sln`** (perte réseau, reconnexion) | P4-10 |
| 11 | **Recette réelle sur plusieurs postes** | P4-11 |
| 12 | **Documentation opérateur / procédure d'exploitation** | P4-13 (critère 13) |
| 13 | **Re-preuve des 14 primitives sur le provider retenu** | P4-4, **bloquée par P4-5** |
| 14 | **Politique de reprise après transaction avortée** (PostgreSQL perd la transaction) | non affectée |
| 15 | **Audit trail et journalisation structurée** | **non affectée à aucune phase** |

## API et communication client/serveur

| Question | Réponse |
|---|---|
| API ? | **NOT FOUND IN REPOSITORY** — aucun `AspNetCore`, `WebApplication`, `SignalR`, `Grpc`, `TcpListener` dans `src/` |
| Communication client/serveur ? | **NOT FOUND IN REPOSITORY** — aucun `HttpClient` de transport applicatif |
| Authentification serveur / multi-poste ? | **NOT FOUND IN REPOSITORY** — l'authentification est locale, contre la table `Users` de la base |

**Ce n'est pas une lacune : c'est l'architecture décidée.** ADR-PROD-DB-001 et la roadmap P4 définissent
le multi-poste comme **plusieurs postes partageant une base centrale**, pas comme une architecture
client/serveur applicative. Aucune couche API n'est prévue en P4. Les postes se connecteront
**directement** à PostgreSQL. **Conséquence de sécurité à noter** : chaque poste détient donc des
identifiants de base de données, et la connexion transitera par le réseau du magasin — d'où
l'importance de P4-8 et de l'exigence TLS manquante (R14).

---

# UI Status

| Question | Réponse |
|---|---|
| Maquettes seulement ? | **Non** |
| Composants codés ? | **Oui** — 39 vues Avalonia + 39 ViewModels + 4 contrôles réutilisables + 2 feuilles de styles |
| Écrans fonctionnels ? | **Oui** — navigation, dialogues, session, permissions et thème sont implémentés et enregistrés dans l'IoC |
| `design-handoff/` | **NOT FOUND IN REPOSITORY** |
| `design/` | **NOT FOUND IN REPOSITORY** |
| `docs/ui/` | **NOT FOUND IN REPOSITORY** |
| Fichiers de design (Figma, maquettes, exports) | **NOT FOUND IN REPOSITORY** |

**Conclusion UI : l'interface est codée, pas maquettée.** Aucun artefact de design n'est versionné.
Les seules traces de travail UI sont documentaires et historiques :
[GUIDE_BOUTONS_ET_BINDINGS.md](../GUIDE_BOUTONS_ET_BINDINGS.md), [CORRECTIONS_LOGIN.md](../CORRECTIONS_LOGIN.md),
[TESTS_LOGIN.md](../TESTS_LOGIN.md), et les rapports P3-2C / P3-3C (alignement UI sur les règles métier).

Périmètre fonctionnel couvert par les vues : Dashboard, Login, Clients (détail, formulaire, info,
ordonnances, historique d'achat, vente), Ordonnances, Produits (détail, formulaire, historique de
commandes, mouvements de stock, fournisseurs), Commandes (détail, formulaire, kanban, fiche de
fabrication), Ventes, Inventaire, Notifications, Rapports, Paramètres, Utilisateurs, Profil.

> Observation : trois vues « Users » coexistent (`Views/UsersView.axaml`, `Views/Users/UsersView.axaml`,
> `Views/Users/UsersListView.axaml`) et `UserProfileView.axaml` est dupliqué entre `Views/` et
> `Views/Users/`. Doublon apparent non traité — **non vérifié** si les deux jeux sont réellement
> utilisés au runtime.

---

# Security Status

| Domaine | Classement | Preuve |
|---|---|---|
| **Authentication** | **EXISTS** | `AuthenticationService` — BCrypt work factor **11**, refus des comptes inactifs, **résultat `null` uniforme** (anti-énumération : compte inconnu, inactif ou mauvais mot de passe sont indistinguables) |
| **Normalisation des identifiants** | **EXISTS** | `GetByNormalizedUsernameAsync` + `UserIdentityPolicy` (P3-10) — migration `AddNormalizedUsernameAndSecureLocalUsers` |
| **Roles** | **EXISTS** | `UserRole` = `Admin`, `Optician`, `Technician` |
| **Authorization / permissions** | **PARTIAL** | `PermissionService` implémente une matrice de rôles documentée (`CanAccessModule`, `CanCreate`, `CanEdit`…) — **mais elle vit dans la couche `MMV.App` (UI)**. Aucune application de permission côté Application/Domain : un use case appelé hors UI n'est pas protégé par les rôles |
| **Secrets management** | **EXISTS** | Chaîne de connexion serveur par variable d'environnement, jamais dans le dépôt, **jamais restituée dans un message d'erreur** (commentaire explicite dans `DatabaseProviderResolver`) ; `DatabaseProviderOptions.ConnectionString` marquée « ne jamais journaliser ni sérialiser » |
| **Seeding sûr** | **EXISTS** | `SeedOptionsResolver` + `DatabaseSeeder` — défaut sûr (`Production`, sans seed), pas d'admin/admin actif en production (secret bootstrap ou neutralisation), validation **avant** préparation de base (P2A-1F) |
| **Protection database** | **PARTIAL** | Décisions prises **par la base en une instruction** (update/delete conditionnels, `CASE`) plutôt que `check-then-act` ; index uniques vérifiés physiquement (P3-8) ; **mais** aucune protection réseau/serveur (P4-8 non commencée), et aucun chiffrement au repos |
| **Validation input** | **PARTIAL** | Validateurs Domain (`Validators/`), `ValidationResult` standardisé (P3-1), règles métier par entité (P3-2 à P3-11). Portée **métier** ; aucune politique transverse d'assainissement |
| **Vulnérabilités de dépendances** | **EXISTS** | CI : `NuGetAuditMode=all` bloquant (NU1903/NU1904 **et** NU1900/NU1905 — un échec de récupération n'est jamais traité comme « 0 vuln ») + contrôle **machine** sur le JSON officiel, échec si High/Critical ou scan non concluant. Pins CVE : `System.Text.Json` 8.0.6, `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0 (CVE-2025-6965) |
| **Logs** | **NOT IMPLEMENTED** | **Aucun `ILogger`, aucun Serilog, aucun NLog** dans `src/`. La journalisation se réduit à `System.Diagnostics.Debug.WriteLine` dans `App.axaml.cs` (perdu en Release) et au `MigrationJournal` (journal de migration sur fichier, **spécifique schéma**) |
| **Audit trail** | **NOT IMPLEMENTED** | Aucune entité, table ni service d'audit. Seules traces : `CreatedAt`/`UpdatedAt` sur les entités et `LastLogin` sur `User`. **Aucune traçabilité « qui a fait quoi »** — lacune structurelle pour un logiciel professionnel multi-utilisateur |
| **Chiffrement du transport** | **NOT IMPLEMENTED** | Aucune exigence TLS/SSL sur la connexion PostgreSQL ; `sslmode` n'est ni imposé ni vérifié. À traiter en P4-8 |

## Points de sécurité les plus exposés

1. **Absence d'audit trail** alors que le produit devient multi-utilisateur et multi-poste.
2. **Absence de journalisation structurée** — un incident en production serait quasi indiagnosticable.
3. **Permissions dans la couche UI uniquement** — la garde n'est pas au bon niveau architectural.
4. **Aucune contrainte TLS** sur la future connexion réseau à la base centrale.

---

# Tests Status

## TEST STATUS REPORT

### Limite de mesure — à lire avant les chiffres

**La suite de tests n'a pas pu être exécutée dans cet environnement.** `dotnet test MMV.sln` échoue à
la restauration : la seule source NuGet enregistrée est `Microsoft Visual Studio Offline Packages`,
aucun accès à nuget.org, et aucun `NuGet.config` n'existe dans le dépôt. Erreurs `NU1101`/`NU1102` sur
`xunit`, `Microsoft.NET.Test.Sdk`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.Extensions.*`…

**Conséquence : aucun comptage ci-dessous n'est une mesure de cet audit.** Les totaux sont
**documentaires** (déclarés par les rapports de lot et les CI) ; les décomptes d'attributs sont
**statiques** (grep).

### Projets de tests

| Projet | Fichiers | `[Fact]` | `[Theory]` | `[InlineData]` |
|---|---|---|---|---|
| `tests/MMV.Domain.Tests` | 50 | 455 | 76 | 177 |
| `tests/MMV.Application.Tests` | 69 | 526 | 28 | 94 |
| `tests/MMV.App.Tests` | 24 | 205 | 5 | 34 |
| **Total** | **143** | **1186** | **109** | **305** |

*(Un `[Theory]` produit autant de cas que d'`[InlineData]` : `1186 + 305 = 1491` cas, à quoi s'ajoutent
les `MemberData`/`ClassData` non comptés — cohérent avec le total déclaré de 1569 sans le prouver.)*

### Progression documentaire des totaux

| Jalon | Total déclaré | Répartition déclarée | Source |
|---|---|---|---|
| Baseline P3 (`main`) | 1499 | — | roadmap P4 en-tête |
| P4-1 Lot D | **1505** | Domain 649 · Application 617 · App 239 | CI `30215445242` |
| P4-3 | **1529** | Domain 673 · Application 617 · App 239 | CI `30823399148` |
| P4-4A1 | **1565** | Domain 706 · Application 620 · App 239 | rapport 4A1 §13 (+36) |
| **P4-4B1 (HEAD)** | **1569** | Domain 710 · Application 620 · App 239 | rapport 4B1 §7 (+4) — **CI non enregistrée** |

### Couverture fonctionnelle visible

- **Domain** : validateurs, politiques (`OrderStatusPolicy`, `SalePricingPolicy`, `UserIdentityPolicy`),
  entités, optique/transposition, primitives de persistance (`EfTransactionRunner`,
  `EfStockMutationService`, `EfNumberSequenceService`), `PersistenceErrorMapper` (SQLite **et**
  PostgreSQL), `UnitOfWork`.
- **Application** : use cases par domaine métier + **tests d'architecture** (`ApplicationArchitectureTests`,
  `ProviderNeutralityArchitectureTests`, `SuppliersApplicationArchitectureTests`,
  `UsersApplicationArchitectureTests`, `NotificationsApplicationArchitectureTests`).
- **App** : ViewModels, navigation, permissions, bindings.

### Tests PostgreSQL

| Type | Où | Dans la CI ? |
|---|---|---|
| **Classification d'erreurs PostgreSQL** (33 tests) | `tests/MMV.Domain.Tests/Persistence/PersistenceErrorMapperPostgreSqlTests.cs` (524 l.) | **OUI** — exceptions **fabriquées en mémoire**, sans serveur |
| Neutralité de provider (3 tests) | `tests/MMV.Application.Tests/Architecture/ProviderNeutralityArchitectureTests.cs` | **OUI** |
| Rollback `UnitOfWork` (4 tests B1-1→B1-4) | `tests/MMV.Domain.Tests/Repositories/UnitOfWorkTests.cs` (237 l.) | **OUI** — doubles de test |
| **Forme des exceptions Npgsql** (E19, E20) | `spikes/P4.ProviderComparison/` | **NON** — serveur réel, hors `MMV.sln` |
| **Comportement transactionnel** (E21, 809 l.) | `spikes/P4.ProviderComparison/E21_…` | **NON** — serveur réel, hors `MMV.sln` |
| Spike comparatif complet (E1→E18) | `spikes/P4.ProviderComparison/` (21 fichiers + 13 supports) | **NON** |

### Tests de concurrence

- **EXISTS, mais hors CI** : `E5_UpsertConcurrencyTests`, `E12_DeadlockTests`,
  `E9_MultiProcessTests` + `Worker/` (processus séparé) — preuves **locales** du spike P4-1
  (concurrence 20/20 par provider serveur, Lot D).
- **Dans `MMV.sln` : aucun test de concurrence réelle multi-processus.**

### Tests d'intégration

- **Aucun test d'intégration serveur dans `MMV.sln`.** Tout ce qui touche un vrai PostgreSQL vit dans
  `spikes/`, **hors solution**, et n'est **pas** exécuté par la CI.
- La roadmap l'énonce explicitement : les résultats de spike « **ne doivent pas** être présentés comme
  reproduits par la CI tant que le workflow n'est pas explicitement modifié pour les exécuter ».

### CI

| Élément | État |
|---|---|
| Workflow | `.github/workflows/ci.yml` — unique |
| Runner | `windows-latest` |
| Déclencheurs | push sur `main`, `phase*`, `p2*`, `p3*`, `p4*` · PR vers `main` · `workflow_dispatch` |
| Étapes | restore → build (Debug, **sans** `-warnaserror`) → test (`trx`) → audit de vulnérabilités (texte + JSON + contrôle machine) → `dotnet tool restore` → `dotnet ef migrations has-pending-model-changes` |
| Couverture des spikes | **NON** — `spikes/` est hors `MMV.sln` |
| CI sur `d5a3656` et `2976401` | **NOT FOUND IN REPOSITORY** |

---

# Known Risks

| # | Risque | Gravité | Fondement |
|---|---|---|---|
| **R1** | **La roadmap officielle est fausse** : elle déclare `P4-4 = READY — NOT STARTED` alors que 4 sous-lots sont commités. Toute reprise qui lui fait confiance recommencerait du travail déjà livré, ou écraserait `PersistenceErrorMapper` et `UnitOfWork` | **CRITIQUE** | roadmap §6 vs `b84c7e3`, `dc4c492`, `d5a3656`, `2976401` |
| **R2** | **CI non enregistrée** pour `P4-4B0` et `P4-4B1`. Le dernier état vérifié par une CI enregistrée est `dc4c492`. Les 2 commits les plus récents — dont une modification de production — n'ont **aucune preuve de verdeur dans le dépôt** | **ÉLEVÉE** | aucun numéro de run dans les rapports 4B0/4B1 |
| **R3** | **Précision monétaire `REAL`** — dette la plus grave héritée de P3, toujours présente sur 8 colonnes. L'ADR précise : « avec `REAL`, la source elle-même est altérée ⇒ **O2 précède O11** » : **tout import SQLite → serveur fidèle est impossible avant correction** | **ÉLEVÉE** | `SaleConfiguration.cs`, `ProductConfiguration.cs` |
| **R4** | **`DateTime` (O4) non résolu** — « MMV ne peut pas écrire de date en l'état » sur PostgreSQL par le chemin EF. Le constat reste **observationnel, sans assertion** ; le risque R4 de l'ADR envisage même que la remédiation soit **impraticable** sans toucher aux contrats Domain/Application | **ÉLEVÉE** | ADR §15 O4 |
| **R5** | **Inversion de dépendance P4-4 ↔ P4-5** : la sortie de P4-4 exige la re-preuve des 14 primitives sur PostgreSQL, que 4B0 §15 déclare **bloquée par l'absence de schéma PostgreSQL (P4-5)**. **P4-4 ne peut pas être close en l'état** | **ÉLEVÉE** | roadmap P4-4 sortie · rapport 4B0 §15 |
| **R6** | **Aucun audit trail, aucune journalisation structurée** — pour un logiciel professionnel multi-utilisateur et bientôt multi-poste, aucune traçabilité des actions et aucun diagnostic d'incident | **ÉLEVÉE** | grep `ILogger|Serilog|NLog` = 0 résultat dans `src/` |
| **R7** | **Preuves serveur hors CI** — E19/E20/E21 et tout le spike P4-1 sont des preuves **locales non reproductibles automatiquement**. Une régression PostgreSQL ne serait détectée par aucun pipeline | **ÉLEVÉE** | `spikes/` hors `MMV.sln` |
| **R8** | **Cycle de vie mono-processus** : `Migrate()` par poste, backup par copie de fichier, adoption PRAGMA — inadapté à une base centrale ; plusieurs postes migreraient concurremment | **ÉLEVÉE** | `SqliteDatabaseManager` · roadmap P4-6 |
| **R9** | **Découpage `A0/A1/B0/B1` non gouverné** — absent de toute roadmap et de tout ADR. Les sous-lots suivants n'ont pas de périmètre officiel : la méthodologie « roadmap → ADR → lot → tests → rapport » est rompue au niveau du découpage | **MOYENNE** | grep `P4-4A|P4-4B` dans `docs/architecture/` + `docs/roadmap/` = 0 résultat |
| **R10** | **`UnitOfWork.RollbackAsync` public n'annule aucune transaction** — dispose seulement le contexte. Dette explicitement enregistrée et non corrigée ; latente aujourd'hui (aucun appelant de production), piège si un futur développeur l'appelle | **MOYENNE** | `UnitOfWork.cs` ~l.73 · rapport 4B1 §10 |
| **R11** | **Deux chemins d'enregistrement DB** : `App.axaml.cs` (multi-provider) et `DependencyInjection.AddInfrastructure` (SQLite en dur, code mort). Activer le second contournerait silencieusement la sélection de provider **et** le garde-fou de démarrage | **MOYENNE** | `DependencyInjection.cs` |
| **R12** | **Filtres d'index littéraux SQLite** (`"IsCurrent" = 1`, filtre LowStock) — corrigés **dans le spike uniquement** (`AdaptedOpticDbContext`), pas en production ; bloquent aussi la re-preuve runtime d'`OrderRepository` | **MOYENNE** | `WorkshopSheetConfiguration.cs:62` · ADR O3 |
| **R13** | **Aucun retry et aucune politique de reprise** alors que P4-1 a établi qu'une transaction PostgreSQL est perdue après violation de contrainte et doit être rejouée **par l'appelant**. Aucun appelant ne le fait | **MOYENNE** | rapport 4B0 §17 |
| **R14** | **Aucune exigence TLS** sur la connexion PostgreSQL — la base centrale sera jointe par le réseau, et chaque poste détiendra des identifiants de base | **MOYENNE** | aucune mention de `sslmode` dans `src/` |
| **R15** | **Environnement de build local non fonctionnel** (NuGet hors ligne, pas de `NuGet.config`, `gh` absent). Impossible de valider ou de vérifier une CI localement | **MOYENNE** | `dotnet nuget list source` |
| **R16** | **Doublons de vues `Users` / `UserProfileView`** entre `Views/` et `Views/Users/` — usage réel non vérifié | **FAIBLE** | arborescence `Views/` |

---

# Open Questions

| # | Question | Pourquoi elle bloque | Décideur |
|---|---|---|---|
| **Q1** | **P4-4 peut-elle être close sans la re-preuve des 14 primitives**, puisque celle-ci est bloquée par P4-5 ? Faut-il (a) scinder P4-4 en « traduction d'erreurs » (close) + « portage des primitives » (après P4-5), (b) réordonner P4-5 avant la fin de P4-4, ou (c) fusionner les deux ? | Sans réponse, **P4-4 ne peut jamais être déclarée close** et la séquence officielle est bloquée | Architecte |
| **Q2** | **O4 (`DateTime`) est-il traité en P4-4 ou en P4-5 ?** L'ADR dit « **P4-4 et/ou P4-5** ». Et par quelle option : conversion de valeur (UTC + horloge injectable, ADR-005) ou type de colonne ? | Détermine si un lot `P4-4C` existe, et si une **nouvelle ADR** est requise | Architecte |
| **Q3** | **Le découpage `A0/A1/B0/B1` doit-il être officialisé dans la roadmap** (avec les sous-lots restants nommés et bornés) ? | La méthodologie du projet exige une roadmap comme source d'état ; elle ignore aujourd'hui ces sous-lots | Architecte |
| **Q4** | **Quel est le statut CI réel de `d5a3656` et `2976401` ?** | Le HEAD n'a **aucune preuve de verdeur** enregistrée dans le dépôt | Opérateur (accès GitHub) |
| **Q5** | **Le harness `spikes/` doit-il entrer dans la CI** (job PostgreSQL conteneurisé) ? La roadmap l'autorise explicitement sous condition de modification du workflow | Sans cela, aucune preuve serveur n'est reproductible et R7 reste entier | Architecte |
| **Q6** | **Type et précision monétaires exacts** (`numeric(p,s)` ? quelle échelle ?) — l'ADR refuse volontairement de trancher | Prérequis **absolu** de P4-5 **et** de P4-7 (O2 précède O11) | Architecte + métier |
| **Q7** | **Forme de la chaîne de migrations serveur** : projet de migrations distinct, contexte distinct, ou autre ? La roadmap dit « forme non figée » | Premier arbitrage de P4-5 | Architecte |
| **Q8** | **Audit trail et journalisation structurée** : sont-ils dans le périmètre de P4 (multi-poste, multi-utilisateur) ou reportés à une phase P5 ? | R6 est une lacune structurelle ; aucun des 18 critères de sortie de P4 ne la couvre | Architecte + métier |
| **Q9** | **Les permissions doivent-elles descendre de `MMV.App` vers `MMV.Application` ?** | En multi-poste, la garde de rôle au seul niveau UI est architecturalement fragile | Architecte |
| **Q10** | **Le découpage P4-5 → P4-12 doit-il être réévalué** maintenant que P4-2 est acceptée ? La roadmap le prévoyait (« réévaluable après acceptation de P4-2 ») et ne l'a pas fait | Le plan d'exécution restant n'est pas confirmé | Architecte |

---

# Recommended Next Step

## NEXT OFFICIAL TASK

### `P4-4R` — Réconciliation de l'état officiel de P4-4 *(documentaire, préalable bloquant)*

| Champ | Contenu |
|---|---|
| **Nom du lot** | `P4-4R` — réconciliation roadmap / dépôt pour P4-4 |
| **Nature** | **Documentaire uniquement.** Aucun code, aucun test, aucune migration. |
| **Objectif** | Rendre la roadmap officielle **vraie** : y enregistrer `P4-4A0`, `P4-4A1`, `P4-4B0`, `P4-4B1` comme livrés, officialiser le découpage en sous-lots, tracer les numéros de CI réels, et **trancher Q1 et Q2** (dépendance P4-4 ↔ P4-5 et affectation de O4). |
| **Dépendances** | Aucune dépendance technique. Requiert **une décision d'architecte sur Q1, Q2, Q3**. |
| **Pourquoi cette étape** | La roadmap est la **source officielle d'état** du projet et elle est **fausse** (R1). Tant qu'elle déclare « P4-4 non commencée, aucune implémentation engagée », **aucune reprise de développement n'est sûre** : le prochain exécutant risque de refaire `PersistenceErrorMapper` ou d'écraser le correctif `UnitOfWork`. De plus, **P4-4 ne peut pas être close en l'état** (R5) : la question doit être tranchée avant d'engager du code, sinon le lot suivant travaille vers un critère de sortie inatteignable. C'est le seul lot qui **ne peut pas** être fait plus tard. |
| **Critères de sortie** | 1. Roadmap §6 mise à jour : les 4 sous-lots apparaissent avec commit + CI + état.<br>2. Le découpage `A0/A1/B0/B1` est **officialisé**, et les sous-lots restants de P4-4 sont **nommés et bornés**.<br>3. **Q1 tranchée** et écrite : périmètre de sortie de P4-4 ajusté, ou P4-5 réordonnée.<br>4. **Q2 tranchée** et écrite : O4 affecté à P4-4 ou P4-5, et option retenue (ADR-005 ou type de colonne) — avec ouverture d'ADR si nécessaire.<br>5. Les numéros de CI de `d5a3656` et `2976401` sont **enregistrés ou explicitement déclarés absents** (Q4).<br>6. Bloc d'état consolidé republié — **`V1 MULTI-POSTE = NOT GO`** maintenu.<br>7. Commit documentaire + CI verte sur le SHA exact. |
| **Interdit** | Modifier `PersistenceErrorMapper`, `UnitOfWork`, `EfTransactionRunner`, les configurations EF ou les migrations. Déclarer `P4-4 = CLOSE`. Déclarer `V1 MULTI-POSTE = GO`. |

### Lot technique suivant, selon la réponse à Q2

| Réponse à Q2 | Lot technique suivant |
|---|---|
| **O4 en P4-4** | `P4-4C` — **mapping `DateTime`** : trancher conversion de valeur (UTC + horloge injectable, ADR-005) vs type de colonne, implémenter, et **verrouiller par assertion** (le constat actuel est observationnel). Sortie : MMV peut écrire une date par le chemin EF sur PostgreSQL, prouvé par test. |
| **O4 en P4-5** | `P4-5` — **schéma et migrations serveur**, précédé de la décision Q6 (type monétaire, **prérequis absolu**) et Q7 (forme de la chaîne). Débloque O2, O3, O4, la re-preuve d'`OrderRepository` et celle des 14 primitives. |

**Recommandation de l'ingénieur :** `O4` en **P4-5**, avec le schéma. Trois raisons tirées du dépôt :
le mapping `DateTime` se matérialise dans les **colonnes** et les **migrations**, donc dans le périmètre
de P4-5 ; O2 (monétaire) et O3 (filtre d'index) sont déjà affectés à P4-5 et touchent les **mêmes
fichiers de configuration EF** — les séparer imposerait deux passes sur `SaleConfiguration`,
`ProductConfiguration` et `WorkshopSheetConfiguration` ; et la re-preuve des primitives, seul critère de
sortie restant de P4-4, est de toute façon **bloquée par P4-5** (R5). Sous cette option, `P4-4R`
enregistre P4-4 comme *« traduction d'erreurs livrée ; portage des primitives reporté après P4-5 »*, et
la phase repart sur **P4-5**.

**Cette recommandation n'engage rien : la décision appartient à l'architecte (Q1, Q2, Q6, Q7).**

---

## État officiel constaté par cet audit

```
BRANCH                      = p4-multi-poste
HEAD                        = 2976401
WORKING TREE                = CLEAN
P4-0                        = CLOSE
P4-1                        = COMPLETE (Lots A, B, C, D — 14/14 primitives)
P4-2                        = ACCEPTED — CLOSE (PostgreSQL retenu)
P4-3                        = CLOSE
P4-4A0                      = RECORDED (CI 33411724068)
P4-4A1                      = RECORDED (CI 33436908075)
P4-4B0                      = COMMITTED — CI NOT RECORDED
P4-4B1                      = COMMITTED — CI NOT RECORDED
P4-4                        = IN PROGRESS — NOT CLOSE
P4-4 ROADMAP STATE          = STALE (declares NOT STARTED)
P4-5 … P4-12                = NOT STARTED
OBLIGATION O5               = DONE
OBLIGATIONS O2, O3, O4      = OPEN
V1 MULTI-POSTE              = NOT GO
```

**Le dépôt réel prime toujours sur ce document.**
