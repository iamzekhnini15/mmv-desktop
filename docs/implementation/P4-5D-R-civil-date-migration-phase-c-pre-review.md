# P4-5D-R Phase C Pre-review

> **Rapport de pré-revue — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`.
> Référence : [spécification Phase B](P4-5D-R-civil-date-migration-phase-b-specification.md) (approuvée).
> Objet : implémentation des décisions architecte **D-B1** (restriction de la troncature) et **D-B3**
> (aucune date en clair au journal).

---

## 1. SHA avant / après

| | SHA |
|---|---|
| Avant | `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60` |
| Après | `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60` — **inchangé** (aucun commit) |

Branches locales : `main`, `p4-multi-poste` — **aucune branche créée**. Aucun push.

---

## 2. Fichiers modifiés et lignes

Delta **de cette phase uniquement**.

| Fichier | + | − | Nature |
|---|---|---|---|
| `src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs` | 47 | 8 | D-B1 : grammaire + contrôle dans `Classify` |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs` | 40 | 4 | D-B3 : `CivilDateOffendingValue` (clé, empreinte, `ToString` masqué) |
| `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` | 9 | 3 | D-B3 : ligne `CIVILDATE REFUSED` + message d'exception |
| `tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs` | 145 | 0 | T-B2, T-B3, non-régression |
| `tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs` | 183 | 0 | intégration D-B1 / D-B3 |
| `tests/MMV.Domain.Tests/Data/Time/CivilDateRepairPreparationTests.cs` | 106 | 1 | démarrage réel D-B1 / D-B3 |
| **Total** | **530** | **16** | code : +96 / −15 · tests : +434 / −1 |

**Méthode de mesure.** Les fichiers `Time/*` ne sont pas suivis par git (ajoutés en V1, non commités) :
`git diff` ne voit pas leur delta. Leur état d'avant cette phase a été reconstruit hors dépôt, puis comparé par
`git diff --no-index --numstat`. **Contrôle :** le `SqliteDatabaseManager` reconstruit, comparé à `HEAD`, redonne
exactement **+89 / −1**, l'état déclaré en V1. Cumul actuel contre `HEAD` : **+95 / −1**.

### Non modifiés

| Élément | Vérification | Résultat |
|---|---|---|
| `SqliteCivilDateRepairService.cs` | relecture | **inchangé** — il consomme `Classify`, il n'a pas sa propre règle |
| Migrations EF | `git status --short src/MMV.Infrastructure/Migrations` | **vide** |
| `.csproj` / `.sln` | `git status --short '*.csproj' '*.sln'` | **vide** |
| ADR | `git status --short docs/adr docs/architecture/ADR*` | **vide** |
| CI | `git status --short .github` | **vide** |
| PostgreSQL, schéma SQLite, stratégie `DateOnly` | — | **non touchés** |
| Multi-poste (P4-6) | — | **non touché** |
| Spécification Phase B, roadmap | — | **non modifiées** (roadmap : toujours +20 / −0 depuis V2) |

---

## 3. Décisions implémentées

### 3.1 D-B1 — Restriction de la reprise des dates

**Emplacement unique : `CivilDateFormat`.** Aucun second moteur. Le vérificateur, le service de reprise et le
gestionnaire de base appellent tous `Classify`, et sont donc couverts sans modification.

- **Grammaire** (`CivilDateFormat.IsLegacyDateTimeForm`) :

  ```
  ^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?\z
  ```

  Soit `yyyy-MM-dd[ T]HH:mm:ss[.f{1,7}][Z|±HH:mm]`. `[0-9]` plutôt que `\d`, qui accepte des chiffres non
  ASCII ; `\z` plutôt que `$`, qui accepte un saut de ligne final. Les deux pièges sont couverts par un test.
- **Contrôle** dans `Classify`, branche **illisible** :
  `IsLegacyDateTimeForm(raw) && TryTruncateToCivilDate(raw, …)` ⇒ `RepairableLegacyDateTime`, sinon `Unrepairable`.
- **Aucun fuseau appliqué.** La valeur écrite reste `substring(0, 10)` : le suffixe `Z` ou `±HH:mm` disparaît
  avec l'heure. Aucune conversion UTC, aucun calcul, aucun changement de jour.

#### Effet mesuré

Mesure faite par une sonde hors dépôt : 49 valeurs, classées avant et après, et confrontées au lecteur réel
`Microsoft.Data.Sqlite` 8.0.27.

