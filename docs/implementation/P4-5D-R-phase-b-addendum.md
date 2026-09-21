# P4-5D-R Phase B Addendum

> **Addendum à la spécification Phase B — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`. HEAD : `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60`.
> Amende : [spécification Phase B](P4-5D-R-civil-date-migration-phase-b-specification.md) (approuvée).
> Sources : [pré-revue Phase C](P4-5D-R-civil-date-migration-phase-c-pre-review.md), revue architecte de la
> Phase C, [pré-revue Phase C2](P4-5D-R-phase-c2-pre-review.md), consolidation finale (Q-C6 tranchée).

---

## Purpose

La spécification Phase B a été écrite **avant** les décisions D-B1 et D-B3. Elle décrit donc à plusieurs endroits
un comportement qui n'est plus celui du code. Cet addendum consigne l'écart, section par section, et fixe la règle
finale. **La spécification n'est pas réécrite** : elle reste le document approuvé, et cet addendum la complète.

**Ordre de précédence** (inchangé par rapport au §0 de la spécification) : le code C# et ses tests font foi. Vient
ensuite cet addendum, puis la spécification.

### Sections de la spécification amendées

| § | Ce que dit la spécification | Règle finale | Décision |
|---|---|---|---|
| 1.2 | `illisible ET tronquable → RepairableLegacyDateTime` | `illisible ET forme historique ET tronquable` ([§A.2](#a2-arbre-de-décision-final)) | D-B1, Q-C2 |
| 1.4 | `24:00:00`, `12:30` et `garbage` sont `RepairableLegacyDateTime` ; les décalages portent un ⚠️ | ces trois valeurs sont `Unrepairable` ; les décalages sont repris, sans réserve ([§A.6](#a6-table-de-vérité-du-14-corrigée)) | D-B1, Q-C2 |
| 1.5 | six règles de troncature | ajout d'une septième règle : forme historique exigée ([§A.2](#a2-arbre-de-décision-final)) | D-B1 |
| 1.7 | `CIVILDATE REFUSED` : « valeur brute » | clé et empreinte, **jamais** la valeur brute ([§B](#d-b3-final-rule)) | D-B3 |
| 2.1–2.5 | prédicat `L` sans bornes horaires | prédicat `L'` ([§A.7](#a7-sql-support--prédicat-l-remplace-l)) : `L` n'est plus utilisable | Q-C2 |
| 2.6 | `1985-03-15garbage` → `RepairableLegacyDateTime` dans le résidu | → `Unrepairable` | D-B1 |
| 4.3 | T-B1 formulé sur `L` ; T-B2 et T-B3 « selon D-B1 » | T-B1 porte sur `L'` ; T-B2 et T-B3 sont réalisés ([§A.8](#a8-tests-de-la-spécification--état)) | — |
| 5 | D-B1 option B : `[ T]HH:mm[:ss[.f{1,7}]][Z\|±HH:mm]`, secondes **facultatives** | secondes **obligatoires**, heure bornée ([§A.1](#a1-grammaire)) | D-B1, Q-C2 |
| 5 | D-B3 : « conserver la valeur brute » pour les `Unrepairable` | valeur brute **retirée** du journal et des messages | D-B3 |

D-B2 (chaîne vide refusée) et D-B4 (sauvegarde multi-poste, P4-6) sont **inchangés**.

---

## D-B1 Final Rule

### A.1 Grammaire

Une valeur **illisible** par le modèle courant n'est reprise par troncature que si elle a **exactement** la forme
d'un `DateTime` écrit par l'application avant P4-5D :

```
yyyy-MM-dd[ T]HH:mm:ss[.f{1,7}][Z|±HH:mm]
```

Implémentation unique : `CivilDateFormat.IsLegacyDateTimeForm`, appelée par `CivilDateFormat.Classify`.

```
^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T]([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?\z
```

| Élément | Règle | Motif |
|---|---|---|
| Tête `yyyy-MM-dd` | 4-2-2 chiffres ASCII, puis date calendaire valide (`TryParseExact`) | une date inexistante n'est jamais recalée |
| Séparateur | un espace **ou** `T` majuscule, un seul | formes écrites par le pilote et par ISO 8601 |
| Heure `HH` | `00` à `23` | **Q-C2** |
| Minutes `mm` | `00` à `59` | **Q-C2** |
| Secondes `ss` | `00` à `59`, **obligatoires** | **Q-C2** : `DateTime` écrit toujours les secondes |
| Fraction | facultative, 1 à 7 chiffres | le pilote écrit `FFFFFFF` et omet les zéros de fin |
| Suffixe | facultatif : `Z`, ou `+` / `-` suivi de `HH:mm` sur deux chiffres chacun | **structure seule** : jamais appliqué |
| Fin | `\z` | un saut de ligne final est refusé |
| Chiffres | `[0-9]` | `\d` accepterait des chiffres non ASCII |

**La transformation ne change pas.** La valeur écrite est `substring(0, 10)`. Aucun fuseau n'est appliqué, aucune
conversion UTC, aucun changement de jour : le suffixe disparaît avec l'heure.

### A.2 Arbre de décision final

Remplace le §1.2 de la spécification. L'ordre est inchangé.

```
valeur NULL                                                → NotApplicable            : rien
canonique                                                  → Canonical                : rien
lisible ET tronquable ET date(tête) = date lue             → NormalizableReadable     : écrire tête
lisible (sinon)                                            → ReadableLeftAsIs         : rien, signalé en audit
illisible ET forme historique (§A.1) ET tronquable         → RepairableLegacyDateTime : écrire tête
illisible (sinon)                                          → Unrepairable             : rien, REFUS du démarrage (Q2)
```

S'ajoute au §1.5 une septième règle de troncature : **la troncature d'une valeur illisible n'est accordée qu'à une
valeur de forme historique (§A.1).** Une tête valide ne suffit plus.

### A.3 Validation horaire (Q-C2)

La Phase C contrôlait la structure seule, si bien que `1985-03-15 24:00:00` était repris. **Ce n'est plus le cas.**

- `DateTime` n'écrit jamais `24:00:00`, une minute `60` ni une seconde `60` : une telle valeur ne vient pas de
  l'application, et son sens n'est pas établi.
- ISO 8601 lit `24:00:00` comme **minuit du lendemain**. Tronquer au jour écrit revient donc déjà à choisir une
  interprétation.
- Le suffixe de fuseau reste contrôlé sur sa **structure** seule. Il n'est jamais appliqué, donc sa valeur est
  sans effet sur le résultat : `+99:99` est accepté.

### A.4 Formats acceptés

| Forme | Exemple | État | Résultat |
|---|---|---|---|
| `yyyy-MM-dd HH:mm:ss` | `1985-03-15 00:00:00`, `1985-03-15 23:59:59` | `RepairableLegacyDateTime` | `1985-03-15` |
| `yyyy-MM-dd HH:mm:ss.f{1,7}` | `1985-03-15 12:30:45.1234567` | `RepairableLegacyDateTime` | `1985-03-15` |
| `yyyy-MM-ddTHH:mm:ss[.f{1,7}]` | `1985-03-15T12:30:45.123` | `NormalizableReadable` (déjà lue) | `1985-03-15` |
| suffixe `Z` | `1985-03-15T12:30:45Z` | `RepairableLegacyDateTime` | `1985-03-15` |
| suffixe `±HH:mm` | `1985-03-15T12:30:45+01:00`, `1985-03-15T23:59:59-05:00` | `RepairableLegacyDateTime` | `1985-03-15`, **jamais le 16** |

### A.5 Valeurs refusées

Toutes sont **illisibles**, donc classées `Unrepairable`. Elles ne sont jamais transformées et refusent le démarrage
(Q2).

| Motif | Exemples | Décision |
|---|---|---|
| Heure hors bornes | `1985-03-15 24:00:00`, `… 25:00:00`, `… 12:60:00`, `… 12:00:60`, `… 23:59:60`, `…T24:00:00`, `… 24:00:00Z` | **Q-C2** |
| Fraction de plus de 7 chiffres | `1985-03-15 12:30:45.12345678` | D-B1 |
| Secondes absentes | `1985-03-15 12:30` | D-B1 |
| Suffixe inconnu | `1985-03-15garbage`, `… 12:30:45 garbage`, `…+0100`, `…+01`, `… +01:00`, `…z`, `…ZZ`, `…\n` | D-B1 |
| Séparateur inconnu | `1985-03-15X12:30:45`, double espace | D-B1 |
| Date inexistante | `2026-02-30 00:00:00`, `2025-02-29T00:00:00+01:00`, `0000-01-01 00:00:00` | §1.5 |
| Incompréhensible | `abc`, `""`, `"   "`, `15/03/1985` | §1.3 |

**Hors du champ de la grammaire : les valeurs déjà lues** (Q-C1). La grammaire ne régit que la troncature des
valeurs **illisibles**. Une valeur que le lecteur `Microsoft.Data.Sqlite` lit déjà garde le sens que ce lecteur lui
donne ; sa remise en forme reste permise quand elle est prouvée neutre. Mesuré sur le lecteur réel :

| Valeur | Lue ? | État |
|---|---|---|
| `1985-03-15T12:30`, `1985-03-15t12:30:45`, `1985-03-15␣` | oui | `NormalizableReadable` |
| `1985-03-15T12:30:45.12345678` | **oui** (→ 15 mars) | `NormalizableReadable` → `1985-03-15` — **Q-C6, VALIDATED** ([Decisions](#decisions)) |
| `1985-03-15 12:30:45.12345678` | non | `Unrepairable` |
| `1985-03-15T24:00:00`, `1985-03-15T23:59:60` | non | `Unrepairable` |

### A.6 Table de vérité du §1.4, corrigée

Seules les lignes modifiées ou ajoutées figurent ici. Les autres lignes du §1.4 restent exactes.

| Entrée | État (spéc. Phase B) | État final | Décision |
|---|---|---|---|
| `1985-03-15 24:00:00` | `RepairableLegacyDateTime` | **`Unrepairable`** | Q-C2 |
| `1985-03-15 12:30:45+01:00` | `RepairableLegacyDateTime` ⚠️ | `RepairableLegacyDateTime` → `1985-03-15` | D-B1 (réserve levée) |
| `1985-03-15T23:30:00-05:00` | `RepairableLegacyDateTime` ⚠️ | `RepairableLegacyDateTime` → `1985-03-15` | D-B1 (réserve levée) |
| `1985-03-15T12:30:45Z` | `RepairableLegacyDateTime` ⚠️ | `RepairableLegacyDateTime` → `1985-03-15` | D-B1 (réserve levée) |
| `1985-03-15 12:30` | `RepairableLegacyDateTime` ⚠️ | **`Unrepairable`** | D-B1 |
| `1985-03-15garbage` | `RepairableLegacyDateTime` ⚠️ | **`Unrepairable`** | D-B1 |
| `1985-03-15 25:00:00`, `… 12:60:00`, `… 12:00:60` | — | `Unrepairable` | Q-C2 |
| `1985-03-15 12:30:45.12345678` | — | `Unrepairable` | D-B1 |
| `1985-03-15T12:30:45.12345678` | — | `NormalizableReadable` (lue) → `1985-03-15` | Q-C1, Q-C6 |

### A.7 SQL support — prédicat `L'` (remplace `L`)

**Le prédicat `L` du §2.2 ne doit plus être utilisé.** Il accepte toute heure de deux chiffres : il ferait tronquer
par le script support des valeurs que le moteur refuse, et l'invariant de T-B1 (`L(x) ⇒ réécrite`) ne tient plus.

**Mesuré** par une sonde hors dépôt, sur 64 valeurs, avec `Microsoft.Data.Sqlite` 8.0.27 et SQLite 3.50.3 :

| Prédicat | Violations de T-B1 avant Q-C2 | Violations de T-B1 après Q-C2 |
|---|---|---|
| `L` (spécification §2.2) | 0 | **16** (`24:00:00`, `12:60:00`, `23:59:60`, `2024-02-29 24:00:00`…) |
| `L'` (ci-dessous) | 0 | **0** |
| `C` ⇔ `Canonical` | 0 | 0 |

`L'` est `L` suivi de trois bornes. Le `GLOB` garantit déjà que ces six caractères sont des chiffres ASCII, donc la
comparaison de texte à deux chiffres est exacte.

```sql
typeof("BirthDate") = 'text'
AND "BirthDate" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9][ T][0-9][0-9]:[0-9][0-9]:[0-9][0-9]*'
AND (length("BirthDate") = 19
     OR (substr("BirthDate", 20, 1) = '.' AND length("BirthDate") BETWEEN 21 AND 27
         AND substr("BirthDate", 21) NOT GLOB '*[^0-9]*'))
AND substr("BirthDate", 1, 4) <> '0000'
AND date(substr("BirthDate", 1, 10)) IS substr("BirthDate", 1, 10)
AND substr("BirthDate", 12, 2) BETWEEN '00' AND '23'
AND substr("BirthDate", 15, 2) BETWEEN '00' AND '59'
AND substr("BirthDate", 18, 2) BETWEEN '00' AND '59'
```

Conséquences sur la spécification :

- **§2.1** : le sous-ensemble strict devient `AAAA-MM-JJ[ T]HH:MM:SS[.f{1,7}]` avec `HH` de 00 à 23, `MM` et `SS` de
  00 à 59.
- **§2.3 à §2.5** : lire `<L'>` partout où figure `<L>`.
- **§2.6** : la mesure avait été faite avec `L`. Dans le résidu, `1985-03-15garbage` est désormais `Unrepairable`, ce
  qui porte le résidu à 3 `ReadableLeftAsIs`, **4** `Unrepairable` et 1 `NormalizableReadable`. La conclusion (« le
  SQL seul ne rend pas la base démarrable ») en sort renforcée. **Le script complet (§2.4–2.5) n'a pas été
  réexécuté avec `L'`** : c'est l'objet de T-B6.

### A.8 Tests de la spécification — état

| # | État |
|---|---|
| T-B1 | **à réaliser, sur `L'`**. Sur `L`, il échouerait désormais (16 violations mesurées). |
| T-B2 | réalisé en Phase C (`LesValeursAvecDecalage_SontTronquees_SansConversion`) et complété en C2 aux bornes horaires |
| T-B3 | réalisé en Phase C (`UnSuffixeInconnu_EstRefuse_JamaisTronque`) |
| Q-C2 | réalisé en C2 (`UneHeureHorsBornes_EstRefusee_JamaisTronquee`, `LesBornesHorairesIncluses_RestentReparables`…) |
| T-B4 à T-B8 | à réaliser |

---

## D-B3 Final Rule

### B.1 Aucune valeur brute au journal

Une date de naissance est une donnée personnelle ; une date d'ordonnance, une donnée de santé indirecte. Aucune des
deux n'apparaît plus **en clair** au journal ni dans les messages d'erreur.

| Sortie | Contenu |
|---|---|
| `CivilDateOffendingValue.ToString()` | `Customers.BirthDate CustomerId=123 BirthDateHash=sha256:<64 hex>` |
| Ligne de journal | `CIVILDATE REFUSED: <n> unrepairable value(s) (raw values withheld): <20 premières désignations>` |
| Message de `DatabaseMigrationException` | mêmes désignations, « valeur brute non reproduite, à consulter en base par la clé » |

Le message d'exception suit la même règle parce qu'il est recopié ailleurs : au journal par
`LegacyDatabaseRecoveryService` (`LEGACY copy adoption FAILED: {ex.Message}`), et à la sortie de débogage par
`App.axaml.cs`. Masquer la seule ligne `CIVILDATE REFUSED` aurait laissé passer la date par ce second chemin.

**Conservés** : table, colonne, nom et valeur de la clé primaire, compteur total, limite aux 20 premières valeurs.
Les autres lignes `CIVILDATE` (`ok`, `detected`, `repaired`, `FAILURE`) ne portent que des compteurs.

**En mémoire seulement** : `CivilDateOffendingValue.RawValue`, nécessaire au diagnostic par code (`dryRun`). Il ne
doit jamais être journalisé, et un commentaire le signale dans le code.

**Portée** : les deux colonnes, `BirthDate` et `IssueDate` (Q-C4).

### B.2 Empreinte

`sha256:` suivi des 64 chiffres hexadécimaux minuscules du SHA-256 de la valeur stockée, encodée en UTF-8. Le support
la reproduit avec n'importe quel outil standard (`sha256sum`) et confirme ainsi que la valeur lue en base par la clé
est bien celle qui a été refusée. Vecteur de référence figé par test : chaîne vide →
`sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.

### B.3 Limite de sécurité

**L'empreinte est une pseudonymisation faible, pas une anonymisation.** Une date de naissance a peu de valeurs
possibles (de l'ordre de 55 000 sur 150 ans) : son SHA-256 non salé se retrouve par dictionnaire en une fraction de
seconde. L'empreinte protège contre la **lecture directe** du journal (fichier partagé, capture d'écran, ticket de
support). Elle ne protège pas contre une personne qui dispose du journal et cherche à retrouver la date.

### B.4 Portée future — P7

| Sujet | Direction |
|---|---|
| Empreinte | HMAC avec une clé propre à l'installation : réellement non réversible, mais suppose une gestion de clé |
| Journal | politique de rétention et d'accès du fichier de migration |
| Diagnostic | commande support `dryRun` (Q7), qui donnerait un accès contrôlé à `RawValue` |

---

## Decisions

| # | Statut | Décision |
|---|---|---|
| **Q-C1** | **VALIDATED** | Les valeurs déjà lisibles par `Microsoft.Data.Sqlite` peuvent être normalisées si la transformation est déterministe (`1985-03-15T12:30` → `1985-03-15`). La grammaire D-B1 ne régit que la troncature des valeurs illisibles. Mise en œuvre : la troncature n'est appliquée à une valeur lue que si elle est prouvée neutre, c'est-à-dire que la date lue avant et après est la même. Sinon, la valeur reste `ReadableLeftAsIs` (Q3, P4-7). |
| **Q-C2** | **VALIDATED** | Validation structurelle et bornes horaires : `00 ≤ HH ≤ 23`, `00 ≤ mm ≤ 59`, `00 ≤ ss ≤ 59`, secondes obligatoires, fraction de 1 à 7 chiffres, suffixe contrôlé sur sa structure seule. Une heure impossible est `Unrepairable` : `24:00:00` n'est plus repris. Le prédicat SQL support passe de `L` à `L'`. |
| **Q-C3** | **ACCEPTED TEMPORARILY** | Le SHA-256 non salé suffit pour ce lot, avec la limite décrite au §B.3. Une protection renforcée est prévue en P7 (§B.4). |
| **Q-C4** | **VALIDATED** | `BirthDate` et `IssueDate` suivent la même politique (`CustomerId` / `BirthDateHash`, `PrescriptionId` / `IssueDateHash`). |
| **Q-C5** | **VALIDATED** | Documentation Phase B alignée par le présent addendum. La spécification n'est pas réécrite ; les sections amendées sont listées dans la partie [Purpose](#purpose). |
| **Q-C6** | **VALIDATED** | `1985-03-15T12:30:45.12345678` reste `NormalizableReadable` → `1985-03-15`. `Microsoft.Data.Sqlite` lit cette valeur (15 mars) ; il n'y a ni conversion de fuseau ni invention de date, et seule la partie civile `yyyy-MM-dd` est conservée. La forme à espace, `1985-03-15 12:30:45.12345678`, est illisible et reste `Unrepairable`. Figé par `HuitDecimales_NeSontJamaisTronquees_MaisUneValeurDejaLueResteNormalisee`. |

Toutes les questions de la Phase C sont tranchées. Le bilan du lot figure dans le
[rapport final P4-5D-R](P4-5D-R-final-implementation-report.md).
