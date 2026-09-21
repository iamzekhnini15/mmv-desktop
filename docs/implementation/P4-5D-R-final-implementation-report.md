# P4-5D-R Final Implementation Report

> **Rapport de clôture — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`. HEAD : `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60`
> (P4-5D), **inchangé** depuis le début du lot.
> Statut : **`READY FOR COMMIT`**, sous réserve de la validation architecte.
>
> Documents du lot : [pré-revue V1](P4-5D-R-civil-date-migration-pre-review.md) ·
> [V2](P4-5D-R-civil-date-migration-pre-review-v2.md) ·
> [spécification Phase B](P4-5D-R-civil-date-migration-phase-b-specification.md) ·
> [addendum Phase B](P4-5D-R-phase-b-addendum.md) ·
> [pré-revue Phase C](P4-5D-R-civil-date-migration-phase-c-pre-review.md) ·
> [pré-revue Phase C2](P4-5D-R-phase-c2-pre-review.md).
> **Le code C# et ses tests font foi**, puis l'addendum, puis la spécification.

---

## Objective

Lever la **limitation n° 1 de P4-5D** ([rapport P4-5D](P4-5D-final-implementation-report.md)) : le passage de
`Customer.BirthDate` et `Prescription.IssueDate` à `DateOnly` rend **illisible toute base SQLite antérieure à
P4-5D** (`FormatException` à la lecture), sans qu'aucun outil ne le signale.

La cause est un changement de **format des valeurs**, pas de schéma : la colonne reste `TEXT`, EF ne voit aucune
dérive, et aucune migration n'est générable. La reprise porte donc sur les valeurs, sous quatre contraintes :

1. **Aucune migration EF, aucun changement de schéma ni de modèle.** `DateOnly` est conservé.
2. **Troncature, jamais conversion.** Aucun fuseau n'est appliqué, aucune date n'est recalée ni inventée.
3. **Refuser plutôt que deviner.** Une valeur de sens incertain bloque le démarrage, et rien n'est modifié.
4. **Aucune date en clair au journal.** Une date de naissance est une donnée personnelle, et une date
   d'ordonnance une donnée de santé indirecte.

Périmètre : `Customers.BirthDate` (nullable) et `Prescriptions.IssueDate` (NOT NULL). Les 19 colonnes d'instants
sont hors périmètre.

---

## Timeline

| Phase | Date | Nature | Livrable | Tests (cumul) |
|---|---|---|---|---|
| **Phase A** | 18/09, approuvée le 21/09 (A2) | code + doc | règle, diagnostic, reprise, branchement au démarrage | 1 758 → **1 825** (+67) |
| **Phase B** | 21/09 | doc seule | spécification exécutable, SQL support, tests T-B1 à T-B8, décisions D-B1 à D-B4 | 1 825 |
| **Phase C** | 21/09 | code | D-B1 (grammaire de troncature), D-B3 (journal sans valeur brute) | → **1 878** (+53) |
| **Phase C2** | 21/09 | code + doc | Q-C2 (bornes horaires), Q-C5 (addendum) | → **1 915** (+37) |
| Consolidation | 21/09 | doc seule | Q-C6 tranchée, décisions portées, ce rapport | 1 915 |

### Phase A — Implémentation initiale et décisions de principe

**Découverte structurante** : toutes les réparations de données antérieures (R-19, P2A-1E, P3-2B) passent par
l'adoption de base historique, rattachée à une migration EF réelle. Ici, **aucune migration n'existe**. Une base
installée après P2A-1A et avant P4-5D, le cas le plus répandu en exploitation, n'emprunte jamais ce chemin. La
reprise est donc placée dans `PrepareDatabase`, **hors du `switch` d'état**, et s'exécute quel que soit le chemin.

Deux mesures ont décidé de la règle :

- certaines valeurs non canoniques sont **déjà lues** (`␣1985-03-15`, `1985-3-5`). Une troncature naïve les
  aurait cassées en silence (`␣1985-03-15` → `␣1985-03-1`, soit le 1er mars). La lisibilité se teste donc en
  premier ;
- `date('2026-02-30')` renvoie `2026-03-02` dans SQLite. La fonction `date()` est proscrite en écriture.

Le V2 a porté les décisions Q1, Q2, Q3 et Q7 dans le rapport V1 et ajouté une trace en roadmap (+20 / −0). Il n'a
touché aucune ligne de code.

### Phase B — Spécification

La spécification fige l'algorithme (arbre de décision, table de vérité mesurée), le SQL support (prédicats `L` et
`C`, script de détection, transformation et vérification), les règles de sûreté et huit tests à réaliser. Elle a
mesuré que la règle d'alors **tronquait aussi des formes non validées** (`1985-03-15garbage`, `12:30`), ce qui a
motivé D-B1. RR1 (interprétation de Q3) y est clos : Q3 ne vise que `ReadableLeftAsIs`.

### Phase C — D-B1 et D-B3

- **D-B1** : la troncature d'une valeur illisible n'est accordée qu'à la **forme exacte** d'un `DateTime`
  historique. Dix-sept formes jusque-là reprises deviennent `Unrepairable`.
- **D-B3** : `CivilDateOffendingValue.ToString()` ne rend plus que la clé et l'empreinte SHA-256. Le message
  d'exception suit la même règle, car il est recopié au journal par `LegacyDatabaseRecoveryService`.

Cinq questions en sont issues (Q-C1 à Q-C5).

### Phase C2 — Bornes horaires et réconciliation documentaire

- **Q-C2** : **une ligne** de grammaire. `HH` de 00 à 23, `mm` et `ss` de 00 à 59. Vingt valeurs illisibles aux
  heures impossibles passent à `Unrepairable`. Aucune valeur lisible n'est touchée (0 divergence sur 64 valeurs
  confrontées au lecteur réel).
- **Q-C5** : l'addendum aligne la spécification sans la réécrire, et remplace le prédicat SQL `L` par `L'`.