| Valeurs | Avant | Après |
|---|---|---|
| `1985-03-15T12:30:45Z`, `…T12:30:45+01:00`, `…T23:30:00-05:00`, `… 00:30:00+01:00`, `…T12:30:45.1234567Z`, `… 12:30:45.123-05:00` | `RepairableLegacyDateTime` → `1985-03-15` | **inchangé** → `1985-03-15` |
| `1985-03-15 12:30:45`, `… 12:30:45.1`, `… 12:30:45.1234567`, `… 24:00:00`, `2024-02-29 00:00:00` | `RepairableLegacyDateTime` | **inchangé** |
| `1985-03-15garbage`, `1985-03-15 12:30`, `… 12:30:45 garbage`, `… .12345678`, `…+0100`, `…+01`, `… +01:00`, `…+01:00:00`, `…z`, `…ZZ`, `…Z+01:00`, `…X12:30:45`, double espace, `… 12:30:4`, `… 12:30:45.`, `…\n`, chiffres arabes-indiens | `RepairableLegacyDateTime` | **`Unrepairable`** (17 valeurs) |
| Canoniques, `ReadableLeftAsIs`, `NormalizableReadable`, dates inexistantes, `abc`, `""` | — | **inchangé** |

**Lecteur réel et règle : 49 / 49 concordants, avant et après.**

#### Choix d'interprétation (voir §8)

1. **Fraction de 1 à 7 chiffres.** Le pilote écrit `DateTime` en `FFFFFFF` et omet les zéros de fin ;
   `.123` existe donc réellement en base. Ce cas était déjà couvert par un test existant.
2. **Portée : branche illisible uniquement.** Une valeur hors grammaire mais **déjà lue** par le modèle courant
   (`1985-03-15T12:30`, `1985-03-15t12:30:45`, `1985-03-15␣`) reste `NormalizableReadable`. Son sens est établi
   par le lecteur lui-même, et la réécriture est prouvée neutre. Une telle valeur ne peut de toute façon jamais
   devenir `Unrepairable`. → **Q-C1**
3. **Contrôle structurel, pas sémantique.** La grammaire vérifie les chiffres et les séparateurs, pas les bornes
   horaires : `24:00:00` reste repris, comme le prévoient la spécification (§1.4) et le test existant
   `UneHeureInvalide_NEmpechePasLaReprise_DeLaPartieDate`. C'est aussi ce qui garde le C# plus large que le prédicat
   SQL support `L` (§2.2), condition de T-B1 (`L(x) ⇒ réécrite`). → **Q-C2**

### 3.2 D-B3 — Journalisation des données sensibles

Le masquage s'applique **à la source** : `CivilDateOffendingValue.ToString()`, seule forme destinée au journal
et aux messages, ne rend **plus jamais** la valeur brute.

