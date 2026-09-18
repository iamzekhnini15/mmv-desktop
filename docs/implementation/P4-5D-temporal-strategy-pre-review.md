# P4-5D Temporal Strategy Pre Review

> **Lot d'implémentation — EN ATTENTE DE REVUE ARCHITECTURALE.**
> Aucun commit n'a été créé, aucun push n'a été effectué, aucune branche n'a été créée.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`.
> SHA de départ : **`b5c2073e73eab100deb2843829f1a687a7c9a31d`** (`feat(P4-5C): implement EF model portability for PostgreSQL`).
> ADR source : [ADR-PROD-DB-004 — Stratégie temporelle](../architecture/adr-prod-db-004-datetime-strategy.md).
> **Le dépôt réel prime toujours sur ce document.** Toutes les valeurs chiffrées ci-dessous sont **mesurées**
> sur le poste, pas citées de mémoire.
>
> **Mise à jour après revue architecturale (phase A2).** Ce rapport décrit la **phase A**. Un point y est
> désormais **caduc** : `Notification.CreatedAt` **n'a plus de valeur par défaut** — ni `DateTime.Now`, ni
> `DateTime.UtcNow`. La propriété est `required` ; l'instant est fourni par le créateur. Partout où ce
> document parle du « défaut d'entité `DateTime.UtcNow` » (analyse des 17 sites, ligne 1 ; *IClock
> Implementation* ; question 8), lire **[P4-5D-temporal-strategy-pre-review-v2.md](P4-5D-temporal-strategy-pre-review-v2.md)**.
> La section **Architecture Exceptions** ci-dessous a été ajoutée lors de la même phase A2.

---

## Objective

Implémenter la stratégie temporelle décidée en P4-5B : rendre le temps de MMV **interprétable dans une base
centrale partagée entre postes**, **testable**, et **protégé contre la régression**.

Les quatre objectifs de la mission, et leur traduction concrète :

| Objectif | Traduction | Statut |
|---|---|---|
| Les nouveaux instants techniques utilisent UTC | 17 `DateTime.Now` éliminés de `src/**` ; défaut d'entité corrigé | **fait** |
| Le temps système devient testable | `IClock` + `SystemClock` + injection ; `FixedClock` dans les tests | **fait** |
| Les `DateTime` invalides sont détectés | convertisseur validant sur les 19 propriétés d'instants + test d'architecture | **fait** |
| Les dates civiles sont séparées des instants | `Customer.BirthDate` et `Prescription.IssueDate` en `DateOnly` | **fait côté code ; reprise de données NON faite — voir Risks** |

**Obligations d'ADR-PROD-DB-004 §8 traitées** : **T1, T2, T3, T4, T6** intégralement ; **T5 partiellement**
(changement de type fait, migration de données volontairement **non** écrite — hors périmètre de la mission).
T7 → P4-5F · T8 → P4-7 · T9 → P4-8/P4-9.

---

## Analyse préalable des usages temporels

Mesure effectuée sur `b5c2073` **avant toute modification**, `src/**` hors migrations.

| Expression | Avant P4-5D | Après P4-5D | Écart avec P4-5A / P4-5C |
|---|---|---|---|
| `DateTime.Now` | **17 sites d'appel** (18 occurrences textuelles, dont 1 en commentaire) | **1** — dans `SystemClock`, en liste blanche explicite | conforme |
| `DateTime.Today` | **0** | 0 | conforme |
| `DateTimeOffset.Now` | **2** (`PrescriptionFormViewModel`) | **0** | inclus dans le décompte « 8 occurrences `DateTimeOffset` » de P4-5C |
| `DateTimeOffset` (total) | **8 occurrences / 7 lignes / 2 ViewModels** | 8, mais **aucune conversion UTC** — toutes passent par le pont de dates civiles | conforme |
| `DateTime.UtcNow` | **61** | **57** dans `src/**` (4 devenus `_clock.UtcNow`) | voir *Questions*, point 5 |
| Colonnes `DateTime` persistées | **21** | **19 instants** + **2 dates civiles** (`DateOnly`) | conforme |

**Répartition des 17 `DateTime.Now`, et ce que chacun est devenu :**