Une question en est issue : **Q-C6**, tranchée lors de la consolidation.

---

## Final Architecture Decisions

### Décisions de la Phase C, toutes tranchées

| # | Statut | Décision |
|---|---|---|
| **Q-C1** | **VALIDATED** | Les valeurs déjà lisibles par `Microsoft.Data.Sqlite` peuvent être normalisées si la transformation est déterministe : `1985-03-15T12:30` → `1985-03-15`. Mise en œuvre : la troncature n'est appliquée que si elle est **neutre**, c'est-à-dire que la date lue avant et après est la même. |
| **Q-C2** | **VALIDATED** | Heures bornées : `HH` de 00 à 23, `mm` et `ss` de 00 à 59. Les valeurs impossibles sont `Unrepairable`. |
| **Q-C3** | **ACCEPTED TEMPORARILY** | SHA-256 non salé acceptable dans ce lot. La protection renforcée relève de **P7** (Security Hardening). |
| **Q-C4** | **VALIDATED** | `BirthDate` et `IssueDate` suivent la même politique. |
| **Q-C5** | **VALIDATED** | Documentation Phase B alignée par l'[addendum](P4-5D-R-phase-b-addendum.md). |
| **Q-C6** | **VALIDATED** | `1985-03-15T12:30:45.12345678` reste `NormalizableReadable` → `1985-03-15`. `Microsoft.Data.Sqlite` lit cette valeur, il n'y a ni conversion de fuseau ni invention de date, et seule la partie civile `yyyy-MM-dd` est conservée. |

**Q-C2 et Q-C5 ont inversé l'option recommandée** dans la pré-revue C, qui retenait un contrôle structurel et
aucune réconciliation documentaire. Les deux inversions sont implémentées en C2.

### Décisions antérieures, rappel

