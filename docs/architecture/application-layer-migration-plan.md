# Plan de migration — Couche Application (P2B)

> **Plan de cadrage (P2B-2A) — AUCUNE implémentation dans cette phase.** Décrit l'ordre de création, les
> règles de dépendances, le premier use case, la stratégie de migration de `SaleFormViewModel`, la stratégie
> de tests, les critères GO/NO-GO et les anti-patterns. **Aucune ligne de la couche Application n'est écrite
> ici.** Référence : [adr-application-boundaries.md](adr-application-boundaries.md),
> [migration-roadmap §Étape 2](migration-roadmap.md), [ADR-001](adr-candidates.md#adr-001).
>
> Principe directeur : **strangler pattern**. On crée la couche, puis on migre **un parcours à la fois** ;
> le produit reste **utilisable à chaque étape**. **Pas de big bang.** **Aucune règle métier modifiée.**

Date : 12 juin 2026. Branche : `p2b-architecture`.

---

## 1. Ordre de création des projets / dossiers

### 1.1 Nouveau projet (P2B-2B)

`src/MMV.Application/MMV.Application.csproj` — `classlib`, `net8.0`, `Nullable=enable`,
`ImplicitUsings=enable`, `LangVersion=12.0` (cohérent avec les autres projets).

- **Référence projet** : `..\MMV.Domain\MMV.Domain.csproj` **uniquement**.
- **Packages autorisés** : *aucun a priori* ; `FluentValidation` **autorisé** (déjà dans Domain) si la
  validation de commande l'utilise.
- **Packages interdits** : `Microsoft.EntityFrameworkCore*`, `Microsoft.Data.Sqlite`, `Avalonia*`,
  `CommunityToolkit.Mvvm`, toute référence à `MMV.Infrastructure` ou `MMV.App`.
- **Ajout à `MMV.sln`** ; **référence depuis `MMV.App`** (`MMV.App` → `MMV.Application`).

### 1.2 Arborescence cible de `MMV.Application`

```
MMV.Application/
  Abstractions/        # (optionnel) interfaces de use cases : IRegisterSaleUseCase, ...
  UseCases/
    Sales/
      RegisterSale/
        RegisterSaleCommand.cs      # entrée (DTO)
        RegisterSaleResult.cs       # sortie (DTO)
        RegisterSaleUseCase.cs      # orchestration (appelle ITransactionRunner + ports + repos)
        RegisterSaleValidator.cs    # (optionnel) validation de commande (FluentValidation)
  Common/
    Result.cs                       # (optionnel) type de résultat explicite, si adopté
  DependencyInjection.cs            # AddApplication(this IServiceCollection) — enregistre les use cases
```

> Convention « dossier par use case » (verbe + agrégat). Elle **prépare** un CQRS léger : un
> `Command`/`Result`/`UseCase` deviendra, sans rupture, `Command`/`Handler` si un dispatcher est introduit.

### 1.3 Ce qui **ne bouge pas**

- Les **ports** (`ITransactionRunner`, `IStockMutationService`, `INumberSequenceService`,
  `I*Repository`, `IUnitOfWork`) restent dans `MMV.Domain.Interfaces`.
- Les **implémentations** (`Ef*`, repos, `UnitOfWork`, `OpticDbContext`) restent dans `MMV.Infrastructure`.
- Les **entités/VO/enums/exceptions/validators** restent dans `MMV.Domain`.

---

## 2. Règles de dépendances (à faire respecter)

1. `MMV.Domain` : **0 référence projet**, **0 package EF/Avalonia** (préserver l'atout « domaine pur »).
2. `MMV.Application` → `MMV.Domain` **seulement**. **Interdits** : EF, SQLite, Avalonia, MVVM, Infrastructure, App.
3. `MMV.Infrastructure` → `MMV.Domain`. **Ne référence pas** `MMV.Application` (aucun port défini dans
   Application n'est requis par l'Infrastructure aujourd'hui).
4. `MMV.App` → `MMV.Domain`, `MMV.Application`, `MMV.Infrastructure`. **Seul** lieu des types **concrets**
   d'Infrastructure (composition root). Les **ViewModels** ne référencent que `MMV.Application` + `MMV.Domain`.
5. **Sens unique** : UI → Application → Domain ← Infrastructure. Aucune dépendance ne remonte.
6. **Garde-fou recommandé (non bloquant P2B)** : un test d'architecture (ex. inspection d'assembly) qui
   échoue si `MMV.Application` charge un type EF/Avalonia, ou si un ViewModel référence `I*Repository`.

---

## 3. Premier use case cible

### 3.1 Use case retenu : **`EnregistrerVente` (vente en magasin)**

Conformément à la [roadmap §7](migration-roadmap.md) et à l'instruction de phase. Il **traverse** les
coutures P2A (transaction, numérotation, décrément de stock) **sans dépendre d'aucune gate réglementaire**,
et **remplace le point le plus risqué** : [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L909-L1057).

> **Le use case `EnregistrerVente` n'est PAS implémenté en P2B-2A.** Il est la cible de P2B-2C (premier
> vertical slice), après la création du projet (P2B-2B).

### 3.2 Contrat envisagé (esquisse, non normative)

```
RegisterSaleCommand:  CustomerId, IList<SaleLine> Lines (ProductId, ItemType, Quantity, UnitPrice,
                      paramètres optiques OD/OG), DiscountAmount, DepositAmount, PaymentMethod, IsCounterSale, Notes
RegisterSaleResult:   SaleId, SaleNumber, OrderNumber?, Status, FinalAmount, RemainingAmount
Comportement:         dans ITransactionRunner.RunAsync :
                        1) NextNumberAsync(SALE) ;
                        2) construire Sale + SaleItems ; déterminer Status ; SaveChanges (obtenir SaleId) ;
                        3) si verres : NextNumberAsync(ORDER) + créer Order fournisseur ;
                        4) si comptoir : DecrementStockAsync (hors verres) + StockMovement ;
                        5) SaveChanges ; renvoyer RegisterSaleResult.
Erreurs:              InsufficientStockException / NumberSequenceException / PersistenceException (inchangées).
```

> **Périmètre strictement conservé** : aucune TVA, aucune devise `Money`, aucun magasin, aucun exercice ;
> `SaleDate`/`DateTime.Now` inchangé (l'horloge injectable est une fondation d'Étape 4). Le comportement
> métier observable est **identique** à `PersistSaleAsync` actuel — c'est un **déplacement**, pas une refonte.

### 3.3 Vente optique complète (différée)
La « vente optique complète » (ordonnance, centrage, verres détaillés, paiements multiples, facture) **n'est
pas** le premier use case. Elle viendra après les fondations (Étapes 3/4/6/8) et **ne doit pas** être
commencée en P2B.

---

## 4. Stratégie de migration de `SaleFormViewModel`

**Strangler en 5 temps, sans rupture fonctionnelle** (exécutés en P2B-2C, pas avant) :

1. **Créer** `RegisterSaleUseCase` dans `MMV.Application` en **copiant** la logique de `PersistSaleAsync`
   (mêmes ports, mêmes entités, même transaction). Le use case est **couvert par tests** (vrai SQLite +
   chemins d'erreur) **avant** d'être branché.
2. **Brancher** par DI : `services.AddApplication()` enregistre le use case (portée `Scoped`, **même portée**
   que `OpticDbContext`/repos/runner, condition de partage de transaction). Injecter le use case dans
   `SaleFormViewModel` (via la chaîne `CustomersViewModel → CustomerDetailViewModel → SaleFormViewModel`,
   comme les primitives P2A le sont déjà — paramètre **obligatoire**, constructeur rejette `null`).
3. **Déléguer** : `ExecuteSave` construit une `RegisterSaleCommand` à partir de l'état VM, appelle
   `useCase.ExecuteAsync(command)`, mappe le `RegisterSaleResult`/les exceptions en `ErrorMessage`/notification.
   La garde `IsSaving` (P2A-1C) **reste dans la VM** (présentation).
4. **Retirer** de `SaleFormViewModel` les dépendances `ISaleRepository`, `IOrderRepository`,
   `IProductRepository`, `IStockMovementRepository`, `IUnitOfWork`, `INumberSequenceService`,
   `IStockMutationService`, `ITransactionRunner` **du flux de vente** (elles passent dans le use case).
   `PersistSaleAsync` est **supprimée** de la VM.
5. **Vérifier** : non-régression (les `SaleFormViewModelTransactionTests` deviennent des tests de
   **délégation** + présentation ; les tests transactionnels migrent au niveau use case) ; `git`/CI verts ;
   `has-pending-model-changes=false` ; aucun nouveau warning.

**Compatibilité ascendante** : tant qu'un parcours n'est pas migré, il continue de fonctionner à l'identique.
La bascule se fait **par écran**, jamais en masse.

---

## 5. Stratégie de tests

| Niveau | Quoi | Comment |
|---|---|---|
| **Use case (intégration)** | `RegisterSaleUseCase` de bout en bout | **vrai SQLite temporaire** (fichier isolé, jamais InMemory) : succès multi-écriture, rollback (stock insuffisant / panne), numéro non consommé sur rollback, idempotence de garde |
| **Use case (unitaire)** | chemins d'erreur / mapping | ports mockés (Moq) : `InsufficientStockException`, `NumberSequenceException`, `PersistenceException` |
| **ViewModel** | présentation | délégation au use case (spy `ExecuteCount==1`), garde `IsSaving`, mapping VM↔Command, affichage d'erreur ; **plus** d'assertions de persistance dans la VM |
| **Domain** | règles/validators/VO | inchangé |
| **Infrastructure** | repos/persistance/lifecycle | inchangé |
| **Architecture (optionnel)** | règles de dépendances | test qui échoue si Application charge EF/Avalonia, ou si un VM référence un repo |
| **Non-régression** | suite complète | **doit rester verte** (≥ 320) à chaque étape ; un parcours migré reste utilisable |

Règles : conserver la couverture transactionnelle P2A (la migrer au niveau use case, ne pas la perdre) ;
**vrai SQLite** pour la persistance ; pas de test dépendant de l'horloge tant que `IClock` n'existe pas
(Étape 4).

---

## 6. Critères GO / NO-GO pour la phase suivante (P2B-2B)

**P2B-2B (création de la couche Application) peut démarrer si et seulement si :**

- [x] Cet ADR (frontières) et ce plan sont **revus et acceptés** par revue humaine.
- [x] Baseline verte : build OK, **320 tests** verts, **0** vulnérabilité High/Critical, `has-pending=false`.
- [ ] **Accord explicite** sur : nom du projet (`MMV.Application`), règles de dépendances (§2), style
      (use cases / services applicatifs simples), stratégie d'erreurs (exceptions typées + `Result` de
      succès), stratégie de validation (3 niveaux non redondants).
- [ ] **Premier use case confirmé** : `EnregistrerVente`.
- [ ] `TARGET_PHASE_ID = P2B-2B` fourni explicitement (Claude ne choisit jamais seul la phase suivante).

**Critères de sortie de P2B-2B (rappel, pour la phase d'après) :** projet `MMV.Application` créé, **DI
unifiée** (résidu `AddInfrastructure` mort assaini), `MMV.App → MMV.Application`, **aucun** changement de
comportement utilisateur, suite verte, `has-pending=false`. La migration effective de `SaleFormViewModel`
relève de **P2B-2C**.

---

## 7. Anti-patterns à éviter

1. **Big-bang refactor** — migrer tous les parcours d'un coup. ➜ *Un parcours à la fois (strangler).*
2. **Application impure** — référencer EF/SQLite/Avalonia/MVVM depuis `MMV.Application`. ➜ *Domain only.*
3. **Domain impur** — réintroduire EF/Avalonia dans `MMV.Domain`. ➜ *Préserver l'atout vérifié.*
4. **Fuite d'infrastructure** — exposer `DbContext`/`IQueryable`/entités EF suivies à l'UI. ➜ *Command/Result.*
5. **VM qui parle encore aux repos** — laisser un ViewModel appeler `I*Repository`/`SaveChangesAsync`/
   `ITransactionRunner` après migration. ➜ *Tout passe par un use case.*
6. **Transaction dans l'UI** — ouvrir `RunAsync` depuis la VM. ➜ *La transaction = la frontière du use case.*
7. **Règle métier dupliquée** — valider la même contrainte en UI **et** Application **et** Domain. ➜ *Un
   seul propriétaire de couche par règle.*
8. **God service applicatif** — un unique `ApplicationService` fourre-tout. ➜ *Un use case = une intention.*
9. **Use case anémique** — un use case qui ne fait que transférer vers un repo sans intention. ➜ *Acceptable
   comme palier transaction-script, mais nommer l'intention métier.*
10. **CQRS/MediatR prématuré** — introduire un dispatcher/médiateur sans besoin de volume. ➜ *Use cases
    simples d'abord ; CQRS léger plus tard, sans rupture.*
11. **Avaler l'erreur contrôlée P2A** — perdre `PersistenceException`/`InsufficientStockException`/le mapping.
    ➜ *Préserver les types et le mapping ; ne pas reswallow.*
12. **Coder une valeur de gate** — introduire TVA, devise, barème, format légal de numéro, magasin/pays. ➜
    *Strictement hors P2B ; ce sont des gates/fondations ultérieures.*
13. **Casser la numérotation/stock P2A** — réécrire la génération de numéro ou le décrément au lieu de
    **réutiliser** les ports. ➜ *Réutiliser `INumberSequenceService`/`IStockMutationService` inchangés.*
14. **Régénérer/altérer une migration** — `has-pending-model-changes` doit rester `false` ; P2B est sans
    changement de schéma. ➜ *Aucune migration en P2B-2A/2B/2C.*
15. **Double DI divergente** — laisser deux modules d'enregistrement (App + `AddInfrastructure`) diverger.
    ➜ *Unifier en P2B-2B.*

---

## 8. Trajectoire complète (rappel, hors P2B-2A)

| Phase | Contenu | État |
|---|---|---|
| **P2B-2A** | ADR frontières + ce plan + rapport | **présente (cadrage seul)** |
| **P2B-2B** | créer `MMV.Application`, DI unifiée, `MMV.App → MMV.Application` | à venir |
| **P2B-2C** | vertical slice `EnregistrerVente` (migrer `SaleFormViewModel`) | à venir |
| **P2B-2D…2I** | migration parcours : Vente, Commandes, Stock, Client, Ordonnances, Atelier | à venir |
| **Étape 3+** | modèle monétaire, org/magasin, horloge, i18n, identités, audit, fiscalité (gates) | différé |

> Chaque phase a son propre `TARGET_PHASE_ID`, son rapport et son verdict. **Arrêt obligatoire** après
> chaque phase. Aucune n'est lancée automatiquement.
