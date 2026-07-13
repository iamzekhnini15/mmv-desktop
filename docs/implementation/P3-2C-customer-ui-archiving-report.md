# P3-2C — UI **Clients** : archivage, réactivation et suppression sûre

> **Mode : `IMPLEMENTATION_AND_REPORT`.** Étape **UI** de P3-2, après P3-2B (métier + persistance).
> **Aucune modification** de `MMV.Domain`, `MMV.Infrastructure`, des migrations, des relations EF, de la politique
> de suppression métier, ni de la CI. **Aucun commit, aucun push.**
> **Le dépôt réel prime toujours sur ce document.**
>
> Branche : `p3-business-rules`. Cadré par la [roadmap P3-2](../architecture/P3-business-rules-roadmap.md),
> l'[ADR-PROD-DB-001 multi-poste](../architecture/adr-prod-db-001-multi-poste-database-strategy.md) et
> l'[ADR frontières](../architecture/adr-application-boundaries.md).

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-2C |
| `EXECUTION_MODE` | `IMPLEMENTATION_AND_REPORT` |
| `SOURCE_BRANCH` | `p3-business-rules` |
| `ALLOW_CODE_CHANGES` / `ALLOW_TEST_CHANGES` / `ALLOW_DOC_CHANGES` | `true` |
| `ALLOW_MIGRATION` | **`false`** |
| `ALLOW_COMMIT` / `ALLOW_PUSH` | `false` / `false` |

---

## 2. Baseline (avant implémentation)

| Contrôle | Attendu | Résultat |
|---|---|---|
| Branche | `p3-business-rules` | ✅ |
| Working tree | propre | ✅ |
| `dotnet build -c Debug` | vert | ✅ **0 avertissement, 0 erreur** |
| `dotnet test --no-build` | ≥ 626 | ✅ **626** (App 192 · Application 198 · Domain 236) |
| `dotnet list … --vulnerable` | 0 | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK | ✅ `dotnet-ef` 8.0.27 |
| `ef migrations has-pending-model-changes` | false | ✅ « No changes … since the last migration » |
| `MMV.Application` pure | Domain + DI.Abstractions | ✅ |

### 2.1 Écart de précondition constaté (signalé, non bloquant)

Le prompt attendait **trois** commits, dont `docs(P3-2B): record customer archiving CI validation`. Le journal réel
n'en porte que **deux** (`docs(P3-2A): …`, `feat(P3-2B): …`) : **le commit de journalisation CI de P3-2B n'existe
pas**.

En revanche, les **deux conditions d'arrêt** énoncées (working tree propre, CI verte) sont **satisfaites** : le
`git status` est vide et la **CI du commit `8a518eb` (`feat(P3-2B)`) est `completed / success`**
(`gh run list --branch p3-business-rules`). L'écart porte donc sur la **tenue du journal documentaire**, pas sur
l'état du dépôt ni sur la validation. **P3-2C a été poursuivi**, l'écart étant signalé plutôt que masqué.

**Baseline = GO.**

---

## 3. Audit UI (avant toute modification)

Lecture intégrale de `CustomersListViewModel`, `CustomersViewModel`, `CustomerDetailViewModel`,
`CustomerInfoViewModel`, `src/MMV.App/Views/Clients/**`, `CustomersListViewModelTests`, plus un balayage
(`Dialog|Confirm|MessageBox|IsArchived|IncludeArchived|SetCustomerArchived|Reload|Refresh`) sur `src/MMV.App` et
`tests/MMV.App.Tests`.

