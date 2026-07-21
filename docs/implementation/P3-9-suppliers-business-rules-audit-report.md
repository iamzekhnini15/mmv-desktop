# P3-9 — Fournisseurs — Rapport d'audit backend

> **MODE = AUDIT_AND_WRITE_REPORT_ONLY** — aucun code modifié, aucun test modifié, aucune migration créée, aucun
> commit, aucun push, aucune UI.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | P3-9 |
| Périmètre | Backend uniquement (Domain / Application / Infrastructure) |
| HEAD attendu | `b30f7748cf6d3eb69cc88c4a6457dea170be4e8a` |
| Run CI attendu | `29783755704` |
| Tests attendus | 1202 |

---

## 2. État Git et CI

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → b30f7748cf6d3eb69cc88c4a6457dea170be4e8a
git rev-parse origin/…      → b30f7748cf6d3eb69cc88c4a6457dea170be4e8a
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
git diff --check            → (vide)
git log -5 --oneline        → b30f774 / ae0e35d / 53d689e / a4d7380 / 3e7a1ab
```

CI du SHA exact :

```json
{"databaseId":29783755704,
 "headSha":"b30f7748cf6d3eb69cc88c4a6457dea170be4e8a",
 "headBranch":"p3-business-rules",
 "event":"push","status":"completed","conclusion":"success"}
```

✅ Branche conforme. ✅ HEAD local = HEAD distant = SHA attendu. ✅ Aucun fichier suivi modifié. ✅ Seuls
`design-handoff/`, `design/` et `docs/ui/` restent non suivis, hors périmètre.

**Porte Git/CI : PASSÉE.**

---

## 3. Baseline

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore` | OK | up-to-date | ✅ |
| `dotnet build -c Debug` | 0 erreur / 0 avertissement | 0 erreur / 0 avertissement | ✅ |
| `MMV.Domain.Tests` | 514 | 514 | ✅ |
| `MMV.Application.Tests` | 449 | 449 | ✅ |
| `MMV.App.Tests` | 239 | 239 | ✅ |
| **Total** | **1202** | **1202** | ✅ |
| Échecs / ignorés | 0 / 0 | 0 / 0 | ✅ |
| Vulnérabilités (incl. transitives) | 0 | 0 sur les 7 projets | ✅ |
| Migrations en attente | 0 | « No changes have been made to the model since the last migration. » | ✅ |
| Références `MMV.Application` | `MMV.Domain` uniquement | `..\MMV.Domain\MMV.Domain.csproj` | ✅ |

`MMV.Application` ne porte qu'un seul paquet : `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1.

**Baseline : CONFORME.**

---

## 4. Documents lus

Lus intégralement : `P3-business-rules-roadmap.md`, `P3-4A-product-domain-audit-report.md`,
`P3-4B-product-business-rules-report.md`, `P3-5-stock-and-movements-implementation-report.md`,
`P3-7-sales-business-rules-implementation-report.md`,
`P3-8-notifications-business-rules-implementation-report.md`, `adr-prod-db-001-multi-poste-database-strategy.md`,
`adr-application-boundaries.md`, `adr-transaction-idempotency.md`.