| # | Phase | Statut | Décision |
|---|---|---|---|
| Q1 | A | VALIDÉ | Reprise automatique au démarrage, avec sauvegarde, transaction, journal et `dryRun` |
| Q2 | A | VALIDÉ | Une valeur non réparable **bloque le démarrage** : aucune modification, erreur explicite, sauvegarde conservée |
| Q3 | A | REPORTÉ → P4-7 | Les valeurs `ReadableLeftAsIs` ne sont pas modifiées dans ce lot |
| Q7 | A | REPORTÉ → P7 | Commande support `dryRun` : atteignable par code seulement |
| D-B1 | B | VALIDÉ (option B) | Troncature restreinte à une grammaire explicite |
| D-B2 | B | comportement conservé | `""` sur `BirthDate` reste `Unrepairable`, donc refusé |
| D-B3 | B | VALIDÉ | Aucune valeur brute au journal ni dans les messages |
| D-B4 | B | rattaché à P4-6 | Sauvegarde non atomique en multi-poste |

### Questions toujours ouvertes (hors lot)

| # | Objet | État |
|---|---|---|
| Q4 | Règle du `Kind` pour les 19 colonnes d'instants (ADR-PROD-DB-004, décision 8) | ouverte → P4-7 |
| Q5 | Sérialisation multi-poste de `PrepareDatabase` | ouverte → P4-6 |
| Q6 | Réconciliation des états de roadmap | ouverte → lot séparé |

---

## Implemented Changes

### Code de production

| Fichier | État | Volume | Rôle |
|---|---|---|---|
| `src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs` | nouveau | 242 lignes | **La règle** : fonction pure, sans base, classification en six états |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs` | nouveau | 292 lignes | **Le diagnostic** : lecture seule du `TEXT` brut en ADO, sous le modèle EF ; `CivilDateOffendingValue` masquée |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs` | nouveau | 270 lignes | **La reprise** : transactionnelle, idempotente, simulable (`dryRun`) |
| `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` | modifié | **+95 / −1** | branchement `RepairCivilDateFormats` hors du `switch`, refus, journal, contrôle d'après-coup |

**Une seule règle.** Le diagnostic, la reprise et le gestionnaire de base appellent tous `CivilDateFormat.Classify`.
Il n'existe aucun second moteur. Le SQL de la spécification est un outil support, au périmètre plus restreint.

**Point d'insertion** :

```
PrepareDatabase
  ├─ BACKUP du fichier                           ← avant toute mutation (existant)
  ├─ détection d'état
  ├─ fresh install | migrations | adoption      ← schéma
  └─ RepairCivilDateFormats                     ← P4-5D-R, hors du switch : tous les chemins
       ├─ diagnostic (dryRun)
       ├─ ≥ 1 Unrepairable → CIVILDATE REFUSED + DatabaseMigrationException, rien n'est écrit
       ├─ rien à reprendre → CIVILDATE ok
       ├─ reprise en une transaction → CIVILDATE detected / repaired
       └─ contrôle d'après-coup → CIVILDATE FAILURE si une valeur reste illisible
```

### Tests

| Fichier | Volume | Tests |
|---|---|---|
| `tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs` | 618 lignes | 118 |
| `tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs` | 627 lignes | 27 |
| `tests/MMV.Domain.Tests/Data/Time/CivilDateRepairPreparationTests.cs` | 442 lignes | 12 |

### Documentation

Sept documents dans `docs/implementation/` (V1, V2, spécification Phase B, pré-revue C, addendum, pré-revue C2,
ce rapport), et la roadmap P4 (**+20 / −0**, section `P4-5D-R` ajoutée en V2).

**Consolidation finale** : aucune ligne de code. Q-C6 est portée dans l'addendum (décisions, §A.5, §A.6), et une
section `Architect Decisions` est ajoutée aux pré-revues C et C2. Leur analyse d'origine est conservée.

### Non modifiés

Migrations EF, snapshot de modèle, schéma SQLite, entités, `DateOnly`, fichiers PostgreSQL, ADR, `.csproj`, `.sln`,
CI, P4-5E. Aucun fichier de test déjà commité n'est modifié.

---

## Data Repair Rules

### Arbre de décision

L'ordre est la garantie de sûreté. Implémentation : `CivilDateFormat.Classify`.

