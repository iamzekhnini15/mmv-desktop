# P4-4C State Reconciliation Report

**Lot :** `P4-4C` — réconciliation de l'état officiel de P4-4
**Nature :** **documentaire uniquement.** Aucun code de production, aucun test, aucune migration,
aucune configuration, aucun fichier CI n'a été modifié.
**Branche :** `p4-multi-poste`
**HEAD de base :** `297640185fb47aa163a792913401c94c6d556ad2` — `fix(P4-4B1): make UnitOfWork rollback cancellation-safe`
**Date :** 2026-09-17

---

## Objective

Rendre la **source officielle d'état** du projet — [P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) —
**conforme au dépôt réel**.

La roadmap déclarait encore `P4-4 = READY — NOT STARTED` et « aucune implémentation engagée », alors que
**quatre sous-lots P4-4 sont commités sur la branche**, dont **deux modifient du code de production**. Tant
que cet écart subsiste, toute reprise d'implémentation est dangereuse : le prochain exécutant pourrait
réécrire `PersistenceErrorMapper` ou écraser le correctif `UnitOfWork`.

Ce lot **ne décide rien de technique**. Il **n'arbitre aucune question d'architecture ouverte**, ne close
pas P4-4, ne modifie l'affectation d'aucune obligation de l'ADR-PROD-DB-002, et **n'ouvre pas P4-5**.

---

## Initial Documentation State

État documentaire **avant** ce lot, tel que mesuré sur `2976401` :

| Emplacement | Affirmation | Exacte ? |
|---|---|---|
| Roadmap, en-tête §0 | « `P4-3 = CLOSE` et `P4-4 = READY — NOT STARTED` » | ❌ périmée |
| Roadmap, fiche P4-1 | « P4-4 est désormais `READY — NOT STARTED` » | ❌ périmée |
| Roadmap, clôture P4-3 | « `P4-3 = CLOSE` et `P4-4 = READY — NOT STARTED` » | ❌ périmée |
| Roadmap, fiche **P4-4** (titre + statut courant) | « `READY — NOT STARTED` … **prête à commencer et non commencée** : aucune implémentation n'est engagée » | ❌ **écart principal** |
| Roadmap §5, point dur n° 2 | « `PersistenceErrorMapper` **entièrement couplé** à `SqliteException` » | ❌ périmée depuis `dc4c492` |
| Roadmap §6, tableau d'état | « `READY — NOT STARTED` — prochaine étape officielle, non commencée » | ❌ périmée |
| Roadmap §6, bloc d'état consolidé | `P4-4 = READY — NOT STARTED` | ❌ périmée |
| Roadmap §6, note « nature exacte de la clôture P4-3 » | « `PersistenceErrorMapper` **reste à porter en P4-4** » | ❌ périmée |
| Roadmap §6, état consolidé strict (fin de document) | « `P4-4` est `READY — NOT STARTED` : aucune implémentation engagée » | ❌ périmée |
| [Rapports `P4-4A0/A1/B0/B1`](.) | décrivent correctement leur propre lot ; chacun note « la roadmap n'a pas été modifiée par ce lot » | ✅ exacts |
| [MMV-PROJECT-RECOVERY-AUDIT](../reports/MMV-PROJECT-RECOVERY-AUDIT.md) | identifie l'écart (« Écart documentaire n° 1 », R1) et recommande un lot de réconciliation préalable | ✅ exact — **fichier non suivi par Git avant ce lot** |

**Aucun autre document** du dépôt ne portait d'état P4-4 périmé : la vérification a porté sur
`docs/architecture/`, `docs/implementation/P4-4*` et `docs/reports/`.

---

## Actual Repository State

Mesuré sur la branche `p4-multi-poste` au commit `2976401` :

