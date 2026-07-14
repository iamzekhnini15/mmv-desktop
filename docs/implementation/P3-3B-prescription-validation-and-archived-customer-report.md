# P3-3B — Validation métier réelle des ordonnances et protection du client archivé

> **Mode : `IMPLEMENTATION_AND_REPORT`.** Aucune UI, aucune migration, aucun package, aucun workflow modifié.
> **Aucun commit, aucun push.**
> **Le dépôt réel prime sur le prompt et sur les rapports antérieurs.**
>
> Branche : `p3-business-rules`. Étape précédente : **P3-3A** (audit, `GO`, CI verte).
> Cadré par le [rapport P3-3A](P3-3A-prescription-domain-audit-report.md), la
> [roadmap P3-3](../architecture/P3-business-rules-roadmap.md), l'[ADR frontières §8/§9](../architecture/adr-application-boundaries.md),
> l'[ADR-PROD-DB-001](../architecture/adr-prod-db-001-multi-poste-database-strategy.md), les
> [notes de transposition](../domain/P3-workshop-sheet-and-transposition-notes.md), et les socles
> [P3-1](P3-1-validation-result-report.md) et [P3-2B](P3-2B-customer-archiving-and-deletion-report.md).

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-3B |
| `EXECUTION_MODE` | `IMPLEMENTATION_AND_REPORT` |
| `SOURCE_BRANCH` | `p3-business-rules` |
| `ALLOW_CODE_CHANGES` / `ALLOW_TEST_CHANGES` / `ALLOW_DOC_CHANGES` | `true` |
| `ALLOW_MIGRATION` / `ALLOW_UI_CHANGES` | **`false`** |
| `ALLOW_COMMIT` / `ALLOW_PUSH` | **`false`** |

---

## 2. Préconditions Git et CI

| Contrôle | Attendu | Résultat |
|---|---|---|
| `git branch --show-current` | `p3-business-rules` | ✅ |
| `git status --short` | propre | ✅ **vide** |
| Dernier commit | `c9c7ffe docs(P3-3A): audit prescription business rules` | ✅ |
| CI distante | verte sur le SHA exact | ✅ |

**Run CI inspecté** (`gh run view 29296145755 --json databaseId,url,headSha,headBranch,status,conclusion`) :

| Élément | Valeur |
|---|---|
| `databaseId` | **`29296145755`** ✅ |
| `headSha` | **`c9c7ffec5ff580060e0e799773820491206b331b`** ✅ (SHA complet attendu) |
| `headBranch` | `p3-business-rules` ✅ |
| `status` / `conclusion` | `completed` / **`success`** ✅ |
| URL | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29296145755 |

**Préconditions = GO.** Aucun fichier restauré, aucun stash.

---

## 3. Baseline locale

| Contrôle | Attendu | Résultat |
|---|---|---|
| `dotnet restore` | OK | ✅ |
| `dotnet build --no-restore -c Debug` | vert | ✅ **0 avertissement, 0 erreur** |
| `dotnet test --no-build -c Debug` | 645 | ✅ **645** — App **211** · Application **198** · Domain **236** ; 0 échec |
| `dotnet list … --vulnerable --include-transitive` | 0 | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK | ✅ `dotnet-ef` 8.0.27 |
| `ef migrations has-pending-model-changes` | aucune | ✅ « No changes … since the last migration » |
| `MMV.Application` — références projet | `MMV.Domain` seule | ✅ |
| `MMV.Application` — packages top-level | `DI.Abstractions 8.0.1` seul | ✅ |

**Baseline = GO.**

---

## 4. Audit ciblé avant modification

Lectures intégrales : `Prescription`, `PrescriptionValidator`, `PrescriptionService`, `CommandValidation`,
`ValidationError`, `CreateCustomerUseCase`/`Result`, `UpdateCustomerUseCase`/`Result`, `DeleteCustomerUseCase`
(patron du message constant), les use cases `CreatePrescription` / `UpdatePrescription` / `DeletePrescription`,
`ICustomerRepository`, `IGenericRepository`, `IUnitOfWork`, `Customer` (`IsArchived`, `Archive()`), les deux
`DependencyInjection.cs`, et les tests existants (Domain + Application + App).

Recherche `rg -n "IPrescriptionService|PrescriptionService|PrescriptionValidator|CommandValidation" src tests`.

**Deux constats structurants confirmés dans le code, pas seulement dans l'audit :**

1. `PrescriptionValidator` était **du code mort** — aucun use case ne l'appelait. Les plages
   sphère/cylindre/axe/addition n'étaient opposées à l'utilisateur **que par l'UI**.