Lus également pour les sujets connexes : `P3-2A`/`P3-2B` (archivage client — précédent directement applicable),
`P3-3B`, `P3-6`, `P3-7` (acquisition atomique d'entité active), `adr-sqlite-lifecycle.md`.

### Ce que la roadmap dit de P3-9 (§475-484)

> « **interdire la suppression** d'un fournisseur ayant des produits liés (`Product.SupplierId` est **non nullable**
> → suppression dure = risque d'échec FK ou d'orphelins) ou archiver ; email valide si renseigné (**absent** :
> `Supplier` n'a **aucun** validateur, contrairement à `Customer`). »

**La roadmap est exacte sur les deux points** — vérifié dans le code et empiriquement (§6, §11, §12). La question
posée dans l'énoncé (« la roadmap affirme-t-elle à tort que `SupplierId` est non nullable ? ») appelle une réponse
**non** : `SupplierId` est bien un `long` non nullable. C'est le seul point où la roadmap aurait pu se tromper ;
elle ne se trompe pas.

**Le code prime toujours sur les documents** — c'est pourquoi les affirmations centrales de ce rapport sont
**vérifiées empiriquement** sur une vraie base SQLite (§16), et non déduites de la lecture seule.

---

## 5. Cartographie

| Élément | Chemin exact | Couche | Responsabilité actuelle |
|---|---|---|---|
| `Supplier` | `src/MMV.Domain/Entities/Supplier.cs` | Domain | Entité anémique — 6 propriétés `{ get; set; }`, 0 méthode, 0 invariant |
| `SupplierValidator` | **n'existe pas** | Domain | — |
| `ISupplierRepository` | `src/MMV.Domain/Interfaces/Repositories/ISupplierRepository.cs` | Domain | Port : hérite `IGenericRepository<Supplier, long>` + 3 méthodes |
| `SupplierRepository` | `src/MMV.Infrastructure/Repositories/SupplierRepository.cs` | Infrastructure | Implémentation EF |
| `BaseRepository` | `src/MMV.Infrastructure/Repositories/BaseRepository.cs` | Infrastructure | CRUD générique (`CreateAsync`/`UpdateAsync`/`DeleteAsync`/`GetAllAsync`) |
| `SupplierConfiguration` | `src/MMV.Infrastructure/Data/Configurations/SupplierConfiguration.cs` | Infrastructure | Fluent API — **contient un `DeleteBehavior.SetNull` sans effet** (§11) |
| `ProductConfiguration` | `src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs` | Infrastructure | Fluent API — **c'est elle qui gagne** : `Restrict` + `IsRequired()` |
| `CreateSupplierUseCase` | `src/MMV.Application/UseCases/Suppliers/CreateSupplier/` | Application | Création — **aucune validation** |
| `UpdateSupplierUseCase` | `src/MMV.Application/UseCases/Suppliers/UpdateSupplier/` | Application | Modification — **aucune validation** |
| `DeleteSupplierUseCase` | `src/MMV.Application/UseCases/Suppliers/DeleteSupplier/` | Application | Suppression **physique** — **aucune vérification produits** |
| `ListSuppliersUseCase` | `src/MMV.Application/UseCases/Suppliers/ListSuppliers/` | Application | Liste complète projetée |
| `GetSupplierWithProductsUseCase` | `src/MMV.Application/UseCases/Suppliers/GetSupplierWithProducts/` | Application | Fiche + produits |
| `ProductSupplierRefDto` | `src/MMV.Application/UseCases/Products/ListProducts/ProductSupplierRefDto.cs` | Application | Réf. fournisseur dans la liste produits |
| `SuppliersViewModel` | `src/MMV.App/ViewModels/SuppliersViewModel.cs` | UI | Orchestration écran + **`catch (Exception ex)` affichant `ex.Message`** |
| `SuppliersListViewModel` | `src/MMV.App/ViewModels/SuppliersListViewModel.cs` | UI | Recherche/pagination **en mémoire** |
| `SupplierFormViewModel` | `src/MMV.App/ViewModels/SupplierFormViewModel.cs` | UI | Formulaire — **seul lieu de validation actuel** |
| `SupplierDetailViewModel` | `src/MMV.App/ViewModels/SupplierDetailViewModel.cs` | UI | Fiche + déclencheur de suppression |
| `ProductFormViewModel` | `src/MMV.App/ViewModels/ProductFormViewModel.cs` | UI | Sélecteur fournisseur (`IListSuppliersUseCase`) |
| Enregistrement DI | `src/MMV.App/App.axaml.cs:129` | Composition root | `services.AddScoped<ISupplierRepository, SupplierRepository>()` |
| Seed | `src/MMV.Infrastructure/Data/DbInitializer.cs:177-215` | Infrastructure | 25 fournisseurs, créés **avant** les produits |
| Tests Application | `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierUseCasesTests.cs` | Test | 7 tests, SQLite réel |
| Tests Application (lecture) | `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierQueryUseCasesTests.cs` | Test | 6 tests, SQLite réel |
| Tests UI | `tests/MMV.App.Tests/ViewModels/SupplierFormViewModelDelegationTests.cs` | Test UI | Délégation — **ne protège rien côté backend** |

**Aucun `SupplierService` Domain, aucune exception Domain fournisseur, aucun `ISupplierRepository` étendu au-delà de
la lecture.** Le domaine Fournisseur est, à ce jour, le **moins couvert par des règles métier de tout P3**.

---

## 6. Modèle réel de `Supplier`

`src/MMV.Domain/Entities/Supplier.cs` — 6 propriétés, toutes `{ get; set; }` publiques, plus une navigation.

| Champ | Type | Nullable | Longueur déclarée | Longueur **réellement imposée** | Défaut | Index | Validation réelle |
|---|---|---:|---:|---:|---|---:|---|
| `SupplierId` | `long` | non | — | — | AUTOINCREMENT | PK | — |
| `Name` | `string` | **non** (`= string.Empty`) | `HasMaxLength(200)` | **aucune** | `""` | **aucun** | **aucune côté backend** |
| `ContactEmail` | `string?` | oui | `HasMaxLength(254)` | **aucune** | `null` | **aucun** | **aucune** |
| `Phone` | `string?` | oui | `HasMaxLength(20)` | **aucune** | `null` | **aucun** | **aucune** |
| `Address` | `string?` | oui | `HasMaxLength(500)` | **aucune** | `null` | **aucun** | **aucune** |
| `ReferenceCode` | `string?` | oui | `HasMaxLength(50)` | **aucune** | `null` | **aucun** | **aucune** |
| `Products` | `ICollection<Product>` | — | — | — | `new List<Product>()` | — | navigation |

### DDL réellement produit (relevé sur base créée par `EnsureCreated()`)

```sql
CREATE TABLE "Suppliers" (
    "SupplierId" INTEGER NOT NULL CONSTRAINT "PK_Suppliers" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "ContactEmail" TEXT NULL,
    "Phone" TEXT NULL,
    "Address" TEXT NULL,
    "ReferenceCode" TEXT NULL
)
```

Indexes sur `Suppliers` : **aucun** hors la clé primaire implicite.

> **Point capital, souvent mal compris.** `HasMaxLength(200)` **n'impose rien en SQLite** : le type généré est
> `TEXT`, sans `CHECK`, sans `VARCHAR(n)`. SQLite applique une affinité de type, pas une contrainte de longueur.
> Les longueurs déclarées dans `SupplierConfiguration` sont donc de la **documentation**, pas une protection.
> Elles ne deviendraient contraignantes que sur un provider serveur (cf. `adr-prod-db-001`).

### Réponses aux 14 questions

1. **Quel champ identifie fonctionnellement un fournisseur ?** Aucun de façon garantie. `Name` et `ReferenceCode`
   sont des candidats naturels, mais **ni l'un ni l'autre n'est unique** en base ni en code. Le seul identifiant
   fiable est la clé technique `SupplierId`.
2. **Le nom est-il obligatoire ?** Au sens de la base, `Name` est `NOT NULL` — mais `""` satisfait `NOT NULL`.
   **Aucune règle backend n'exige un nom non vide.** Seule l'UI (`SupplierFormViewModel`) le contrôle.
3. **L'email est-il facultatif ?** Oui — `string?`, colonne `NULL`, absent du seed pour aucun fournisseur mais
   nullable par construction.
4. **L'email est-il validé dans le Domain, l'Application ou seulement l'UI ?** **Seulement l'UI.** Il n'existe
   aucun `SupplierValidator`, et ni `CreateSupplierUseCase` ni `UpdateSupplierUseCase` n'appellent
   `CommandValidation`.
5. **Le téléphone est-il validé ?** Non, nulle part côté backend.
6. **L'adresse est-elle structurée ou libre ?** **Libre** — un seul `string?` `Address`. Aucune décomposition
   rue/ville/code postal. Le seed y stocke une adresse complète en une chaîne.
7. **Existe-t-il un `IsActive` ou `IsArchived` ?** **Non.** Le fournisseur est la seule entité de référence de P3
   sans état de cycle de vie (`Customer` a `IsArchived` depuis P3-2B, `Product` a `IsActive`).
8. **Existe-t-il une date de création/modification ?** **Non.** Aucun `CreatedAt`, aucun `UpdatedAt`.
9. **Existe-t-il un code fournisseur ou une référence externe ?** Oui — `ReferenceCode`, `string?`, **sans
   unicité**, **sans normalisation**, et **jamais lu par aucun appelant** (§7).
10. **Existe-t-il une contrainte d'unicité ?** **Aucune**, à aucun niveau : ni index base, ni garde applicative.
11. **Les setters sont-ils publics ?** Oui, tous les six. N'importe quelle couche ayant l'entité peut tout
    réécrire.
12. **Le modèle possède-t-il des invariants métier ?** **Aucun.** Zéro méthode, zéro garde, zéro constructeur
    contraignant. Entité purement anémique.
13. **Une valeur vide, espaces seuls ou trop longue peut-elle être persistée ?** **Oui — les trois, vérifié
    empiriquement** (§16).
14. **Les erreurs de longueur proviennent-elles uniquement de la base ?** **Non — elles ne proviennent de nulle
    part.** La base ne les produit pas (TEXT sans contrainte), et aucune couche backend ne les produit non plus.
    C'est une correction importante à l'intuition naturelle : le filet supposé n'existe pas.

**Aucune règle nouvelle n'est inventée ici** : ce tableau décrit ce que le code fait, pas ce qu'il devrait faire.

---

## 7. Inventaire exhaustif des écritures

Recherche menée sur `new Supplier`, `Supplier {`, `CreateSupplier`, `UpdateSupplier`, `DeleteSupplier`,
`CreateAsync`, `UpdateAsync`, `DeleteAsync`, `SupplierId =`, `Email =`, `Phone =`, `IsActive`, `IsArchived`,
`SaveChanges`, `ExecuteUpdate`, `ExecuteDelete`, `ITransactionRunner`.

| Chemin | Déclencheur | Champs écrits | Validation | Transaction | Concurrence sûre |
|---|---|---|---|---|---|
| `CreateSupplierUseCase.ExecuteAsync` | UI formulaire (création) | `Name`, `ContactEmail`, `Phone`, `Address`, `ReferenceCode` | ❌ **aucune** | ❌ `SaveChangesAsync` nu | ⚠️ mono-écriture atomique, mais aucun doublon empêché |
| `UpdateSupplierUseCase.ExecuteAsync` | UI formulaire (édition) | les 5 mêmes, **tous écrasés inconditionnellement** | ❌ **aucune** | ❌ `SaveChangesAsync` nu | ❌ **lost update** (read-modify-write sans jeton) |
| `DeleteSupplierUseCase.ExecuteAsync` | UI fiche → confirmation | suppression physique de la ligne | ❌ **aucune** | ❌ `SaveChangesAsync` nu | ❌ **TOCTOU** + exception FK brute |
| `CreateProductUseCase:78` | UI formulaire produit | `Product.SupplierId = command.SupplierId ?? 0` | ❌ fournisseur **jamais vérifié** | ✅ `ITransactionRunner` | ❌ FK peut échouer, non traduite |
| `UpdateProductUseCase:68` | UI formulaire produit | `Product.SupplierId = command.SupplierId ?? 0` | ❌ fournisseur **jamais vérifié** | ✅ `ITransactionRunner` | ❌ idem |
| `DbInitializer.CreateSuppliers` | **Seed** | 25 fournisseurs complets | n/a | seed | n/a |
| `DbInitializer.CreateProducts` | **Seed** | `SupplierId` tiré au hasard parmi les fournisseurs déjà persistés | n/a | seed | n/a |
| `20260127184542_InitialCreate` | **Migration** | table `Suppliers` + FK | n/a | migration | n/a |
| `20260201181136_ProductSchemaRefactoring` | **Migration** | FK recréée en `Restrict` (le `Down` la remet en `SetNull`) | n/a | migration | n/a |
| `BaseRepository.DeleteAsync(TId)` | **aucun appelant** | — | — | — | **chemin de suppression alternatif non utilisé** |
| `ISupplierRepository.GetByNameAsync` | **aucun appelant** | lecture | — | — | **code mort** |
| `ISupplierRepository.GetByReferenceCodeAsync` | **aucun appelant** | lecture | — | — | **code mort** |

**Écriture UI directe** : **aucune**. P2C-GLOBAL a bien déplacé toutes les écritures fournisseur vers
l'Application ; `AppUiPersistenceGuardrailTests` verrouille ce point. `SuppliersViewModel` n'accède plus à
`ISupplierRepository`.

**Aucun `ExecuteUpdate`/`ExecuteDelete` sur `Suppliers`.** Aucun `ITransactionRunner` dans les trois use cases
d'écriture fournisseur.

### Le contraste qui résume l'état du domaine

`CreateProductUseCase` fait — validation Domain avant écriture, garde d'unicité applicative, filet d'index unique
en base, `ITransactionRunner`, traduction de `PersistenceException` en message métier stable. `CreateSupplierUseCase`
fait — `new Supplier { … }` puis `SaveChangesAsync`. Le fournisseur n'a simplement **jamais eu son tour** dans P3.

---

## 8. Contrats `CreateSupplier` / `UpdateSupplier`

`CreateSupplierCommand` : `Name`, `ContactEmail`, `Phone`, `Address`, `ReferenceCode`.
`UpdateSupplierCommand` : les mêmes **plus** `SupplierId`.

| Champ | Create | Update | Fourni par appelant | Normalisé | Validé | Peut être falsifié |
|---|---:|---:|---:|---:|---:|---:|
| `SupplierId` | ❌ | ✅ | ✅ | — | ❌ (existence testée par `GetByIdAsync`) | n/a (clé) |
| `Name` | ✅ | ✅ | ✅ | ❌ pas de `Trim` | ❌ | ✅ vide / espaces / 5000 car. |
| `ContactEmail` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `"pas-un-email"` accepté |
| `Phone` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ |
| `Address` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ |
| `ReferenceCode` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ doublon possible |

### Réponses aux 10 questions

1. **Les commandes sont-elles nullables ?** Les commandes elles-mêmes sont rejetées si `null`
   (`ArgumentNullException`). Leurs **champs** sont nullables sauf `Name` (`string` avec défaut `""` — donc jamais
   `null`, mais potentiellement vide).
2. **Les use cases appellent-ils un validateur ?** **Non.** `CommandValidation` n'est jamais importé dans
   `UseCases/Suppliers/`. C'est le seul dossier de use cases d'écriture de P3 dans ce cas.
3. **La validation s'exécute-t-elle avant toute écriture ?** Sans objet — il n'y a pas de validation.
4. **Les messages sont-ils stables ?** Il n'existe **aucun** message métier fournisseur. Aucune constante publique
   du type `DeleteCustomerUseCase.CustomerHasHistoryMessage`. Ce que l'utilisateur voit en cas d'échec est le
   `ex.Message` d'une exception EF (§12).
5. **Les résultats suivent-ils la convention P3-1 ?** **Partiellement.** `CreateSupplierResult`,
   `UpdateSupplierResult` et `DeleteSupplierResult` portent bien un booléen « trouvé » (sauf Create), mais
   **aucun ne porte de `ValidationErrors`** — contrairement à `CreateProductResult`/`UpdateProductResult`.
   Ajouter la validation exigera donc d'**étendre ces résultats** (additif).
6. **Un fournisseur introuvable est-il distingué d'une erreur technique ?** **Oui**, et c'est un point sain :
   `SupplierFound = false` sans écriture, jamais d'exception. Comportement correct, à préserver.
7. **Une modification peut-elle écraser des champs avec des valeurs nulles ou vides ?** **Oui.**
   `UpdateSupplierUseCase` affecte les cinq champs inconditionnellement. Une commande partiellement remplie
   **efface** l'email, le téléphone, l'adresse et le code existants. Il n'existe aucune sémantique « champ non
   fourni = ne pas toucher ». C'est aussi le vecteur du **lost update** multi-poste (§15).
8. **Existe-t-il une normalisation `Trim` ?** **Non**, nulle part. À comparer avec `Product.Reference`, dont le
   setter nettoie et normalise (P3-4B).
9. **L'email vide est-il traité comme `null` ou comme valeur ?** **Comme valeur.** `""` est persisté tel quel.
   Deux représentations de « pas d'email » coexistent donc en base (`NULL` et `""`), ce qui rendrait toute future
   règle d'unicité ou de comparaison ambiguë.
10. **Des champs inconnus ou non utilisés existent-ils dans le contrat ?** Non — les cinq champs correspondent
    exactement aux colonnes. En revanche `SupplierListItemDto.Products` est **toujours vide** en contexte liste
    (documenté comme iso-fonctionnel P2D-2 : le compteur « {0} ref. » de l'écran affiche donc **0** en permanence).

---

## 9. Validation email

### Ce qui existe déjà dans le dépôt

| Validateur | Chemin | Règle email |
|---|---|---|
| `CustomerValidator` | `src/MMV.Domain/Validators/CustomerValidator.cs` | `RuleFor(c => c.Email).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email)).MaximumLength(200)` |
| `UserValidator` | `src/MMV.Domain/Validators/UserValidator.cs` | présent |
| `ProductValidator` | `src/MMV.Domain/Validators/ProductValidator.cs` | pas d'email |
| `PrescriptionValidator` | `src/MMV.Domain/Validators/PrescriptionValidator.cs` | pas d'email |
| **`SupplierValidator`** | **n'existe pas** | — |

Le mécanisme d'application est `MMV.Application/Common/CommandValidation.cs`, qui convertit un résultat
FluentValidation en `IReadOnlyList<ValidationError>` — exactement ce que `CreateProductUseCase:82` consomme.

### Réponses aux 10 questions

1. **Quel validateur email existe déjà ?** `FluentValidation.EmailAddress()`, employé par `CustomerValidator` selon
   le motif « valide **si renseigné** ». C'est précisément la règle candidate P3-9.
2. **Peut-il être réutilisé sans duplication ?** **Oui.** Un `SupplierValidator : AbstractValidator<Supplier>`
   calquant `CustomerValidator` réutilise le même opérateur FluentValidation, le même `CommandValidation`, la même
   convention de message. Aucune regex maison ne doit être écrite — l'énoncé l'interdit et le dépôt n'en a pas
   besoin.
3. **L'email fournisseur doit-il être facultatif ?** **Oui** — la colonne est nullable, le modèle l'autorise, et
   rien dans le métier ne le rend obligatoire. Le rendre requis serait une **règle inventée**, et invaliderait
   rétroactivement toute ligne historique sans email.
4. **Une chaîne vide doit-elle être acceptée comme absence ?** Le `.When(!IsNullOrWhiteSpace)` de `CustomerValidator`
   traite déjà `""` et `"   "` comme absence — donc **oui**, par cohérence stricte avec le précédent client.
   La normalisation `""` → `null` est une amélioration souhaitable mais **distincte** de la validation ; elle doit
   être décidée explicitement (§21) car elle modifie des valeurs persistées.
5. **Les espaces doivent-ils être supprimés ?** Un `Trim` est souhaitable et sans risque (aucune unicité ne
   dépend de l'email aujourd'hui). À noter : sans `Trim`, `" a@b.fr "` **échouerait** `EmailAddress()` —
   introduire la validation sans normalisation transformerait donc une saisie bénigne en erreur bloquante.
   **Les deux vont ensemble.**
6. **La comparaison doit-elle être sensible à la casse ?** **Question sans objet en P3-9** : aucune comparaison
   d'email n'existe et aucune n'est proposée (pas d'unicité email — §10). À trancher seulement si une unicité était
   un jour introduite.
7. **Existe-t-il une limite de longueur compatible avec la base ?** `HasMaxLength(254)` est déclaré (longueur
   maximale RFC 5321) mais **non imposé par SQLite** (§6). Une règle `MaximumLength(254)` dans le validateur
   rendrait cette limite **réellement effective pour la première fois**, côté Application. Elle est **compatible**
   avec la base : elle est plus stricte, jamais plus permissive.
8. **Une adresse Unicode est-elle supportée par le validateur actuel ?** `EmailAddress()` de FluentValidation
   utilise par défaut le mode `EmailValidationMode.AspNetCoreCompatible`, qui se réduit à « contient exactement un
   `@` non en première ni dernière position ». Il est donc **permissif** et accepte les adresses internationalisées.
   Conséquence honnête : cette règle attrape les fautes de frappe grossières (`"pas-un-email"`), **pas** les
   adresses syntaxiquement exotiques. C'est suffisant pour l'objectif — et c'est exactement le niveau de garantie
   déjà accepté pour `Customer`. Prétendre davantage serait surestimer la protection.
9. **Le validateur produit-il un message métier stable ?** `CustomerValidator` laisse le message par défaut de
   FluentValidation pour l'email (il ne surcharge `.WithMessage` que pour prénom/nom). Pour la stabilité exigée par
   P3-1, un `.WithMessage("…")` explicite est préférable côté fournisseur.
10. **Une migration est-elle nécessaire pour introduire la validation ?** **Non.** La validation est un contrôle
    applicatif pré-écriture ; elle ne touche ni schéma ni données existantes. Les 25 emails du seed sont tous
    syntaxiquement valides (§16), donc aucun blocage rétroactif n'est à craindre sur une base de démonstration.

---

## 10. Doublons fournisseurs

| Axe de doublon | Contrainte base | Garde applicative | Doublon possible aujourd'hui |
|---|---|---|---|
| `Name` | ❌ | ❌ | ✅ **vérifié empiriquement** |
| `ContactEmail` | ❌ | ❌ | ✅ |
| `Phone` | ❌ | ❌ | ✅ |
| `Address` | ❌ | ❌ | ✅ |
| `Name` + `Phone` | ❌ | ❌ | ✅ |
| `ReferenceCode` | ❌ | ❌ | ✅ **vérifié empiriquement** |

### Réponses aux 8 questions

1. **Une contrainte unique existe-t-elle ?** **Aucune** — la table `Suppliers` ne porte aucun index (§6).
2. **Deux fournisseurs peuvent-ils légitimement partager un nom ?** Oui, plausiblement : deux agences d'un même
   groupe, deux entités régionales. Rien dans le dépôt ne l'interdit ni ne le documente.
3. **Un email partagé est-il possible ?** Oui — une adresse `contact@groupe.fr` commune à plusieurs entités est un
   cas réel courant.
4. **Le téléphone peut-il être partagé ?** Oui, pour les mêmes raisons (standard commun).
5. **Existe-t-il une politique produit documentée ?** **Non.** Aucun document du dépôt n'énonce de règle de doublon
   fournisseur. La roadmap P3-9 n'en mentionne aucune.
6. **L'UI avertit-elle sans bloquer ?** **Non** — `SupplierFormViewModel` ne fait aucune recherche de doublon.
7. **Une politique de doublon serait-elle prouvée ou inventée ?** **Inventée.** C'est le point le plus important de
   cette section.
8. **Quel sujet doit rester hors P3-9 faute de preuve ?** **Toute unicité métier fournisseur** — nom, email,
   téléphone, et `ReferenceCode` compris.

> **`ReferenceCode` mérite un mot particulier.** Son nom suggère fortement un identifiant unique, et le seed lui
> donne 25 valeurs distinctes (`VP001` … `AC025`) — ce qui *ressemble* à une preuve. Ce n'en est pas une : un seed
> de démonstration est une donnée d'illustration, pas une règle métier. De plus, `GetByReferenceCodeAsync` existe
> dans le port et renvoie `FirstOrDefaultAsync` — signature qui **présuppose** l'unicité sans jamais la garantir —
> et **n'a aucun appelant**. Introduire un index unique sur `ReferenceCode` en P3-9 imposerait de traiter les
> doublons historiques d'une base client réelle qu'aucune donnée du dépôt ne permet d'anticiper, et de décider si
> `""` et `NULL` sont distincts. **Report explicite.**

---

## 11. Relation `Supplier` → `Product`

### La contradiction de configuration, et ce qui gagne réellement

Deux fichiers configurent **la même relation** :

```csharp
// SupplierConfiguration.cs:33-36
builder.HasMany(s => s.Products).WithOne(p => p.Supplier)
    .HasForeignKey(p => p.SupplierId)
    .OnDelete(DeleteBehavior.SetNull);      // ← n'a AUCUN effet

// ProductConfiguration.cs:74-78
builder.HasOne(p => p.Supplier).WithMany(s => s.Products)
    .HasForeignKey(p => p.SupplierId)
    .OnDelete(DeleteBehavior.Restrict)      // ← c'est CELLE-CI qui gagne
    .IsRequired();
```

**Preuve, et non déduction.** Trois sources concordantes :

- `OpticDbContextModelSnapshot.cs:1146-1150` — `.OnDelete(DeleteBehavior.Restrict).IsRequired()` ;
- `20260201181136_ProductSchemaRefactoring.cs:258` (`Up`) — `onDelete: ReferentialAction.Restrict` ;
- **relevé direct sur une base créée** :
  `PRAGMA foreign_key_list('Products')` ⇒ `from=SupplierId to=SupplierId on_delete=RESTRICT`.

Le `SetNull` de `SupplierConfiguration` est donc du **code trompeur mais inoffensif aujourd'hui** — il décrit un
comportement qui n'existe pas. Il serait d'ailleurs **impossible** à appliquer : `SetNull` exige une FK nullable,
or `SupplierId` est `long` non nullable. C'est une dette de lisibilité 🟠, pas un défaut fonctionnel.

### Schéma réel relevé

- `Product.SupplierId` : `long` **non nullable** (`b.Property<long>("SupplierId")`, snapshot:564) ;
- index `IX_Products_SupplierId` (**non unique**) — présent, utile aux jointures et à la vérification FK ;
- FK `FK_Products_Suppliers_SupplierId` : `ON DELETE RESTRICT` ;
- `PRAGMA foreign_keys = 1` — **les FK sont effectivement appliquées** (relevé, cf. §16).

| Relation | FK | Nullable | DeleteBehavior | Risque suppression |
|---|---|---:|---|---|
| `Product` → `Supplier` | `FK_Products_Suppliers_SupplierId` | **non** | **RESTRICT** (effectif) | ❌ aucun orphelin, aucune cascade — mais **exception technique brute** (§12) |

### Réponses aux 12 questions

1. **Le fournisseur est-il obligatoire pour tous les produits ?** **Oui**, structurellement : colonne non nullable
   + FK `RESTRICT` + `IsRequired()`.
2. **Peut-on créer un produit sans fournisseur ?** **Non — et le chemin d'échec est mauvais.**
   `CreateProductCommand.SupplierId` est `long?`, et `CreateProductUseCase:78` écrit `command.SupplierId ?? 0`.
   Comme aucun fournisseur ne porte l'id `0`, la FK rejette l'insertion. **Vérifié empiriquement** : l'écriture
   lève une `DbUpdateException`. Le `?? 0` transforme une donnée manquante en violation d'intégrité, au lieu d'une
   erreur de validation lisible.
3. **Peut-on modifier un produit vers un autre fournisseur ?** Oui, librement — `UpdateProductUseCase:68`, sans
   aucune vérification.
4. **Un fournisseur introuvable produit-il une erreur contrôlée ?** **Non.** Ni `CreateProductUseCase` ni
   `UpdateProductUseCase` ne chargent le fournisseur ; `ProductValidator` ne porte **aucune** règle sur
   `SupplierId`. Un `SupplierId` inexistant produit une `PersistenceException` de catégorie `ConstraintViolation`
   via `ITransactionRunner` — non traduite en message métier, donc propagée à l'UI.
5. **Un fournisseur inactif/archivé pourrait-il être sélectionné aujourd'hui ?** Question **sans objet** : le
   concept n'existe pas (§6.7). Elle ne deviendrait pertinente que si P3-9 introduisait un état.
6. **Une FK protège-t-elle l'intégrité ?** **Oui, et elle fonctionne** — c'est le seul filet réel du domaine.
7. **Quel est le `DeleteBehavior` exact ?** `Restrict` (voir preuves ci-dessus).
8. **La suppression échoue-t-elle, cascade-t-elle ou met-elle la FK à null ?** **Elle échoue.** Aucune cascade,
   aucun orphelin. **Vérifié empiriquement** (§16) : le fournisseur reste présent, le produit conserve son
   `SupplierId`.
9. **Des données historiques peuvent-elles avoir `SupplierId = null` ?** **Non** — la colonne est `NOT NULL`
   depuis `InitialCreate`. En revanche, une base ayant subi le `Down` de `ProductSchemaRefactoring` porterait une FK
   `SetNull` ; scénario de rollback théorique, non observé, et sans effet tant qu'aucune suppression n'a lieu.
10. **La roadmap affirme-t-elle à tort que `SupplierId` est non nullable ?** **Non, la roadmap a raison.**
11. **Les produits archivés/inactifs comptent-ils comme historique lié ?** **Oui, nécessairement.** Un produit
    `IsActive = false` conserve sa ligne, donc sa FK. La base ne fait aucune distinction : elle bloque la
    suppression du fournisseur qu'il soit actif ou non. Toute garde applicative P3-9 **doit** compter les produits
    inactifs, sous peine d'annoncer « suppression possible » puis d'échouer en base — le pire des deux mondes.
12. **Le seed crée-t-il toujours des fournisseurs avant les produits ?** **Oui** — `DbInitializer:48-49` persiste
    les 25 fournisseurs, puis `:63` crée les produits en piochant parmi les `SupplierId` déjà attribués. Ordre
    correct, jamais de FK violée au seed.

---

## 12. Suppression actuelle — `DeleteSupplierUseCase`

### Séquence exacte

1. **lecture** — `_supplierRepository.GetByIdAsync(command.SupplierId)` (`FindAsync`, entité **suivie**) ;
2. **vérification** — `if (supplier is null) → SupplierFound = false` — **c'est la seule vérification** ; aucun
   contrôle de produits liés ;
3. **suppression** — `_supplierRepository.DeleteAsync(supplier)` → `_dbSet.Remove(entity)` (marquage local) ;
4. **sauvegarde** — `_unitOfWork.SaveChangesAsync()` → `DELETE FROM Suppliers …` ;
5. **commit** — **aucun** : pas de transaction explicite, pas d'`ITransactionRunner`.

### Réponses aux 15 questions

1. **Le use case existe-t-il ?** Oui.
2. **Suppression physique ?** **Oui** — `DELETE`, ligne détruite.
3. **Charge-t-il le fournisseur ?** Oui, par `FindAsync`.
4. **Vérifie-t-il les produits liés ?** **Non.** C'est le défaut central de P3-9.
5. **Utilise-t-il une transaction ?** Non. Une seule écriture ⇒ atomique de fait, mais **sans `ITransactionRunner`
   il n'y a aucune traduction d'exception** — c'est la conséquence importante, pas l'atomicité.
6. **Fournisseur introuvable ?** `SupplierFound = false`, aucune écriture. **Correct.**
7. **Si la FK bloque ?** SQLite renvoie `SQLITE_CONSTRAINT_FOREIGNKEY` ; EF l'enveloppe en `DbUpdateException`.
   **Vérifié empiriquement.**
8. **Une exception EF/SQLite brute peut-elle remonter ?** **Oui, prouvé.** Aucun `try/catch`, aucun
   `ITransactionRunner`, donc aucun passage par `PersistenceErrorMapper`. La `DbUpdateException` traverse
   l'Application intacte.
9. **L'UI interprète-t-elle une erreur technique ?** **Elle l'affiche.** `SuppliersViewModel:194-198` :

   ```csharp
   catch (Exception ex)
   {
       await _dialogService.ShowErrorAsync(
           "Erreur de suppression",
           $"Impossible de supprimer le fournisseur :\n{ex.Message}");
   }
   ```

   L'utilisateur voit donc littéralement *« An error occurred while saving the entity changes. See the inner
   exception for details. »* — message anglais, technique, sans rapport avec la cause réelle (« ce fournisseur a
   des produits »). Le `catch` évite le crash ; il ne rend pas l'erreur compréhensible. **Ce point viole
   frontalement `adr-application-boundaries`**, qui interdit qu'un message provider franchisse la frontière.

10. **Existe-t-il une confirmation UI ?** Oui — `ShowConfirmationAsync` avec le texte « ⚠️ Cette action est
    irréversible ! ». Elle promet une suppression qui, dans le cas le plus fréquent (fournisseur ayant des
    produits), **n'aura pas lieu**.
11. **Suppression concurrente avec une création de produit ?** **Non sûre au sens applicatif — mais la base
    tranche.** Voir §15.
12. **Suppression concurrente avec une modification de produit ?** Idem §15.
13. **Un retry est-il idempotent ?** **Oui, par accident heureux** : au second appel le fournisseur est introuvable
    ⇒ `SupplierFound = false`, aucune écriture, aucune exception. Rejouer est sûr.
14. **Existe-t-il un test SQLite réel ?** Oui — `Delete_ExistingSupplier_RemovesIt` et
    `Delete_MissingSupplier_ReturnsNotFound`, sur vraie base. **Mais le fournisseur supprimé n'a aucun produit.**
    Le cas dangereux n'est couvert par **aucun** test.
15. **Le repository générique permet-il un chemin alternatif ?** **Oui** — `BaseRepository.DeleteAsync(TId)`
    (surcharge par identifiant) est publique, héritée par `SupplierRepository`, et **n'a aucun appelant**. Toute
    garde placée uniquement dans le use case serait contournable par cette surcharge. La FK reste le filet ultime,
    ce qui est acceptable, mais le point mérite d'être connu.

---

## 13. Suppression vs désactivation vs archivage

### Précédents du dépôt

1. **Quel concept pour `Customer` ?** `IsArchived` (P3-2B) — `bool` avec `private set`, muté uniquement par
   `Archive()` / `Reactivate()`, plus `DeleteCustomerUseCase` qui **refuse** la suppression d'un client porteur
   d'historique via `BusinessRuleException(CustomerHasHistoryMessage)` — message stable exposé en constante
   publique.
2. **Quel concept pour `Product` ?** `IsActive` (`bool`, défaut `true`), piloté par `SetProductActive`, et
   filtrage des sélecteurs (`ProductSelectorActiveFilterTests`), plus acquisition atomique `TryAcquireActiveAsync`
   (P3-7).

Le dépôt possède donc **deux motifs éprouvés** : *refus de suppression* et *état de cycle de vie*. `Customer`
utilise les deux ; `Product` aussi.

### Options candidates

| Option | Schéma | Historique | Impact produits | Migration | Risque |
|---|---|---|---|---|---|
| **A** — refus applicatif si produits liés, suppression physique sinon | inchangé | intégralement conservé | aucun | **aucune** | faible — s'appuie sur la FK déjà présente et vérifiée |
| **B** — ajouter `IsActive` | +1 colonne | conservé | filtrage des sélecteurs à ajouter | **oui**, additive | moyen — état à backfiller, nouvelle garde produit, sémantique à définir |
| **C** — ajouter `IsArchived` (calque `Customer`) | +1 colonne | conservé | idem B | **oui**, additive | moyen — identique à B, nom différent |
| **D** — réutiliser un champ existant | inchangé | — | — | aucune | **rejetée** : aucun champ ne porte de sémantique d'état ; détourner `ReferenceCode` serait une invention |
| **E** — supprimer toute suppression | inchangé | conservé | aucun | aucune | **rejetée** : régression fonctionnelle nette, l'écran propose la suppression depuis toujours |
| **F** — passer la FK en `Cascade` ou `SetNull` | migration FK | **détruit** | **orphelins ou destruction en masse** | oui | **rejetée** — contraire à P3-4B, qui a précisément converti les FK produit de `SetNull`/`Cascade` vers `Restrict` |

### Réponses aux 10 questions

3. **Le fournisseur possède-t-il un historique autre que `Product` ?** **Non.** `Supplier.Products` est sa seule
   navigation, et aucune autre table ne référence `Suppliers` (relevé sur `sqlite_master`). C'est un fait
   déterminant : **une seule** relation à protéger, contre plusieurs pour `Customer` (prescriptions + ventes).
4. **Un fournisseur avec uniquement des produits inactifs reste-t-il lié à l'historique ?** **Oui** (§11.11).
5. **La désactivation suffit-elle pour empêcher de nouveaux produits ?** **Non, pas à elle seule.** Un `IsActive`
   sans garde dans `CreateProductUseCase`/`UpdateProductUseCase` ne serait qu'un champ décoratif : le sélecteur UI
   filtrerait, mais une sélection obsolète ou un appel direct passerait. Il faudrait **en plus** une acquisition
   atomique type `TryAcquireActiveAsync`. L'option B/C est donc **strictement plus coûteuse** que son schéma ne le
   laisse croire — colonne + migration + backfill + garde produit + filtrage + tests de course.
6. **Faut-il permettre la réactivation ?** Si et seulement si un état est introduit — `Customer.Reactivate()`
   fournirait le motif. Sans état, la question ne se pose pas.
7. **La suppression physique d'un fournisseur sans produit est-elle acceptable ?** **Oui.** Sans produit lié, il ne
   porte aucun historique (§13.3) : le supprimer ne détruit rien. C'est le comportement actuel, et il est sain.
8. **Quel comportement minimise le changement et respecte P3 ?** **L'option A.** Argumentation :
   - elle atteint l'objectif de sortie de la roadmap — « intégrité fournisseur ↔ produits garantie » — car la seule
     relation existante est `Product`, déjà protégée par une FK `RESTRICT` **effective et vérifiée** ;
   - elle transforme un échec technique incompréhensible en **règle métier explicite**, ce qui est exactement le
     défaut prouvé (§12.8-9) ;
   - elle réutilise un motif **déjà validé** dans le dépôt (`DeleteCustomerUseCase`), sans en inventer un ;
   - elle **ne nécessite aucune migration**, donc aucun backfill, aucune extension du `SqliteSchemaVerifier`,
     aucun risque d'adoption sur base historique ;
   - elle laisse la porte ouverte : si un besoin d'archivage émerge, l'option C reste additive et pourra être
     ajoutée plus tard sans rien défaire.

   L'option C (archivage) ne devient supérieure que si l'on veut **cesser de proposer** un fournisseur sans
   supprimer sa fiche. **Aucun élément du dépôt n'exprime ce besoin** — ni la roadmap, ni un document, ni un
   commentaire de code, ni l'UI. L'introduire en P3-9 reviendrait à inventer un besoin métier, exactement ce que
   §10.7 interdit pour les doublons. Le refuser ici est la même discipline.

9. **Une migration additive serait-elle nécessaire ?** **Uniquement pour B/C.** L'option A n'en requiert aucune.
10. **Des données existantes peuvent-elles être backfillées sans invention ?** Pour B/C, `IsActive = true` /
    `IsArchived = false` serait défendable (tout fournisseur existant est présumé en activité) — mais ce serait
    tout de même **poser un état qu'aucune donnée ne porte**. Argument supplémentaire en faveur de A, qui ne pose
    la question à aucun moment.

---

## 14. Garde lors des écritures produit

Audit de `CreateProductUseCase`, `UpdateProductUseCase`, `ProductValidator`, `ISupplierRepository`.

1. **Le backend charge-t-il le fournisseur ?** **Non**, jamais, dans aucun des deux use cases.
2. **Vérifie-t-il seulement l'existence ?** **Il ne vérifie même pas l'existence.** `ProductValidator` ne porte
   aucune règle sur `SupplierId` (règles présentes : `Name`, `Reference`, `PurchasePrice`, `SalePrice` ×2,
   `RecommendedPrice`, `StockQuantity`, `StockAlertThreshold`).
3. **Une désactivation future nécessiterait-elle une nouvelle garde ?** **Oui** — et c'est le coût caché de
   l'option B/C (§13.5).
4. **La garde devrait-elle être atomique dans la transaction ?** **Oui.** Les deux use cases enveloppent déjà leur
   écriture dans `ITransactionRunner` ; une garde exécutée hors de ce périmètre serait un « check-then-act »
   ouvert. Le précédent P3-7 est explicite sur ce point.
5. **Existe-t-il déjà un pattern d'acquisition active ?** **Oui, deux :**
   `ICustomerRepository.TryAcquireActiveAsync(long customerId)` et
   `IProductRepository.TryAcquireActiveAsync(long productId)` — tous deux introduits en P3-7, tous deux
   provider-neutres (`Task<bool>`), documentés comme devant être suivis d'une relecture explicite plutôt que d'un
   `GetByIdAsync`.
6. **Une sélection UI obsolète peut-elle contourner le filtre ?** **Oui.** `ProductFormViewModel` charge la liste
   via `IListSuppliersUseCase` et la conserve en mémoire ; un fournisseur supprimé sur un autre poste reste
   sélectionnable jusqu'au prochain rechargement. Le filtrage UI n'est jamais une protection — motif déjà établi en
   P3-4B et P3-7.
7. **Un produit historique peut-il conserver un fournisseur désactivé ?** Sans objet aujourd'hui. Sous l'option A,
   la question disparaît : un fournisseur lié ne peut pas être supprimé, donc aucun produit ne peut pointer vers un
   fournisseur absent.
8. **Modifier un autre champ d'un produit historique lié à un fournisseur désactivé doit-il rester possible ?**
   Oui — principe déjà retenu pour les clients archivés (P3-3B) : l'état de l'entité liée ne doit pas geler
   l'édition des autres champs. Pertinent seulement sous B/C.
9. **Changer explicitement vers un fournisseur désactivé doit-il être refusé ?** Oui, sous B/C. Sans objet sous A.
10. **Quel contrat provider-neutre serait minimal ?** Sous l'option A, **une seule** primitive suffit :

    ```csharp
    // ISupplierRepository — vérifie l'existence, ne matérialise aucune entité
    Task<bool> ExistsAsync(long supplierId, CancellationToken cancellationToken = default);
    ```

    (héritée de `IGenericRepository`, donc **déjà disponible** — voir `BaseRepository.ExistsAsync`), plus, pour la
    suppression :

    ```csharp
    // ISupplierRepository — au moins un produit référence ce fournisseur (actif OU inactif)
    Task<bool> HasProductsAsync(long supplierId, CancellationToken cancellationToken = default);
    ```

    Aucune signature n'expose SQL, EF ni SQLite — conforme aux ports P3-8.

**Aucun flux Produit n'a été modifié pendant cet audit.**

---

## 15. Concurrence multi-poste

| Acte | Transaction | CAS / condition | Contrainte DB | Risque résiduel |
|---|---|---|---|---|
| Créer un fournisseur | ❌ `SaveChanges` nu | aucune | aucune (pas d'unicité) | doublon silencieux — **conséquence de l'absence d'unicité, pas de la concurrence** |
| Modifier un fournisseur | ❌ | ❌ aucun jeton | aucune | 🟠 **lost update** — deux postes éditant la même fiche : le dernier écrase l'autre sans avertissement, sur les **cinq** champs |
| Supprimer un fournisseur | ❌ pas de runner | ❌ « check-then-act » | ✅ FK `RESTRICT` | 🔴 **exception technique brute** exposée à l'utilisateur |
| Supprimer vs créer un produit | — | — | ✅ FK | **intégrité sauve** : la FK arbitre. Si le produit est validé d'abord, la suppression échoue ; sinon la création échoue. Dans les deux cas **une exception non traduite** |
| Supprimer vs modifier un produit | — | — | ✅ FK | idem |
| Double suppression | ❌ | — | — | ✅ **idempotent** — 2ᵉ appel ⇒ `SupplierFound = false` |
| Double modification | ❌ | — | — | 🟠 dernier écrivain gagne |
| Désactivation / réactivation | — | — | — | **sans objet** (concept inexistant) |

### Analyse par risque

- **TOCTOU lecture-puis-suppression.** Présent dans `DeleteSupplierUseCase` (§12) et **inévitable** dans toute
  garde applicative — c'est le motif reconnu et documenté dans `DeleteCustomerUseCase` : *« ce contrôle applicatif
  produit un message métier clair mais reste un “check-then-act” »*. La bonne réponse n'est pas de le supprimer,
  mais de **doubler la garde applicative d'un filet base**. Ce filet **existe déjà** ici (FK `RESTRICT`) — c'est
  une position plus confortable que celle de P3-2B.
- **La FK comme dernière protection.** Elle fonctionne, elle est activée (`PRAGMA foreign_keys = 1`), elle est
  vérifiée. **Aucun orphelin, aucune cascade n'est possible.** C'est le point rassurant du domaine.
- **Exception technique exposée.** 🔴 **Défaut prouvé** (§12.8-9). C'est le vrai problème de concurrence : non pas
  une corruption, mais une **erreur illisible** dans un cas parfaitement légitime.
- **Lost update.** 🟠 Défaut prouvé sur `UpdateSupplierUseCase`. À noter : ce risque est **commun à toutes** les
  entités de référence du dépôt et n'a été traité nulle part (aucun `RowVersion`, aucun jeton d'optimisme). Le
  corriger pour le seul fournisseur serait incohérent et hors du plus petit périmètre.
- **Suppression après vérification périmée.** Couverte par la FK.
- **Produit créé avec un fournisseur devenu inactif.** Sans objet sous A.
- **Retry dangereux ?** Non — la suppression est idempotente (§12.13), la création ne l'est pas mais ne corrompt
  rien.

### Séparation exigée

| Catégorie | Constat |
|---|---|
| **Protection déjà acquise** | FK `RESTRICT` effective ; `PRAGMA foreign_keys = 1` ; aucun orphelin possible ; suppression idempotente ; « fournisseur introuvable » distingué proprement ; aucune écriture UI directe |
| **Défaut prouvé** | aucune validation (nom, email) ; aucune garde produits avant suppression ; exception EF/SQLite brute affichée ; `SupplierId ?? 0` produisant une violation FK ; lost update sur `Update` |
| **Limite SQLite** | `HasMaxLength` non imposé (TEXT sans contrainte) ; écritures sérialisées, donc les courses réelles se manifestent en verrou plutôt qu'en entrelacement observable — protocole de test à concevoir séquentiellement, comme en P3-8 §22 |
| **Besoin futur PostgreSQL/SQL Server** | longueurs réellement contraignantes (risque de troncature/rejet sur données historiques longues) ; comportement FK identique ; cf. `adr-prod-db-001` |

---

## 16. Persistance — vérification empirique

Les affirmations centrales de cet audit ont été **exécutées**, non déduites, sur une **vraie base SQLite jetable**
créée par `EnsureCreated()` via `OpticDbContext`, au moyen d'un projet console **hors du dépôt** (répertoire
scratchpad de session). **Aucun fichier du dépôt n'a été créé, modifié ou supprimé.**

