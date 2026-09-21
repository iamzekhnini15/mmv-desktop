# P4-5D-R — Phase B : spécification exécutable de la reprise des dates civiles

> **Spécification — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE. Aucune exécution en production.**
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`. HEAD : `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60`.
>
> Entrée : revue architecte **A2 — APPROUVÉE** ([V1](P4-5D-R-civil-date-migration-pre-review.md),
> [V2](P4-5D-R-civil-date-migration-pre-review-v2.md)).
> Portée : `Customers.BirthDate` (nullable) et `Prescriptions.IssueDate` (NOT NULL). **Rien d'autre.**

---

## 0. Cadre

| Invariant | Statut |
|---|---|
| Migration EF | **aucune** — ni générée, ni générable : la colonne reste `TEXT` |
| Changement de schéma | **aucun** |
| Changement de modèle | **aucun** — `DateOnly` conservé |
| Nature de la reprise | **format des valeurs uniquement** |
| Conversion de fuseau | **interdite** |
| Recalage de calendrier (`date()` SQLite en écriture) | **interdit** |
| Colonnes d'instants (19) | **hors périmètre** |

### Référence normative

La règle est **déjà implémentée** dans l'arbre de travail et couverte par les tests. Cette spécification la
**fige** ; elle ne l'invente pas.

| Rôle | Élément |
|---|---|
| Règle (fonction pure) | `CivilDateFormat.Classify` — `src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs` |
| Détection (lecture seule) | `SqliteCivilDateFormatVerifier.Audit` |
| Transformation | `SqliteCivilDateRepairService.Repair(context, dryRun)` |
| Orchestration, sauvegarde, refus | `SqliteDatabaseManager.PrepareDatabase` → `RepairCivilDateFormats` |

**En cas de divergence entre ce document et le code C# : le code C# et ses tests font foi.** Le SQL du §2 est un
**outil support** au périmètre strictement plus restreint (voir §2.1), pas un second moteur de reprise.

### Mesures faites pour ce document

Toutes les affirmations de comportement ci-dessous ont été **exécutées**, pas supposées :

- une sonde jetable (hors dépôt) a classé **37 valeurs** avec `CivilDateFormat.Classify` et les a confrontées
  au prédicat SQL du §2 sur `Microsoft.Data.Sqlite` 8.0.27 : **0 divergence** ;
- le script SQL complet du §2 (détection, instantané, mise à jour, vérification, simulation par `ROLLBACK`) a
  été exécuté sur une table au schéma de `Customers` : résultats reportés au §2.6.

---

## 1. Algorithme de reprise

### 1.1 Grammaire

```
canonique       := AAAA "-" MM "-" JJ                    (exactement 10 caractères, date calendaire valide)
tête            := les 10 premiers caractères de la valeur
lisible         := DateOnly.TryParse(valeur, InvariantCulture, DateTimeStyles.None) réussit
                   (= comportement exact du lecteur Microsoft.Data.Sqlite, prouvé par
                    LaClassification_EstFidele_AuLecteurReel)
