# P4-5D-R Civil Date Migration Preparation — V2

> **Rapport de revue V2 — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`.
> SHA de départ **et actuel** : **`023048fb2a9cd4cd2e22b173f0157a6f0c57ef60`** — **inchangé**.
>
> Ce document rend compte des **corrections appliquées après revue architecturale**. Il ne remplace pas
> [le rapport V1](P4-5D-R-civil-date-migration-pre-review.md), il le complète : le V1 a été **amendé sur
> place** pour y porter les décisions, conformément à la décision **C1**.

---

## Changes Applied

Deux modifications, **strictement documentaires**. **Aucun fichier `.cs` n'a été touché.**

| # | Décision | Fichier | Nature | Diff |
|---|---|---|---|---|
| **C1** | Porter les décisions architecte dans le rapport | `docs/implementation/P4-5D-R-civil-date-migration-pre-review.md` | **ajout** d'une section `## Architect Decisions`, insérée **entre** `## Architect Review Questions` et `## État final` | **+77 / −0** |
| **C2** | Tracer le lot dans la roadmap | `docs/architecture/P4-multi-poste-roadmap.md` | **ajout en fin de fichier** d'une section `## P4-5D-R Civil Date Migration Preparation` | **+20 / −0** |

**Le diff de la roadmap est strictement additif** — vérifié : `git diff --numstat` donne `20  0`, et la
recherche de lignes supprimées dans `git diff -U0` ne remonte **aucune ligne**. **Aucun statut de phase
existant n'a été modifié**, conformément à l'instruction : la réconciliation complète de la roadmap reste un
lot séparé.

### Ce qui n'a **pas** été modifié

Contrôlé fichier par fichier, conformément aux interdits du lot :

| Interdit | Vérification | Résultat |
|---|---|---|
| `SqliteDatabaseManager` — logique principale | `git status --short` | **inchangé depuis le V1** (`+89 / −1`, identique) |
| Détecteur de formats (`SqliteCivilDateFormatVerifier`) | idem | **inchangé** |
| Convertisseur / règle (`CivilDateFormat`) | idem | **inchangé** |
| Service de reprise (`SqliteCivilDateRepairService`) | idem | **inchangé** |
| Tests existants | idem | **aucun test modifié, supprimé, ignoré ou affaibli** |
| ADR | `git status --short docs/architecture/` | **0 fichier ADR** |
| Migrations EF | `git status --short src/MMV.Infrastructure/Migrations/` | **vide** |
| PostgreSQL | — | **0 fichier touché** |
| `DateOnly` / stratégie `DateTime` | — | **inchangées** |
| `.csproj` / `.sln` / paquets | — | **inchangés** |

**Aucune migration EF n'a été ajoutée. Aucune stratégie de réparation n'a été modifiée.**

---

## Architect Decisions

Décisions rendues par le Lead Software Architect et **portées dans le rapport V1** (section
`## Architect Decisions`).

### Q1 — Réparation automatique au démarrage — **VALIDÉ**

La réparation automatique au démarrage est **acceptée**, sous quatre conditions :

| Condition | Mise en œuvre existante | Preuve |
|---|---|---|
| **backup obligatoire** avant modification | `PrepareDatabase` sauvegarde le fichier avant toute mutation | `LaPreparation_SauvegardeLaBase_AvantDeLaReprendre` |
| **transaction obligatoire** | transaction unique dans `SqliteCivilDateRepairService` — tout ou rien | transaction explicite + `UneValeurNonReparable_…_SansRienModifier` |
| **journalisation** | `CIVILDATE ok` / `detected` / `repaired` / `REFUSED` au journal de migration | `LaReprise_EstJournalisee_AvantEtApres` |
| **possibilité `dryRun`** pour le support | `Repair(context, dryRun: true)` | `UneSimulation_NEcritRien_MaisRendLeMemeCompte` |

> **Les quatre conditions étaient déjà satisfaites par l'implémentation soumise à la revue.** La décision
> les **fige** ; elle n'a exigé **aucune ligne de code**.

### Q2 — Valeurs non réparables — **VALIDÉ**

