# P4-4 et P4-5 — réconciliation et clôture

> **Verdicts : `P4-4 = COMPLETE — CLOSED`, `P4-5 = COMPLETE — CLOSED`** (5 octobre 2026), **décision de
> l'architecte principal** sur la recommandation de ce rapport : clôture par réconciliation, la re-preuve Windows
> native des primitives (O12) étant reportée à la recette P4-11 — même traitement que Q-P4-8-3 et Q-P4-9-1.
> Lot **documentaire** : aucun code, aucun test, aucune migration modifiés. Base : `3dd3957`.

## 1. Constat

Le bloc d'état de la roadmap marquait encore O2, O3, O4, O7 « DECIDED — NOT IMPLEMENTED » et P4-4/P4-5
« IN PROGRESS ». Le dépôt montre le contraire : les obligations ont été **livrées** par P4-5C/D/E et **prouvées sur
un vrai PostgreSQL** par P4-5F ; les lignes d'état n'avaient pas été rafraîchies.

## 2. Obligations (ADR-PROD-DB-002 §15)

| # | Obligation | Livrée par | Preuve exécutée |
|---|---|---|---|
| **O2** | mapping monétaire exact | P4-5C `b5c2073` — `HasPrecision(12,2)`, point de sélection unique (`Data/Portability`), `REAL` conservé pour SQLite seul | IT `Fidelity/MoneyFidelityTests` (N3, 6 tests : aller-retour exact, borne 12,2 refusée, arrondis, sommes) |
| **O3** | filtre d'index booléen PostgreSQL | P4-5C (filtre par fournisseur) ; baseline PG : `filter: "\"IsCurrent\""` | IT `Schema/SchemaTests` (`idx_workshop_sheets_current_unique :: "IsCurrent"`) |
| **O4** | stratégie `DateTime` verrouillée par assertion | P4-5D `023048f` — `IClock`, UTC partout, convertisseur validant, `DateOnly` ; P4-5D-R `45f67a2` | unitaire `Architecture/AmbientTimeArchitectureTests` ; IT `Fidelity/DateTimeFidelityTests` (N4 : `Kind=Utc`, instant UTC stocké, **non-UTC refusé sans écriture**, dates civiles) |
| **O5** | classification des erreurs | P4-4A0/A1 | déjà `DONE` |
| **O7** | chaîne de migrations serveur distincte | P4-5E `a06c19f` + `ccbeb32` — `InitialPostgreSqlBaseline` seule, 14 migrations SQLite intactes | CI : double contrôle de dérive EF + garde anti-faux-vert de la chaîne (run `37291313801`) |
| **O12** | re-prouver les 14 primitives sur PostgreSQL | P4-5F `692b6da` (corpus N6) | IT `Primitives/PrimitivesTests` (15 tests) vert à chaque CI — **Linux conteneurisé** ; Windows native → **Q-P4-4-1** |

## 3. Sous-lots P4-5

| Sous-lot | État |
|---|---|
| P4-5A, P4-5B | COMPLETE (documentaires, ADR acceptés) |
| P4-5C, P4-5D, P4-5D-R, P4-5E | COMPLETED (commits ci-dessus) |
| P4-5F | COMPLETE — CLOSED (`692b6da`, CI `37202557042`) |
| P4-5G | absorbé par P4-6C — **livré** (`0d2d3e3`, CI `37290396173`) |

## 4. P4-4

Livré : P4-4A0 `b84c7e3`, P4-4A1 `dc4c492` (CI enregistrées), P4-4B0 `d5a3656`, P4-4B1 `2976401` (CI non
retrouvées à l'époque). Le code de ces deux derniers est un ancêtre de chaque SHA vérifié depuis : il est couvert par
toute CI verte ultérieure (dernière : `37291313801` sur `3dd3957`, unitaires 2384, IT 195/195). La « validation au
runtime après disponibilité du schéma serveur » est faite (O12 Linux, §2).

## 5. Questions de gouvernance closes

- **D4 / Q-18 (inversion P4-4 ↔ P4-5)** : **sans objet** — les deux lots sont livrés ; aucun ordre ne reste à arbitrer.
- **Démarrage PostgreSQL propre** : **débloqué** — provision → backup → verify-backup → migrate → bootstrap-admin →
  configure-workstation (procédures P4-8 et P4-9).

## 6. Reste ouvert (non bloquant)

| # | Sujet | Où |
|---|---|---|
| **Q-P4-4-1** | re-preuve des 14 primitives contre l'installation **Windows native** (laboratoire Lot C) | recette **P4-11** |
| **RR4** (P4-5D-R) | essai à blanc de la reprise des dates civiles sur une **copie** de base SQLite de production | **avant tout déploiement** |

## 7. Effet

**P4-7** (outil SQLite → serveur, dépend de P4-5) et **P4-10** (multi-processus et résilience, dépend de P4-4 et
P4-5) sont **débloqués**. V1 multi-poste reste **NOT GO** (P4-7, P4-10, P4-11, P4-12).