2. `CreatePrescriptionUseCase` n'injectait **même pas** `ICustomerRepository` : une ordonnance était créable pour un
   client **archivé** comme pour un client **inexistant** (la FK produisant alors une `DbUpdateException` brute).

---

## 5. Traitement de `PrescriptionService`

**Recherche runtime finale** (`rg -n "IPrescriptionService|PrescriptionService" src tests`) — occurrences trouvées
**avant** suppression :

| Occurrence | Nature | Compte comme consommateur runtime ? |
|---|---|---|
| `src/MMV.Domain/Services/PrescriptionService.cs` (interface **et** implémentation, même fichier) | définition | **non** |
| `src/MMV.Infrastructure/DependencyInjection.cs:67` | enregistrement DI | **non** |
| `tests/MMV.Domain.Tests/ServiceTests/PrescriptionServiceTests.cs` (4 tests) | tests | **non** |

> **Aucun consommateur runtime réel.** Aucune ViewModel, aucun use case, aucun service ne l'injecte ni ne l'appelle.

**Décision appliquée : suppression.** C'était un **second chemin d'écriture** (`CreatePrescriptionAsync` /
`UpdatePrescriptionAsync` écrivant directement via `IUnitOfWork`), **enregistré dans la DI**, qui court-circuitait les
use cases — donc **tous** les garde-fous ajoutés par P3-3B. Dormant hier, piège demain.

- 🗑️ `src/MMV.Domain/Services/PrescriptionService.cs` **supprimé** (interface + classe) ;
- 🗑️ son enregistrement DI **supprimé** (remplacé par un commentaire expliquant pourquoi) ;
- 🗑️ `tests/MMV.Domain.Tests/ServiceTests/PrescriptionServiceTests.cs` **supprimé** (4 tests devenus sans objet).

**Contrôle final** : `rg -n "IPrescriptionService|PrescriptionService" src tests` ⇒ **une seule ligne**, le
**commentaire** d'explication dans `MMV.Infrastructure/DependencyInjection.cs`. **Aucun code, aucune DI, aucun test.**

> La seule règle que ce service portait (`IssueDate > UtcNow + 1 j` ⇒ `BusinessRuleException`) est **conservée et
> généralisée** : elle vit désormais dans `PrescriptionValidator`, **réellement exécuté** par les deux use cases.

---

## 6. Règles cylindre / axe

Implémentées dans `PrescriptionValidator`, **symétriquement pour OD et OG** (une méthode privée `AddEyeRules` est
appelée une fois par œil : la symétrie est garantie **par construction**, pas par recopie).