| # | Site | Nature réelle | Traitement | Pourquoi |
|---|---|---|---|---|
| 1 | `Notification.cs:50` — défaut d'entité | valeur **persistée** | `DateTime.UtcNow` | Site le plus exposé (ADR §2.2). Une entité ne peut pas recevoir `IClock` par injection ; le défaut est aligné sur les **neuf autres entités datées**, qui utilisaient déjà `UtcNow`. Les quatre chemins applicatifs qui créent une notification renseignent désormais `CreatedAt` **explicitement** depuis l'horloge et ne dépendent plus de ce défaut. |
| 2 | `OrderRepository.cs:167` | comparaison **traduite en SQL** | paramètre `asOfUtc` dans la signature | Deuxième site prioritaire (ADR §5, décision 3b). Un `Local` y était comparé à une colonne UTC — décalé d'une à deux heures selon la saison, **sans aucune erreur**. Voir *Questions*, point 3. |
| 3–4 | `GenerateLowStockNotificationsUseCase` (2) | événements métier | `IClock` injecté, **un seul instant** pour toute la réconciliation | Les fermetures et ouvertures d'un même passage décrivent un acte atomique ; relire l'horloge à chaque écriture en aurait fait des événements distincts. |
| 5 | `AdvanceOrderStatusUseCase:190` | événement métier | `IClock` injecté | Transition de statut = fait horodaté d'une base partagée. |
| 6 | `SettleOrderBalanceUseCase:141` | événement métier | `IClock` injecté | Encaissement = fait horodaté. |
| 7–11 | `RegisterSaleUseCase` (5) | événements métier | `IClock` injecté, **un seul instant** pour toute la vente | Vente, échéance, commande fournisseur, échéance fournisseur et mouvements de stock décrivent **un seul acte de gestion**, dans **une seule transaction**. |
| 12–13 | `DbSeeder.cs:261,279` — `IssueDate` | **date civile** | `SystemClock.Instance.LocalToday` + `DateOnly` | Une date d'ordonnance de démonstration est une date de calendrier, pas un instant. |
| 14 | `OrderDetailViewModel.cs:292` | **calcul de retard** | `_clock.UtcNow` | **C'était un bogue réel** : `EstimatedDelivery` est un instant UTC relu depuis la base, dont on soustrayait une heure **locale**. Le nombre de jours restants était décalé du fuseau du poste, et pouvait basculer « en retard » une à deux heures trop tôt ou trop tard. |
| 15 | `OrderFormViewModel.cs:432` | valeur **persistée** | `_clock.UtcNow` | Part dans `CreateOrderCommand`/`UpdateOrderCommand` puis dans `Order.EstimatedDelivery`. En `Local`, **Npgsql l'aurait refusée** (ADR §2.1). |
| 16 | `CustomersView.axaml.cs:35` | amorçage de DTO | `SystemClock.Instance.UtcNow` | Code-behind Avalonia, construit sans conteneur. La valeur est réécrite par `CreateCustomerUseCase` à l'enregistrement, mais elle est affichée et comparée avec celles de la liste — donc UTC comme les autres. |
| 17 | `DocumentSequenceConfiguration.cs:22` | **commentaire** | inchangé | Le mot y explique pourquoi le seed **n'utilise pas** `DateTime.Now`. Le test d'architecture ignore les commentaires (voir *Questions*, point 6). |

> **Résultat de l'audit UI (obligation T6).** ADR-PROD-DB-004 §5 décision 5 autorisait une liste blanche
> « limitée à la frontière d'affichage UI ». **Aucune n'a été nécessaire** : les trois sites UI examinés un
> par un ne relevaient pas de l'affichage — deux produisaient une valeur **persistée**, le troisième un
> **calcul faux**. Les trois ont été corrigés au lieu d'être exemptés.

---

## Files Modified

**83 fichiers `.cs`** : **67 modifiés**, **16 ajoutés** — plus le présent rapport. Cinq répertoires sont
créés : `src/MMV.Domain/Interfaces/Time/`, `src/MMV.Infrastructure/Data/Time/`, `src/MMV.App/Time/`,
`tests/**/TestDoubles/` et `tests/**/Time/`.

### Ajoutés — production (5)

| Fichier | Rôle |
|---|---|
| [`src/MMV.Domain/Interfaces/Time/IClock.cs`](../../src/MMV.Domain/Interfaces/Time/IClock.cs) | **T1.** Abstraction du temps, en Domain, neutre. Deux membres : `UtcNow` (instant, `Kind = Utc` garanti) et `LocalToday` (date civile du poste). |
| [`src/MMV.Infrastructure/Services/SystemClock.cs`](../../src/MMV.Infrastructure/Services/SystemClock.cs) | **T1.** Implémentation système. **Unique point d'accès à l'horloge du système d'exploitation dans tout `src/**`**, et unique entrée de la liste blanche du test d'architecture. |
| [`src/MMV.Infrastructure/Data/Time/UtcDateTimeConverter.cs`](../../src/MMV.Infrastructure/Data/Time/UtcDateTimeConverter.cs) | **T3.** Convertisseur de valeur **validant** (option D3) + `ApplyTo(ModelBuilder)`, qui **balaye** le modèle au lieu d'énumérer les colonnes. |
| [`src/MMV.Infrastructure/Data/Time/NonUtcDateTimeException.cs`](../../src/MMV.Infrastructure/Data/Time/NonUtcDateTimeException.cs) | **T3.** Échec explicite, avec un message qui **désigne la correction à faire** (injecter `IClock`, ou reconnaître une date civile) et interdit nommément `ToUniversalTime()`. |
| [`src/MMV.App/Time/DatePickerCivilDate.cs`](../../src/MMV.App/Time/DatePickerCivilDate.cs) | **T6.** Pont **unique** entre le `DateTimeOffset` du `DatePicker` d'Avalonia et le `DateOnly` du Domain. Remplace les six `.UtcDateTime` / `new DateTimeOffset(...)` qui décalaient le jour. |

### Ajoutés — tests (11)