tronquable      := longueur ≥ 10 ET DateOnly.TryParseExact(tête, "yyyy-MM-dd", InvariantCulture) réussit
```

`DateOnly.TryParseExact` **refuse** les dates inexistantes (`2026-02-30`, `2025-02-29`, mois 13, année `0000`) :
une tête invalide n'est **jamais** recalée.

### 1.2 Arbre de décision (l'ordre est la garantie de sûreté)

```
valeur NULL                                   → NotApplicable            : rien
canonique                                     → Canonical                : rien
lisible ET tronquable ET date(tête) = date lue → NormalizableReadable     : écrire tête
lisible (sinon)                               → ReadableLeftAsIs         : rien, signalé en audit
illisible ET tronquable                       → RepairableLegacyDateTime : écrire tête
illisible (sinon)                             → Unrepairable             : rien, REFUS du démarrage (Q2)
```

**La lisibilité est testée avant la troncature.** C'est ce qui protège `␣1985-03-15` : tronqué, il deviendrait
`␣1985-03-1`, **lisible comme le 1er mars** — une date de naissance changée sans erreur
(`LaRepriseNaive_EnSqlDeMasse_ChangeraitSilencieusementUneDate`).

### 1.3 Table de vérité — cas de la demande (mesurés)

| Entrée | État | Sortie | Action |
|---|---|---|---|
| `1985-03-15T12:30:45.123` | `NormalizableReadable` | `1985-03-15` | **normalisée** |
| `1985-03-15 12:30:45` | `RepairableLegacyDateTime` | `1985-03-15` | **normalisée** |
| `1985-03-15T00:00:00` | `NormalizableReadable` | `1985-03-15` | **normalisée** |
| `1985-03-15` | `Canonical` | — | intacte |
| `1985/03/15` | `ReadableLeftAsIs` | — | **intacte** (lisible, non canonique — Q3) |
| `1985-3-5` | `ReadableLeftAsIs` | — | **intacte** (lisible, non canonique — Q3) |
| `abc`, `not-a-date at all`, `15/03/1985` | `Unrepairable` | — | **intacte + refus** |
| `""` (vide), `"   "` | `Unrepairable` | — | **intacte + refus** |
| `NULL` | `NotApplicable` | — | intacte |

> **Conséquence sur RR1 (V2).** La demande de Phase B classe explicitement `1985-03-15T00:00:00` et
> `1985-03-15T12:30:45.123` parmi les valeurs **à normaliser**. La décision Q3 est donc lue comme visant
> **uniquement** `ReadableLeftAsIs` : le comportement implémenté est conforme, **RR1 est clos sans code**.

### 1.4 Table de vérité — cas complémentaires (mesurés)

| Entrée | État | Sortie | Remarque |
|---|---|---|---|
| `1985-03-15 00:00:00` | `RepairableLegacyDateTime` | `1985-03-15` | cas nominal pré-P4-5D |
| `1985-03-15 12:30:45.1234567` | `RepairableLegacyDateTime` | `1985-03-15` | 7 décimales du pilote |
| `1985-03-15 23:59:59` | `RepairableLegacyDateTime` | `1985-03-15` | **jamais le 16** |
| `1985-03-15 24:00:00` | `RepairableLegacyDateTime` | `1985-03-15` | heure invalide, tête valide |
| `1985-03-15 ` (espace de fin) | `NormalizableReadable` | `1985-03-15` | neutre |
| `␣1985-03-15` (espace de tête) | `ReadableLeftAsIs` | — | troncature destructrice → interdite |
| `2026-02-30`, `2026-02-30 00:00:00` | `Unrepairable` | — | **jamais recalée en 03-02** |
| `2025-02-29 00:00:00` | `Unrepairable` | — | 2025 non bissextile |
| `2024-02-29 00:00:00` | `RepairableLegacyDateTime` | `2024-02-29` | bissextile réel |
| `0001-01-01 00:00:00` / `9999-12-31 23:59:59` | `RepairableLegacyDateTime` | tête | aucune borne métier |
| `0000-01-01 00:00:00` | `Unrepairable` | — | hors domaine `DateOnly` |
| `1985-03-15 12:30:45+01:00` | `RepairableLegacyDateTime` | `1985-03-15` | ⚠️ voir D-B1 |
| `1985-03-15T23:30:00-05:00` | `RepairableLegacyDateTime` | `1985-03-15` | ⚠️ voir D-B1 |
| `1985-03-15T12:30:45Z` | `RepairableLegacyDateTime` | `1985-03-15` | ⚠️ voir D-B1 |
| `1985-03-15 12:30` | `RepairableLegacyDateTime` | `1985-03-15` | ⚠️ voir D-B1 |
| `1985-03-15garbage` | `RepairableLegacyDateTime` | `1985-03-15` | ⚠️ **voir D-B1** |

### 1.5 Règles de troncature

1. La valeur écrite est **exactement** `substring(0, 10)` de la valeur brute — aucun calcul, aucun reformatage.
2. La tête doit passer `TryParseExact("yyyy-MM-dd")` ; sinon **aucune écriture**.
3. **Aucun fuseau** : un décalage (`+01:00`, `Z`) n'est **jamais appliqué** ; il est jeté avec l'heure. Le jour
   retenu est le jour **écrit**, ce qui est la seule lecture correcte d'une date civile.
4. Pour une valeur lisible, l'écriture exige `date(tête) == date lue` — preuve de neutralité, ligne à ligne.
5. `date()` SQLite est **proscrite en écriture** (`SqliteRecaleraitSilencieusement_CeQueLaRegleRefuse`).
6. **Aucun `UPDATE` de masse par expression** dans le moteur : chaque ligne reçoit la valeur calculée pour elle,
   par clé primaire, en requête paramétrée.

### 1.6 Traitement des échecs

| Situation | Comportement | Garantie |
|---|---|---|
| ≥ 1 `Unrepairable` au diagnostic | `DatabaseMigrationException`, **aucune écriture** | refus en bloc, avant transaction |
| Exception pendant l'écriture | transaction non validée → **retour à l'état initial** | `using var transaction` sans `Commit` |
| Valeur encore illisible après reprise | `DatabaseMigrationException` (`CIVILDATE FAILURE`) | contrôle d'après-coup |
| Table/colonne absente | colonne ignorée (`Exists = false`), pas d'erreur | base partielle tolérée |
| Valeur stockée non `TEXT` (entier, réel) | lue via `CAST(... AS TEXT)`, classée comme texte | aucune interprétation de type |

Dans **tous** les cas d'échec : la base et sa sauvegarde sont **conservées**, aucune donnée n'est supprimée.

### 1.7 Journalisation (exigences)

Lignes écrites au journal de migration (`IMigrationJournal`), dans cet ordre :

| Préfixe | Quand | Contenu minimal |
|---|---|---|
| `BACKUP created '<chemin>'` | avant toute mutation (fichier existant) | chemin de la sauvegarde |
| `CIVILDATE ok:` | rien à reprendre | constat explicite (une base saine le journalise **aussi**) |
| `CIVILDATE detected:` | avant écriture | nombre de valeurs à réécrire, nombre d'illisibles |
| `CIVILDATE repaired:` | après validation | `rewritten`, `truncated`, `normalized`, `unrepairable`, `dryRun`, `applied` |
| `CIVILDATE REFUSED:` | refus Q2 | total + **20 premières** valeurs : table, colonne, clé, valeur brute |
| `CIVILDATE FAILURE:` | contrôle d'après-coup en échec | nombre de valeurs encore illisibles |

Exigence : les compteurs journalisés sont **ceux mesurés**, jamais ceux supposés (le rapport `dryRun` annonce le
même compte que l'exécution réelle — `UneSimulation_NEcritRien_MaisRendLeMemeCompte`).

> ⚠️ `CIVILDATE REFUSED` inscrit des **valeurs brutes de date de naissance** avec leur clé : donnée de santé
> indirecte dans un fichier local. Voir D-B3.

---

## 2. Stratégie SQL (outil support)

### 2.1 Statut du SQL

Le SQL ci-dessous **ne remplace pas** le service C#. Il sert à : diagnostiquer une copie de base sans lancer
l'application, préparer le `dryRun` sur base réelle exigé par RR4, et vérifier après coup.

**Il ne reproduit pas la lisibilité .NET** — c'est impossible en SQL. Il ne cible donc qu'un **sous-ensemble
strict**, dont la sûreté se prouve sans lecteur .NET :

```
AAAA-MM-JJ[ T]HH:MM:SS           (19 caractères)
AAAA-MM-JJ[ T]HH:MM:SS.f{1,7}    (21 à 27 caractères, chiffres uniquement après le point)
```

avec une tête calendairement valide et une année ≠ `0000`. **Mesuré :** toute valeur retenue par ce prédicat est
classée `NormalizableReadable` ou `RepairableLegacyDateTime` par le C#, avec **la même valeur de sortie**
(37 valeurs, 0 divergence). Le reste (`+01:00`, `Z`, espace de fin, `garbage`…) est laissé au C#.

**`date()` n'est utilisé qu'en comparaison** (`date(x) IS x`) pour **rejeter** une tête qu'il recalerait —
jamais pour produire une valeur.

### 2.2 Prédicats

Écrits pour `Customers."BirthDate"` ; pour `Prescriptions`, remplacer `"Customers"` → `"Prescriptions"`,
`"BirthDate"` → `"IssueDate"`, `"CustomerId"` → `"PrescriptionId"`.

**C — canonique**

```sql
typeof("BirthDate") = 'text' AND length("BirthDate") = 10
AND "BirthDate" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
AND substr("BirthDate", 1, 4) <> '0000'
AND date("BirthDate") IS "BirthDate"
```

**L — historique strict (à tronquer)**

```sql
typeof("BirthDate") = 'text'
AND "BirthDate" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9][ T][0-9][0-9]:[0-9][0-9]:[0-9][0-9]*'
AND (length("BirthDate") = 19
     OR (substr("BirthDate", 20, 1) = '.' AND length("BirthDate") BETWEEN 21 AND 27
         AND substr("BirthDate", 21) NOT GLOB '*[^0-9]*'))