| Élément | Avant | Après |
|---|---|---|
| `ToString()` | `Customers.BirthDate (id=123) = '1985-03-15garbage'` | `Customers.BirthDate CustomerId=123 BirthDateHash=sha256:1e8212494e9672b7bf482087690007ead272b9403ec1bae01e64f860152e5a6a` |
| Propriétés ajoutées | — | `KeyColumn` (`CustomerId` / `PrescriptionId`), `RawValueHash` |
| Ligne de journal | `CIVILDATE REFUSED: 1 unrepairable value(s): <valeurs en clair>` | `CIVILDATE REFUSED: 1 unrepairable value(s) (raw values withheld): <désignations>` |
| Message d'exception | `Valeurs concernées : <valeurs en clair>` | `Valeurs concernées, désignées par leur clé et l'empreinte SHA-256 de la valeur stockée (valeur brute non reproduite, à consulter en base par la clé) : <désignations>` |

**Pourquoi le message d'exception aussi.** Il est recopié au journal par
[`LegacyDatabaseRecoveryService.cs:192`](../../src/MMV.Infrastructure/Data/LegacyDatabaseRecoveryService.cs)
(`LEGACY copy adoption FAILED: {ex.Message}`) et envoyé en sortie de débogage par
[`App.axaml.cs:270`](../../src/MMV.App/App.axaml.cs). Masquer la seule ligne `CIVILDATE REFUSED` aurait laissé
la date passer dans le journal par ce second chemin.

**Conservé :** table, colonne, clé primaire, compteur total, limite aux 20 premières valeurs, et la possibilité
de diagnostic. L'empreinte est `sha256:` suivi des 64 chiffres hexadécimaux minuscules du SHA-256 de la valeur en
UTF-8. Le support la reproduit avec n'importe quel outil standard (`sha256sum`) pour confirmer que la valeur lue
en base par la clé est bien celle qui a été refusée. Un test fige le vecteur de référence publié de la chaîne vide
(`e3b0c442…b855`).

**Conservé en mémoire :** `RawValue`. Il reste nécessaire au diagnostic par code (`dryRun`), et un commentaire
indique qu'il ne doit jamais être journalisé.

**Périmètre :** les deux colonnes. `IssueDate` (date d'ordonnance, donnée de santé indirecte) est traitée comme
`BirthDate`, avec `PrescriptionId=` et `IssueDateHash=`. → **Q-C4**

Les autres lignes `CIVILDATE` (`ok`, `detected`, `repaired`, `FAILURE`) ne portent que des compteurs. Cela a été
vérifié à la relecture du code, sans modification nécessaire.

---

## 4. Tests ajoutés

**+53 cas** (67 → 120 sur les trois classes). Aucun test supprimé, ignoré ou affaibli.

### `CivilDateFormatTests` — règle pure (36 → 82)

| Test | Cas | Exigence |
|---|---|---|
| `LesValeursAvecDecalage_SontTronquees_SansConversion` | 7 | **T-B2** : `Z`, `+01:00`, `-05:00` (+ variantes espace/fraction) → toujours `1985-03-15` |
| `UneConversionUtc_AuraitChangeLeJour_CeQueLaRegleNeFaitPas` | 1 | preuve que T-B2 est discriminant : en UTC, ces valeurs tombent le 16 et le 14 |
| `UnSuffixeInconnu_EstRefuse_JamaisTronque` | 17 | **T-B3** : `1985-03-15garbage`, `1985-03-15 12:30`, pièges `$`/`\d`… → `Unrepairable` |
| `LesFormesHistoriquesAutorisees_RestentReprises` | 9 | non-régression : les 4 formes, avec et sans suffixe ISO |
| `UneDateInexistante_ResteRefusee_MemeSousUneFormeConforme` | 6 | non-régression : dates invalides refusées même sous une forme conforme |
| `LaRepriseDesFormesAvecDecalage_EstUnPointFixe` | 3 | idempotence |
| `D_B1_NeRestreintPas_LaMiseEnFormeNeutre_DUneValeurDejaLue` | 3 | fige l'interprétation Q-C1 |

### `SqliteCivilDateRepairTests` — SQLite en mémoire (21 → 26)

| Test | Exigence |
|---|---|
| `LesValeursAvecDecalage_SontReprisesEnBase_SansConversion` | T-B2 de bout en bout, deux colonnes, relecture EF en `DateOnly` |
| `UnSuffixeInconnu_EstSignaleEnBase_EtJamaisTronque` | T-B3 de bout en bout ; la forme historique voisine reste reprise |
| `LaRepriseDesFormesAvecDecalage_EstIdempotente_EtLesRefusStables` | idempotence ; refus identique à chaque passage |
| `LaReprise_NeModifieAucuneAutreColonne_NiAucunInstant` | colonnes d'instants chargées des **mêmes** suffixes (`Z`, décalage, fraction) ; instantané de **toutes** les autres colonnes identique |
| `UneValeurNonReparable_SeJournalise_SansSaValeurBrute` | format D-B3 exact, deux colonnes, vecteur SHA-256 de référence |

### `CivilDateRepairPreparationTests` — démarrage réel sur fichiers (10 → 12)

| Test | Exigence |
|---|---|
| `UnSuffixeInconnu_RefuseLeDemarrage_SansJournaliserLaDate` | D-B1 refuse le démarrage ; D-B3 vérifié sur le journal en mémoire, **le fichier journal** et le message d'exception ; rien n'est écrit en base |
| `LeJournalDeRefus_PermetDeRetrouverEtDeConfirmerChaqueValeur` | diagnostic : la clé désigne la ligne, l'empreinte de la valeur relue par la clé correspond au journal |

### Tests existants modifiés — 2, déclarés

| Test | Modification | Motif |
|---|---|---|
| `UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier` | l'assertion `WithMessage("*pas-une-date*")`, qui **exigeait la valeur en clair**, est remplacée par : message contient `BirthDateHash=<sha256>` **et ne contient pas** `pas-une-date` | contraire à D-B3 ; assertion **renforcée** |
| `LaClassification_EstFidele_AuLecteurReel` | corpus étendu de 8 valeurs (T-B2, T-B3, formes lisibles hors grammaire) | ajout pur |

**Pouvoir discriminant.** La sonde exécutée **avant** modification classait les 17 valeurs T-B3 en
`RepairableLegacyDateTime` : ces tests échouent sur le code d'avant cette phase. Les tests D-B3 échouent eux aussi
sur ce code, puisque son `ToString` inscrivait la valeur brute.

---

## 5. Résultats build / tests

```
dotnet build MMV.sln -c Debug
→ La génération a réussi.  0 Avertissement(s)  0 Erreur(s)
```

```
dotnet test MMV.sln -c Debug
→ MMV.App.Tests          échec : 0, réussite :  258, ignorée(s) : 0, total :  258
→ MMV.Application.Tests  échec : 0, réussite :  628, ignorée(s) : 0, total :  628
→ MMV.Domain.Tests       échec : 0, réussite :  992, ignorée(s) : 0, total :  992
```

| Projet | Avant | Après | Échecs | Ignorés |
|---|---|---|---|---|
| `MMV.Domain.Tests` | 939 | **992** | 0 | 0 |
| `MMV.Application.Tests` | 628 | 628 | 0 | 0 |
| `MMV.App.Tests` | 258 | 258 | 0 | 0 |
| **Total** | 1 825 | **1 878** | **0** | **0** |

Seuil de la spécification (§4.3 : « 1 825 + nouveaux tests, 0 ignoré, 0 avertissement ») : **atteint**.

---

## 6. Migrations vérifiées

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
 M docs/architecture/P4-multi-poste-roadmap.md                         (V2, inchangé)
 M src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs                (+95 / −1 contre HEAD)
?? docs/architecture/P5-product-completion-roadmap.md                  (préexistant, non touché)
?? docs/implementation/P4-5D-R-civil-date-migration-phase-b-specification.md
?? docs/implementation/P4-5D-R-civil-date-migration-phase-c-pre-review.md   (ce rapport)
?? docs/implementation/P4-5D-R-civil-date-migration-pre-review-v2.md
?? docs/implementation/P4-5D-R-civil-date-migration-pre-review.md
?? docs/implementation/P4-5D-final-implementation-report.md            (préexistant, non touché)
?? src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs
?? src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs
?? src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs
?? tests/MMV.Domain.Tests/Data/Time/CivilDateFormatTests.cs
?? tests/MMV.Domain.Tests/Data/Time/CivilDateRepairPreparationTests.cs
?? tests/MMV.Domain.Tests/Data/Time/SqliteCivilDateRepairTests.cs
```