| Question | Constat **réel** du dépôt |
|---|---|
| Mécanisme de dialogue existant | **Oui** : [`IDialogService`](../../src/MMV.App/Services/IDialogService.cs) — `ShowConfirmationAsync(title, message) → Task<bool>`, `ShowErrorAsync`, `ShowInformationAsync`. Implémenté par `DialogService` (Avalonia), enregistré **singleton** dans `App.axaml.cs`. |
| Service de confirmation existant | **Oui**, le même. Déjà utilisé pour la **suppression** dans `SuppliersViewModel`, `OrdersViewModel`, `ProductsViewModel` (titre conventionnel : « Confirmation de suppression »). |
| Convention pour les messages métier | Deux canaux : `BaseViewModel.ErrorMessage` (bandeau inline, déjà **lié** dans `CustomersView.axaml`) et `IDialogService.ShowErrorAsync` (modal, convention des flux de suppression). |
| Commandes de la liste Clients | `Create`, `Edit`, `Delete`, `Refresh`, `ViewDetails`, `NextPage`, `PreviousPage`. **Aucune** commande d'archivage. |
| Chargement / filtrage | `LoadCustomersAsync()` → `IListCustomersUseCase.ExecuteAsync(new ListCustomersQuery())` ; `ApplyFilter()` filtre **en mémoire** sur `SearchText` uniquement. |
| Dépendances constructeur | `IListCustomersUseCase`, `IDeleteCustomerUseCase`. **Aucun repository, aucun `IUnitOfWork`** (acquis P2C-3 / P2D-7C à préserver). |
| Tests existants | `CustomersListViewModelTests` — 6 tests, tous sur la suppression. Leur en-tête notait explicitement : *« le flux d'origine ne comporte aucun dialogue de confirmation »*. |

**Trois constats structurants** (qui ont dicté la conception) :

1. **`CustomersListViewModel` n'est pas résolu par le conteneur** : il est **construit à la main** par
   `CustomersViewModel` ([`CustomersViewModel.cs`](../../src/MMV.App/ViewModels/CustomersViewModel.cs)). Toute
   nouvelle dépendance doit donc être **injectée dans `CustomersViewModel`** (lui, transitoire dans le conteneur)
   puis **transmise**.
2. **La liste n'expose aucun bouton d'action** : `CustomersView.axaml` ne contient ni « Modifier » ni « Supprimer ».
   Un clic sur une ligne **ouvre la fiche détail**. `SelectedCustomer` n'était donc **jamais** renseigné depuis la
   liste, et `DeleteCommand` n'était **atteignable par aucun chemin d'écran** de la liste (seul un
   `ButtonSupprimer_Click` **orphelin** subsistait dans le code-behind, référencé par aucun XAML).
3. **Le code-behind est la convention de cette vue** pour les actions de ligne (`Border_PointerPressed`), comme dans
   `InventoryView.axaml.cs` (`ConfirmButton_Click`).

---

## 4. Mécanisme de confirmation retenu