| Sous-lot | Objet | Commit | Code de production | Tests | CI |
|---|---|---|---|---|---|
| **P4-4A0** | Preuve du comportement des exceptions PostgreSQL (forme structurelle Npgsql, sondes E19/E20) | `b84c7e3` — `test(P4-4A0): record PostgreSQL exception shape evidence` | ➖ sondes **hors `MMV.sln`** | 10 (sonde) | **`33411724068` — SUCCESS** sur le SHA exact |
| **P4-4A1** | Mapper d'erreurs de persistance PostgreSQL (`PersistenceErrorMapper`) — **obligation ADR O5** | `dc4c492` — `feat(P4-4A1): add PostgreSQL persistence error mapping` | ✅ `PersistenceErrorMapper` | **1529 → 1565** (+36) | **`33436908075` — SUCCESS** sur le SHA exact |
| **P4-4B0** | Preuve du comportement transactionnel PostgreSQL (spike E21, `25P02`, isolation, retry) | `d5a3656` — `test(P4-4B0): record PostgreSQL transaction behavior evidence` | ➖ spike **hors `MMV.sln`** | ➖ hors CI | **`NOT FOUND IN REPOSITORY`** |
| **P4-4B1** | Sûreté d'annulation du rollback de `UnitOfWork.CommitAsync` | `2976401` — `fix(P4-4B1): make UnitOfWork rollback cancellation-safe` | ✅ `UnitOfWork` (5 lignes) | **1565 → 1569** (+4) | **`NOT FOUND IN REPOSITORY`** |

Faits complémentaires :

- **Arbre de travail propre** avant le lot, à l'exception du fichier non suivi
  `docs/reports/MMV-PROJECT-RECOVERY-AUDIT.md`.