| Cas | Verdict |
|---|---|
| `Cylinder` **null** + `Axis` null | ✅ valide |
| `Cylinder = 0` + `Axis` null | ✅ valide (0 = pas d'astigmatisme) |
| `Cylinder ≠ 0` + `Axis` **null** | ❌ **invalide** — `AxisRequiredMessage` |
| `Cylinder` null + `Axis` renseigné | ❌ **invalide** — `AxisWithoutCylinderMessage` (axe orphelin) |
| `Cylinder = 0` + `Axis` renseigné | ❌ **invalide** — `AxisWithoutCylinderMessage` |
| `Cylinder ≠ 0` + `Axis = 0` | ✅ **valide en saisie** (normalisé à 180 en aval) |
| `Cylinder ≠ 0` + `Axis = 180` | ✅ valide |
| `Axis` hors `[0, 180]` | ❌ invalide |

**Sémantique exacte `value == 0d`**, sans tolérance arbitraire : les valeurs optiques sont saisies au **quart de
dioptrie**, fractions binaires **exactes** en `double` (P3-3A §4.7). Un cylindre `NaN` n'est pas considéré comme
« orientable » : il est déjà refusé par la règle de finitude, et ne réclame donc pas en plus un axe.

---

## 7. Règles prisme / base

| Cas | Verdict |
|---|---|
| `PrismValue` null + `PrismBase` null | ✅ valide |
| `PrismValue > 0` + `PrismBase` renseignée | ✅ valide |
| `PrismValue > 0` + `PrismBase` **null** | ❌ **invalide** — `PrismBaseRequiredMessage` |
| `PrismBase` renseignée + `PrismValue` null | ❌ **invalide** — `PrismBaseWithoutValueMessage` |
| `PrismValue = 0` + `PrismBase` null | ✅ valide (0 = **absence de prisme**) |
| `PrismValue = 0` + `PrismBase` renseignée | ❌ **invalide** — `PrismBaseWithoutValueMessage` |
| `PrismValue < 0` | ❌ **invalide** — `PrismValueNegativeMessage` |
| `PrismValue` NaN / infinie | ❌ **invalide** — `FiniteValueMessage` |
| `PrismValue = 50` (grande, finie, positive, avec base) | ✅ **valide** |

> **AUCUNE borne supérieure n'est imposée à `PrismValue`.** La proposition `[0, 10]` est **écartée** faute de preuve
> métier dans le dépôt (décision verrouillée en P3-3A). Un test (`Validate_LargeFinitePrismValue_ShouldPass`)
> **verrouille** cette décision : introduire un plafond devra être un choix conscient, jamais un effet de bord.

`PrismValue` étant le **seul** champ optique **sans plage**, la règle de finitude est ce qui le protège réellement.

---

## 8. Valeurs numériques finies

Pour les 8 propriétés `double?` (`Sphere`, `Cylinder`, `Addition`, `PrismValue` × OD/OG) : toute valeur **renseignée**
doit être **finie**. `double.NaN`, `double.PositiveInfinity` et `double.NegativeInfinity` sont **refusés**.

**Ordre d'évaluation** : la finitude est vérifiée **d'abord**, et les plages ne s'appliquent **que** sur une valeur
finie (`.When(p => IsFinite(...))`). Motif : une comparaison de plage avec `NaN` est toujours fausse et produirait un
message trompeur (« hors plage » au lieu de « valeur non finie »), et `PrismValue` — sans borne supérieure — n'aurait
été protégée par rien.

Les plages existantes restent appliquées : Sphere `[-20, 20]` · Cylinder `[-6, 6]` · Addition `[0, 4]` · Axis `[0, 180]`.

**`IssueDate`** : la borne `≤ UtcNow + 1 j` est désormais évaluée **à chaque validation**
(`LessThanOrEqualTo(_ => DateTime.UtcNow.AddDays(1))`, surcharge à lambda) et non **figée à la construction** du
validateur — le bug latent relevé en P3-3A §5.2 est corrigé. **Aucun `IClock` n'est introduit** dans cette phase.

---

## 9. Ordonnance partielle

**Préservée, iso-comportement.** Aucun champ optique n'est rendu obligatoire ; les règles croisées sont
**conditionnelles à la présence** des champs.

- OD seul renseigné ⇒ ✅ ; OG seul renseigné ⇒ ✅ ; un seul champ ⇒ ✅ ;
- **ordonnance totalement vide ⇒ ✅ acceptée** (Domain et Application).

**Aucune règle « au moins une valeur optique » n'est ajoutée** (décision P3-3A : à confirmer après pilote terrain,
pas à imposer).

---

## 10. `DoctorName`

**Reste facultatif** dans Domain et Application (tests : `null`, `""`, `"   "` acceptés). **Aucune** obligation n'est
ajoutée en P3-3B.

L'UI l'exige toujours à tort (`PrescriptionFormViewModel.ValidateDoctorName`) : cette divergence — **sans propriétaire
métier démontré** — sera **alignée en P3-3C**. Aucun fichier UI n'a été touché ici.

---

## 11. Normalisation d'axe

**Nouveau fichier** : `src/MMV.Domain/Optics/OpticalAxisNormalizer.cs` — **fonction Domain pure** :

```
NormalizeAxis(int? axis) :  null → null  |  0 → 180  |  1..180 → inchangé
```

- **sans** repository, **sans** `UnitOfWork`, **sans** dépendance externe, **sans** état ;
- **déterministe**, testable directement (5 cas : `null`, `0`, `1`, `90`, `180`) ;
- **hors** du validateur : `PrescriptionValidator` **valide uniquement** — il ne mute ni commande, ni entité, et
  n'exécute aucun effet de bord. L'axe `0` est **accepté** à la saisie ; la valeur canonique est produite par les use
  cases **après** la validation et **avant** l'écriture.

**Aucune donnée historique n'est réécrite. Aucune migration.** Une ligne existante à `0` le reste jusqu'à son prochain
enregistrement.

**La transposition sphère/cylindre/axe n'est PAS implémentée** — elle reste **intégralement reportée à P3-6B**. P3-3B
n'en fige que les invariants.

---

## 12. `CreatePrescriptionUseCase`

Ordre d'exécution implémenté :

1. commande nulle ⇒ `ArgumentNullException` ;
2. construction d'un **candidat** `Prescription` **non persisté** (jamais suivi par le contexte) ;
3. `CommandValidation.Validate(PrescriptionValidator, candidat)` ;
4. **invalide** ⇒ retour `ValidationErrors` : **le client n'est même pas chargé**, ni `CreateAsync`, ni
   `SaveChangesAsync` ;