Une valeur non réparable **bloque le démarrage**.
**Raison retenue :** une donnée métier invalide est préférable à une corruption silencieuse.

| Comportement exigé | Mise en œuvre existante | Preuve |
|---|---|---|
| **aucune modification appliquée** | refus en bloc **avant** toute écriture | `UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier` |
| **erreur explicite** | table, colonne, clé primaire et valeur brute nommées | `UneValeurIncomprehensible_EstSignaleeNommement_EtLaisseeIntacte` |
| **backup conservé** | sauvegarde préalable non supprimée en cas de refus | `LaSauvegarde_PermetDeRevenirEnArriere` |

> Comportement **déjà en place**. **Aucune ligne de code requise.** Le risque **R2** est **assumé par
> décision**, non éliminé : un refus bloque bien le lancement de l'application.

### Q3 — Valeurs lisibles mais non canoniques — **REPORTÉ**

Les valeurs lisibles mais non canoniques **ne sont pas modifiées dans ce lot**. La canonicalisation sera
traitée **avant l'export PostgreSQL**, en **P4-7 — PostgreSQL Export Validation**.

Conséquence assumée : `␣1985-03-15`, `1985-3-5`, `1985/03/15` restent en place, et **la base n'est pas
garantie intégralement canonique** après reprise. Sans effet sur SQLite, où ces valeurs sont lues
correctement. **Le risque R3 reste ouvert et est explicitement reporté.**

> ⚠️ **Un point d'interprétation subsiste — voir *Remaining Risks*, RR1.**

### Q7 — Mode support `dryRun` — **REPORTÉ**

Le **mode support complet** est renvoyé à une phase **P7 — Production Readiness**. Le `dryRun` reste
**atteignable par code uniquement** ; **aucune commande de maintenance n'a été ajoutée**.

### Questions non tranchées

| Question | Objet | État |
|---|---|---|
| **Q4** | Partage de la règle `Kind` (ADR-PROD-DB-004, décision 8) entre ce lot et P4-7 | **OUVERTE** |
| **Q5** | Rattachement de la concurrence multi-poste à P4-6 | **OUVERTE** |
| **Q6** | Réconciliation des états de roadmap (P4-5C et P4-5D portés `NOT STARTED` alors que commités) | **OUVERTE** — réconciliation complète différée à un lot séparé |

---

## Implementation Status

**Inchangé depuis le V1.** Cette passe n'a produit **aucune modification de code**.

| Fichier | Volume | État |
|---|---|---|
| `src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs` | 194 lignes | **ajouté** (V1) — inchangé |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs` | 256 lignes | **ajouté** (V1) — inchangé |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs` | 270 lignes | **ajouté** (V1) — inchangé |
| `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` | **+89 / −1** | **modifié** (V1) — inchangé |
| `tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs` | 36 tests | **ajouté** (V1) — inchangé |
| `tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs` | 21 tests | **ajouté** (V1) — inchangé |
| `tests/MMV.Domain.Tests/Data/Time/CivilDateRepairPreparationTests.cs` | 10 tests | **ajouté** (V1) — inchangé |

**Statut fonctionnel : complet.** Les quatre décisions rendues (Q1, Q2 validées ; Q3, Q7 reportées)
**correspondent au comportement déjà implémenté** — aucune ne demandait de changement.

---

## Tests Executed

Exécutés **après** les modifications documentaires, sur le SHA `023048f` + modifications locales.

```
dotnet build MMV.sln -c Debug
→ Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
dotnet test MMV.sln -c Debug
→ MMV.App.Tests          Failed: 0, Passed:  258, Skipped: 0, Total:  258
→ MMV.Application.Tests  Failed: 0, Passed:  628, Skipped: 0, Total:  628
→ MMV.Domain.Tests       Failed: 0, Passed:  939, Skipped: 0, Total:  939
```

| Projet | Tests | Échecs | Ignorés |
|---|---|---|---|
| `MMV.Domain.Tests` | **939** | 0 | 0 |
| `MMV.Application.Tests` | **628** | 0 | 0 |
| `MMV.App.Tests` | **258** | 0 | 0 |
| **Total** | **1 825** | **0** | **0** |