```
PRAGMA foreign_keys = 1
FK Products->Suppliers: from=SupplierId to=SupplierId on_delete=RESTRICT
INDEX (Suppliers) : aucun
INDEX idx_products_category_id, idx_products_normalized_reference_unique, IX_Products_SupplierId

[OK  ] Supplier nom VIDE + email invalide persiste
[OK  ] Supplier nom ESPACES SEULS persiste
[OK  ] Supplier nom 5000 car. (MaxLength 200) persiste
[OK  ] Deux fournisseurs nom+ReferenceCode IDENTIQUES persistent
[FAIL] Produit avec SupplierId = 0 (chemin '?? 0')  :: DbUpdateException
[FAIL] SUPPRESSION d'un fournisseur AYANT un produit lie :: DbUpdateException
  -> fournisseur 1 encore present : True
  -> produits pointant encore vers 1 : [P-LIE]
```

**Ce que ces six lignes établissent :**

| Fait | Portée |
|---|---|
| `Name = ""` persiste | aucune obligation de nom côté backend |
| `Name = "   "` persiste | aucune normalisation |
| `Name` de 5000 caractères persiste | `HasMaxLength(200)` **n'impose rien** en SQLite |
| email `"pas-un-email"` persiste | aucune validation email |
| nom **et** `ReferenceCode` dupliqués persistent | aucune unicité, à aucun niveau |
| `SupplierId = 0` ⇒ `DbUpdateException` | le `?? 0` de `Create/UpdateProductUseCase` produit une violation FK, pas une erreur de validation |
| suppression avec produit ⇒ `DbUpdateException`, **fournisseur conservé, produit conservé** | la FK protège réellement ; **aucun orphelin, aucune cascade** ; mais l'échec est technique |

