# P2B-2G — Cinquième vertical slice : Encaissement du solde de commande

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2B-2G |
| `EXECUTION_MODE` | IMPLEMENT |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

**Objectif strict** : extraire iso-fonctionnellement le flux d'encaissement du solde d'une commande depuis
`OrderDetailViewModel.ExecuteEncashBalanceAsync` vers la couche Application
(`src/MMV.Application/UseCases/Orders/SettleOrderBalance/`), poursuite du strangler pattern
(P2B-2C / 2D / 2E / 2F). Déplacement, **pas** refonte. Aucun module financier (Payment/Invoice/Quote/Money/TVA/
règles pays) n'est ouvert.

## 2. Prérequis

Phases validées (GO définitif) : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B, P2B-2C, P2B-2D, P2B-2E, P2B-2F.

Documents d'architecture et rapports P2B-2B…2F relus. Le dépôt réel prime sur les rapports — vérification faite
sur le code (`OrderDetailViewModel`, `AdvanceOrderStatusUseCase` comme patron, `RegisterSaleUseCase` comme patron
transactionnel, interfaces de persistance, entités `Order`/`Sale`/`Notification`).

## 3. État Git initial

- Branche : `p2b-architecture`
- Working tree : propre
- Derniers commits : `a67aa4d` (docs P2B-2F), `fcaad91` (feat P2B-2F), `bfa1cff` (docs P2B-2E)…
- `dotnet --version` : `8.0.417`

