# P3-8 — Notifications métier — Rapport d'audit backend

> **MODE = AUDIT_AND_WRITE_REPORT_ONLY** — aucun code, aucun test, aucune migration, aucune UI n'a été modifié.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | P3-8 |
| Périmètre | Backend uniquement (Domain / Application / Infrastructure) |
| HEAD attendu | `ae0e35d7409a7287af1881025843b26382b5ecfc` |
| Run CI attendu | `29709378954` |
| Tests attendus | 1134 |

---

## 2. État Git et CI

### Git

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → ae0e35d7409a7287af1881025843b26382b5ecfc
git rev-parse origin/…      → ae0e35d7409a7287af1881025843b26382b5ecfc
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
git diff --check            → (vide)
```

`git log -5 --oneline` :

```
ae0e35d feat(P3-7): enforce sale business rules
53d689e feat(P3-6B): add versioned workshop sheets
a4d7380 feat(P3-6): enforce order workflow rules
3e7a1ab feat(P3-5): secure stock mutations
c246ebc feat(P3-4B): enforce product business rules
```

✅ Branche conforme. ✅ HEAD local = HEAD distant = SHA attendu. ✅ Aucun fichier suivi modifié. ✅ Seuls `design-handoff/`, `design/`, `docs/ui/` non suivis — tous autorisés.

### CI

```json
{"id":29709378954,
 "sha":"ae0e35d7409a7287af1881025843b26382b5ecfc",
 "branch":"p3-business-rules",
 "status":"completed","conclusion":"success","event":"push"}
```

✅ SHA exact. ✅ `event = push`. ✅ `status = completed`. ✅ `conclusion = success`.

> **Note d'exécution** : l'API GitHub a renvoyé `HTTP 503` (incident de service) lors des quatre premières tentatives de vérification. La vérification a été reprise et a abouti avant l'émission du verdict ; aucun résultat CI n'a été supposé ni extrapolé pendant l'indisponibilité.

**Porte Git/CI : PASSÉE.**

---

## 3. Baseline locale

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore` | OK | up-to-date | ✅ |
| `dotnet build -c Debug` | 0 erreur / 0 avertissement | 0 erreur / 0 avertissement | ✅ |
| `MMV.Domain.Tests` | 481 | 481 | ✅ |
| `MMV.Application.Tests` | 414 | 414 | ✅ |
| `MMV.App.Tests` | 239 | 239 | ✅ |
| **Total** | **1134** | **1134** | ✅ |
| Échecs | 0 | 0 | ✅ |
| Ignorés | 0 | 0 | ✅ |
| Vulnérabilités (incl. transitives) | 0 | 0 sur les 7 projets | ✅ |
| Migrations en attente | 0 | « No changes have been made to the model since the last migration. » | ✅ |
| Références `MMV.Application` | `MMV.Domain` uniquement | `..\MMV.Domain\MMV.Domain.csproj` | ✅ |

**Baseline : CONFORME.**

---

## 4. Documents lus

- `docs/architecture/P3-business-rules-roadmap.md` (notamment §P3-8, l. 425-435, et le report P3-6 → P3-8 l. 317-318)
- `docs/implementation/P3-5-stock-and-movements-audit-report.md` / `…-implementation-report.md`
- `docs/implementation/P3-6-orders-workflow-audit-report.md` / `…-implementation-report.md`
- `docs/implementation/P3-6B-workshop-sheet-audit-report.md` / `…-implementation-report.md`
- `docs/implementation/P3-7-sales-business-rules-audit-report.md` / `…-implementation-report.md`
- `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md`
- `docs/architecture/adr-application-boundaries.md`
- `docs/architecture/adr-transaction-idempotency.md`

Périmètre P3-8 tel que fixé par la roadmap (l. 427-435) :

> **Objectif** : fiabiliser les notifications stock bas / commande.
> **Règles candidates** : anti-doublon (« existe mais seulement sur *non lues* ») ; **corriger le N+1** (`GenerateLowStockNotificationsUseCase` recharge **toutes** les notifications à chaque produit) ; envisager la **résolution** quand le stock repasse au-dessus du seuil ; « marquer lu » ne supprime pas l'historique.
> **Hors périmètre** : email/push, notifications temps réel.

Le report P3-6 (« règles générales de notifications → **P3-8** ») est confirmé et pris en charge par le présent audit.

**Le code réel prime : toutes les affirmations ci-dessous sont sourcées sur des chemins et numéros de ligne vérifiés.**

---

## 5. Cartographie

| Élément | Chemin exact | Couche | Responsabilité actuelle |
|---|---|---|---|
| `Notification` | `src/MMV.Domain/Entities/Notification.cs` | Domain | Entité anémique : 7 propriétés publiques mutables, aucun invariant, aucun constructeur métier |
| `INotificationRepository` | `src/MMV.Domain/Interfaces/Repositories/INotificationRepository.cs` | Domain | 4 méthodes + héritage `IGenericRepository<Notification, long>` |
| `GenerateLowStockNotificationsUseCase` | `src/MMV.Application/UseCases/Notifications/GenerateLowStockNotifications/` | Application | Génère les alertes stock bas + renvoie le compteur non lu |
| `ListNotificationsUseCase` | `src/MMV.Application/UseCases/Notifications/ListNotifications/` | Application | Liste **complète** projetée en DTO, triée en mémoire |
| `CountUnreadNotificationsUseCase` | `src/MMV.Application/UseCases/Notifications/CountUnreadNotifications/` | Application | Délègue à `CountUnreadAsync` |
| `MarkAllNotificationsReadUseCase` | `src/MMV.Application/UseCases/Notifications/MarkAllNotificationsRead/` | Application | Délègue à `MarkAllAsReadAsync` |
| `NotificationRepository` | `src/MMV.Infrastructure/Repositories/NotificationRepository.cs` | Infrastructure | 4 méthodes ; `MarkAllAsReadAsync` en `ExecuteUpdateAsync` |
| `NotificationConfiguration` | `src/MMV.Infrastructure/Data/Configurations/NotificationConfiguration.cs` | Infrastructure | Table, longueurs, 3 index |
| Migration | `src/MMV.Infrastructure/Migrations/20260202005050_AddNotifications.cs` | Infrastructure | Création initiale de la table |
| Seed | `src/MMV.Infrastructure/Data/DbInitializer.cs:926-982` | Infrastructure | `CreateNotifications` — 4 formes de notification de démonstration |
| `NotificationsViewModel` | `src/MMV.App/ViewModels/NotificationsViewModel.cs` | UI (lecture seule) | Enveloppe le `NotificationsListViewModel` |
| `NotificationsListViewModel` | `src/MMV.App/ViewModels/NotificationsListViewModel.cs` | UI (lecture seule) | Liste + compteur + génération + marquage global |
| `MainWindowViewModel` | `src/MMV.App/ViewModels/MainWindowViewModel.cs:355-378` | UI (lecture seule) | **Déclenche la génération depuis le chemin de lecture du compteur** |

### Absences constatées (constat, pas manque à combler par défaut)

- ❌ **Aucun enum de type de notification.** `Type` est une `string` libre.
- ❌ **Aucun champ `Priority`.** Le modèle n'en comporte pas — contrairement à ce que suppose l'énoncé de l'audit (§5, §7, §17, §22).
- ❌ **Aucun service Domain, aucune policy, aucune exception** propre aux notifications.
- ❌ **Aucune relation de navigation** vers `Product`, `Order`, `Sale`, `WorkshopSheet` ou `User`.
- ❌ **Aucun use case de marquage individuel** ni de suppression.
- ❌ **Aucun test Domain** sur `Notification` ; aucun test de concurrence ; aucun test de N+1.

---

## 6. Inventaire des créations

Recherche exhaustive de `new Notification` / `Notification {` / `CreateAsync` sur `src/**/*.cs`. **Quatre sites au total** (3 runtime + 1 seed) :

| Chemin | Déclencheur | Type réel | Transaction | Anti-doublon | Entité liée | Multi-poste sûr |
|---|---|---|---|---|---|---|
| `GenerateLowStockNotificationsUseCase.cs:55-66` | Ouverture MainWindow (login), `RefreshNotificationsCountAsync`, bouton « Générer » | `"LowStock"` | ❌ Aucun `ITransactionRunner` — `SaveChangesAsync` unique après boucle | ⚠️ Partiel : `Type=="LowStock" && EntityId==p && !IsRead`, calculé **en mémoire** | `Product` / `ProductId` | ❌ **Non** — lecture-puis-écriture non atomique, aucune contrainte DB |
| `AdvanceOrderStatusUseCase.cs:171-183` | Transition de statut réussie | `"OrderStatusChanged"` | ✅ Dans le runner, après le CAS de statut | ✅ Implicite : le CAS `TryTransitionStatusAsync` garantit un seul gagnant | `Order` / `OrderId` | ✅ **Oui** — protégé par le CAS P3-6 |
| `SettleOrderBalanceUseCase.cs:132-142` | Encaissement du solde réussi | `"PaymentReceived"` | ✅ Après la prise atomique `TrySettleRemainingBalanceAsync` | ✅ Implicite : la prise atomique P3-7 garantit un seul encaissement | `Order` / `OrderId` | ✅ **Oui** — protégé par la prise atomique P3-7 |
| `DbInitializer.cs:934/950/963/972` | Seed de démonstration | `"LowStock"`, `"StockOut"`, `"Info"` ×2 | Seed | ❌ Aucun | `Product` / `ProductId` (nul pour `"Info"`) | N/A (initialisation) |

### Distinctions demandées

- **Création centralisée** : aucune. Il n'existe **pas** de service ou factory unique de notification ; chaque site instancie `new Notification { … }` à la main.
- **Création inline dans un autre use case** : `AdvanceOrderStatusUseCase` et `SettleOrderBalanceUseCase` — les deux via un `INotificationRepository?` **optionnel** (nullable, défaut `null`).
- **Notification purement UI** : aucune. Toutes les notifications sont persistées.
- **Notification persistée** : toutes.

### Constat sur l'injection optionnelle