AND substr("BirthDate", 1, 4) <> '0000'
AND date(substr("BirthDate", 1, 10)) IS substr("BirthDate", 1, 10)
```

`C` et `L` sont faux sur `NULL` (`typeof(NULL) = 'null'`) : les `NULL` ne sont **jamais** candidats.

### 2.3 Requête de détection (lecture seule)

```sql
SELECT
    'Customers.BirthDate'                    AS target,
    COUNT(*)                                 AS total_rows,
    SUM("BirthDate" IS NULL)                 AS null_rows,
    SUM(<C>)                                 AS canonical_rows,
    SUM(<L>)                                 AS strict_legacy_rows,
    SUM("BirthDate" IS NOT NULL AND NOT (<C>) AND NOT (<L>)) AS residual_rows
FROM "Customers";
```

Liste nominative du résidu, à faire classer par le C# (audit `SqliteCivilDateFormatVerifier`) :

```sql
SELECT "CustomerId", "BirthDate", typeof("BirthDate")
FROM "Customers"
WHERE "BirthDate" IS NOT NULL AND NOT (<C>) AND NOT (<L>)
ORDER BY "CustomerId";
```

**Lecture :** `residual_rows = 0` ⇒ le SQL suffit et le C# réécrira exactement `strict_legacy_rows` lignes.
`residual_rows > 0` ⇒ **le diagnostic C# est obligatoire** : ces lignes peuvent être `ReadableLeftAsIs`
(intactes), réparables hors sous-ensemble strict, ou `Unrepairable` (**démarrage refusé**).

### 2.4 Transformation (instantané + mise à jour)

```sql
BEGIN IMMEDIATE;