---

## 8. Risques restants

### Nouveaux

| # | Risque | Statut |
|---|---|---|
| **RC1** | **D-B1 élargit la surface de refus.** Toute valeur au suffixe inconnu bloque désormais le démarrage (Q2). Le volume réel reste inconnu, ce qui rend la simulation `dryRun` sur copie réelle (**RR4**) plus nécessaire encore. | assumé par décision, **à mesurer** |
| **RC2** | **Empreinte non salée = pseudonymisation faible.** Une date a peu de valeurs possibles ; son SHA-256 se retrouve par dictionnaire. L'empreinte protège contre la lecture directe du journal (fichier partagé, capture d'écran), pas contre un attaquant déterminé. Documenté dans le code. | ouvert → **Q-C3**, P7 |
| **RC3** | **`RawValue` reste accessible en mémoire** (`CivilDateRepairReport.UnrepairableValues[i].RawValue`). Un futur appelant qui la journaliserait directement contournerait D-B3. `ToString` est sûr par défaut ; seul le chemin actuel est couvert par test. | ouvert, faible |
| **RC4** | **Spécification Phase B partiellement dépassée** : §1.4 (5 lignes ⚠️ D-B1, dont `garbage` et `12:30`), §1.7 (`CIVILDATE REFUSED` : « valeur brute »), §2.6 (`1985-03-15garbage` classé réparable dans le résidu), §5 D-B3 (recommandation « conserver la valeur brute »). Selon son §0, le code et ses tests font foi. | réconciliation documentaire → **Q-C5** |

Aucun journal existant n'est à purger : le code du V1 qui inscrivait les valeurs brutes n'a jamais été commité
ni livré.

### Reportés, inchangés

RR2 (incohérence de roadmap, Q6), RR3 (refus bloquant, assumé), **RR4 (`dryRun` sur base réelle, préalable au
déploiement, non exécutable depuis ce poste)**, RR5 (canonicalisation → P4-7), RR6 / D-B4 (multi-poste → P4-6),
RR7 (coût de démarrage), RR8 (règle du `Kind`, Q4). D-B2 (`""` refusé) : comportement conservé, inchangé.

