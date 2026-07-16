# P3-3C — UI Ordonnances alignée sur les règles métier P3-3B

> **Mode : `IMPLEMENTATION_AND_REPORT`.** Aucune modification Domain, Application ou Infrastructure. Aucune migration,
> aucun package, aucun workflow CI modifié. **Aucun commit, aucun push.**
> **Le dépôt réel prime sur le prompt et sur les rapports antérieurs.**
>
> Branche : `p3-business-rules`. Étape précédente : **P3-3B** (validation métier réelle, `GO`, CI verte).
> Cadré par le [rapport P3-3A](P3-3A-prescription-domain-audit-report.md), le
> [rapport P3-3B](P3-3B-prescription-validation-and-archived-customer-report.md), le
> [rapport P3-2C](P3-2C-customer-ui-archiving-report.md) (patron UI réutilisé), la
> [roadmap P3](../architecture/P3-business-rules-roadmap.md), l'[ADR frontières](../architecture/adr-application-boundaries.md)
> et l'[ADR-PROD-DB-001](../architecture/adr-prod-db-001-multi-poste-database-strategy.md).

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-3C |
| `EXECUTION_MODE` | `IMPLEMENTATION_AND_REPORT` |
| `SOURCE_BRANCH` | `p3-business-rules` |
| `ALLOW_CODE_CHANGES` / `ALLOW_TEST_CHANGES` / `ALLOW_DOC_CHANGES` | `true` |
| `ALLOW_MIGRATION` | **`false`** |
| `ALLOW_DOMAIN_CHANGES` / `ALLOW_APPLICATION_CHANGES` / `ALLOW_INFRASTRUCTURE_CHANGES` | **`false`** |
| `ALLOW_COMMIT` / `ALLOW_PUSH` | **`false`** |

---

## 2. Préconditions Git et CI

| Contrôle | Attendu | Résultat |
|---|---|---|
| `git branch --show-current` | `p3-business-rules` | ✅ |
| `git status --short` | propre | ✅ **vide** |
| `git rev-parse HEAD` | `982b349a3d465f2464c22e4e99e78019d20e78aa` | ✅ **exact** |
| Dernier commit | `docs(P3-3B): record prescription validation CI` | ✅ |

**Run CI inspecté** (`gh run view 29340721869 --json databaseId,url,headSha,headBranch,status,conclusion,event`) :

| Élément | Valeur |
|---|---|
| `databaseId` | **`29340721869`** ✅ |
| `headSha` | **`982b349a3d465f2464c22e4e99e78019d20e78aa`** ✅ |
| `headBranch` | `p3-business-rules` ✅ |
| `status` / `conclusion` | `completed` / ✅ **`success`** |
| `event` | `push` ✅ |
| URL | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29340721869 |

**Préconditions = GO.** Aucun fichier restauré, stashé ou supprimé.

---

## 3. Baseline locale

| Contrôle | Attendu | Résultat |
|---|---|---|
| `dotnet restore` | OK | ✅ |
| `dotnet build --no-restore -c Debug` | vert | ✅ **0 avertissement, 0 erreur** |
| `dotnet test --no-build -c Debug` | 723 | ✅ **723** — App **211** · Application **218** · Domain **294** ; 0 échec |
| `dotnet list … --vulnerable --include-transitive` | 0 | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK | ✅ `dotnet-ef` 8.0.27 |
| `ef migrations has-pending-model-changes` | aucune | ✅ « No changes … since the last migration » |
| `MMV.Application` — référence projet | `MMV.Domain` seule | ✅ |
| `MMV.Application` — packages top-level | `DI.Abstractions 8.0.1` seul | ✅ |

**Baseline = GO.**

---

## 4. Audit UI (avant toute modification)

Lectures intégrales : `PrescriptionFormViewModel`, `CustomerPrescriptionsViewModel`, `PrescriptionDetailViewModel`,
`CustomerDetailViewModel`, `CustomersViewModel`, `CustomersListViewModel` (patron P3-2C), `PrescriptionFormView.axaml`,
`CustomerPrescriptionsView.axaml(.cs)`, `IDialogService`, `App.axaml.cs`, `AppUiPersistenceGuardrailTests`, et les tests
existants (`PrescriptionFormViewModelTests`, `CustomerPrescriptionsViewModelTests`, `P2D5CustomerReadViewModelTests`,
`SaleFormViewModelTransactionTests`).

Les **dix questions exigées**, répondues **par le code** :