5. `ICustomerRepository.GetByIdAsync(command.CustomerId)` ;
6. **introuvable** ⇒ `CustomerFound = false`, **aucune écriture** ;
7. **archivé** ⇒ **`BusinessRuleException`** (`CustomerArchivedMessage`), **aucune écriture** ;
8. normalisation des axes OD **et** OG (`OpticalAxisNormalizer`) ;
9. `CreateAsync` + `SaveChangesAsync` ;
10. succès (`PrescriptionId`, `CustomerFound = true`, `IsValid = true`).

`ICustomerRepository` **ajouté au constructeur** (garde `ArgumentNullException`, cohérente avec le reste du dépôt).
Résolution DI inchangée par ailleurs : le dépôt est déjà enregistré en `Scoped` (même `DbContext`).

**`ITransactionRunner` n'est PAS introduit** : le flux reste **mono-écriture**, et aucun rempart atomique
provider-neutre ne peut garantir `IsArchived = false` entre le contrôle et l'écriture (cf. §22).

---

## 13. `UpdatePrescriptionUseCase`

1. commande nulle ⇒ `ArgumentNullException` ;
2. chargement de l'ordonnance ;
3. **introuvable** ⇒ `PrescriptionFound = false`, **aucune écriture** ;
4. construction d'un **candidat séparé** : valeurs de la commande + **`CustomerId` et `CreatedAt` existants** ;
5. validation du candidat ;
6. **invalide** ⇒ `ValidationErrors` : **l'entité suivie n'est pas mutée** (ni `UpdateAsync`, ni `SaveChangesAsync`) ;
7. normalisation des axes du candidat ;
8. report des valeurs validées et normalisées vers l'entité suivie ;
9. `CustomerId` et `CreatedAt` **préservés** ; `UpdateAsync` + `SaveChangesAsync` ; succès.

> **Différence assumée avec `UpdateCustomerUseCase` (P3-1)** : celui-ci mute l'entité suivie **puis** valide (l'absence
> de `SaveChangesAsync` suffit à ne rien persister). Ici, la validation opère sur un **candidat séparé**, **avant**
> toute mutation : l'entité suivie ne peut donc jamais se retrouver dans un état **partiellement modifié** lors d'un
> retour invalide. C'est le point explicitement exigé pour cette phase.

**Le client n'est délibérément PAS chargé** : `ICustomerRepository` n'est **pas** injecté ici. La **correction** d'une
ordonnance reste autorisée même si le client est archivé (§15).

---

## 14. Résultats Application

Convention **P3-1** réutilisée telle quelle (mêmes types, même forme que `CreateCustomerResult` /
`UpdateCustomerResult`) : `IReadOnlyList<ValidationError>` + `bool IsValid => ValidationErrors.Count == 0`.
**Aucune monade `Result<T>`**, **aucun** `Ardalis.Result`, **aucune** exception pour une validation normale ni pour un
client introuvable.

| `CreatePrescriptionResult` | Type | Sens |
|---|---|---|
| `PrescriptionId` | `long` | 0 si refus |
| **`CustomerFound`** | `bool` (**défaut `true`**) | `false` = client inexistant, aucune écriture |
| `ValidationErrors` | `IReadOnlyList<ValidationError>` | vide = commande valide |
| `IsValid` | `bool` (calculé) | — |