### Tests de la spécification non réalisés dans ce lot

T-B1, T-B4, T-B5, T-B6, T-B7, T-B8 (§4.3). Ce lot était limité à T-B2, T-B3 et à la non-régression. L'invariant
de T-B1 (`L(x) ⇒ réécrite`) reste vrai par construction : la grammaire C# contient strictement le prédicat SQL `L`.

---

## 9. Questions architecturales

| # | Question | Option retenue | Alternative |
|---|---|---|---|
| **Q-C1** | La grammaire D-B1 doit-elle régir aussi la **mise en forme neutre** des valeurs déjà lues ? | **Non** : elle ne régit que la troncature des valeurs illisibles. `1985-03-15T12:30`, `1985-03-15t12:30:45` et `1985-03-15␣` restent normalisés, la neutralité étant prouvée ligne à ligne. | Oui : ces valeurs deviennent `ReadableLeftAsIs`, jamais `Unrepairable`, donc aucun refus. Changement d'une ligne, et le test `D_B1_…` s'inverse. |
| **Q-C2** | Contrôle **structurel** ou **sémantique** des heures et décalages ? Fraction de 1 à 7 chiffres ? | **Structurel.** `24:00:00` reste repris, `+99:99` est accepté ; fraction de 1 à 7 chiffres. Cohérent avec §1.4, le test existant et le prédicat SQL `L`. | Sémantique : `24:00:00` devient `Unrepairable` (DateTime ne l'écrit jamais, et ISO 8601 le lit comme minuit **du lendemain**). Il faudrait alors restreindre aussi le prédicat SQL §2.2. |
| **Q-C3** | Le SHA-256 non salé suffit-il comme protection minimale jusqu'à P7 ? | **Oui**, limite documentée (RC2). | HMAC avec clé propre à l'installation : réellement non réversible, mais exige une gestion de clé (P7). |
| **Q-C4** | D-B3 s'applique-t-il à `IssueDate` ? | **Oui** (`IssueDateHash`). | Valeur en clair pour `IssueDate` seulement : déconseillé (donnée de santé indirecte). |
| **Q-C5** | Réconcilier la spécification Phase B (§1.4, §1.7, §2.6, §5) : dans ce lot ou dans un lot documentaire séparé ? | **Non faite** : la spécification n'était pas dans le périmètre d'écriture. | Amendement additif de la spécification avant commit. |

---

## 10. Verdict

**D-B1 et D-B3 sont implémentés, rien d'autre.** Build propre, **1 878 / 1 878**, aucune migration, aucun
changement de schéma, de modèle, d'ADR, de `.csproj` ou de CI.

**Statut : `READY FOR ARCHITECT REVIEW`.** Aucun commit, aucun push. En attente de validation, notamment sur
**Q-C1** et **Q-C2**, les deux seuls points qui peuvent entraîner une modification de code.

---

## Architect Decisions

> Décisions rendues par le Lead Software Architect en revue de ce rapport, puis confirmées lors de la
> consolidation finale (21 septembre 2026).

| # | Statut | Décision | Écart avec l'option retenue au §9 |
|---|---|---|---|
| **Q-C1** | **VALIDATED** | Les valeurs déjà lisibles par `Microsoft.Data.Sqlite` peuvent être normalisées si la transformation est déterministe (`1985-03-15T12:30` → `1985-03-15`). | aucun |
| **Q-C2** | **VALIDATED** | Heures bornées : `HH` de 00 à 23, `mm` et `ss` de 00 à 59. Les valeurs impossibles sont `Unrepairable`. | **inversé** : l'option retenue ici (contrôle structurel, `24:00:00` repris) est **écartée**. Implémenté en [Phase C2](P4-5D-R-phase-c2-pre-review.md). |
| **Q-C3** | **ACCEPTED TEMPORARILY** | SHA-256 non salé acceptable dans ce lot. La protection renforcée relève de P7 (Security Hardening). | aucun |
| **Q-C4** | **VALIDATED** | `BirthDate` et `IssueDate` suivent la même politique. | aucun |
| **Q-C5** | **VALIDATED** | Documentation Phase B alignée par l'[addendum](P4-5D-R-phase-b-addendum.md), sans réécriture de la spécification. | **inversé** : la réconciliation, « non faite » ici, est faite en C2 |

Q-C6, soulevée en Phase C2, est tranchée elle aussi (**VALIDATED**). Voir le
[rapport final P4-5D-R](P4-5D-R-final-implementation-report.md).