```
valeur NULL                                              → NotApplicable            : rien
yyyy-MM-dd exact                                         → Canonical                : rien
lisible ET troncature valide ET même date qu'à la lecture → NormalizableReadable     : écrire les 10 premiers caractères
lisible (sinon)                                          → ReadableLeftAsIs         : rien, compté à l'audit (Q3 → P4-7)
illisible ET forme historique ET tête valide             → RepairableLegacyDateTime : écrire les 10 premiers caractères
illisible (sinon)                                        → Unrepairable             : rien, REFUS du démarrage (Q2)
```

« Lisible » signifie lu sans erreur par le lecteur réel (`DateOnly.TryParse`, culture invariante). La
concordance entre la règle et `Microsoft.Data.Sqlite` est vérifiée par test, verdict **et** date produite.

### Grammaire de la forme historique (D-B1, Q-C2)

```
yyyy-MM-dd[ T]HH:mm:ss[.f{1,7}][Z|±HH:mm]
^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T]([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?\z
```

- Heure de `00:00:00` à `23:59:59`, secondes obligatoires, fraction de 1 à 7 chiffres.
- Suffixe `Z` ou `±HH:mm` contrôlé sur sa **structure seule** : il n'est jamais appliqué.
- `[0-9]` plutôt que `\d`, qui accepte des chiffres non ASCII ; `\z` plutôt que `$`, qui accepte un saut de ligne
  final.
- La tête doit être une date calendaire valide (`TryParseExact`). `2026-02-30` est refusé, jamais recalé.

### Transformation

**`substring(0, 10)`**, validée avant écriture. L'heure est perdue volontairement, car une date civile n'a pas
d'heure (ADR-PROD-DB-004 §2.4). Le suffixe disparaît avec l'heure, et le jour retenu est le **jour écrit** :
`1985-03-15T23:59:59-05:00` donne `1985-03-15`, jamais le 16. Le `Kind` historique ne laisse aucune trace dans le
`TEXT` : toute conversion serait une invention.

### Exemples

| Valeur stockée | État | Résultat | Décision |
|---|---|---|---|
| `1985-03-15 00:00:00`, `1985-03-15 23:59:59` | `RepairableLegacyDateTime` | `1985-03-15` | D-B1, Q-C2 |
| `1985-03-15 12:30:45.1234567` | `RepairableLegacyDateTime` | `1985-03-15` | D-B1 |
| `1985-03-15T12:30:45Z`, `…T12:30:45+01:00` | `RepairableLegacyDateTime` | `1985-03-15` | D-B1 |
| `1985-03-15T12:30`, `1985-03-15T12:30:45.123` | `NormalizableReadable` | `1985-03-15` | Q-C1 |
| `1985-03-15T12:30:45.12345678` | `NormalizableReadable` | `1985-03-15` | Q-C6 |
| `␣1985-03-15`, `1985-3-5`, `1985/03/15` | `ReadableLeftAsIs` | inchangée | Q3 |
| `1985-03-15 24:00:00`, `… 12:60:00`, `… 12:00:60`, `… 23:59:60` | `Unrepairable` | refus | Q-C2 |
| `1985-03-15 12:30:45.12345678`, `1985-03-15 12:30` | `Unrepairable` | refus | D-B1 |
| `1985-03-15garbage`, `…+0100`, `…z`, `…\n` | `Unrepairable` | refus | D-B1 |
| `2026-02-30 00:00:00`, `abc`, `""`, `15/03/1985` | `Unrepairable` | refus | spéc. §1.3, §1.5 ; D-B2 pour `""` |
| `NULL` | `NotApplicable` | inchangée | — |

### Garanties d'exécution

| Garantie | Mise en œuvre |
|---|---|
| Sauvegarde | prise par `PrepareDatabase` **avant** toute mutation, jamais supprimée |
| Refus en bloc | une seule valeur `Unrepairable` ⇒ aucune écriture, `DatabaseMigrationException` |
| Atomicité | une transaction unique couvre les deux colonnes |
| Contrôle d'après-coup | une valeur encore illisible après reprise ⇒ échec, pas de succès affirmé |
| Idempotence | la seconde exécution ne réécrit rien |
| Simulation | `Repair(context, dryRun: true)` : aucune écriture, mêmes compteurs |
| Périmètre | aucune autre colonne modifiée, en particulier aucun instant |
| Retour arrière | `SqliteDatabaseManager.Restore` depuis la sauvegarde |