**Résultat : 1 825 / 1 825. 0 erreur, 0 avertissement, 0 test ignoré.**
Identique au V1 — attendu, puisque **aucun code n'a changé**.

---

## Migration Status

| Contrôle | Commande | Résultat |
|---|---|---|
| Migrations EF créées / modifiées / supprimées | `git status --short src/MMV.Infrastructure/Migrations/` | **vide — 0 fichier** |
| Dérive de modèle EF | `dotnet ef migrations has-pending-model-changes` (mesuré en V1) | **aucune** — `No changes have been made to the model` |
| Changement de schéma | — | **aucun** |
| `DateOnly` conservé | — | **oui**, aucun retour à `DateTime` |
| Fichiers PostgreSQL | — | **0 touché** |

**Aucune migration EF n'existe pour ce lot, et aucune n'est générable** : le schéma SQLite est identique
(`TEXT` avant et après). Seul le **format des valeurs** change, et il est corrigé par le service de reprise,
non par une migration.

---

## Roadmap Update

Fichier : [`docs/architecture/P4-multi-poste-roadmap.md`](../architecture/P4-multi-poste-roadmap.md).

**Ajout en fin de fichier, +20 / −0 :**

```markdown
## P4-5D-R Civil Date Migration Preparation

Status:
READY FOR ARCHITECT REVIEW

Objective:
Prepare safe migration of existing SQLite civil date values before deployment.

Scope:
Customer.BirthDate
Prescription.IssueDate

No EF migration created.
No schema change.
```

**Portée exacte de la modification :**

- **aucun statut de phase existant modifié** — P4-5A … P4-5G, P4-1 … P4-12 et le bloc « État officiel de P4 »
  sont **intacts** ;
- **la ligne `P4-5D-R` du tableau des sous-lots P4-5 n'a pas été touchée** : elle porte toujours
  `NOT STARTED — BLOQUE LE DÉPLOIEMENT SUR BASE EXISTANTE` ;
- **le bloc d'état de synthèse** (`P4-5D-R CIVIL DATE REPRISE = NOT STARTED …`) n'a pas été touché ;
- **placement** : la section a été ajoutée **en fin de document** plutôt qu'au milieu de la section
  « 2. Étapes suivantes », car un titre de niveau `##` inséré avant `### P4-6` aurait fait apparaître
  P4-6 … P4-12 comme des sous-sections de P4-5D-R. Le niveau de titre demandé a été **respecté à
  l'identique**.

> **Incohérence interne résultante, assumée et signalée** — voir *Remaining Risks*, RR2.

---

## Remaining Risks

### RR1 — Q3 : ambiguïté entre la décision et le comportement implémenté *(CLOS — 21/09/2026, Phase B)*