### Seed et données historiques

1. **Combien de fournisseurs dans le seed ?** **25** (`DbInitializer:181-208`).
2. **Tous valides selon les règles candidates ?** **Oui** — 25 noms non vides ≤ 30 caractères, 25 emails
   syntaxiquement valides, 25 `ReferenceCode` distincts, adresses et téléphones renseignés. Introduire la
   validation P3-9 ne rejetterait **aucune** ligne du seed.
3. **Des emails historiques peuvent-ils être invalides ?** Pas dans le seed. Sur une base client réelle, oui —
   puisque rien n'a jamais empêché d'en saisir. C'est un argument fort pour que la validation reste un contrôle
   **pré-écriture** et **non** une contrainte base : une règle applicative n'invalide jamais rétroactivement une
   ligne existante ; elle s'applique à la prochaine modification.
4. **`IsActive`/`IsArchived` avec un défaut sûr ?** Techniquement oui (`defaultValue: true` / `false`).
5. **Le backfill inventerait-il un état ?** Oui, légèrement (§13.10).
6. **Une contrainte FK existe-t-elle déjà ?** Oui, `RESTRICT`, active.
7. **Changer le `DeleteBehavior` nécessite-t-il une migration ?** Oui — et **aucun changement n'est proposé** :
   `Restrict` est déjà le comportement voulu.