CREATE TEMP TABLE IF NOT EXISTS civil_date_snapshot (
    tbl TEXT NOT NULL, id INTEGER NOT NULL, raw TEXT NOT NULL, expected TEXT NOT NULL);
DELETE FROM civil_date_snapshot;

INSERT INTO civil_date_snapshot (tbl, id, raw, expected)
SELECT 'Customers', "CustomerId", "BirthDate", substr("BirthDate", 1, 10)
FROM "Customers"
WHERE <L>;

UPDATE "Customers"
SET "BirthDate" = (SELECT s.expected FROM civil_date_snapshot s
                   WHERE s.tbl = 'Customers' AND s.id = "Customers"."CustomerId")
WHERE "CustomerId" IN (SELECT id FROM civil_date_snapshot WHERE tbl = 'Customers');

-- SELECT changes();  → doit être égal au nombre de lignes de l'instantané
```

- **Idempotente** : après exécution, les lignes réécrites sont canoniques, donc hors de `L` ; la seconde passe
  réécrit **0** ligne.
- **Aucune perte** : l'instantané garde `raw` pour chaque ligne touchée, pendant toute la transaction.
- `BEGIN IMMEDIATE` prend le verrou d'écriture **dès le départ** : aucun autre poste ne peut écrire entre
  l'instantané et la mise à jour.

### 2.5 Vérification (dans la transaction, avant `COMMIT`)

```sql
SELECT
    (SELECT COUNT(*) FROM civil_date_snapshot WHERE tbl = 'Customers')           AS snapshot_rows,
    (SELECT COUNT(*) FROM civil_date_snapshot s
       JOIN "Customers" c ON c."CustomerId" = s.id
      WHERE s.tbl = 'Customers' AND c."BirthDate" IS NOT s.expected)            AS mismatched_rows,
    (SELECT COUNT(*) FROM civil_date_snapshot s
       LEFT JOIN "Customers" c ON c."CustomerId" = s.id
      WHERE s.tbl = 'Customers' AND c."CustomerId" IS NULL)                      AS lost_rows;