> **Clos sans modification de code.** La demande de Phase B classe explicitement `1985-03-15T00:00:00` et
> `1985-03-15T12:30:45.123` parmi les valeurs **à normaliser**, et `1985/03/15`, `1985-3-5` parmi les valeurs
> **laissées intactes**. Q3 vise donc **uniquement** `ReadableLeftAsIs` ; la réécriture neutre
> `NormalizableReadable` est confirmée. Voir
> [spécification Phase B, §1.3](P4-5D-R-civil-date-migration-phase-b-specification.md#13-table-de-vérité--cas-de-la-demande-mesurés).
> Analyse d'origine conservée ci-dessous.

La décision Q3 énonce : « les valeurs **lisibles mais non canoniques** ne sont pas modifiées dans ce lot ».

L'implémentation soumise distingue **deux** familles de valeurs lisibles non canoniques :

| Famille | Exemple | Comportement implémenté |
|---|---|---|
| `NormalizableReadable` — réécriture **démontrablement neutre** (même date avant et après) | `1985-03-15T12:30:45.123` → `1985-03-15` | **réécrite** |
| `ReadableLeftAsIs` — réécriture **non neutre** | `␣1985-03-15`, `1985-3-5`, `1985/03/15` | **laissée intacte** |

Lue à la lettre, la décision Q3 couvrirait **les deux** familles et impliquerait de **supprimer la réécriture
`NormalizableReadable`**. Lue comme une confirmation de **R3**, elle ne vise que `ReadableLeftAsIs`, et
l'implémentation est déjà conforme.

**Aucune modification n'a été faite**, parce que la règle de processus du lot est explicite :
« **Aucune modification de stratégie de réparation** » et « ne pas modifier le convertisseur ».

> **Arbitrage demandé :** la décision Q3 confirme-t-elle le comportement actuel (seul `ReadableLeftAsIs` est
> préservé), ou exige-t-elle de retirer aussi la réécriture neutre `NormalizableReadable` ? Dans le second
> cas, un lot de correction dédié est nécessaire — il toucherait `CivilDateFormat`, le service de reprise et
> les tests associés.

### RR2 — Roadmap : incohérence interne introduite par C2 *(nouveau — voulu)*

La roadmap affirme désormais **deux choses différentes** sur le même lot :

- tableau des sous-lots P4-5 et bloc d'état de synthèse : `P4-5D-R = NOT STARTED` ;
- nouvelle section de fin de fichier : `Status: READY FOR ARCHITECT REVIEW`.

C'est le résultat **direct** de l'instruction « ajouter uniquement une trace documentaire » + « ne pas
modifier les autres statuts de phases ». **Cette incohérence se résoudra à la réconciliation de roadmap**
annoncée comme lot séparé (Q6, toujours ouverte).

### RR3 — Q2 : le refus bloque le démarrage *(assumé par décision)*

Statut : **décidé, non éliminé**. Une seule date de naissance illisible empêche le lancement de
l'application. L'ancien risque **R2** est désormais un **comportement validé**, pas un risque ouvert — mais
son **impact d'exploitation dépend entièrement de RR4**.

### RR4 — Volume réel de valeurs `Unrepairable` : toujours inconnu *(inchangé — R6)*

**Aucune base de production n'a été inspectée** (aucune n'est accessible depuis ce poste). Avec Q2 validé,
c'est **le risque d'exploitation n° 1** : une seule valeur illisible en base réelle = démarrage bloqué au
déploiement.

> **La simulation `dryRun` sur une copie de base réelle reste un préalable obligatoire au déploiement.**
> Elle n'a **pas** été exécutée — elle ne peut pas l'être depuis ce poste.

### RR5 — Canonicalisation reportée à P4-7 *(inchangé — R3, désormais daté)*

La base ne sera pas intégralement canonique à l'issue de ce lot. Sans effet sur SQLite. **Échéance fixée par
la décision Q3 : avant l'export PostgreSQL (P4-7).**

### RR6 — Concurrence multi-poste *(inchangé — R4, Q5 ouverte)*

`PrepareDatabase` s'exécute **par poste**. La reprise est transactionnelle et idempotente, donc **sûre** en
concurrence, mais la sérialisation relève de **P4-6**. **Q5 n'est pas tranchée.**

### RR7 — Coût de démarrage *(inchangé — R5)*

Le diagnostic lit toutes les lignes de `Customers` et `Prescriptions` à chaque lancement. Négligeable à
l'échelle d'un magasin ; **à mesurer** sur une base centrale volumineuse.

### RR8 — Règle du `Kind` pour les 19 colonnes d'instants *(inchangé — Q4 ouverte)*

Ce lot ne traite que les **deux dates civiles**. Le traitement des instants importés (ADR-PROD-DB-004,
décision 8) **reste entier pour P4-7**. **Q4 n'est pas tranchée.**

---

## Ready For Final Approval

### Contrôle de conformité au lot

