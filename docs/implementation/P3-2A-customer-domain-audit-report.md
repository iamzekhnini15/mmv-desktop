# P3-2A — Audit métier **Clients** avant sécurisation suppression / archivage

> **Mode : `AUDIT_ONLY`.** Ce document **n'implémente rien**. Aucun fichier source, test, migration, package
> ou workflow CI n'a été modifié. Seul ce rapport est créé. **Le dépôt réel prime toujours sur ce document.**
>
> Branche : `p3-business-rules`. Date : 2026-07-13. Étape précédente : **P3-1** (validation / `Result`, domaine
> pilote Clients). Étape cadrée par la [roadmap P3 §P3-2](../architecture/P3-business-rules-roadmap.md) et
> l'[ADR-PROD-DB-001 multi-poste](../architecture/adr-prod-db-001-multi-poste-database-strategy.md).

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-2A |
| `EXECUTION_MODE` | `AUDIT_ONLY` |
| `SOURCE_BRANCH` | `p3-business-rules` |
| `ALLOW_CODE_CHANGES` | `false` |
| `ALLOW_TEST_CHANGES` | `false` |
| `ALLOW_DOC_CHANGES` | `true` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

Objectif : déterminer exactement comment un client est créé/modifié/lu/supprimé aujourd'hui, quelles données
historiques dépendent de `Customer`, quelles cascades EF existent **réellement**, si une suppression client peut
effacer un historique métier, et quelle stratégie de sécurisation (suppression conditionnelle / refus /
désactivation / archivage / soft delete) est la plus sûre et compatible multi-poste — **sans rien implémenter**.

---

## 2. État Git initial

```
$ git branch --show-current
p3-business-rules

$ git status --short
(vide — working tree propre)

$ git log -5 --oneline
9d2a865 docs(P3-1): record validation result CI validation
6e39bd2 feat(P3-1): standardize application validation result pilot
870c81a docs(P3-0B): record multi-workstation DB strategy CI validation
be65134 docs(P3-0B): define multi-workstation database strategy
9413cfa docs(P3-0): record business audit CI validation
```

- Branche = `p3-business-rules` ✔
- Working tree **propre** ✔
- P3-1 applicatif (`feat(P3-1)`) et documentaire (`docs(P3-1)`) présents ✔
- CI P3-1 documentée verte (`docs(P3-1): record validation result CI validation`) ✔

**Précondition Git : GO.**

---

## 3. Baseline (exécutée avant tout audit)

| Contrôle | Résultat |
|---|---|
| `dotnet restore MMV.sln` | OK — tous projets à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | **592 tests** verts (Domain 223 + Application 177 + App 192), 0 échec, 0 ignoré |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** (tous projets) |
| `dotnet tool restore` | OK — `dotnet-ef 8.0.27` restauré |
| `dotnet ef migrations has-pending-model-changes` | **`No changes … since the last migration`** (= false) |
| `dotnet list src/MMV.Application … reference` | **uniquement** `MMV.Domain` |
| `dotnet list src/MMV.Application … package` | **uniquement** `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` |

**Baseline : GO** (build vert, tests ≥ 592, 0 vulnérabilité, aucune migration en attente, `MMV.Application` pure).

---

## 4. Inventaire de l'entité `Customer`

Source : [`src/MMV.Domain/Entities/Customer.cs`](../../src/MMV.Domain/Entities/Customer.cs).

| Propriété | Type | Nullable | Défaut | Remarque |
|---|---|---|---|---|
| `CustomerId` | `long` | non | — | PK, auto-incrément |
| `FirstName` | `string` | non | `string.Empty` | requis (validateur + EF `IsRequired`, max 100) |
| `LastName` | `string` | non | `string.Empty` | requis (validateur + EF `IsRequired`, max 100) |
| `BirthDate` | `DateTime?` | oui | `null` | — |
| `Phone` | `string?` | oui | `null` | max 20 |
| `Email` | `string?` | oui | `null` | validé si renseigné, max 254 (EF) / 200 (validateur) |
| `Address` | `string?` | oui | `null` | max 500 |
| `City` | `string?` | oui | `null` | max 100 |
| `PostalCode` | `string?` | oui | `null` | max 10 |
| `SocialSecurityNumber` | `string?` | oui | `null` | max 50 (EF) / 30 (validateur) |
| `InsuranceName` | `string?` | oui | `null` | max 200 |
| `Notes` | `string?` | oui | `null` | max 2000 |
| `CreatedAt` | `DateTime` | non | `DateTime.UtcNow` | géré par l'application |
| `UpdatedAt` | `DateTime` | non | `DateTime.UtcNow` | géré par l'application |
| `Prescriptions` | `ICollection<Prescription>` | — | liste vide | navigation |
| `Sales` | `ICollection<Sale>` | — | liste vide | navigation |

