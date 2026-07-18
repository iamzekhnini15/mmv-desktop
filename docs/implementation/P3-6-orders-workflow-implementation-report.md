# P3-6 — Implémentation backend : workflow Commandes et machine à états

> **Mode** : `IMPLEMENT_TEST_AND_WRITE_REPORT_NO_COMMIT`. Backend uniquement. Aucune UI, aucune migration,
> aucun commit, aucun push. Le code réel prime toujours sur les documents cités.

---

## 1. Paramètres et périmètre

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | **P3-6** (Commandes — backend uniquement) |
| Périmètre | Domain (politique + exception) / Application (Advance, Delete, Update) / Infrastructure (DI) / Tests |
| HEAD de départ | `3e7a1ab3a5f7936555b1126c488c748d0dcc0096` |

**Interdits respectés** : aucune modification sous `src/MMV.App/**`, aucune migration, aucun changement d'enum /
de configuration EF / de snapshot, aucun commit ni push.

---

## 2. État Git et CI de départ

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → 3e7a1ab3a5f7936555b1126c488c748d0dcc0096
git rev-parse origin/…      → 3e7a1ab3a5f7936555b1126c488c748d0dcc0096
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
                              ?? docs/implementation/P3-6-orders-workflow-audit-report.md
git diff --check            → (aucune erreur d'espaces)
```

- Branche = `p3-business-rules` ✅ ; HEAD local **et** distant = `3e7a1ab…` ✅
- Seul changement P3-6 présent au départ : le rapport d'audit P3-6 (non suivi) ✅
- CI de référence P3-5 (`gh run 29623464755`) : même SHA, `completed / success` ✅

**Conditions de démarrage remplies.**

---

## 3. Baseline (avant implémentation)

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet build -c Debug` | 0 erreur / 0 warning | 0 / 0 | ✅ |
| `dotnet test` | 819 réussis / 0 échec / 0 ignoré | **819** (Domain 307 + Application 273 + App 239) | ✅ |
| `dotnet ef migrations has-pending-model-changes` | aucune | *No changes … since the last migration* | ✅ |

Baseline conforme : l'implémentation démarre sur base propre et reproductible.

---

## 4. Décisions métier retenues

1. **Matrice linéaire stricte** (une seule successeur par statut) :
   `New → ToFabricate → InProgress → QualityCheck → Ready → Delivered`. `Delivered` est **terminal**.
2. **Refus** de tout couple hors matrice : saut d'étape, retour arrière, même statut → même statut, toute sortie
   de `Delivered`, toute autre paire.
3. **Suppression** physique autorisée **uniquement** si `Order.Status == New` (brouillon initial). Tout statut
   ≥ `ToFabricate` appartient à l'historique du workflow ; à partir de `InProgress` le stock a pu être décrémenté.
4. **Modification** autorisée uniquement en `New` et `ToFabricate` ; refusée dès `InProgress`
   (la reconstruction des lignes désynchroniserait la consommation de stock déjà enregistrée).
5. **Aucun** nouveau statut (`Cancelled`/`Canceled`/`Archived`/`Reopened`), aucun archivage, aucune
   restauration de stock, aucune suppression de mouvements/notifications : décisions à modèle reportées.
6. **Prise atomique multi-poste P3-5 conservée** telle quelle : la matrice couvre la *légalité*, la prise
   atomique couvre le *conflit* de concurrence — deux gardes complémentaires, aucune remplacée par l'autre.

---

## 5. Politique Domain

Nouveau fichier : [`src/MMV.Domain/Services/OrderStatusPolicy.cs`](../../src/MMV.Domain/Services/OrderStatusPolicy.cs).

- Classe **statique, pure, sans état ni dépendance** (Domain).
- **Source de vérité unique** des transitions autorisées.
- API exposée :
  - `OrderStatus? GetNext(OrderStatus current)` — successeur unique, `null` si terminal/hors séquence.
  - `bool IsAllowed(OrderStatus current, OrderStatus next)` — **dérivé** de `GetNext` (`GetNext(current) == next`).
- La dérivation de `IsAllowed` à partir de `GetNext` garantit qu'**une seule** matrice codée en dur existe dans
  tout le code vivant.

---

## 6. Matrice finale

| Depuis | Transition autorisée unique | `GetNext` |
|---|---|---|
| `New` | → `ToFabricate` | `ToFabricate` |
| `ToFabricate` | → `InProgress` | `InProgress` |
| `InProgress` | → `QualityCheck` | `QualityCheck` |
| `QualityCheck` | → `Ready` | `Ready` |
| `Ready` | → `Delivered` | `Delivered` |
| `Delivered` | *(terminal — aucune)* | `null` |

Sur les **36** couples de l'énumération (6 statuts), **exactement 5** sont autorisés (vérifié par test
exhaustif). Tous les autres (31) sont refusés.