| Exigence du lot | Résultat |
|---|---|
| C1 — décisions ajoutées au rapport | ✅ section `## Architect Decisions` dans le V1 |
| C2 — trace documentaire en roadmap | ✅ section ajoutée, **+20 / −0** |
| Aucun autre statut de phase modifié | ✅ diff strictement additif, vérifié |
| Aucune nouvelle fonctionnalité | ✅ **0 fichier `.cs` touché** |
| Aucune modification de stratégie de réparation | ✅ |
| Aucune migration EF ajoutée | ✅ `src/MMV.Infrastructure/Migrations/` vide au `git status` |
| `DateOnly` / stratégie `DateTime` inchangés | ✅ |
| ADR inchangées | ✅ |
| PostgreSQL non touché | ✅ |
| Build `0 erreur / 0 warning` | ✅ |
| Tests | ✅ **1 825 / 1 825**, 0 ignoré |
| **Commit** | **NON** |
| **Push** | **NON** |
| **Branche créée** | **NON** |
| **HEAD** | **`023048fb2a9cd4cd2e22b173f0157a6f0c57ef60` — inchangé** |

### Fichiers touchés par cette passe

```
 M  docs/architecture/P4-multi-poste-roadmap.md                          (+20 / −0)
 ?? docs/implementation/P4-5D-R-civil-date-migration-pre-review.md       (amendé — fichier non suivi)
 ?? docs/implementation/P4-5D-R-civil-date-migration-pre-review-v2.md    (ce rapport)
```

### Verdict

**Les deux décisions architecturales C1 et C2 sont appliquées, et rien d'autre.**
Le lot est **techniquement prêt** : build propre, suite verte, aucun effet de bord.

**Deux points requièrent un arbitrage avant approbation finale :**

1. **RR1** — l'interprétation de la décision **Q3** vis-à-vis de la famille `NormalizableReadable`. C'est le
   seul point susceptible d'entraîner une modification de code.
2. **RR4** — l'exécution de la simulation `dryRun` sur une **copie de base réelle**, préalable obligatoire au
   déploiement, **impossible depuis ce poste**.

**Statut : `READY FOR ARCHITECT REVIEW`. En attente de validation.**

---

## Addendum — Revue A2 et préparation Phase B (21 septembre 2026)

**Revue architecte A2 : APPROUVÉE.** Décisions confirmées : aucune migration EF, aucun changement de schéma,
reprise du **format des valeurs** uniquement, périmètre `Customer.BirthDate` / `Prescription.IssueDate`.

**Livrable :** [P4-5D-R-civil-date-migration-phase-b-specification.md](P4-5D-R-civil-date-migration-phase-b-specification.md)
— algorithme, SQL support (détection / transformation / vérification), règles de sûreté, tests.
**Documentation uniquement** : aucun fichier `.cs`, aucune migration, aucun changement EF.

### Mise à jour des risques

| Risque | Avant | Après Phase B |
|---|---|---|
| **RR1** | à trancher | **CLOS** — Q3 vise uniquement `ReadableLeftAsIs` |
| **RR4** | `dryRun` sur base réelle non exécuté | **inchangé** — procédure outillée (spéc. §2.3, §6), toujours préalable au déploiement |
| RR2, RR3, RR5–RR8 | — | inchangés |

### Nouveaux points à trancher (spéc. §5)

| # | Objet | Recommandation |
|---|---|---|
| **D-B1** | La troncature reprend aussi des formes hors liste validée (`1985-03-15garbage`, `12:30`, décalages) — **mesuré** | **restreindre** à une grammaire explicite ; suffixe inconnu ⇒ `Unrepairable` |
| **D-B2** | `""` sur `BirthDate` ⇒ refus du démarrage | **conserver le refus**, mesurer l'occurrence réelle (RR4) |
| **D-B3** | Dates de naissance brutes au journal `CIVILDATE REFUSED` | conserver pour les seules `Unrepairable`, rétention à fixer en P7 |
| **D-B4** | Sauvegarde non atomique en multi-poste | tous postes fermés au premier démarrage, jusqu'à P4-6 |

### Mesures nouvelles

- Prédicat SQL strict confronté à `CivilDateFormat.Classify` sur 37 valeurs : **0 divergence**.
- Script SQL support exécuté (sonde hors dépôt) : simulation par `ROLLBACK` sans effet, exécution
  5 réécritures / 0 écart / 0 perte, seconde exécution **0** réécriture.

**Statut : Phase B `SPECIFIED — READY FOR ARCHITECT REVIEW`. Implémentation non démarrée.**