## 4. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `git status --short` | propre |
| `dotnet restore MMV.sln` | OK |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 erreur**, 1 avertissement (CS1998 préexistant, `OrderFormViewModel.cs:464`) |
| `dotnet test MMV.sln --no-build -c Debug` | **360 tests verts** (App 107 / Application 30 / Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** |
| `dotnet tool restore` | OK (`dotnet-ef` 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | **false** (« No changes… ») |

Baseline conforme → GO pour modification.

## 5. Inventaire du flux d'encaissement avant extraction

Source : `OrderDetailViewModel.ExecuteEncashBalanceAsync` (déclenché par `EncashBalanceCommand`, `CanExecute = HasRemainingBalance`).

| Aspect | Constat |
|---|---|
| **Garde d'entrée** | `if (Order == null \|\| !HasRemainingBalance \|\| Order.Sale == null) return;` (validation UI) |
| **Dépendances utilisées** | `IOrderRepository` (`GetWithItemsAsync`, `UpdateAsync`), `IUnitOfWork` (`SaveChangesAsync`), `INotificationRepository?` (optionnel, `CreateAsync`) |
| **Ports P2A** | **Aucun** utilisé par ce flux (ni `ITransactionRunner`, ni `IStockMutationService`, ni `INumberSequenceService`) |
| **Vente liée** | Oui : rechargée via `GetWithItemsAsync`, paiement mis à jour |
| **Champs de `Sale` modifiés** | `DepositAmount = FinalAmount` ; `RemainingAmount = 0` |
| **Champs de `Order` modifiés** | Aucun (seul l'agrégat `Sale` est muté ; `UpdateAsync(order)` marque le graphe) |
| **Calcul du solde** | `amountToEncash = fresh.Sale.RemainingAmount ?? 0` (capturé **avant** mise à zéro) |
| **Conditions d'encaissement** | Aucune re-validation côté logique : encaissement inconditionnel une fois la commande rechargée (la garde « solde restant » est portée par `HasRemainingBalance`/`CanExecute`) |
| **Notification** | Si `_notificationRepository != null` : `Notification { Type="PaymentReceived", Title=$"Paiement encaissé - {OrderNumber}", Message=$"Le solde de {amountToEncash:F2} € a été encaissé pour la commande {OrderNumber}.", EntityId, EntityType="Order", IsRead=false, CreatedAt=DateTime.Now }` |
| **SaveChanges** | **Deux** appels : (1) après mise à jour du paiement ; (2) après création de la notification |
| **Erreurs attrapées** | `catch (Exception ex)` → `ErrorMessage = $"Erreur lors de l'encaissement : {ex.Message}"` + `Debug.WriteLine` |
| **Messages utilisateur** | « Commande introuvable. » (rechargement nul) ; « Erreur lors de l'encaissement : … » |
| **Refresh / événements UI** | `Order = fresh` ; `OnPropertyChanged(DepositAmount/RemainingAmount/HasRemainingBalance)` ; `RaiseCanExecuteChanged` ; `OrderUpdated?.Invoke(this, fresh)` ; `IsLoading` true/false |

**Migré en P2B-2G** : rechargement, mise à jour du paiement de la vente, notification, enregistrement (les deux
`SaveChanges`), gestion « introuvable ».
**Reporté** : avancement de statut (déjà P2B-2E), checklist qualité, édition, suppression (dans `OrdersViewModel`),
création, stock manuel, et tout module financier (facture/devis/paiement complet/Money).

## 6. Décision de périmètre P2B-2G

Un **seul** flux migré : encaissement du solde. Nom retenu : **`SettleOrderBalance`** (nom recommandé). La
ViewModel conserve le vocabulaire UI existant (`EncashBalanceCommand`, `ExecuteEncashBalanceAsync`) — non migré,
présentation. Aucun autre use case touché.

## 7. Fichiers créés / modifiés

**Créés**
- `src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceCommand.cs`
- `src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceResult.cs`
- `src/MMV.Application/UseCases/Orders/SettleOrderBalance/ISettleOrderBalanceUseCase.cs`
- `src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceUseCase.cs`
- `tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceUseCaseTests.cs`
- `tests/MMV.App.Tests/ViewModels/OrderDetailViewModelEncashDelegationTests.cs`
- `docs/implementation/P2B-2G-report.md`

**Modifiés**
- `src/MMV.Application/DependencyInjection.cs` (enregistrement du use case)
- `src/MMV.App/ViewModels/OrderDetailViewModel.cs` (délégation du flux)
- `src/MMV.App/ViewModels/OrdersViewModel.cs` (injection + transmission du use case)
- `tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs` (adaptation au nouveau paramètre de constructeur)

## 8. Command / Result / UseCase

**`SettleOrderBalanceCommand`** : DTO neutre minimal — `long OrderId`. Le flux d'origine recharge la commande
fraîche par son identifiant et lit tout le reste (montant restant, montant final, numéro) sur l'entité ; aucune
autre donnée n'a à transiter.

**`SettleOrderBalanceResult`** : `OrderFound`, `Order?` (entité fraîche rechargée — transit toléré pendant la
migration, comme `AdvanceOrderStatusResult.Order`), `AmountEncashed`, `OldRemainingAmount`, `NewRemainingAmount`,
`IsFullyPaid`, `HasNotification`. Permet à la VM de poursuivre (réaffichage + `OrderUpdated`) sans toucher la
persistance.

**`SettleOrderBalanceUseCase`** : recharge `GetWithItemsAsync` → si `Order`/`Sale` nul, `OrderFound = false`
(aucune écriture) ; sinon capture `amountToEncash`, applique `DepositAmount = FinalAmount` / `RemainingAmount = 0`,
`UpdateAsync` + `SaveChangesAsync`, puis notification (si repo non nul) + `SaveChangesAsync`. Texte de notification
préservé au caractère près (format `:F2` inclus).

## 9. Modifications ViewModel

`OrderDetailViewModel.ExecuteEncashBalanceAsync` ne contient plus de logique de persistance : il construit
`new SettleOrderBalanceCommand { OrderId = Order.OrderId }`, appelle le use case, puis mappe le résultat
(`Order = result.Order`, `OnPropertyChanged`, `RaiseCanExecuteChanged`, `OrderUpdated`). La garde d'affichage
(`HasRemainingBalance`), `IsLoading`, les messages (« Commande introuvable. », « Erreur lors de l'encaissement :
… ») et l'événement `OrderUpdated` sont **identiques**. Nouveau paramètre de constructeur **obligatoire**
`ISettleOrderBalanceUseCase` (rejet de `null`). `OrdersViewModel` injecte le use case (DI) et le transmet au
`OrderDetailViewModel` qu'il instancie.

## 10. Modifications DI

`AddApplication` enregistre `services.AddScoped<ISettleOrderBalanceUseCase, SettleOrderBalanceUseCase>();`
(portée `Scoped` : même que `OpticDbContext` / repositories / `IUnitOfWork` / `ITransactionRunner` → même
DbContext, donc frontière transactionnelle atomique). Aucun autre enregistrement modifié.

## 11. Tests ajoutés / adaptés

**Application** — `SettleOrderBalanceUseCaseTests` (vrai SQLite temporaire, jamais InMemory), 7 tests :
encaissement nominal (paiement vente + notification, texte exact) ; sans repo de notifications (paiement mis à
jour, pas de notification) ; commande introuvable (aucune écriture) ; **rollback** transactionnel si la
notification échoue (paiement inchangé) ; commande nulle → `ArgumentNullException` ; constructeur sans
`ITransactionRunner` / sans `IOrderRepository` → `ArgumentNullException`.

**App** — `OrderDetailViewModelEncashDelegationTests`, 6 tests : délégation + `OrderUpdated` ; « Commande
introuvable. » sans mise à jour ; exception → message d'erreur, pas de notification ; constructeur sans use case
→ `ArgumentNullException` ; mapping état VM → commande (`OrderId`) ; garde `HasRemainingBalance` (commande
désactivée sans solde). La couverture de persistance du flux a été **déplacée** vers l'Application (pas supprimée).

**Adapté** — `OrderDetailViewModelAdvanceDelegationTests` : ajout du nouveau paramètre de constructeur (mock
`ISettleOrderBalanceUseCase`). Test d'architecture (`ApplicationArchitectureTests`) inchangé et toujours vert.

## 12. Migrations créées ou non

**Aucune migration.** Aucune entité Domain modifiée, aucun changement de modèle EF.
`has-pending-model-changes` = **false**.

## 13. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check`, `dotnet restore`, `dotnet build --no-restore`,
`dotnet test --no-build`, `dotnet list … --vulnerable --include-transitive`, `dotnet tool restore`,
`dotnet ef migrations has-pending-model-changes`, `dotnet list MMV.Application … reference`,
`dotnet list MMV.Application … package`.

## 14. Résultats

| Contrôle | Résultat |
|---|---|
| Build | **0 erreur**, 1 avertissement (CS1998 préexistant, hors périmètre) |
| Tests | **373 verts** (App 113 / Application 37 / Domain 223), 0 échec, 0 ignoré |
| `git diff --check` | OK (aucun espace parasite ; warnings LF→CRLF informatifs) |
| `has-pending-model-changes` | **false** |
| Référence `MMV.Application` | **uniquement** `MMV.Domain` |
| Packages `MMV.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (inchangé) |

Delta tests : **+13** (360 → 373) : +7 Application, +6 App.

## 15. Vulnérabilités

**0 vulnérabilité** (tous projets), transitives incluses.

## 16. Warnings résiduels

1 seul : `CS1998` dans `OrderFormViewModel.cs:464` — **préexistant**, hors périmètre P2B-2G (interdiction de
correction). Aucun nouveau warning introduit.

## 17. Risques résiduels

1. **Consolidation transactionnelle (changement intentionnel et borné).** Le flux d'origine effectuait **deux**
   `SaveChanges` non transactionnés (paiement validé, puis notification validée séparément). Conformément à la
   consigne (« si plusieurs écritures → `ITransactionRunner`, transaction dans le use case ») et à R-23, ces deux
   écritures sont désormais regroupées dans **une** frontière transactionnelle. Le **chemin nominal est
   identique** ; seule la rare voie d'échec partiel change : si la notification échoue, le paiement est annulé
   (plus de solde « à moitié encaissé »). C'est un **renforcement** d'atomicité, pas un changement de
   comportement observable en cas de succès. Couvert par le test de rollback.
2. **Dépendances désormais inutilisées dans `OrderDetailViewModel`** : `IOrderRepository`, `IUnitOfWork`,
   `INotificationRepository` ne sont plus lues par la VM (tous ses flux sont délégués). Elles restent injectées
   (constructeur conservé pour minimiser le diff et préserver les tests P2B-2E) — **sans avertissement compilateur**.
   Prunables dans une future phase de nettoyage (hors périmètre).
3. **`Order.SaleId = 0`** et autres problèmes préexistants (résolution Scoped depuis la racine, `AddInfrastructure`
   mort) : non corrigés (interdiction explicite). Les tests Application seedent une vente liée correcte.
4. Aucun module financier introduit (pas de `Money`/devise/TVA/facture/devis/règles pays) — invariant respecté.

## 18. État Git final

Aucun commit, aucun push (conforme `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`).

```
 M src/MMV.App/ViewModels/OrderDetailViewModel.cs
 M src/MMV.App/ViewModels/OrdersViewModel.cs
 M src/MMV.Application/DependencyInjection.cs
 M tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs
?? src/MMV.Application/UseCases/Orders/SettleOrderBalance/
?? tests/MMV.App.Tests/ViewModels/OrderDetailViewModelEncashDelegationTests.cs
?? tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceUseCaseTests.cs
?? docs/implementation/P2B-2G-report.md
```

## 19. Validation CI distante

| Propriété | Valeur |
|---|---|
| Run ID | 27469802432 |
| Lien | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27469802432 |
| Commit testé | `e683bc3` |
| Branche testée | `p2b-architecture` |
| Restore | success |
| Build | success |
| Test | success |
| Nombre de tests CI | 373 (113 App / 37 Application / 223 Domain) |
| Audit NuGet | success |
| Restore .NET tools | success |
| Check EF Core pending model changes | success |
| Statut final du workflow | **success** |

## Verdict

**P2B-2G = GO DÉFINITIF**

GO local et GO distant. Tous les critères d'acceptation sont satisfaits : use case + interface + Command + Result
créés ; `AddApplication` enregistre le use case ; `OrderDetailViewModel` délègue le flux ciblé ; transaction dans
le use case (flux multi-écritures) ; calcul du solde et champs `Sale` préservés ; notification préservée ;
comportement utilisateur identique ; tests Application et ViewModel ajoutés ; build vert ; 373 tests verts ; 0
vulnérabilité ; `has-pending-model-changes` = false ; aucune migration ; aucun modèle EF modifié ; aucune règle
Belgique/Maroc/fiscalité/devis/facture/Money ; `MMV.Application` ne référence que `MMV.Domain` ; CI distante
success sur tous les steps.

## 20. Prochaine étape candidate

**P2B-2H** — sixième vertical slice. Candidats restants dans la sphère Commandes/Ventes, par ordre de cohérence
strangler :
- **Suppression de commande** (`OrdersViewModel.OnDeleteOrderRequested`) — extraction d'un
  `DeleteOrderUseCase` (suppression + `SaveChanges`, simple, mono-écriture).
- **Édition de commande** (`OrderFormViewModel`, branche édition) — plus volumineux.
- Nettoyage technique : prune des dépendances de persistance désormais mortes dans `OrderDetailViewModel`
  (point §17.2), à isoler dans une phase dédiée.

La sélection définitive reste soumise au gating (une seule étape à la fois).