| Fichier | Tests | Ce qu'il prouve |
|---|---|---|
| `tests/MMV.Domain.Tests/Data/Time/ClockTests.cs` | **8** | Contrat d'`IClock` : `Kind = Utc` **et** l'horloge avance ; déterminisme de `FixedClock` ; dissociation instant / date civile. |
| `tests/MMV.Domain.Tests/Data/Time/UtcDateTimeConverterTests.cs` | **15** | La règle (accepté / refusé / **jamais corrigé**) ; la couverture (**19/19** propriétés d'instants, sur **les deux providers**) ; l'absence d'effet de schéma. |
| `tests/MMV.Domain.Tests/Data/Time/UtcPersistenceRoundTripTests.cs` | **16** | L'invariant **tel que la base l'applique** : refus total sans écriture partielle, refus d'un **paramètre de requête** non-UTC, relecture en `Kind = Utc`, ordre chronologique, format `yyyy-MM-dd` des dates civiles. |
| `tests/MMV.Domain.Tests/Data/Time/LegacyCivilDateFormatTests.cs` | **6** | **La dette de reprise, prouvée** : une base au format historique **échoue** à la lecture ; la reprise attendue est une troncature exprimable en SQL pur ; les instants, eux, ne sont pas concernés. |
| `tests/MMV.Domain.Tests/Architecture/AmbientTimeArchitectureTests.cs` | **5** | **T4.** Zéro temps ambiant dans `src/**` ; liste blanche à une seule entrée, existante et réellement utilisée ; migrations explicitement hors périmètre. |
| `tests/MMV.Application.Tests/UseCases/Time/InjectedClockDeterminismTests.cs` | **8** | Le temps est une **donnée d'entrée** : égalités **strictes** sur `SaleDate`, `OrderDate`, `CreatedAt`, `ResolvedAt` ; « un acte = un instant » ; deux horloges ⇒ deux résultats ; horloge nulle refusée. |
| `tests/MMV.App.Tests/Time/DatePickerCivilDateTests.cs` | **12** | Le décalage de jour est supprimé, **fuseaux explicites** (+02:00, −05:00, UTC) pour que la preuve vaille aussi en CI ; aller-retour ; dates limites ; date absente. |
| `tests/MMV.App.Tests/ViewModels/PrescriptionFormCivilDateTests.cs` | **7** | La date par défaut vient de l'horloge injectée ; la date transmise est la date **saisie** ; un aller-retour sans modification ne change rien. |
| `tests/{Domain,Application,App}.Tests/TestDoubles/FixedClock.cs` | — | Horloge de test, **qui refuse elle-même un instant non-UTC** afin que l'échec survienne là où la valeur est choisie. Voir *Questions*, point 7. |

**Total : +77 tests.**

### Modifiés — production (17)

| Couche | Fichiers | Changement |
|---|---|---|
| **Domain** | `Entities/Notification.cs` | Défaut `CreatedAt` : `DateTime.Now` → `DateTime.UtcNow` |
| | `Entities/Customer.cs` | `BirthDate` : `DateTime?` → **`DateOnly?`** |
| | `Entities/Prescription.cs` | `IssueDate` : `DateTime` → **`DateOnly`** |
| | `Validators/PrescriptionValidator.cs` | Borne « +1 jour » recalculée en dates civiles |
| | `Interfaces/Repositories/IOrderRepository.cs` | `GetOverdueOrdersAsync(DateTime asOfUtc, …)` |
| | `Interfaces/Repositories/IPrescriptionRepository.cs` | `GetByDateRangeAsync(DateOnly, DateOnly, …)` |
| | **`Interfaces/Time/IClock.cs`** *(ajouté)* | — |
| **Application** | 4 use cases (`RegisterSale`, `GenerateLowStockNotifications`, `AdvanceOrderStatus`, `SettleOrderBalance`) | `IClock` injecté, **obligatoire** |
| | 3 commandes + 3 DTO (Customers, Prescriptions) | `DateTime` → `DateOnly` sur les deux dates civiles |
| **Infrastructure** | `Data/OpticDbContext.cs` | `UtcDateTimeConverter.ApplyTo(modelBuilder)` en **fin** d'`OnModelCreating` |
| | `Repositories/OrderRepository.cs` | `DateTime.Now` retiré de la comparaison SQL |
| | `Repositories/PrescriptionRepository.cs` | Bornes `DateOnly` |
| | `Data/DbInitializer.cs`, `Seeders/DbSeeder.cs` | Dates civiles en `DateOnly` ; `DateTime.Now` retiré |
| | `DependencyInjection.cs` | `services.AddSingleton<IClock>(SystemClock.Instance)` |
| **UI** | `App.axaml.cs` | Même enregistrement, dans le **composition root réel** |
| | `CustomerFormViewModel`, `PrescriptionFormViewModel` | Conversions civiles par `DatePickerCivilDate` ; `IClock` optionnel |
| | `OrderFormViewModel`, `OrderDetailViewModel` | UTC ; `IClock` optionnel |
| | `Views/Clients/CustomersView.axaml.cs` | `SystemClock.Instance.UtcNow` |

### Modifiés — tests (50)

Deux catégories, à ne pas confondre :

1. **Adaptation mécanique de signature (26 fichiers)** — ajout de l'argument `IClock` aux 28 sites de
   construction des 4 use cases (`SystemClock.Instance`, pour préserver la sémantique existante), et
   conversion des littéraux de dates civiles en `DateOnly`. Aucun changement d'intention.
2. **Fautes révélées par le convertisseur (9 fichiers, 24 valeurs)** — voir *Test Results*.

### Non touchés — vérifié par `git status`

- `src/MMV.Infrastructure/Migrations/**` — **les 14 migrations et le snapshot sont bit-à-bit inchangés.**
- `.github/workflows/**` — aucune modification.
- `docs/architecture/adr-*` — **aucun ADR modifié.**
- `*.csproj`, `*.sln`, `global.json`, `Directory.Build.props` — **aucune modification, aucun paquet ajouté.**

---

## Architecture Impact

### Ce qui change dans la structure

| Avant P4-5D | Après P4-5D |
|---|---|
| Le temps est une **propriété statique** lue partout | Le temps est une **dépendance déclarée**, injectée par le composition root |
| `Kind` du `DateTime` persisté : **non contraint** | `Kind = Utc` **obligatoire**, imposé par le modèle EF, sur les deux providers |
| `Kind` relu : **Unspecified** (SQLite) / **Utc** (PostgreSQL) | `Kind = Utc` **des deux côtés** |
| Dates civiles : `DateTime`, convertibles, donc décalables | `DateOnly`, **non convertibles**, donc non décalables |
| Faute temporelle : **silencieuse**, visible en production | Faute temporelle : **échec au test**, sans serveur |

### Les trois mécanismes, et leur complémentarité

Ils ne se recouvrent pas — c'est pourquoi l'ADR retient D2 **et** D3 :

1. **`IClock`** supprime la **cause** (personne n'a plus de raison d'appeler l'horloge ambiante).
2. **Le convertisseur validant** attrape ce qui **franchit la frontière de persistance** — y compris les
   paramètres de requête traduits en SQL, y compris une valeur venue d'un chemin non recensé.
3. **Le test d'architecture** attrape ce que le convertisseur ne **peut pas** voir : `OrderDetailViewModel`
   ne persistait rien, il **calculait faux**. Aucun test fonctionnel ne l'aurait signalé.

### Portée sur le respect des couches

- `IClock` est en **Domain**, sans dépendance ; `SystemClock` en **Infrastructure**. Conforme à §5.2.
- Le convertisseur est en **Infrastructure**, appliqué **par balayage** du modèle plutôt que par une liste
  écrite à la main : une liste serait une seconde source de vérité, et la première propriété datée ajoutée
  après P4-5D y échapperait en silence.
- Le convertisseur **n'interroge jamais `Database.ProviderName`** et n'est pas construit par
  `ModelPortability` : contrairement à P4-5C qui **choisit** selon le moteur, il **impose** la même règle
  des deux côtés. C'est exactement ce qui rend la suite SQLite représentative de PostgreSQL.

### Effet sur le schéma : aucun

`dotnet ef migrations has-pending-model-changes` ⇒ **« No changes have been made to the model since the last
migration. »** Le convertisseur laisse le type fourni au provider à `DateTime` ; `DateOnly` se mappe sur le
même `TEXT` SQLite. **C'est le résultat attendu — et c'est aussi le piège**, voir *Risks*.

---

## DateTime Changes

### Ce qui a été remplacé, et ce qui ne l'a pas été

**Remplacé** — 17 `DateTime.Now` + 2 `DateTimeOffset.Now`, chacun justifié dans le tableau d'analyse
ci-dessus. Le critère appliqué est celui de la mission : **la valeur représente-t-elle un événement métier,
une valeur persistée, ou un calcul sur un instant persisté ?** Les trois répondaient oui dans tous les cas
rencontrés.

**Non remplacé, délibérément :**

- **Les 57 `DateTime.UtcNow` restants de `src/**`.** Ils produisent déjà `Kind = Utc` : ils sont
  persistables et comparables entre postes. L'ADR demande de faire **disparaître les `DateTime.Now`**, pas
  de convertir tous les `UtcNow`. Les convertir serait un lot à part entière, au bénéfice bien moindre.
  Sites concernés : `CustomerService`, `UpdateCustomerUseCase`, `SetCustomerArchivedUseCase`,
  `CreateUserUseCase`, `CreateOrderUseCase`, `AuthenticationService`, `DbInitializer`, `DbSeeder`,
  `DatabaseSeeder`, `WorkshopSheetQcDecision`. Voir *Questions*, point 5.
- **Les commentaires** citant `DateTime.Now` pour expliquer la règle. Les interdire jusque dans la prose
  interdirait de documenter la règle.
- **Le formatage d'affichage** (`StringFormat` XAML). Conforme à §5 décision 6 : la conversion en heure
  locale n'a lieu qu'au formatage, et aucune valeur convertie ne repart vers la persistance.

### Cohérence temporelle intra-transaction

Un choix **au-delà du strict remplacement**, et qu'il faut valider : dans `RegisterSaleUseCase` et
`GenerateLowStockNotificationsUseCase`, l'horloge est lue **une seule fois** par exécution, et l'instant
obtenu est réutilisé pour toutes les écritures de la transaction. Cinq horodatages d'une même vente
décrivent **un seul acte de gestion** ; cinq lectures d'horloge en auraient fait cinq événements légèrement
distincts, et auraient rendu tout test daté dépendant de sa propre durée d'exécution.

---

## IClock Implementation

```csharp
public interface IClock
{
    DateTime UtcNow { get; }    // contrat : Kind == Utc, toujours
    DateOnly LocalToday { get; } // date civile du poste
}
```

### Pourquoi deux membres et non un

`LocalToday` n'est pas une commodité, c'est la **contrepartie de la décision 7**. Une date civile suit le
calendrier de l'opérateur. À 23:30 UTC le 18, un poste en UTC+2 est **déjà le 19** : une ordonnance qu'il
saisit porte la date du 19. Dériver la date civile d'`UtcNow` aurait réintroduit, dans la valeur par défaut
du formulaire, exactement le décalage d'un jour que le passage à `DateOnly` supprime. Le test
`LaDateParDefaut_EstLaDateCIVILEDuPoste_PasCelleDUtc` fige ce comportement.

### Cycle de vie

Enregistré en **`Singleton`** — et non `Scoped` comme les repositories : `SystemClock` est sans état, ne
touche ni au `DbContext` ni à une transaction, et il ne doit exister **qu'une horloge dans le processus**.
L'instance enregistrée est `SystemClock.Instance` elle-même, celle que lisent les rares chemins non
injectés : il n'existe donc jamais deux sources de temps divergentes.

### Injection : obligatoire en Application, optionnelle en UI

| Couche | Forme | Nombre de sites | Raison |
|---|---|---|---|
| **Application** (4 use cases) | `IClock clock` **obligatoire**, `ArgumentNullException` si nul | 28 sites de construction mis à jour | Un use case sans horloge ne peut plus horodater : il doit le dire à la construction. |
| **UI** (3 ViewModels) | `IClock? clock = null` ⇒ `SystemClock.Instance` | 0 site de construction touché | Les ViewModels sont construites **à la main** (navigation, code-behind), pas résolues par le conteneur. Motif déjà en usage dans le dépôt (`INotificationRepository? = null`). **Voir *Questions*, point 1.** |
| **Code-behind Avalonia** | `SystemClock.Instance` | 1 | Aucune injection disponible : le contrôle est construit par le framework. |
| **Seeders** (`DbInitializer` statique, `DbSeeder`) | `SystemClock.Instance` | 3 | `DbInitializer` est une classe **statique**. **Voir *Questions*, point 2.** |

---

## UTC Enforcement

### Mécanisme

`UtcDateTimeConverter : ValueConverter<DateTime, DateTime>`, appliqué par balayage à **toute** propriété
`DateTime` / `DateTime?` du modèle, en **fin** d'`OnModelCreating` :

- **Écriture** ⇒ `NonUtcDateTimeException` si `Kind != Utc`. **Aucune correction silencieuse.**
- **Lecture** ⇒ `DateTime.SpecifyKind(value, Utc)`. Ce n'est pas une conversion : la valeur stockée *est*
  UTC par l'invariant d'écriture ; seul son étiquetage diffère selon le provider.

### Pourquoi le refus plutôt que la conversion

C'est le point que l'ADR tranche explicitement (§4.2, D1 rejetée). Un `ToUniversalTime()` automatique
« marcherait » — et c'est le problème : il **masquerait** un `DateTime.Now` oublié au lieu de le révéler, et
sur un `Unspecified` il **supposerait** un fuseau au lieu de le connaître. Le test
`EnsureUtc_NeCorrigeJamaisSilencieusement` existe précisément pour tomber si quelqu'un remplaçait un jour
le `throw` par une conversion.

### Portée mesurée

| Mesure | Valeur | Test |
|---|---|---|
| Propriétés d'instants du modèle | **19** | `LeModeleCompte_DixNeufProprietesDInstant` |
| Protégées, modèle **SQLite** | **19 / 19** | `ToutesLesProprietesDInstantDuModeleSqlite_PortentLeConvertisseur` |
| Protégées, modèle **PostgreSQL** | **19 / 19** | `ToutesLesProprietesDInstantDuModelePostgreSql_PortentLeConvertisseur` |
| Dates civiles soumises au convertisseur | **0** | `LesDatesCiviles_NeSontPasDesInstants_…` |
| Type fourni au provider | **`DateTime`** (inchangé) | `LeConvertisseur_NeChangePasLeTypeFourniAuProvider` |

Le convertisseur s'applique aussi aux **paramètres de requête** : un filtre non-UTC contre une colonne UTC
ne « marche presque » plus, il échoue (`UnParametreDeRequeteNonUtc_EchoueAussi`). C'est exactement le cas
d'`OrderRepository:167`.

### Test d'architecture (T4)

Interdit `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now` **et `DateTimeOffset.UtcNow`** dans
`src/**`, hors migrations, commentaires exclus.

**Liste blanche : une seule entrée** — `src/MMV.Infrastructure/Services/SystemClock.cs`. Trois tests
complémentaires empêchent qu'elle dérive : elle ne contient que `SystemClock` ; son entrée désigne un
fichier **réel** ; et `SystemClock` **utilise réellement** `DateTime.Now`, de sorte qu'une implémentation
qui « corrigerait » `LocalToday` en le dérivant d'`UtcNow` serait détectée.

---

## DateOnly Changes

### Analyse d'impact avant modification

| Axe | Constat mesuré | Décision |
|---|---|---|
| **Domain** | 2 propriétés, 1 règle de validation, 1 signature de repository | Converti |
| **Application** | 3 commandes, 3 DTO, 5 recopies de champ | Converti |
| **UI** | 8 occurrences `DateTimeOffset` sur 7 lignes, 2 ViewModels, 2 `DatePicker`, 4 `StringFormat` XAML | ViewModels convertis via `DatePickerCivilDate` ; **aucun `.axaml` modifié** — `DatePicker` reste lié à `DateTimeOffset?`, ce qu'Avalonia exige |
| **Données existantes** | Type de colonne SQLite **inchangé** (`TEXT`) ; **format changé** (`yyyy-MM-dd HH:mm:ss[.fffffff]` → `yyyy-MM-dd`) | **Reprise NON écrite** — hors périmètre de la mission. **Dette documentée et prouvée par test.** |

### Le bogue métier corrigé

Les deux formulaires envoyaient `picker.UtcDateTime` vers la couche Application. Le `DatePicker` restitue la
date choisie **à minuit avec le décalage local** : en heure d'été française, `15/03/1985 00:00 +02:00` devient
`14/03/1985 22:00` en UTC. **Le jour saisi n'était pas le jour enregistré**, sur une date de naissance et sur
une date d'ordonnance — donnée de santé. Pire : le décalage se reproduisait **à chaque
ouverture-enregistrement** d'une fiche, sans que personne n'y touche
(`UnAllerRetourSansModification_NeChangePasLaDate`).

`DatePickerCivilDate` ne lit et n'écrit que la **composante calendaire** ; l'heure et le décalage sont
ignorés, car ils n'ont aucun sens pour une date civile. Les tests emploient des décalages **explicites**
(+02:00, −05:00, UTC) : une preuve qui ne vaudrait qu'à Paris ne vaudrait rien en CI.

### Impact futur — ce que P4-7 devra faire

**Aucune migration n'a été créée ni modifiée.** Et EF n'en réclamera aucune :
`has-pending-model-changes` est **vert**, parce que le type de colonne SQLite ne change pas. C'est
précisément le piège décrit par l'ADR §7.2 comme « le point le plus dangereux ».

`LegacyCivilDateFormatTests` transforme ce silence en **fait mesuré et versionné** :

| Fait établi par test | Conséquence pour P4-7 |
|---|---|
| Une base au format `yyyy-MM-dd HH:mm:ss` **lève `FormatException`** à la lecture des clients | La reprise est **obligatoire** avant toute exécution sur une base existante |
| La variante `.fffffff` lève aussi | Une reprise qui ne traiterait qu'une seule forme laisserait des lignes cassées |
| La forme cible `yyyy-MM-dd` est lue sans perte | La transformation attendue est une **troncature**, jamais une conversion de fuseau |
| `UPDATE … SET "BirthDate" = substr("BirthDate", 1, 10)` suffit | La reprise est exprimable en **SQL pur**, sans code applicatif ni relecture ligne à ligne |
| Une valeur nulle n'a rien à corriger | La reprise ne doit pas inventer de date par défaut |
| Les **19 colonnes d'instants** gardent leur format | La reprise ne concerne **que** les deux dates civiles — plus la règle d'interprétation du `Kind` historique (décision 8) |

**Portée à couvrir : `Customers.BirthDate` et `Prescriptions.IssueDate`, sur toute base antérieure à
P4-5D.**

---

## Tests Executed

```
dotnet build MMV.sln -c Debug
dotnet test  MMV.sln -c Debug
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure
git status --short
```

Restauration NuGet : disponible sur le poste (cache chaud depuis P4-5C). **Aucun fichier de configuration
NuGet n'a été créé ni modifié.**

---

## Test Results

### Build

| Mesure | Valeur |
|---|---|
| Erreurs | **0** |
| Avertissements | **0** |

### Suite complète

| Projet | Avant P4-5D | Après P4-5D | Nouveaux | Échecs | Ignorés |
|---|---|---|---|---|---|
| `MMV.Domain.Tests` | 821 | **871** | +50 | 0 | 0 |
| `MMV.Application.Tests` | 620 | **628** | +8 | 0 | 0 |
| `MMV.App.Tests` | 239 | **258** | +19 | 0 | 0 |
| **Total** | **1 680** | **1 757** | **+77** | **0** | **0** |

### Dérive de modèle EF

> `No changes have been made to the model since the last migration.`

### Ce que le convertisseur a révélé — 29 échecs, tous réels

À sa première application, la suite est passée de 0 à **29 échecs**. C'est exactement l'effet annoncé par
l'ADR §7.2 (« il peut révéler des sites non recensés »). **Aucun n'était un faux positif** ; aucune
assertion n'a été affaiblie pour les faire passer.

| Cause | Fichiers | Valeurs | Correction |
|---|---|---|---|
| Fixtures écrivant `DateTime.Now` (donc `Local`) | 5 | 13 | `DateTime.UtcNow` |
| Littéraux d'instants sans `Kind` (donc `Unspecified`) | 4 | 11 | `DateTimeKind.Utc` explicite |
| Base héritée construite en SQL brut, `IssueDate` au format historique | 1 | 1 | Fixture réécrite au format civil `yyyy-MM-dd` |

Le dernier mérite d'être souligné : **`CustomerArchivingMigrationTests` a échoué en reproduisant exactement
le mode de défaillance de la reprise de données non écrite.** C'est ce qui a motivé l'écriture de
`LegacyCivilDateFormatTests`, pour que le fait soit consigné plutôt que contourné.

Les **24 fixtures** corrigées écrivaient donc, jusqu'ici, des instants non-UTC en base — silencieusement
sur SQLite, et refusés par PostgreSQL. Elles constituaient une mesure de la dette que ce lot supprime.

---

## Architecture Exceptions

Trois écarts sont **assumés**, et le sont **nommément** : une exception qui n'est pas écrite devient une
convention par défaut. Chacune est bornée — la portée autorisée est donnée, et ce qui est hors de cette
portée est **interdit**, pas simplement déconseillé.

### 1. `IClock` optionnel dans les ViewModels

**Forme.** En couche **Application**, `IClock` est un paramètre **obligatoire** (`ArgumentNullException` si
nul). En couche **UI** seulement, les ViewModels le déclarent `IClock? clock = null` et retombent sur
`SystemClock.Instance`.

**Raison.**

- Les ViewModels Avalonia sont **construites à la main** — par la navigation, par le code-behind, par une
  ViewModel parente — et **non résolues par le conteneur**. Il n'existe pas de point unique où injecter.
- Rendre le paramètre obligatoire aurait imposé de modifier **25 sites de construction** de ViewModels,
  dans du code que ce lot n'a par ailleurs aucune raison de toucher : une refonte **hors périmètre**, au
  risque proportionnel.
- **Le comportement d'exécution est rigoureusement identique** : le défaut est `SystemClock.Instance`,
  c'est-à-dire **la même instance** que celle enregistrée en `Singleton` dans le conteneur. Il n'existe
  jamais deux sources de temps divergentes ; l'oubli d'injection n'a aucune conséquence fonctionnelle,
  seulement une conséquence de **testabilité**.
- Le motif **existe déjà dans le dépôt** (`INotificationRepository? = null` dans deux use cases) : ce n'est
  pas un précédent nouveau.

**Portée autorisée.** ViewModels de `MMV.App` **uniquement**.
**Interdit.** Tout paramètre `IClock` optionnel en **Domain**, **Application**, **repository** ou
**service métier** — ces couches sont construites par le conteneur ou par leurs tests, et n'ont donc
aucune raison de se passer d'une injection explicite.

### 2. `SystemClock.Instance` en accès statique

`SystemClock` est l'**unique** point d'accès à l'horloge du système d'exploitation dans tout `src/**`, et
l'**unique** entrée de la liste blanche du test d'architecture. Son accès **statique** reste néanmoins une
exception, limitée aux chemins où **aucune injection n'est disponible**.

**Autorisé uniquement dans :**

| Emplacement | Site réel | Pourquoi aucune injection n'est possible |
|---|---|---|
| **Composition root** | `App.axaml.cs`, `DependencyInjection.cs` | C'est le point qui **construit** le graphe : il ne peut pas s'injecter à lui-même. |
| **Code framework UI** | `Views/Clients/CustomersView.axaml.cs` | Le contrôle est instancié par Avalonia, sans conteneur ni constructeur paramétrable. |
| **Seeders** | `DbInitializer` (classe **statique**), `DbSeeder` | Données de démonstration, hors chemin métier ; `DbInitializer` n'a pas d'instance. |

**Interdit dans :**

- le **Domain** — entités, validateurs, services de domaine ;
- la couche **Application** — tout use case prend `IClock` en paramètre **obligatoire** ;
- les **repositories** — un repository ne décide pas de l'instant courant ; s'il a besoin d'un instant, il
  le reçoit **en paramètre de méthode** (`GetOverdueOrdersAsync(DateTime asOfUtc, …)`) ;
- les **services métier**.

Cette frontière n'est pas seulement documentée : elle est **exécutoire**. Toute violation consisterait à
lire l'horloge ailleurs, donc à réintroduire `DateTime.Now`/`DateTime.UtcNow` dans une de ces couches — ce
que le test d'architecture et l'injection obligatoire rendent visibles.

### 3. `FixedClock` dupliqué dans trois projets de test

**Forme.** `tests/{Domain,Application,App}.Tests/TestDoubles/FixedClock.cs` — même contenu, trois fois.
**Raison.** La solution ne comporte **aucun projet de support de test partagé** ; en créer un
(`MMV.TestSupport`) modifierait `MMV.sln` — explicitement hors périmètre de ce lot. Placer `FixedClock` en
Infrastructure aurait ajouté au code de production une classe **sans consommateur runtime**, exactement le
type de code mort que P3-6/P3-7 ont supprimé.
**Portée autorisée.** Projets de **test uniquement**. À reconsidérer si un quatrième projet de test
apparaît, ou si `MMV.TestSupport` est créé pour une autre raison.

---

## Risks

| # | Risque | Gravité | État |
|---|---|---|---|
| **R1** | **Bases SQLite existantes illisibles** : le format des dates civiles change sans qu'EF ne génère de migration. Toute base antérieure à P4-5D lève `FormatException` à la lecture d'un client ou d'une ordonnance. | **ÉLEVÉE** | **Ouvert — délibérément.** La reprise est l'obligation T5/T8, explicitement hors périmètre de la mission. **Prouvé** par `LegacyCivilDateFormatTests` (6 tests), avec la requête de correction. **Ne pas déployer sur une base existante avant P4-7.** |
| **R2** | Un chemin d'écriture **non couvert par les tests** peut porter un instant non-UTC et ne lever qu'en production. | Moyenne | Atténué, non éliminé. Les 29 échecs ont révélé tous les chemins exercés ; le test d'architecture couvre l'écriture du code. Un chemin **ni testé ni écrit avec `DateTime.Now`** (par exemple un `new DateTime(y,m,d)` sans `Kind`) resterait invisible. **P4-5F (T7) doit le confirmer contre PostgreSQL.** |
| **R3** | Le paramètre `IClock?` optionnel des ViewModels permet d'oublier l'injection — le défaut masque l'oubli. | Faible | Assumé. Le défaut est **la même instance** que celle du conteneur : l'oubli n'a aucune conséquence de comportement, seulement de testabilité. **Voir *Questions*, point 1.** |
| **R4** | `SystemClock.Instance` en accès statique (3 sites) est un point de couplage fixe, non substituable en test. | Faible | Assumé. Les trois sites sont des chemins sans conteneur (code-behind Avalonia, seeder statique). **Voir *Questions*, point 2.** |
| **R5** | La **dérive d'horloge entre postes** n'est pas résolue — seule la moitié « fuseau » l'est. | Moyenne | **Hors périmètre, par décision d'ADR** (§5 décision 9). Exigence NTP → P4-8 ; supervision → P4-9 ; horodatage serveur → §5.1 si la dérive mesurée le justifie. |
| **R6** | `FixedClock` est **dupliqué** dans trois projets de test. | Très faible | Assumé : la solution ne comporte aucun projet de support de test partagé, et en créer un dépasse le périmètre. **Voir *Questions*, point 7.** |
| **R7** | Le changement de signature `GetOverdueOrdersAsync(DateTime asOfUtc, …)` touche une interface publique du Domain. | Très faible | La méthode n'a **aucun consommateur en production** (vérifié) — seulement l'implémentation et un décorateur de test. **Voir *Questions*, point 3.** |

---

## Questions For Architect Review

1. **`IClock` optionnel dans les ViewModels — acceptable ?**
   En Application, `IClock` est **obligatoire** (28 sites de construction mis à jour). En UI, il est
   `IClock? clock = null` avec défaut `SystemClock.Instance`, ce qui a évité de toucher **25 sites** de
   construction de ViewModels pour un bénéfice runtime nul. Le motif existe déjà dans le dépôt
   (`INotificationRepository? = null` dans deux use cases). **Faut-il le rendre obligatoire malgré le coût,
   par cohérence de règle ?**

2. **`SystemClock.Instance` en accès statique — 3 sites.**
   `CustomersView.axaml.cs` (code-behind construit par Avalonia), `DbInitializer` (classe **statique**) et
   `DbSeeder`. Aucune injection n'y est disponible sans refonte. **Accepté comme point de couplage nommé, ou
   faut-il transformer `DbInitializer` en classe d'instance recevant `IClock` ?**

3. **`GetOverdueOrdersAsync(DateTime asOfUtc, …)` — signature plutôt qu'injection.**
   `OrderRepository` est instancié directement par `UnitOfWork` et par **plus de cinquante sites de test** ;
   lui injecter `IClock` aurait propagé le changement partout. Rendre l'instant explicite dans la signature
   est plus petit **et** plus honnête (un repository ne décide pas de l'instant courant). **Validez-vous ce
   principe pour les futurs repositories ?**

4. **R1 — la reprise de données est la vraie décision à prendre.**
   Le code est prêt, la dette est prouvée et la requête de correction est écrite dans le test. Confirmez-vous
   que la migration de données reste bien en **P4-7** (décision 8 de l'ADR), et non en P4-5E ? **Tant qu'elle
   n'existe pas, ce lot ne doit pas être déployé sur une base existante.** Faut-il inscrire ce blocage
   ailleurs que dans ce rapport ?

5. **`DateTimeOffset.UtcNow` ajouté à la liste des interdits.**
   L'ADR §5 décision 5 en nomme **trois** (`DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`). J'ai
   ajouté `DateTimeOffset.UtcNow` : il n'est pas dangereux en soi, mais il ouvre la porte à des conversions
   implicites vers les colonnes `DateTime` du modèle. **À conserver, ou à retirer pour coller strictement
   à l'ADR ?**
   Question jumelle : les **57 `DateTime.UtcNow`** restants de `src/**` n'ont pas été routés vers `IClock`
   (seuls 4, situés dans les use cases déjà touchés, l'ont été pour la cohérence intra-transaction).
   **Faut-il un lot dédié, ou cette dette est-elle acceptable en l'état ?**

6. **Le test d'architecture ignore les commentaires.**
   Sans cela il serait rouge sur `DocumentSequenceConfiguration.cs:22` — dont le commentaire cite
   `DateTime.Now` pour expliquer pourquoi le seed **ne l'utilise pas** — et sur les commentaires posés par
   P4-5D lui-même pour documenter chaque remplacement. Le filtre est un simple découpage sur `//`.
   **Suffisant, ou faut-il un analyseur Roslyn en P4-5F ?**

7. **`FixedClock` dupliqué dans 3 projets de test.**
   Le mettre en production (Infrastructure) aurait ajouté une classe sans consommateur runtime — exactement
   le type de code mort que P3-6/P3-7 ont supprimé. Créer un projet `MMV.TestSupport` dépasse le périmètre
   de P4-5D. **Duplication acceptée, ou projet partagé à créer ?**

8. **Défaut d'entité `Notification.CreatedAt = DateTime.UtcNow`.**
   Une entité ne peut pas recevoir `IClock` par injection. Le défaut est aligné sur les neuf autres entités
   datées, et les quatre chemins applicatifs renseignent `CreatedAt` explicitement depuis l'horloge.
   **Acceptez-vous que ce dernier point reste une lecture directe de l'horloge, ou faut-il retirer le
   défaut et rendre l'horodatage obligatoire à la construction ?**

---

## Recommended Next Step

1. **Revue architecturale de ce lot** — en particulier les points **1**, **3**, **4** et **5** ci-dessus, qui
   engagent des principes réutilisables au-delà de P4-5D.
2. **Après validation**, commit du lot sur `p4-multi-poste`. Suggestion de message :
   `feat(P4-5D): implement UTC instants, injectable clock and civil dates`.
3. **Avant tout déploiement sur une base existante**, ordonnancer la **reprise de données des dates
   civiles** (T5/T8, P4-7). `LegacyCivilDateFormatTests` en contient déjà la spécification exécutable.
4. **P4-5E** (chaîne de migrations PostgreSQL) et **P4-5F** (intégration PostgreSQL, T7 : fidélité
   `DateTime` prouvée **avec assertion** contre un vrai serveur) peuvent se dérouler en parallèle de la
   reprise : ce lot ne les bloque plus.

---

## Vérification finale

```
$ git status --short
 M  … 67 fichiers modifiés
 ?? … 16 fichiers ajoutés (+ le présent rapport)

$ git rev-list --count b5c2073..HEAD
0

$ git rev-parse --short HEAD
b5c2073
```

| Contrôle | Résultat |
|---|---|
| Modifications locales présentes | **OUI** — 67 modifiés, 16 ajoutés |
| Commit créé | **NON** — `HEAD` est toujours `b5c2073` |
| Push effectué | **NON** |
| Branche créée | **NON** — toujours `p4-multi-poste` |
| Migration créée ou modifiée | **NON** — `src/MMV.Infrastructure/Migrations/` bit-à-bit inchangé |
| CI modifiée | **NON** |
| ADR modifié | **NON** |
| `.csproj` / `.sln` / paquet modifié | **NON** |