| # | Question | Réponse constatée |
|---|---|---|
| 1 | Construction de `PrescriptionFormViewModel` ? | **Manuelle**, dans `CustomerPrescriptionsViewModel.ExecuteCreate()` et `ExecuteEdit()`. |
| 2 | Résolu par DI ? | **Non.** Ni lui, ni `CustomerPrescriptionsViewModel` (construit par `CustomerDetailViewModel`), ni `CustomerDetailViewModel` (construit par `CustomersViewModel`). **Seul `CustomersViewModel` est résolu par DI.** |
| 3 | Signalement de la réussite au parent ? | Événement `PrescriptionSaved` (charge utile : entité `Prescription` reconstruite localement). |
| 4 | Rechargement des ordonnances par le parent ? | `OnPrescriptionSaved` ⇒ `LoadPrescriptionsAsync()` — **rechargement depuis la source déjà en place**. |
| 5 | Affichage des erreurs ? | `ErrorMessage` (`BaseViewModel`) dans un bandeau de pied + 9 `*Error` par champ. |
| 6 | `IDialogService` accessible ? | **Pas sur ce chemin.** Enregistré en singleton (`App.axaml.cs:159`) et déjà injecté dans `CustomersViewModel` (P3-2C), mais **jamais transmis** à la fiche détail. |
| 7 | Liaison réelle de `PrismBase` ? | `<TextBox Text="{Binding OdPrismBase}">`, watermark **« H/V/In/Out »** — `H` et `V` **n'existent pas** dans l'enum. |
| 8 | Câblage des boutons de suppression ? | **Aucun bouton de ligne.** La ligne (`PointerPressed` → `OnPrescriptionTapped`) ouvre le détail ; la suppression part de `PrescriptionDetailViewModel.DeleteRequested` ⇒ `CustomerPrescriptionsViewModel.ExecuteDeleteAsync`. |
| 9 | Confirmation possible sans nouveau framework ? | **Oui** — `IDialogService.ShowConfirmationAsync` existe et est le patron P3-2C. |
| 10 | Validations locales dupliquant le Domain ? | **9 méthodes** : 8 plages (sphère/cylindre/axe/addition × OD/OG) **désormais propriété du Domain**, + `ValidateDoctorName` (**règle inventée par l'UI**, sans propriétaire métier). |

> **Constat structurant.** Le `TODO: Ajouter confirmation` de `ExecuteDeleteAsync` était réel : une ordonnance était
> supprimée **physiquement, sans aucune confirmation**, sur un simple clic dans la fiche détail.

---

## 5. Mécanisme de dialogue retenu

`IDialogService` **existant** (`ShowConfirmationAsync` / `ShowErrorAsync` / `ShowInformationAsync`), **aucun nouveau
service, aucun nouveau framework**. Il est propagé par le **vrai chemin de composition**, pas par un enregistrement DI
supposé suffisant :

```
App.axaml.cs (DI, singleton)
  └─ CustomersViewModel            ← IDialogService déjà injecté (P3-2C)
       └─ CustomerDetailViewModel          ← + IDialogService  (nouveau paramètre)
            └─ CustomerPrescriptionsViewModel  ← + IDialogService  (nouveau paramètre)
                 └─ PrescriptionFormViewModel      ← + IDialogService  (nouveau paramètre)
```

> **Écart assumé au §6 du prompt.** `CustomerDetailViewModel` et `CustomersViewModel` ne figuraient pas dans la liste
> « conceptuelle » des fichiers autorisés, mais le **§17** impose de propager `IDialogService` **par le vrai chemin de
> composition** quand les ViewModels sont construits manuellement — ce que l'audit a prouvé (questions 1 et 2). Ces deux
> fichiers sont donc modifiés **au strict minimum** (un paramètre transmis, aucune logique ajoutée), et **`App.axaml.cs`
> n'a PAS eu besoin d'être touché** : la dépendance était déjà résolue au sommet de la chaîne.

---

## 6. Saisie de `PrismBase`

**Le `TextBox` libre a disparu** (OD **et** OG), remplacé par un `ComboBox` — conforme à la convention `ComboBox` du
dépôt (`ProductFormView`, `UsersView`, `SaleFormView`).

**Conventions existantes inspectées, et pourquoi aucune n'était réutilisable telle quelle :**

- `SaleFormViewModel.PaymentMethodOptions` = `ObservableCollection<string>` + `ConvertPaymentMethodFromString()`
  (`switch` sur des littéraux français) ⇒ c'est **exactement** le « parsing manuel de texte vers enum » et la
  « conversion de chaîne fragile » que le prompt interdit. **Écartée.**
- `ProductFormView` : `<ComboBox.Items><x:String>…` ⇒ chaînes en dur, même défaut. **Écartée.**
- Aucun converter, aucun modèle d'options typé, aucune convention d'enum **nullable** n'existe dans le dépôt.

**Retenu — représentation UI locale minimale** (autorisée explicitement par le §7), déclarée dans le fichier de la
ViewModel :

```csharp
public sealed record PrismBaseOption(PrismBase? Value, string Label);
```

| `Value` (Domain, persisté) | `Label` (affiché) |
|---|---|
| `null` | Aucune |
| `PrismBase.In` | Interne |
| `PrismBase.Out` | Externe |
| `PrismBase.Up` | Haut |
| `PrismBase.Down` | Bas |

- **L'enum Domain n'est pas dupliqué** : `Value` le porte **tel quel** ; seul le libellé est francisé ;
- **la valeur persistée reste toujours `In` / `Out` / `Up` / `Down`** — jamais le libellé français ;
- **`null` est explicitement sélectionnable** (« Aucune ») : ne pas mettre de base reste possible ;
- **aucune valeur `H` ou `V`** n'est proposée ; **le watermark erroné « H/V/In/Out » est supprimé** ;
- **aucune saisie libre**, **aucun parsing de chaîne**, **aucune conversion fragile** ;
- **OD et OG sont symétriques et indépendants** (`SelectedOdPrismBaseOption` / `SelectedOgPrismBaseOption`, projections
  de `OdPrismBase` / `OgPrismBase`, qui restent des `PrismBase?`).

Liaison AXAML : `ItemsSource="{x:Static vm:PrescriptionFormViewModel.PrismBaseOptions}"` (liste statique = **source
unique** des valeurs proposées), `SelectedItem="{Binding Selected{Od|Og}PrismBaseOption}"`.

> **C'est le correctif d'intégrité de saisie n°1 de P3-3B §28** : depuis P3-3B, la règle prisme ⇄ base est **active**
> alors que l'écran ne permettait **pas** de saisir une base valide. Toute saisie de prisme était donc **refusée**.

---

## 7. `DoctorName`

**L'obligation UI est supprimée** — elle n'avait **aucun propriétaire métier** (`DoctorName` est facultatif dans le
Domain **et** dans l'Application, P3-3B §10).

- `ValidateDoctorName()` **supprimée** ; propriété `DoctorNameError` **supprimée** ; binding AXAML **supprimé** ;
- le message **« Le nom du médecin est requis »** n'existe plus nulle part dans `src/` ;
- `null`, `""` et `"   "` **n'empêchent plus l'enregistrement** ;
- **aucune normalisation métier ajoutée** (la valeur part telle quelle) ;
- le libellé de la vue indique désormais le caractère facultatif : **« MÉDECIN PRESCRIPTEUR (FACULTATIF) »**, watermark
  « Dr. Nom Prénom (facultatif) ».

---

## 8. Déduplication des validations UI

**Les 8 validations de plages en dur sont supprimées de la ViewModel** (sphère, cylindre, axe, addition × OD/OG), ainsi
que les 8 propriétés `*Error`, `ClearErrors()` et `ValidateForm()`.

**Justification :** depuis P3-3B, ces règles sont **réellement exécutées** par `PrescriptionValidator` via les use
cases. Les maintenir dans l'UI en ferait une **seconde source de vérité**, condamnée à diverger — c'est **précisément**
ce qui avait rendu la saisie d'un prisme impossible entre P3-3B et P3-3C.

- **aucun second `PrescriptionValidator` UI** n'est créé ;
- **aucun message Domain n'est dupliqué** ;
- **ni cylindre/axe, ni prisme/base ne sont reprogrammés** dans la ViewModel ;
- **aucune vérification ergonomique n'a été jugée nécessaire** : les champs numériques sont liés à des `double?`/`int?`
  (une saisie non numérique n'atteint jamais la ViewModel), et le `ComboBox` ne peut produire qu'une valeur légale.
  **La commande part donc toujours telle qu'elle a été saisie, et c'est le use case qui tranche.**

Un test verrouille ce point (`Save_DoesNotApplyAnyLocalBusinessRule_ThatTheUseCaseWouldAccept`) : une VM qui refuserait
localement une commande que le use case accepte ferait **échouer** la suite.

---

## 9. Affichage des `ValidationErrors`

Après l'appel du use case, si `IsValid == false` :

- le formulaire **n'est pas fermé** ; les champs **ne sont pas vidés** ; **aucune réussite n'est signalée**
  (`PrescriptionSaved` **non émis**) ;
- les messages sont affichés dans un **résumé de validation** (`ValidationMessages`, `ObservableCollection<string>` +
  `HasValidationErrors`), rendu en pied de formulaire — **toujours visible**, une erreur **par ligne** ;
- **ordre conservé**, **doublons retirés** (`Distinct()`) ;
- **aucun nom d'exception, aucune pile** ;
- les textes proviennent **des validateurs Domain** : ils sont affichés **tels quels** et **jamais comparés** pour en
  déduire un comportement métier.

**Deux canaux disjoints, jamais redondants** (pas de « framework général de validation UI ») :

| Canal | Contenu |
|---|---|
| `ValidationMessages` (résumé) | erreurs de validation du résultat (`ValidationErrors`) |
| `ErrorMessage` (bandeau existant) | messages **uniques** : client introuvable · ordonnance introuvable · refus métier · erreur inattendue |

---

## 10. Client introuvable (`CustomerFound = false`)

Traité **explicitement**, sans exception :

> **« Ce client n'existe plus. Actualisez la fiche client avant de réessayer. »**
> (constante `PrescriptionFormViewModel.CustomerNotFoundMessage`, utilisée par les tests — aucun littéral dupliqué)

- formulaire **conservé**, champs **conservés**, **aucune réussite** ;
- **aucune `DbUpdateException`**, aucune information technique affichée ;
- **aucune réactivation inventée** pour un client **supprimé**.

---

## 11. Client archivé (`BusinessRuleException`)

**Capture spécifique** autour du use case, **avant** le `catch` générique :

- le message affiché est **exactement** celui porté par l'exception, soit la constante publique
  `CreatePrescriptionUseCase.CustomerArchivedMessage` :

  > **« Ce client est archivé. Réactivez-le avant de créer une nouvelle ordonnance. »**

- **combinaison retenue** (explicitement autorisée par le §12) : message **inline** dans `ErrorMessage` **et** modal
  d'erreur `IDialogService.ShowErrorAsync` — le refus devient **incontournable** ;
- formulaire **conservé**, champs **conservés**, **aucune réussite** ;
- **jamais** « BusinessRuleException », **jamais** de pile ;
- le `catch (Exception)` générique **subsiste, séparé**, pour les erreurs inattendues (message distinct, et **aucun
  modal** — une panne technique n'est pas un refus métier).

**Non fait, volontairement** (§12) : aucun bouton de réactivation, aucune navigation automatique, **aucun appel à
`ISetCustomerArchivedUseCase`**, aucune dépendance inter-domaine ajoutée. *Le message explique l'action à faire ; P3-3C
ne l'exécute pas.*

---

## 12. Mise à jour d'une ordonnance

- **aucun blocage local sur `IsArchived`** : la **correction** d'une ordonnance existante reste autorisée pour un client
  archivé (P3-3B §15) — un test le verrouille ;
- résultat **invalide** ⇒ `ValidationErrors` affichées, formulaire conservé, **aucune fausse réussite** ;
- **`PrescriptionFound == false`** ⇒ message clair (`PrescriptionNotFoundMessage`, texte historique conservé :
  « L'ordonnance à modifier est introuvable. »), **aucune fermeture silencieuse**, **jamais** traité comme un succès.

---

## 13. Normalisation d'axe et rechargement

- l'UI **accepte `Axis = 0`** avec un cylindre non nul et **l'envoie tel quel** ;
- **aucune normalisation locale** : le use case (`OpticalAxisNormalizer`, P3-3B) est l'**unique propriétaire** de la
  valeur canonique ; la VM ne force **jamais** 180 avant le retour ;
- après un succès, le parent **recharge depuis la source** — l'affichage ultérieur reflète donc `Axis = 180` ;
- la collection locale **n'est jamais la vérité**.

Test : `Save_SendsAxisZero_WithoutLocalNormalization` (la commande porte `0`, **pas** `180`).

---

## 14. Confirmation avant suppression

Le `// TODO: Ajouter confirmation` est **levé**. `IDialogService` existant, patron P3-2C :

| | |
|---|---|
| **Titre** | `Confirmation de suppression` (`DeleteConfirmationTitle`) |
| **Message** | `Supprimer définitivement cette ordonnance ? Cette action est irréversible.` (`DeleteConfirmationMessage`) |

| Cas | Comportement implémenté |
|---|---|
| **Refusée** | **aucun** appel au use case · **aucun** rechargement · **aucune** mutation locale · **aucun** message de réussite |
| **Acceptée** | `DeletePrescriptionUseCase` appelé **avec le `PrescriptionId` visé** |
| **Introuvable** | message clair (`PrescriptionNotFoundMessage`) **+ rechargement depuis la source** |
| **Succès** | **rechargement depuis la source** (jamais un simple `Remove` local) |
| **Erreur inattendue** | message générique **distinct** (`Erreur lors de la suppression : …`) |

**Aucun archivage d'ordonnance introduit.** La suppression reste **physique**, conformément au périmètre décidé.

---

## 15. Stratégie multi-poste (ADR-PROD-DB-001)

- après **toute** mutation réussie **et** après **tout** constat « introuvable », les ordonnances sont rechargées via le
  **use case de lecture réel** (`IListPrescriptionsByCustomerUseCase`) ;
- **aucun `Remove` local** comme source de vérité, **aucune** modification locale d'un DTO en supposant le résultat ;
- le flux création/modification **rechargeait déjà** après l'événement de succès (`OnPrescriptionSaved`) : ce
  comportement est **conservé et désormais prouvé par test** — **aucun second rechargement inutile** n'a été ajouté ;
- **aucun temps réel, aucun polling, aucun SignalR.**

Les tests prouvent le rechargement en **comptant les invocations réelles** du use case de lecture (spy sur les
`Invocations` du mock), pas en inspectant l'état interne de la VM.

---

## 16. Composition root

- **`App.axaml.cs` n'est PAS modifié** : `IDialogService` y était déjà enregistré (singleton) et déjà injecté dans
  `CustomersViewModel` — l'audit a prouvé qu'aucune dépendance nouvelle n'était irrésoluble ;
- la dépendance suit le **vrai chemin de composition** (§5), avec garde `ArgumentNullException` à chaque niveau,
  conformément au reste du dépôt ;
- **aucune ViewModel n'a reçu** de repository, `IUnitOfWork`, `DbContext`, type `MMV.Infrastructure` ou service Domain
  d'écriture ;
- les garde-fous d'architecture par **réflexion** (`AppUiPersistenceGuardrailTests`, allowlists **vides** depuis P2D-7)
  couvrent **automatiquement** les ViewModels modifiés et **restent verts** ; un test **explicite** supplémentaire
  (`ViewModels_TakeNoPersistenceDependency`) vérifie par réflexion les constructeurs **et** propriétés publiques des deux
  ViewModels d'ordonnance.

---

## 17. Tests

| Fichier | Avant | Après | Δ |
|---|---|---|---|
| `ViewModels/PrescriptionFormViewModelTests.cs` | 6 | **26** | **+20** |
| `ViewModels/CustomerPrescriptionsViewModelTests.cs` | 5 | **17** | **+12** |
| `ViewModels/P2D5CustomerReadViewModelTests.cs` | 8 | 8 | 0 *(adapté : nouveau paramètre)* |
| `ViewModels/SaleFormViewModelTransactionTests.cs` | — | — | 0 *(adapté : nouveau paramètre)* |
| **Total App** | **211** | **239** | **+28** |

**`PrescriptionFormViewModel`** — prouvé : les options exposées **sont exactement** les 4 valeurs de l'enum réel (+
l'option vide), vérifié **contre `Enum.GetValues<PrismBase>()`** et non contre une liste recopiée · option vide ⇒ `null` ·
`In`/`Out`/`Up`/`Down` envoyés comme `PrismBase.*` (`[Theory]` sur les 4 valeurs, **OD et OG**) · **aucune valeur H/V** ·
OD et OG **indépendants** · `DoctorName` `null`/`""`/`"   "` **n'empêche pas** la sauvegarde (`[Theory]`) · la propriété
`DoctorNameError` **n'existe plus** (réflexion) · résultat invalide ⇒ erreurs affichées, formulaire **non fermé**,
valeurs **conservées**, succès **non émis** · plusieurs erreurs ⇒ toutes affichées, **sans doublon**, **dans l'ordre** ·
**aucune règle locale** ne rejette une commande acceptée par le use case · `CustomerFound = false` ⇒ message clair, pas
de fermeture, pas de nettoyage, pas de succès, **aucun détail technique** · `BusinessRuleException` ⇒ **message métier
exact** (constante du use case), « Réactivez-le » présent, **pas** de type d'exception, **modal** affiché, valeurs
conservées · erreur inattendue ⇒ message générique **distinct** et **aucun modal** · `PrescriptionFound = false` ⇒
message clair, **pas de fausse réussite** · update invalide ⇒ formulaire conservé, pas de succès · **update autorisé
pour un client archivé** · **axe 0 envoyé sans normalisation UI** · gardes de constructeur (dont `IDialogService`).

**`CustomerPrescriptionsViewModel`** — prouvé : suppression **confirmée** ⇒ use case appelé **avec le bon
`PrescriptionId`** · `IDialogService` reçoit **le titre et le message attendus** (constantes) · suppression **annulée**
⇒ **aucun** appel, **aucun** rechargement, **aucune** mutation locale, aucun message · succès ⇒ **rechargement depuis la
source** · introuvable ⇒ message clair **+ rechargement** · erreur inattendue ⇒ message générique distinct · **aucune
suppression locale trompeuse** (la liste reflète la source, pas l'intention) · **après création réussie** ⇒
rechargement · **après modification réussie** ⇒ rechargement · **aucune dépendance de persistance** (réflexion) · gardes
de constructeur (dont `IDialogService`).

Spies/stubs **explicites** (`SpyCreate/Update/DeletePrescriptionUseCase`, `StubDialogService`) plutôt qu'assertions
fragiles sur des détails internes.

### Total

| | Avant | Après |
|---|---|---|
| **App** | 211 | **239** |
| **Application** | 218 | **218** *(inchangé)* |
| **Domain** | 294 | **294** *(inchangé)* |
| **TOTAL** | **723** | **751** |

**+28 tests. 0 échec, 0 ignoré.**

---

## 18. Contrôle AXAML et interaction

| Contrôle | Résultat |
|---|---|
| Le `ComboBox` `PrismBase` compile | ✅ (les **liaisons compilées** Avalonia — `x:DataType` — sont validées **au build** : une propriété inexistante casserait la compilation, ce qui a effectivement été observé et corrigé pendant la refonte) |
| Bindings corrects | ✅ `ItemsSource` (statique), `SelectedItem`, `ItemTemplate` typé `vm:PrismBaseOption` |
| Options nullables affichées | ✅ « Aucune » = `null` |
| Deux yeux correctement liés | ✅ `SelectedOdPrismBaseOption` / `SelectedOgPrismBaseOption` |
| Contrôle orphelin lié à `PrismBase` | ✅ **aucun** (`grep` : plus aucun `TextBox` lié à `Od/OgPrismBase`) |
| `TextBox` libre disparu | ✅ |
| Watermark « H/V/In/Out » disparu | ✅ (`grep` sur `src/` : **0 occurrence**) |
| Résumé des erreurs visible | ✅ pied de formulaire, toujours visible (hors zone scrollable) |
| Boutons de suppression accessibles | ✅ **inchangés** (fiche détail — `PrescriptionDetailView`, non modifiée) |
| Clic « supprimer » sans action de ligne parasite | ✅ **sans objet** : il n'existe **aucun** bouton de suppression **dans la ligne** ; la suppression part de la fiche détail. Le `PointerPressed` de ligne reste **inchangé**. |

**Aucune refonte graphique.** Le style `ComboBox.OpticSelect` reprend simplement les métriques de `TextBox.OpticInput`.

---

## 19. Contrôle visuel

⚠️ **CONTRÔLE VISUEL NON EXÉCUTÉ.** L'environnement d'exécution (agent non interactif, sans session graphique
observable) ne permet pas de lancer et d'**observer** réellement `dotnet run --project src/MMV.App`. **Aucune
prétention n'est faite** sur un test manuel qui n'a pas eu lieu.

**Ce qui est néanmoins garanti automatiquement :** les liaisons AXAML de cet écran sont **compilées** (`x:DataType` +
`x:CompileBindings`), donc une propriété ou un type absent **casserait le build** — ce qui n'est pas le cas.

**Checklist à exécuter manuellement avant merge :**

1. création sans nom de médecin ;
2. création d'une ordonnance partielle ;
3. cylindre non nul **sans** axe (⇒ refus lisible) ;
4. axe **sans** cylindre (⇒ refus lisible) ;
5. axe **0** avec cylindre (⇒ accepté, réaffiché à **180** après rechargement) ;
6. prisme avec **chacune** des quatre bases ;
7. prisme **sans** base (⇒ refus lisible) ;
8. base **sans** prisme (⇒ refus lisible) ;
9. client **archivé** pendant qu'un formulaire est ouvert (⇒ message métier + modal, formulaire conservé) ;
10. suppression **annulée** (⇒ rien ne bouge) ;
11. suppression **confirmée** (⇒ liste rechargée) ;
12. formulaire **conservé** après erreur (valeurs saisies intactes).

---

## 20. Domain / Application / Infrastructure / migrations / dépendances

| Contrôle | Résultat |
|---|---|
| `git diff -- src/MMV.Domain` | ✅ **vide** |
| `git diff -- src/MMV.Application` | ✅ **vide** |
| `git diff -- src/MMV.Infrastructure` | ✅ **vide** |
| `git diff -- src/MMV.Infrastructure/Migrations` | ✅ **vide** |
| `git diff -- .github` | ✅ **vide** |
| `git diff -- "*.csproj" "*.sln"` | ✅ **vide** |
| `ef migrations has-pending-model-changes` | ✅ aucune migration en attente |
| Packages ajoutés / retirés / mis à jour | ✅ **aucun** (`FluentAssertions` reste en **6.x**) |
| `MMV.Application` pure | ✅ `MMV.Domain` seule en référence projet |
| Vulnérabilités | ✅ **0** (7 projets) |

**Aucun défaut de conception de P3-3B n'a exigé de modification Application ou Domain.** Les contrats livrés
(`ValidationErrors` / `IsValid`, `CustomerFound`, `PrescriptionFound`, `BusinessRuleException` + constante publique) se
sont révélés **suffisants et directement consommables** par l'UI. **Aucun `NO-GO conception`.**

---

## 21. Risques résiduels

1. 🟠 **Aucun token de concurrence** : deux postes corrigeant **des yeux différents** de la même ordonnance ⇒ la première
   correction est **écrasée en silence**. **Inchangé par P3-3C** (chantier **transverse**, ADR-PROD-DB-001).
2. 🟠 **Immutabilité forte de `Prescription` : toujours NON implémentée** (ni versionnement, ni verrouillage après usage,
   ni FK `SaleItem.PrescriptionId`). La suppression **physique** existe toujours — désormais **confirmée**, mais toujours
   **irréversible**. Dette **documentée**, non résolue.
3. 🟠 **Fenêtre de course « archivage pendant création »** (P3-3B §22) : un client peut être archivé entre le contrôle et
   l'écriture. L'UI **ne peut pas** la fermer. Assumée, sans perte de donnée.
4. 🟡 **Les erreurs de validation ne sont plus rattachées au champ fautif** : la suppression des 9 validations locales
   fait perdre le liseré rouge **par champ** au profit d'un **résumé** en pied de formulaire. C'est le prix de la
   **source de vérité unique** (§8). Les messages Domain nomment la propriété fautive (`ValidationError.PropertyName`
   est disponible mais **non consommé** ici : le mapping propriété ⇒ contrôle serait le « framework général de validation
   UI » explicitement interdit). **Réévaluable après pilote terrain.**
5. 🟡 **`LoadPrescription` lève sur une `IssueDate` invalide** (`DateTime.MinValue` ⇒ `DateTimeOffset` hors bornes) —
   fragilité **préexistante**, révélée par l'écriture des tests, **non corrigée ici** (hors périmètre : aucune donnée
   réelle ne porte `MinValue`, la colonne étant toujours renseignée).
6. 🟡 **Prisme perdu** au pré-remplissage d'une commande (`OrderFormViewModel.AutoFillFromPrescription()`) — bug
   **préexistant**, hors périmètre ⇒ **P3-6**.
7. 🟡 **Refus de vente pour client archivé** toujours **non implémenté** ⇒ **P3-7**.
8. 🟡 **Ordonnance totalement vide** toujours persistable — iso-comportement délibéré (P3-3B §9).

---

## 22. Fichiers modifiés / créés

**Modifiés — `src/` (5)**
- `src/MMV.App/ViewModels/PrescriptionFormViewModel.cs` — `PrismBaseOption` + options, `IDialogService`, suppression des
  9 validations locales et des 9 `*Error`, affichage des `ValidationErrors`, `CustomerFound`, `PrescriptionFound`,
  capture de `BusinessRuleException` ;
- `src/MMV.App/ViewModels/CustomerPrescriptionsViewModel.cs` — `IDialogService`, **confirmation** avant suppression,
  rechargement depuis la source sur « introuvable », constantes de messages ;
- `src/MMV.App/Views/Clients/PrescriptionFormView.axaml` — `ComboBox` `PrismBase` (OD/OG), style `OpticSelect`, libellé
  médecin **facultatif**, résumé de validation, retrait des 8 blocs d'erreur par champ et du bloc `DoctorNameError` ;
- `src/MMV.App/ViewModels/CustomerDetailViewModel.cs` — **propagation** d'`IDialogService` (§17) ;
- `src/MMV.App/ViewModels/CustomersViewModel.cs` — **propagation** d'`IDialogService` (§17).

**Modifiés — `tests/` (4)**
- `tests/MMV.App.Tests/ViewModels/PrescriptionFormViewModelTests.cs` — 6 → **26** ;
- `tests/MMV.App.Tests/ViewModels/CustomerPrescriptionsViewModelTests.cs` — 5 → **17** ;
- `tests/MMV.App.Tests/ViewModels/P2D5CustomerReadViewModelTests.cs` — adapté (nouveau paramètre) ;
- `tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs` — adapté (nouveau paramètre).

**Créés (1)**
- `docs/implementation/P3-3C-prescription-ui-validation-report.md` — **ce rapport**.

**Mis à jour (1)**
- `docs/architecture/P3-business-rules-roadmap.md`.

**Intacts** : `src/MMV.Domain/**` · `src/MMV.Application/**` · `src/MMV.Infrastructure/**` · `.github/**` · `*.csproj` ·
`*.sln` · `App.axaml.cs` · `PrescriptionDetailView(Model)` · `CustomerPrescriptionsView.axaml(.cs)`.

---

## 23. Contrôles exécutés

`git branch --show-current` · `git status --short` · `git log -10 --oneline` · `git rev-parse HEAD` · `git diff --stat` ·
`git diff --check` · `gh run view 29340721869 --json databaseId,url,headSha,headBranch,status,conclusion,event` ·
`dotnet restore MMV.sln` · `dotnet build MMV.sln --no-restore -c Debug` · `dotnet test MMV.sln --no-build -c Debug` ·
`dotnet list MMV.sln package --vulnerable --include-transitive` · `dotnet tool restore` ·
`dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` ·
`dotnet list src/MMV.Application/MMV.Application.csproj reference` / `… package` ·
`rg -n "PrescriptionFormViewModel|CustomerPrescriptionsViewModel|PrismBase|DoctorName|ValidationErrors|BusinessRuleException|ShowConfirmationAsync|ShowErrorAsync|CreatePrescriptionResult|UpdatePrescriptionResult|CustomerFound" src/MMV.App tests/MMV.App.Tests` ·
`rg -n "H/V/In/Out|ValidateDoctorName|DoctorNameError"` (⇒ **0** dans `src/`) ·
`git diff -- src/MMV.Domain` · `… src/MMV.Application` · `… src/MMV.Infrastructure` · `… src/MMV.Infrastructure/Migrations` ·
`… .github` · `… "*.csproj" "*.sln"`.

---

## 24. Résultats

| Contrôle | Attendu | Résultat |
|---|---|---|
| Branche | `p3-business-rules` | ✅ |
| CI du commit de départ | verte sur le SHA exact | ✅ `29340721869` — `success` sur `982b349a…` |
| Build | vert | ✅ **0 avertissement, 0 erreur** |
| Tests | **> 723** | ✅ **751** — App **239** (> 211) · Application **218** · Domain **294** — **0 échec** |
| Vulnérabilités | 0 | ✅ **0** (7 projets) |
| Migration en attente | aucune | ✅ |
| `MMV.Application` pure | Domain seul | ✅ |
| Domain / Application / Infrastructure | intacts | ✅ **diffs vides** |
| CI | intacte | ✅ **diff vide** |
| Package ajouté | aucun | ✅ |
| `git diff --check` | propre | ✅ |
| Commit / push | aucun | ✅ |

---

## 25. Verdict

### **P3-3C = GO local.**
### **P3-3 = TERMINÉ localement** (sous réserve de CI distante).

- ✅ **`PrismBase` n'est plus un `TextBox` libre** — `ComboBox` sur l'enum Domain réel ;
- ✅ **seules `In` / `Out` / `Up` / `Down` sont sélectionnables** ; **aucune valeur `H`/`V`** ; **watermark erroné supprimé** ;
- ✅ **`null` reste sélectionnable** (« Aucune ») ;
- ✅ **`DoctorName` est facultatif dans l'UI** — la règle inventée par l'écran a disparu ;
- ✅ **`ValidationErrors` affichées proprement** (ordre conservé, dédupliquées, une par ligne, sans détail technique) ;
- ✅ **`BusinessRuleException` traitée spécifiquement** — message métier **exact**, modal incontournable, `catch`
  générique **séparé** ;
- ✅ **`CustomerFound = false` traité explicitement** ;
- ✅ **`PrescriptionFound = false` traité explicitement** ;
- ✅ **suppression confirmée** — le `// TODO` de P3-3B est levé ;
- ✅ **annulation ⇒ aucun appel, aucun rechargement, aucune mutation** ;
- ✅ **toute mutation est suivie d'un rechargement depuis la source** (multi-poste) ;
- ✅ **aucune persistance directe dans l'UI** (garde-fous par réflexion verts) ;
- ✅ **aucune duplication de règle métier** dans la ViewModel ;
- ✅ **aucune modification Domain / Application / Infrastructure**, **aucune migration**, **aucune dépendance ajoutée** ;
- ✅ **751 tests verts**, rapport complet.

⚠️ **Réserve honnête : le contrôle visuel manuel n'a PAS été exécuté** (§19). La checklist est fournie.

**Aucun commit, aucun push.**

---

## 26. Prochaine étape — P3-4A (Produits, audit)

P3-3 (Ordonnances) est **terminé localement** : audit (**P3-3A**), métier (**P3-3B**), UI (**P3-3C**).

**Reste explicitement hors P3-3, inchangé :**

- **transposition** sphère/cylindre/axe ⇒ **P3-6B** ;
- **concurrence** (token / immutabilité forte de `Prescription`) ⇒ chantier **transverse**, ADR-PROD-DB-001 ;
- **prisme perdu** au pré-remplissage des commandes ⇒ **P3-6** ;
- **refus de vente** pour client archivé ⇒ **P3-7**.

**P3-4A** — audit du domaine **Produits** : unicité de `Reference` (documentée « unique », **non vérifiée**), cohérence
`Category` ↔ détail associé (`GlassDetail` / `LensDetail` / `AccessoryDetail`), et suppression d'un produit **utilisé**
en vente/commande (aujourd'hui **cascade EF**) ⇒ désactivation (`IsActive` existe déjà) plutôt que suppression dure.