---

## 7. Exception de transition illégale

Ajoutée dans [`src/MMV.Domain/Exceptions/DomainExceptions.cs`](../../src/MMV.Domain/Exceptions/DomainExceptions.cs)
(à côté de `OrderStatusConflictException`) :

- `InvalidOrderStatusTransitionException : DomainException`.
- Propriétés : `CurrentStatus`, `NextStatus`.
- Message métier stable en français, **sans détail EF/SQLite** :
  `La transition de commande de « {current} » vers « {next} » n'est pas autorisée.`

**Responsabilités distinctes conservées** :

| Exception | Sens | Étape |
|---|---|---|
| `InvalidOrderStatusTransitionException` | Paire **interdite** par la règle métier (matrice) | **P3-6** |
| `OrderStatusConflictException` | Paire **légale**, mais statut réel devenu différent (concurrence) | P3-5 (conservée) |

---

## 8. Réutilisation de la transition atomique P3-5

Dans [`AdvanceOrderStatusUseCase`](../../src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs) :

1. **Avant** d'ouvrir la transaction, le couple `CurrentStatus → NextStatus` est validé par
   `OrderStatusPolicy.IsAllowed`. Couple illégal ⇒ `InvalidOrderStatusTransitionException` **immédiate**, aucune
   transaction ouverte ⇒ **aucune** écriture, prise de statut, décrément, mouvement ni notification.
2. **Ensuite**, à l'intérieur de la transaction, la prise atomique conditionnelle inchangée
   `TryTransitionStatusAsync(orderId, expectedStatus, nextStatus)` (P3-5) est appelée. Elle **n'a pas** été
   remplacée par une comparaison en mémoire : elle reste la garde de concurrence multi-poste
   (`UPDATE … WHERE Status = expected`).

Ordre : **légalité (Domain, hors transaction) → prise atomique (Infrastructure, dans la transaction)**.

---

## 9. Effets transactionnels

La transaction unique P3-5 (`ITransactionRunner.RunAsync`) est **inchangée** : prise atomique → décrément →
mouvement → notification → sauvegarde, le tout « tout ou rien ».

| Effet | Déclencheur | Vérifié par test |
|---|---|---|
| Décrément stock + mouvement `Out` | **uniquement** `ToFabricate → InProgress` | ✅ (décrément exactement 1 fois) |
| Aucun décrément | les 4 autres transitions légales | ✅ (stock inchangé) |
| Aucun effet | transition illégale | ✅ (statut/stock/mouvements/notifications inchangés) |
| Rollback total | stock insuffisant | ✅ (P3-5, conservé) |
| Un seul succès | 2 postes sur la même transition | ✅ (P3-5, conservé) |
| Conflit | paire légale mais statut réel obsolète | ✅ `OrderStatusConflictException` |

`ReceivedDate` n'est **pas** renseignée, `SaleStatus` n'est **pas** synchronisé, aucun contrôle qualité backend
n'est ajouté (conformément au périmètre).

---

## 10. Suppression protégée

[`DeleteOrderUseCase`](../../src/MMV.Application/UseCases/Orders/DeleteOrder/DeleteOrderUseCase.cs) : après le
chargement de l'entité (contrat `NotFound` **inchangé** : commande absente ⇒ `OrderFound = false` sans écrire),
une garde refuse la suppression si `Status != New` via
`BusinessRuleException("Seule une commande au statut « Nouvelle » peut être supprimée.")`.