**Aucune méthode métier** (entité anémique — POCO pur). Validations actuelles
([`CustomerValidator.cs`](../../src/MMV.Domain/Validators/CustomerValidator.cs)) : `FirstName`/`LastName` requis
+ longueurs ; `Email` format si renseigné ; longueurs `Phone`/`SocialSecurityNumber`. **Règles absentes** :
aucune règle d'unicité, aucune règle de suppression, aucune notion d'état (actif/archivé).

### Réponses explicites (§5 du prompt)

1. **Archivage déjà présent ?** — **NON.** Aucun champ `IsArchived` / `ArchivedAt`.
2. **Désactivation déjà présente ?** — **NON.** Aucun champ `IsActive` (contrairement à `Product`, qui en possède un).
3. **Soft delete déjà présent ?** — **NON.** Aucun `DeletedAt`, aucun `HasQueryFilter` sur `Customer`.
4. **Token de concurrence ?** — **NON.** Aucun `RowVersion` / `[ConcurrencyToken]` / `IsRowVersion` (confirmé :
   0 occurrence dans le modèle, cf. ADR-PROD-DB-001 §2.2).
5. **Migration nécessaire pour ajouter un archivage ?** — **OUI.** Ajouter `IsActive`/`IsArchived` (ou `DeletedAt`)
   modifie le schéma ⇒ nouvelle migration EF + backfill des clients existants (cf. §13).

---

## 5. Inventaire Application — Clients

Dossier : [`src/MMV.Application/UseCases/Customers/`](../../src/MMV.Application/UseCases/Customers/).

| Use case | Entrée | Sortie | Validation | Repository | SaveChanges / Txn | Horodatage | Effets de bord |
|---|---|---|---|---|---|---|---|
| **CreateCustomer** | `CreateCustomerCommand` | `CreateCustomerResult` (`CustomerId`, `DisplayName`, `ValidationErrors`) | P3-1 : `CustomerValidator` **avant** écriture | `ICustomerRepository.CreateAsync` | 1× `SaveChangesAsync`, **pas** de `ITransactionRunner` (mono-écriture) | `CreatedAt`=`UpdatedAt`=`UtcNow` | normalisation blancs→`null` |
| **UpdateCustomer** | `UpdateCustomerCommand` | `UpdateCustomerResult` (`CustomerFound`, `ValidationErrors`, `DisplayName`) | P3-1 : `CustomerValidator` **avant** écriture | `GetByIdAsync` puis `UpdateAsync` | 1× `SaveChangesAsync`, pas de runner | `CreatedAt` préservé, `UpdatedAt`=`UtcNow` | `CustomerFound=false` si absent, sans écrire ; normalisation |
| **DeleteCustomer** | `DeleteCustomerCommand` (`CustomerId`) | `DeleteCustomerResult` (`CustomerFound`, `CustomerId`) | **aucune** (pas de garde-fou métier) | `GetByIdAsync` puis `DeleteAsync(entity)` | 1× `SaveChangesAsync`, pas de runner | — | **cascade EF** (cf. §7) |
| **ListCustomers** | `ListCustomersQuery` | `IReadOnlyList<CustomerListItemDto>` | — | `GetAllAsync` (AsNoTracking) | lecture | — | projection plate, **aucun filtre** actif/archivé |
| **ListCustomersForPicker** | `ListCustomersForPickerQuery` | `IReadOnlyList<CustomerPickerItemDto>` | — | `GetAllAsync` | lecture | — | tri `OrderBy(LastName)`, **aucun filtre** actif/archivé |

### DeleteCustomerUseCase — réponses précises (§6 du prompt)

Source : [`DeleteCustomerUseCase.cs`](../../src/MMV.Application/UseCases/Customers/DeleteCustomer/DeleteCustomerUseCase.cs).

- **Charge-t-il le client avant suppression ?** — **OUI** : `GetByIdAsync(command.CustomerId)`. Si `null` ⇒
  `CustomerFound=false` sans écrire.
