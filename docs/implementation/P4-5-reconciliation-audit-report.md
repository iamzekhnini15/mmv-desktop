# P4-5 Reconciliation Audit Report

> **Note d'enregistrement (22 septembre 2026).** Ce rapport est **enregistré au dépôt** par le commit documentaire
> de la [revue d'acceptation P4-6A](P4-6A-adr-acceptance-report.md). Les mentions « non commité », « aucun commit »
> ou « non suivi » ci-dessous décrivent l'état **au moment de sa rédaction** : elles restent exactes pour le lot
> qu'elles décrivent, qui n'a produit **aucun commit de code**.

> **Lot P4-5-RECON-A : audit documentaire.** Aucun commit, aucun push, aucune branche.
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`.
> HEAD audité : **`440ddcb352b12e0c6900cfd5d6df550e234856c5`** (`docs(P4-5D-R): close civil date migration preparation`).
> Nature : **audit seul**. Aucun fichier de code, de test, de migration, d'ADR, de CI ni de roadmap n'est modifié.
> Le seul fichier produit est ce rapport.
> Statut : **`P4-5-RECON-A = AUDIT COMPLETE`**, en attente de validation architecte.
>
> **Le dépôt réel prime sur ce document, et sur la roadmap.**

---

## Objective

Établir l'état **réel** de chaque sous-lot de P4-5 à partir de Git et du code, puis le comparer à la
[roadmap P4](../architecture/P4-multi-poste-roadmap.md). Ce rapport prépare la réconciliation de la roadmap, sans
l'appliquer. Il fournit :

1. le statut réel de P4-5A, P4-5B, P4-5C, P4-5D, P4-5D-R et P4-5E ;
2. l'état réel des obligations **O2, O3, O4 et O7** d'[ADR-PROD-DB-002 §15](../architecture/adr-prod-db-002-server-database-provider-selection.md) ;
3. l'inventaire des incohérences, doublons et contenus périmés de la roadmap ;
4. une liste de corrections proposées et les décisions qui reviennent à l'architecte.

Ce lot correspond à la « Q6 » du [rapport final P4-5D-R](P4-5D-R-final-implementation-report.md) et à la
recommandation 3 du [rapport de clôture P4-5D-R](P4-5D-R-closure-report.md#next-phase).

---

## Methodology

### Ordre de preuve

Chaque statut est établi dans cet ordre, et un niveau inférieur ne peut jamais contredire un niveau supérieur :

| Rang | Source | Usage |
|---|---|---|
| 1 | **Git** | commits, fichiers, dates, état du distant |
| 2 | **Code présent à HEAD** | vérification que ce que le commit annonce existe réellement |
| 3 | **Rapports d'implémentation** | mesures (tests, build, dérive EF), décisions, limites |
| 4 | **Roadmap** | objet de l'audit, jamais preuve |

### Commandes exécutées (toutes en lecture seule)

```bash
git rev-parse HEAD ; git status --short ; git branch -vv
git log --oneline --decorate -40
git show --stat <sha>            # b49f2ff c9f37bc 9247497 b5c2073 023048f 45f67a2 440ddcb
git diff --stat 45f67a2 HEAD
git ls-remote origin             # état réel du distant, sans modifier les refs locales
git log --oneline -- <chemin>    # Migrations/, .github/workflows/, ADR 003 à 008
grep / sed / ls                  # sur src/, tests/, docs/, MMV.sln, ci.yml
```

### Ce qui n'a pas été exécuté

- **Build et tests : non relancés.** Le mandat limite l'exécution à la lecture et à `git status`. Les compteurs
  cités viennent des rapports. Ils restent valables à HEAD : `git diff --stat 45f67a2 HEAD` ne touche que deux
  fichiers `docs/`, donc **le code à HEAD est identique à celui de `45f67a2`**. La clôture P4-5D-R y a mesuré
  1915 / 1915 le 21/09/2026.
- **CI GitHub : non interrogée.** `gh` n'est pas installé sur le poste. L'absence de CI se déduit de l'état du
  distant, vérifié en direct par `git ls-remote`. Le workflow se déclenche sur `push`, et aucun SHA de P4-5 n'a été
  poussé.

---

## Git Evidence

### État du dépôt

| Élément | Valeur | Preuve |
|---|---|---|
| Branche | `p4-multi-poste` | `git rev-parse --abbrev-ref HEAD` |
| **HEAD** | **`440ddcb`** | `git rev-parse HEAD` |
| `origin/p4-multi-poste` | **`2976401`** (P4-4B1) | `git ls-remote origin`, vérifié en direct |
| Avance locale | **7 commits** (`b49f2ff` … `440ddcb`) | `git branch -vv` |
| `origin/main` | `3ed8836` | `git ls-remote origin` |
| Arbre à l'ouverture | 2 fichiers non suivis : `docs/architecture/P5-product-completion-roadmap.md`, `docs/implementation/P4-5D-final-implementation-report.md` | `git status --short` |

**Écart avec le brief.** Le brief donne `45f67a2` comme dernier commit. HEAD est en réalité **`440ddcb`**, le commit
de clôture documentaire de P4-5D-R (roadmap +44 / −4, rapport de clôture +284). Ce commit ne contient aucun code.

### Table des commits P4-5

| Lot | Commit | Status |
|---|---|---|
| *(P4-4C, socle)* | `b49f2ff` | commité, **non poussé**, CI absente |
| P4-5A | `c9f37bc` | **COMPLETED**, documentaire, non poussé, CI absente |
| P4-5B | `9247497` | **COMPLETED**, documentaire, non poussé, CI absente |
| P4-5C | `b5c2073` | **COMPLETED**, code, non poussé, CI absente |
| P4-5D | `023048f` | **COMPLETED**, code, non poussé, CI absente, rapport final **non commité** |
| P4-5D-R | `45f67a2` | **COMPLETED**, code, non poussé, CI absente |
| P4-5D-R (clôture) | `440ddcb` | commit documentaire, non poussé, CI absente |
| P4-5E | — | **NOT STARTED** : aucun commit |

### Détail

| Commit | Date | Message | Volume | Contenu |
|---|---|---|---|---|
| `b49f2ff` | 17/09 23:13 | `docs(P4-4C): reconcile P4-4 implementation state` | 3 fichiers, +879 / −21 | roadmap, rapport P4-4C, audit de reprise |
| `c9f37bc` | 18/09 00:09 | `docs(P4-5A): record PostgreSQL production schema audit report` | 1 fichier, +765 | rapport P4-5A |
| `9247497` | 18/09 00:10 | `docs(P4-5B): define PostgreSQL architecture decisions` | 8 fichiers, +1821 / −4 | 6 ADR, rapport P4-5B, roadmap |
| `b5c2073` | 18/09 00:39 | `feat(P4-5C): implement EF model portability for PostgreSQL` | 16 fichiers, +1250 / −52 | 3 prod ajoutés, 10 prod modifiés, 2 tests, rapport |
| `023048f` | 18/09 13:00 | `feat(P4-5D): implement temporal strategy and UTC enforcement` | 86 fichiers, +3774 / −222 | 33 `src/`, 50 `tests/`, 3 `docs/` |
| `45f67a2` | 21/09 17:01 | `feat(P4-5D-R): repair legacy civil date formats at startup` | 15 fichiers, +5360 / −1 | 4 prod, 3 tests, 7 docs, roadmap |
| `440ddcb` | 21/09 18:16 | `docs(P4-5D-R): close civil date migration preparation` | 2 fichiers, +324 / −4 | roadmap, rapport de clôture |

### Trajectoire des tests (mesures des rapports)

| Point | Domain | Application | App | **Total** | Source |
|---|---|---|---|---|---|
| `2976401` (P4-4B1, poussé) | 710 | 620 | 239 | **1569** | rapport P4-5C, *Starting State* |
| `b5c2073` (P4-5C) | 821 | 620 | 239 | **1680** (+111) | rapport P4-5C, *Vérification finale* |
| `023048f` (P4-5D) | 872 | 628 | 258 | **1758** (+78) | rapport P4-5D, *Tests Executed* |
| `45f67a2` (P4-5D-R) | 1029 | 628 | 258 | **1915** (+157) | rapport final P4-5D-R ; relancé à la clôture |

La CI n'a reproduit **aucune** de ces trois dernières mesures. Dans la roadmap, la dernière CI enregistrée est celle
de P4-4A1 (`dc4c492`, run `33436908075`), et le dernier compteur explicitement associé à une CI est **1529** (P4-3,
run `30823399148`). La roadmap déclare non enregistrées les CI de `d5a3656` et `2976401` (l. 805–806).

### Observations d'hygiène, sans correction proposée

- `023048f`, `45f67a2` et `440ddcb` ont un message sans corps. `45f67a2` et `440ddcb` n'ont pas le trailer
  `Co-Authored-By`, que tous les commits P4 précédents portent. Le message proposé par le rapport final P4-5D-R le
  prévoyait. Aucune réécriture d'historique n'est recommandée.
- `c9f37bc` et `9247497` sont séparés de 21 secondes, et `b5c2073` suit `9247497` de 29 minutes (voir P4-5B).

---

## P4-5A Status

| Critère | Constat | Preuve |
|---|---|---|
| Rapport | présent et suivi : [P4-5A-postgresql-schema-audit-report.md](P4-5A-postgresql-schema-audit-report.md) | `c9f37bc` |
| SHA audité | `b49f2ff` (P4-4C), arbre propre | rapport §0 |
| Nature | audit **statique** strict, aucun fichier de code modifié | `git show --stat c9f37bc` : 1 fichier `docs/` |
| Tests | **aucun exécuté** : restauration NuGet indisponible sur le poste | rapport §0.2 |
| Décisions prises | **aucune**, par mandat. Six décisions posées (AD-1 … AD-6) | rapport, *Required Architectural Decisions* |
| Recommandations dépassées | mapping monétaire unique sur les deux providers, et « 15ᵉ migration SQLite » : **renversées par P4-5B** | rapport P4-5B, *Ce que ces décisions changent* |

```
REAL STATUS: COMPLETED — c9f37bc — DOCUMENTAIRE — CI NOT RECORDED
```

La roadmap porte `COMPLETE — DECISIONS REQUIRED` (l. 598). C'est vrai sur le fond, mais le qualificatif
`DECISIONS REQUIRED` est dépassé : P4-5B a pris ces décisions.

---

## P4-5B Status

| Critère | Constat | Preuve |
|---|---|---|
| ADR créées | **6** : ADR-PROD-DB-003 à 008 | `git show --stat 9247497` |
| Statut des ADR | en-tête `Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation)`, §1 `Accepted` | en-tête de chaque ADR |
| SHA de décision | `b49f2ff` | en-tête de chaque ADR |
| Stabilité | **aucune modification** des six ADR depuis `9247497` | `git log` sur les six fichiers : un seul commit |
| Rapport | [P4-5B-postgresql-architecture-decisions-report.md](P4-5B-postgresql-architecture-decisions-report.md) | `9247497` |
| Code | aucun | 8 fichiers, tous `docs/` |

**Répartition des obligations ADR-002 décidée par P4-5B :**

| ADR | Obligation ADR-002 | Obligations filles | Lot d'exécution |
|---|---|---|---|
| [003 monétaire](../architecture/adr-prod-db-003-money-persistence.md) | **O2** | M1 … M7 | P4-5C, P4-5F, P4-7 |
| [004 temporelle](../architecture/adr-prod-db-004-datetime-strategy.md) | **O4** | T1 … T9 | P4-5D, P4-5F, P4-7, P4-8/9 |
| [005 migrations](../architecture/adr-prod-db-005-migration-architecture.md) | **O7** | G1 … G7 | P4-5E, P4-5F, P4-6 |
| [006 index](../architecture/adr-prod-db-006-index-and-model-portability.md) | **O3** | X1 … X6 | P4-5C, P4-5F |
| [007 dérive](../architecture/adr-prod-db-007-schema-drift-prevention.md) | — | S1 … S7 | P4-5E, P4-5F, P4-6 |
| [008 intégration](../architecture/adr-prod-db-008-postgresql-integration-testing.md) | prépare O12 | Q1 … Q8 | P4-5F, P4-8 |

**Validation architecte des ADR : non trouvée dans le dépôt.** Le rapport P4-5B pose comme premier prérequis de
P4-5C la « validation des six ADR par le Lead Software Architect », et précise qu'elles sont `ACCEPTED` « en tant
que décisions proposées et formalisées par ce lot ; l'arbitrage final lui appartient ». Aucun document ultérieur
n'enregistre cette validation. Le rapport P4-5C n'en fait pas mention, et P4-5C a été commité 29 minutes après
P4-5B. Ce n'est pas une preuve d'absence de validation, seulement une absence de trace. Le cas est traité comme
`NOT FOUND IN REPOSITORY`, selon la convention de P4-4C (voir D-5).

Autres prérequis de P4-5B :
- restauration NuGet : **levée** par P4-5C (source passée en ligne de commande, rapport P4-5C, *Note d'environnement*) ;
- ordre P4-5 / P4-4 : **toujours non tranché** (roadmap l. 834–838).

```
REAL STATUS: COMPLETED — 9247497 — 6 ADR ACCEPTED — CI NOT RECORDED
```

La roadmap est cohérente sur ce lot (l. 599 et l. 813).

---

## P4-5C Status

**Objectif** (ADR-003 et ADR-006) : un seul modèle EF pour SQLite et PostgreSQL. Il porte `numeric(12,2)` sur les
14 colonnes monétaires, un filtre d'index sélectionné par provider, et retire les littéraux `"REAL"` optiques.

### Fichiers du commit `b5c2073` (16)

- **Ajoutés en production :** `Data/Portability/ModelPortability.cs`, `LegacySqliteMoneyStoreType.cs`, `MoneyMappingExtensions.cs`.
- **Modifiés en production :** `OpticDbContext.cs` et 9 configurations d'entités.
- **Tests ajoutés :** `ModelPortabilityTests.cs` (12), `EfModelPortabilityTests.cs` (99).
- **Documentation :** le rapport du lot.

### Vérification dans le code à HEAD

| Exigence | Constat | Emplacement |
|---|---|---|
| Précision monétaire (12,2) en un point unique | `MoneyPrecision = 12`, `MoneyScale = 2` | [ModelPortability.cs:41](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L41), [:44](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L44) |
| 14 colonnes monétaires mappées | **14** appels `HasMoneyMapping(` dans `src/` | `grep` |
| Type SQLite historique reconduit | `HasColumnType(storeType)` délégué au point de sélection | [MoneyMappingExtensions.cs:48](../../src/MMV.Infrastructure/Data/Portability/MoneyMappingExtensions.cs#L48) |
| Littéraux de type restants | **1 seul**, `"TEXT"`, justifié dans le code | [ProductConfiguration.cs:75](../../src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs#L75) |
| Filtre `IsCurrent` par provider | `"IsCurrent" = 1` (SQLite) / `"IsCurrent"` (PostgreSQL) | [ModelPortability.cs:53](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L53), [:60](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L60) |
| Migrations SQLite intactes | dossier `Migrations/` inchangé depuis `8d4bf49` (P3-10) : aucun commit P4 ne l'a touché | `git log -- src/MMV.Infrastructure/Migrations` |

- **Tests :** 1569 → **1680**, 0 échec, 0 ignoré. Aucun test existant n'a été modifié (d'après le rapport).
- **Obligations closes selon le rapport :** M1, M2, M3, X1, X2, X3, X4.
- **Restant :** M4, M5, M6, X5 et X6 (preuves contre PostgreSQL, P4-5F) ; M7 (P4-7).
- **Proposition non tranchée :** P-1, `IModelCacheKeyFactory` explicite, « appartient naturellement à P4-5E » si
  elle est retenue.
- **Revue architecte :** aucune trace dans le dépôt, ni dans le rapport ni dans un document ultérieur. Le rapport se
  borne à indiquer « validation de l'architecte attendue » pour ouvrir P4-5D. P4-5D a ensuite été ouvert et commité.

```
REAL STATUS: COMPLETED — b5c2073 — CI NOT RECORDED
```

La roadmap porte **`NOT STARTED`** (l. 600 et l. 817). **Elle est incohérente.**

---

## P4-5D Status

**Objectif** (ADR-004) : UTC pour tout instant, horloge injectable, convertisseur validant, test d'architecture et
`DateOnly` pour les dates civiles.

### Vérification dans le code à HEAD

| Exigence ADR-004 | Constat | Emplacement |
|---|---|---|
| **T1** : `IClock` en Domain, implémentation en Infrastructure | présents ; `SystemClock` enregistré en singleton | [IClock.cs](../../src/MMV.Domain/Interfaces/Time/IClock.cs), [SystemClock.cs](../../src/MMV.Infrastructure/Services/SystemClock.cs), [DependencyInjection.cs:69](../../src/MMV.Infrastructure/DependencyInjection.cs#L69), [App.axaml.cs:161](../../src/MMV.App/App.axaml.cs#L161) |
| **T2** : zéro `DateTime.Now` dans `src/**` | **une seule** occurrence de `DateTime.Now`, `DateTime.Today` ou `DateTimeOffset.Now` dans tout `src/`, dans l'unique entrée de la liste blanche | [SystemClock.cs:48](../../src/MMV.Infrastructure/Services/SystemClock.cs#L48) |
| **T3** : convertisseur **validant** | écriture : **lève** `NonUtcDateTimeException` si `Kind != Utc` ; lecture : `SpecifyKind(Utc)` ; appliqué à tout le modèle | [UtcDateTimeConverter.cs:60](../../src/MMV.Infrastructure/Data/Time/UtcDateTimeConverter.cs#L60), [OpticDbContext.cs:202](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L202) |
| **T4** : test d'architecture | `AmbientTimeArchitectureTests`, 5 tests | [AmbientTimeArchitectureTests.cs](../../tests/MMV.Domain.Tests/Architecture/AmbientTimeArchitectureTests.cs) |
| **T5** : `DateOnly` + reprise des données | `DateOnly? BirthDate`, `DateOnly IssueDate` ; **reprise déléguée à P4-5D-R** | [Customer.cs:39](../../src/MMV.Domain/Entities/Customer.cs#L39), [Prescription.cs:35](../../src/MMV.Domain/Entities/Prescription.cs#L35) |
| **T6** : conversions UI | pont unique `DatePickerCivilDate` ; audit consigné | [DatePickerCivilDate.cs](../../src/MMV.App/Time/DatePickerCivilDate.cs), pré-revue P4-5D l. 70 |

- **Tests :** 1680 → **1758**, 0 échec, 0 ignoré.
- **Migration EF :** aucune. Ce résultat est voulu : le changement de format des dates civiles n'est pas visible par
  EF (ADR-004 §7.2).
- **Revue architecte :** tracée. Pré-revue, pré-revue V2 (corrections C1 à C3), puis « Validation architecte obtenue »
  dans le rapport final.

### Documentation du lot

| Document | État Git | Remarque |
|---|---|---|
| [Pré-revue](P4-5D-temporal-strategy-pre-review.md) | suivi (`023048f`) | — |
| [Pré-revue V2](P4-5D-temporal-strategy-pre-review-v2.md) | suivi (`023048f`) | — |
| [Rapport final](P4-5D-final-implementation-report.md) | **NON SUIVI** | rédigé après `023048f`, jamais commité |

Le rapport final non suivi contient **une imprécision factuelle**. Il dit que `UtcDateTimeConverter` « normalise en
UTC à l'écriture » (*Objective* point 4 et tableau *Les quatre pièces*). Or le code **ne normalise pas** : il
laisse passer une valeur UTC telle quelle et **lève** sur toute autre valeur, conformément à l'ADR (qui rejette
l'option `ToUniversalTime()`). Le rapport indique aussi `P4-5D-R` en `NOT STARTED`, ce qui était l'état au 18/09
et ne l'est plus.

```
REAL STATUS: COMPLETED — 023048f — CI NOT RECORDED — FINAL REPORT NOT COMMITTED
```

La roadmap porte **`NOT STARTED`** (l. 601 et l. 817). **Elle est incohérente.**

---

## P4-5D-R Status

**Objectif :** rendre lisibles les bases SQLite antérieures à P4-5D, dont `Customer.BirthDate` et
`Prescription.IssueDate` sont encore stockés au format `DateTime`. Sans cette reprise, leur lecture lève
`FormatException`.

### Vérification dans le code à HEAD

| Exigence | Constat | Emplacement |
|---|---|---|
| Règle unique de classification | `CivilDateFormat.Classify` | [CivilDateFormat.cs](../../src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs) |
| Diagnostic en lecture seule | vérificateur ADO sur le `TEXT` brut | [SqliteCivilDateFormatVerifier.cs](../../src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs) |
| Reprise transactionnelle | service de reprise | [SqliteCivilDateRepairService.cs](../../src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs) |
| Branchement au démarrage | appel dans `PrepareDatabase` | [SqliteDatabaseManager.cs:124](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L124) |
| `dryRun` d'abord, refus sans écriture | `Repair(context, dryRun: true)`, puis `CIVILDATE REFUSED` / `ok` / `detected` / `repaired` / `FAILURE` | [SqliteDatabaseManager.cs:264-305](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L264-L305) |

- **Tests :** 1758 → **1915** (+157, tous dans `MMV.Domain.Tests`).
- **Migration et schéma :** aucune migration EF, schéma inchangé, `DateOnly` conservé.
- **Décisions :** toutes tranchées, de Q1 à Q-C6. Q1 (« reprise automatique au démarrage ») est **VALIDÉ** ;
  Q3 → P4-7, Q7 → P7, D-B4 → P4-6.
- **Encore ouvert :**
  - **RR4 :** `dryRun` jamais exécuté sur une copie de base réelle ; bloque le déploiement ;
  - tests de spécification T-B1 (sur `L'`) et T-B4 à T-B8 ;
  - CI.

### Cohérence documentaire

- **Roadmap :** `COMPLETED` à **trois** endroits (l. 602, l. 818, section finale l. 901–953). Les trois sont
  cohérents entre eux. En revanche, la l. 817 donne sa dépendance P4-5D en `NOT STARTED` (voir I-03).
- **Rapport final :** figé avant le commit (`HEAD 023048f`, `READY FOR COMMIT`, RR2 ouvert). C'est un instantané
  cohérent avec sa date.
- **Rapport de clôture :** commité dans `440ddcb`, il affirme pourtant « AUCUN COMMIT »,
  `CLOSURE COMMIT = NOT CREATED`, HEAD `45f67a2` et « 6 commits d'avance ». Ces quatre énoncés sont démentis par le
  commit qui le porte. **`440ddcb` n'est mentionné nulle part dans la roadmap.**

```
REAL STATUS: COMPLETED — 45f67a2 (clôture 440ddcb) — CI NOT RECORDED — RR4 OPEN (DEPLOYMENT GATE)
```

---

## P4-5E Status

**Périmètre attendu :** ADR-005 G1 à G4 et G6, ADR-007 S1, S2 et S6. Cela comprend une chaîne de migrations
PostgreSQL, une factory design-time sélective et un double contrôle de dérive en CI.

| Contrôle | Constat | Preuve |
|---|---|---|
| Assembly ou projet de migrations PostgreSQL | **absent** : `MMV.sln` compte 7 projets (4 `src`, 3 `tests`) | `MMV.sln` |
| Sélection de l'assembly de migrations | **absente** : aucun `MigrationsAssembly` dans `src/` | `grep` |
| Dossiers de migrations | seul `src/MMV.Infrastructure/Migrations` (14 migrations et snapshot, 29 fichiers) | `find`, `ls` |
| Factory design-time | **figée sur SQLite** | [OpticDbContextFactory.cs:24](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L24) |
| `ci.yml` | inchangé depuis **`8cf0919` (P4-0)** ; un seul contrôle de dérive | [ci.yml:117-118](../../.github/workflows/ci.yml#L117-L118) |
| Règle de double migration (G6, S6) | non documentée | — |
| Dépendances roadmap | P4-5C ✓ et P4-5D ✓ **en code** ; aucune n'a de CI | ci-dessus |

```
REAL STATUS: NOT STARTED — DEPENDENCIES SATISFIED IN CODE — CANDIDATE NEXT (see D-1 … D-4)
```

La roadmap porte `NOT STARTED` (l. 603), ce qui est **exact**. Le statut `NEXT` n'apparaît nulle part.

**Au-delà de P4-5E, P4-5F et P4-5G ne sont pas commencés.** Il n'existe aucun projet d'intégration PostgreSQL, et le
garde-fou de démarrage est toujours en place ([App.axaml.cs:205-207](../../src/MMV.App/App.axaml.cs#L205-L207)).

---

## Obligation O2/O3/O4/O7 Review

### Synthèse

| Obligation | Roadmap aujourd'hui | Statut réel recommandé |
|---|---|---|
| **O2** monétaire | `OPEN` (l. 809, sous P4-4) · `DECIDED — NOT IMPLEMENTED` (l. 815) · « non tranchés » (l. 526) | **MODEL IMPLEMENTED (M1–M3) — RUNTIME PROOF PENDING** |
| **O3** index | `OPEN` (l. 809) · `DECIDED — NOT IMPLEMENTED` (l. 815) | **MODEL IMPLEMENTED (X1–X4) — MIGRATION + RUNTIME PROOF PENDING** |
| **O4** `DateTime` | `OPEN` (l. 809) · `DECIDED — NOT IMPLEMENTED` (l. 815) · « non tranché » (l. 531) | **CODE IMPLEMENTED (T1–T6) — RUNTIME PROOF PENDING — RR4 OPEN** |
| **O7** migrations | `DECIDED — NOT IMPLEMENTED` (l. 816) | **DECIDED — NOT IMPLEMENTED** (inchangé) |

Aucune des quatre obligations n'est **close**. Ce point est conforme à la roadmap elle-même : « elles ne le seront
qu'après P4-5C … P4-5F, preuves à l'appui » (l. 830–831). La roadmap commet toutefois l'erreur inverse, car trois
d'entre elles y figurent comme **non implémentées** alors qu'elles le sont au niveau du modèle ou du code.

### O2 — Mapping monétaire exact

**Original decision.** [ADR-002 §15](../architecture/adr-prod-db-002-server-database-provider-selection.md) prévoit
un mapping monétaire exact à la place de `HasColumnType("REAL")`, avec « type et précision non choisis », à exécuter
en P4-5 (+ P4-7). [ADR-003](../architecture/adr-prod-db-003-money-persistence.md) (`9247497`) a tranché : `decimal`
+ `HasPrecision(12,2)` ⇒ `numeric(12,2)` sur PostgreSQL, avec un type physique SQLite conservé (11 `REAL`, 3 `TEXT`)
par un point de sélection unique.

**Current implementation.**

| Obligation | État | Lot |
|---|---|---|
| M1 : `HasPrecision(12,2)` sur les 14 colonnes | **fait** | P4-5C (`b5c2073`) |
| M2 : aucun `REAL` monétaire vers PostgreSQL, point de sélection unique | **fait** | P4-5C |
| M3 : chaîne SQLite inchangée | **fait** | P4-5C |
| M4 : `numeric(12,2)` vérifié physiquement | non commencé | P4-5F |
| M5 : fidélité aller-retour contre PostgreSQL | non commencé | P4-5F |
| M6 : sémantique `RemainingAmount > 0` / `SUM` sur les deux providers | non commencé | P4-5F |
| M7 : règle de conversion transmise à l'import | non commencé | P4-7 |

**Evidence.**
- Code : [ModelPortability.cs:41](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L41) et `:44` ;
  14 appels `HasMoneyMapping(` ; un seul littéral `"TEXT"` restant.
- Tests : `EfModelPortabilityTests` (99) et `ModelPortabilityTests` (12).
- Migrations inchangées depuis `8d4bf49`.

**Recommended status.**
`IMPLEMENTED IN MODEL (M1–M3) — RUNTIME PROOF PENDING (M4–M6, P4-5F) — IMPORT RULE PENDING (M7, P4-7)`.
Une précision s'impose : l'exactitude monétaire **n'est pas prouvée**. Elle ne peut l'être que contre PostgreSQL
(ADR-003 §5.7).

### O3 — Filtre d'index booléen PostgreSQL

**Original decision.** ADR-002 §15 demande de réécrire `"IsCurrent" = 1` en forme booléenne PostgreSQL « dans les
**mappings et migrations** de production », en P4-5.
[ADR-006](../architecture/adr-prod-db-006-index-and-model-portability.md) a tranché :
- filtre sélectionné par provider en un point unique, avec échec sur provider inconnu ;
- filtre `Notifications` inchangé ;
- retrait des 18 littéraux `"REAL"` optiques.

**Current implementation.**

| Obligation | État | Lot |
|---|---|---|
| X1 : filtre par provider, échec sur provider inconnu | **fait** | P4-5C |
| X2 : un seul point de lecture du provider | **fait** | P4-5C |
| X3 : 18 littéraux optiques retirés | **fait** | P4-5C |
| X4 : chaîne SQLite inchangée | **fait** | P4-5C |
| Partie « migrations » d'O3 : filtre porté par la baseline PostgreSQL | non commencé | P4-5E |
| X5 : `pg_index` vérifié physiquement | non commencé | P4-5F |
| X6 : `lower()` verrouillé par provider | non commencé | P4-5F |

**Evidence.**
- Code : [ModelPortability.cs:53](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L53)
  (SQLite) et [:60](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs#L60) (PostgreSQL) ;
  `WorkshopSheetConfiguration.cs:71-72`.
- Tests : filtre par provider (2), `Notifications` inchangé (1), colonnes optiques (36).

**Recommended status.**
`IMPLEMENTED IN MODEL (X1–X4) — PENDING P4-5E (PostgreSQL baseline) AND P4-5F (X5, X6)`.

### O4 — Stratégie `DateTime`

**Original decision.** ADR-002 §15 demande de résoudre le mapping `DateTime`, car PostgreSQL refuse `Kind = Local`
par le chemin EF. Il faut décider entre conversion de valeur et type de colonne, puis verrouiller par assertion.
L'obligation était affectée à « P4-4 et/ou P4-5 ».
[ADR-004](../architecture/adr-prod-db-004-datetime-strategy.md) a tranché : UTC, `IClock`, convertisseur validant,
test d'architecture et `DateOnly` pour les dates civiles.

**Current implementation.**

| Obligation | État | Lot |
|---|---|---|
| T1 : `IClock` | **fait** | P4-5D (`023048f`) |
| T2 : `DateTime.Now` éliminé | **fait** | P4-5D |
| T3 : convertisseur validant | **fait** | P4-5D |
| T4 : test d'architecture | **fait** | P4-5D |
| T5 : `DateOnly` | **fait** | P4-5D |
| T5 : reprise des données | **fait**, sous forme de reprise au démarrage et non de migration EF | P4-5D-R (`45f67a2`), Q1 validé |
| T6 : audit des conversions UI | **fait** | P4-5D |
| T7 : fidélité contre PostgreSQL, avec assertion | non commencé | P4-5F |
| T8 : règle du `Kind` transmise à l'import | non commencé | P4-7 |
| T9 : exigence NTP | non commencé | P4-8 / P4-9 |

**Evidence.**
- Horloge : [SystemClock.cs:48](../../src/MMV.Infrastructure/Services/SystemClock.cs#L48) est le seul appel
  d'horloge ambiante dans `src/`.
- Convertisseur : [UtcDateTimeConverter.cs:60](../../src/MMV.Infrastructure/Data/Time/UtcDateTimeConverter.cs#L60)
  lève sur toute valeur non UTC.
- Dates civiles : [Customer.cs:39](../../src/MMV.Domain/Entities/Customer.cs#L39) et
  [Prescription.cs:35](../../src/MMV.Domain/Entities/Prescription.cs#L35).
- Reprise : [SqliteDatabaseManager.cs:124](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L124).

**Recommended status.**
`IMPLEMENTED IN CODE (T1–T6) — RUNTIME PROOF PENDING (T7, P4-5F) — TRANSFERS PENDING (T8 → P4-7, T9 → P4-8/9) — DEPLOYMENT GATE RR4 OPEN`.
L'alternative de phase « P4-4 et/ou P4-5 » est tranchée **de fait** : l'obligation a été exécutée en P4-5. La ligne
l. 809 (`P4-4 OBLIGATIONS O2, O3, O4 = OPEN`) n'a donc plus d'objet pour O4, ni pour O2 et O3, que l'ADR-002
affecte à P4-5.

### O7 — Chaîne de migrations serveur distincte

**Original decision.** ADR-002 §15 demande une chaîne serveur distincte avec une baseline propre, les 14 migrations
SQLite restant utilisables, en P4-5. [ADR-005](../architecture/adr-prod-db-005-migration-architecture.md) a
tranché : deux assemblys de migrations, un seul `DbContext`. [ADR-007](../architecture/adr-prod-db-007-schema-drift-prevention.md)
y ajoute le double contrôle de dérive.

**Current implementation.** **Aucune.** G1 à G7 et S1 à S7 ne sont pas commencés. Le prérequis de conservation
**G4 est tenu à ce jour** : les 14 migrations SQLite n'ont été touchées par aucun commit P4.

**Evidence.**
- Pas de projet de migrations PostgreSQL dans `MMV.sln`, et aucun `MigrationsAssembly` dans `src/`.
- [OpticDbContextFactory.cs:24](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L24) est figée sur SQLite.
- `ci.yml` n'a pas changé depuis `8cf0919`.
- `git log -- src/MMV.Infrastructure/Migrations` : dernier commit `8d4bf49` (P3-10).

**Recommended status.** `DECIDED — NOT IMPLEMENTED — P4-5E`. La l. 816 de la roadmap est **exacte** et doit être
conservée.

---

## Roadmap Inconsistencies

Tous les numéros de ligne renvoient à [P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) à HEAD
`440ddcb`.

### A. Statuts contredits par le dépôt

| # | Ligne(s) | Texte actuel | Constat |
|---|---|---|---|
| **I-01** | 600 | P4-5C `NOT STARTED` | commité `b5c2073`, code présent, +111 tests |
| **I-02** | 601 | P4-5D `NOT STARTED` | commité `023048f`, code présent, +78 tests |
| **I-03** | 817–818 | `P4-5C … P4-5G = NOT STARTED` puis `P4-5D-R = COMPLETED` | double erreur : P4-5C et P4-5D sont faux, et le bloc donne un lot **COMPLETED** dont la dépendance (P4-5D, l. 602 colonne « Dépend de ») est **NOT STARTED** |
| **I-04** | 770 | §6, P4-5 : « P4-5A […] et P4-5B […] livrés, tous deux strictement documentaires : aucun code […] » · « Restant : P4-5C … P4-5G, **aucune ligne implémentée** » | trois sous-lots de code sont livrés |
| **I-05** | 573 | titre `IN PROGRESS (P4-5A et P4-5B livrés)` | cinq sous-lots livrés |
| **I-06** | 16–21 | « `P4-5` est ouverte avec deux sous-lots strictement documentaires […] Aucune ligne de code n'a encore été écrite pour le schéma serveur » | faux depuis `b5c2073` |
| **I-07** | 850–851 | « **P4-5 n'est pas commencée** », au présent | faux ; la phrase n'est pas marquée comme historique |

### B. Doublons et contradictions internes

| # | Ligne(s) | Constat |
|---|---|---|
| **I-08** | 809 / 815 | O2, O3 et O4 sont suivis **deux fois** avec deux statuts différents : `OPEN` sous P4-4 et `DECIDED — NOT IMPLEMENTED` sous P4-5. Aucun des deux n'est exact (voir la revue des obligations) |
| **I-09** | 524–532, 769 | la liste « Restant à faire dans P4-4 » dit O2 « type et précision **non tranchés** » et O4 « **non tranché** ». Elle est contredite par ADR-003 et ADR-004 (acceptées dans `9247497`) et par la l. 815 du même document. La l. 769 répète « Restant : O2, O3, O4 » |
| **I-10** | 602, 818, 901–953 | le statut P4-5D-R apparaît **trois fois**. La section finale est en anglais, non numérotée, placée après §6 et d'une forme différente du reste du document (« Status: / Commit: / Objective: ») |
| **I-11** | 601, 607–612 | la ligne P4-5D annonce « `DateOnly` + **migration de données écrite à la main** », et la note (1) dit que « seul le passage des dates civiles à `DateOnly` exige une migration, **de données**, écrite à la main ». Le mécanisme livré est une **reprise au démarrage** (P4-5D-R, aucune migration EF), ce que la l. 602 décrit correctement. Le vocabulaire de l'ADR-004 (T5, §7.2) a été repris sans être aligné sur l'implémentation |

### C. Contenus périmés

| # | Ligne(s) | Texte actuel | Constat |
|---|---|---|---|
| **I-12** | 825–832 | « La baseline **1569** reste celle de P4-4C » ; « `POSTGRESQL CLEAN START` reste `BLOCKED` par ses trois causes cumulatives — […] modèle EF non portable » | la baseline est **1915**. La cause « modèle non portable » est **levée au niveau du modèle** par P4-5C et P4-5D, sans preuve au runtime. Le garde-fou de démarrage et l'absence de migrations serveur restent |
| **I-13** | 880, 893–894 | « **Ces trois chantiers restent ouverts** » (l. 880) ; « **Trois chantiers techniques restent ouverts** (`DateTime` — O4, filtres d'index booléens PostgreSQL — O3, mapping monétaire `REAL` — O2) » (l. 893–894) | les trois sont implémentés au niveau du modèle ; seules leurs preuves au runtime sont ouvertes |
| **I-14** | 746, 754 | §5, points durs 1 (`REAL`) et 4 (index filtrés) : sans annotation | les points 2 et 3 portent une annotation ✅ de résolution, pas les points 1 et 4, pourtant traités par P4-5C |
| **I-15** | 593–594 | « chaque sous-lot se termine par **commit + CI verte sur le SHA exact** » | règle remplie par **aucun** des sous-lots P4-5 (aucun push). Les états « COMPLETE » des l. 598–599 et 602 sont affichés sans cette réserve |
| **I-16** | 605 | garde-fou de démarrage cité en `App.axaml.cs:198` | depuis `023048f`, il se trouve en [App.axaml.cs:205](../../src/MMV.App/App.axaml.cs#L205) (`if (!usesSqlite)`) et `:207` (`throw`). La même ancre périmée figure dans les rapports P4-5A, P4-5B et P4-5C |
| **I-17** | 598 | P4-5A `COMPLETE — DECISIONS REQUIRED` | décisions prises par P4-5B |
| **I-18** | 603 | P4-5E `NOT STARTED` | exact, mais aucune étape n'est désignée comme suivante dans P4-5 |
| **I-19** | 600–601 | lignes P4-5C et P4-5D sans lien vers leur rapport | les lignes P4-5A, P4-5B et P4-5D-R en ont un. Le rapport P4-5D n'est pas commité |
| **I-20** | — | `440ddcb` absent de la roadmap | le commit de clôture de P4-5D-R n'est tracé nulle part |

### D. Documents adjacents (hors roadmap, même périmètre de réconciliation)

| # | Document | Constat |
|---|---|---|
| **I-21** | [P4-5D-final-implementation-report.md](P4-5D-final-implementation-report.md) | **non suivi**. Il décrit le convertisseur comme « normalisant », alors que le code lève. Il donne `P4-5D-R` en `NOT STARTED` (instantané du 18/09) |
| **I-22** | [P4-5D-R-closure-report.md](P4-5D-R-closure-report.md) | commité dans `440ddcb`, il affirme « AUCUN COMMIT » et `CLOSURE COMMIT = NOT CREATED`, et donne HEAD `45f67a2` et « 6 commits d'avance » |
| **I-23** | [P4-5B report](P4-5B-postgresql-architecture-decisions-report.md), *Decisions Summary* | la colonne est intitulée « Obligation ADR-002 **close** », alors que le bloc final du même rapport dit « **décidées, non implémentées** ». La formulation est ambiguë. Aucune correction n'est proposée sur un rapport historique, mais la roadmap ne doit pas reprendre la lecture « close » |

---

## Recommended Corrections

**Rien n'est appliqué par ce lot.** Les corrections ci-dessous sont proposées pour un lot documentaire séparé
(P4-5-RECON-B), sur le modèle de P4-4C. La roadmap a une convention : un énoncé dépassé est **conservé et marqué**
(`[HISTORIQUE]`, ou une note *« Mise à jour … »*) plutôt qu'effacé. Les propositions la respectent.

### Corrections de statut (sous réserve de D-2)

| # | Cible | Correction proposée | Traite |
|---|---|---|---|
| **C-01** | l. 598 | P4-5A → `COMPLETED — c9f37bc` ; retirer `DECISIONS REQUIRED` ou le remplacer par « décisions prises en P4-5B » | I-17 |
| **C-02** | l. 599 | P4-5B → `COMPLETED — 9247497 — 6 ADR ACCEPTED` | cohérence |
| **C-03** | l. 600 | P4-5C → `COMPLETED — b5c2073` + lien vers le [rapport](P4-5C-ef-model-portability-report.md) | I-01, I-19 |
| **C-04** | l. 601 | P4-5D → `COMPLETED — 023048f` + lien vers le rapport une fois commité (D-6) ; remplacer « migration de données écrite à la main » par « reprise des dates civiles : P4-5D-R » | I-02, I-11, I-19 |
| **C-05** | l. 602 | ajouter `440ddcb` (clôture) à la ligne P4-5D-R | I-20 |
| **C-06** | l. 603 | P4-5E → `NOT STARTED — NEXT`, si l'architecte le confirme (D-4) | I-18 |
| **C-07** | l. 812–819 | remplacer le bloc P4-5 de l'état courant par le bloc proposé ci-dessous | I-03, I-08 |
| **C-08** | l. 770 | réécrire la ligne P4-5 du tableau §6 : sous-lots livrés, baseline 1915, restant P4-5E … P4-5G | I-04 |
| **C-09** | l. 573 | titre → `IN PROGRESS (P4-5A … P4-5D-R livrés)` | I-05 |

### Corrections de contenu

| # | Cible | Correction proposée | Traite |
|---|---|---|---|
| **C-10** | l. 16–21 | mettre à jour le bandeau : P4-5C, P4-5D et P4-5D-R ont écrit du code ; aucune chaîne de migrations serveur n'existe encore | I-06 |
| **C-11** | l. 850–851 | marquer « P4-5 n'est pas commencée » comme **historique**, selon la convention du document | I-07 |
| **C-12** | l. 809 | marquer la ligne P4-4 `O2, O3, O4 = OPEN` comme **transférée à P4-5** plutôt que la supprimer | I-08 |
| **C-13** | l. 524–532, 769 | ajouter une note *« Mise à jour P4-5-RECON »* : O2, O3 et O4 tranchés par ADR-003, 004 et 006 (`9247497`), implémentés au niveau du modèle ou du code par P4-5C et P4-5D, preuves au runtime dues en P4-5F | I-09 |
| **C-14** | l. 607–612 | note (1) : le passage à `DateOnly` n'a produit **aucune migration EF**. La reprise des valeurs est une **reprise au démarrage** (P4-5D-R) | I-11 |
| **C-15** | l. 825–832 | baseline → 1915 ; pour `CLEAN START`, préciser que la cause « modèle » est levée au niveau du modèle mais non prouvée au runtime, et que les deux autres causes restent | I-12 |
| **C-16** | l. 880, 893–894 | remplacer « trois chantiers restent ouverts » par « trois chantiers implémentés au niveau du modèle ; preuve PostgreSQL due en P4-5F » | I-13 |
| **C-17** | l. 746, 754 | ajouter aux points durs 1 et 4 une annotation, comme pour les points 2 et 3 : « modèle neutralisé en P4-5C (`b5c2073`) ; preuve physique due en P4-5F » | I-14 |
| **C-18** | l. 593–594 | ajouter une réserve explicite : aucun sous-lot P4-5 n'a encore de CI (dépend de D-1) | I-15 |
| **C-19** | l. 605 | ancre `App.axaml.cs:198` → `App.axaml.cs:205` | I-16 |
| **C-20** | l. 901–953 | traiter selon D-7 : conserver, intégrer à la fiche P4-5 en français, ou retirer comme doublon | I-10 |

### Bloc d'état courant proposé pour P4-5 (C-07)

```
P4-5A SCHEMA AUDIT           = COMPLETED — c9f37bc — DOCUMENTAIRE — CI NOT RECORDED
P4-5B ARCHITECTURE DECISIONS = COMPLETED — 9247497 — 6 ADR ACCEPTED — CI NOT RECORDED
P4-5C MODEL PORTABILITY      = COMPLETED — b5c2073 — 1680 TESTS — CI NOT RECORDED
P4-5D TEMPORAL STRATEGY      = COMPLETED — 023048f — 1758 TESTS — CI NOT RECORDED
P4-5D-R CIVIL DATE REPRISE   = COMPLETED — 45f67a2 (+ 440ddcb) — 1915 TESTS — CI NOT RECORDED — RR4 OPEN
P4-5E PG MIGRATION CHAIN     = NOT STARTED — NEXT
P4-5F … P4-5G                = NOT STARTED
P4-5 OBLIGATION O2           = MODEL IMPLEMENTED (M1–M3) — RUNTIME PROOF PENDING (P4-5F)
P4-5 OBLIGATION O3           = MODEL IMPLEMENTED (X1–X4) — PENDING P4-5E / P4-5F
P4-5 OBLIGATION O4           = CODE IMPLEMENTED (T1–T6) — RUNTIME PROOF PENDING (T7, P4-5F)
P4-5 OBLIGATION O7           = DECIDED — NOT IMPLEMENTED (P4-5E)
P4-5 TESTS                   = 1915 (LOCAL — NOT REPRODUCED BY CI)
P4-5                         = IN PROGRESS — NOT CLOSE
POSTGRESQL CLEAN START       = BLOCKED (garde-fou de démarrage · absence de migrations serveur)
```

Les termes `COMPLETED` et `NEXT` dépendent de D-2 et D-4.

### Hors roadmap

| # | Cible | Correction proposée | Traite |
|---|---|---|---|
| **C-21** | rapport final P4-5D | le commiter (`docs(P4-5D)`) après correction de la seule imprécision factuelle (« normalise » → « lève sur toute valeur non UTC ») ; laisser l'instantané `P4-5D-R NOT STARTED` daté du 18/09 | I-21, D-6 |
| **C-22** | rapport de clôture P4-5D-R | ajouter une note post-commit (« enregistré par `440ddcb` »), ou le laisser comme instantané et tracer `440ddcb` dans la roadmap seule | I-22, D-8 |

---

## Architect Decisions Required

| # | Décision | Pourquoi elle bloque | Recommandation de l'audit |
|---|---|---|---|
| **D-1** | **Mode de push des 7 commits et CI sur le SHA exact** | Un `push` unique de 7 commits déclenche **un seul** run, sur le SHA de tête. La règle « CI verte sur le SHA exact » de chaque sous-lot (l. 593–594) ne peut pas être remplie par un push groupé | choisir entre (a) des pushes successifs commit par commit, qui donnent une CI par lot ; (b) un push unique, avec la CI de tête déclarée comme couverture rétroactive des lots intermédiaires, et la règle amendée pour ces lots ; (c) un autre mode. **Trancher avant RECON-B**, puisque la rédaction des statuts en dépend |
| **D-2** | **Vocabulaire de statut** : `COMPLETED` (commité) contre `CLOSE` (commité + CI verte) | la roadmap emploie `CLOSE` pour P4-0 à P4-3, `RECORDED` / `COMMITTED` pour P4-4, et `COMPLETE` / `COMPLETED` pour P4-5, sans définition commune | adopter explicitement : `COMPLETED` = commité avec rapport ; `CLOSE` = `COMPLETED` + CI verte sur le SHA exact |
| **D-3** | **Statut des obligations O2, O3 et O4** | la roadmap les donne à la fois `OPEN` et `DECIDED — NOT IMPLEMENTED`, alors qu'elles sont implémentées au niveau du modèle ou du code | adopter les statuts de la revue des obligations ; rattacher O2, O3 et O4 à P4-5 seule ; marquer la l. 809 comme transférée |
| **D-4** | **Ouverture de P4-5E comme `NEXT`**, et ses préalables | P4-5E sera la première modification de `ci.yml` depuis P4-0. Deux questions antérieures touchent directement son périmètre : **P-1** (`IModelCacheKeyFactory`, que P4-5C rattache à P4-5E) et l'**inversion de dépendance P4-4 ↔ P4-5** (R-5B-6, toujours non tranchée) | confirmer P4-5E comme `NEXT` ; trancher P-1 et R-5B-6 **avant** son ouverture ; confirmer que RR4 et les tests T-B1, T-B4 à T-B8 **ne précèdent pas** P4-5E (RR4 bloque le déploiement, pas le développement) |
| **D-5** | **Trace de validation des six ADR et de P4-5C** | le rapport P4-5B faisait de la validation architecte un prérequis de P4-5C ; aucune trace n'existe dans le dépôt | enregistrer la validation dans le lot RECON-B (une ligne datée), ou constater `NOT FOUND IN REPOSITORY` selon la convention de P4-4C |
| **D-6** | **Rapport final P4-5D non suivi** | il est la seule trace de la validation architecte de P4-5D et de ses 1758 tests | le commiter avec la correction C-21 ; dans un commit `docs(P4-5D)` séparé, ou avec RECON-B |
| **D-7** | **Section finale « P4-5D-R Civil Date Migration Preparation »** (l. 901–953) | triple mention du même statut, dans une langue et une forme distinctes du document | l'intégrer à la fiche P4-5, en français, et retirer la section autonome |
| **D-8** | **Rapport de clôture P4-5D-R**, démenti par son propre commit | les rapports du dépôt sont des instantanés, mais celui-ci est contredit par le commit qui l'enregistre | ajouter une note post-commit d'une ligne (C-22), ou le laisser en l'état et tracer `440ddcb` dans la roadmap |
| **D-9** | **Lecture de T5 (ADR-004)** | l'ADR exige une « migration de données écrite à la main » ; P4-5D-R livre une reprise au démarrage, validée par Q1 | confirmer que Q1 vaut exécution de T5, **sans addendum d'ADR**, et le consigner dans la roadmap uniquement (C-14) |

---

## Conclusion

**1. P4-5 est plus avancée que la roadmap ne le dit.** Cinq sous-lots sont livrés et commités : P4-5A, P4-5B, P4-5C,
P4-5D et P4-5D-R. Trois d'entre eux écrivent du code. La roadmap en déclare deux « NOT STARTED » (P4-5C, P4-5D) et
affirme à plusieurs endroits qu'aucune ligne de P4-5 n'est implémentée. L'erreur ne va que dans un sens : **aucun
statut de la roadmap ne surestime l'état réel**.

**2. Le code confirme chaque rapport.** Tous les livrables annoncés sont présents à HEAD : point de sélection
unique, `numeric(12,2)` déclaré, filtre d'index par provider, `IClock`, convertisseur qui lève, `DateOnly`,
reprise au démarrage. Aucune migration SQLite n'a été touchée depuis P3-10, et aucune ADR n'a été modifiée depuis
son acceptation.

**3. Aucune de ces avancées n'est prouvée par la CI.** Les 7 commits (`b49f2ff` … `440ddcb`) sont locaux, et le
distant pointe sur `2976401`. La règle de découpage « commit + CI verte sur le SHA exact » n'est remplie pour
**aucun** sous-lot de P4-5, et un push groupé ne suffira pas à la remplir (D-1). La baseline de 1915 tests est une
mesure **locale**.

**4. Aucune obligation n'est close.** O2, O3 et O4 sont **implémentées au niveau du modèle ou du code**, et leurs
preuves contre PostgreSQL restent dues en P4-5F. O7 est **décidée, non implémentée**, et constitue le cœur de
P4-5E. La roadmap les donne à tort comme « non implémentées », et même « non tranchées » dans la fiche P4-4.

**5. P4-5E n'est pas commencé et peut devenir `NEXT`.** Ses dépendances sont satisfaites en code. Son ouverture
dépend de décisions architecte : D-1, D-2, D-4, et en particulier P-1 et l'inversion P4-4 ↔ P4-5.

**6. La compatibilité applicative de MMV sur PostgreSQL reste `NOT_PROVED`**, `POSTGRESQL CLEAN START` reste
`BLOCKED`, et **la V1 multi-poste n'est pas `GO`**. **RR4** reste le préalable obligatoire à tout déploiement sur une
installation en service.

```
P4-5-RECON-A AUDIT          = COMPLETE
HEAD AUDITED                = 440ddcb (brief: 45f67a2 — superseded by closure commit)
ORIGIN p4-multi-poste       = 2976401 — LOCAL AHEAD 7
P4-5A                       = COMPLETED — c9f37bc — CI NOT RECORDED
P4-5B                       = COMPLETED — 9247497 — CI NOT RECORDED
P4-5C                       = COMPLETED — b5c2073 — CI NOT RECORDED   (roadmap: NOT STARTED — WRONG)
P4-5D                       = COMPLETED — 023048f — CI NOT RECORDED   (roadmap: NOT STARTED — WRONG)
P4-5D-R                     = COMPLETED — 45f67a2 + 440ddcb — CI NOT RECORDED — RR4 OPEN
P4-5E                       = NOT STARTED — DEPENDENCIES SATISFIED IN CODE
O2 / O3                     = IMPLEMENTED IN MODEL — RUNTIME PROOF PENDING
O4                          = IMPLEMENTED IN CODE — RUNTIME PROOF PENDING — RR4 OPEN
O7                          = DECIDED — NOT IMPLEMENTED
ROADMAP INCONSISTENCIES     = 20 (I-01 … I-20) + 3 ADJACENT (I-21 … I-23)
CORRECTIONS PROPOSED        = 22 (C-01 … C-22) — NONE APPLIED
ARCHITECT DECISIONS         = 9 (D-1 … D-9)
CODE CHANGED                = NONE
ROADMAP CHANGED             = NONE
V1 MULTI-POSTE              = NOT GO
```
