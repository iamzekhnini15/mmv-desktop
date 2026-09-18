# P4-5D Temporal Strategy Pre Review V2

> **Corrections de revue architecturale — phase A2. EN ATTENTE DE VALIDATION FINALE.**
> Aucun commit n'a été créé, aucun push n'a été effectué, aucune branche n'a été créée.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. `HEAD` = **`b5c2073`** (inchangé).
> Rapport de phase A : [P4-5D-temporal-strategy-pre-review.md](P4-5D-temporal-strategy-pre-review.md).
> ADR source : [ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md).
>
> **Portée de ce document : uniquement ce que la phase A2 a changé.** Tout ce qui n'y figure pas est
> inchangé depuis la phase A et reste décrit par le rapport V1 — lequel porte désormais un bandeau
> signalant le seul point devenu caduc.

---

## Changes Applied After Architect Review

Trois corrections demandées, trois corrections appliquées. **Aucune autre modification** : l'implémentation
de la phase A n'a pas été refaite, et le périmètre n'a pas été élargi.

| # | Correction demandée | Nature | État |
|---|---|---|---|
| **C1** | `Notification.CreatedAt` ne doit plus lire l'horloge système | **Code** + tests | **fait** |
| **C2** | Documenter explicitement les exceptions d'architecture | Documentation | **fait** |
| **C3** | Tracer la reprise des dates civiles dans la roadmap P4 | Documentation | **fait** |

**Explicitement non touché**, conformément à la consigne : `IClock`, `SystemClock`, `UtcDateTimeConverter`,
l'implémentation `DateOnly`, les migrations, les tests PostgreSQL, les ADR. Vérifié par `git status` — voir
*Vérification finale*.

---

## Notification CreatedAt Change

### Le problème

L'entité portait un défaut de propriété :

```csharp
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
```

La phase A avait corrigé la **valeur** (`DateTime.Now` → `DateTime.UtcNow`) mais conservé le **mécanisme** :
une entité du Domain lisant l'horloge du système. La revue a identifié que c'est le mécanisme, et non la
valeur, qui pose problème — **même lorsque tous les chemins applicatifs renseignent déjà `CreatedAt`
explicitement**, le défaut subsistant constitue un **précédent architectural** : il rend légitime, pour la
prochaine entité datée, de lire l'horloge au lieu de la recevoir.

### La correction

```csharp
public required DateTime CreatedAt { get; set; }
```

**Plus aucune valeur par défaut** — ni `Now`, ni `UtcNow`. L'instant devient une **donnée d'entrée
obligatoire**, fournie par le créateur de la notification.

### Pourquoi `required` et non un constructeur

La consigne était explicite : « ne pas introduire un nouveau pattern si le projet possède déjà une
convention ». La convention du Domain a donc été **mesurée avant de choisir**, pas supposée :

| Convention mesurée sur `src/MMV.Domain/Entities/` | Constat |
|---|---|
| Entités déclarant un constructeur | **0** — toutes sont des POCO à initialiseur d'objet |
| Entités utilisant `required` | 0 — mais **aucune convention contraire** : le mot-clé n'était simplement pas encore employé |
| `LangVersion` / `TargetFramework` | **C# 12 / net8.0** — `required` (C# 11) est disponible |
| Obligation déjà exprimée pour cette colonne | `builder.Property(n => n.CreatedAt).IsRequired()` en configuration EF |

Les trois options, et leur coût réel :

| Option | Effet | Coût mesuré | Retenue |
|---|---|---|---|
| **Propriété obligatoire (`required`)** | Erreur **de compilation** si l'instant est omis | **4 sites** à corriger, tous en test | **OUI** |
| Constructeur obligatoire | Même garantie | Briserait l'initialiseur d'objet sur **~20 sites** et ferait de `Notification` la **seule** entité à constructeur du Domain | non |
| Validation FluentValidation | Erreur **à l'exécution**, et seulement si le validateur est appelé | Aucun `NotificationValidator` n'existe ; les 5 validateurs du dépôt portent des **règles métier**, pas des obligations de construction | non |