```

Puis relancer la **détection** (§2.3).

**Critères d'acceptation — tous obligatoires, sinon `ROLLBACK` :**

| Contrôle | Attendu |
|---|---|
| `changes()` après `UPDATE` | = `snapshot_rows` |
| `mismatched_rows` | 0 |
| `lost_rows` | 0 |
| `total_rows` après | = `total_rows` avant |
| `null_rows` après | = `null_rows` avant |
| `canonical_rows` après | = `canonical_rows` avant + `snapshot_rows` |
| `strict_legacy_rows` après | 0 |
| `residual_rows` après | = `residual_rows` avant (le SQL n'y touche pas) |

Enfin, **seule preuve de lisibilité complète** : l'audit C# après reprise doit donner
`TotalUnreadableCount = 0` (déjà exigé par `RepairCivilDateFormats`).

### 2.6 Exécution mesurée du script

Sur 15 lignes (`1985-03-15`, 5 formes strictes, `␣1985-03-15`, `1985-3-5`, `1985/03/15`, `abc`, `""`,
`2026-02-30`, `1985-03-15garbage`, `1985-03-15␣`, `NULL`) :

| Étape | total | null | canonical | strict_legacy | snapshot | mismatched | lost |
|---|---|---|---|---|---|---|---|
| Détection avant | 15 | 1 | 1 | 5 | — | — | — |
| Simulation (`ROLLBACK`) | — | — | — | — | 5 | 0 | 0 |
| Détection après `ROLLBACK` | 15 | 1 | 1 | 5 | — | — | — |
| Exécution (`changes()` = 5) | — | — | — | — | 5 | 0 | 0 |
| Détection après `COMMIT` | 15 | 1 | **6** | **0** | — | — | — |
| Seconde exécution | — | — | — | — | **0** | 0 | 0 |

Résidu après le SQL, classé par le C# : 3 `ReadableLeftAsIs`, 3 `Unrepairable`, `1985-03-15garbage` →
`RepairableLegacyDateTime`, `1985-03-15␣` → `NormalizableReadable`. **Le SQL seul ne rend donc pas la base
démarrable** : il est un outil de diagnostic et de préparation, le passage par le service C# reste obligatoire.

---

## 3. Règles de sûreté

### 3.1 Sauvegarde — obligatoire

- Voie normale : `PrepareDatabase` appelle `Backup(databasePath)` **avant la détection d'état et avant toute
  mutation** → `backups/<nom>-<yyyyMMdd-HHmmss-fff>.db.bak` (+ `-wal` / `-shm` s'ils existent). Journal :
  `BACKUP created`.
- Voie manuelle (support, SQL du §2) : **application fermée sur tous les postes**, puis copie du fichier
  **et** de ses annexes `-wal`/`-shm`, **ou** `VACUUM INTO '<chemin>'` (copie cohérente en une opération).
- La sauvegarde n'est **jamais** supprimée par la reprise, y compris après succès.

### 3.2 Transaction — obligatoire

- Moteur C# : une transaction **unique** couvre les deux colonnes. Tout ou rien.
- Le diagnostic (`dryRun`) et le refus Q2 ont lieu **avant** l'ouverture de la transaction : un refus n'écrit
  rien, même pas partiellement.
- SQL support : `BEGIN IMMEDIATE … COMMIT`, avec `ROLLBACK` si un critère du §2.5 échoue.

### 3.3 Stratégie de retour arrière

| Situation | Retour arrière |
|---|---|
| Erreur pendant l'écriture | automatique — transaction non validée |
| Refus Q2 | aucun nécessaire — rien n'a été écrit |
| Reprise validée mais jugée fausse a posteriori | `SqliteDatabaseManager.Restore(backupPath, databasePath)`, application arrêtée sur tous les postes (journal : `RESTORE from`) |
| Voie manuelle SQL | `ROLLBACK` avant `COMMIT` ; après `COMMIT`, restauration de la copie |

> ⚠️ **Deux transactions distinctes au démarrage.** Les migrations de schéma EF (s'il y en a) sont validées
> **avant** la reprise des dates. Un refus Q2 laisse donc une base **au schéma à jour** mais aux dates non
> reprises. C'est sans danger (le prochain démarrage refait le même diagnostic), mais **revenir à l'état exact
> d'avant le démarrage exige la restauration de la sauvegarde**, pas un simple `ROLLBACK`.

### 3.4 Compteurs avant / après

Le rapport `CivilDateRepairReport` porte, par colonne : `TruncatedRows`, `NormalizedRows`, `UnrepairableRows`,
et l'audit `AuditBefore` (`TotalRows`, `NullCount`, `CanonicalCount`, `NormalizableCount`,
`ReadableLeftAsIsCount`, `RepairableCount`, `UnrepairableCount`). Invariants exigés :

```
après.TotalRows          = avant.TotalRows
après.NullCount          = avant.NullCount
après.CanonicalCount     = avant.CanonicalCount + avant.NormalizableCount + avant.RepairableCount
après.ReadableLeftAsIs   = avant.ReadableLeftAsIs
après.Normalizable       = 0
après.Repairable         = 0
après.Unrepairable       = avant.Unrepairable   (= 0 si la reprise a été autorisée)
rapport.RewrittenRows    = avant.RewriteCount
```

### 3.5 Simulation (`dryRun`)

| Propriété | Exigence |
|---|---|
| Écritures | **aucune** — pas même l'ouverture d'une transaction |
| Rapport | **identique** en compteurs à celui de l'exécution réelle qui suivrait |
| `Applied` | toujours `false` |
| Accès | par code uniquement (`Repair(context, dryRun: true)`) — commande support reportée à **P7** (Q7) |
| Usage obligatoire | **sur une copie de chaque base réelle avant déploiement** (RR4) — SQL §2.3 si l'application ne peut pas être lancée sur la copie |

---

## 4. Spécification des tests

### 4.1 Tests unitaires (règle pure) — **existants**, `CivilDateFormatTests`

| Exigence | Test | Statut |
|---|---|---|
| Date canonique inchangée | `UneValeurDejaCanonique_NEstPasTouchee` | ✅ |
| Datetime tronqué correctement | `UneDateHistorique_EstReprise_ParTroncature` (5 cas) | ✅ |
| Pas de décalage de jour | `LaReprise_NeDecaleJamaisLeJour` | ✅ |
| Lisible non canonique normalisé sans changement | `UneValeurLisibleNonCanonique_EstRemiseEnForme_SansChangerDeValeur` | ✅ |
| Formats invalides rejetés | `UneValeurIncomprehensible_NEstJamaisTransformee` (dont `""`, `"   "`) | ✅ |
| `1985/03/15`, `1985-3-5` intacts | `UneValeurLisible_QueLaTroncatureCasserait_EstLaisseeIntacte` | ✅ |
| Date inexistante refusée | `UneDateInexistanteAuCalendrier_EstRefusee_JamaisRecalee` | ✅ |
| `NULL` | `UneValeurNulle_EstHorsPerimetre` | ✅ |
| Bornes | `LesDatesExtremes_SontReprises_SansBorneArbitraire` | ✅ |
| Idempotence | `AppliquerLaRegleDeuxFois_DonneLeMemeResultat` | ✅ |
| Fidélité au lecteur réel | `LaClassification_EstFidele_AuLecteurReel` | ✅ |

### 4.2 Tests d'intégration — **existants**

`SqliteCivilDateRepairTests` (SQLite en mémoire, schéma migré) :
détection sans écriture, relecture `DateOnly` par EF après reprise
(`ApresReprise_LeModeleCourantRelitCeQuIlNeSavaitPlusLire`), ordonnances, `NULL` et valeurs saines intactes,
**aucune colonne d'instant touchée**, date inexistante jamais recalée en base, idempotence, `dryRun`, volume
(28 lignes sans mélange).

`CivilDateRepairPreparationTests` (vrais fichiers temporaires, `PrepareDatabase`) :
base gérée par migrations **et** antérieure à P4-5D reprise au démarrage, sauvegarde avant reprise, retour
arrière par la sauvegarde, base neuve, base déjà reprise, refus sans modification, conservation base +
sauvegarde, journalisation, base historique adoptée.

### 4.3 Tests à ajouter en Phase B (implémentation)

| # | Test | Type | Objet |
|---|---|---|---|
| T-B1 | `LePredicatSqlStrict_EstInclusDansLaRegleCSharp` | unitaire + SQLite mémoire | pour tout le corpus (§1.3–1.4) : `L(x)` ⇒ `AllowsRewrite(Classify(x))` **et** `substr(x,1,10) = repaired` ; `C(x)` ⇔ `Canonical`. Garde le SQL support honnête si le pilote évolue. |
| T-B2 | `LesValeursAvecDecalage_SontTronquees_SansConversion` | unitaire | `+01:00`, `-05:00`, `Z` → même jour écrit. Fige le comportement actuel, **ou** le nouveau selon D-B1. |
| T-B3 | `UnSuffixeInconnu_…` | unitaire | `1985-03-15garbage` : comportement selon **D-B1**. |
| T-B4 | `LaReprise_ConserveLesCompteurs` | intégration | invariants du §3.4, sur les deux colonnes, avec `NULL`, canoniques, `ReadableLeftAsIs`, historiques. |
| T-B5 | `UneBaseReellePreP45D_EstRepriseEtRelueEnDateOnly` | intégration fichier | base construite par la **chaîne de migrations jusqu'à la dernière migration pré-P4-5D**, peuplée **par l'ancien chemin d'écriture** (valeurs `DateTime` réellement produites par le pilote : `yyyy-MM-dd HH:mm:ss[.fffffff]`), puis `PrepareDatabase`, puis lecture EF de **toutes** les entités `Customer` et `Prescription` sans exception. |
| T-B6 | `LeScriptSqlSupport_SimulationPuisExecution` | intégration SQLite | script §2.4–2.5 : simulation laisse la base identique octet pour octet sur les colonnes visées ; exécution satisfait les critères §2.5 ; seconde exécution = 0. |
| T-B7 | `UnRefus_ApresMigrationDeSchema_LaisseLaSauvegardeRestaurable` | intégration fichier | base nécessitant une migration EF **et** portant une valeur `Unrepairable` : refus, puis `Restore` redonne l'état exact d'avant démarrage (§3.3). |
| T-B8 | `UneValeurNonTexte_EstClasseeSansInterpretation` | intégration | `BirthDate` stocké en `INTEGER`/`REAL` (affinité SQLite) : classé via `CAST AS TEXT`, jamais converti en date. |

**Seuil de validation Phase B :** suite complète verte, **1 825 + nouveaux tests**, 0 ignoré, 0 avertissement.

---

## 5. Décisions à trancher avant implémentation

### D-B1 — Portée de la troncature *(principal point ouvert)*

La règle actuelle tronque **toute** valeur illisible dont la tête est une date valide. Elle réécrit donc aussi
des formes **absentes** de la liste validée : décalages (`+01:00`, `Z`), heure sans secondes (`12:30`), et
**suffixe arbitraire** (`1985-03-15garbage`).

| Option | Effet | Risque |
|---|---|---|
| **A — conserver** (état actuel) | toute tête valide est reprise | une valeur d'origine inconnue devient une date « propre », sans trace au-delà du compteur |
| **B — restreindre** à la grammaire `[ T]HH:mm[:ss[.f{1,7}]][Z\|±HH:mm]` | `…garbage` devient `Unrepairable` | refus de démarrage supplémentaires (Q2) |

**Recommandation : B.** C'est l'application directe du principe validé en Q2 (« une donnée invalide plutôt
qu'une corruption silencieuse ») : un suffixe inconnu n'a pas été produit par `DateTime`, on ne sait pas ce qu'il
signifie. Les décalages et `Z` restent repris par troncature (jour écrit conservé, aucune conversion). Impact
code : `CivilDateFormat.TryTruncateToCivilDate` + T-B2/T-B3.

### D-B2 — Chaîne vide sur `BirthDate` (nullable)

`""` est aujourd'hui `Unrepairable` → **refus du démarrage**. La convertir en `NULL` serait défendable (absence
de donnée) mais reste une **écriture de sens**. **Recommandation : conserver le refus** (Q2), et mesurer
l'occurrence réelle par le `dryRun` de RR4 avant de rouvrir la question.

### D-B3 — Données personnelles au journal

`CIVILDATE REFUSED` écrit jusqu'à 20 dates de naissance brutes avec leur clé. **Recommandation :** conserver la
clé et la table/colonne, et ne journaliser la valeur brute **que** pour les `Unrepairable` (nécessaire à
l'arbitrage support) — c'est déjà le cas ; à confirmer au regard de la politique de rétention du journal (P7).

### D-B4 — Sauvegarde non atomique en multi-poste

`Backup` copie le fichier puis ses annexes avec `File.Copy` : non atomique si un autre poste écrit au même
moment. Relève de **P4-6** (Q5, toujours ouverte). **Recommandation :** d'ici P4-6, la procédure de
déploiement impose **tous les postes fermés** pendant le premier démarrage post-P4-5D.

---

## 6. Procédure d'exécution (pour le déploiement — non exécutée)

1. **Collecte** d'une copie de chaque base réelle (tous postes fermés).
2. **Diagnostic** sur la copie : SQL §2.3 puis audit C# (`dryRun`). Consigner les compteurs.
3. **Décision** : `UnrepairableCount > 0` ⇒ correction manuelle **sur la base réelle** avant déploiement
   (ligne par ligne, clé en main, avec validation métier). Sinon, poursuivre.
4. **Déploiement** : premier démarrage, poste unique, autres postes fermés.
5. **Contrôle** du journal : `BACKUP created` → `CIVILDATE detected` → `CIVILDATE repaired` avec les compteurs
   du point 2. Tout écart ⇒ arrêt et `Restore`.
6. **Conservation** de la sauvegarde jusqu'à validation métier explicite.

---

## 7. Hors périmètre (rappel)

Canonicalisation de `ReadableLeftAsIs` (**P4-7**, Q3) · règle du `Kind` des 19 colonnes d'instants (**P4-7**,
Q4) · sérialisation multi-poste de la préparation (**P4-6**, Q5) · commande support `dryRun` (**P7**, Q7) ·
toute migration EF · tout fichier PostgreSQL.

**Statut : `READY FOR ARCHITECT REVIEW` — Phase B spécifiée, non implémentée.**