`AdvanceOrderStatusUseCase.cs:62` et `SettleOrderBalanceUseCase.cs:64` déclarent `INotificationRepository? notificationRepository = null`. La notification n'est donc créée que si le conteneur la fournit. C'est un héritage assumé de P2C/P3-6/P3-7 (« reproduisant exactement la garde `if (_notificationRepository != null)` du flux d'origine »). En composition root réelle le repository **est** enregistré (`DependencyInjection.cs:50`, `App.axaml.cs:130`), donc le comportement de production est « notification toujours créée ». La nullabilité est une facilité de test, pas un interrupteur métier — mais elle rend le contrat silencieusement optionnel.

---

## 7. Modèle `Notification`

`src/MMV.Domain/Entities/Notification.cs` — 7 propriétés, toutes `{ get; set; }` publiques.

| Champ | Type | Obligatoire | Source | Mutable | Indexé | Usage réel |
|---|---|---:|---|---:|---:|---|
| `NotificationId` | `long` | oui (PK) | Identity DB | non (PK) | PK | Identité technique |
| `Type` | `string` (max 50) | oui | Littéral codé en dur au site de création | oui | ✅ | Discrimine `LowStock` / `StockOut` / `OrderStatusChanged` / `PaymentReceived` / `Info` |
| `Title` | `string` (max 200) | oui | Interpolation FR codée en dur | oui | non | Affichage |
| `Message` | `string` (max 1000) | oui | Interpolation FR codée en dur | oui | non | Affichage |
| `EntityId` | `long?` | non | `ProductId` ou `OrderId` | oui | ❌ **non** | Corrélation métier — **participe à l'anti-doublon** |
| `EntityType` | `string?` (max 50) | non | `"Product"` / `"Order"` | oui | ❌ **non** | Corrélation métier — **non utilisé par l'anti-doublon actuel** |
| `IsRead` | `bool` | oui | `false` à la création | oui | ✅ | Sert **à la fois** de « vu par l'utilisateur » et de « alerte encore active » |
| `CreatedAt` | `DateTime` | oui | `DateTime.Now` (runtime) / `DateTime.UtcNow` (seed) | oui | ✅ | Tri d'affichage |

### Réponses

1. **`Type` est-il un enum ou une chaîne libre ?** — Chaîne libre. `public string Type { get; set; } = string.Empty;` avec les valeurs en commentaire (`Notification.cs:18`). Aucune validation, aucune constante partagée.
2. **Les types sont-ils normalisés ?** — Non. Les cinq littéraux (`"LowStock"`, `"StockOut"`, `"OrderStatusChanged"`, `"PaymentReceived"`, `"Info"`) sont dupliqués textuellement entre sites de création et site de lecture (`GenerateLowStockNotificationsUseCase.cs:49` compare à `"LowStock"` littéral).
3. **Variantes orthographiques ou de casse ?** — Aucune variante constatée aujourd'hui : les cinq littéraux sont cohérents. Le risque est structurel (aucune barrière), non réalisé.
4. **`EntityType` est-il normalisé ?** — Non, mais cohérent en pratique (`"Product"`, `"Order"`). **Il n'est lu par aucun code de décision** — l'anti-doublon l'ignore (voir §10).
5. **`EntityId` peut-il être nul ?** — Oui, `long?`. Les deux notifications `"Info"` du seed le laissent nul (`DbInitializer.cs:963-979`).
6. **Une FK réelle existe-t-elle ?** — Non. `NotificationConfiguration` ne déclare aucune relation. `EntityId` est un entier libre, non contraint.
7. **Une entité supprimée laisse-t-elle une notification orpheline ?** — Oui, structurellement. Aucune cascade, aucun nettoyage. En pratique le risque est faible : P3-4B/P3-6/P3-7 ont converti les suppressions en archivage/désactivation, donc les entités référencées survivent.
8. **`Priority` possède-t-elle une convention ?** — **Le champ `Priority` n'existe pas.** Question sans objet.
9. **`CreatedAt` : UTC ou local ?** — **Les deux — incohérence prouvée.** Runtime : `DateTime.Now` (`GenerateLowStockNotificationsUseCase.cs:63`, `AdvanceOrderStatusUseCase.cs:181`, `SettleOrderBalanceUseCase.cs:140`) et défaut d'entité `DateTime.Now` (`Notification.cs:48`). Seed : `DateTime.UtcNow` (`DbInitializer.cs:942, 958, 969, 978`). Le reste du domaine récent utilise UTC (`AdvanceOrderStatusUseCase.cs:155` → `DateTime.UtcNow` pour la fiche atelier). Le tri par `CreatedAt` mélange donc deux bases de temps.
10. **Existe-t-il un état autre que `IsRead` ?** — Non. `IsRead` est le seul état.
11. **Une notification peut-elle être résolue sans être lue ?** — **Non — le concept de résolution n'existe pas.** Aucun champ, aucun code.
12. **Une notification lue peut-elle redevenir non lue ?** — Aucun chemin de code ne le fait. Techniquement possible (propriété mutable), jamais exercé.
13. **Une notification est-elle mutable après création ?** — Oui, entièrement (toutes les propriétés ont un setter public). En pratique seul `IsRead` est muté, par `MarkAllAsReadAsync`.
14. **Existe-t-il une clé métier stable ?** — **Non persistée.** Le triplet implicite utilisé par le code est `(Type, EntityId, IsRead==false)`. `EntityType` en est absent, et aucune contrainte DB ne le matérialise.

---

## 8. Notifications de stock bas

Source unique : `GenerateLowStockNotificationsUseCase.ExecuteAsync` (`…/GenerateLowStockNotifications/GenerateLowStockNotificationsUseCase.cs:38-75`).

```csharp
var products = await _productRepository.GetAllAsync(cancellationToken);          // l.40
var lowStockProducts = products.Where(p => p.StockQuantity <= p.StockAlertThreshold).ToList();  // l.41

foreach (var product in lowStockProducts)                                        // l.44
{
    var existingNotifications = await _notificationRepository.GetAllAsync(ct);    // l.47  ← DANS LA BOUCLE
    var exists = existingNotifications.Any(n =>
        n.Type == "LowStock" && n.EntityId == product.ProductId && !n.IsRead);    // l.48-51
    if (!exists) { … await _notificationRepository.CreateAsync(notification, ct); created++; }
}
await _unitOfWork.SaveChangesAsync(cancellationToken);                            // l.71
```

### Réponses

1. **Règle exacte ?** — `p.StockQuantity <= p.StockAlertThreshold` (l. 41). Comparaison **`<=`**, inclusive. Le champ réel est `StockAlertThreshold` (`Product.cs:102`, défaut `5`) — **il n'existe pas de `MinimumStockLevel`** contrairement à l'énoncé.
2. **Les produits inactifs sont-ils inclus ?** — ⚠️ **Oui.** `GetAllAsync()` ne filtre rien et le prédicat n'examine pas `IsActive` (`Product.cs:112`). Un produit désactivé au stock bas génère et régénère des alertes indéfiniment.
3. **Les catégories sans stock sont-elles incluses ?** — Il n'existe aucune notion de « catégorie sans stock » : `ProductCategoryEnum` ne porte pas d'attribut de suivi de stock. Tous les produits sont traités uniformément.
4. **Un seuil nul est-il valide ?** — Oui. `StockAlertThreshold = 0` est accepté ; la règle devient alors `StockQuantity <= 0`, c'est-à-dire alerte uniquement à rupture. Cohérent, mais non documenté.
5. **Une notification par produit ?** — Oui, une par produit sous seuil, par exécution, sous réserve de l'anti-doublon.
6. **Produit identifié par `EntityId` ?** — Oui (l. 60), avec `EntityType = "Product"` (l. 61).
7. **Le type est-il stable ?** — Oui, littéral `"LowStock"` à l'écriture (l. 57) comme à la lecture (l. 49).
8. **Titre/message utilisés comme clé implicite ?** — **Non.** Ils ne participent à aucune comparaison. Preuve additionnelle : le seed produit un `Title` différent (`"Stock faible"`, `DbInitializer.cs:937`) pour le même `Type = "LowStock"` sans que cela perturbe l'anti-doublon.
9. **L'anti-doublon vérifie-t-il seulement les non lues ?** — ✅ **Oui, confirmé** : `&& !n.IsRead` (l. 51). C'est la faille centrale de P3-8.
10. **Lire permet-il d'en recréer une immédiatement ?** — ✅ **Oui — défaut critique prouvé.** Après `MarkAllAsRead`, toutes les alertes `LowStock` passent à `IsRead = true`, l'anti-doublon devient aveugle, et la génération suivante recrée une alerte pour **chaque** produit encore sous seuil.
11. **Une notification est-elle créée à chaque ouverture du dashboard ?** — ✅ **Oui, et c'est pire que « dashboard ».** `MainWindowViewModel` appelle `LoadUnreadNotificationsCountAsync()` depuis son **constructeur** (l. 161), et cette méthode appelle `_generateLowStockNotificationsUseCase.ExecuteAsync()` (l. 362). La MainWindow est construite à chaque connexion réussie (`App.axaml.cs:81-82`). `RefreshNotificationsCountAsync()` (l. 375-378) rejoue le même chemin à la demande. **Un chemin de lecture (« combien de non lues ? ») exécute une écriture.** S'y ajoute `NotificationsListViewModel.GenerateLowStockNotificationsAsync()` (l. 94-105).
12. **Existe-t-il une génération planifiée ?** — Non. Aucun timer, aucun scheduler, aucun service hébergé.
13. **Créée dans la même transaction que la mutation de stock ?** — **Non.** Aucun site de mutation de stock (`IStockMutationService`, `CreateStockMovementUseCase`, `AdvanceOrderStatusUseCase.CreateStockMovementsForFabricationAsync`) ne crée de notification `LowStock`. La génération est entièrement découplée et *a posteriori*.
14. **Rupture et stock bas partagent-ils le type ?** — ⚠️ **Divergence seed/runtime.** Le runtime n'émet **que** `"LowStock"` : un stock à 0 satisfait `0 <= seuil` et produit une alerte `"LowStock"`. Le type `"StockOut"` **n'est produit que par le seed** (`DbInitializer.cs:952`) et n'est jamais généré ni lu par le code applicatif. C'est un type orphelin.
15. **Plusieurs seuils ou niveaux ?** — Non. Un seuil unique par produit, aucune notion de gravité (pas de `Priority`).

### Matrice état stock × notification

| État stock | Notification existante | Lue ? | Comportement actuel | Comportement cible candidat |
|---|---|---|---|---|
| `Stock > seuil` | aucune | — | Aucune création ✅ | Idem ✅ |
| `Stock > seuil` | `LowStock` active | non | **L'alerte survit indéfiniment** ❌ | Résolue automatiquement |
| `Stock == seuil` | aucune | — | Création (`<=` inclusif) ✅ | Idem — comportement à figer par test |
| `Stock < seuil` | aucune | — | Création ✅ | Idem ✅ |
| `Stock == 0` | aucune | — | Création de type `"LowStock"` (jamais `"StockOut"`) ⚠️ | Idem, `"StockOut"` documenté comme orphelin de seed |
| `Stock < seuil` | `LowStock` | **non lue** | Aucune création ✅ | Idem ✅ |
| `Stock < seuil` | `LowStock` | **lue** | ❌ **Recréation à chaque exécution — croissance illimitée** | Aucune création tant que l'alerte est **non résolue** |
| `Stock < seuil` | `LowStock` historique **résolue** | — | Concept inexistant | Nouvelle alerte : nouvel épisode légitime ✅ |
| Produit **désactivé**, stock bas | — | — | ❌ Alerte créée et régénérée | Exclu de la génération ; alertes actives résolues |
| Produit **supprimé** | — | — | Suppression convertie en désactivation depuis P3-4B — cas non atteignable | Notification historique conservée |

---

## 9. Analyse du N+1 — **PROUVÉ**

Le N+1 annoncé par la roadmap est confirmé, et il est **plus grave que décrit** : il ne s'agit pas de N requêtes ciblées mais de **N chargements intégraux de la table `Notifications`**.

`GenerateLowStockNotificationsUseCase.cs:47` — `await _notificationRepository.GetAllAsync(cancellationToken)` est placé **à l'intérieur** du `foreach` (l. 44), alors que son résultat ne dépend pas de `product`. `BaseRepository.GetAllAsync` (`BaseRepository.cs:42-47`) exécute `_dbSet.AsNoTracking().ToListAsync()` — **aucun filtre, aucune projection, aucune limite**.

Soit **N** le nombre de produits sous seuil et **M** le nombre total de notifications en base.

| Étape | Requêtes actuelles | Complexité | Données chargées | Risque |
|---|---:|---:|---|---|
| Chargement produits (l. 40) | 1 | O(P) | **Tous** les produits, entités complètes | 🟡 Modéré — pas de filtre `IsActive`, pas de filtre seuil en SQL |
| Filtrage stock bas (l. 41) | 0 | O(P) | En mémoire | 🟡 Le prédicat est traduisible en SQL mais évalué côté client |
| **Anti-doublon (l. 47)** | **N** | **O(N × M)** | **N × M lignes** — toutes colonnes, tous types, lues **et** non lues | 🔴 **Critique** |
| `Any(...)` en mémoire (l. 48-51) | 0 | O(N × M) | — | 🔴 Balayage linéaire répété |
| `CreateAsync` (l. 66) | 0 | O(1) | `AddAsync` — pas d'I/O | ✅ |
| `SaveChangesAsync` (l. 71) | **1** | — | Insert batch | ✅ Correct — une seule écriture |
| `CountUnreadAsync` (l. 73) | 1 | O(1) SQL | Scalaire | ✅ `CountAsync` côté serveur, index `IsRead` |

**Total : `3 + N` allers-retours, `N × M` lignes matérialisées.**

Effet d'emballement : chaque exécution qui crée des alertes augmente **M**, ce qui alourdit l'exécution suivante — et le défaut §8.10 garantit que M croît sans borne. Le coût est donc **quadratique dans le temps**, pas seulement dans une exécution.

### Réponses

1. **Toutes les notifications rechargées pour chaque produit ?** — ✅ Oui, intégralement (l. 47).
2. **Les lues sont-elles chargées inutilement ?** — ✅ Oui. Le filtre `!n.IsRead` est appliqué en mémoire (l. 51), jamais en SQL.
3. **Les autres types sont-ils chargés ?** — ✅ Oui. `OrderStatusChanged`, `PaymentReceived`, `Info` sont tous matérialisés puis écartés en mémoire (l. 49).
4. **L'anti-doublon est-il calculé en mémoire ?** — ✅ Oui, `IEnumerable.Any` sur une `IList` déjà matérialisée.
5. **Existe-t-il une méthode repository ensembliste ?** — ❌ **Non.** `INotificationRepository` n'expose que `GetUnreadNotificationsAsync()`, `MarkAllAsReadAsync()`, `MarkAsReadAsync(id)`, `CountUnreadAsync()`. Aucune méthode ne filtre par `Type` / `EntityType` / `EntityId`. **C'est la lacune structurelle qui force le N+1.**
6. **Une seule lecture des clés actives suffirait-elle ?** — ✅ Oui. Une requête projetant `EntityId` sur `Type == "LowStock"` et `EntityType == "Product"` et actives suffit à trancher tous les produits.
7. **Un `HashSet` en mémoire suffirait-il ?** — ✅ Oui. `HashSet<long>` des `EntityId` actifs → test O(1) par produit, complexité globale **O(P + K)** avec K ≪ M (K = alertes stock actives seulement).
8. **Un `NOT EXISTS` en base serait-il préférable ?** — Fonctionnellement équivalent au point 7 et plus économe en mémoire, mais il ne résout **pas** la course multi-poste (§19) sans contrainte unique, et il complique l'insertion par lot via EF. **Le `HashSet` + une contrainte DB est le meilleur rapport sûreté/coût.** Recommandation : option 7.
9. **Combien de `SaveChanges` ?** — **Un seul** (l. 71), correctement placé hors boucle. Ce point est déjà sain et ne doit pas être régressé.

---

## 10. Clé de doublon

### Clé implicite actuelle

`(Type == "LowStock", EntityId == ProductId, IsRead == false)` — `GenerateLowStockNotificationsUseCase.cs:48-51`.

| Composant | Dans la clé actuelle ? | Doit-il l'être ? | Justification |
|---|---|---|---|
| `Type` | ✅ | ✅ | Discrimine la famille d'alerte |
| `EntityType` | ❌ | ✅ | `EntityId` seul est ambigu : `EntityId = 42` désigne le produit 42 **ou** la commande 42 selon le type. La collision est aujourd'hui masquée par `Type`, mais la clé est structurellement incomplète |
| `EntityId` | ✅ | ✅ | Cible de l'alerte |
| État actif/résolu | ❌ (approximé par `!IsRead`) | ✅ | **Cœur du défaut** — voir §11 |
| `IsRead` | ✅ (à tort) | ❌ | État de lecture utilisateur, pas état métier |
| Date | ❌ | ❌ | Une clé datée n'empêche aucun doublon |
| Contenu du message | ❌ | ❌ | Volatil ; le seed prouve qu'un même type porte des textes différents |
| Seuil | ❌ | ❌ | Le seuil décide de l'ouverture, pas de l'identité |
| Magasin | absent du modèle | hors périmètre | Mono-magasin ; report |

### Réponses

1. **Deux `LowStock` pour le même produit sont-elles toujours des doublons ?** — **Non.** Deux alertes **simultanément actives** sont un doublon. Deux alertes appartenant à des **épisodes disjoints** (baisse → réapprovisionnement → nouvelle baisse) sont un historique légitime.
2. **Nouvel épisode après résolution ?** — **Oui, une nouvelle notification est souhaitable.** C'est un fait métier nouveau. C'est l'argument décisif contre un anti-doublon « une seule alerte par produit à vie ».
3. **Lire une notification la clôture-t-elle ?** — **Non, et c'est le défaut.** Le code la traite comme si oui (l. 51). Lire signifie « l'opérateur a vu le message », pas « le stock est reconstitué ».
4. **Le message peut-il changer sans nouvelle alerte ?** — Oui, et il le devrait : le message intègre `({StockQuantity}/{StockAlertThreshold})` (l. 59), qui évolue à chaque mouvement. Un message figé sur l'épisode est acceptable ; le rafraîchir est une amélioration facultative, hors périmètre minimal.
5. **Notification de commande répétable pour deux transitions ?** — **Oui, légitimement.** `ToFabricate→InProgress` puis `InProgress→QualityCheck` produisent deux `OrderStatusChanged` avec le même `EntityId`. Ce ne sont **pas** des doublons.
6. **Notification de paiement répétable ?** — Non pour un même solde : la prise atomique P3-7 le garantit (§14).
7. **Quelle clé métier fiable ?** — **`(Type, EntityType, EntityId)` restreinte aux alertes actives**, et **uniquement pour les types « alerte à condition persistante »** (`LowStock`). Les types événementiels (`OrderStatusChanged`, `PaymentReceived`, `Info`) sont des faits historiques horodatés et **doivent rester hors de toute contrainte d'unicité**.
8. **Contrainte unique possible avec le modèle actuel ?** — **Non.** Sans champ de résolution, une contrainte sur `(Type, EntityType, EntityId)` interdirait tout nouvel épisode (elle collerait à la vie du produit), et une contrainte incluant `IsRead` serait annulée dès le premier marquage.
9. **Contrainte filtrée sur les alertes actives nécessaire ?** — **Oui**, c'est la seule forme correcte : `UNIQUE (Type, EntityType, EntityId) WHERE ResolvedAt IS NULL AND EntityId IS NOT NULL`. SQLite supporte les index partiels depuis 3.8.0 ; EF Core les expose via `HasFilter`.
10. **Migration requise ?** — **Oui** pour la forme correcte (champ de résolution + index filtré). Une correction applicative sans migration est possible mais dégradée — voir §21 et §25.

---

## 11. Cycle de vie : lecture et résolution

### Concepts existants — constat explicite

| Concept | Existe ? | Preuve |
|---|---|---|
| **non lue** | ✅ | `IsRead == false` |
| **lue** | ✅ | `IsRead == true`, écrit par `MarkAllAsReadAsync` |
| **active** | ❌ **N'existe pas** | Aucun champ. Approximé — à tort — par `!IsRead` |
| **résolue** | ❌ **N'existe pas** | Aucun champ, aucun code, aucun test |
| **supprimée** | ❌ **N'existe pas** | Aucun use case ni appel `DeleteAsync` sur `Notification` |
| **expirée** | ❌ **N'existe pas** | Aucune rétention, aucune purge |

**Le modèle ne possède qu'un seul axe d'état là où le métier en exige deux : « vu par l'humain » et « condition encore vraie ».** Toute la dette P3-8 découle de cette confusion.

### Réponses

1. **`IsRead` signifie-t-il seulement « vue par l'utilisateur » ?** — C'est sa sémantique déclarée (`Notification.cs:41-43` : « Indique si la notification a été lue »).
2. **Est-il utilisé comme « problème encore actif » ?** — ✅ **Oui** (`GenerateLowStockNotificationsUseCase.cs:51`). Surcharge sémantique prouvée.
3. **Marquer comme lu doit-il permettre une notification identique ?** — **Non.** C'est précisément le bug : marquer lu rouvre la porte au doublon alors que la condition n'a pas changé.
4. **Que se passe-t-il quand le stock repasse au-dessus du seuil ?** — **Rien.** Aucun code n'observe cette transition. L'alerte reste telle quelle.
5. **L'alerte reste-t-elle éternellement active ?** — ✅ **Oui.** Une alerte non lue sur un produit réapprovisionné reste non lue et comptée pour toujours. **Le compteur du badge est donc durablement faux.**
6. **Faut-il `ResolvedAt` ou `IsResolved` ?** — Oui, **`ResolvedAt DateTime?` de préférence** : il porte l'information de `IsResolved` (`IS NULL` / `IS NOT NULL`) **plus** la date, sans champ redondant ni risque de désynchronisation entre deux colonnes. Un unique champ nullable suffit.
7. **La résolution peut-elle être dérivée sans persistance ?** — Techniquement oui (jointure `Notification` × `Product` recalculant `StockQuantity <= StockAlertThreshold`). **Rejeté** : (a) impossible d'exprimer une contrainte unique DB sur un état calculé, donc la course multi-poste subsiste ; (b) casse dès que le seuil change *a posteriori* ; (c) ne généralise pas aux types non-stock ; (d) alourdit chaque lecture de liste et de compteur.
8. **L'historique doit-il être conservé ?** — ✅ Oui — exigence explicite de la roadmap (« marquer lu ne supprime pas l'historique ») et cohérent avec la doctrine P3-4B/P3-6/P3-7 (archiver, ne pas supprimer).
9. **Résolue mais non lue possible ?** — Oui, et fréquent : le stock est reconstitué avant que l'opérateur n'ouvre le panneau. C'est un état parfaitement légitime.
10. **Lue mais toujours active possible ?** — Oui : l'opérateur a vu l'alerte, le stock reste bas. **Cet état est aujourd'hui inexprimable**, d'où le doublon.
11. **Quel état pour les compteurs ?** — `CountUnreadAsync` doit rester **non lues** (sémantique de badge inchangée pour l'UI). En revanche il devient légitime de ne compter que les **non lues et non résolues**, ce qui répare le compteur faux du point 5. ⚠️ **Ce choix modifie une valeur observable par l'UI** : à trancher explicitement en implémentation.
12. **Quel état pour l'anti-doublon ?** — **Non résolue uniquement.** `IsRead` doit disparaître du prédicat d'anti-doublon.

### Options candidates

| Option | Schéma | Historique | Anti-doublon | Coût | Risque |
|---|---|---|---|---|---|
| **A** — conserver `IsRead` seul | inchangé | conservé | ❌ Cassé (bug actuel) | nul | 🔴 Statu quo : doublons illimités |
| **B** — anti-doublon ignorant `IsRead` | inchangé | conservé | ⚠️ Partiel : jamais de doublon, mais **aucun nouvel épisode possible à vie** | très faible, **aucune migration** | 🟠 Perte métier : une 2ᵉ pénurie après réapprovisionnement n'alerte jamais |
| **C** — `IsResolved bool` | +1 colonne `NOT NULL DEFAULT 0` | conservé | ✅ | faible | 🟡 Perd la date de résolution ; redondant avec D |
| **D** — **`ResolvedAt DateTime?`** | +1 colonne **nullable** | conservé | ✅ | faible | 🟢 **Additif, backfill = `NULL` sans invention d'état** |
| **E** — suppression physique des résolues | inchangé | ❌ **détruit** | ✅ | faible | 🔴 Viole l'exigence d'historique |
| **F** — résolution dérivée du produit | inchangé | conservé | ⚠️ Non contraignable en DB | moyen | 🟠 Course multi-poste non résolue ; fragile au changement de seuil |
| **G** — nouvelle notification par épisode | nécessite D | conservé | ✅ | — | 🟢 **Conséquence naturelle de D** |

**Recommandation : D (+ G).** Une seule colonne nullable, additive, sans backfill inventé (toute notification existante est `ResolvedAt = NULL`, donc « active » — ce qui est la lecture prudente et vraie), et c'est la **seule** option qui permette à la fois l'anti-doublon correct, les épisodes multiples, l'historique, et une contrainte DB opposable au multi-poste.

---

## 12. Résolution au réapprovisionnement

### Chemins pouvant augmenter ou fixer le stock

| Chemin | Fichier | Nature |
|---|---|---|
| Mouvement `In` | `CreateStockMovementUseCase` → `IStockMutationService` | Incrément atomique (P3-5) |
| Ajustement absolu | `IStockMutationService` (`Adjustment`) | Écriture conditionnelle (P3-5) |
| Mise à jour produit | `UpdateProductUseCase` | Peut modifier `StockAlertThreshold` — **déplace le seuil, donc la condition** |
| Rollback de transition | `AdvanceOrderStatusUseCase` (runner) | Restitue le stock décrémenté |
| Seed | `DbInitializer` | Initialisation |
| Import | — | ❌ N'existe pas |

### Réponses

1. **Où détecter le franchissement à la hausse ?** — Deux stratégies : (a) *au point de mutation*, dans la transaction ; (b) *par réconciliation ensembliste*, dans la génération. Voir points 2-5.
2. **Dans `CreateStockMovementUseCase` ?** — Possible mais **insuffisant seul** : ce use case ne couvre pas les rollbacks ni les changements de seuil, et il faudrait dupliquer la logique sur chaque appelant.
3. **Dans `IStockMutationService` ?** — Le point le plus **exhaustif** (tout incrément y passe depuis P3-5) mais **la mauvaise couche** : c'est un service de mutation de stock ; `adr-application-boundaries.md` réserve l'orchestration inter-agrégats à la couche Application. Y injecter `INotificationRepository` créerait un couplage Infrastructure→Notifications indésirable.
4. **Dans `GenerateLowStockNotificationsUseCase` par recalcul global ?** — ✅ **Oui — option retenue.** Le use case parcourt déjà l'ensemble des produits et connaît déjà l'ensemble des alertes actives. Il peut, **dans le même passage et sans requête supplémentaire**, résoudre les alertes actives dont le produit n'est plus sous seuil (ou est désactivé). Coût marginal nul, couverture totale (mouvements, rollbacks, seuils, ajustements), aucune couche violée.
5. **Use case de réconciliation séparé ?** — Non nécessaire pour le périmètre minimal : ce serait un doublon de la responsabilité du point 4. À rouvrir seulement si la génération cesse d'être déclenchée régulièrement.
6. **Quel propriétaire de couche ?** — **Application**, dans `GenerateLowStockNotificationsUseCase`. Le Domain reste dépourvu de logique de notification ; l'Infrastructure ne gagne aucune dépendance.
7. **Résoudre dans la même transaction que le réapprovisionnement ?** — **Non**, et c'est délibéré. Coupler la résolution au mouvement de stock ferait échouer un réapprovisionnement légitime sur un incident de notification. La résolution est *eventually consistent* — acceptable ici, car aucune décision métier ne dépend d'une alerte. **La génération doit en revanche être atomique** (résolution + création dans un même `SaveChanges`).
8. **Réconciliation périodique nécessaire ?** — Le déclenchement actuel (chaque login + rafraîchissement manuel) suffit à réparer les écarts historiques dès la première exécution après déploiement. Aucun planificateur n'est requis. ⚠️ **Corollaire important** : dès lors que la génération devient l'organe de résolution, il devient nécessaire qu'elle soit appelée — donc **on ne peut pas simplement supprimer l'appel depuis le chemin de lecture du compteur** (§8.11) sans lui offrir un autre déclencheur. C'est une contrainte de conception à respecter en implémentation.
9. **Un produit désactivé doit-il résoudre ses alertes ?** — ✅ **Oui.** Un produit hors catalogue ne génère plus de besoin de réapprovisionnement. Cela répare simultanément le défaut §8.2.
10. **Changement de seuil : résoudre ou recréer ?** — Le recalcul global du point 4 traite ce cas **naturellement et sans code spécifique** : seuil abaissé sous le stock ⇒ l'alerte se résout au passage suivant ; seuil relevé au-dessus du stock ⇒ une alerte s'ouvre. Aucun traitement dédié dans `UpdateProductUseCase`.

### Analyse des courses

| Scénario | Déroulé | Issue actuelle | Issue cible |
|---|---|---|---|
| **A réapprovisionne / B génère** | A commit `stock=50` ; B a lu `stock=2` avant | B crée une alerte périmée qui ne se résout jamais | B crée une alerte périmée, **résolue au passage suivant** — auto-réparant |
| **A réapprovisionne / C marque lu** | Indépendants | Aucun conflit (axes distincts) | Aucun conflit — `IsRead` et `ResolvedAt` sont orthogonaux ✅ |
| **B et B′ génèrent simultanément** | Deux postes, même produit sous seuil | ❌ **Deux alertes créées** — lecture-puis-écriture non atomique, aucune contrainte | Index unique filtré ⇒ un seul gagnant, le perdant absorbe l'échec sans erreur remontée |
| **C marque lu / B génère** | C `MarkAllAsRead`, B génère | ❌ **B recrée tout le jeu d'alertes** (défaut central) | B ne crée rien : les alertes sont non **résolues** |

**Opération autoritaire :** l'**état du produit en base** est la source de vérité ; la **génération** est l'unique organe autorisé à ouvrir et à fermer une alerte de stock. `MarkAllAsRead` n'est autoritaire que sur `IsRead` et ne doit avoir **aucun** effet sur le cycle de vie métier de l'alerte.

---

## 13. Notifications de commande

Site unique : `AdvanceOrderStatusUseCase.cs:169-185`. La notification est créée **inconditionnellement après** la prise atomique du statut (l. 135-140 pour le cas général, l. 108-131 pour le cas `Ready` gardé par la fiche atelier), **à l'intérieur du `ITransactionRunner`**, et validée par le `SaveChangesAsync` unique de la l. 189.

| Transition | Notification ? | Type | Message | Transaction | Doublon possible |
|---|---:|---|---|---:|---:|
| `ToFabricate → InProgress` | ✅ | `OrderStatusChanged` | `« La commande {N} ({client}) est passée de '{ancien}' à '{nouveau}'. »` | ✅ runner | ❌ CAS |
| `InProgress → QualityCheck` | ✅ | `OrderStatusChanged` | idem | ✅ runner | ❌ CAS |
| `QualityCheck → Ready` | ✅ | `OrderStatusChanged` | idem | ✅ runner | ❌ CAS + garde fiche atelier |
| `Ready → Delivered` | ✅ | `OrderStatusChanged` | idem | ✅ runner | ❌ CAS |
| Transition refusée (conflit) | ❌ | — | — | rollback | — |
| Transition refusée (fiche atelier) | ❌ | — | — | rollback | — |

### Réponses

1. **Notification à chaque transition ?** — Oui, une par transition réussie, quel que soit le couple. Aucune transition n'est distinguée.
2. **Transitions concurrentes dupliquent-elles ?** — **Non.** `TryTransitionStatusAsync(orderId, previousStatus, nextStatus)` (l. 135) est un UPDATE conditionnel : un seul poste obtient `true`, l'autre lève `OrderStatusConflictException` (l. 138) **avant** d'atteindre le bloc de notification (l. 169).
3. **Le CAS protège-t-il l'unicité ?** — ✅ Oui, intégralement. La notification est en aval du CAS dans le même flux transactionnel.
4. **Un retry après commit peut-il recréer ?** — Non. Un rejeu trouve `previousStatus` déjà consommé ⇒ le CAS échoue ⇒ conflit ⇒ aucune notification. **Idempotence acquise.**
5. **Même transaction que le statut ?** — ✅ Oui, `ITransactionRunner` — statut, fiche atelier, mouvements de stock et notification sont tout-ou-rien (commentaire l. 149 : « Un échec de génération lève et annule TOUT (statut, stock, mouvements, notification) »).
6. **Le type distingue-t-il `Ready`, `Delivered`, QC ?** — **Non.** Un type unique `"OrderStatusChanged"`. La distinction n'existe que dans le texte libre du `Title`/`Message`, donc **non exploitable par requête**. Dette documentée, sans impact sur la correction P3-8.
7. **Plusieurs `OrderStatusChanged` sur le même `EntityId` sont-elles légitimes ?** — ✅ **Oui, absolument** : une commande traverse jusqu'à 4 transitions. **Conséquence de conception majeure : ce type ne doit jamais entrer dans une contrainte d'unicité `(Type, EntityType, EntityId)`.** C'est l'argument décisif pour un index filtré restreint aux alertes à condition persistante plutôt qu'une contrainte générale.
8. **Le texte contient-il ancien et nouveau statut ?** — ✅ Oui, tous deux (l. 175-177), fournis par la ViewModel via `command.CurrentStatusDisplay` / `NextStatusDisplay`.
9. **Localisés ou codés en dur ?** — Codés en dur en français, par interpolation. ⚠️ Les libellés de statut proviennent de la **couche UI** (`command.*Display`) : la couche Application dépend donc de textes d'affichage fournis par l'appelant. Dette d'architecture réelle mais **hors périmètre P3-8**.
10. **Modifier en P3-8 ?** — **Non. Documenter uniquement.** Ces notifications sont correctes, atomiques et idempotentes. Y toucher rouvrirait la matrice P3-6 — explicitement interdit.

---

## 14. Notifications de paiement

Site unique : `SettleOrderBalanceUseCase.cs:130-145`, atteint **uniquement** si `TrySettleRemainingBalanceAsync` a renvoyé `true` (l. 108-109).

### Réponses

1. **Le règlement atomique garantit-il une seule notification ?** — ✅ **Oui, prouvé.** La prise atomique P3-7 est un UPDATE conditionnel ; l'échec renvoie immédiatement `AlreadySettled = true, HasNotification = false` (l. 114-124) **sans atteindre** le bloc de création. Un seul encaissement ⇒ une seule notification.
2. **Le type est-il stable ?** — Oui, littéral `"PaymentReceived"` (l. 134), site unique.
3. **`EntityId` référence Sale ou Order ?** — ⚠️ **`Order`** : `EntityId = fresh.OrderId`, `EntityType = "Order"` (l. 137-138), **alors que l'entité réellement mutée est la `Sale`** (`fresh.Sale.SaleId`). C'est un choix cohérent avec l'UI (l'opérateur raisonne en commandes) et cohérent avec `OrderStatusChanged`, mais la corrélation vers la vente encaissée est perdue. Dette 🟡, pas un défaut fonctionnel.
4. **Le message contient-il un montant ?** — Oui : `« Le solde de {montant:F2} € a été encaissé pour la commande {N}. »` (l. 136).
5. **Le montant est-il celui réellement encaissé ?** — ✅ **Oui.** `amountToEncash` est lu **avant** la prise (l. 104), mais le commentaire l. 102-103 est exact : cette lecture ne décide jamais de l'écriture. Comme la prise met le solde à zéro d'un seul coup et n'a qu'un seul gagnant, le montant lu par le gagnant **est** le montant encaissé. Correct.
6. **Un retry rejeté crée-t-il une notification ?** — ❌ **Non** — retour anticipé l. 114-124, `HasNotification = false` explicite.
7. **Doit-elle participer à un anti-doublon générique ?** — **Non.** C'est un **fait historique horodaté**, pas une condition persistante. Deux encaissements sur deux commandes distinctes, ou sur la même commande après un avenant, sont des faits distincts légitimes. L'inclure dans une contrainte d'unicité serait une régression.
8. **Peut-elle être résolue ?** — **Non — sans objet.** Un paiement reçu ne « cesse » pas d'être vrai. Avec l'option D, `ResolvedAt` reste `NULL` à vie pour ce type, ce qui est sémantiquement neutre (elle n'est jamais candidate à l'anti-doublon puisque l'index filtré est restreint aux types d'alerte).
9. **Purement historique ?** — ✅ Oui. Seuls `IsRead` et l'affichage la concernent.
10. **Changer type ou structure en P3-8 ?** — **Non.** Aucun défaut concret. Documenter la dette `EntityId = OrderId` (point 3) et ne rien rouvrir du CAS de règlement ni du modèle de paiement.