8. **Ajouter un index serait-il nécessaire ?** **Non.** `IX_Products_SupplierId` existe déjà et sert exactement la
   requête « ce fournisseur a-t-il des produits ? ». Aucun index fournisseur n'est requis puisqu'aucune unicité
   n'est introduite (§10).
9. **Une migration additive suffirait-elle ?** Pour B/C oui. **Pour A, aucune migration n'est nécessaire.**
10. **Adoption des bases historiques / `SqliteSchemaVerifier` ?** **Sans objet sous l'option A** — aucun élément de
    schéma nouveau, donc aucune entrée à ajouter aux ensembles `AdditiveColumnsToleratedWhenAbsent` /
    `AdditiveUniqueIndexesToleratedWhenAbsent`. Sous B/C, une entrée `Suppliers.IsActive` serait requise, selon le
    mécanisme déjà employé pour `Customers.IsArchived` (P3-2B) et `Products.NormalizedReference` (P3-4B).
11. **Quels états partiels devraient être refusés ?** Sous A, aucun état partiel n'est possible — rien n'est ajouté.
12. **Une solution sans migration est-elle possible ?** **Oui — c'est précisément l'option A**, et c'est
    l'argument décisif en sa faveur.

**Aucune migration n'a été créée pendant cet audit.**

---