`required` préserve donc **exactement** la convention existante (POCO + initialiseur d'objet), tout en
déplaçant la garantie du runtime vers le **compilateur** : un chemin de création qui oublierait
l'horodatage **ne compile pas**.

### EF Core n'est pas affecté

`required` est une vérification **purement compilatoire** (`RequiredMemberAttribute`). La matérialisation
d'EF Core 8 passe par un arbre d'expression, qui ne l'applique pas : la relecture d'une notification
existante fonctionne à l'identique. **Confirmé par l'exécution** — les 1 758 tests passent, dont l'ensemble
des tests de persistance des notifications.

### Vérification des chemins de création — les 7 sites de production

La compilation est ici la preuve : **`src/**` a compilé sans une seule erreur**, ce qui démontre que
*chaque* chemin de production renseignait déjà `CreatedAt`. Recensement explicite :

| # | Site | Source de l'instant | Statut |
|---|---|---|---|
| 1 | `GenerateLowStockNotificationsUseCase.cs:110` | `now` — **une seule** lecture d'`IClock` pour toute la réconciliation | inchangé |
| 2 | `AdvanceOrderStatusUseCase.cs:189` | `_clock.UtcNow` | inchangé |
| 3 | `SettleOrderBalanceUseCase.cs:142` | `_clock.UtcNow` | inchangé |
| 4–7 | `DbInitializer.cs:947, 963, 976, 985` | `DateTime.UtcNow.AddDays(-n)` — **seeder**, exception documentée en C2 | inchangé |

`NotificationRepository.CreateAsync` et `TryCreateActiveLowStockAsync` ne font que **transmettre** l'entité
reçue : ils ne fabriquent aucun instant, et n'ont donc pas eu à changer.

### Tests

**4 sites de test** dépendaient du défaut supprimé ; aucun n'était en production.

| Fichier | Traitement |
|---|---|
| `NotificationUseCasesTests.cs:63-64` | Deux notifications de fixture reçoivent un instant UTC **explicite** (08:00 et 09:00) — l'ordre voulu par le test devient lisible au lieu d'être implicite. |
| `UtcPersistenceRoundTripTests.cs` | Les **2** tests affirmant « le défaut d'entité est UTC » n'ont plus d'objet : **il n'y a plus de défaut**. Remplacés par **3** tests qui figent la décision de la revue. |

Les trois nouveaux tests, et ce que chacun empêche :

| Test | Ce qu'il fige |
|---|---|
| `LHorodatageDUneNotification_EstObligatoireALaCompilation` | `CreatedAt` porte bien `RequiredMemberAttribute`. Retirer `required` casserait ce test **avant** de casser un comportement. |
| `AucunDefautDHorloge_NeSubsisteSurNotification` | Contourne `required` par réflexion (comme le fait EF) pour observer l'entité **sans** horodatage fourni : `CreatedAt` vaut `default`. **Ré-introduire `= DateTime.UtcNow` ferait tomber ce test** — c'est lui qui protège la décision, pas seulement sa forme. |
| `UneNotificationHorodateeParSonCreateur_EstPersistableEtRelueEnUtc` | L'aller-retour complet : l'instant vient de l'appelant, est persisté, et est relu à l'identique en `Kind = Utc`. L'entité n'a plus besoin d'horloge. |

**Bilan : −2 tests, +3 tests ⇒ +1.**

---

## Architecture Exceptions Added

Section **`## Architecture Exceptions`** ajoutée au rapport V1, entre *Test Results* et *Risks*. Elle
documente **trois** écarts assumés. Le principe qui la gouverne : *une exception qui n'est pas écrite
devient une convention par défaut* — chacune est donc **bornée**, avec sa portée autorisée **et** ce qui
reste interdit.

| Exception | Portée **autorisée** | **Interdit** |
|---|---|---|
| **`IClock` optionnel en UI** (`IClock? clock = null` ⇒ `SystemClock.Instance`) | ViewModels de `MMV.App` uniquement | Domain, Application, repositories, services métier |
| **`SystemClock.Instance` en accès statique** | composition root (`App.axaml.cs`, `DependencyInjection.cs`), code framework UI (`CustomersView.axaml.cs`), seeders (`DbInitializer`, `DbSeeder`) | Domain, Application, repositories, services métier |
| **`FixedClock` dupliqué** | projets de test uniquement | code de production |

Raisons consignées pour la première : les ViewModels Avalonia sont **construites à la main** et non résolues
par le conteneur ; rendre le paramètre obligatoire aurait imposé une refonte de **25 sites** hors périmètre ;
et le **comportement d'exécution est identique**, le défaut étant **la même instance** que celle enregistrée
en `Singleton` — l'oubli d'injection ne coûte que de la testabilité, jamais de la justesse.

Pour la seconde, la justification est uniforme : ce sont les **trois seuls** contextes où **aucune injection
n'est disponible** — un composition root ne peut pas s'injecter à lui-même, un contrôle Avalonia est
instancié par le framework, et `DbInitializer` est une classe **statique**. Le cas des repositories est
tranché en creux, et de façon réutilisable : un repository qui a besoin d'un instant le **reçoit en
paramètre de méthode** (`GetOverdueOrdersAsync(DateTime asOfUtc, …)`), il ne le lit pas.

**La correction C1 réduit la surface de ces exceptions** : le Domain ne figure plus dans aucune colonne
« autorisé ». Après C1, **plus une seule entité ne lit l'horloge**.

---

## Roadmap Update

Ajout dans [`docs/architecture/P4-multi-poste-roadmap.md`](../architecture/P4-multi-poste-roadmap.md) —
**documentation seule, aucun code**.

**Nouvelle étape `P4-5D-R` — Civil Date Data Migration Preparation**, insérée dans le tableau de découpage
de P4-5 entre **P4-5D** et **P4-5E** (ligne 602) :

- **Objectif** : préparer la reprise des données de `Customer.BirthDate` et `Prescription.IssueDate`.
- **Raison** : le passage à `DateOnly` change le **format** des valeurs `TEXT` SQLite
  (`yyyy-MM-dd HH:mm:ss[.fffffff]` → `yyyy-MM-dd`) **sans qu'EF ne génère automatiquement de migration** —
  `has-pending-model-changes` reste vert. C'est précisément le piège signalé par l'ADR §7.2.
- **Dépend de** : P4-5D. **ADR appliquée** : [004](../architecture/adr-prod-db-004-datetime-strategy.md).
- **Statut** : **`NOT STARTED`**.
- **Bloque** : le **déploiement sur toute base existante** — une base antérieure à P4-5D lève
  `FormatException` à la lecture d'un client ou d'une ordonnance.
- La spécification exécutable de la reprise **existe déjà** : `LegacyCivilDateFormatTests` (6 tests,
  phase A) établit que la transformation est une **troncature**, jamais une conversion de fuseau, et
  qu'elle est exprimable en **SQL pur**.

Ligne ajoutée au bloc de statut machine (ligne 818) :

```
P4-5D-R CIVIL DATE REPRISE  = NOT STARTED — BLOCKS DEPLOYMENT ON EXISTING DB
```

Le blocage de déploiement n'est donc plus consigné dans le seul rapport de pré-revue : il figure désormais
dans la roadmap, à l'endroit où l'ordonnancement des lots se décide. **Aucun autre statut de la roadmap n'a
été modifié** — en particulier, ni P4-5C ni P4-5D n'ont vu le leur changer : cela appartient à l'architecte,
après validation.

---

## Files Modified

**5 fichiers touchés en phase A2** — 1 de production, 2 de test, 2 de documentation — plus le présent
rapport.

### Production (1)

| Fichier | Changement |
|---|---|
| [`src/MMV.Domain/Entities/Notification.cs`](../../src/MMV.Domain/Entities/Notification.cs) | `public DateTime CreatedAt { get; set; } = DateTime.UtcNow;` → `public required DateTime CreatedAt { get; set; }`, plus la documentation XML de la décision |

### Tests (2)

| Fichier | Changement |
|---|---|
| `tests/MMV.Domain.Tests/Data/Time/UtcPersistenceRoundTripTests.cs` | 2 tests de défaut d'entité remplacés par 3 tests d'obligation ; `using System.Runtime.CompilerServices;` ajouté |
| `tests/MMV.Application.Tests/UseCases/Notifications/NotificationUseCasesTests.cs` | 2 fixtures reçoivent un `CreatedAt` UTC explicite |

### Documentation (2 + ce rapport)

| Fichier | Changement |
|---|---|
| `docs/implementation/P4-5D-temporal-strategy-pre-review.md` | **C2** — section `## Architecture Exceptions` ajoutée ; bandeau signalant le point rendu caduc par **C1** |
| `docs/architecture/P4-multi-poste-roadmap.md` | **C3** — étape `P4-5D-R` + ligne de statut machine |
| `docs/implementation/P4-5D-temporal-strategy-pre-review-v2.md` | **ajouté** — ce rapport |

### Non touché — vérifié par `git status`

| Élément | État |
|---|---|
| `IClock`, `SystemClock`, `UtcDateTimeConverter`, `NonUtcDateTimeException`, `DatePickerCivilDate` | **inchangés** |
| Implémentation `DateOnly` (entités, commandes, DTO, ViewModels, repositories) | **inchangée** |
| `src/MMV.Infrastructure/Migrations/**` | **0 fichier modifié** — 14 migrations + snapshot bit-à-bit identiques |
| `docs/architecture/adr-*` | **0 fichier modifié** |
| `.github/workflows/**` | **0 fichier modifié** |
| `*.csproj`, `*.sln`, `Directory.Build.props` | **0 fichier modifié**, aucun paquet ajouté |

---

## Tests Executed

```
dotnet build MMV.sln -c Debug
dotnet test  MMV.sln -c Debug
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure
git status --short
git rev-list --count b5c2073..HEAD
```

---

## Test Results

### Build

| Mesure | Valeur |
|---|---|
| Erreurs | **0** |
| Avertissements | **0** |

**Étape intermédiaire, et c'est le résultat le plus informatif de cette phase :** à la première compilation
après le passage en `required`, le compilateur a signalé **exactement 4 erreurs `CS9035`**, **toutes dans
`tests/`**, **aucune dans `src/`**. C'est la vérification demandée par la revue — « s'assurer qu'aucun
chemin production ne casse » — obtenue non par relecture, mais par le compilateur lui-même.

### Suite complète

| Projet | Phase A (V1) | Phase A2 (V2) | Écart | Échecs | Ignorés |
|---|---|---|---|---|---|
| `MMV.Domain.Tests` | 871 | **872** | **+1** | 0 | 0 |
| `MMV.Application.Tests` | 628 | **628** | 0 | 0 | 0 |
| `MMV.App.Tests` | 258 | **258** | 0 | 0 | 0 |
| **Total** | **1 757** | **1 758** | **+1** | **0** | **0** |

L'écart `+1` est intégralement expliqué : **−2** tests de défaut d'entité, **+3** tests d'obligation
d'horodatage. Aucune assertion n'a été affaiblie, aucun test n'a été ignoré.

### Dérive de modèle EF

> `No changes have been made to the model since the last migration.`

**Aucun changement en attente.** `required` ne modifie pas le modèle relationnel : la colonne
`Notifications.CreatedAt` était déjà déclarée `IsRequired()` dans `NotificationConfiguration`, et son type
est inchangé.

### Migrations

`git status --short src/MMV.Infrastructure/Migrations/` ⇒ **sortie vide**. Aucune migration créée, modifiée
ou supprimée en phase A2.

---

## Remaining Questions

Les questions **2, 3, 5, 6 et 7** du rapport V1 restent ouvertes et **inchangées**. Les questions **1**
(`IClock` optionnel en UI) et **4** (reprise des données) ont reçu une réponse partielle : elles sont
désormais **documentées** — respectivement en *Architecture Exceptions* et en `P4-5D-R` — mais la revue n'a
pas indiqué s'il fallait en outre **changer** le comportement.

| # | Question | Statut après A2 |
|---|---|---|
| **Q1** | `IClock` optionnel en UI — à rendre obligatoire malgré le coût de 25 sites ? | **Documenté et borné** (C2). La demande portait sur la documentation, pas sur un changement de forme : l'optionnalité est **conservée**. Confirmez-vous que documenter suffit ? |
| **Q2** | `SystemClock.Instance` statique — `DbInitializer` à transformer en classe d'instance ? | **Ouverte.** Bornée par C2, mais non modifiée. |
| **Q3** | Instant en **paramètre de méthode** pour les repositories — principe valable pour les futurs repositories ? | **Ouverte.** C2 l'inscrit comme règle dans *Architecture Exceptions* ; reste à le valider comme principe général. |
| **Q4** | Reprise des données en P4-7 ou plus tôt ? | **Tracée** en `P4-5D-R` (C3), dépendance P4-5D, `NOT STARTED`. Son **ordonnancement** reste à trancher. |
| **Q5** | `DateTimeOffset.UtcNow` ajouté aux interdits + 57 `DateTime.UtcNow` non routés vers `IClock` | **Ouverte, inchangée.** |
| **Q6** | Test d'architecture ignorant les commentaires — analyseur Roslyn en P4-5F ? | **Ouverte, inchangée.** |
| **Q7** | `FixedClock` dupliqué — projet `MMV.TestSupport` ? | **Documenté et borné** (C2), non modifié. |
| **Q8** | Défaut d'entité `Notification.CreatedAt` | **RÉSOLUE par C1** — le défaut est supprimé, l'horodatage est obligatoire à la compilation. |

**Deux points nouveaux, issus de cette phase :**

1. **Faut-il étendre `required` aux autres entités datées ?** `Notification` était le site que la revue a
   nommé, et il est traité. Les autres entités (`Order`, `Sale`, `StockMovement`, …) portent encore
   `= DateTime.UtcNow` comme défaut de propriété : **la valeur est correcte**, mais le mécanisme est celui
   que C1 vient d'écarter. Le faire serait cohérent ; ce serait aussi un lot à part entière, hors périmètre
   de cette correction. **Lot dédié, ou dette acceptée ?**
2. **`required` devient-il la convention du Domain pour les données obligatoires à la construction ?**
   `Notification.CreatedAt` est la **première** utilisation du mot-clé dans le dépôt. Si la réponse est oui,
   elle mérite d'être écrite quelque part de plus durable que ce rapport.

---

## Ready For Final Approval

| Contrôle | Résultat |
|---|---|
| C1 — `Notification.CreatedAt` sans lecture d'horloge | **fait** — `required`, aucun défaut |
| C1 — tous les chemins de création vérifiés | **fait** — 7 sites de production, 0 erreur de compilation dans `src/` |
| C1 — tests ajoutés / adaptés | **fait** — +3 / −2, 4 sites de test corrigés |
| C2 — `## Architecture Exceptions` documentée | **fait** — 3 exceptions bornées, portée autorisée **et** interdits |
| C3 — `P4-5D-R` en roadmap | **fait** — tableau + bloc de statut, `NOT STARTED` |
| Périmètre non élargi | **vérifié** — `IClock`, `SystemClock`, convertisseur, `DateOnly`, migrations, ADR, CI : intacts |
| Build | **0 erreur, 0 avertissement** |
| Tests | **1 758 / 1 758**, 0 échec, 0 ignoré |
| Dérive de modèle EF | **aucune** |
| Commit | **NON** |
| Push | **NON** |
| Branche créée | **NON** |

**Un point de blocage subsiste, inchangé depuis la phase A et désormais tracé en roadmap :** ce lot **ne
doit pas être déployé sur une base existante** avant la reprise des dates civiles (`P4-5D-R`).

---

## Vérification finale

```
$ git rev-parse --abbrev-ref HEAD
p4-multi-poste

$ git rev-parse --short HEAD
b5c2073

$ git rev-list --count b5c2073..HEAD
0

$ git status --short src/MMV.Infrastructure/Migrations/   → vide
$ git status --short docs/architecture/adr-*              → vide
$ git status --short .github/                             → vide
$ git status --short -- *.csproj *.sln                    → vide
```

| Contrôle | Résultat |
|---|---|
| Branche | **`p4-multi-poste`** — inchangée |
| `HEAD` | **`b5c2073`** — inchangé |
| Commits depuis `b5c2073` | **0** |
| Push effectué | **NON** |
| Branche créée | **NON** |
| Modifications locales | **68 modifiés, 13 non suivis** (phases A + A2 cumulées) |
| Migration créée ou modifiée | **NON** |
| ADR modifié | **NON** |
| CI modifiée | **NON** |
| `.csproj` / `.sln` / paquet modifié | **NON** |