> **Défaut `CustomerFound = true`** : compatibilité des appelants existants **préservée** (l'UI ne lit que
> `PrescriptionId`), et sémantique honnête — ce drapeau ne signale **que** le refus « client introuvable » et ne
> prétend rien quand la commande est rejetée en amont (le client n'est alors pas chargé).

| `UpdatePrescriptionResult` | Type | Sens |
|---|---|---|
| `PrescriptionFound` | `bool` | `false` = introuvable, aucune écriture (**comportement préservé**) |
| `PrescriptionId` | `long` | écho de l'entrée |
| `ValidationErrors` / `IsValid` | *(ajoutés)* | convention P3-1 |

**Pas de `CustomerFound` sur Update** : `CustomerId` est préservé, l'ordonnance ne change jamais de propriétaire, et le
client n'est pas chargé.

---

## 15. Client archivé

**Politique A** (P3-3A), appliquée telle quelle :

| Opération | Comportement |
|---|---|
| **Création** | ❌ **refusée** — `BusinessRuleException`, **aucune écriture** |
| **Modification** | ✅ **autorisée** (correction d'une ordonnance existante) |
| **Suppression** | ✅ **non restreinte** sur le seul critère `IsArchived` |

Message métier stable, exposé en **constante publique**
`CreatePrescriptionUseCase.CustomerArchivedMessage` (patron P3-2B `DeleteCustomerUseCase.CustomerHasHistoryMessage`,
réutilisable par l'UI de P3-3C et par les tests, sans littéral dupliqué) :

> **« Ce client est archivé. Réactivez-le avant de créer une nouvelle ordonnance. »**

**Justification du refus asymétrique** (et non intuition) : l'archivage (P3-2B) est **réversible et sans cérémonie**
(`Archive()`/`Reactivate()`, un booléen) — ce n'est **ni** une clôture comptable **ni** un verrou légal. « Archivé »
signifie **« plus de nouvelle activité »**, **pas** « données gelées ». Obliger l'utilisateur à réactiver puis
ré-archiver un client pour corriger un chiffre mal saisi serait une régression d'UX sans fondement métier.

---

## 16. Client introuvable

`CreatePrescriptionUseCase` **charge désormais le client** avant toute création.

- **Introuvable** ⇒ `CustomerFound = false`, **aucune écriture**, **aucune** `BusinessRuleException` ;
- la **clé étrangère n'est plus le chemin nominal** : plus de `DbUpdateException` brute remontée à l'UI pour un cas
  parfaitement prévisible ;
- la FK `Prescriptions.CustomerId → Customers` (**`Restrict`**, P3-2B) **reste** le rempart de dernier recours en base.

---

## 17. Tests Domain

| Fichier | Avant | Après | Δ |
|---|---|---|---|
| `ValidatorTests/PrescriptionValidatorTests.cs` | 4 | **61** | **+57** |
| `OpticsTests/OpticalAxisNormalizerTests.cs` *(créé)* | 0 | **5** | **+5** |
| `ServiceTests/PrescriptionServiceTests.cs` *(supprimé)* | 4 | **0** | **−4** |
| **Total Domain** | **236** | **294** | **+58** |

Couverture ajoutée (OD **et** OG, par `[Theory]` sur les deux yeux) : valeurs finies (`NaN`, `±∞` sur sphère,
cylindre, addition, prisme) · plages (non-régression) · matrice **cylindre ⇄ axe** complète · matrice **prisme ⇄ base**
complète · **absence de borne supérieure** du prisme · ordonnance partielle (OD seul, OG seul, vide) · `DoctorName`
absent/vide/blanc · normalisation directe (`null`, `0`, `1`, `90`, `180`).

**Fixture historique amendée, non supprimée** : `Validate_WithValidRanges_ShouldPass` portait `OgCylinder = 0` avec
`OgAxis = 0`. La fixture est **adaptée** (`OgAxis = null`), et le cas qu'elle encodait est **réaffirmé explicitement**
par `Validate_AxisWithoutCylinder_ShouldFail`, qui prouve que la combinaison est désormais **refusée**. Ce n'est pas un
affaiblissement : c'est le passage d'une combinaison tolérée à une combinaison déclarée incohérente.

---

## 18. Tests Application

| Fichier | Avant | Après | Δ |
|---|---|---|---|
| `CreatePrescriptionUseCaseTests.cs` | 5 | **18** | **+13** |
| `UpdatePrescriptionUseCaseTests.cs` | 6 | **13** | **+7** |
| **Total Application** | **198** | **218** | **+20** |

**Create** — prouvé : commande invalide ⇒ `ValidationErrors`, `IsValid = false`, et **aucun** appel de dépôt (mocks
**`MockBehavior.Strict` sans aucun setup** : la lecture du client elle-même ferait échouer le test) · `NaN`/`±∞`
refusés sans écriture · client **introuvable** ⇒ `CustomerFound = false`, aucune écriture · client **archivé** ⇒
`BusinessRuleException` **au message exact de la constante**, aucune écriture (prouvé **deux fois** : mocks stricts
**et** vraie base SQLite restée vide) · client actif ⇒ création réussie · **axe 0 ⇒ persisté à 180** (OD **et** OG),
axe 180 inchangé, axe 90 inchangé · ordonnance partielle et **vide** acceptées · gardes de constructeur, dont le
**nouvel** `ICustomerRepository`.

**Update** — prouvé : ordonnance introuvable ⇒ `PrescriptionFound = false`, aucune écriture · commande invalide ⇒
`ValidationErrors`, **entité existante intacte en base** (`DoctorName`, `OdSphere`, `Notes` inchangés) · update valide
⇒ persistance, `CustomerId` et `CreatedAt` conservés · **axe 0 ⇒ 180** · **client archivé ⇒ modification autorisée**
(sans introduire `ICustomerRepository`) · `NaN`/`±∞` refusés sans écriture · ordonnance partielle acceptée.

Écritures prouvées sur **vrai SQLite temporaire** (jamais le provider InMemory) ; absences d'écriture prouvées par
**mocks stricts**.

### Total

| | Avant | Après |
|---|---|---|
| **App** | 211 | **211** *(inchangé — aucune UI touchée)* |
| **Application** | 198 | **218** |
| **Domain** | 236 | **294** |
| **TOTAL** | **645** | **723** |

**+82 tests ajoutés, −4 supprimés** (les 4 tests de `PrescriptionService`, devenus sans objet) ⇒ **net +78**.
**0 échec, 0 ignoré.**

---

## 19. Migrations

**AUCUNE.** ✅

- `git diff -- src/MMV.Infrastructure/Migrations` ⇒ **vide** ;
- `git diff -- src/MMV.Infrastructure/Data/Configurations` ⇒ **vide** ;
- `dotnet ef migrations has-pending-model-changes` ⇒ « No changes … since the last migration ».

Aucune configuration EF, aucun snapshot, aucune contrainte SQL, aucun trigger, aucun changement de fournisseur, aucun
parsing d'erreur SQLite. **Toutes les règles sont applicatives, appliquées à la saisie, non rétroactives** : les lignes
historiques incohérentes restent **lisibles et affichables**, et ne deviennent bloquantes que si l'utilisateur les
**ré-enregistre** — précisément le moment où on **veut** qu'il les corrige.

Seule modification Infrastructure : **le retrait de l'enregistrement DI** de `PrescriptionService` (autorisé §18 du
prompt).

---

## 20. UI

**AUCUNE modification.** ✅ `git diff -- src/MMV.App` et `git diff -- tests/MMV.App.Tests` ⇒ **vides**.
Les 211 tests `MMV.App.Tests` passent **inchangés** : l'ajout de propriétés aux `*Result` est **source-compatible**
(l'UI ne lit que `result.PrescriptionId`), et le nouveau paramètre de constructeur est résolu **par la DI**.

**Non traité ici, reporté à P3-3C** (comme exigé) : `DoctorName` obligatoire dans l'UI · `TextBox` de `PrismBase`
(watermark « H/V/In/Out » **erroné** — `H` et `V` n'existent pas dans l'enum) · confirmation avant suppression ·
affichage des `ValidationErrors` · capture de `BusinessRuleException` (aujourd'hui absorbée par un `catch` **générique**
qui l'afficherait comme un incident technique).

⚠️ **Conséquence à assumer jusqu'à P3-3C** : la règle prisme ⇄ base est **active** alors que l'écran ne permet toujours
pas de saisir une base valide. Un utilisateur qui saisit une valeur de prisme verra sa création **refusée** avec un
message technique. **C'est le prix de l'ordre imposé (métier avant UI), et c'est exactement ce que P3-3C doit corriger
en priorité.**

---

## 21. Dépendances

**AUCUN package ajouté, retiré ou mis à jour.** ✅

- Règles : **FluentValidation** (déjà transitif du Domain) + **`CommandValidation`** (socle P3-1) ;
- Normalisation : **Domain pur** (aucune dépendance) ;
- Refus métier dur : **`BusinessRuleException`** (Domain, ADR §8) ;
- `MMV.Application` **reste pure** : `MMV.Domain` seule en référence projet, `DI.Abstractions 8.0.1` seul package
  top-level ;
- **`FluentAssertions` reste en 6.x** (v7+ commercial) — **non touché** ;
- **0 vulnérabilité** (7 projets).

---

## 22. Multi-poste

Conforme à **ADR-PROD-DB-001** et à la leçon P3-2B : *un « check-then-act » applicatif n'est **pas** atomique.*

**Fenêtre de course résiduelle, documentée honnêtement** : entre `GetByIdAsync(customer)` et `SaveChangesAsync`, un
autre poste peut archiver le client ; l'ordonnance **passerait** malgré la garde.

- **Aucun rempart atomique disponible à coût raisonnable** : une FK **ne peut pas** exprimer « le client ne doit pas
  être archivé » ; un `CHECK` inter-tables est **impossible en SQLite** ; un trigger ne serait **pas** neutre
  vis-à-vis du fournisseur (hors ADR).
- **`ITransactionRunner` n'y changerait rien** : le flux est **mono-écriture**, et une transaction **ne sérialise pas**
  l'archivage venu d'une autre connexion. Il n'est donc **pas** introduit.
- **Risque accepté** : fenêtre **étroite**, **aucune perte de donnée**, issue **sûre** et **corrigeable** (réactiver le
  client, ou supprimer l'ordonnance indûment créée).
- La garde applicative élimine en revanche **100 % des cas non concurrents** : écran resté ouvert, identifiant fourni
  par un appelant, sélection obsolète — dont la « course 4 » de P3-3A, que le filtrage du picker **ne couvrait pas**.

---

## 23. Risques résiduels

1. 🔴 **UI non alignée** (§20) : la règle prisme est active, l'écran ne sait pas encore saisir une base valide, et la
   `BusinessRuleException` tombe dans un `catch` générique. ⇒ **P3-3C, prioritaire.**
2. 🟠 **Aucun token de concurrence** : deux postes corrigeant **des yeux différents** de la même ordonnance ⇒ la
   première correction est **écrasée en silence**. `UpdatePrescriptionUseCase` réécrit **tous** les champs optiques.
   **Inchangé par P3-3B** ⇒ chantier **transverse** (ADR-PROD-DB-001).
3. 🟠 **Immutabilité forte de `Prescription` : toujours NON implémentée.** Aucun versionnement, aucune FK
   `SaleItem.PrescriptionId` / `OrderItem.PrescriptionId`, aucun verrouillage après usage. **P3-3B ne prétend pas le
   contraire.** Les documents historiques restent protégés **par la copie par valeur** dans `SaleItem`/`OrderItem`
   (inchangées) — pas par une règle.
4. 🟠 **La suppression physique d'ordonnance existe toujours**, **sans confirmation** (`// TODO` resté dans
   `CustomerPrescriptionsViewModel`), jusqu'à la confirmation UI de **P3-3C**. `DeletePrescriptionUseCase` n'est pas
   restreint.
5. 🟠 **Fenêtre de course « archivage pendant création »** (§22) — assumée, sans perte de donnée.
6. 🟡 **Ordonnance totalement vide toujours persistable** — iso-comportement délibéré, réévaluable après pilote.
7. 🟡 **`DoctorName`** : divergence UI/Domain **toujours ouverte** (l'UI l'exige, le Domain non) ⇒ P3-3C.
8. 🟡 **Prisme perdu** au pré-remplissage d'une commande (`OrderFormViewModel.AutoFillFromPrescription()`) — bug
   **préexistant**, hors périmètre ⇒ **P3-6**.
9. 🟡 **Données historiques incohérentes** (axe orphelin, base sans valeur) **non migrées** : elles restent lisibles et
   ne bloquent qu'au ré-enregistrement. **C'est voulu.**

---

## 24. Fichiers modifiés / créés / supprimés

**Créés (3)**
- `src/MMV.Domain/Optics/OpticalAxisNormalizer.cs` — normalisation Domain pure ;
- `tests/MMV.Domain.Tests/OpticsTests/OpticalAxisNormalizerTests.cs` — 5 tests ;
- `docs/implementation/P3-3B-prescription-validation-and-archived-customer-report.md` — **ce rapport**.

**Supprimés (2)**
- `src/MMV.Domain/Services/PrescriptionService.cs` — `IPrescriptionService` **+** `PrescriptionService` (chemin
  d'écriture parallèle mort) ;
- `tests/MMV.Domain.Tests/ServiceTests/PrescriptionServiceTests.cs` — 4 tests devenus sans objet.

**Modifiés (10)**
- `src/MMV.Domain/Validators/PrescriptionValidator.cs` — finitude, `IssueDate` différée, règles croisées OD/OG,
  messages constants ;
- `src/MMV.Application/UseCases/Prescriptions/CreatePrescription/CreatePrescriptionUseCase.cs` — validation, client
  introuvable, garde archivé, normalisation, `ICustomerRepository` ;
- `…/CreatePrescription/CreatePrescriptionResult.cs` — `CustomerFound`, `ValidationErrors`, `IsValid` ;
- `…/UpdatePrescription/UpdatePrescriptionUseCase.cs` — candidat validé avant mutation, normalisation ;
- `…/UpdatePrescription/UpdatePrescriptionResult.cs` — `ValidationErrors`, `IsValid` ;
- `src/MMV.Application/DependencyInjection.cs` — commentaire (nouvelle dépendance du use case) ;
- `src/MMV.Infrastructure/DependencyInjection.cs` — **retrait** de l'enregistrement `IPrescriptionService` ;
- `tests/MMV.Domain.Tests/ValidatorTests/PrescriptionValidatorTests.cs` — 4 → 61 tests, fixture amendée ;
- `tests/MMV.Application.Tests/UseCases/Prescriptions/CreatePrescriptionUseCaseTests.cs` — 5 → 18 tests ;
- `tests/MMV.Application.Tests/UseCases/Prescriptions/UpdatePrescriptionUseCaseTests.cs` — 6 → 13 tests, fixture
  amendée.

**Intacts** : `src/MMV.App/**` · `tests/MMV.App.Tests/**` · `src/MMV.Infrastructure/Migrations/**` ·
`src/MMV.Infrastructure/Data/Configurations/**` · `.github/**` · `*.csproj` · `*.sln`.

---

## 25. Contrôles exécutés

`git branch --show-current` · `git status --short` · `git log -10 --oneline` · `git rev-parse HEAD` ·
`git diff --stat` · `git diff --check` · `gh run list --branch p3-business-rules --limit 10` ·
`gh run view 29296145755 --json databaseId,url,headSha,headBranch,status,conclusion` ·
`dotnet restore MMV.sln` · `dotnet build MMV.sln --no-restore -c Debug` · `dotnet test MMV.sln --no-build -c Debug` ·
`dotnet list MMV.sln package --vulnerable --include-transitive` · `dotnet tool restore` ·
`dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` ·
`dotnet list src/MMV.Application/MMV.Application.csproj reference` / `… package` ·
`rg -n "IPrescriptionService|PrescriptionService" src tests` ·
`git diff -- src/MMV.App` · `… tests/MMV.App.Tests` · `… src/MMV.Infrastructure/Migrations` ·
`… src/MMV.Infrastructure/Data/Configurations` · `… .github`.

---

## 26. Résultats

| Contrôle | Attendu | Résultat |
|---|---|---|
| Branche | `p3-business-rules` | ✅ |
| CI du commit de départ | verte | ✅ `29296145755` — `success` sur `c9c7ffec…` |
| Build | vert | ✅ **0 avertissement, 0 erreur** |
| Tests | **> 645** | ✅ **723** (App 211 · Application 218 · Domain 294) — **0 échec** |
| Vulnérabilités | 0 | ✅ **0** (7 projets) |
| Migration en attente | aucune | ✅ |
| `MMV.Application` pure | Domain + DI.Abstractions | ✅ |
| Modification UI | aucune | ✅ **diff vide** |
| Migration / config EF | aucune | ✅ **diff vide** |
| Modification CI | aucune | ✅ **diff vide** |
| Package ajouté | aucun | ✅ |
| `PrescriptionService` restant | aucun | ✅ (**seul** un commentaire d'explication subsiste) |
| `git diff --check` | propre | ✅ |
| Commit / push | aucun | ✅ |

---

## 27. Verdict

### **P3-3B = GO local.**

- ✅ `PrescriptionValidator` **réellement exécuté** par Create **et** Update (fin du code mort — le risque n°1 de P3-3A) ;
- ✅ commande invalide ⇒ **aucune écriture** (prouvé par mocks **stricts**, et en base) ;
- ✅ règles **cylindre ⇄ axe** appliquées **aux deux yeux** ;
- ✅ règles **prisme ⇄ base** appliquées **aux deux yeux**, **sans borne supérieure inventée** ;
- ✅ **`NaN` et infinis refusés** sur les 8 valeurs optiques ;
- ✅ **axe 0 normalisé vers 180**, **hors** du validateur, par une fonction Domain **pure** ;
- ✅ **client introuvable** traité explicitement — plus de `DbUpdateException` FK comme chemin nominal ;
- ✅ **client archivé** ⇒ création refusée par `BusinessRuleException` au message **constant**, **aucune écriture** ;
- ✅ **update autorisé** pour un client archivé (correction possible) ;
- ✅ **`PrescriptionService` supprimé** — aucun consommateur runtime, second chemin d'écriture éliminé ;
- ✅ **aucune UI**, **aucune migration**, **aucune nouvelle dépendance** ;
- ✅ **723 tests verts**, rapport complet.

**Aucun commit, aucun push.**

---

## 28. Prochaine étape — P3-3C (UI Ordonnances)

**Prioritaire, car P3-3B a rendu des règles actives que l'écran ne sait pas encore honorer :**

1. **`PrismBase` : `TextBox` → `ComboBox`** sur les 4 valeurs de l'enum (`In`, `Out`, `Up`, `Down`) et **suppression du
   watermark erroné « H/V/In/Out »** — correctif d'**intégrité de saisie**, pas de confort : sans lui, toute saisie de
   prisme est désormais **refusée**.
2. **Capturer `BusinessRuleException`** et afficher le refus « client archivé » **en clair**, en proposant
   « Réactiver le client » (aujourd'hui : `catch` générique ⇒ message technique).
3. **Afficher les `ValidationErrors`** des résultats Create/Update ; **dédupliquer** les 8 validations en dur du
   `PrescriptionFormViewModel` (le Domain est désormais propriétaire des règles).
4. **Confirmation avant suppression** (le `// TODO` de `CustomerPrescriptionsViewModel`) — patron P3-2C.
5. **Aligner `DoctorName`** : retirer l'obligation UI (le Domain le laisse facultatif).

> **Hors P3-3** : token de concurrence (**transverse**, ADR-PROD-DB-001) · prisme perdu au pré-remplissage des
> commandes (**P3-6**) · refus de vente pour client archivé (**P3-7**) · **transposition** (**P3-6B**) · archivage /
> versionnement d'ordonnance (**dette documentée**).
