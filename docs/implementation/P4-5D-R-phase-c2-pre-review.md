# P4-5D-R Phase C2 Pre-review

> **Rapport de pré-revue — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`.
> Référence : [pré-revue Phase C](P4-5D-R-civil-date-migration-phase-c-pre-review.md) et sa revue architecte.
> Objet : **Q-C2** (bornes horaires de D-B1) et **Q-C5** (réconciliation de la spécification Phase B). Rien d'autre.

---

## 0. À arbitrer en priorité — Q-C6 *(TRANCHÉ — VALIDATED, consolidation finale du 21/09/2026)*

> **Tranché sans modification de code.** L'option retenue (`NormalizableReadable`) est validée : la valeur est lue
> par `Microsoft.Data.Sqlite`, aucun fuseau n'est appliqué, aucune date n'est inventée, et seule la partie civile
> `yyyy-MM-dd` est conservée. Voir [Architect Decisions](#architect-decisions) et le
> [rapport final P4-5D-R](P4-5D-R-final-implementation-report.md). L'analyse d'origine est conservée ci-dessous.

La demande classe `1985-03-15T12:30:45.12345678` parmi les valeurs **refusées** (`Unrepairable`). **Ce cas n'a pas été
appliqué tel quel**, parce qu'il contredit Q-C1, également validé :

- **Mesuré** : le lecteur réel `Microsoft.Data.Sqlite` **lit** cette valeur, comme le 15 mars. Elle relève donc de la
  branche **lisible**, que Q-C1 laisse hors du champ de la grammaire.
- La classer `Unrepairable` **refuserait le démarrage** sur une valeur que l'application lit correctement
  aujourd'hui. Cela contredirait aussi le sens de l'état (`Unrepairable` = illisible).
- **Comportement livré** : `NormalizableReadable` → `1985-03-15`, figé par
  `HuitDecimales_NeSontJamaisTronquees_MaisUneValeurDejaLueResteNormalisee`.
- La forme à espace, `1985-03-15 12:30:45.12345678`, est **illisible** : elle est bien `Unrepairable`, comme demandé.

La règle demandée est donc respectée : cette valeur n'est **pas** `RepairableLegacyDateTime`. Seul le résultat
attendu, `Unrepairable`, n'est pas atteint. Les options sont détaillées au §9.

---

## 1. SHA avant / après

| | SHA |
|---|---|
| Avant | `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60` |
| Après | `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60` — **inchangé** (aucun commit) |

Branches locales : `main`, `p4-multi-poste` — **aucune branche créée**. Aucun push.

---

## 2. Fichiers modifiés

Delta **de cette phase uniquement**.

| Fichier | + | − | Nature |
|---|---|---|---|
| `src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs` | 25 | 16 | Q-C2 : **une ligne** de grammaire, le reste en documentation |
| `tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs` | 128 | 10 | tests Q-C2, 1 test remplacé (§4.3) |
| `tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs` | 52 | 1 | intégration Q-C2, 1 donnée d'entrée corrigée (§4.3) |
| `docs/implementation/P4-5D-R-phase-b-addendum.md` | nouveau | — | Q-C5 |
| `docs/implementation/P4-5D-R-phase-c2-pre-review.md` | nouveau | — | ce rapport |
| **Total code + tests** | **205** | **27** | code : +25 / −16 · tests : +180 / −11 |

**Méthode de mesure.** Les fichiers `Time/*` ne sont pas suivis par git. `CivilDateFormat.cs` et `CivilDateFormatTests.cs`
ont été copiés hors dépôt **avant** toute modification. `SqliteCivilDateRepairTests.cs` a été reconstruit en retirant
les deux modifications. Chaque paire a ensuite été comparée par `git diff --no-index --numstat`.

**Le changement de règle tient en une ligne :**

```diff
- @"^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?\z"
+ @"^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T]([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?\z"
```

### Non modifiés

| Élément | Vérification | Résultat |
|---|---|---|
| `SqliteCivilDateRepairService.cs` | date de modification (13:29, avant cette phase), relecture | **inchangé** — il consomme `Classify` |
| `SqliteCivilDateFormatVerifier.cs`, `SqliteDatabaseManager.cs` | date de modification (15:18) ; `SqliteDatabaseManager` toujours **+95 / −1** contre `HEAD` | **inchangés** |
| `CivilDateRepairPreparationTests.cs` | date de modification (15:19) | **inchangé** |
| Spécification Phase B | date de modification (14:49) | **inchangée** — amendée par l'addendum |
| Migrations EF | `git status --short src/MMV.Infrastructure/Migrations` | **vide** |
| `.csproj` / `.sln` / CI / ADR | `git status --short '*.csproj' '*.sln' .github docs/adr` | **vide** |
| `DateOnly`, schéma SQLite, PostgreSQL | — | **non touchés** |
| Roadmap | `git diff --numstat` | toujours **+20 / −0** (V2) |

---

## 3. Décisions appliquées

### 3.1 Q-C2 — Bornes horaires

**Emplacement unique : `CivilDateFormat.IsLegacyDateTimeForm`**, appelée par `Classify` dans sa branche
**illisible**. Aucun second moteur. Le vérificateur, le service de reprise et le gestionnaire de base héritent de la
règle sans modification, ce que prouve le test d'intégration du §4.2.

| Élément | Avant (Phase C) | Après (C2) |
|---|---|---|
| `HH` | `[0-9]{2}` | `[01][0-9]\|2[0-3]` → 00 à 23 |
| `mm` | `[0-9]{2}` | `[0-5][0-9]` → 00 à 59 |
| `ss` | `[0-9]{2}` | `[0-5][0-9]` → 00 à 59 |
| Fraction | 1 à 7 chiffres | **inchangée** |
| Suffixe `Z` / `±HH:mm` | structure seule | **inchangé** (structure seule, jamais appliqué) |
| Transformation | `substring(0, 10)` | **inchangée** — aucune conversion UTC |

#### Effet mesuré

Sonde hors dépôt : 64 valeurs, classées avec la règle d'avant puis avec celle d'après, confrontées au lecteur réel
(`Microsoft.Data.Sqlite` 8.0.27, SQLite 3.50.3) et aux prédicats SQL support.

| Valeurs | Avant | Après |
|---|---|---|
| `… 24:00:00`, `… 25:00:00`, `… 30:00:00`, `… 99:99:99`, `… 12:60:00`, `… 12:99:00`, `… 12:00:60`, `… 12:00:99`, `… 23:59:60`, `… 24:00:00.0000000`, `… 12:00:60.5`, `… 24:00:00Z`, `… 12:60:00+01:00`, `… 12:00:60-05:00`, `…T24:00:00`, `…T12:60:00`, `…T12:00:60`, `…T23:59:60`, `…T24:00:00+01:00`, `2024-02-29 24:00:00` | `RepairableLegacyDateTime` | **`Unrepairable`** (20 valeurs) |
| `2026-02-30 24:00:00` | `Unrepairable` (date) | `Unrepairable` (date **et** heure) |
| `00:00:00`, `23:59:59`, `19:59:59`, `20:00:00`, `23:00:00`, `00:59:00`, `00:00:59`, `23:59:59.9999999`, suffixes `Z` / `±HH:mm`, `+99:99` | réécrites | **inchangé** |
| Toutes les valeurs lisibles, dont `…T12:30:45.12345678` | — | **inchangé** |

| Contrôle | Avant | Après |
|---|---|---|
| Divergences avec le lecteur réel | 0 / 64 | **0 / 64** |
| Violations de T-B1 avec `L` (spéc. §2.2) | 0 | **16** |
| Violations de T-B1 avec `L'` (addendum) | 0 | **0** |

**Seules des valeurs illisibles aux heures impossibles changent d'état.** Aucune valeur lisible n'est touchée.

#### Cas de la demande

| Valeur | Attendu | Obtenu |
|---|---|---|
| `1985-03-15 00:00:00` | `1985-03-15` | `RepairableLegacyDateTime` → `1985-03-15` ✅ |
| `1985-03-15 23:59:59` | `1985-03-15` | `RepairableLegacyDateTime` → `1985-03-15` ✅ |
| `1985-03-15T12:30:45.123` | `1985-03-15` | `NormalizableReadable` (déjà lue) → `1985-03-15` ✅ |
| `1985-03-15T12:30:45Z` | `1985-03-15` | `RepairableLegacyDateTime` → `1985-03-15` ✅ |
| `1985-03-15T12:30:45+01:00` | `1985-03-15` | `RepairableLegacyDateTime` → `1985-03-15` ✅ |
| `1985-03-15 24:00:00` | `Unrepairable` | `Unrepairable` ✅ |
| `1985-03-15 25:00:00` | `Unrepairable` | `Unrepairable` ✅ |
| `1985-03-15 12:60:00` | `Unrepairable` | `Unrepairable` ✅ |
| `1985-03-15 12:00:60` | `Unrepairable` | `Unrepairable` ✅ |
| `1985-03-15T12:30:45.12345678` | `Unrepairable` | **`NormalizableReadable`** ⚠️ — valeur **lue** par le lecteur réel → **Q-C6** (§0) |

### 3.2 Q-C5 — Documentation

Créé : [`P4-5D-R-phase-b-addendum.md`](P4-5D-R-phase-b-addendum.md), selon le plan demandé (*Purpose*, *D-B1 Final
Rule*, *D-B3 Final Rule*, *Decisions*). La spécification Phase B **n'est pas modifiée**. L'addendum liste ses
sections amendées : §1.2, §1.4, §1.5, §1.7, §2.1–2.6, §4.3 et §5.

Deux écarts que la pré-revue C n'avait pas relevés y sont consignés :

1. **Prédicat SQL support `L` (§2.2) : obsolète.** Il accepte `24:00:00`. Le script support tronquerait donc des
   valeurs que le moteur refuse désormais (16 violations mesurées). L'addendum le remplace par `L'`, mesuré à
   0 violation. Le script complet (§2.4–2.5) n'a pas été réexécuté avec `L'` : cela relève de T-B6.
2. **Option B de D-B1 (§5) : secondes facultatives** (`HH:mm[:ss…]`). La règle implémentée depuis la Phase C, et la
   règle Q-C2, les exigent : `1985-03-15 12:30` est refusé. L'addendum aligne le texte sur le code.

---

## 4. Tests ajoutés

**+37 cas** (120 → 157 sur les trois classes). Aucun test ignoré.

### 4.1 `CivilDateFormatTests` — règle pure (82 → 118)

| Test | Cas | Exigence |
|---|---|---|
| `UneHeureHorsBornes_EstRefusee_JamaisTronquee` | 16 | **Validation heure** : les 4 cas demandés (`24:00:00`, `25:00:00`, `12:60:00`, `12:00:60`), plus `23:59:60`, les séparateurs `T`, les fractions, les suffixes et `2024-02-29 24:00:00`. Vérifie aussi que la tête est valide : c'est bien l'heure seule qui refuse. |
| `LesBornesHorairesIncluses_RestentReparables` | 8 | **Non-régression** : `00:00:00`, `23:59:59` et les bornes de chaque branche (`19`/`20`, `:59`) restent `RepairableLegacyDateTime` |
| `LesCasAcceptesDeQ_C2_SontReecrits_AuJourEcrit` | 5 | les 5 cas acceptés de la demande → `1985-03-15` |
| `LesDecalages_AuxBornesHoraires_SontTronques_SansConversion` | 4 | **Offset** : `Z`, `+01:00`, `-05:00` aux deux extrémités de la journée, là où une conversion changerait le jour |
| `LeDecalage_NEstControleQueSurSaStructure` | 3 | fige l'interprétation « structure seule » (`+99:99` accepté) |
| `HuitDecimales_NeSontJamaisTronquees_MaisUneValeurDejaLueResteNormalisee` | 1 | forme à espace refusée ; forme `T` lue, donc normalisée (**Q-C6**) |

Les cas d'offset de la Phase C (`LesValeursAvecDecalage_SontTronquees_SansConversion`, 7 cas) sont **conservés à
l'identique**.

### 4.2 `SqliteCivilDateRepairTests` — SQLite en mémoire (26 → 27)

| Test | Exigence |
|---|---|
| `UneHeureHorsBornes_EstSignaleeEnBase_EtJamaisTronquee` | Q-C2 de bout en bout, sur les deux colonnes : `24:00:00`, `12:60:00` et `T12:00:60Z` sont signalés et laissés intacts ; `23:59:59`, `00:00:00+01:00` et `T23:59:59-05:00` sont repris au jour écrit ; D-B3 est vérifié sur ce nouveau motif de refus |

`CivilDateRepairPreparationTests` (12) : aucun ajout. Le refus au démarrage ne dépend pas du motif, puisque toute
valeur `Unrepairable` le déclenche, et ce chemin est déjà couvert.

### 4.3 Tests existants modifiés — 3, déclarés

| Test | Modification | Motif |
|---|---|---|
| `UneHeureInvalide_NEmpechePasLaReprise_DeLaPartieDate` | **retiré**, remplacé par `UneHeureHorsBornes_EstRefusee_JamaisTronquee` | il affirmait `24:00:00` → `RepairableLegacyDateTime`, soit exactement ce que Q-C2 abolit. L'assertion est **inversée**, pas affaiblie. |
| `UneBaseVolumineuse_EstReprise_IntegralementEtSansMelange` | donnée d'entrée : heure `{day:D2}` → `{day % 24:D2}`. Assertions inchangées. | le générateur produisait `24:15:00` à `28:15:00` pour les jours 24 à 28. L'objet du test est l'appariement ligne à ligne ; ces heures n'auraient jamais pu être écrites par `DateTime`. Q-C2 les refuse, à juste titre. |
| `LaClassification_EstFidele_AuLecteurReel` | corpus étendu de 9 valeurs (heures hors bornes, 8 décimales) | ajout pur. Il prouve que les valeurs refusées sont bien **illisibles**, donc qu'aucune valeur lue n'est bloquée. |

### 4.4 Pouvoir discriminant — mesuré, pas supposé

Les tests de `CivilDateFormatTests` et `SqliteCivilDateRepairTests` ont été exécutés contre l'**ancienne** règle.
Le fichier d'avant a été remis en place le temps de l'exécution, restauration garantie par `trap` et contrôlée par
SHA-256 :

```
Échoué!  - échec : 17, réussite : 128, total : 145
  → les 16 cas de UneHeureHorsBornes_EstRefusee_JamaisTronquee
  → UneHeureHorsBornes_EstSignaleeEnBase_EtJamaisTronquee
```

Exactement les tests de refus. Les tests de non-régression passent avec les deux règles, comme ils le doivent.
La solution a ensuite été entièrement reconstruite (`--no-incremental`) avant les mesures du §5.

---

## 5. Résultats build / tests

```
dotnet build MMV.sln -c Debug --no-incremental
→ La génération a réussi.  0 Avertissement(s)  0 Erreur(s)
```

```
dotnet test MMV.sln -c Debug
→ MMV.App.Tests          échec : 0, réussite :  258, ignorée(s) : 0, total :  258
→ MMV.Application.Tests  échec : 0, réussite :  628, ignorée(s) : 0, total :  628
→ MMV.Domain.Tests       échec : 0, réussite : 1029, ignorée(s) : 0, total : 1029
```

| Projet | Après Phase C | Après C2 | Échecs | Ignorés |
|---|---|---|---|---|
| `MMV.Domain.Tests` | 992 | **1 029** | 0 | 0 |
| `MMV.Application.Tests` | 628 | 628 | 0 | 0 |
| `MMV.App.Tests` | 258 | 258 | 0 | 0 |
| **Total** | 1 878 | **1 915** | **0** | **0** |

---

## 6. Migration status

| Contrôle | Commande | Résultat |
|---|---|---|
| Dérive de modèle EF | `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | `No changes have been made to the model since the last migration.` |
| Migrations créées / modifiées | `git status --short src/MMV.Infrastructure/Migrations` | **vide** |
| Schéma SQLite | — | **inchangé** (`TEXT`) |
| `DateOnly` | — | **conservé** |
| PostgreSQL | — | **0 fichier** |

---

## 7. `git status` final

```
 M docs/architecture/P4-multi-poste-roadmap.md                         (V2, inchangé : +20 / −0)
 M src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs                (Phase C, inchangé : +95 / −1 contre HEAD)
?? docs/architecture/P5-product-completion-roadmap.md                  (préexistant, non touché)
?? docs/implementation/P4-5D-R-civil-date-migration-phase-b-specification.md   (non modifiée)
?? docs/implementation/P4-5D-R-civil-date-migration-phase-c-pre-review.md
?? docs/implementation/P4-5D-R-civil-date-migration-pre-review-v2.md
?? docs/implementation/P4-5D-R-civil-date-migration-pre-review.md
?? docs/implementation/P4-5D-R-phase-b-addendum.md                     (nouveau — Q-C5)
?? docs/implementation/P4-5D-R-phase-c2-pre-review.md                  (nouveau — ce rapport)
?? docs/implementation/P4-5D-final-implementation-report.md            (préexistant, non touché)
?? src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs                 (Q-C2)
?? src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs
?? src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs
?? tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs             (Q-C2)
?? tests/MMV.Domain.Tests/Data/Time/CivilDateRepairPreparationTests.cs
?? tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs       (Q-C2)
```

---

## 8. Risques restants

### Nouveaux ou modifiés

| # | Risque | Statut |
|---|---|---|
| **RC1** | **Surface de refus encore élargie.** Après D-B1, Q-C2 bloque à son tour le démarrage sur toute heure impossible. Le pilote ne les écrit jamais, mais un import ou une saisie directe en base le peut. Le test `UneBaseVolumineuse…` en était un exemple involontaire. Le volume réel reste inconnu. | assumé par décision ; **RR4 (`dryRun` sur copie réelle) d'autant plus nécessaire** |
| **RC5** | **SQL support de la spécification obsolète.** Utilisé sans l'addendum, le prédicat `L` du §2.2 tronquerait des valeurs refusées par le moteur. Aucun test automatique ne garde encore cet invariant : T-B1 n'est pas réalisé. | atténué par l'addendum ; **T-B1 sur `L'` recommandé au prochain lot** |
| **RC6** | **Q-C6** : `…T12:30:45.12345678`, lu par le lecteur, est normalisé au lieu d'être refusé. | **clos** : Q-C6 VALIDATED, le comportement livré est confirmé |
| **RC7** | **Décalage non borné** : `+99:99` est accepté. Il est sans effet, puisque jamais appliqué, mais il signale une valeur probablement fabriquée. | assumé (« structure seule ») ; figé par test |
| RC2 | Empreinte non salée | **accepté temporairement** (Q-C3), P7 |
| RC3 | `RawValue` accessible en mémoire | inchangé, faible |
| RC4 | Spécification Phase B partiellement dépassée | **résolu** par l'addendum (Q-C5) |

### Reportés, inchangés

RR2 (incohérence de roadmap, Q6), RR3 (refus bloquant, assumé), **RR4 (`dryRun` sur base réelle, préalable au
déploiement, non exécutable depuis ce poste)**, RR5 (canonicalisation → P4-7), RR6 / D-B4 (multi-poste → P4-6),
RR7 (coût de démarrage), RR8 (règle du `Kind`, Q4). D-B2 inchangé.

Tests de la spécification encore à réaliser : T-B1 (désormais sur `L'`), T-B4 à T-B8.

---

## 9. Question architecturale

| # | Question | Option retenue | Alternatives |
|---|---|---|---|
| **Q-C6** | `1985-03-15T12:30:45.12345678` est **lu** par le lecteur réel (15 mars). Doit-il être refusé, comme le demande Q-C2, ou normalisé, comme le veut Q-C1 ? | **Normalisé** (`NormalizableReadable`). Aucun refus de démarrage sur une valeur que l'application lit aujourd'hui ; la branche lisible reste régie par Q-C1. | **(b)** `ReadableLeftAsIs` : laissée intacte, sans refus. Application de la grammaire à la branche lisible, soit l'alternative de Q-C1 limitée à la fraction. Une ligne de code, et le test `HuitDecimales…` s'inverse. **(c)** `Unrepairable` : refus du démarrage, comme demandé. Crée un état « lisible mais refusé » qui n'existe pas aujourd'hui, et bloque une base fonctionnelle. **Déconseillé.** |

---

## 10. Verdict

**Q-C2 est implémentée par une ligne de grammaire, dans `CivilDateFormat` seulement. Q-C5 est traitée par
l'addendum.** Build propre, **1 915 / 1 915**, aucune migration, aucun changement de schéma, de modèle, d'ADR, de
`.csproj` ou de CI. Le service de reprise n'est pas modifié.

**Commit = NO.** Aucun push, aucune branche.

**Statut : `READY FOR ARCHITECT REVIEW`**, avec une question ouverte : **Q-C6**.

---

## Architect Decisions

> Décision rendue par le Lead Software Architect lors de la consolidation finale (21 septembre 2026).

| # | Statut | Décision |
|---|---|---|
| **Q-C6** | **VALIDATED** | `1985-03-15T12:30:45.12345678` reste `NormalizableReadable` → `1985-03-15`. `Microsoft.Data.Sqlite` lit cette valeur ; il n'y a ni conversion de fuseau ni invention de date, et seule la partie civile `yyyy-MM-dd` est conservée. |

Cette décision confirme le comportement livré en C2 et **n'exige aucune ligne de code**. Le test
`HuitDecimales_NeSontJamaisTronquees_MaisUneValeurDejaLueResteNormalisee` la fige. La forme à espace,
`1985-03-15 12:30:45.12345678`, est illisible et reste `Unrepairable`.

L'[addendum Phase B](P4-5D-R-phase-b-addendum.md#decisions) porte la décision, et le
[rapport final P4-5D-R](P4-5D-R-final-implementation-report.md) clôt le lot.