- Autorisé : `New`.
- Refusé : `ToFabricate`, `InProgress`, `QualityCheck`, `Ready`, `Delivered`.
- Aucun statut `Cancelled`, aucun archivage, aucune restauration de stock, aucune suppression de
  mouvements/notifications, aucune modification de FK.

---

## 11. Modification protégée

[`UpdateOrderUseCase`](../../src/MMV.Application/UseCases/Orders/UpdateOrder/UpdateOrderUseCase.cs) : après le
chargement (contrat `NotFound` **inchangé**), une garde refuse la modification si le statut n'est ni `New` ni
`ToFabricate`, via
`BusinessRuleException("Cette commande ne peut plus être modifiée après le début de la fabrication.")`.

- Autorisé : `New`, `ToFabricate`.
- Refusé : `InProgress`, `QualityCheck`, `Ready`, `Delivered`.
- Aucune modification partielle des notes ajoutée dans cette étape.

---

## 12. Traitement de `OrderService`

**Stratégie retenue : suppression complète.** Une recherche exhaustive
(`IOrderService|OrderService` sur `src/**`) a prouvé qu'aucun consommateur vivant n'existait : le type
n'apparaissait qu'en **définition** (`OrderService.cs`), en **enregistrement DI**
(`DependencyInjection.cs:70`, dans `AddInfrastructure` — elle-même inerte) et dans son **propre test**
(`OrderServiceTests.cs`). Son `IsValidStatusTransition` portait une **seconde matrice morte**.

Actions :

- Supprimé `src/MMV.Domain/Services/OrderService.cs` (interface + implémentation).
- Retiré la ligne DI `services.AddScoped<IOrderService, OrderService>();` (remplacée par un commentaire P3-6
  expliquant la suppression, cohérent avec le précédent P3-3B pour `IPrescriptionService`).
- Supprimé `tests/MMV.Domain.Tests/ServiceTests/OrderServiceTests.cs` (testait du code mort).

Résultat : **une seule** matrice de transitions dans le code vivant backend (`OrderStatusPolicy`).

> **Réserve honnête** : `OrderStatusPolicy` est l'unique source d'autorité backend. Deux calculs UI du prochain
> statut subsistent, mais ils ne peuvent plus autoriser une transition rejetée par le backend et seront traités
> lors du redesign global.

---

## 13. Fichiers modifiés et créés

**Créés**

- `src/MMV.Domain/Services/OrderStatusPolicy.cs`
- `tests/MMV.Domain.Tests/ServiceTests/OrderStatusPolicyTests.cs`
- `tests/MMV.Domain.Tests/ServiceTests/OrderWorkflowArchitectureTests.cs`
- `docs/implementation/P3-6-orders-workflow-implementation-report.md` (ce rapport)

**Modifiés**

- `src/MMV.Domain/Exceptions/DomainExceptions.cs` (nouvelle exception)
- `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs` (garde de légalité)
- `src/MMV.Application/UseCases/Orders/DeleteOrder/DeleteOrderUseCase.cs` (garde de suppression)
- `src/MMV.Application/UseCases/Orders/UpdateOrder/UpdateOrderUseCase.cs` (garde de modification)
- `src/MMV.Infrastructure/DependencyInjection.cs` (retrait DI `OrderService`)
- `tests/MMV.Application.Tests/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs`
- `tests/MMV.Application.Tests/UseCases/Orders/DeleteOrderUseCaseTests.cs`
- `tests/MMV.Application.Tests/UseCases/Orders/UpdateOrderUseCaseTests.cs`
- `docs/architecture/P3-business-rules-roadmap.md` (mise à jour minimale P3-6)

**Supprimés**

- `src/MMV.Domain/Services/OrderService.cs`
- `tests/MMV.Domain.Tests/ServiceTests/OrderServiceTests.cs`

---

## 14. Tests ajoutés

### Politique Domain (`OrderStatusPolicyTests`)

- Les 5 transitions autorisées (théorie).
- Refus : sauts d'étape, retours arrière, même statut (tous statuts), toute transition depuis `Delivered`.
- **Matrice exhaustive** : sur les 36 couples, exactement 5 autorisés.
- `GetNext` : successeur unique + `null` pour `Delivered` ; cohérence `IsAllowed` ⇔ `GetNext` sur tous les couples.