## 17. Lectures et filtrage

| Requête | Filtre SQL | Tri SQL | Pagination | Tracking | Usage |
|---|---|---|---|---|---|
| `ListSuppliersUseCase` → `GetAllAsync()` | **aucun** | **aucun** | ❌ | `AsNoTracking` ✅ | liste écran **et** sélecteur du formulaire produit |
| `GetSupplierWithProductsUseCase` → `GetWithProductsAsync` | `SupplierId ==` | tri **en mémoire** (`OrderBy(p => p.Name)` LINQ-to-Objects) | ❌ | ❌ **suivi** (`_context.Suppliers` sans `AsNoTracking`) | fiche détail |
| `GetByNameAsync` | `Name ==` | — | ❌ | `AsNoTracking` | **code mort** |
| `GetByReferenceCodeAsync` | `ReferenceCode ==` | — | ❌ | `AsNoTracking` | **code mort** |
| `ListProductsUseCase` | — | — | — | — | projette `ProductSupplierRefDto` depuis la navigation |

### Réponses aux 7 questions

1. **Les sélecteurs chargent-ils tous les fournisseurs ?** **Oui** — `ProductFormViewModel` consomme
   `IListSuppliersUseCase`, qui renvoie la table entière sans filtre ni tri SQL.
2. **Les fournisseurs supprimés ou inactifs pourraient-ils rester visibles ?** Un fournisseur **supprimé** reste
   visible dans une liste déjà chargée jusqu'au prochain rechargement (§14.6). « Inactif » n'existe pas.
3. **La liste distingue-t-elle les fournisseurs liés à des produits ?** **Non.**
4. **Existe-t-il un compteur de produits ?** L'UI **prétend** en afficher un (« {0} ref. » lié à
   `Products.Count`), mais `SupplierListItemDto.Products` est **toujours vide** — le compteur affiche donc **0**
   pour tous. Le DTO le documente explicitement comme iso-fonctionnel P2D-2. **Fait notable** : si P3-9 devait un
   jour renseigner ce compteur, l'écran l'afficherait immédiatement sans aucune modification UI — mais ce serait un
   **changement de valeur observable**, donc hors du plus petit périmètre.
5. **Le filtrage est-il en SQL ou en mémoire ?** **En mémoire** — recherche et pagination sont portées par
   `SuppliersListViewModel`, sur la liste complète. Acceptable à l'échelle actuelle (25 lignes de seed) ; c'est une
   dette 🟡, pas un défaut.
6. **Une future désactivation nécessiterait-elle un `IncludeInactive` ?** **Oui** — le sélecteur devrait exclure les
   inactifs tandis que l'écran de gestion devrait les montrer. Deux besoins divergents sur une seule requête :
   coût supplémentaire de l'option B/C.
7. **L'UI peut-elle rester source-compatible sans modification ?** **Oui sous l'option A**, à une réserve près :
   `DeleteSupplierUseCase` lèverait une `BusinessRuleException` porteuse d'un message métier, que le
   `catch (Exception ex)` existant afficherait **déjà** via `ex.Message` — sans aucune modification de
   `SuppliersViewModel`. L'écran passerait mécaniquement d'un message EF anglais à un message métier français.
   **L'amélioration UI est obtenue sans toucher à l'UI** — c'est ce qui rend l'option A compatible avec
   l'interdiction absolue de modifier l'UI en P3-9.

---

## 18. Tests existants

| Fichier | Niveau | Tests | Provider réel |
|---|---|---:|---:|
| `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierUseCasesTests.cs` | Application (écriture) | 7 | ✅ SQLite fichier |
| `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierQueryUseCasesTests.cs` | Application (lecture) | 6 | ✅ SQLite fichier |
| `tests/MMV.App.Tests/ViewModels/SupplierFormViewModelDelegationTests.cs` | UI | — | ❌ **non comptabilisé** |

Détail (`SupplierUseCasesTests`) : `Create_PersistsSupplier`, `Update_ExistingSupplier_AppliesChanges`,
`Update_MissingSupplier_ReturnsNotFound_NoWrite`, `Delete_ExistingSupplier_RemovesIt`,
`Delete_MissingSupplier_ReturnsNotFound`, `NullCommand_Throws`, `Constructors_RejectNullDependencies`.

### Couverture réelle

| Sujet | Couvert ? |
|---|---|
| Création nominale | ✅ |
| Validation nom | ❌ **aucun test** (aucune règle à tester) |
| Validation email | ❌ |
| Email vide | ❌ |
| Modification | ✅ |
| Introuvable (update / delete) | ✅ |
| **Suppression sans produit** | ✅ |
| **Suppression avec produit** | ❌ **le cas dangereux n'est testé nulle part** |
| FK | ❌ aucun test ne l'exerce |
| Concurrence | ❌ |
| Désactivation / réactivation | ❌ (sans objet) |
| Sélecteurs | ❌ aucun équivalent de `ProductSelectorActiveFilterTests` |
| Migration | ❌ (sans objet — aucune migration fournisseur postérieure à `InitialCreate`) |

**Aucun test UI n'est compté comme protection backend**, conformément à l'énoncé.

---

## 19. Trous de couverture

| Règle ou risque | Test existant | Suffisant ? | Test manquant |
|---|---|---:|---|
| Nom vide | aucun | ❌ | `Create`/`Update` avec `Name = ""` ⇒ erreur de validation, **aucune écriture** |
| Nom espaces seuls | aucun | ❌ | `Name = "   "` ⇒ rejeté (ou normalisé puis rejeté) |
| Email valide | aucun | ❌ | email correct ⇒ accepté et persisté tel quel |
| Email invalide | aucun | ❌ | `"pas-un-email"` ⇒ erreur de validation, aucune écriture |
| Email absent | aucun | ❌ | `null` **et** `""` ⇒ acceptés (facultatif) |
| Champs trop longs | aucun | ❌ | `Name` > 200, email > 254 ⇒ rejetés **côté Application** (la base ne les rejette pas) |
| Fournisseur introuvable | ✅ ×2 | ✅ | — |
| Création nominale | ✅ | ✅ | — |
| Modification nominale | ✅ | ✅ | — |
| Suppression sans produit | ✅ | ✅ | — |
| **Suppression avec produit actif** | aucun | ❌ | ⇒ **message métier stable**, fournisseur **conservé**, produit **conservé**, **aucune** `DbUpdateException`/`SqliteException` exposée |
| **Suppression avec produit inactif** | aucun | ❌ | même résultat — l'inactivité ne doit **pas** autoriser la suppression (§11.11) |
| **FK comme filet** | aucun | ❌ | suppression forcée hors use case ⇒ la base refuse (verrouille la protection contre une régression de configuration) |
| Course suppression / création produit | aucun | ❌ | deux connexions distinctes ; l'une supprime, l'autre crée un produit ⇒ **jamais** d'orphelin ; erreur traduite des deux côtés |
| Course suppression / modification produit | aucun | ❌ | idem |
| Retry suppression | aucun | ❌ | 2ᵉ appel ⇒ `SupplierFound = false`, aucune écriture, aucune exception |
| Produit avec `SupplierId` inexistant | aucun | ❌ | `Create`/`UpdateProduct` avec un id inconnu ⇒ **erreur de validation**, jamais une `PersistenceException` |
| Produit avec `SupplierId = null` | aucun | ❌ | verrouille le remplacement du `?? 0` |
| Neutralité provider du port | aucun | ❌ | aucune signature de `ISupplierRepository` n'expose SQL/EF/SQLite (garde d'architecture, motif P3-8) |
| Double désactivation / réactivation | — | — | **sans objet sous l'option A** |
| Filtrage des sélecteurs | — | — | **sans objet sous l'option A** |
| Données historiques | — | — | **sans objet** — aucune migration |

**Estimation : ~18 à 24 tests**, tous à provider SQLite réel, aucun sur EF InMemory — conformément au protocole
établi de P3-4B à P3-8.

---

## 20. Risques classés

### 🔴 Critique — uniquement ce qui est **prouvé**

**🔴-1 — Exception technique brute exposée à l'utilisateur lors de la suppression d'un fournisseur lié.**
- *Preuve* : `DeleteSupplierUseCase.cs` (aucun `try/catch`, aucun `ITransactionRunner`) +
  `SuppliersViewModel.cs:194-198` (`ShowErrorAsync($"…{ex.Message}")`) + relevé empirique §16
  (`DbUpdateException`).
- *Scénario* : l'opticien ouvre la fiche « Essilor France », clique Supprimer, confirme « ⚠️ irréversible ! », et
  reçoit *« An error occurred while saving the entity changes. See the inner exception for details. »*
- *Impact* : action légitime, refus correct, **explication nulle**. L'utilisateur ignore que la cause est
  « ce fournisseur a des produits » et ne sait pas quoi faire. Violation directe de `adr-application-boundaries`.