### SQL support

Le prédicat `L` de la spécification (§2.2) est **obsolète** : il accepte `24:00:00`. Seul `L'` (addendum §A.7)
reste utilisable. Mesuré sur 64 valeurs : `L'` donne 0 violation de l'invariant `L'(x) ⇒ réécrite par le moteur`,
contre 16 pour `L`.

---

## Logging Policy

### Lignes du journal de migration

| Ligne | Quand | Contenu |
|---|---|---|
| `BACKUP created '<chemin>'` | avant toute mutation | chemin de la sauvegarde |
| `CIVILDATE ok: …` | rien à reprendre | constat explicite, y compris sur une base saine |
| `CIVILDATE detected: <n> value(s) to rewrite (<m> unreadable by the current model) — repairing.` | avant écriture | compteurs |
| `CIVILDATE repaired: civil dates: rewritten=… (truncated=…, normalized=…), unrepairable=…, dryRun=…, applied=…` | après validation | compteurs mesurés |
| `CIVILDATE REFUSED: <n> unrepairable value(s) (raw values withheld): <désignations>` | refus Q2 | 20 premières désignations |
| `CIVILDATE FAILURE: <n> value(s) still unreadable after repair.` | contrôle d'après-coup | compteur |

### Aucune valeur brute (D-B3)

Une valeur refusée est désignée par sa table, sa colonne, sa clé primaire et l'empreinte de la valeur stockée :

```
Customers.BirthDate CustomerId=123 BirthDateHash=sha256:<64 hex>
Prescriptions.IssueDate PrescriptionId=45 IssueDateHash=sha256:<64 hex>
```

- **Deux chemins couverts.** Le message de `DatabaseMigrationException` suit la même règle. Il est recopié au
  journal par `LegacyDatabaseRecoveryService` (`LEGACY copy adoption FAILED: {ex.Message}`) et en sortie de débogage
  par `App.axaml.cs`.
- **Empreinte** : `sha256:` suivi des 64 chiffres hexadécimaux minuscules du SHA-256 de la valeur en UTF-8. Le
  support la reproduit avec `sha256sum` pour confirmer que la valeur lue en base par la clé est bien celle qui a
  été refusée. Vecteur de référence figé par test (chaîne vide → `e3b0c442…b855`).
- **Portée** : `BirthDate` et `IssueDate` (Q-C4).
- **En mémoire seulement** : `CivilDateOffendingValue.RawValue`, pour le diagnostic par code. Un commentaire
  interdit de la journaliser.

### Limite de sécurité (Q-C3)

L'empreinte est une **pseudonymisation faible**, pas une anonymisation. Une date de naissance a environ 55 000
valeurs possibles sur 150 ans : son SHA-256 non salé se retrouve par dictionnaire. Il protège contre la lecture
directe du journal (fichier partagé, capture d'écran, ticket), pas contre une personne qui cherche à retrouver
la date. Accepté temporairement, HMAC à clé d'installation prévu en P7.

---

## Tests

### Résultats

| Projet | P4-5D | Phase A | Phase C | **Phase C2 = final** | Échecs | Ignorés |
|---|---|---|---|---|---|---|
| `MMV.Domain.Tests` | 872 | 939 | 992 | **1 029** | 0 | 0 |
| `MMV.Application.Tests` | 628 | 628 | 628 | **628** | 0 | 0 |
| `MMV.App.Tests` | 258 | 258 | 258 | **258** | 0 | 0 |
| **Total** | 1 758 | 1 825 | 1 878 | **1 915** | **0** | **0** |

**+157 tests pour le lot**, tous dans `MMV.Domain.Tests` : 118 + 27 + 12, recomptés classe par classe lors de la
consolidation.

### Couverture par exigence

| Exigence | Tests principaux |
|---|---|
| Troncature historique, jamais de décalage de jour | `UneDateHistorique_EstReprise_ParTroncature`, `LaReprise_NeDecaleJamaisLeJour`, `ChaqueFormatHistorique_EstRepris_SansDecalageDeJour` |
| Fidélité au lecteur réel | `LaClassification_EstFidele_AuLecteurReel` |
| Lisibles préservées (Q3) | `UneValeurLisible_QueLaTroncatureCasserait_EstLaisseeIntacte`, `LaRepriseNaive_EnSqlDeMasse_ChangeraitSilencieusementUneDate` |
| Aucun recalage de calendrier | `UneDateInexistanteAuCalendrier_EstRefusee_JamaisRecalee`, `SqliteRecaleraitSilencieusement_CeQueLaRegleRefuse` |
| Décalages sans conversion (T-B2) | `LesValeursAvecDecalage_SontTronquees_SansConversion`, `UneConversionUtc_AuraitChangeLeJour_CeQueLaRegleNeFaitPas`, `LesDecalages_AuxBornesHoraires_SontTronques_SansConversion` |
| Suffixe inconnu refusé (T-B3, D-B1) | `UnSuffixeInconnu_EstRefuse_JamaisTronque`, `UnSuffixeInconnu_EstSignaleEnBase_EtJamaisTronque`, `UnSuffixeInconnu_RefuseLeDemarrage_SansJournaliserLaDate` |
| Bornes horaires (Q-C2) | `UneHeureHorsBornes_EstRefusee_JamaisTronquee`, `LesBornesHorairesIncluses_RestentReparables`, `UneHeureHorsBornes_EstSignaleeEnBase_EtJamaisTronquee` |
| Lisibles normalisées (Q-C1, Q-C6) | `D_B1_NeRestreintPas_LaMiseEnFormeNeutre_DUneValeurDejaLue`, `HuitDecimales_NeSontJamaisTronquees_MaisUneValeurDejaLueResteNormalisee` |
| Journal sans valeur brute (D-B3, Q-C4) | `UneValeurNonReparable_SeJournalise_SansSaValeurBrute`, `LeJournalDeRefus_PermetDeRetrouverEtDeConfirmerChaqueValeur` |
| Reprise au démarrage, hors adoption | `UneBaseGereeParMigrations_MaisAnterieureAP45D_EstRepriseAuDemarrage`, `UneBaseHistoriqueAdoptee_EstAussiReprise` |
| Sauvegarde, refus, retour arrière | `LaPreparation_SauvegardeLaBase_AvantDeLaReprendre`, `UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier`, `LaSauvegarde_PermetDeRevenirEnArriere` |
| Idempotence, `dryRun` | `DeuxExecutionsSuccessives_DonnentLeMemeResultat`, `UneSimulation_NEcritRien_MaisRendLeMemeCompte` |
| Aucune autre colonne touchée | `LaReprise_NeToucheAucuneColonneDInstant`, `LaReprise_NeModifieAucuneAutreColonne_NiAucunInstant` |

### Pouvoir discriminant

Les tests de C et C2 ont été exécutés contre la règle d'avant leur phase. En C2, avec l'ancienne grammaire remise en
place le temps de l'exécution : **17 échecs**, exactement les tests de refus. Les tests de non-régression passent
avec les deux règles. Détail dans les pré-revues [C](P4-5D-R-civil-date-migration-phase-c-pre-review.md#4-tests-ajoutés)
et [C2](P4-5D-R-phase-c2-pre-review.md#44-pouvoir-discriminant--mesuré-pas-supposé).

### Tests modifiés en cours de lot, tous déclarés

Cinq modifications, sur quatre tests **du lot** : deux en C, trois en C2. Aucune assertion n'est affaiblie.

| Test | Phase | Modification |
|---|---|---|
| `UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier` | C | assertion **renforcée** (D-B3) : le message ne doit plus contenir la valeur brute |
| `LaClassification_EstFidele_AuLecteurReel` | C, puis C2 | corpus étendu deux fois, ajout pur |
| `UneHeureInvalide_NEmpechePasLaReprise_DeLaPartieDate` | C2 | **retiré** et remplacé par `UneHeureHorsBornes_EstRefusee_JamaisTronquee` : assertion **inversée** par Q-C2 |
| `UneBaseVolumineuse_EstReprise_IntegralementEtSansMelange` | C2 | donnée d'entrée corrigée : le générateur produisait des heures impossibles (`24:15:00` à `28:15:00`) |

Aucun test antérieur au lot n'est modifié.

### Tests de la spécification non réalisés

| # | Objet | État |
|---|---|---|
| T-B1 | invariant du prédicat SQL support | à réaliser, **sur `L'`** |
| T-B4 | conservation des compteurs (§3.4) | à réaliser |
| T-B5 | base réelle construite par la chaîne de migrations pré-P4-5D | à réaliser |
| T-B6 | script SQL support, simulation puis exécution | à réaliser |
| T-B7 | refus après migration de schéma, puis restauration | à réaliser |
| T-B8 | valeur stockée en `INTEGER` / `REAL` | à réaliser |