- **Obligation ADR O5** (classification d'erreurs PostgreSQL) : **traitée** par `P4-4A1`.
- **Obligations ADR O2, O3, O4** : **ouvertes**, vérifiées dans le code au HEAD (`REAL` présent dans
  `SaleConfiguration` et `ProductConfiguration` ; filtre `"IsCurrent" = 1` présent dans
  `WorkshopSheetConfiguration` + snapshot + 3 migrations ; aucune conversion `DateTime`).
- **Les numéros de CI de `d5a3656` et `2976401` n'existent nulle part dans le dépôt.** Ils sont enregistrés
  comme **absents**, et non supposés verts.
- Le découpage `A0 / A1 / B0 / B1` n'était **gouverné par aucun document d'architecture** : seule la roadmap
  fait foi, et elle ne connaissait que « P4-4 » comme bloc unique.

---

## Changes Made

**2 fichiers documentaires**, **0 fichier de code**.

### 1. `docs/architecture/P4-multi-poste-roadmap.md` — 10 corrections ciblées

| # | Emplacement | Correction |
|---|---|---|
| 1 | En-tête §0 | `P4-4` passe de `READY — NOT STARTED` à **`IN PROGRESS`**, avec renvoi au §6 |
| 2 | Fiche P4-1 (phrase de chaînage) | idem, mention des sous-lots `A0/A1/B0/B1` |
| 3 | Clôture P4-3 | l'énoncé `READY — NOT STARTED` est **daté** (« était alors ») et l'état courant indiqué |
| 4 | P4-3 « restent ouverts » | note de mise à jour : O5 et le rollback `UnitOfWork` traités depuis ; `DateTime`, monétaire et index filtrés toujours ouverts. **Le texte d'origine est conservé comme trace** |
| 5 | **Fiche P4-4** (titre + statut) | réécrite : **`IN PROGRESS`**, tableau des 4 sous-lots livrés (commit, CI, rapport), liste explicite du **restant**, et conservation de l'ancien statut en **bloc historique** |
| 6 | §5, point dur n° 2 | marqué **✅ porté en `P4-4A1`** (O5 traitée) |
| 7 | §6, tableau d'état | ligne P4-4 → **`IN PROGRESS — NOT CLOSE`** avec livré / restant / 1569 tests |
| 8 | §6, bloc d'état consolidé | ajout des lignes `P4-4A0`, `P4-4A1`, `P4-4B0`, `P4-4B1`, `P4-4 TESTS`, `P4-4 OBLIGATION O5`, `P4-4 OBLIGATIONS O2, O3, O4`, `P4-4 RUNTIME VALIDATION`, `P4-4 = IN PROGRESS — NOT CLOSE`, `P4-5 … P4-12 = NOT STARTED` ; **`V1 MULTI-POSTE = NOT GO` maintenu** |
| 9 | §6, note « nature exacte de la clôture P4-3 » | l'affirmation « `PersistenceErrorMapper` reste à porter » est **datée** et corrigée |
| 10 | §6, état consolidé strict (fin de document) | paragraphe P4-4 réécrit : livré, tests, O5 traitée, CI manquantes déclarées, restant borné, blocage P4-5 explicité |

**Convention respectée** : conformément à l'usage du document, **aucun énoncé historique n'a été supprimé** —
les états périmés sont **conservés et étiquetés comme tels**.

### 2. `docs/reports/MMV-PROJECT-RECOVERY-AUDIT.md` — mise sous suivi Git

Le rapport d'audit de reprise existait dans l'arbre de travail mais **n'était pas suivi par Git** : la preuve
à l'origine de ce réalignement n'était donc pas versionnée. Il est **ajouté tel quel**, sans modification de
son contenu.

### Ce qui n'a **pas** été modifié

- Aucun fichier `.cs`, aucun test, aucun `.csproj`, aucune migration, aucun snapshot EF.
- Aucun fichier de configuration, aucun workflow CI.
- **Aucune ADR** — en particulier, l'affectation des obligations O2/O3/O4 de
  [ADR-PROD-DB-002](../architecture/adr-prod-db-002-server-database-provider-selection.md) §15 est
  **inchangée**.
- Les rapports `P4-4A0`, `P4-4A1`, `P4-4B0`, `P4-4B1` : **intacts** — ils étaient exacts.

---

## Remaining P4-4 Work

Aucune de ces lignes n'est engagée à ce jour.

| # | Travail restant | État | Note d'affectation |
|---|---|---|---|
| **O2** | **Décision de mapping monétaire** — remplacement de `HasColumnType("REAL")` ; type et précision **non tranchés** | **OUVERT** | L'ADR §15 affecte O2 à **P4-5**. Le présent lot l'enregistre comme restant de la trajectoire P4-4/P4-5 **sans modifier l'affectation ADR** |
| **O3** | **Adaptation PostgreSQL des index uniques filtrés** (`"IsCurrent" = 1`, filtre LowStock actif) | **OUVERT** | ADR §15 → **P4-5** ; affectation inchangée |
| **O4** | **Stratégie `DateTime`** — PostgreSQL refuse `Kind = Local` par le chemin EF ; conversion de valeur (UTC + horloge injectable, ADR-005) **ou** type de colonne | **OUVERT — non tranché** | ADR §15 → **« P4-4 et/ou P4-5 »**. **L'alternative n'est pas arbitrée ici** : elle appartient à l'architecte |
| — | **Validation PostgreSQL au runtime**, dont la **re-preuve des 14 primitives** sur le provider retenu | **OUVERT — bloqué** | Impossible **tant que le schéma serveur n'est pas disponible** (P4-5). Établi par le rapport P4-4B0 §15 (`BLOCKED_BY_P4_5_SCHEMA`) |
| — | **Enregistrement des CI de `d5a3656` et `2976401`** | **ABSENT** | Nécessite un accès GitHub ; déclaré `NOT FOUND IN REPOSITORY` jusqu'à enregistrement |

**Conséquence directe** : le critère de sortie de P4-4 (« les 14 primitives prouvées sur le provider
retenu ») dépend d'un schéma serveur produit par **P4-5**. **P4-4 ne peut donc pas être close en l'état.**
Cette inversion de dépendance est **signalée, non résolue** : l'arbitrage (scinder P4-4, réordonner P4-5, ou
fusionner) appartient à l'architecte.