---

## 15. Notifications fiche atelier / QC

Recherche exhaustive de `new Notification` dans `src/MMV.Application/UseCases/WorkshopSheets/**` et dans `WorkshopSheetOperations` / `WorkshopSheetPolicy` : **aucun résultat**.

### Réponses

1. **P3-6B crée-t-il des notifications ?** — ❌ **Non, aucune.** `EnsureCurrentSheetAsync` (`AdvanceOrderStatusUseCase.cs:154-157`) crée la fiche silencieusement. La seule notification émise lors d'une transition impliquant une fiche est l'`OrderStatusChanged` générique.
2. **Les reports P3-6B demandaient-ils P3-8 ?** — **Non.** Le report explicite de la roadmap vers P3-8 provient de **P3-6** (« règles générales de notifications → P3-8 », l. 317-318) et vise les règles générales, pas des notifications atelier. Aucun report P3-6B ne mentionne de notification QC.
3. **Une notification QC est-elle nécessaire au périmètre minimal P3-8 ?** — ❌ **Non.** Aucune preuve métier, aucun report, aucune mention dans la roadmap P3-8 (l. 425-435, qui vise « stock bas / commande »).
4. **Quelle entité serait liée ?** — `WorkshopSheet` (nouveau `EntityType`) ou `Order`. **Question prématurée** en l'absence de besoin établi.
5. **`Failed` doit-il créer une alerte active ?** — Hypothèse plausible (un QC échoué est une condition persistante jusqu'à correction), mais **non étayée**. Ne pas implémenter.
6. **`Passed` doit-il la résoudre ?** — Symétriquement plausible, symétriquement non étayé.
7. **Une nouvelle version rend-elle l'ancienne alerte obsolète ?** — Question dépendante des points 5-6. Sans objet aujourd'hui.
8. **Preuve métier suffisante pour implémenter maintenant ?** — ❌ **Non.**

**Conclusion : aucune notification atelier/QC ne sera inventée en P3-8. Report explicite.**

---

## 16. Marquage comme lu

### Réponses

1. **Le marquage individuel existe-t-il ?** — ⚠️ **Partiellement, et c'est du code mort.** `INotificationRepository.MarkAsReadAsync(long)` est déclaré (l. 23) et implémenté (`NotificationRepository.cs:32-40`), mais **aucun use case ni ViewModel ne l'appelle** (vérifié sur `src/**`). Pire, l'implémentation charge l'entité puis appelle `UpdateAsync`, lequel se contente de `_dbSet.Update(entity)` (`BaseRepository.cs:62-69`) **sans `SaveChangesAsync`** : appelée telle quelle hors d'un use case qui commit, elle **ne persisterait rien**. Code mort ET latent-défectueux.
2. **`MarkAllNotificationsReadUseCase` charge-t-il toutes les entités ?** — ❌ **Non — c'est déjà correct.** Il délègue à `MarkAllAsReadAsync` qui exécute un `ExecuteUpdateAsync` ensembliste (`NotificationRepository.cs:25-30`). Aucune matérialisation, aucun tracking.
3. **UPDATE ensembliste ?** — ✅ Oui : `UPDATE Notifications SET IsRead = 1 WHERE IsRead = 0`, une seule requête serveur.
4. **Un deuxième appel est-il idempotent ?** — ✅ Oui. Le prédicat `!n.IsRead` ne sélectionne plus rien ⇒ 0 ligne affectée, aucune erreur, aucun effet de bord.
5. **Deux postes simultanément ?** — ✅ Sûr. Deux `UPDATE … WHERE IsRead = 0` concurrents convergent vers le même état final. Pas de last-write-wins nuisible : la cible est une constante, pas une valeur lue.
6. **Le compteur est-il recalculé depuis la base ?** — ✅ Oui. `MarkAllAsReadAsync` est suivi de `LoadNotificationsAsync()` (`NotificationsListViewModel.cs:112`) qui rappelle `CountUnreadAsync` — `CountAsync` côté serveur.
7. **Une notification créée pendant `MarkAll` peut-elle être marquée par erreur ?** — Oui, si son INSERT est validé avant l'UPDATE. ⚠️ **Impact réel aggravé par le défaut central** : `MarkAllAsRead` et `GenerateLowStock` peuvent s'entrelacer, et comme le marquage rend l'anti-doublon aveugle (§8.10), l'entrelacement produit des doublons. Le correctif §11-D neutralise ce risque : `MarkAll` cesse d'influer sur la création.
8. **Sémantique attendue ?** — **« Toutes au moment de l'UPDATE »**, ce qu'implémente exactement `ExecuteUpdateAsync`. C'est la sémantique correcte pour un bouton « tout marquer comme lu » et elle ne doit pas être changée.
9. **Une notification résolue doit-elle aussi être marquée lue ?** — **Non.** Les deux axes sont orthogonaux (§11). Une alerte résolue non lue reste consultable dans l'historique — c'est souhaitable.
10. **Besoin d'un destinataire utilisateur ?** — Le modèle **ne comporte aucun champ utilisateur** et les notifications sont globales au poste. Introduire un destinataire changerait la sémantique de `MarkAllAsRead` et de tous les compteurs. **Hors périmètre — report vers P3-10 (utilisateurs locaux).**

---

## 17. Lectures et performances

| Requête | Filtre SQL | Tri SQL | Pagination | Tracking | Risque |
|---|---|---|---|---|---|
| `ListNotificationsUseCase` (l. 30) → `GetAllAsync` | ❌ **aucun** | ❌ **aucun** — `OrderByDescending` appliqué **en mémoire** (l. 33) sur la liste déjà matérialisée | ❌ **aucune** | ✅ `AsNoTracking` (`BaseRepository.cs:45`) | 🟠 **Table entière chargée à chaque ouverture du panneau et après chaque action** |
| `CountUnreadNotificationsUseCase` → `CountUnreadAsync` | ✅ `WHERE IsRead = 0` | — | — | ✅ scalaire | ✅ Optimal, index `IsRead` exploité |
| `GetUnreadNotificationsAsync` | ✅ `WHERE IsRead = 0` | ✅ `ORDER BY CreatedAt DESC` | ❌ | ✅ `AsNoTracking` | 🟡 Correctement écrite mais **jamais appelée** — code mort |
| Anti-doublon (§9) | ❌ aucun | — | ❌ | ✅ | 🔴 N+1 (voir §9) |
| `MarkAllAsReadAsync` | ✅ `WHERE IsRead = 0` | — | — | ✅ `ExecuteUpdate` | ✅ Optimal |

### Détail

- **Tri** : effectué en mémoire par `ListNotificationsUseCase` (l. 33) — le tri SQL disponible dans `GetUnreadNotificationsAsync` n'est pas réutilisé.
- **Pagination / limite** : **inexistantes**. Aucun `Skip`/`Take` nulle part.
- **Filtrage non lu / type / priorité** : aucun filtre exposé par `ListNotificationsQuery`. Pas de `Priority` dans le modèle.
- **Tracking inutile** : ✅ aucun — `AsNoTracking` est systématique en lecture.
- **Compteur séparé** : ✅ oui, `CountAsync` dédié — bon choix.
- **N+1 UI** : ❌ aucun. Le DTO `NotificationListItemDto` est plat, sans navigation ni chargement différé.
- **Nombre maximal théorique de lignes** : ⚠️ **non borné.** Avec le défaut §8.10, la table croît d'environ *(nombre de produits sous seuil)* lignes à **chaque connexion suivant un « tout marquer comme lu »**. Sur un poste utilisé quotidiennement avec quelques dizaines de produits sous seuil, l'ordre de grandeur atteint plusieurs milliers de lignes par an — chacune relue intégralement à chaque affichage **et** N fois par génération.

**La pagination est une dette réelle mais non prouvée bloquante à volumétrie saine.** Elle devient superflue une fois la croissance illimitée corrigée : c'est le doublon qui crée le problème de volume, pas la lecture. **Ne pas optimiser la lecture en P3-8** — corriger la cause.

---

## 18. Suppression et conservation

| Recherche | Résultat |
|---|---|
| Use case de suppression | ❌ Aucun |
| `DeleteAsync` sur `Notification` | ❌ Aucun appel (`BaseRepository.DeleteAsync` existe génériquement, jamais invoqué pour ce type) |
| Purge / rétention | ❌ Aucune |
| Cascade | ❌ Aucune FK, donc aucune cascade |
| Suppression locale côté UI | `Notifications.Clear()` (`NotificationsListViewModel.cs:75`) — **vide la seule `ObservableCollection` avant rechargement**, aucun effet en base |

### Réponses

1. **Peuvent-elles être supprimées ?** — **Non**, par aucun chemin applicatif ou UI.
2. **Marquer lu est-il l'unique cycle terminal ?** — ✅ Oui. Une notification naît non lue et finit lue. Il n'existe aucun autre état terminal.
3. **Une suppression effacerait-elle un historique utile ?** — ✅ Oui — `PaymentReceived` et `OrderStatusChanged` constituent une trace métier des encaissements et du parcours des commandes.
4. **Rétention maximale ?** — Aucune.
5. **Notifications liées à une entité supprimée ?** — Elles resteraient (aucune FK). Cas peu atteignable : les suppressions ont été converties en archivage/désactivation depuis P3-4B.
6. **Types historiques à conserver ?** — ✅ Oui : `PaymentReceived`, `OrderStatusChanged`, `Info`.
7. **Alertes stock résolues à conserver ?** — ✅ Oui — exigence explicite de la roadmap. La résolution doit être un **marquage**, jamais une suppression. C'est ce qui disqualifie l'option E (§11).
8. **Une purge appartient-elle à P3-8 ?** — ❌ **Non.** Aucune preuve de besoin, et le vrai moteur de volumétrie est le doublon, corrigé par ailleurs. **Report.**

---

## 19. Concurrence multi-poste

Contexte : `adr-prod-db-001-multi-poste-database-strategy.md` — SQLite partagé aujourd'hui, provider serveur visé à terme.

| Acte | Transaction | CAS / condition | Contrainte DB | Risque résiduel |
|---|---|---|---|---|
| Génération alerte stock bas | ❌ Aucun runner ; `SaveChanges` unique | ❌ **Aucune** — `Any()` en mémoire sur un instantané périmé | ❌ **Aucune** | 🔴 **Doublon prouvé** entre deux postes ; 🔴 doublon garanti après `MarkAllAsRead` (même poste) |
| Réapprovisionnement | ✅ P3-5 | ✅ Incrément atomique | Contrainte de non-négativité | ✅ Sûr ; aucun effet sur les alertes (aucune résolution n'existe) |
| Changement de seuil | ✅ P3-4B | ✅ | — | 🟠 Alerte devenue fausse, jamais corrigée |
| Lecture / résolution concurrente | — | — | — | ⚪ Sans objet — la résolution n'existe pas |
| `MarkAllAsRead` | ✅ `ExecuteUpdate` | ✅ `WHERE IsRead = 0` | — | ✅ Sûr et idempotent |
| Transition de commande | ✅ Runner | ✅ CAS statut (P3-6) | — | ✅ Une transition ⇒ une notification |
| Règlement de solde | ✅ Runner | ✅ Prise atomique (P3-7) | — | ✅ Un encaissement ⇒ une notification |

### Classement des risques identifiés

| Risque | Statut | Preuve |
|---|---|---|
| **Double notification** | 🔴 **Défaut prouvé** | Deux chemins : (a) inter-poste — deux `GetAllAsync` lisent le même instantané sans alerte, les deux insèrent ; (b) **intra-poste, déterministe** — `MarkAllAsRead` puis toute génération (§8.10). Le chemin (b) ne requiert **aucune** concurrence et se déclenche à chaque connexion |
| **Alerte perdue** | ✅ Non constaté | La génération est ré-exécutée à chaque connexion ; une alerte manquée est rattrapée |
| **Alerte active alors que condition résolue** | 🟠 **Défaut prouvé** | Aucune résolution n'existe (§11.4-5). Le compteur non lu est durablement faux |
| **Alerte résolue alors que stock bas** | ✅ Non constaté | La résolution n'existe pas — risque sans objet aujourd'hui ; à couvrir par test lors de son introduction |
| **Lecture qui rouvre un doublon** | 🔴 **Défaut prouvé** | `IsRead` dans le prédicat d'anti-doublon (l. 51) |
| **Last-write-wins** | ✅ Non constaté | `MarkAllAsRead` écrit une constante ; aucune écriture de notification ne relit-puis-écrit une valeur |
| **Retry dangereux** | ✅ Non constaté sur commande/paiement (CAS et prise atomique absorbent le rejeu) ; 🔴 constaté sur la génération stock (aucune protection) | §13.4, §14.6 vs §8.10 |
| **Exception technique exposée** | ✅ Non constaté aujourd'hui | Les ViewModels capturent et reformulent (`NotificationsListViewModel.cs:103`, `MainWindowViewModel.cs:367`). ⚠️ **Point d'attention** : l'ajout d'un index unique fera remonter une `DbUpdateException` sur collision — elle **devra** être absorbée comme un refus métier silencieux, jamais propagée |

### Séparation demandée

- **Protection déjà acquise** : transitions de commande (CAS P3-6), règlement de solde (prise atomique P3-7), `MarkAllAsRead` (UPDATE conditionnel ensembliste).
- **Défaut prouvé** : génération des alertes de stock — anti-doublon en mémoire sur un instantané, sans transaction ni contrainte, avec en outre une régression déterministe déclenchée par le marquage comme lu.
- **Limite SQLite** : aucune limite bloquante. Les index partiels (`WHERE`) sont supportés depuis SQLite 3.8.0 et exposés par EF Core via `HasFilter`. `ExecuteUpdateAsync` est opérationnel.
- **Besoin futur PostgreSQL / SQL Server** : les deux supportent l'index unique filtré (`WHERE` / `WHERE` sur index filtré SQL Server). ⚠️ **Réserve SQL Server** : les index filtrés y sont incompatibles avec certains `SET` de session (`ANSI_NULLS`, `QUOTED_IDENTIFIER` non conformes) — contrainte connue et gérable, à vérifier lors de la migration de provider, pas en P3-8.

---

## 20. Persistance

`NotificationConfiguration.cs` (43 lignes) et `20260202005050_AddNotifications.cs`.

| Élément | État actuel | Commentaire |
|---|---|---|
| Table | `Notifications` | ✅ |
| PK | `NotificationId` (identity `long`) | ✅ |
| Longueur `Type` | 50, `IsRequired` | ✅ |
| Longueur `Title` | 200, `IsRequired` | ✅ |
| Longueur `Message` | 1000, `IsRequired` | ✅ |
| Longueur `EntityType` | 50, nullable | ✅ |
| `IsRead` | `IsRequired`, `bool` | ✅ |
| `CreatedAt` | `IsRequired` | ⚠️ Alimenté en heure **locale** au runtime, **UTC** au seed |
| Index `IsRead` | ✅ l. 39 | Exploité par `CountUnreadAsync` et `MarkAllAsReadAsync` |
| Index `Type` | ✅ l. 40 | Peu sélectif seul |
| Index `CreatedAt` | ✅ l. 41 | Exploité par `GetUnreadNotificationsAsync` (méthode morte) |
| **Index `EntityType` / `EntityId`** | ❌ **Absent** | 🟠 **Aucun index ne supporte la requête d'anti-doublon** — quelle que soit sa forme, elle scannera la table |
| Enums | Aucun — `Type` en `string` | 🟡 Dette |
| Valeurs par défaut | Aucune côté DB ; `CreatedAt` a un défaut C# (`DateTime.Now`) | 🟡 |
| Nullable | `EntityId`, `EntityType` | ✅ Cohérent avec les notifications `"Info"` |
| FK | ❌ Aucune | Volontaire — `EntityId` est polymorphe |
| Index unique filtré | ❌ Aucun | Requis par le plan candidat |

---

## 21. Migration éventuelle

### Réponses

1. **Le périmètre minimal nécessite-t-il une migration ?** — **Oui pour la solution correcte (option D), non pour une solution dégradée (option B).** Détail au point 10 et §25.
2. **`IsResolved` ou `ResolvedAt` réellement nécessaire ?** — **Oui**, dès lors qu'on veut simultanément : anti-doublon correct, nouveaux épisodes après réapprovisionnement, conservation de l'historique, et contrainte DB opposable au multi-poste. Ces quatre exigences sont **inconciliables** avec le schéma actuel (§10.8). **`ResolvedAt DateTime?` seul** — pas les deux champs.
3. **Anti-doublon possible sans migration ?** — **Oui, mais dégradé** (option B) : retirer `!n.IsRead` du prédicat élimine tous les doublons immédiatement et sans schéma. **Coût métier** : plus jamais de nouvelle alerte pour un produit ayant déjà été alerté — une seconde pénurie après réapprovisionnement passerait silencieuse. C'est un troc défavorable pour une application de gestion de stock.
4. **Contrainte unique nécessaire pour le multi-poste ?** — **Oui** pour fermer la course inter-poste. Sans elle, le correctif applicatif ferme le chemin déterministe (§8.10, l'essentiel du volume) mais laisse une fenêtre étroite entre deux postes générant simultanément.
5. **Des doublons historiques existent-ils potentiellement ?** — ⚠️ **Oui, avec une quasi-certitude** sur toute base ayant vécu : le défaut §8.10 en produit à chaque connexion suivant un « tout marquer comme lu ». Le seed en produit également (un produit à stock 0 et sous seuil reçoit un `"LowStock"` **et** un `"StockOut"` — types différents, donc non-doublons au sens de la clé, mais la base de démonstration contient bien plusieurs alertes par produit).
6. **Un index unique échouerait-il sur les données existantes ?** — ✅ **Oui, très probablement** — c'est le point de vigilance majeur de la migration. Un `CREATE UNIQUE INDEX` sur `(Type, EntityType, EntityId) WHERE ResolvedAt IS NULL` échouera tant que des doublons `LowStock` actifs coexistent. **La migration devra impérativement dédoublonner avant de créer l'index**, dans le même script : conserver la plus ancienne alerte active par clé (`MIN(NotificationId)`) et résoudre les autres.
7. **Un backfill est-il possible sans inventer l'état résolu ?** — ✅ **Oui, proprement.** `ResolvedAt` nullable ⇒ toute ligne existante vaut `NULL` = « active ». Aucun état n'est inventé : c'est exactement ce que le schéma actuel affirme (rien n'a jamais été résolu). La première génération après déploiement résoudra ce qui doit l'être, sur la foi de l'état réel des produits. **Le dédoublonnage du point 6 est en revanche une décision de rétention à assumer** : il marque des lignes historiques comme résolues à une date arbitraire. Alternative plus prudente : ne pas créer l'index unique en P3-8 et s'en tenir au correctif applicatif — voir §25.
8. **Une migration additive serait-elle sûre ?** — ✅ Oui pour la colonne : `ALTER TABLE Notifications ADD COLUMN ResolvedAt TEXT NULL` est additif, non destructif, réversible, et supporté nativement par SQLite (contrairement à `DROP COLUMN`). ⚠️ L'index unique n'est additif que **conditionnellement aux données** (point 6).
9. **SQLite et le futur provider supportent-ils l'index filtré ?** — ✅ SQLite ≥ 3.8.0 (index partiels), ✅ PostgreSQL, ✅ SQL Server (avec la réserve `SET` de §19). EF Core : `HasIndex(...).IsUnique().HasFilter(...)`.
10. **Le schéma actuel suffit-il pour une première correction applicative ?** — ✅ **Oui, partiellement et utilement.** Sans aucune migration, on peut déjà : (a) supprimer le N+1 ; (b) fermer le chemin de doublon déterministe (option B) ; (c) exclure les produits inactifs ; (d) ajouter un index `(EntityType, EntityId)` non unique. Ce qui reste inaccessible sans migration : la **résolution** et les **épisodes multiples**.

---

## 22. Tests existants

**7 tests au total** sur le domaine Notifications, tous dans `MMV.Application.Tests` (SQLite réel, fichier temporaire). **Zéro test Domain, zéro test de concurrence, zéro test de performance.**

| Fichier | Test | Portée |
|---|---|---|
| `NotificationUseCasesTests.cs:53` | `MarkAllAsRead_MarksEveryNotification` | Marquage global sur SQLite réel |
| `NotificationUseCasesTests.cs:77` | `GenerateLowStock_CreatesOneAlertPerLowStockProduct_ThenIsIdempotent` | Génération + anti-doublon **sur non lues uniquement** |
| `NotificationUseCasesTests.cs:113` | `Constructors_RejectNullDependencies` | Garde de nullité |
| `NotificationQueryUseCasesTests.cs:61` | `ListNotifications_ReturnsProjectedDtos_SortedByCreatedAtDescending` | Projection + tri |
| `NotificationQueryUseCasesTests.cs:79` | `CountUnread_ReturnsUnreadCount` | Compteur |
| `NotificationQueryUseCasesTests.cs:92` | `ListNotifications_NoData_ReturnsEmpty` | Cas vide |
| `NotificationQueryUseCasesTests.cs:114` | `Constructors_RejectNullRepository` | Garde de nullité |

### Analyse critique du test d'idempotence

`GenerateLowStock_…_ThenIsIdempotent` (l. 77-110) est le test le plus proche du défaut central — **et il le manque exactement.** Il exécute la génération deux fois de suite (l. 95, l. 104) et vérifie `CreatedCount == 0` au second passage. Mais **entre les deux exécutions, il ne marque rien comme lu**. La notification reste non lue, donc l'anti-doublon fonctionne, donc le test passe.

**Il suffirait d'intercaler un `MarkAllAsRead` entre les deux exécutions pour que ce test échoue.** C'est la reproduction la plus économique du bug 🔴-1 et le premier test à écrire.

---

## 23. Trous de couverture

| Règle ou risque | Test existant | Niveau | Suffisant ? | Test manquant |
|---|---|---|---|---|
| Génération nominale | `…ThenIsIdempotent` (l. 95-97) | Application/SQLite | ✅ | — |
| **Exactement au seuil** (`==`) | ❌ | — | ❌ | Fige la frontière `<=` : `stock == seuil` ⇒ alerte créée |
| Sous le seuil | ✅ (`stock=1`, `seuil=5`) | Application | ✅ | — |
| **Stock zéro** | ❌ | — | ❌ | `stock=0` ⇒ une seule alerte de type `"LowStock"` (jamais `"StockOut"`) |
| **Produit inactif** | ❌ | — | ❌ | `IsActive=false` + stock bas ⇒ **aucune** alerte ; alerte active existante résolue |
| Doublon **non lu** | ✅ (l. 104-105) | Application | ✅ | — |
| **Doublon après marquage lu** | ❌ | — | ❌ | 🔴 **Test prioritaire** : générer → `MarkAllAsRead` → générer ⇒ `CreatedCount == 0` et **1 seule** ligne `LowStock` |
| **Nouvel épisode après résolution** | ❌ | — | ❌ | Générer → réapprovisionner → générer (résout) → rebaisser → générer ⇒ **2** notifications, la 1ʳᵉ résolue |
| **N+1** | ❌ | — | ❌ | Compter les requêtes (intercepteur EF / `DbCommandInterceptor`) : nombre **constant** en fonction du nombre de produits sous seuil |
| **Génération simultanée** | ❌ | — | ❌ | Deux contextes concurrents sur la même base ⇒ **1** alerte active (échec d'unicité absorbé silencieusement) |
| **Réapprovisionnement → résolution** | ❌ | — | ❌ | Stock repassé au-dessus ⇒ `ResolvedAt != null`, notification **conservée**, compteur corrigé |
| **Changement de seuil** | ❌ | — | ❌ | Seuil abaissé sous le stock ⇒ résolution ; seuil relevé ⇒ nouvelle alerte |
| Marquage **individuel** | ❌ | — | ❌ | Soit couvrir `MarkAsReadAsync` (et corriger son absence de `SaveChanges`), soit **le supprimer comme code mort** — décision à prendre |
| Marquage global | ✅ (l. 53) | Application/SQLite | ✅ | — |
| **Idempotence du marquage global** | ❌ | — | ❌ | Deuxième `MarkAllAsRead` ⇒ 0 ligne affectée, aucune exception |
| **Notification créée pendant `MarkAll`** | ❌ | — | ❌ | Entrelacement ⇒ aucun doublon (garanti par le correctif, à verrouiller) |
| **`MarkAllAsRead` n'affecte pas la résolution** | ❌ | — | ❌ | Après marquage, `ResolvedAt` reste `NULL` ⇒ anti-doublon toujours actif |
| Transition de commande | Indirect (`AdvanceOrderStatusUseCaseTests`) | Application | ⚠️ Partiel | Assertion explicite : **1** `OrderStatusChanged` par transition réussie |
| **Retry de transition** | ❌ (conflit couvert, notification non asservie) | — | ❌ | Transition rejouée ⇒ conflit ⇒ **aucune** notification créée |
| Paiement nominal | Indirect (`SettleOrderBalanceUseCaseTests`) | Application | ⚠️ Partiel | Assertion explicite : **1** `PaymentReceived`, montant = solde encaissé |
| **Retry de paiement** | `SettleOrderBalanceAtomicityTests` | Application | ⚠️ Partiel | Assertion explicite : 2ᵉ appel ⇒ **0** notification supplémentaire |
| **Types événementiels non contraints** | ❌ | — | ❌ | Deux `OrderStatusChanged` sur le **même** `EntityId` restent possibles malgré l'index unique |
| QC `Failed`/`Passed` | ❌ | — | ⚪ Sans objet | Aucune notification QC n'existe — **rien à tester** |
| Lecture | ✅ (l. 61, l. 92) | Application | ✅ | — |
| Compteur | ✅ (l. 79) | Application | ⚠️ Partiel | Le compteur ne doit pas compter les alertes **résolues** non lues |
| Suppression | ⚪ | — | ⚪ Sans objet | Aucune suppression n'existe |
| **Migration** | ❌ | — | ❌ | Dédoublonnage : base pré-chargée de doublons actifs ⇒ migration réussit, **1** actif conservé par clé |

---

## 24. Risques classés

### 🔴 Critique

**🔴-1 — Croissance illimitée des notifications de stock bas (doublons déterministes).**
- **Preuve** : `GenerateLowStockNotificationsUseCase.cs:51` (`&& !n.IsRead`) combiné à `MainWindowViewModel.cs:161 → 362` (génération appelée depuis le constructeur, à chaque connexion) et `NotificationRepository.cs:23-30` (`MarkAllAsRead` bascule **tout** à lu).
- **Scénario** : l'opérateur clique « tout marquer comme lu ». Toutes les alertes `LowStock` passent à `IsRead = true`. À la connexion suivante, `MainWindowViewModel` déclenche la génération ; l'anti-doublon ne trouve aucune alerte **non lue** ; une nouvelle alerte est créée **pour chaque produit encore sous seuil**. Répété à chaque cycle.
- **Impact** : table `Notifications` non bornée ; panneau de notifications saturé de doublons ; compteur non lu gonflé artificiellement ; dégradation quadratique de la génération (§9) ; perte de confiance dans l'outil d'alerte.
- **Aucune concurrence n'est requise** — le défaut est déterministe et mono-poste.
- **Propriétaire de couche** : Application (`GenerateLowStockNotificationsUseCase`).
- **Étape cible** : **P3-8, obligatoire.**

**🔴-2 — N+1 sur l'anti-doublon (`O(N × M)` avec M non borné).**
- **Preuve** : `GenerateLowStockNotificationsUseCase.cs:47` — `GetAllAsync()` **dans** la boucle `foreach` de la l. 44 ; `BaseRepository.cs:42-47` ⇒ `SELECT *` sans filtre ni limite.
- **Scénario** : 40 produits sous seuil, 4 000 notifications en base ⇒ 40 requêtes chargeant 160 000 lignes cumulées, à chaque connexion.
- **Impact** : latence de connexion ; couplage vicieux avec 🔴-1 (chaque doublon créé alourdit l'exécution suivante).
- **Propriétaire de couche** : Application + Domain (interface repository à étendre) + Infrastructure (implémentation).
- **Étape cible** : **P3-8, obligatoire.**

**🔴-3 — Aucune protection multi-poste sur la création d'alertes.**
- **Preuve** : `GenerateLowStockNotificationsUseCase.cs:47-68` — lecture puis écriture non atomiques, hors `ITransactionRunner` ; `NotificationConfiguration.cs` — aucune contrainte d'unicité.
- **Scénario** : deux postes se connectent simultanément ; les deux lisent l'absence d'alerte pour le produit 42 ; les deux insèrent.
- **Impact** : doublon inter-poste. Fenêtre étroite, mais permanente et non détectable.
- **Nuance** : ce risque est **d'un ordre de grandeur inférieur** à 🔴-1 en volume réel. Le correctif applicatif de 🔴-1 le réduit sans le fermer ; seule une contrainte DB le ferme.
- **Propriétaire de couche** : Infrastructure (index unique filtré) + Application (absorption de la collision).
- **Étape cible** : **P3-8** si la migration est retenue — voir §25.

### 🟠 Important

| # | Constat | Preuve | Impact | Couche | Cible |
|---|---|---|---|---|---|
| 🟠-1 | **`IsRead` utilisé comme état métier** — confusion « vu » / « actif » | `GenerateLowStockNotificationsUseCase.cs:51` | Cause racine de 🔴-1 ; rend tout anti-doublon correct impossible | Domain + Application | **P3-8** |
| 🟠-2 | **Alertes jamais résolues** | Aucun code n'écrit de résolution ; §11.4-5 | Alertes fantômes permanentes sur produits réapprovisionnés | Application | **P3-8** |
| 🟠-3 | **Compteur non lu faux** | Conséquence de 🟠-2 et 🔴-1 | Le badge affiche un nombre sans rapport avec les problèmes réels | Application | **P3-8** |
| 🟠-4 | **Produits inactifs alertés** | `GenerateLowStockNotificationsUseCase.cs:40-41` — aucun filtre `IsActive` | Bruit permanent sur des produits hors catalogue | Application | **P3-8** (coût marginal nul) |
| 🟠-5 | **Aucune clé métier persistée ni indexée** | `NotificationConfiguration.cs:39-41` — pas d'index `(EntityType, EntityId)` | Toute requête d'anti-doublon scanne la table | Infrastructure | **P3-8** |
| 🟠-6 | **Aucune méthode repository ensembliste** | `INotificationRepository.cs` — 4 méthodes, aucune par `Type`/`EntityId` | Force le N+1 (🔴-2) | Domain + Infrastructure | **P3-8** |
| 🟠-7 | **Types en chaînes libres, non centralisés** | `Notification.cs:18` ; 5 littéraux dupliqués sur 4 sites | Toute faute de frappe casse silencieusement l'anti-doublon | Domain | **P3-8** (constantes ; **pas d'enum** — voir §25) |
| 🟠-8 | **`MarkAsReadAsync` : code mort et défectueux** | `NotificationRepository.cs:32-40` — `UpdateAsync` sans `SaveChanges` ; aucun appelant | Piège pour un futur appelant | Infrastructure | **P3-8** (décision : supprimer ou corriger) |
| 🟠-9 | **Écriture déclenchée par un chemin de lecture** | `MainWindowViewModel.cs:355-363` — « charger le compteur » exécute une génération | Chaque affichage du badge écrit en base | UI (⚠️ **non modifiable en P3-8**) | Documenter ; **report UI** — voir §26 |

### 🟡 Dette

| # | Constat | Preuve |
|---|---|---|
| 🟡-1 | Textes codés en dur, non localisés | 4 sites de création, interpolation FR |
| 🟡-2 | Aucun champ `Priority` — pas de hiérarchisation | `Notification.cs` |
| 🟡-3 | **`CreatedAt` mélange local et UTC** | `DateTime.Now` (runtime) vs `DateTime.UtcNow` (`DbInitializer.cs:942`) — tri incohérent |
| 🟡-4 | Aucune pagination ni limite en lecture | `ListNotificationsUseCase.cs:30` |
| 🟡-5 | Aucune politique de purge ou de rétention | §18 |
| 🟡-6 | Aucun destinataire utilisateur | `Notification.cs` |
| 🟡-7 | Type `"StockOut"` orphelin — produit par le seed, jamais par le runtime, jamais lu | `DbInitializer.cs:952` |
| 🟡-8 | `GetUnreadNotificationsAsync` — code mort | `NotificationRepository.cs:15-21`, aucun appelant |
| 🟡-9 | `PaymentReceived.EntityId` référence l'`Order`, pas la `Sale` mutée | `SettleOrderBalanceUseCase.cs:137` |
| 🟡-10 | `OrderStatusChanged` : type unique, statuts seulement dans le texte libre | `AdvanceOrderStatusUseCase.cs:173-177` |
| 🟡-11 | `INotificationRepository` injecté en **optionnel** (`= null`) dans deux use cases | `AdvanceOrderStatusUseCase.cs:62`, `SettleOrderBalanceUseCase.cs:64` |
| 🟡-12 | La couche Application dépend de libellés d'affichage fournis par l'UI | `command.CurrentStatusDisplay` / `NextStatusDisplay` |
| 🟡-13 | Tri appliqué en mémoire alors qu'une variante SQL existe | `ListNotificationsUseCase.cs:33` |

### 🔵 Report explicite

| Sujet | Destination |
|---|---|
| UI notifications (panneau, filtres, badge, déclenchement) | **Redesign global UI** |
| Découplage écriture/lecture du compteur (🟠-9) | **Redesign global UI** — nécessite de toucher `MainWindowViewModel` |
| Email / push / temps réel | **Hors P3** (roadmap l. 432) |
| Notifications utilisateur ciblées (destinataire) | **P3-10** (utilisateurs locaux) |
| Notifications atelier / QC | **Report — aucune preuve métier** (§15) |
| Purge / rétention | **Futur — aucune preuve de besoin** |
| Localisation / i18n | **Futur** |
| Planificateur externe / génération périodique | **Hors périmètre** |
| Provider serveur (PostgreSQL / SQL Server) | **Chantier production** (`adr-prod-db-001`) |
| Observabilité / logs structurés | **Futur** |
| Normalisation `Type` en enum + migration de données | **Futur** — voir §25 |

---

## 25. Plan candidat

### Décision structurante préalable

Deux plans cohérents existent. Ils diffèrent sur un seul point : **accepte-t-on une migration ?**

| | **Plan MINIMAL** (sans migration) | **Plan CORRECT** (avec migration) — *recommandé* |
|---|---|---|
| Clé métier | `(Type, EntityType, EntityId)` sur **toutes** les alertes | `(Type, EntityType, EntityId)` sur les alertes **non résolues** |
| Doublons | ✅ Éliminés | ✅ Éliminés |
| N+1 | ✅ Supprimé | ✅ Supprimé |
| Historique | ✅ Conservé | ✅ Conservé |
| **Nouvel épisode après réapprovisionnement** | ❌ **Impossible — jamais de 2ᵉ alerte** | ✅ Supporté |
| Résolution | ❌ Inexistante | ✅ `ResolvedAt` |
| Compteur réparé | ❌ Non (alertes fantômes persistent) | ✅ Oui |
| Course multi-poste (🔴-3) | ❌ Ouverte | ✅ Fermée (index unique filtré) |
| Migration | Aucune | 1 colonne nullable + 1 index + dédoublonnage |
| Risque de migration | — | ⚠️ Dédoublonnage des données existantes |

**Recommandation : Plan CORRECT.** Le Plan MINIMAL échange un bug (doublons) contre un autre (alertes définitivement muettes après le premier épisode) — inacceptable pour un outil de gestion de stock. Le surcoût du Plan CORRECT est **une colonne nullable**, ce qui est le geste de schéma le moins risqué possible, et il seul satisfait les quatre exigences de la roadmap simultanément.

### Obligatoire P3-8 (Plan CORRECT)

**Domain**
1. Ajouter `ResolvedAt : DateTime?` à `Notification` (nullable ⇒ `NULL` = active). **Ne pas** ajouter `IsResolved` en doublon.
2. Introduire des **constantes** de type (`NotificationTypes.LowStock`, `.OrderStatusChanged`, `.PaymentReceived`, `.StockOut`, `.Info`) et d'entité (`NotificationEntityTypes.Product`, `.Order`). **Constantes `string`, pas un enum** : un enum imposerait une conversion de la colonne existante et une migration de données, hors périmètre minimal (cf. 🔵). Remplacer les littéraux aux 4 sites.
3. Étendre `INotificationRepository` d'une méthode ensembliste, p. ex. `GetActiveEntityIdsAsync(string type, string entityType, CancellationToken)` renvoyant les `EntityId` d'alertes non résolues, et d'une méthode de résolution ensembliste `ResolveAsync(string type, string entityType, IEnumerable<long> entityIds, DateTime resolvedAt, CancellationToken)`.

**Infrastructure**
4. Implémenter les deux méthodes : projection SQL (`Select(n => n.EntityId)`) pour la première ; `ExecuteUpdateAsync` conditionnel (`WHERE ResolvedAt IS NULL AND …`) pour la seconde.
5. `NotificationConfiguration` : index non unique `(EntityType, EntityId)` **et** index **unique filtré** `(Type, EntityType, EntityId) WHERE ResolvedAt IS NULL AND EntityId IS NOT NULL` via `HasFilter`.
   - ⚠️ Le filtre `EntityId IS NOT NULL` est **indispensable** : sans lui, les notifications `"Info"` du seed (deux lignes, `EntityId = NULL`, `EntityType = NULL`) entreraient en collision selon la sémantique de `NULL` de l'index.
   - ⚠️ Les types événementiels (`OrderStatusChanged`, `PaymentReceived`) **entrent** dans le périmètre de cet index puisqu'ils portent un `EntityId`. **Ils y entreraient en collision** (plusieurs transitions par commande — §13.7). **Le filtre doit donc aussi restreindre le type** : `WHERE ResolvedAt IS NULL AND Type = 'LowStock'`. **Ce point est le piège principal du plan — à traiter explicitement en implémentation.**
6. Migration additive : `ADD COLUMN ResolvedAt` (nullable, pas de défaut) + dédoublonnage préalable des `LowStock` actifs (conserver `MIN(NotificationId)` par `(EntityType, EntityId)`, résoudre les autres à la date de migration) + création des index.

**Application — `GenerateLowStockNotificationsUseCase`, réécriture du corps**
7. Charger les produits **une fois**, en filtrant `IsActive` (🟠-4). Idéalement pousser le prédicat en SQL.
8. Charger **une fois** les `EntityId` d'alertes `LowStock` actives → `HashSet<long>` (supprime le N+1, 🔴-2).
9. **Résoudre** : alertes actives dont l'`EntityId` n'est plus dans l'ensemble « sous seuil et actif » ⇒ `ResolvedAt = now` (UTC), en un seul `ExecuteUpdate` (🟠-2, 🟠-3).
10. **Créer** : produits sous seuil absents du `HashSet` ⇒ nouvelle notification. **Prédicat fondé sur `ResolvedAt IS NULL`, jamais sur `IsRead`** (🔴-1, 🟠-1).
11. Envelopper résolution + création dans un `ITransactionRunner` (atomicité de la réconciliation).
12. **Absorber `DbUpdateException` sur violation d'unicité comme un refus silencieux** (l'autre poste a gagné la course) — jamais de message technique remonté (§19).
13. Conserver **un seul** `SaveChangesAsync` et le `CountUnreadAsync` final (contrat de retour inchangé).
14. Décider le sort de `MarkAsReadAsync` (🟠-8) : **supprimer** (recommandé — code mort, aucun appelant) ou corriger et couvrir.
15. Aligner `CreatedAt` sur `DateTime.UtcNow` aux sites de création (🟡-3). ⚠️ **Changement observable** (décalage d'affichage sur les nouvelles lignes) — à assumer explicitement ou à reporter.

**Non modifié, documenté seulement**
16. `AdvanceOrderStatusUseCase` et `SettleOrderBalanceUseCase` : **aucune modification fonctionnelle**. Seul le remplacement des littéraux par les constantes (point 2) les touche. Leurs garanties d'unicité sont acquises par le CAS P3-6 et la prise atomique P3-7.
17. `MarkAllNotificationsReadUseCase` : **inchangé**. Déjà ensembliste, idempotent et multi-poste sûr. Son unique défaut — influencer l'anti-doublon — disparaît par le point 10, sans le toucher.
18. ⚠️ **`CountUnreadAsync` : trancher explicitement** si le compteur doit exclure les alertes résolues non lues. Réparer 🟠-3 l'exige, mais **modifie une valeur affichée par l'UI**. Recommandation : oui, l'exclure — un badge doit refléter des problèmes actifs.

**Tests (≈ 18 nouveaux, cf. §23)** — priorité absolue au premier :
19. 🔴 `MarkAllAsRead` intercalé entre deux générations ⇒ `CreatedCount == 0`, **1** seule ligne `LowStock`.
20. Frontière `stock == seuil` ; `stock == 0` ; produit inactif exclu et ses alertes résolues.
21. Résolution au réapprovisionnement ; nouvel épisode après résolution ; changement de seuil (deux sens).
22. Absence de N+1 (compte de requêtes constant via intercepteur EF).
23. Génération concurrente sur deux contextes ⇒ **1** alerte, aucune exception remontée.
24. `OrderStatusChanged` multiples sur le même `EntityId` restent possibles malgré l'index.
25. Retry de transition et retry de paiement ⇒ **0** notification supplémentaire.
26. Migration : base pré-chargée de doublons actifs ⇒ migration réussie, **1** actif conservé par clé.

### Reportable

UI et déclenchement (🟠-9) ; email/push/temps réel ; destinataires ; purge/rétention ; localisation ; planificateur ; enum de types ; pagination ; `PaymentReceived.EntityId` ; provider serveur ; observabilité.

**Aucune nouvelle sous-phase officielle n'est créée.** Tout ce qui précède tient dans P3-8.

---

## 26. Reports explicites

| # | Sujet | Motif | Destination |
|---|---|---|---|
| R1 | Panneau de notifications, filtres, badge | Interdiction absolue de modifier l'UI en P3-8 | Redesign global UI |
| R2 | **Génération déclenchée par le chemin de lecture du compteur** (🟠-9) | Le correctif exige de modifier `MainWindowViewModel` — interdit. ⚠️ **Et la génération doit rester appelée** : elle devient l'organe de résolution (§12.8). Le déplacer sans déclencheur de remplacement casserait la résolution | Redesign global UI |
| R3 | Email / push / temps réel | Roadmap l. 432 | Hors P3 |
| R4 | Destinataires utilisateur | Modèle sans champ utilisateur ; changerait la sémantique de `MarkAll` | P3-10 |
| R5 | Notifications atelier / QC | **Aucune preuve métier** (§15) | Redesign métier futur |
| R6 | Purge / rétention | Aucun besoin prouvé ; la volumétrie est causée par le doublon, corrigé par ailleurs | Futur |
| R7 | Localisation | Aucun besoin exprimé | Futur |
| R8 | Planificateur / génération périodique | Le déclenchement à la connexion suffit | Hors périmètre |
| R9 | `Type` en enum + migration de données | Les constantes suffisent au périmètre minimal | Futur |
| R10 | Pagination des lectures | Devient superflue une fois la croissance illimitée corrigée | Futur |
| R11 | `PaymentReceived.EntityId` → `SaleId` | Dette de corrélation, aucun défaut fonctionnel ; rouvrirait P3-7 | Redesign métier futur |
| R12 | Champ `Priority` | N'a jamais existé ; aucun besoin exprimé | Futur |
| R13 | Provider serveur, index filtrés SQL Server | `adr-prod-db-001` | Chantier production |
| R14 | Observabilité / logs | Aucun cadre en place | Futur |
| R15 | Dépendance Application → libellés UI (🟡-12) | Rouvrirait la matrice P3-6 | Redesign métier futur |

---

## 27. Verdict

| Critère de §26 | Statut |
|---|---|
| Toutes les créations cartographiées | ✅ 4 sites (3 runtime + 1 seed), §6 |
| N+1 prouvé ou réfuté | ✅ **Prouvé** — `O(N × M)`, `GenerateLowStockNotificationsUseCase.cs:47`, §9 |
| Clé de doublon définie | ✅ `(Type, EntityType, EntityId)` sur alertes non résolues, restreinte à `LowStock`, §10 / §25.5 |
| Lecture et résolution distinguées | ✅ §11 — `IsRead` (vu) vs `ResolvedAt` (actif), 7 options comparées |
| Comportement au réapprovisionnement cadré | ✅ §12 — recalcul global dans le use case de génération, courses analysées |
| Notifications commande/paiement analysées | ✅ §13 / §14 — sûres via CAS P3-6 et prise atomique P3-7, **non modifiées** |
| Concurrence multi-poste traitée | ✅ §19 — protections acquises, défauts prouvés, limites SQLite/serveur séparées |
| Migration précisément décidée | ✅ §21 / §25 — **migration additive retenue** (`ResolvedAt` nullable + 2 index + dédoublonnage), alternative sans migration documentée et argumentée comme inférieure |
| Plan candidat suffisamment précis | ✅ §25 — 26 points, obligatoire/reportable séparés, pièges d'implémentation signalés |
| Rapport `.md` existant | ✅ `docs/implementation/P3-8-notifications-business-rules-audit-report.md` |
| Aucun code modifié | ✅ Vérifié — §validation documentaire |

### Constats majeurs

1. 🔴 **Croissance illimitée déterministe** — « tout marquer comme lu » puis toute connexion recrée l'intégralité du jeu d'alertes. Aucune concurrence requise.
2. 🔴 **N+1 prouvé en `O(N × M)`** — toute la table `Notifications` rechargée pour **chaque** produit sous seuil, avec effet d'emballement.
3. 🔴 **Aucune protection multi-poste** sur la création d'alertes — ni transaction, ni contrainte.
4. 🟠 **`IsRead` surchargé** comme état métier : cause racine unique des trois précédents.
5. ✅ **Commande et paiement sont sains** — protégés par les acquis P3-6 et P3-7. **À ne pas rouvrir.**
6. ⚪ **Aucune notification atelier/QC n'existe** et aucune preuve ne justifie d'en créer.

---

# **P3-8 AUDIT = GO**

**Aucun commit. Aucun push. Aucune UI. STOP.**

---

## Décisions retenues pour l'implémentation

> Section ajoutée **après** l'implémentation backend. Elle fige les arbitrages effectivement retenus parmi les
> options comparées ci-dessus. Détail complet et preuves :
> [rapport d'implémentation P3-8](P3-8-notifications-business-rules-implementation-report.md).

| # | Décision | Option d'audit retenue | Justification déterminante |
|---|---|---|---|
| 1 | **`ResolvedAt DateTime?` retenu** | §11 option **D (+G)** | Une seule colonne nullable porte « résolue ou non » **et** la date. `IsResolved` (option C) serait redondant et pourrait diverger. Backfill = `NULL` : aucun état inventé. |
| 2 | **`IsRead` strictement orthogonal** | §11.12 | `IsRead` ne participe plus à aucun prédicat d'anti-doublon. Les quatre quadrants sont atteignables et testés, dont « lue mais active » et « résolue mais jamais consultée ». |
| 3 | **Index unique limité à `LowStock`/`Product`** | §25.5 (piège signalé) | Le filtre porte **quatre** termes : `Type = 'LowStock'`, `EntityType = 'Product'`, `EntityId IS NOT NULL`, `ResolvedAt IS NULL`. Sans la restriction de **type**, la deuxième transition d'une même commande serait refusée par la base. Test dédié. |
| 4 | **Keeper de migration déterministe** | §21.6, précisé | Ordre : **`IsRead` ASC** (priorité au non-lu) **puis `NotificationId` ASC**. L'audit ne proposait que `MIN(NotificationId)` : conserver une alerte déjà lue au détriment de la seule jamais vue effacerait l'information du badge. Critère total ⇒ reproductible. |
| 5 | **Doublons résolus à leur `CreatedAt`** | précision sur §21.7 | Jamais l'heure d'exécution de la migration, jamais une date globale. C'est une **fermeture technique** de ligne dupliquée, pas une affirmation sur une date de réapprovisionnement — qu'aucune donnée ne permet de reconstituer. **Aucune ligne supprimée.** |
| 6 | **Lecture des produits filtrée en SQL** | §25.7 | Nouveau port `GetActiveLowStockAsync` : `IsActive` **et** `StockQuantity <= StockAlertThreshold` poussés en SQL, tri par `ProductId`, aucune navigation chargée. `GetLowStockProductsAsync` (comparaison **stricte**) est laissée intacte : la fusionner déplacerait la frontière métier. |
| 7 | **Création atomique spécialisée** | §25.12, renforcé | `TryCreateActiveLowStockAsync` ⇒ `INSERT … ON CONFLICT DO NOTHING`. **Pas** `INSERT OR IGNORE` (masquerait `NOT NULL`/`CHECK`), **pas** de catch générique de `DbUpdateException`. Seules les violations d'unicité sont absorbées, en refus métier silencieux. |
| 8 | **Compteur excluant les résolues** | §11.11 / §25.18, **tranché : oui** | `CountUnreadAsync` compte `IsRead = false` **ET** `ResolvedAt IS NULL`. Répare un badge durablement faux. Les faits historiques n'étant jamais résolus, leur comptage est inchangé. |
| 9 | **Aucune notification atelier/QC** | §15 | Aucune preuve métier, aucun report la demandant. Garde d'architecture vérifiant que les constantes se limitent aux cinq types canoniques. |
| 10 | **Horloge globale non modifiée** | §25.15, **reporté** | Le point 15 de l'audit proposait d'aligner `CreatedAt` sur UTC : **écarté**. Les nouvelles résolutions suivent la convention runtime en vigueur (`DateTime.Now`) ; aucun ancien horodatage n'est réécrit. La dette 🟡-3 reste transverse et hors P3. |
| 11 | **UI et déclenchement reportés** | §26 R1/R2 | Aucun fichier `src/MMV.App/**` modifié. Le déclenchement depuis `MainWindowViewModel` est **conservé** : la génération étant devenue l'organe de résolution, la supprimer sans déclencheur de remplacement casserait la résolution. L'appel est désormais sûr et idempotent. |

### Écarts assumés par rapport au plan candidat

- **§25.14 — `MarkAsReadAsync`** : option « supprimer » retenue. `GetUnreadNotificationsAsync` supprimée également.
  Absence d'appelant vivant reconfirmée sur le code réel avant suppression (seuls des stubs de test implémentaient
  ces membres). Garde d'architecture ajoutée contre leur réintroduction.
- **§25.5 — second index `(EntityType, EntityId)`** : **non créé**. L'index unique filtré a `Type` en tête et sert
  déjà la requête des alertes actives, dont le prédicat reprend exactement ses colonnes. Un second index serait
  redondant.
- **§25.15 — alignement UTC** : reporté (cf. décision 10).
- **Ajout non prévu par l'audit** : `GenerateLowStockNotificationsResult.ResolvedCount`, purement additif — aucune
  ViewModel ne le consomme, donc aucune UI n'est modifiée — pour rendre la moitié « fermeture » de la
  réconciliation observable et testable.