---

## Migration Status

| Contrôle | Commande | Résultat |
|---|---|---|
| Dérive de modèle EF | `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | `No changes have been made to the model since the last migration.` |
| Migrations créées ou modifiées | `git status --short src/MMV.Infrastructure/Migrations` | **vide** |
| Schéma SQLite | — | **inchangé** : `TEXT` |
| Modèle | — | **inchangé** : `DateOnly` conservé |
| PostgreSQL | — | **0 fichier** ; `DateOnly` y mappe vers `date`, le problème n'existe pas |

**L'absence de dérive est attendue.** C'est précisément le piège de l'ADR §7.2 : un format de valeur a changé
sans que le schéma bouge. La reprise corrige les valeurs au démarrage, pas par une migration.

---

## Known Limitations

| # | Limitation | Portée | Statut |
|---|---|---|---|
| **RR4** | **`dryRun` jamais exécuté sur une copie de base réelle.** Le volume réel de valeurs `Unrepairable` est inconnu. Aucune base de production n'est accessible depuis ce poste. | **préalable obligatoire au déploiement** | ouvert |
| RC1 / RR3 | Surface de refus élargie par D-B1 puis Q-C2 : une seule valeur non conforme bloque le démarrage. | exploitation | assumé (Q2) ; mesuré par RR4 |
| RC5 | Le prédicat `L` de la spécification tronquerait des valeurs que le moteur refuse. Aucun test automatique ne garde encore `L'` (T-B1). | outil support | atténué par l'addendum |
| RC2 | Empreinte non salée : pseudonymisation faible. | journal | accepté temporairement (Q-C3) |
| RC3 | `RawValue` accessible en mémoire : un futur appelant pourrait la journaliser. | code | faible, commenté |
| RC7 | Décalage non borné (`+99:99` accepté), jamais appliqué, donc sans effet sur le résultat. | règle | assumé, figé par test |
| RR5 | Base non garantie intégralement canonique : `ReadableLeftAsIs` reste en place. | export PostgreSQL | reporté P4-7 (Q3) |
| RR6 | Sauvegarde non atomique si un autre poste écrit pendant le premier démarrage. | multi-poste | P4-6 (Q5, D-B4) |
| — | Deux transactions au démarrage : un refus laisse une base au schéma migré mais aux dates non reprises. Revenir à l'état exact exige la sauvegarde. | exploitation | documenté (spéc. §3.3), T-B7 à réaliser |
| RR7 | Diagnostic complet des deux tables à chaque démarrage. | performance | négligeable en magasin, à mesurer en central |
| RR2 | La roadmap porte `P4-5D-R` en `NOT STARTED` dans son tableau et en `READY FOR ARCHITECT REVIEW` dans sa section de fin. **Non corrigé** : la roadmap globale est hors du périmètre d'écriture. | documentation | ouvert (Q6) |