### `AdvanceOrderStatusUseCase`

- Les 4 transitions légales non-fabrication avancent le statut **sans** toucher au stock (théorie).
- 7 transitions **illégales** (saut, retour arrière, réouverture de `Delivered`, même statut) ⇒
  `InvalidOrderStatusTransitionException` **et** aucune écriture (statut/stock/mouvements/notifications inchangés).
- Paire **légale** mais statut réel obsolète ⇒ `OrderStatusConflictException` (et non `InvalidOrderStatusTransition`).
- Tests P3-5 conservés (fabrication + décrément, insuffisance ⇒ rollback, répétition/concurrence ⇒ pas de double
  décrément).

### Suppression (`DeleteOrderUseCase`)

- `New` supprimée ; commande inexistante ⇒ `NotFound` (contrat conservé).
- `ToFabricate`/`InProgress`/`QualityCheck`/`Ready`/`Delivered` refusées (théorie) ⇒ `BusinessRuleException`,
  commande **et** lignes conservées.

### Modification (`UpdateOrderUseCase`)

- `New` et `ToFabricate` modifiables (théorie).
- `InProgress`/`QualityCheck`/`Ready`/`Delivered` refusées (théorie) ⇒ `BusinessRuleException`, champs **et**
  lignes inchangés.

### Architecture (`OrderWorkflowArchitectureTests` + `ApplicationArchitectureTests` existants)

- `OrderStatusPolicy` présent dans l'assembly Domain.
- `OrderService`/`IOrderService` **absents** de l'assembly Domain (matrice unique).
- Application ne référence que Domain, aucune dépendance EF/SQLite (tests existants, toujours verts).
- Aucune règle P3-6 testée via un ViewModel (tous les tests portent sur Domain/Application).

---

## 15. Résultats ciblés

| Cible | Réussis / Échec / Ignoré |
|---|---|
| `OrderStatusPolicyTests` + `OrderWorkflowArchitectureTests` (Domain) | **30** / 0 / 0 |
| Tests Application `~Orders` (Advance + Delete + Update + Create + Get + List) | **67** / 0 / 0 |

---

## 16. Résultat complet

`dotnet test MMV.sln --no-build -c Debug` :

| Assembly | Réussis | Échec | Ignoré |
|---|---|---|---|
| `MMV.Domain.Tests` | 333 | 0 | 0 |
| `MMV.Application.Tests` | 297 | 0 | 0 |
| `MMV.App.Tests` | 239 | 0 | 0 |
| **Total** | **869** | **0** | **0** |

Évolution vs baseline (819) : **+50** nets. Détail : Domain 307 → 333 (+30 nouveaux − 4 `OrderServiceTests`
supprimés = +26) ; Application 273 → 297 (+24) ; App 239 (inchangé).

`dotnet list MMV.sln package --vulnerable --include-transitive` : **aucun package vulnérable** (7 projets).

---

## 17. Migrations

Aucune. Aucun enum, aucune configuration EF, aucune ancienne migration, aucun snapshot modifié.

```
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
→ No changes have been made to the model since the last migration.
```

---

## 18. Frontières

```
dotnet list src/MMV.Application/MMV.Application.csproj reference
→ ..\MMV.Domain\MMV.Domain.csproj   (Domain uniquement)
```

- Règle métier (matrice) **dans le Domain** ; orchestration/gardes **dans l'Application** ; DI **dans
  l'Infrastructure**. Aucune règle dans un ViewModel.
- Aucun fichier sous `src/MMV.App/**`, `design-handoff/**`, `design/**`, `docs/ui/**`.
- `git diff --check` : aucune erreur d'espaces (seuls des avertissements informatifs LF→CRLF).

---

## 19. Réserves et reports

**Réserves honnêtes conservées (non traitées en P3-6, conformément au périmètre)** :