**`IDialogService` existant, réutilisé tel quel.** **Aucun framework de dialogue n'a été introduit**, aucune
bibliothèque UI ajoutée, aucune abstraction créée : l'interface couvrait déjà exactement le besoin
(confirmation booléenne + message d'erreur modal).

`CustomersListViewModel` reçoit désormais `IDialogService` par injection (via `CustomersViewModel`). En test, il est
remplacé par un `StubDialogService` à réponse programmable — le flux « annulation » devient donc **testable**, ce
qu'il n'était pas auparavant (il n'existait pas).

---

## 5. Comportement de la suppression

`ExecuteDeleteAsync(customer)` — ordre exact :

1. **Confirmation explicite** : `ShowConfirmationAsync("Confirmation de suppression", …)` avec le message
   ```
   Supprimer définitivement ce client sans historique ? Cette action est irréversible.
   ```
   Exposé en constante publique `CustomersListViewModel.DeleteConfirmationMessage` (les tests s'y réfèrent sans le
   dupliquer). Le libellé **ne présente pas la suppression comme le moyen normal de retirer un client** : il la
   cantonne au client **sans historique** et annonce l'irréversibilité.
2. **Annulation ⇒ arrêt immédiat** : **aucun appel** à `IDeleteCustomerUseCase`, **aucun rechargement**, **aucune
   mutation locale**, aucun message. *(Prouvé par test.)*
3. Confirmation ⇒ `DeleteCustomerUseCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId })`.
4. `CustomerFound = false` ⇒ **rechargement** depuis la source, puis message clair (le client a pu être supprimé
   depuis un autre poste).
5. Succès ⇒ **rechargement** depuis la source (et **non** un `Customers.Remove(...)` local).
6. Refus métier ⇒ §6.

**L'archivage n'affiche aucune confirmation** (conformément au cadrage) : l'action est **réversible** — c'est
précisément ce qui la distingue de la suppression, et la convention du dépôt ne confirme que les actions
destructives.

**Les `CanExecute` ne sont qu'un confort d'écran.** L'autorité reste le use case (qui refuse tout client porteur
d'historique), et la base le dernier rempart (clés étrangères `Restrict`, P3-2B). La sécurité **ne repose à aucun
moment** sur la disponibilité d'un bouton.

---

## 6. Gestion du refus métier

```csharp
catch (BusinessRuleException ex)
{
    await ReloadPreservingSelectionAsync(customer.CustomerId);   // le client N'EST PAS retiré localement
    ErrorMessage = ex.Message;                                   // bandeau inline
    await _dialogService.ShowErrorAsync(DeleteConfirmationTitle, ex.Message);   // modal, incontournable
}
catch (Exception ex)   // générique, resté SÉPARÉ
{
    ErrorMessage = $"Erreur lors de la suppression du client : {ex.Message}";
}
```

- **Capture spécifique** de `BusinessRuleException` (P3-2B : refus dur = exception typée, ADR frontières §8), avec
  un **traitement générique distinct** conservé pour les erreurs inattendues.
- **Aucune comparaison de chaîne fragile.** Le prompt autorisait à s'appuyer sur la constante publique
  `DeleteCustomerUseCase.CustomerHasHistoryMessage` ; le choix retenu est **encore plus robuste** : le message porté
  par l'exception **est déjà** ce message (c'est le use case qui l'y met), et il est **déjà rédigé pour
  l'utilisateur** — il est donc **affiché tel quel**, sans être comparé à quoi que ce soit. Aucune règle métier
  future n'aura besoin de modifier ce `catch`, et **aucun couplage textuel** n'est introduit. Le **test**, lui,
  vérifie l'égalité avec la constante publique : le contrat est donc **verrouillé** sans être **dupliqué**.
- **L'archivage est proposé** : le message métier lui-même l'indique (« *Ce client possède un historique et ne peut
  pas être supprimé. **Archivez-le à la place.*** »), et le bouton **Archiver** est présent **sur la ligne même**.
- **Ni type technique, ni pile d'appel** ne sont affichés (asserté par test :
  `DoesNotContain("Exception")`, `DoesNotContain("Erreur lors de la suppression")`).
- **Le client n'est pas retiré de la collection locale** ; la liste est **rechargée** depuis
  `IListCustomersUseCase` pour refléter l'état partagé.

---

## 7. Archivage et réactivation

**Un seul use case injecté** : `ISetCustomerArchivedUseCase` (P3-2B). Aucun repository, aucun `IUnitOfWork`, aucun
`DbContext` n'entre dans un ViewModel.

**Deux commandes distinctes, fondées sur un état absolu** — jamais un basculement aveugle :

| Commande | Disponible pour | Commande envoyée |
|---|---|---|
| `ArchiveCommand` | client **actif** visé | `SetCustomerArchivedCommand { IsArchived = true }` |
| `ReactivateCommand` | client **archivé** visé | `SetCustomerArchivedCommand { IsArchived = false }` |

- **L'intention provient de l'état réellement affiché** : le bouton cliqué appartient à la **ligne**, et le
  `CustomerListItemDto` de cette ligne est passé **en paramètre de commande**. On n'infère jamais l'état cible à
  partir d'un état local supposé frais (« si archivé alors désarchiver »).
- **Idempotence applicative conservée** : la VM demande toujours un **état absolu**, donc réappliquer le même état
  reste un no-op côté Application (P3-2B : aucune écriture, `UpdatedAt` intact). Deux postes convergent.
- **Introuvable** (`CustomerFound = false`) ⇒ message clair + rechargement.
- **Succès** ⇒ **rechargement depuis la base**, puis **restauration raisonnable de la sélection** : le client
  n'est re-sélectionné **que s'il reste visible** (un client archivé disparaît de la vue « actifs seulement » — la
  sélection retombe alors à `null`, ce qui est le comportement correct).

---

## 8. Filtre actifs / archivés

- Nouvelle propriété **`bool IncludeArchived`** sur `CustomersListViewModel`, **`false` par défaut** ⇒ la vue par
  défaut ne montre que les **clients actifs**.
- Tout changement **relance la lecture** : `ListCustomersQuery { IncludeArchived = … }`. Le filtre est donc
  appliqué **en base** (P3-2B pousse le `Where` en SQL) — **la collection locale n'est jamais filtrée après coup**
  sur `IsArchived`.
- Contrôle d'écran : case à cocher **« Afficher les clients archivés »** dans la barre d'outils.
- **Distinction visuelle légère** : badge **« ARCHIVÉ »** sur la ligne (`IsVisible="{Binding IsArchived}"`), styles
  existants réutilisés — **aucune refonte graphique**.
- **Pickers inchangés** : `ListCustomersForPickerUseCase` exclut **toujours** les archivés (P3-2B). **Aucun code de
  sélecteur n'a été touché en P3-2C.**

---

## 9. Stratégie multi-poste

**Après toute mutation — suppression, refus, archivage, réactivation — la liste est rechargée via
`IListCustomersUseCase`** (`ReloadPreservingSelectionAsync`).

Motif (ADR-PROD-DB-001) : la base centrale est partagée ; **un autre poste a pu modifier le client** entre
l'affichage et l'action. La collection locale n'est **pas** la vérité :

- `Customers.Remove(item)` mentirait sur le résultat réel d'une suppression ;
- `item.IsArchived = true` mentirait sur l'état réel — et serait de toute façon **impossible** ici, le
  `CustomerListItemDto` étant **immuable** (`init`), ce qui **interdit structurellement** la mutation locale comme
  source de vérité.

**Aucun temps réel, aucun polling** n'a été introduit (hors périmètre P3-2C).

---

## 10. Tests

**645 tests** (626 → **+19**), tous verts. **MMV.App.Tests : 192 → 211.**

`tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs` — réécrit autour de doublures explicites
(`SpyListCustomersUseCase` qui **mémorise les `ListCustomersQuery` reçues**, `SpyDeleteCustomerUseCase`,
`SpySetCustomerArchivedUseCase` qui **mémorise l'état absolu demandé**, `StubDialogService` à réponse programmable).

### Suppression (6)
- confirmation **acceptée** ⇒ use case appelé (commande construite depuis le client visé) ;
- confirmation **refusée** ⇒ **use case non appelé**, **aucun rechargement**, **aucun changement local**, aucun message ;
- **succès** ⇒ liste **rechargée** (`ExecuteCount == 2`), pas un `Remove` local ;
- **introuvable** ⇒ message clair **+ rechargement** ;
- **historique** ⇒ message métier **exact** (égal à `DeleteCustomerUseCase.CustomerHasHistoryMessage`), proposant
  l'archivage, **sans type ni pile technique**, **client conservé dans la collection**, **liste rechargée**, modal
  d'erreur affiché ;
- **erreur inattendue** ⇒ message **générique** (`"Erreur lors de la suppression du client : …"`), **distinct** du
  refus métier, **aucun modal** de refus.

### Archivage (4)
- client actif ⇒ commande envoie **`IsArchived = true`** (et le bon `CustomerId`) ;
- succès ⇒ **rechargement** ; le client archivé **disparaît** de la vue « actifs » et la sélection retombe à `null` ;
- introuvable ⇒ message clair ;
- **idempotence applicative conservée** : la VM envoie toujours un **état absolu** — après archivage puis
  rechargement, c'est **Réactiver** qui s'applique et qui demande **`false`** ; **jamais un toggle aveugle**.

### Réactivation (3)
- client archivé ⇒ commande envoie **`IsArchived = false`** ;
- succès ⇒ **rechargement** + **sélection restaurée** sur l'instance rechargée (désormais active) ;
- introuvable ⇒ message clair.

### Filtre (4)
- défaut ⇒ `IncludeArchived = false` **et** query émise avec `IncludeArchived = false` ;
- activation ⇒ **nouvelle lecture** avec `IncludeArchived = true` ;
- désactivation ⇒ **nouvelle lecture** avec `false` ;
- archivés inclus ⇒ le client archivé est **présent et identifiable** (`IsArchived`) dans la liste affichée.

### Disponibilité des commandes (3)
- aucune sélection ⇒ Delete / Archive / Reactivate / Edit **indisponibles** ;
- client **actif** ⇒ Delete, Archive, Edit disponibles ; Reactivate **non** ;
- client **archivé** ⇒ Reactivate disponible ; Delete, Archive, **Edit** **non**.

### Architecture (3)
- **aucune** dépendance `Repository` / `UnitOfWork` / `DbContext` / `MMV.Infrastructure` dans les **paramètres du
  constructeur** (assertion par réflexion) ;
- **aucune** surface de persistance dans les **propriétés publiques** (réflexion) ;
- **garde-fous de constructeur** sur les 4 dépendances.

---

## 11. Fichiers modifiés

**UI (4)**
- `src/MMV.App/ViewModels/CustomersListViewModel.cs` — `IncludeArchived`, `ArchiveCommand`, `ReactivateCommand`,
  confirmation, refus métier, `ReloadPreservingSelectionAsync`, `CanExecute` par état.
- `src/MMV.App/ViewModels/CustomersViewModel.cs` — injecte `ISetCustomerArchivedUseCase` + `IDialogService` et les
  transmet à `CustomersListViewModel` (qui n'est pas résolu par le conteneur).
- `src/MMV.App/Views/Clients/CustomersView.axaml` — case « Afficher les clients archivés », badge « ARCHIVÉ »,
  colonne d'actions (Archiver / Réactiver / Supprimer).
- `src/MMV.App/Views/Clients/CustomersView.axaml.cs` — handlers de ligne (convention existante de la vue) + garde
  empêchant un clic sur bouton d'ouvrir la fiche.

**Tests (1)** — `tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs` (6 → **25** tests).

**Docs (2)** — `docs/architecture/P3-business-rules-roadmap.md` · ce rapport.

**Intacts** : `src/MMV.Domain/**` · `src/MMV.Infrastructure/**` (dont `Migrations/**`) · `src/MMV.Application/**` ·
`src/MMV.App/App.axaml.cs` · `tests/MMV.Domain.Tests/**` · `tests/MMV.Application.Tests/**` · `.github/**` ·
`*.csproj` / `*.sln`.

### 11.1 Composition root — vérifiée, **non modifiée**

Le prompt demandait de **ne pas supposer** que `MMV.Application.DependencyInjection` est utilisé par
`App.axaml.cs`. **Vérification faite dans le dépôt** : `App.ConfigureServices()` appelle bien
**`services.AddApplication()`** ([`App.axaml.cs`](../../src/MMV.App/App.axaml.cs)), et
`ISetCustomerArchivedUseCase` **y est enregistré** (`AddScoped`, `DependencyInjection.cs`). `IDialogService` est
déjà enregistré (singleton). **Les deux dépendances sont donc résolvables sans aucune modification de la
composition root** — `App.axaml.cs` n'a **pas** été touché.

`CustomersViewModel` (transitoire) consomme déjà plusieurs use cases *scoped* : **aucune nouvelle classe de risque
de portée DI** n'est introduite.

---

## 12. Contrôles exécutés

`git branch --show-current` · `git status --short` · `git diff --stat` · `git diff --check` ·
`gh run list --branch p3-business-rules` (CI P3-2B) · `dotnet restore` · `dotnet build --no-restore -c Debug` ·
`dotnet test --no-build -c Debug` · `dotnet list … package --vulnerable --include-transitive` ·
`dotnet tool restore` · `dotnet ef migrations has-pending-model-changes` ·
`dotnet list src/MMV.Application … reference` / `… package`.

---

## 13. Résultats

| Contrôle | Attendu | Résultat |
|---|---|---|
| Build | vert | ✅ **0 avertissement, 0 erreur** |
| Tests | > 626 | ✅ **645** (App **211** · Application **198** · Domain **236**) — **+19** |
| Vulnérabilités | 0 | ✅ **0** (7 projets) |
| `has-pending-model-changes` | false | ✅ « No changes … since the last migration » |
| Migration | aucune | ✅ `Migrations/**` intact |
| Nouvelle dépendance | aucune | ✅ aucun paquet, aucune référence projet ajoutés |
| `MMV.Application` pure | Domain + DI.Abstractions | ✅ |
| Régression d'architecture | aucune | ✅ aucun repository / `UnitOfWork` / `DbContext` dans un ViewModel (asserté par test) |
| Domain / Infrastructure / CI | intacts | ✅ |
| `git diff --check` | propre | ✅ |

---

## 14. Risques résiduels

1. **Aucun garde-fou à l'écriture pour un client archivé** *(hérité de P3-2B — inchangé, et le plus important)*.
   `CreatePrescriptionUseCase` et `RegisterSaleUseCase` **ne vérifient toujours pas** `IsArchived`. L'UI P3-2C rend
   le cas **plus improbable encore** (archivés hors listes par défaut, **toujours** hors sélecteurs) mais **ne
   l'empêche pas**. **Il ne faut donc pas prétendre que l'archivage empêche toute nouvelle vente ou ordonnance : ce
   n'est pas codé.** Exigence obligatoire maintenue sur **P3-3** et **P3-7**.
2. **Course multi-poste : message dégradé** *(hérité de P3-2B, §12 du rapport P3-2B)*. Si un historique est créé
   **entre** le contrôle applicatif et le `DELETE`, une `DbUpdateException` (violation de contrainte) remonte au
   lieu de la `BusinessRuleException` : elle tombe alors dans le `catch` **générique** et l'utilisateur voit un
   message **technique** plutôt que le message métier. **Issue sûre — rien n'est supprimé, aucune donnée perdue.**
   La traduction propre relève d'une étape transverse « erreurs de persistance ».
3. **Rafraîchissement à la demande, pas en temps réel.** La liste est rechargée **après une mutation locale**, pas
   quand un **autre poste** modifie un client : entre deux actions, l'écran peut afficher un état périmé. Un
   `CanExecute` peut donc autoriser un bouton qui sera **refusé** par le use case — comportement **assumé et sûr**
   (l'autorité n'est jamais l'écran). Temps réel / polling explicitement **hors périmètre P3-2C**.
4. **Vue non exercée en GUI.** La couverture est au niveau **ViewModel** ; l'AXAML est **compilé** au build (donc
   structurellement valide) mais l'application **n'a pas été lancée** dans le cadre de cette étape. Les liaisons de
   ligne (`IsArchived`, badge, boutons) et les handlers de code-behind reposent sur les conventions **déjà en
   service** dans cette même vue (`Border_PointerPressed`) et dans `InventoryView`, mais **un contrôle visuel
   manuel reste recommandé** avant livraison.
5. **`Edit` refusé pour un client archivé** : choix appliqué (il faut réactiver d'abord). Aucun besoin métier
   contraire n'a été trouvé dans le dépôt ; si un tel besoin existe, c'est une décision **métier** à trancher, pas
   un défaut technique.
6. **Suppression sans historique = perte réelle.** Confirmée explicitement et irréversible, par conception : c'est
   l'outil de nettoyage des doublons / erreurs de saisie voulu par P3-2B. La confirmation est le **seul** rempart
   côté écran contre l'erreur humaine (il n'y a pas de corbeille).
7. **Écart documentaire de précondition** (§2.1) : le commit `docs(P3-2B): record customer archiving CI validation`
   est **absent** du journal, alors que la CI de P3-2B est bien verte.

---

## 15. Verdict

**P3-2C = GO local.**

- Suppression physique **confirmée explicitement** (message irréversible), **annulation = aucun appel, aucun
  changement** ✅
- Refus métier **capturé spécifiquement** (`BusinessRuleException`), affiché **en clair**, **proposant l'archivage**,
  **sans détail technique**, **sans retrait local trompeur**, **avec rechargement** ✅
- **Archivage / réactivation** fonctionnels, fondés sur un **état absolu** (jamais un toggle aveugle), idempotence
  applicative conservée ✅
- **Actifs / archivés** consultables (`IncludeArchived`, filtré **en base**), archivés **distinguables**, **pickers
  inchangés** ✅
- **Rechargement depuis la source après chaque mutation** (multi-poste) ✅
- **Aucune persistance directe dans l'UI** : un seul use case ajouté, **aucun** repository / `UnitOfWork` /
  `DbContext` — **asserté par test** ✅
- **Aucun nouveau framework de dialogue** : `IDialogService` existant réutilisé ✅
- **Composition root vérifiée** et **non modifiée** ✅
- Build vert · **645 tests** · 0 vulnérabilité · 0 migration · EF vert · `MMV.Application` pure ✅
- Risque résiduel n°1 **explicitement maintenu**, pas dissimulé ✅

**Aucun commit, aucun push.**

---

## 16. Prochaine étape

**P3-3 — Ordonnances** *(non commencée)*, avec l'**exigence obligatoire** héritée de P3-2B et reconduite ici :
`CreatePrescriptionUseCase` doit **refuser la création d'une ordonnance pour un client archivé, au moment de
l'écriture** — l'exclusion des listes et des sélecteurs (P3-2B/P3-2C) est une **commodité d'écran**, pas un
garde-fou. Idem pour **P3-7 — Ventes** (`RegisterSaleUseCase`).

Préalable de tenue de journal : **commiter P3-2C** puis **journaliser la validation CI** (et régulariser l'écart
§2.1 relatif à P3-2B).