---

## Future Work

| Cible | Travail | Origine |
|---|---|---|
| **Avant déploiement** | `dryRun` sur une copie de chaque base réelle, puis correction manuelle des `Unrepairable` (spéc. §6). Premier démarrage sur un poste unique, autres postes fermés. | RR4, D-B4 |
| Prochain lot de tests | T-B1 sur `L'`, puis T-B4 à T-B8 | spéc. §4.3, addendum §A.8 |
| Roadmap | Réconciliation des états P4-5C, P4-5D, P4-5D-R | Q6, RR2 |
| **P4-6** | Sérialisation multi-poste de `PrepareDatabase` et sauvegarde cohérente | Q5, D-B4 |
| **P4-7** | Canonicalisation des `ReadableLeftAsIs` avant export ; règle du `Kind` des 19 colonnes d'instants | Q3, Q4 |
| **P7** | HMAC à clé d'installation pour l'empreinte ; rétention et accès du journal de migration ; commande support `dryRun` | Q-C3, D-B3, Q7 |

La roadmap P5 intitule P7 « Production Readiness » (Backup, Restore, Logs, Support). La décision Q-C3 la désigne
comme « Security Hardening », et P6 porte « Sécurité & Gestion utilisateurs ». **La phase de rattachement du
durcissement de l'empreinte reste à confirmer.**