- `CreateOrderUseCase` **ne renseigne pas** `SaleId` (commande manuelle orpheline, `SaleId = 0`).
- Le **chemin manuel** perd plusieurs données optiques (`UsageType`, `PrismValue`, `PrismBase`, `VisualAcuity`)
  — champs absents des DTO `Create/UpdateOrderLineCommand`. Le chemin **automatique** depuis la vente reste
  complet et inchangé.
- `Order` est **de facto** utilisé comme ordre de fabrication atelier (attributs fournisseur `SupplierId`/
  `ReceivedDate` inertes).
- Deux copies UI de `GetNextStatus()` subsistent (ViewModels, hors périmètre) — non autoritaires.

**Reports explicites** :

| Sujet | Étape |
|---|---|
| Fiche atelier persistée, QC bloquant, mesures/cotes de montage | **P3-6B** |
| Réconciliation `SaleStatus` ↔ `OrderStatus`, validations monétaires vente | **P3-7** |
| Règles générales de notifications (anti-doublon, résolution) | **P3-8** |
| Statut `Cancelled`/archivage, `SaleId` manuel, pertes optiques DTO manuel, séparation formelle
  fournisseur/atelier, consolidation des `GetNextStatus` UI | **redesign métier / UI futur** |

---

## 20. État Git final

```
git status --short
 M src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs
 M src/MMV.Application/UseCases/Orders/DeleteOrder/DeleteOrderUseCase.cs
 M src/MMV.Application/UseCases/Orders/UpdateOrder/UpdateOrderUseCase.cs
 M src/MMV.Domain/Exceptions/DomainExceptions.cs
 D src/MMV.Domain/Services/OrderService.cs
 M src/MMV.Infrastructure/DependencyInjection.cs
 M tests/MMV.Application.Tests/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs
 M tests/MMV.Application.Tests/UseCases/Orders/DeleteOrderUseCaseTests.cs
 M tests/MMV.Application.Tests/UseCases/Orders/UpdateOrderUseCaseTests.cs
 D tests/MMV.Domain.Tests/ServiceTests/OrderServiceTests.cs
 M docs/architecture/P3-business-rules-roadmap.md
?? src/MMV.Domain/Services/OrderStatusPolicy.cs
?? tests/MMV.Domain.Tests/ServiceTests/OrderStatusPolicyTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/OrderWorkflowArchitectureTests.cs
?? docs/implementation/P3-6-orders-workflow-audit-report.md
?? docs/implementation/P3-6-orders-workflow-implementation-report.md
?? design-handoff/  ?? design/  ?? docs/ui/
```

Changements strictement limités au périmètre autorisé (Domain, Application, DI, tests, rapports, roadmap).
**Aucun commit. Aucun push.**

---

## 21. Préparation du commit

| Élément | Valeur |
|---|---|
| Date de validation locale | 2026-07-19 |
| Total exact des tests | **869** réussis / 0 échec / 0 ignoré (Domain 333, Application 297, App 239) |
| Périmètre prévu du commit | Domain (`OrderStatusPolicy`, exception, suppression `OrderService`) / Application (`AdvanceOrderStatusUseCase`, `DeleteOrderUseCase`, `UpdateOrderUseCase`) / Infrastructure (retrait DI mort) / Tests associés / Documentation (audit, implémentation, roadmap) |
| Migration | **Aucune** |
| UI | **Aucune** modification sous `src/MMV.App/**` |
| Statut | **P3-6 encore sous réserve de la CI du commit** — non marqué définitivement terminé avant vert CI |

---

## 22. Verdict

- Une seule matrice Domain (`OrderStatusPolicy`) ✅
- Tous les couples illégaux refusés (`InvalidOrderStatusTransitionException`) ✅
- Prise atomique P3-5 conservée (concurrence multi-poste) ✅
- Stock cohérent (décrément uniquement `ToFabricate → InProgress`, jamais contourné) ✅
- Suppression limitée à `New` ✅
- Modification bloquée dès `InProgress` ✅
- 869 tests réussis / 0 échec / 0 ignoré ✅
- Aucune migration ✅ ; rapport `.md` présent ✅ ; aucune UI modifiée ✅

## **P3-6 = GO LOCAL**

(subordonné au vert de la CI du commit — **non** marqué définitivement terminé)

Aucun commit. Aucun push. Aucune UI. STOP.