---

## Impact On Roadmap

- La roadmap redevient **vraie** : elle décrit l'état réel de la branche `p4-multi-poste` au commit `2976401`.
- **P4-4 n'est pas close.** Elle passe de `READY — NOT STARTED` à **`IN PROGRESS — NOT CLOSE`**.
- Le **découpage en sous-lots** `A0 / A1 / B0 / B1` est **officialisé** dans la roadmap, avec pour chacun son
  commit, sa CI (ou son absence) et son rapport.
- **Aucune étape n'est avancée** : P4-5 à P4-12 restent `NOT STARTED`.
- **`V1 MULTI-POSTE = NOT GO` est maintenu** — les 18 critères de sortie du §4 restent à satisfaire.
- Le **risque de régression par méconnaissance** (réécriture de `PersistenceErrorMapper`, écrasement du
  correctif `UnitOfWork`) est levé : la roadmap signale désormais ces deux fichiers comme **déjà traités**.
- **Deux sous-lots restent sans preuve de CI enregistrée** : ce trou est désormais **visible dans la source
  officielle d'état**, au lieu d'être invisible.

---

## Decision Log

| # | Décision | Justification |
|---|---|---|
| **D1** | **Statut retenu : `IN PROGRESS — NOT CLOSE`**, et non `CLOSE` ni `PARTIALLY CLOSE` | Quatre sous-lots sont livrés, mais le critère de sortie (re-preuve des 14 primitives) n'est pas atteint et est bloqué par P4-5 |
| **D2** | **Les énoncés périmés sont conservés et étiquetés « historique »**, jamais supprimés | Convention déjà en vigueur dans ce document pour P4-1 et P4-3 ; la traçabilité prime sur la concision |
| **D3** | **Les CI de `d5a3656` et `2976401` sont déclarées `NOT FOUND IN REPOSITORY`** | Aucun numéro n'existe dans le dépôt. **Inventer ou supposer un verdict de CI est interdit.** Le trou est enregistré tel quel |
| **D4** | **Aucune question d'architecture ouverte n'est arbitrée** (inversion de dépendance P4-4 ↔ P4-5 ; affectation de O4 ; officialisation d'un découpage pour les sous-lots **à venir**) | Le mandat de ce lot est **documentaire**. Ces arbitrages **changent le périmètre de sortie de P4-4** : ils appartiennent au lead architect |
| **D5** | **L'affectation ADR de O2 et O3 (→ P4-5) n'est pas modifiée** ; elles sont enregistrées comme *restant* de la trajectoire, avec renvoi explicite à l'ADR §15 | Le lot demandait de lister O2/O3 comme restant. Les inscrire **comme travail P4-4** contredirait une décision d'architecture existante, ce que ce lot s'interdit. Le restant est donc listé **avec** son affectation ADR d'origine — **signalé à l'architecte** |
| **D6** | **`docs/reports/MMV-PROJECT-RECOVERY-AUDIT.md` est mis sous suivi Git, sans modification** | C'est la pièce de preuve du réalignement ; la laisser non suivie rendrait la trace incomplète |
| **D7** | **Aucun test n'a été exécuté, aucune build lancée** | Aucun fichier de `MMV.sln` n'est touché. La baseline **1569** est **reprise des rapports de lot**, non re-mesurée — elle est citée comme telle |
| **D8** | **Le lot est nommé `P4-4C`** conformément à l'instruction de l'architecte | L'audit de reprise proposait `P4-4R` pour ce même travail ; le nom retenu est celui de l'architecte. **Le contenu est équivalent**, à l'exception des arbitrages Q1/Q2/Q3 qui restent ouverts (voir D4) |
| **D9** | **P4-5 n'est pas ouverte** | Hors mandat. Ce lot **termine la synchronisation documentaire**, rien de plus |

---

**Le dépôt réel prime toujours sur ce document.**