---

## Commit Readiness

### Contrôles de clôture (21/09/2026)

| Contrôle | Attendu | Obtenu |
|---|---|---|
| `dotnet build MMV.sln -c Debug` | 0 erreur, 0 avertissement | **0 erreur, 0 avertissement** (également en `--no-incremental`) |
| `dotnet test MMV.sln -c Debug` | ≥ 1 915, 0 échec, 0 ignoré | **1 915 / 1 915**, 0 échec, 0 ignoré |
| `dotnet ef migrations has-pending-model-changes` | aucun changement | **`No changes have been made to the model since the last migration.`** |
| Migrations, ADR, PostgreSQL, P4-5E, roadmap, code | non modifiés par la consolidation | **conformes** |
| HEAD | `023048f` | **`023048f`**, aucun commit, aucun push, aucune branche |

### Périmètre du commit P4-5D-R, 15 fichiers

```
 M docs/architecture/P4-multi-poste-roadmap.md                              (+20 / −0, V2)
 M src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs                     (+95 / −1)
?? docs/implementation/P4-5D-R-civil-date-migration-pre-review.md
?? docs/implementation/P4-5D-R-civil-date-migration-pre-review-v2.md
?? docs/implementation/P4-5D-R-civil-date-migration-phase-b-specification.md
?? docs/implementation/P4-5D-R-civil-date-migration-phase-c-pre-review.md
?? docs/implementation/P4-5D-R-phase-b-addendum.md
?? docs/implementation/P4-5D-R-phase-c2-pre-review.md
?? docs/implementation/P4-5D-R-final-implementation-report.md
?? src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs
?? src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs
?? src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs
?? tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs
?? tests/MMV.Domain.Tests/Data/Time/CivilDateRepairPreparationTests.cs
?? tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs
```

### Hors lot, présents dans l'arbre, non touchés

| Fichier | Nature | À arbitrer |
|---|---|---|
| `docs/implementation/P4-5D-final-implementation-report.md` | rapport de clôture de P4-5D, rédigé après le commit `023048f`, jamais commité | commit séparé `docs(P4-5D)`, ou inclusion dans ce commit |
| `docs/architecture/P5-product-completion-roadmap.md` | roadmap P5, sans lien avec ce lot | hors de ce commit |

### Message de commit proposé

Proposition seulement, sur le modèle de `023048f`. À valider.

```
feat(P4-5D-R): repair legacy civil date formats at startup

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

### Verdict

**Le lot est complet.** Toutes les décisions de la Phase C sont tranchées (Q-C1 à Q-C6), la documentation est
alignée sur le code, le build est propre, les 1 915 tests passent, aucune migration n'est créée et le modèle est
inchangé.

**Statut : `READY FOR COMMIT`**, en attente de validation architecte. Deux points à arbitrer avant le commit :

1. l'inclusion ou non du rapport P4-5D non commité ;
2. le message de commit.

**RR4** ne bloque pas le commit, mais **bloque le déploiement** sur une installation en service.