- *Propriétaire de couche* : Application (`DeleteSupplierUseCase`).
- *Étape cible* : **P3-9 obligatoire**.

**🔴-2 — Aucune validation métier sur `Supplier` : nom vide, espaces seuls, longueur illimitée, email invalide sont persistés.**
- *Preuve* : absence de `SupplierValidator` ; absence de `CommandValidation` dans les deux use cases ; §16 (quatre
  écritures aberrantes acceptées).
- *Scénario* : un fournisseur sans nom apparaît dans la liste et dans le sélecteur du formulaire produit comme une
  ligne vide, sélectionnable, indistinguable.
- *Impact* : données de référence corrompues, sans aucun filet — ni base, ni Domain, ni Application.
- *Propriétaire de couche* : Domain (règle) + Application (application de la règle).
- *Étape cible* : **P3-9 obligatoire**.

**🔴-3 — `SupplierId ?? 0` dans `CreateProductUseCase:78` et `UpdateProductUseCase:68`.**
- *Preuve* : les deux lignes + §16 (`DbUpdateException` sur `SupplierId = 0`).
- *Scénario* : un produit est soumis sans fournisseur (sélecteur vide, ou sélection sur un fournisseur supprimé
  entre-temps) ⇒ écriture d'un `0` ⇒ violation FK ⇒ `PersistenceException` non traduite.
- *Impact* : une donnée manquante devient une erreur d'intégrité au lieu d'une erreur de saisie. C'est le
  **symétrique exact** de 🔴-1, côté produit.
- *Propriétaire de couche* : Application.
- *Étape cible* : **P3-9 obligatoire** — c'est la moitié « produits » de l'objectif « intégrité fournisseur ↔
  produits ».

> **Ce qui n'est *pas* 🔴, et doit être dit clairement.** Il n'y a **ni suppression destructive, ni produit
> orphelin, ni cascade inattendue**. La FK `RESTRICT` est effective et vérifiée. Le domaine Fournisseur souffre
> d'un défaut de **lisibilité et de validation**, pas d'intégrité. C'est une différence de nature importante par
> rapport à P3-4B ou P3-7, et elle **réduit** le périmètre nécessaire de P3-9.

### 🟠 Important

| # | Constat | Preuve | Étape cible |
|---|---|---|---|
| 🟠-1 | Aucune vérification de produits liés avant suppression | `DeleteSupplierUseCase` | P3-9 |
| 🟠-2 | `DeleteBehavior.SetNull` mort et trompeur dans `SupplierConfiguration:36`, contredit par `ProductConfiguration:77` | snapshot + `PRAGMA foreign_key_list` | P3-9 (retrait sans effet fonctionnel) |
| 🟠-3 | Lost update sur `UpdateSupplierUseCase` (5 champs écrasés inconditionnellement) | `UpdateSupplierUseCase:57-61` | **Report** — dette transverse à toutes les entités |

---

## Note de clôture — référence à l'implémentation finale

> Ajout minimal post-implémentation. **L'historique de l'audit ci-dessus n'est pas réécrit** : les constats 🔴-1,
> 🔴-2, 🔴-3 et 🟠-1/🟠-2 restent la description exacte de l'état **avant** P3-9.