- **`DeleteAsync(id)` ou `DeleteAsync(entity)` ?** — **`DeleteAsync(entity)`** (l'entité chargée est passée).
- **Vérifie-t-il l'historique ?** — **NON.** Aucun contrôle prescriptions / ventes / commandes.
- **Vérifie-t-il prescriptions / ventes / commandes ?** — **NON.**
- **Utilise-t-il `ITransactionRunner` ?** — **NON** (documenté volontaire : mono-écriture atomique, cohérent
  avec `DeleteOrderUseCase`).
- **Appelle-t-il `SaveChangesAsync` ?** — **OUI** (1×, via `IUnitOfWork`).
- **Retourne-t-il `CustomerFound` ?** — **OUI** (`true` si supprimé, `false` si introuvable).
- **Peut-il provoquer une cascade EF ?** — **OUI, et c'est le point dur** (cf. §7/§8). Le use case ne fait
  qu'`_dbSet.Remove(entity)` ; c'est EF Core / le schéma SQL qui décide des cascades.
- **Exceptions possibles** : `ArgumentNullException` (commande nulle) ; toute `DbUpdateException` /
  `PersistenceException` remontée par `SaveChangesAsync`. Le use case **n'attrape rien** : il propage.

---

## 6. Inventaire Repository `Customer`

Sources : [`ICustomerRepository.cs`](../../src/MMV.Domain/Interfaces/Repositories/ICustomerRepository.cs),
[`IGenericRepository.cs`](../../src/MMV.Domain/Interfaces/Repositories/IGenericRepository.cs),
[`CustomerRepository.cs`](../../src/MMV.Infrastructure/Repositories/CustomerRepository.cs),
[`BaseRepository.cs`](../../src/MMV.Infrastructure/Repositories/BaseRepository.cs).

- **Méthodes spécifiques Customer** : `SearchByNameAsync`, `GetByPhoneAsync`, `GetByEmailAsync`,
  `GetWithPrescriptionsAsync` (Include `Prescriptions`), `GetWithHistoryAsync` (Include `Prescriptions` + `Sales`),
  `GetByDateRangeAsync`. **Aucune** ne filtre par état actif/archivé (inexistant).
- **`GetByIdAsync`** (générique) : `_dbSet.FindAsync` — **suivi** (tracking), pas d'`Include`.
- **`GetAllAsync`** (générique) : `_dbSet.AsNoTracking().ToListAsync()` — **sans tracking**, sans `Include`,
  **sans filtre**. C'est la source des deux query use cases (liste + picker).
- **Lectures custom** (`GetQueryable()`) : `AsNoTracking()`.
- **`DeleteAsync(entity)`** : `_dbSet.Remove(entity)` (marque `Deleted`) — **suppression physique**, aucune
  vérification, aucun soft-delete. `DeleteAsync(id)` charge puis `Remove`.
- **Requêtes d'existence** : `ExistsAsync(id)` (générique, `FindAsync != null`). **Aucune** requête « le client
  a-t-il un historique ? » côté `CustomerRepository`.
- **Support filtrage `IsActive`/`IsArchived`** : **ABSENT** (les champs n'existent pas).
- **Multi-poste / concurrence** : `GetByIdAsync` suit l'entité mais aucune stratégie de jeton ; la suppression
  est une lecture-puis-écriture **non atomique** (cf. §14).

> **Réutilisable pour un futur garde-fou d'historique** : il existe déjà
> [`IPrescriptionRepository.GetByCustomerIdAsync`](../../src/MMV.Domain/Interfaces/Repositories/IPrescriptionRepository.cs)
> et [`ISaleRepository.GetByCustomerIdAsync`](../../src/MMV.Domain/Interfaces/Repositories/ISaleRepository.cs) —
> un contrôle « a un historique » pourrait s'appuyer dessus **sans** nouvelle méthode de repository (idéalement
> via un `AnyByCustomerIdAsync` plus léger, à ajouter en P3-2).

---

## 7. Relations EF et cascades autour de `Customer`

Sources : configurations Fluent
([`CustomerConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs),
[`PrescriptionConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/PrescriptionConfiguration.cs),
[`SaleConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs)),
snapshot de modèle et **SQL réel des migrations**
([`InitialCreate`](../../src/MMV.Infrastructure/Migrations/20260127184542_InitialCreate.cs),
[`RestoreSaleOrderSeparation`](../../src/MMV.Infrastructure/Migrations/20260212220902_RestoreSaleOrderSeparation.cs)).

### Tableau des dépendances directes de `Customer`

| Entité dépendante | FK `CustomerId` | Nullable ? | Navigation | `DeleteBehavior` explicite | Comportement SQL **réel** confirmé | Historique métier ? |
|---|---|---|---|---|---|---|
| **Prescription** | `long` | **non** (requis) | `Customer.Prescriptions` ↔ `Prescription.Customer` | **`Cascade`** | `FK_Prescriptions_Customers_CustomerId … onDelete: Cascade` (InitialCreate:124) | **OUI** — ordonnances (donnée optique/médicale) |
| **Sale** | `long?` | **oui** | `Customer.Sales` ↔ `Sale.Customer` | **`SetNull`** | `FK_Sales_Customers_CustomerId … onDelete: SetNull` (InitialCreate:219) | **OUI** — ventes (historique commercial) |
| ~~Order~~ | — | — | — | — | **Plus de FK directe** : `FK_Orders_Customers_CustomerId` **supprimée** par `RestoreSaleOrderSeparation` (Up:14-32). `Order` est désormais lié à `Sale` (`FK_Orders_Sales_SaleId … Cascade`). | indirect (via `Sale`) |
| Notification | — | — | — | — | `Notification.EntityId` est un `long?` **générique non contraint** (ProductId/OrderId/…), **aucune FK** vers `Customer`. | non lié en FK |

### Chaînes de cascade au-delà de `Sale` (déclenchées seulement si une **Sale** est supprimée)

`Sale` → `SaleItems` (`Cascade`), `Sale` → `Orders` (`Cascade`) → `OrderItems` (`Cascade`).
**Important** : ces cascades **ne se déclenchent PAS** lors d'une suppression de `Customer`, car le lien
`Customer → Sale` est **`SetNull`** (la vente survit, orpheline). Elles ne se déclenchent que si l'on supprime
directement une `Sale`.

### Réponses explicites (§8 du prompt)

1. **Quelles lignes sont supprimées si `Customer` est supprimé ?** — **Toutes ses `Prescriptions`** (cascade
   physique). Les `Sales` **ne sont pas supprimées**.
2. **Quelles FK bloquent la suppression ?** — **AUCUNE.** Il n'existe aucun `Restrict`/`NoAction` vers `Customer`.
   La suppression réussit **toujours** au niveau base.
3. **Quelles relations sont en cascade ?** — `Customer → Prescription` (**Cascade**). C'est la seule cascade
   destructrice directe.
4. **Quelles relations sont nullable ?** — `Sale.CustomerId` (**nullable**, `SetNull`).
5. **Une suppression client peut-elle effacer :**
   - ordonnances → **OUI** (cascade physique — **perte de données médicales/optiques**) ;
   - ventes → **NON** (conservées, mais `CustomerId` mis à `NULL` — **anonymisées**, lien client perdu) ;
   - commandes → **NON** directement (plus de FK `Order → Customer`) ; les `Order` restent rattachées à leur `Sale` ;
   - articles de vente (`SaleItem`) → **NON** (la vente survit) ;
   - articles de commande (`OrderItem`) → **NON** ;
   - fiches atelier futures (P3-6B) → **à concevoir cascade-safe** (n'existent pas encore) ;
   - notifications → **NON** (pas de FK).
6. **La base actuelle protège-t-elle l'historique métier ?** — **NON.** Les ordonnances sont **détruites**
   silencieusement (cascade) et les ventes sont **anonymisées** (SetNull). Aucun garde-fou.

> **Correction factuelle vs roadmap.** La [roadmap P3-2](../architecture/P3-business-rules-roadmap.md) écrit que
> « la suppression d'un client peut effacer son **historique de ventes** ». Le dépôt réel montre l'inverse de
> nuance : ce sont les **ordonnances** qui sont **cascade-supprimées** ; les **ventes** sont **conservées mais
> anonymisées** (`SetNull`). Le risque de **perte pure** porte donc d'abord sur les **ordonnances**. À corriger
> dans la formulation de la roadmap le moment venu (autorisé §17).

---

## 8. Comportement réel **actuel** de la suppression client

Enchaînement effectif, du clic au SQL :

1. UI ([`CustomersListViewModel.ExecuteDelete`](../../src/MMV.App/ViewModels/CustomersListViewModel.cs#L249)) :
   **aucune boîte de confirmation**. Le bouton *Supprimer* (activable dès qu'un client est sélectionné) appelle
   directement le use case.
2. `DeleteCustomerUseCase` : `GetByIdAsync` → si trouvé, `Remove(entity)` + `SaveChangesAsync`.
3. EF Core / SQLite exécute : `DELETE FROM Customers WHERE CustomerId = @id`, avec **`ON DELETE CASCADE`** sur
   `Prescriptions` (⇒ suppression physique de toutes les ordonnances du client) et **`ON DELETE SET NULL`** sur
   `Sales` (⇒ `UPDATE Sales SET CustomerId = NULL …`).
4. La VM retire le client de la liste locale et vide la sélection. Aucun avertissement sur la perte d'historique.

**Verdict comportemental : la suppression est physique, silencieuse, non confirmée, non gardée, et détruit les
ordonnances tout en anonymisant les ventes.** C'est précisément le « point dur » annoncé par la roadmap.

---

## 9. Audit UI — Clients

ViewModels existants : `CustomersViewModel`, `CustomersListViewModel`, `CustomerFormViewModel`,
`CustomerDetailViewModel`, `CustomerInfoViewModel`, `CustomerPrescriptionsViewModel`,
`CustomerPurchaseHistoryViewModel`. Vues : [`src/MMV.App/Views/Clients/`](../../src/MMV.App/Views/Clients/).

> Note : les VM `CustomersListViewModel.cs` et `CustomersViewModel.cs` contiennent quelques caractères parasites
> `\` en tête de commentaire (lignes de doc). C'est cosmétique, hors périmètre P3-2A, **non modifié**.

- **Où `DeleteCustomerUseCase` est appelé** : `CustomersListViewModel.ExecuteDelete()` (async void), via
  `DeleteCommand` (`RelayCommand(ExecuteDelete, CanEditOrDelete)`).
- **Confirmation utilisateur** : **ABSENTE.** Confirmé par le test VM et son commentaire : « le flux d'origine
  ne comporte aucun dialogue de confirmation ». Suppression immédiate.
- **Message succès/erreur** : pas de message de succès ; en cas d'introuvable → `ErrorMessage` = « Le client à
  supprimer est introuvable. » ; exception → `ErrorMessage` = « Erreur lors de la suppression du client : … ».
- **Retrait de la liste locale** : `Customers.Remove(customerToDelete)` + `ApplyFilter()` + `SelectedCustomer=null`.
- **Rafraîchissement** : pas de rechargement serveur après suppression (retrait local uniquement).
- **Comportement si suppression refusée** : **inexistant aujourd'hui** (rien ne refuse). Un futur refus métier
  (exception ou `Result`) tomberait dans le `catch` / nécessiterait un nouveau champ de `Result` + message dédié.
- **Bouton supprimer** : présent, actif dès qu'un client est sélectionné et hors chargement.
- **Masquer les clients inactifs** : **impossible aujourd'hui** (pas d'état, pas de filtre ; `ListCustomers` et
  le picker chargent **tout**).
- **Dépendances DTO P2D** : la liste consomme `CustomerListItemDto` (P2D-7C), le picker `CustomerPickerItemDto`
  (P2D-6). Un archivage ajouterait un champ d'état à projeter (ou un filtrage en amont).
- **Impacts UI probables d'un archivage** : bouton « Archiver » (vs « Supprimer »), filtre « afficher archivés »,
  action de réactivation, exclusion par défaut dans liste + picker, message expliquant le refus de suppression
  avec historique.

**Aucun fichier UI modifié.**

---

## 10. Audit des tests — Clients

Fichiers pertinents :
[`DeleteCustomerUseCaseTests`](../../tests/MMV.Application.Tests/UseCases/Customers/DeleteCustomerUseCaseTests.cs),
`CreateCustomerUseCaseTests`, `UpdateCustomerUseCaseTests`, `ListCustomersUseCaseTests`,
`ListCustomersForPickerUseCaseTests` (Application) ;
[`CustomersListViewModelTests`](../../tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs),
`CustomerFormViewModelTests` (App) ; `CustomerServiceTests`, `DbContextTests` (Domain).

### Cas **déjà couverts** (suppression)

- **Application** (`DeleteCustomerUseCaseTests`, vrai SQLite temporaire) : suppression d'un client **sans
  historique** persiste (`CustomerFound=true`, table vide) ; client introuvable (`CustomerFound=false`, aucune
  écriture) ; commande nulle → `ArgumentNullException` ; constructeur rejette `null`.
- **App/VM** (`CustomersListViewModelTests`) : délégation au use case + retrait de liste ; mapping état→commande ;
  introuvable → message d'erreur ; **aucun** test de confirmation (car aucun dialogue n'existe).

### Cas **manquants** (à couvrir en P3-2)

- ❌ suppression **avec prescriptions** (aujourd'hui cascade → aucune preuve du comportement destructeur) ;
- ❌ suppression **avec ventes** (SetNull → aucune preuve de l'anonymisation) ;
- ❌ suppression **avec commandes** (via ventes) ;
- ❌ **cascade** explicitement testée (aucun test ne prouve Cascade/SetNull sur `Customer`) ;
- ❌ **refus** de suppression si historique (n'existe pas encore) ;
- ❌ **archivage / réactivation** (n'existe pas) ;
- ❌ **exclusion** des archivés dans liste + picker ;
- ❌ **atomicité / concurrence** multi-poste (aucun test de course) ;
- ❌ **retrait de liste UI** après refus (chemin inexistant).

`DbContextTests` **ne teste aucune** cascade `Customer` (0 occurrence Cascade/SetNull/Prescription/Sale).

---

## 11. Comparaison des stratégies (au regard du dépôt réel)

### Option A — Suppression physique toujours autorisée (statu quo)

- **Historique** : ordonnances **détruites**, ventes anonymisées. Perte réelle.
- **Intégrité référentielle** : « propre » au sens FK (cascade/setnull) mais **détruit du métier**.
- **Audit / multi-poste** : dangereux (destruction concurrente possible, aucune traçabilité).
- **Pertinence métier** : **faible** — un opticien ne doit pas perdre les ordonnances d'un client.
- **Verdict** : **à écarter.** C'est le comportement à corriger.

### Option B — Suppression physique **uniquement sans historique**, sinon refus

- **Comportement** : client sans prescription **ni** vente ⇒ suppression autorisée ; sinon **refus métier explicite**.
- **Migration** : **aucune** strictement nécessaire pour le refus applicatif. **MAIS** pour que le refus soit
  **multi-poste-safe**, il faut idéalement passer la FK `Prescription` de `Cascade` à `Restrict`/`NoAction`
  (migration) afin que la **base** bloque la suppression atomiquement (cf. §13/§14).
- **Simplicité** : élevée (une garde `Any(prescriptions) || Any(sales)`).
- **UX** : bonne pour supprimer un doublon créé par erreur ; mais **aucun moyen de « ranger »** un vrai client
  devenu inactif mais ayant un historique (il reste éternellement dans la liste).
- **Conservation historique** : **totale** (rien n'est jamais détruit).
- **Verdict** : **socle de sécurité minimal solide**, mais **incomplet** sans mécanisme pour masquer un client
  historique inactif.

### Option C — Archivage / désactivation (`IsActive`/`IsArchived`)

- **Comportement** : ajout d'un état ; client archivé **exclu par défaut** des listes/pickers ; historique
  **conservé** ; **réactivation** possible ; pas de suppression physique une fois l'historique présent.
- **Migration** : **requise** (colonne + index + backfill `IsArchived=false` pour l'existant).
- **Impacts queries P2D** : `ListCustomers` et `ListCustomersForPicker` doivent **filtrer** (ou exposer un
  paramètre « inclure archivés ») ; DTO enrichis d'un champ d'état.
- **Impacts UI** : bouton Archiver/Réactiver, filtre d'affichage, exclusion par défaut.
- **Impacts multi-poste** : bon — l'archivage est un **UPDATE** d'un booléen (idempotent, sans destruction) ;
  robuste à la concurrence (deux postes qui archivent aboutissent au même état).
- **Rétrocompatibilité** : bonne (backfill neutre).
- **Verdict** : **mécanisme de rétention le plus aligné** avec la roadmap (« archivage dès qu'un historique
  existe »). Coût = une migration + filtrage des lectures.

### Option D — Soft delete global avec filtre EF (`HasQueryFilter`)

- **`HasQueryFilter`** : masque globalement les lignes « supprimées ». Puissant mais **risqué** : requêtes/Includes
  oubliant le filtre, navigation EF, incohérences d'historique (une vente pointant un client soft-deleted),
  complexité multi-provider (SQLite dev vs futur PostgreSQL/SQL Server), surcharge de tests.
- **Pertinence** : **sur-ingénierie** pour un besoin « ranger les clients inactifs ». MMV n'a qu'**une** entité
  concernée ici ; un filtre global transverse est disproportionné.
- **Verdict** : **à éviter** (sauf besoin transverse démontré ultérieurement). Ne pas choisir par réflexe.

### Option E — Refus total de suppression

- **Simplicité** : maximale (aucune suppression).
- **Inconvénient** : **accumulation de clients créés par erreur** (fautes de frappe, doublons) sans moyen de
  nettoyage ; besoin futur de **fusion** de doublons (hors périmètre P3-2, cf. roadmap).
- **Verdict** : trop rigide seul ; acceptable seulement combiné à un archivage.

---

## 12. Décision recommandée (à **ne pas** implémenter en P3-2A)

**Recommandation : Option B + Option C combinées (« supprimer si vierge, sinon archiver »), avec la base qui
protège l'historique.**

Concrètement, pour P3-2 :

1. **Suppression physique autorisée uniquement si le client n'a AUCUN historique** (aucune prescription **ni**
   vente). Sert à nettoyer les doublons / erreurs de saisie.
2. **Si un historique existe** : **refus** de la suppression physique (règle métier dure → exception typée
   `BusinessRuleException`, déjà disponible, cf. [`DomainExceptions.cs`](../../src/MMV.Domain/Exceptions/DomainExceptions.cs),
   conforme à l'[ADR frontières §8](../architecture/adr-application-boundaries.md) et au choix P3-1 « refus dur =
   exception typée ») **et** proposition d'**archivage** (`IsActive`/`IsArchived`).
3. **Archivage** = état booléen ; **exclusion par défaut** des listes/pickers ; **réactivation** possible ;
   **historique toujours conservé** (ordonnances **et** ventes).
4. **Protection au niveau base (recommandée)** : passer la FK `Prescription → Customer` de **`Cascade`** à
   **`Restrict`/`NoAction`** pour que la **base** interdise atomiquement la suppression d'un client ayant des
   ordonnances (garantie multi-poste, cf. §14). La FK `Sale → Customer` (`SetNull`) devient de fait sans effet
   destructeur puisque la suppression est refusée avant.

**Fondements** (structure réelle, relations EF, conservation historique, simplicité, maintenance, multi-poste,
effort de migration, UI, évolutivité) :

- Le risque **réel** (cascade des ordonnances) impose au minimum de **retirer la destruction silencieuse**.
- L'archivage est **exigé par la roadmap** (« Suppression = dernier recours ; préférer l'archivage dès qu'un
  historique existe ») et par le principe de rétention métier d'un opticien.
- La combinaison B+C évite les deux excès : ni destruction (A/D), ni rigidité totale (E).
- **Pas** de soft-delete global (D) : disproportionné, risqué en multi-provider.
- **Aucune règle légale/RGPD/fiscale inventée** ; **aucun** `Organization`/`Store`/SaaS.

---

## 13. Analyse migration potentielle

Si la stratégie retenue (B+C) est confirmée en P3-2, la migration EF minimale porterait sur :

| Élément | Proposition |
|---|---|
| Propriété | `Customer.IsArchived` (`bool`) — nom le plus explicite pour « rangé, non supprimé ». (`IsActive` = alternative, mais inverse la sémantique par défaut.) |
| Valeur par défaut | `false` (client actif). |
| Backfill | Tous les clients existants ⇒ `IsArchived = false` (défaut colonne, neutre). |
| Index | `HasIndex(c => c.IsArchived)` optionnel (utile si les listes filtrent souvent ; à mesurer, volumétrie magasin faible). |
| Changement FK (recommandé) | `Prescription → Customer` : `Cascade` **→** `Restrict`/`NoAction` (garde-fou base). Modifie une contrainte existante ⇒ **inclus dans la même migration**. |
| Query use cases | `ListCustomersUseCase` et `ListCustomersForPickerUseCase` : filtrer `!IsArchived` par défaut (ou paramètre « inclure archivés »). |
| Pickers | Exclure les archivés du sélecteur de commande/vente. |
| DTO | Ajouter `IsArchived` à `CustomerListItemDto` (pas au picker, sauf besoin). |
| UI | Bouton Archiver/Réactiver, filtre d'affichage, message de refus de suppression. |
| Réactivation | Use case `SetCustomerArchived`/`ReactivateCustomer` (miroir de `SetUserActiveUseCase` déjà existant). |
| Tests | Migration appliquée ; refus si historique ; archivage/réactivation ; exclusion liste+picker ; suppression vierge OK. |
| Multi-poste | Archivage = UPDATE idempotent ; garde-fou base = Restrict atomique (cf. §14). |
| Concurrence | Pas de jeton requis pour l'archivage lui-même ; le garde-fou base couvre la course suppression↔création d'historique. |

> **Contrainte forte** : baseline actuelle `has-pending-model-changes = false`. Toute modification de
> `Customer`/FK en P3-2 **créera** une migration ⇒ la sortie P3-2 devra **assumer et documenter** cette migration
> (la roadmap l'autorise : « migrations P3 assumées et documentées »). **Aucune migration n'est créée en P3-2A.**

---

## 14. Analyse multi-poste

Risques d'une implémentation naïve (`if (!hasHistory) delete;`) sur base centrale partagée :

- **Course « check-then-act »** : le poste A vérifie « pas d'historique », pendant que le poste B crée une vente
  ou une ordonnance pour ce client ; A supprime ⇒ soit la vente devient orpheline (SetNull), soit l'ordonnance
  fraîchement créée est **détruite** (cascade). La vérification préalable **non atomique** est **insuffisante**.
- **Lecture obsolète** : la décision est prise sur des données lues (`GetByCustomerIdAsync`) qui peuvent changer
  avant le `SaveChanges` — l'ADR-PROD-DB-001 §5 interdit explicitement de « décider une règle métier sur une
  valeur lue puis supposée stable ».
- **Archivage concurrent** : deux postes archivant le même client convergent (idempotent) — **sûr**.
- **Suppression concurrente** : deux postes supprimant le même client → le second obtient `CustomerFound=false`
  (déjà géré) — **sûr**.

**Conséquences de conception pour P3-2 (à ne pas implémenter ici)** :

- Le garde-fou **le plus robuste** est la **contrainte FK au niveau base** (`Prescription → Customer` en
  `Restrict`) : la base **refuse atomiquement** la suppression d'un client ayant des ordonnances, indépendamment
  de toute course applicative. C'est la traduction multi-poste correcte de la règle.
- La vérification applicative (`AnyByCustomerIdAsync`) reste utile pour **un message métier clair** et pour
  couvrir les ventes (SetNull ne bloque pas), mais elle **ne doit pas** être l'unique rempart.
- Aucun **jeton de concurrence** `Customer` n'est requis pour l'archivage/refus ; le sujet reste cadré (non
  activé) au niveau global (cf. ADR-PROD-DB-001 §2.2). La suppression étant mono-écriture, `ITransactionRunner`
  n'est pas nécessaire (cohérent avec l'existant) **tant que** le rempart est la contrainte FK.

---

## 15. Frameworks / dépendances

| Besoin | Solution existante | Framework possible | Choix recommandé | Justification |
|---|---|---|---|---|
| Validation | `CustomerValidator` (FluentValidation, transitif Domain) + `CommandValidation` (P3-1) | — | **Existant, suffisant** | pas de duplication, Apache-2.0, déjà en place |
| Refus dur | `BusinessRuleException` (Domain) | — | **Existant, suffisant** | conforme ADR §8 / P3-1 (refus dur = exception typée) |
| Archivage / état | — (à ajouter : `IsArchived` + use case miroir de `SetUserActiveUseCase`) | bibliothèque soft-delete tierce | **Aucune bibliothèque** ; simple booléen + EF Core | évite la magie globale (`HasQueryFilter`), garde le contrôle explicite, multi-provider-safe |
| Contraintes / requêtes | EF Core (`DeleteBehavior`, `Any`) | — | **EF Core existant** | suffisant pour Restrict + existence |

- **Impact NuGet** : **nul** (aucune dépendance ajoutée recommandée).
- **Impact licence** : **nul** (rappel mémoire : FluentAssertions reste **6.x** — non concerné ici).
- **Impact architecture** : `MMV.Application` reste **pure** (Domain + DI.Abstractions) ; le garde-fou vit en
  Application (précondition de use case) + Domain (exception, contrainte via configuration Infrastructure).

**Conclusion : aucune nouvelle bibliothèque nécessaire.**

---

## 16. Risques

1. **Perte d'ordonnances** (cascade) — **risque actif aujourd'hui**, à neutraliser en priorité en P3-2.
2. **Anonymisation de ventes** (SetNull) — perte du lien client sur l'historique commercial.
3. **Migration P3-2** : casser `has-pending-model-changes=false` ; backfill à valider ; changement de FK
   `Cascade→Restrict` à tester sur vrai SQLite (et à re-vérifier au futur portage PostgreSQL/SQL Server).
4. **Régression des lectures P2D** : ajouter un filtre `!IsArchived` peut masquer des clients dans des flux
   existants (liste, picker de commande/vente) — tests de non-régression requis.
5. **Multi-poste** : garde applicatif seul = course « check-then-act » (cf. §14) → privilégier la contrainte base.
6. **UI** : absence de confirmation actuelle ; introduire refus + archivage change plusieurs chemins VM/vues.
7. **Churn** : rester dans le périmètre « Clients » ; ne pas déborder sur Produits/Fournisseurs (P3-4/P3-9) qui
   ont le même point dur mais leur propre étape.

---

## 17. Fichiers modifiés

| Fichier | Nature |
|---|---|
| `docs/implementation/P3-2A-customer-domain-audit-report.md` | **Créé** (ce rapport) |

Aucun autre fichier modifié. La correction de formulation « ventes vs ordonnances » de la roadmap (autorisée
§17 du prompt) est **signalée** ici (§7) mais **non appliquée** : le rapport suffit à la tracer sans toucher la
roadmap en mode audit.

- `src/**` : **inchangé.**
- `tests/**` : **inchangé.**
- `migrations/**` : **inchangé.**
- `.github/**` : **inchangé.**
- `*.csproj` / `*.sln` / packages : **inchangés.**

---

## 18. Contrôles exécutés

```
git branch --show-current                       → p3-business-rules
git status --short                              → (seul le rapport en untracked)
dotnet restore MMV.sln                          → OK
dotnet build MMV.sln --no-restore -c Debug      → Réussi (0 warning, 0 erreur)
dotnet test MMV.sln --no-build -c Debug         → 592 tests, 0 échec
dotnet list MMV.sln package --vulnerable …      → 0 vulnérabilité
dotnet tool restore                             → dotnet-ef 8.0.27
dotnet ef migrations has-pending-model-changes  → No changes since last migration (false)
dotnet list src/MMV.Application … reference     → MMV.Domain uniquement
dotnet list src/MMV.Application … package       → DI.Abstractions 8.0.1 uniquement
```

---

## 19. Résultats

- Build **vert**, **592** tests verts, **0** vulnérabilité, **aucune** migration en attente, `MMV.Application`
  **pure**.
- Audit **Customer** complet (propriétés, validations, absence d'état/token).
- Relations EF et cascades **confirmées dans le code, le snapshot ET les migrations SQL** :
  `Prescription = Cascade`, `Sale = SetNull`, `Order = plus de FK directe`.
- Comportement réel de `DeleteCustomer` documenté (physique, silencieux, non confirmé, non gardé).
- Risques de perte historique documentés (ordonnances détruites, ventes anonymisées).
- Options A→E comparées ; recommandation **B+C** motivée ; impacts multi-poste traités.
- **Aucune** implémentation.

---

## 20. Verdict

**P3-2A = GO local.**

- Audit Customer complet ✔
- Cascades confirmées code + migrations ✔
- Comportement de suppression réel documenté ✔
- Risques de perte historique documentés ✔
- Options suppression/archivage comparées ✔
- Recommandation claire (B+C, historique toujours conservé) ✔
- Impacts multi-poste traités ✔
- Aucune implémentation ✔
- Baseline finale verte ✔

---

## 21. Prochaine étape recommandée

**P3-2B — Implémentation** de la sécurisation suppression/archivage Clients (cycle standard : un commit + un
rapport + CI verte) :

1. Ajouter `Customer.IsArchived` (+ migration + backfill) et changer la FK `Prescription → Customer`
   `Cascade → Restrict` **dans la même migration** (garde-fou base multi-poste).
2. `DeleteCustomerUseCase` : refuser (`BusinessRuleException`) si historique (prescriptions **ou** ventes) ;
   sinon suppression physique. Ajouter une existence légère (`AnyByCustomerIdAsync`) côté repositories
   Prescription/Sale plutôt que charger les collections.
3. Nouveau use case d'archivage/réactivation (miroir de `SetUserActiveUseCase`).
4. Filtrer `!IsArchived` par défaut dans `ListCustomersUseCase` et `ListCustomersForPickerUseCase`.
5. UI : confirmation, bouton Archiver/Réactiver, filtre d'affichage, message de refus.
6. Tests (vrai SQLite) : refus avec historique, archivage/réactivation, exclusion liste+picker, suppression
   vierge OK, cascade base (Restrict) prouvée.

> À valider métier au démarrage de P3-2B : **définition de « historique »** (prescriptions seules, ou
> prescriptions + ventes), et politique de **doublon souple** (alerte, pas blocage — cf. roadmap P3-2).

**Aucun commit, aucun push, P3-2B non commencé.**