- **Implémentation finale** : voir
  [rapport d'implémentation P3-9](P3-9-suppliers-business-rules-implementation-report.md), qui couvre les trois
  défauts 🔴 (option A, `SupplierValidator`, garde fournisseur obligatoire dans `CreateProductUseCase` /
  `UpdateProductUseCase`) et 🟠-1/🟠-2 (suppression atomique conditionnelle, retrait du `SetNull` trompeur). 🟠-3
  (lost update) reste **reporté**, comme prévu, en dette transverse.
- **Revue ciblée avant commit** (section dédiée du rapport d'implémentation) a testé directement le SQL produit
  par la suppression conditionnelle, l'état du change tracker après `ExecuteDeleteAsync`, et la classification des
  violations de contrainte dans les use cases produit. Un défaut concret **non anticipé par cet audit** y a été
  trouvé et corrigé : une entité `Supplier` chargée puis supprimée dans la **même** portée `DbContext` restait
  ressuscitable par un `FindAsync` ultérieur sur ce même contexte (le change tracker n'est jamais synchronisé par
  `ExecuteDeleteAsync`). Corrigé par un détachement ciblé sur l'identifiant supprimé, sans toucher aucune autre
  entité suivie. Voir la revue pour le détail et les tests.
- **Verdict final** : `P3-9 = GO LOCAL`, sous réserve de la CI du commit.
| 🟠-4 | Aucun `ITransactionRunner` ⇒ aucune traduction de `PersistenceException` | 3 use cases d'écriture | P3-9 (suppression) |
| 🟠-5 | `BaseRepository.DeleteAsync(TId)` offre un chemin de suppression alternatif sans garde | `BaseRepository.cs` | Report (la FK reste le filet) |
| 🟠-6 | Filtre UI seul (sélecteur fournisseur en mémoire, jamais rafraîchi) | `ProductFormViewModel` | Report (UI) |
| 🟠-7 | `ProductValidator` ne porte aucune règle sur `SupplierId` | `ProductValidator.cs` | P3-9 (lié à 🔴-3) |

### 🟡 Dette

| # | Constat |
|---|---|
| 🟡-1 | Aucune normalisation `Trim` sur les cinq champs |
| 🟡-2 | `""` et `NULL` coexistent comme « absence d'email » |
| 🟡-3 | `Address` non structurée (chaîne libre) |
| 🟡-4 | `Phone` sans aucun format ni validation |
| 🟡-5 | Aucune pagination ni tri SQL ; recherche en mémoire |
| 🟡-6 | Aucun `CreatedAt` / `UpdatedAt` sur `Supplier` |
| 🟡-7 | Aucun audit utilisateur (« qui a modifié ce fournisseur ») |
| 🟡-8 | `GetByNameAsync` et `GetByReferenceCodeAsync` : **code mort** |
| 🟡-9 | `SupplierListItemDto.Products` toujours vide ⇒ compteur « 0 ref. » permanent à l'écran |
| 🟡-10 | `HasMaxLength` déclaré mais non imposé — deviendra contraignant sur provider serveur |
| 🟡-11 | Aucune fusion de doublons possible |
| 🟡-12 | `GetWithProductsAsync` renvoie une entité **suivie** (seul repository fournisseur sans `AsNoTracking`) |

### 🔵 Report explicite

Commandes fournisseur réelles ; catalogue fournisseur ; tarifs négociés ; délais de livraison ; contacts multiples ;
toute évolution UI fournisseur ; email automatique ; multi-magasin ; audit utilisateur → **P3-10** ; provider
serveur et longueurs réellement contraignantes → **chantier production** (`adr-prod-db-001`).

---

## 21. Plan candidat P3-9

Le plus petit plan backend réaliste, fondé sur l'**option A** (§13.8) : **refus de suppression + validation, sans
migration**.

### Obligatoire P3-9

| # | Action | Motif |
|---|---|---|
| 1 | **`SupplierValidator`** (`src/MMV.Domain/Validators/`) calqué sur `CustomerValidator` : `Name` `NotEmpty` + `MaximumLength(200)` ; `ContactEmail` `EmailAddress().When(!IsNullOrWhiteSpace)` + `MaximumLength(254)` ; `Phone` `MaximumLength(20)` ; `Address` `MaximumLength(500)` ; `ReferenceCode` `MaximumLength(50)`. Messages explicites via `.WithMessage`. | 🔴-2 ; réutilise le pattern existant, aucune regex maison |
| 2 | Appliquer le validateur dans `CreateSupplierUseCase` **et** `UpdateSupplierUseCase` via `CommandValidation`, **avant toute écriture** | 🔴-2, convention P3-1 |
| 3 | Étendre `CreateSupplierResult` / `UpdateSupplierResult` d'un `ValidationErrors` (**additif**, aucune ViewModel ne le consomme aujourd'hui) | convention P3-1 |
| 4 | **`Trim`** sur les cinq champs, et `""` → `null` pour les quatre champs optionnels | 🟡-1, 🟡-2 ; **indispensable** : sans `Trim`, `" a@b.fr "` échouerait la validation (§9.5) |
| 5 | Ajouter `Task<bool> HasProductsAsync(long supplierId, …)` à `ISupplierRepository` + implémentation `AnyAsync` en SQL (provider-neutre, sans matérialisation) | 🟠-1 |
| 6 | `DeleteSupplierUseCase` : après la lecture, refuser via `BusinessRuleException(SupplierHasProductsMessage)` — **constante publique**, calque exact de `DeleteCustomerUseCase.CustomerHasHistoryMessage`. Compter les produits **actifs et inactifs**. | 🔴-1, 🟠-1, §11.11 |
| 7 | Envelopper la suppression dans `ITransactionRunner` et convertir `PersistenceException` de catégorie `ConstraintViolation` en **le même message métier stable** que la garde pré-écriture — motif identique à `CreateProductUseCase` pour `UniqueConstraint`. Ferme le TOCTOU (§15). | 🔴-1, 🟠-4 |
| 8 | Remplacer `SupplierId ?? 0` dans `CreateProductUseCase` et `UpdateProductUseCase` par une **garde d'existence** (`ISupplierRepository.ExistsAsync`, déjà héritée de `IGenericRepository`) renvoyant un `ValidationError` stable ; `SupplierId` absent ⇒ erreur de validation, jamais un `0`. Garde exécutée **dans** le `ITransactionRunner` existant. | 🔴-3, 🟠-7 |
| 9 | Retirer le `DeleteBehavior.SetNull` mort de `SupplierConfiguration` — **vérifier que `has-pending-model-changes` reste vide**, le modèle effectif étant déjà `Restrict` | 🟠-2 |
| 10 | **~18-24 tests** (§19), tous sur SQLite réel, dont : suppression avec produit **actif** et **inactif** ; FK comme filet ; course suppression/création produit sur **deux connexions distinctes** (protocole séquentiel P3-8 §22, sans `Thread.Sleep`) ; retry idempotent ; `SupplierId` inexistant et `null` ; validation nom/email/longueurs ; garde d'architecture provider-neutre du port | §19 |
| 11 | Rapport `docs/implementation/P3-9-suppliers-business-rules-implementation-report.md` | convention P3 |

**Aucune migration. Aucune modification de `SqliteSchemaVerifier`. Aucun fichier sous `src/MMV.App/**`.**

L'écran de suppression **s'améliore sans être touché** : le `catch (Exception ex)` existant affichera le message
métier français au lieu du message EF anglais (§17.7).

### Reportable

| Sujet | Motif |
|---|---|
| `IsActive` / `IsArchived` fournisseur | **Aucun besoin métier prouvé** (§13.8) ; coût réel = colonne + migration + backfill + garde produit + filtrage sélecteur + tests de course. L'option A garantit déjà l'intégrité. Reste **additive** si le besoin émerge. |
| Unicité nom / email / téléphone / `ReferenceCode` | **Serait inventée** (§10.7-8) |
| Lost update (`RowVersion`) | Dette transverse à toutes les entités ; la traiter pour le seul fournisseur serait incohérent |
| Compteur de produits dans la liste | Changerait une **valeur observable** par l'UI ; hors du plus petit périmètre (§17.4) |
| Suppression de `GetByNameAsync` / `GetByReferenceCodeAsync` | Code mort, sans risque ; retrait opportuniste seulement s'il ne coûte rien |
| Pagination / tri SQL, `Trim` téléphone, `Address` structurée, timestamps | 🟡 |
| UI fournisseur, commandes fournisseur, catalogue, tarifs, contacts multiples, fusion de doublons | 🔵 |
| Audit utilisateur | **P3-10** |
| Provider serveur, longueurs contraignantes | Chantier production (`adr-prod-db-001`) |

**Rien n'a été implémenté.** Aucune sous-phase officielle n'est inventée : le plan tient entièrement dans P3-9.

---

## 22. Reports explicites

| # | Sujet | Motif | Destination |
|---|---|---|---|
| R1 | Toute évolution UI fournisseur | Interdiction absolue de modifier l'UI en P3-9 | Redesign global UI |
| R2 | `IsActive` / `IsArchived` fournisseur | Aucun besoin métier prouvé ; l'option A garantit l'intégrité sans migration | Redesign métier futur |
| R3 | Unicité `Name` / `ContactEmail` / `Phone` / `ReferenceCode` | Serait inventée (§10) | Futur, sur preuve métier |
| R4 | Fusion de doublons fournisseurs | Dépend de R3 | Futur |
| R5 | Commandes fournisseur réelles, catalogue, tarifs négociés, délais | Hors P3 (roadmap §481) | Hors P3 |
| R6 | Contacts multiples par fournisseur | Modèle mono-contact ; exigerait une nouvelle table | Redesign métier futur |
| R7 | `Address` structurée (rue / ville / CP / pays) | Aucun besoin exprimé ; migration de données | Futur |
| R8 | Validation / normalisation du format téléphone | Aucune règle métier ni pays de référence documenté | Futur |
| R9 | `CreatedAt` / `UpdatedAt` sur `Supplier` | Migration additive sans besoin exprimé | Futur |
| R10 | Audit utilisateur (« qui a modifié ») | Modèle sans champ utilisateur | **P3-10** |
| R11 | Lost update / `RowVersion` (🟠-3) | Dette transverse à toutes les entités du dépôt | Futur, transverse |
| R12 | Pagination et tri SQL des fournisseurs (🟡-5) | 25 lignes ; recherche en mémoire acceptable | Futur |
| R13 | Compteur de produits dans la liste (🟡-9) | Changerait une valeur observable par l'UI | Redesign global UI |
| R14 | `BaseRepository.DeleteAsync(TId)` non gardé (🟠-5) | Refactor transverse ; la FK reste le filet ultime | Futur |
| R15 | `GetWithProductsAsync` sans `AsNoTracking` (🟡-12) | Correction isolée hors périmètre P3-9 | Futur |
| R16 | Longueurs réellement contraignantes, index filtrés, provider serveur | `adr-prod-db-001` | Chantier production |
| R17 | Email automatique / notifications fournisseur | Hors P3 | Hors P3 |
| R18 | Multi-magasin | Hors P3 | Hors P3 |

---

## 23. Validation documentaire

```
git diff --check   → (vide)
git diff --stat    → (vide — aucun fichier suivi modifié)
git status --short → ?? design-handoff/
                     ?? design/
                     ?? docs/ui/
                     ?? docs/implementation/P3-9-suppliers-business-rules-audit-report.md
```

✅ Le **seul** nouveau fichier P3-9 est
`docs/implementation/P3-9-suppliers-business-rules-audit-report.md`.
✅ Aucun fichier Domain, Application, Infrastructure, test, migration ou UI modifié.
✅ Aucune autre documentation mise à jour.
✅ Le projet de vérification empirique (§16) a été créé **hors du dépôt**, dans le scratchpad de session.

---

## 24. Verdict

| Critère (§24 de l'énoncé) | Statut |
|---|---|
| Toutes les écritures fournisseur cartographiées | ✅ §7 — 3 use cases, 2 sites produit, seed, migrations, 3 chemins morts |
| Validation email comprise | ✅ §9 — `CustomerValidator` réutilisable, portée réelle d'`EmailAddress()` énoncée sans surestimation |
| Relation produit/fournisseur prouvée | ✅ §11 — `long` non nullable, FK `RESTRICT`, contradiction de configuration tranchée par trois sources concordantes |
| Comportement de suppression prouvé | ✅ §12 + §16 — échec FK, aucun orphelin, `DbUpdateException` affichée à l'utilisateur |
| Option suppression / désactivation cadrée | ✅ §13 — six options, option A argumentée et retenue, B/C reportées faute de preuve |
| Courses multi-poste analysées | ✅ §15 — protection acquise / défaut prouvé / limite SQLite / besoin serveur séparés |
| Migration éventuelle décidée précisément | ✅ §16 — **aucune migration nécessaire**, et pourquoi |
| Tests manquants listés | ✅ §19 — ~18-24 tests, SQLite réel |
| Plan candidat précis | ✅ §21 — 11 actions obligatoires, reportable séparé |
| Rapport `.md` existant | ✅ `docs/implementation/P3-9-suppliers-business-rules-audit-report.md` |
| Aucun code modifié | ✅ §23 — `git diff --stat` vide |

---

# **P3-9 AUDIT = GO**

**Aucun commit. Aucun push. Aucune UI. STOP.**

---

## Décisions retenues pour l'implémentation

> Section ajoutée **après** l'audit, au moment de l'implémentation P3-9. Elle fige les décisions effectivement
> appliquées et **corrige** les points où le code réel a contredit l'audit.

### Décisions appliquées

| # | Décision | Portée |
|---|---|---|
| 1 | **Option A retenue** (§13.8) — refus de suppression + validation | Aucune option B/C/D/E/F |
| 2 | **Aucune désactivation** — ni `IsActive`, ni `IsArchived`, ni réactivation, ni filtre de sélecteur | Report R2 |
| 3 | **Aucune migration** — schéma, snapshot et migrations existantes strictement inchangés | `has-pending-model-changes` vide |
| 4 | **Aucune unicité** — ni `Name`, ni `ContactEmail`, ni `Phone`, ni `ReferenceCode`, ni combinaison | Report R3 |
| 5 | **Normalisation des cinq champs** par un propriétaire unique (`SupplierInputNormalizer`), partagé par Create et Update | `Trim` ; `""` → `null` pour les 4 champs optionnels |
| 6 | **Validation Domain** (`SupplierValidator`), appliquée via `CommandValidation` **avant** toute écriture | Messages stables en constantes publiques |
| 7 | **Suppression conditionnelle atomique** (`TryDeleteIfUnusedAsync`) — la condition et l'écriture sont la même instruction SQL | Ferme le TOCTOU, ne le documente pas |
| 8 | **FK `RESTRICT` conservée** comme filet ultime ; `DeleteBehavior.SetNull` mort retiré de `SupplierConfiguration` | Aucun changement de modèle |
| 9 | **Fournisseur obligatoire et existant** dans `CreateProduct` / `UpdateProduct` ; `SupplierId ?? 0` supprimé | Vérification **dans** la transaction |
| 10 | **Courses traduites en erreurs métier** — aucune `DbUpdateException` / `SqliteException` n'atteint l'appelant | Messages stables identiques aux gardes |
| 11 | **UI reportée** — aucun fichier sous `src/MMV.App/**` | L'écran s'améliore sans être touché |
| 12 | **Lost update non traité** — dette transverse à toutes les entités | Report R11 |
| 13 | **Code mort conservé** — `GetByNameAsync`, `GetByReferenceCodeAsync`, `BaseRepository.DeleteAsync(TId)` | Report R14 |

### Corrections apportées à l'audit par le code réel

L'audit affirmait deux choses que l'implémentation a **réfutées empiriquement**. Elles sont corrigées ici plutôt
que laissées telles quelles, conformément au principe « le code réel prime ».

| Affirmation de l'audit | Réalité vérifiée | Conséquence |
|---|---|---|
| **§9.5** — « sans `Trim`, `" a@b.fr "` **échouerait** `EmailAddress()` ; introduire la validation sans normalisation transformerait une saisie bénigne en erreur bloquante » | **Faux.** FluentValidation 11 utilise `EmailValidationMode.AspNetCoreCompatible`, qui tolère les espaces environnants : `" contact@essilor.fr "` est **accepté**. Prouvé par `SupplierValidatorTests.EmailEntoureDEspaces_EstAccepteAvantCommeApresNormalisation` | La normalisation reste **retenue**, mais pour ses raisons réelles : ne pas persister d'espaces parasites, unifier `""` et `NULL`, et mesurer les longueurs sur le contenu réel. L'argument « sinon ça bloque » est retiré |
| **§21 action 5** — plan fondé sur une garde `HasProductsAsync` **puis** `DeleteAsync` | Ce couple resterait un « check-then-act » : un produit créé entre les deux passerait inaperçu | Remplacé par la primitive **atomique** `TryDeleteIfUnusedAsync` (condition + écriture en une instruction). La lecture de diagnostic qui subsiste ne décide **jamais** de l'écriture |

Un troisième point mérite d'être noté, non comme une erreur de l'audit mais comme une **précision obtenue à
l'écriture des tests** : l'état « un produit référence un fournisseur inexistant » est **infabricable**, y compris
en contournant entièrement la couche Application. La FK le refuse à l'insertion, et EF refuse même de détacher la
relation (requise, non nullable). L'audit décrivait ce risque comme théorique ; il est en réalité **impossible**.

### Écart assumé avec le plan candidat

L'audit §21 action 7 prévoyait de convertir « toute `PersistenceException` de catégorie contrainte » en message
métier. L'implémentation est **plus stricte** : `PersistenceErrorMapper` classe **toute** contrainte SQLite (FK,
`NOT NULL`, `CHECK`) en `ConstraintViolation`. Convertir aveuglément aurait étiqueté « fournisseur introuvable »
une violation sans aucun rapport — reproduisant à l'envers le défaut que P3-9 corrige. Côté produit, la traduction
n'a donc lieu **que si une lecture fraîche confirme** que le fournisseur a réellement disparu ; sinon l'exception
remonte inchangée. Verrouillé par
`ProductSupplierGuardTests.UneViolationDeContrainteSansRapport_NEstPasEtiqueteeFournisseurIntrouvable`.
