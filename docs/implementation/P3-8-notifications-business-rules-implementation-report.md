# P3-8 — Notifications métier — Rapport d'implémentation backend

> **MODE = IMPLEMENT_TEST_MIGRATE_AND_WRITE_REPORT_NO_COMMIT** — aucun commit, aucun push, aucune UI.

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
| Tests baseline attendus | 1134 |
| Tests après P3-8 | **1193** |

---

## 2. État Git et CI de départ

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → ae0e35d7409a7287af1881025843b26382b5ecfc
git rev-parse origin/…      → ae0e35d7409a7287af1881025843b26382b5ecfc
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
                              ?? docs/implementation/P3-8-…-audit-report.md
git diff --check            → (vide)
```

CI du SHA exact :

```json
{"databaseId":29709378954,
 "headSha":"ae0e35d7409a7287af1881025843b26382b5ecfc",
 "headBranch":"p3-business-rules",
 "event":"push","status":"completed","conclusion":"success"}
```

✅ Branche conforme. ✅ HEAD local = HEAD distant = SHA attendu. ✅ Aucun fichier suivi modifié. ✅ Seul le rapport
d'audit P3-8 est présent comme changement P3-8. ✅ Aucun fichier sous `src/MMV.App/**`. ✅ `design-handoff/`,
`design/` et `docs/ui/` restent non suivis et hors périmètre.

**Porte Git/CI : PASSÉE.**

---

## 3. Baseline

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore` | OK | up-to-date | ✅ |
| `dotnet build -c Debug` | 0 erreur / 0 avertissement | 0 erreur / 0 avertissement | ✅ |
| `MMV.Domain.Tests` | 481 | 481 | ✅ |
| `MMV.Application.Tests` | 414 | 414 | ✅ |
| `MMV.App.Tests` | 239 | 239 | ✅ |
| **Total** | **1134** | **1134** | ✅ |
| Échecs / ignorés | 0 / 0 | 0 / 0 | ✅ |
| Vulnérabilités (incl. transitives) | 0 | 0 sur les 7 projets | ✅ |
| Migrations en attente | 0 | « No changes have been made to the model since the last migration. » | ✅ |
| Références `MMV.Application` | `MMV.Domain` uniquement | `..\MMV.Domain\MMV.Domain.csproj` | ✅ |

**Baseline : CONFORME.**

---

## 4. Décisions finales

Les onze arbitrages sont consignés dans la section **« Décisions retenues pour l'implémentation »** du
[rapport d'audit](P3-8-notifications-business-rules-audit-report.md). Synthèse :

1. `ResolvedAt DateTime?` — option D de l'audit, une seule colonne nullable.
2. `IsRead` strictement orthogonal, retiré de tout prédicat d'anti-doublon.
3. Index unique **filtré** limité aux alertes `LowStock` / `Product` **non résolues** (une seule alerte active,
   c'est-à-dire `ResolvedAt IS NULL`, par produit), filtre à quatre termes.
4. Keeper de migration : `IsRead` ASC puis `NotificationId` ASC.
5. Doublons fermés à **leur propre `CreatedAt`**, aucune ligne supprimée.
6. Produits filtrés en SQL par un port dédié.
7. Création atomique `ON CONFLICT DO NOTHING`, jamais `INSERT OR IGNORE`.
8. Compteur excluant les résolues (**tranché : oui**).
9. Aucune notification atelier/QC.
10. Horloge globale non modifiée (alignement UTC **reporté**).
11. UI et déclenchement reportés ; l'appel depuis `MainWindowViewModel` est **conservé**.

---

## 5. Modèle `Notification`

`src/MMV.Domain/Entities/Notification.cs` — **un seul champ ajouté** :

```csharp
public DateTime? ResolvedAt { get; set; }
```

- ❌ Pas de `IsResolved` — deux colonnes pour un même état finiraient par diverger ; `ResolvedAt` porte
  `IS NULL`/`IS NOT NULL` **et** la date.
- ❌ Pas de clé étrangère (`EntityId` reste polymorphe, conformément au modèle existant).
- ❌ Pas de `Priority`, pas de destinataire utilisateur (reports R12 / R4).
- Publiquement lisible et mutable, conformément aux conventions de l'entité (7 propriétés `{ get; set; }`).

Le commentaire d'origine de `Type` — `// "LowStock", "StockOut", "OrderReceived", etc.` — a été remplacé par un
renvoi aux constantes. Il énumérait des littéraux recopiés, **dont un (`OrderReceived`) qui n'a jamais existé dans
le code** : illustration directe de la dérive que la centralisation supprime. Le garde-fou d'architecture l'a
détecté automatiquement.

---

## 6. Constantes

Nouveau dossier `src/MMV.Domain/Constants/` :

| Fichier | Contenu |
|---|---|
| `NotificationTypes.cs` | `LowStock`, `StockOut`, `OrderStatusChanged`, `PaymentReceived`, `Info` |
| `NotificationEntityTypes.cs` | `Product`, `Order` |

**Constantes `string`, pas un `enum`** : la colonne `Type` est un `TEXT` historique alimenté depuis P2 ; la convertir
imposerait une migration de données sans bénéfice pour ce périmètre (report R9). Vérifié par test.

Littéraux remplacés — **quatre sites** :

| Site | Remplacement |
|---|---|
| `GenerateLowStockNotificationsUseCase` | `"LowStock"`, `"Product"` |
| `AdvanceOrderStatusUseCase:173,179` | `"OrderStatusChanged"`, `"Order"` |
| `SettleOrderBalanceUseCase:134,138` | `"PaymentReceived"`, `"Order"` |
| `DbInitializer` (seed) | `"LowStock"`, `"StockOut"`, `"Info"` ×2, `"Product"` ×2 |

Un test `[Theory]` scanne l'intégralité de `src/**` (hors `bin`/`obj` et hors migrations) et échoue si l'un des
quatre types canoniques réapparaît en littéral ailleurs que dans sa déclaration. Les migrations sont exclues
délibérément : une migration est un **instantané historique figé**, ses littéraux SQL ne doivent jamais être
réécrits pour suivre une constante.

---

## 7. Clé métier

**`(Type, EntityType, EntityId)` restreinte aux alertes non résolues, et uniquement pour `LowStock`/`Product`.**

| Composant | Dans la clé | Motif |
|---|---|---|
| `Type` | ✅ | Discrimine la famille d'alerte |
| `EntityType` | ✅ **ajouté** | `EntityId` est polymorphe : « 42 » désigne le produit 42 **ou** la commande 42 |
| `EntityId` | ✅ | Cible de l'alerte |
| `ResolvedAt IS NULL` | ✅ | Ne contraint que les alertes **actives** — c'est ce qui autorise l'historique |
| `IsRead` | ❌ **retiré** | État de lecture utilisateur, jamais état métier |

---

## 8. Migration

`src/MMV.Infrastructure/Migrations/20260720204330_AddNotificationResolution.cs` — **une seule migration**, additive,
en trois temps dont l'ordre est contraint :

1. `AddColumn<DateTime>("ResolvedAt", "Notifications", type: "TEXT", nullable: true)` ;
2. **dédoublonnage déterministe** des `LowStock`/`Product` historiques (§9) ;
3. création de l'index unique filtré (§10).

L'étape 2 ne peut pas être omise : sur toute base ayant vécu, le défaut corrigé produisait un doublon à chaque
connexion suivant un « tout marquer comme lu ». Créer l'index avant de dédoublonner échouerait.

**Aucune ancienne migration modifiée.** Seul `OpticDbContextModelSnapshot.cs` est régénéré, comme l'exige EF.

### 8.1 Down

```
DropIndex(idx_notifications_active_low_stock_unique) → DropColumn(ResolvedAt)
```

**Perte d'information assumée et documentée honnêtement** : supprimer `ResolvedAt` efface la distinction
active/terminée, donc **les doublons historiques fermés par le `Up` redeviennent implicitement actifs** — l'ancien
schéma ne sait pas les représenter autrement. Rejouer le `Up` les refermerait à l'identique sur leur `CreatedAt`.
Aucune ligne n'est perdue dans un sens comme dans l'autre. Vérifié par test (10 lignes avant, 10 après, 3 alertes
`LowStock` sur le produit 10 restaurées).

---

## 9. Normalisation historique

Pour chaque groupe `(Type = 'LowStock', EntityType = 'Product', même EntityId non nul)`, **exactement une** ligne
reste active.

**Ordre de sélection du gardien :**

1. **`IsRead` ASC** — priorité au **non-lu**. Stocké en `INTEGER` 0/1, donc `0` (non lue) trie avant `1`.
2. **`NotificationId` ASC** — départage : la plus ancienne l'emporte.

> **Écart argumenté par rapport au plan candidat.** L'audit (§21.6) proposait `MIN(NotificationId)` seul. Ce critère
> peut conserver une alerte **déjà lue** et fermer la **seule jamais vue** — l'information disparaîtrait alors du
> badge sans que personne ne l'ait consultée. La priorité au non-lu corrige cela ; le second critère rend l'ordre
> **total**, donc reproductible à l'identique sur toute base indépendamment de l'ordre physique des lignes.

Toutes les autres lignes du groupe reçoivent **`ResolvedAt = CreatedAt`**.

Sens exact de cette valeur :

- fermeture **technique** d'une ligne dupliquée ;
- **pas** une affirmation sur la date réelle de réapprovisionnement — qu'aucune donnée en base ne permet de
  reconstituer ;
- l'heure d'exécution de la migration inventerait un événement métier collectif qui n'a jamais eu lieu ;
- `CreatedAt` maintient chaque doublon à sa propre place dans l'historique ;
- réparation déterministe et reproductible.

**Aucune notification n'est supprimée.** Les faits historiques (`OrderStatusChanged`, `PaymentReceived`, `Info`,
`StockOut`) ne sont jamais touchés : le `WHERE` de l'`UPDATE` les exclut.

SQL (`ROW_NUMBER() OVER (PARTITION BY "EntityId" ORDER BY "IsRead" ASC, "NotificationId" ASC)`) — SQLite ≥ 3.25,
prérequis déjà impliqué par `ON CONFLICT` (≥ 3.24).

---

## 10. Index filtré

```sql
CREATE UNIQUE INDEX "idx_notifications_active_low_stock_unique"
ON "Notifications" ("Type", "EntityType", "EntityId")
WHERE "Type" = 'LowStock'
  AND "EntityType" = 'Product'
  AND "EntityId" IS NOT NULL
  AND "ResolvedAt" IS NULL;
```

Chacun des quatre termes est indispensable :

| Terme | Ce qu'il protège |
|---|---|
| `Type = 'LowStock'` | **Le piège principal.** Sans lui, l'index contraindrait les faits historiques : une commande traverse jusqu'à **quatre** transitions, donc quatre `OrderStatusChanged` sur le même `EntityId`. La base refuserait la deuxième. |
| `EntityType = 'Product'` | `EntityId` est polymorphe et sans clé étrangère |
| `EntityId IS NOT NULL` | Les `Info` du seed n'ont pas d'entité liée |
| `ResolvedAt IS NULL` | Autorise l'historique : une seconde pénurie ouvre légitimement une nouvelle alerte |

**Aucun second index `(EntityType, EntityId)` n'a été créé** : l'index ci-dessus a `Type` en colonne de tête et son
prédicat reprend exactement celui de `GetActiveLowStockEntityIdsAsync`, qu'il sert donc déjà. En ajouter un second
« par principe » serait redondant.

Déclaré côté modèle via `HasIndex(...).IsUnique().HasFilter(...)`, donc également créé par `EnsureCreated()` — les
tests d'usage métier bénéficient de la même contrainte que la production.

---

## 11. Repository produits

Ajout au port `IProductRepository` :

```csharp
Task<IReadOnlyList<Product>> GetActiveLowStockAsync(CancellationToken cancellationToken = default);
```

Implémentation (`ProductRepository`) :

- `IsActive` **et** `StockQuantity <= StockAlertThreshold` filtrés **en SQL** ;
- `AsNoTracking()` ;
- `OrderBy(p => p.ProductId)` — déterministe et stable, indépendant du stock ;
- **n'emprunte pas** le `GetQueryable()` privé, qui charge cinq navigations (catégorie, fournisseur, détails
  verre/lentille/accessoire) dont la génération d'alertes n'utilise aucune.

> **`GetLowStockProductsAsync` est laissée intacte.** Elle applique une comparaison **stricte** (`<`) et trie par
> quantité : elle sert l'écran de réapprovisionnement. Les fusionner déplacerait silencieusement la frontière
> métier — un produit exactement au seuil est alerté depuis toujours (test dédié qui fige `<=`).

Le `GetAllAsync()` suivi d'un filtrage Application a disparu.

---

## 12. Repository notifications

`INotificationRepository` après P3-8 :

| Méthode | État |
|---|---|
| `MarkAllAsReadAsync` | conservée, inchangée (ensembliste, atomique, idempotente) |
| `CountUnreadAsync` | **modifiée** — `!IsRead && ResolvedAt == null` |
| `GetActiveLowStockEntityIdsAsync` | **ajoutée** |
| `ResolveActiveLowStockAsync` | **ajoutée** |
| `TryCreateActiveLowStockAsync` | **ajoutée** |
| `GetUnreadNotificationsAsync` | **supprimée** (code mort) |
| `MarkAsReadAsync` | **supprimée** (code mort et latent-défectueux) |

**Lecture des clés actives** — filtre SQL sur les quatre colonnes de l'index, `Select(n => n.EntityId!.Value)`,
`Distinct()`, `AsNoTracking()`. Aucun titre, aucun message, aucune entité complète matérialisée.

**Résolution ensembliste** — un seul `ExecuteUpdateAsync` conditionné par `ResolvedAt == null` pour tout le lot.
Collection vide ⇒ **retour 0 sans émettre de requête** (cas fréquent en régime permanent). La condition rend l'appel
idempotent : une seconde passe ne réécrit pas la date déjà posée (test dédié).

Le port reste **provider-neutre** : aucune signature n'expose SQL, EF ou SQLite (test dédié).

---

## 13. Création atomique

```csharp
INSERT INTO "Notifications" (…, "ResolvedAt")
VALUES (@type, @title, @message, @entityId, @entityType, @isRead, @createdAt, NULL)
ON CONFLICT DO NOTHING;
```

Retour : `true` si 1 ligne créée, `false` si une alerte active existait déjà.

**Ce qui a été délibérément évité :**

| Rejeté | Motif |
|---|---|
| `AnyAsync` puis `Add` | Laisse une fenêtre entre lecture et écriture — la course multi-poste d'origine |
| `NOT EXISTS` sans index unique | Ne ferme pas la course : rien ne garantit l'unicité au moment de l'écriture |
| `INSERT OR IGNORE` | Masquerait **aussi** les violations `NOT NULL`, `CHECK` et de clé étrangère — un bug d'écriture deviendrait une ligne manquante indétectable |
| `catch (DbUpdateException)` générique | Absorberait n'importe quelle erreur de persistance |

`ON CONFLICT DO NOTHING` (forme upsert, SQLite ≥ 3.24) n'absorbe **que** les violations d'unicité ; sans cible
explicite il s'applique à toutes les contraintes d'unicité, **index partiels compris**. Toute autre violation
continue de produire l'exception Infrastructure normale.

**Aucun message SQLite ou EF ne remonte à Application** : le contrat est un `bool`.

**Garde de contrat** : la primitive rejette (`ArgumentException`) toute notification qui ne serait pas
`LowStock`/`Product` avec `EntityId` non nul. L'employer pour un fait historique donnerait l'illusion d'une
protection que l'index filtré n'apporte pas à ce type. Test dédié.

---

## 14. Réconciliation

`GenerateLowStockNotificationsUseCase` — **corps entièrement réécrit**, contrat de résultat préservé (un champ
additif). L'exécution complète tient dans un unique `ITransactionRunner` (dépendance ajoutée au constructeur ;
`ITransactionRunner` était déjà enregistré en DI — **aucune modification de composition root nécessaire**).

Ordre exécuté :

1. `GetActiveLowStockAsync` — **une** requête ;
2. `HashSet<long>` des `ProductId` sous seuil ;
3. `GetActiveLowStockEntityIdsAsync` — **une** requête ;
4. `HashSet<long>` des alertes actives ;
5. différence symétrique en mémoire (O(1) par élément) :
   - `idsToResolve = activeAlertIds − lowStockProductIds`,
   - `productsToCreate = lowStockProducts − activeAlertIds` ;
6. `ResolveActiveLowStockAsync` — **au plus une** mise à jour ;
7. `TryCreateActiveLowStockAsync` par alerte manquante ;
8. `created` incrémenté **uniquement** sur `true` — un `false` signifie qu'un autre poste a gagné la course et ne
   doit pas être annoncé comme une création ;
9. `CountUnreadAsync` — **une** requête, après réconciliation ;
10. commit par le runner.

Fermeture et ouverture sont **tout-ou-rien** : une réconciliation partiellement appliquée laisserait la base dans un
état qu'aucune exécution ultérieure ne distinguerait d'un état normal.

`ResolvedCount` ajouté au résultat : purement additif, aucune ViewModel ne le consomme (**donc aucune UI modifiée**),
et il rend la moitié « fermeture » observable et testable au même titre que `CreatedCount`.

---

## 15. Résolution

Le recalcul ensembliste traite **quatre causes sans aucun code dédié à chacune** :

| Cause | Mécanisme | Test |
|---|---|---|
| Réapprovisionnement | le produit sort de la population « sous seuil » | ✅ |
| Produit désactivé | exclu par `IsActive` dans le port produit | ✅ |
| Produit absent | son `EntityId` n'est dans aucune population | ✅ |
| Seuil abaissé sous le stock | la condition devient fausse | ✅ |

Horodatage : `DateTime.Now`, **convention runtime en vigueur** pour les notifications. Aucun ancien horodatage
n'est réécrit ; aucune horloge injectable n'est introduite (aucun pattern existant du dépôt ne le permettait sans
ouvrir un chantier transverse). La dette 🟡-3 (`Now` vs `UtcNow`) reste documentée et hors P3.

L'alerte est **conservée**, jamais supprimée.

---

## 16. Nouvel épisode

Séquence prouvée par test :

| Étape | Action | État |
|---|---|---|
| 1 | stock 1 / seuil 5 → réconcilier | alerte **v1** active, non lue |
| 2 | stock → 50, réconcilier | **v1 résolue**, conservée |
| 3 | stock → 2, réconcilier | **v2** créée, active, non lue |

Résultat : **2 lignes**, dont **exactement une active**. C'est la capacité que le « Plan MINIMAL » (anti-doublon à
vie) aurait sacrifiée : une seconde pénurie serait passée silencieuse.

---

## 17. Suppression du N+1

| | Avant | Après |
|---|---|---|
| Lecture produits | 1 (`GetAllAsync`, toutes navigations, filtrage en mémoire) | 1 (filtré en SQL, sans navigation) |
| Anti-doublon | **N** chargements **intégraux** de `Notifications` | **1** projection d'identifiants |
| Résolution | inexistante | ≤ 1 |
| Création | `Add` + 1 `SaveChanges` | 1 insertion atomique par **nouvelle** alerte |
| Compteur | 1 | 1 |
| **Complexité** | **`O(N × M)`**, M non borné | **`O(P + K)`** |

Test avec `DbCommandInterceptor` : exécution avec **1** produit puis avec **12** produits sur bases neuves ⇒

- nombre de `SELECT` **identique** ;
- nombre de `SELECT` touchant `Notifications` identique et **≤ 2** ;
- aucun `SELECT *` ;
- nombre d'`INSERT` **strictement supérieur** avec 12 produits — les écritures croissent légitimement, seul le
  volume de **lectures** devait devenir constant.

---

## 18. Lecture et compteur

**Compteur** — `CountUnreadAsync` compte désormais `IsRead = false` **ET** `ResolvedAt IS NULL`.

- ⚠️ **Valeur observable par l'UI modifiée** — assumé explicitement (décision 8). Un badge doit refléter des
  problèmes **actifs** ; avant P3-8 une alerte sur un produit réapprovisionné depuis des mois restait comptée à vie.
- Les faits historiques n'étant jamais résolus, leur comptage est **strictement inchangé** (test dédié).

**Liste** — `ListNotificationsUseCase` renvoie toujours l'**historique intégral**, tri décroissant préservé. Aucune
notification résolue n'est filtrée. Le DTO expose `ResolvedAt` et l'indicateur dérivé `IsResolved` pour le futur
redesign ; **aucun filtrage n'en découle**.

---

## 19. Marquage comme lu

`MarkAllNotificationsReadUseCase` et `MarkAllAsReadAsync` : **inchangés**. Déjà ensemblistes (`ExecuteUpdateAsync`),
atomiques, idempotents et multi-poste sûrs.

Leur unique défaut — influencer l'anti-doublon — **disparaît sans les toucher**, par le seul retrait de `IsRead` du
prédicat de réconciliation.

Deux tests verrouillent le point : après `MarkAllAsRead`, `ResolvedAt` reste `NULL`, et une réconciliation ultérieure
ne crée rien.

---

## 20. Commandes et paiements

**Aucune modification fonctionnelle.** `AdvanceOrderStatusUseCase` et `SettleOrderBalanceUseCase` ne diffèrent que
par le remplacement de deux chaînes littérales chacun (`Type`, `EntityType`) et l'ajout d'un `using`. CAS,
transactions, messages, `EntityId`, contrats : intacts.

Leurs garanties d'unicité restent acquises par le CAS de statut (P3-6) et la prise atomique (P3-7).

Non-régression prouvée :

- une transition réussie ⇒ **1** notification (tests existants, textes vérifiés au caractère près) ;
- **plusieurs transitions d'une même commande restent autorisées** ⇒ 2 `OrderStatusChanged` sur le même `EntityId`
  (test P3-8 dédié — c'est ce que l'index aurait cassé si son filtre n'avait pas restreint le type) ;
- rejeu d'une transition consommée ⇒ conflit ⇒ **0** notification supplémentaire ;
- règlement réussi ⇒ 1 notification ; rejeu ⇒ 0 supplémentaire (`SettleOrderBalanceAtomicityTests`, verts sans
  modification) ;
- l'index `LowStock` ne bloque aucun fait historique (test dédié : 4 `OrderStatusChanged` + 2 `PaymentReceived` +
  3 `Info` sur les mêmes clés).

### Dette documentée, non corrigée

`INotificationRepository` reste injecté en **optionnel** (`= null`) dans ces deux use cases (🟡-11). En composition
root réelle il **est** enregistré, donc le comportement de production est « notification toujours créée ». La
nullabilité est une facilité de test qui rend le contrat silencieusement optionnel. **Conservée en l'état pour
P3-8** : la corriger rouvrirait les matrices P3-6/P3-7, explicitement interdit.

---

## 21. Code mort

Recherche préalable (`git grep -n "MarkAsReadAsync\|GetUnreadNotificationsAsync" -- src tests`) : **aucun appelant
vivant**. Les seules occurrences étaient des implémentations de stub dans deux fakes de test — confirmation directe
par la compilation, qui n'a signalé que ces stubs.

Supprimées de l'interface **et** du repository :

- `MarkAsReadAsync(long)` — mort **et latent-défectueux** : il déléguait à `UpdateAsync`, qui se contente de
  `_dbSet.Update(entity)` **sans `SaveChangesAsync`**. Appelé hors d'un use case qui commit, il n'aurait **rien
  persisté** — un piège pour un futur appelant ;
- `GetUnreadNotificationsAsync()` — mort (correctement écrite, jamais appelée).

`MarkAllAsReadAsync` **conservée** (déjà ensembliste, atomique, idempotente).

**Aucun use case individuel de lecture n'a été créé.** Garde d'architecture ajoutée contre la réintroduction des
deux méthodes.

---

## 22. Concurrence

| Acte | Protection après P3-8 |
|---|---|
| Génération d'alerte | ✅ `ITransactionRunner` + insertion atomique + index unique filtré |
| Résolution | ✅ `ExecuteUpdate` conditionné par `ResolvedAt IS NULL` (idempotent) |
| `MarkAllAsRead` | ✅ inchangé — UPDATE conditionnel ensembliste |
| Transition de commande | ✅ CAS P3-6, inchangé |
| Règlement de solde | ✅ prise atomique P3-7, inchangé |

### Honnêteté du protocole de test

**SQLite sérialise les écritures concurrentes sur un même fichier** : deux `INSERT` véritablement simultanés
produiraient un **verrou**, pas une course observable. Le scénario dangereux est donc reproduit fidèlement mais
**séquentiellement**, au niveau de la primitive atomique :

1. deux connexions **distinctes** observent la même absence d'alerte (`GetActiveLowStockEntityIdsAsync` ⇒ vide pour
   les deux) ;
2. puis chacune tente l'insertion.

Résultat : `true` / `false`, **une seule** alerte active, **aucune exception technique**. C'est exactement
l'entrelacement « deux postes se connectent en même temps » de l'audit, et c'est ce que l'implémentation d'origine
(`AnyAsync` puis `Add`) échouait à empêcher.

**Aucun `Thread.Sleep`, aucun délai fragile.**

---

## 23. Fichiers créés / modifiés / supprimés

### Créés (11)

Le total corrige une erreur d'énumération du rapport initial (« Créés (9) » alors que dix fichiers étaient déjà
listés) et inclut le fichier ajouté par la revue ciblée avant commit (dernière ligne — voir « Revue ciblée avant
commit » ci-dessous).

| Fichier | Rôle |
|---|---|
| `src/MMV.Domain/Constants/NotificationTypes.cs` | Types canoniques |
| `src/MMV.Domain/Constants/NotificationEntityTypes.cs` | Types d'entité |
| `src/MMV.Infrastructure/Migrations/20260720204330_AddNotificationResolution.cs` | Migration |
| `src/MMV.Infrastructure/Migrations/20260720204330_AddNotificationResolution.Designer.cs` | Migration (EF) |
| `tests/MMV.Application.Tests/UseCases/Notifications/LowStockReconciliationTests.cs` | Règles métier |
| `tests/MMV.Application.Tests/UseCases/Notifications/LowStockConcurrencyAndQueryCountTests.cs` | Concurrence + N+1 |
| `tests/MMV.Application.Tests/UseCases/Notifications/EventNotificationsNonRegressionTests.cs` | Non-régression commandes |
| `tests/MMV.Application.Tests/Architecture/NotificationsApplicationArchitectureTests.cs` | Gardes Application |
| `tests/MMV.Domain.Tests/Data/NotificationResolutionMigrationTests.cs` | Migration |
| `tests/MMV.Domain.Tests/ServiceTests/NotificationsArchitectureTests.cs` | Gardes Domain |
| `tests/MMV.Application.Tests/UseCases/Notifications/LowStockTransactionalEnrollmentTests.cs` | Enrôlement transactionnel (revue ciblée) |

### Modifiés (20)

**Domain (3)** — `Entities/Notification.cs`, `Interfaces/Repositories/INotificationRepository.cs`,
`Interfaces/Repositories/IProductRepository.cs`.

**Application (6)** — `Notifications/GenerateLowStockNotifications/{UseCase,Result}.cs`,
`Notifications/ListNotifications/{ListNotificationsUseCase,NotificationListItemDto}.cs`,
`Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs` (constantes + `using`),
`Orders/SettleOrderBalance/SettleOrderBalanceUseCase.cs` (constantes + `using`).

**Infrastructure (7)** — `Data/Configurations/NotificationConfiguration.cs`, `Data/DbInitializer.cs` (constantes),
`Data/SqliteSchemaVerifier.cs`, `Migrations/OpticDbContextModelSnapshot.cs` (régénéré),
`Repositories/NotificationRepository.cs`, `Repositories/ProductRepository.cs`,
`Data/SqliteDatabaseManager.cs` (revue ciblée — voir ci-dessous).

**Tests (4)** — `NotificationUseCasesTests.cs` (nouveau constructeur),
`SettleOrderBalanceUseCaseTests.cs` (stub d'interface), `RegisterSaleProductAcquisitionOrderTests.cs` (stub
d'interface), `tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs` (revue ciblée — voir ci-dessous).

### Supprimés

Aucun fichier. Deux **membres** d'interface et leurs implémentations (§21).

### Non touchés

`src/MMV.App/**`, `design-handoff/**`, `design/**`, `docs/ui/**`, toute migration antérieure.

Sur `SqliteSchemaVerifier` : modification **strictement nécessaire** à l'adoption. Sans elle, une base historique
serait déclarée incompatible pour cause de colonne `Notifications.ResolvedAt` et d'index unique manquants, alors
que la migration qui les crée est précisément **exécutée** pendant l'adoption. Deux entrées ajoutées aux ensembles
de tolérance existants, selon le mécanisme déjà en place pour P2A-1E / P3-2B / P3-4B — **aucun élargissement de
baseline automatique**. `SqliteDatabaseManager` inspecté : **aucune modification nécessaire**.

---

## 24. Tests ajoutés

**+59 tests** (0 échec, 0 ignoré).

| Fichier | Tests | Couverture |
|---|---:|---|
| `LowStockReconciliationTests` | **20** | Génération, anti-doublon, résolution, épisodes, seuils, compteur, liste |
| `NotificationResolutionMigrationTests` | **16** | Base neuve, adoption, dédoublonnage, contrainte, rollback |
| `NotificationsArchitectureTests` | **11** | Modèle, constantes, littéraux, couches, code mort, atelier/QC |
| `LowStockConcurrencyAndQueryCountTests` | **5** | Concurrence, index, garde de contrat, N+1 |
| `NotificationsApplicationArchitectureTests` | **4** | Couche Application, DTO, transaction |
| `EventNotificationsNonRegressionTests` | **3** | Transitions multiples, rejeu, neutralité des constantes |

Tests métier obligatoires — **tous couverts** : produit sous seuil ; stock **égal** au seuil ; stock zéro (une seule
`LowStock`, jamais `StockOut`) ; actif au-dessus du seuil ; inactif sous seuil ; deuxième génération sans
changement ; **générer → marquer lu → générer** ; `MarkAllAsRead` n'affecte pas `ResolvedAt` ; réapprovisionnement ;
désactivation ; produit absent ; alerte résolue conservée dans la liste ; résolue non lue exclue du compteur ;
active lue non recréée ; seconde réconciliation idempotente ; nouvel épisode complet ; seuil abaissé ; seuil
relevé ; N+1 ; concurrence ; compteur et lecture ; commandes et paiements ; architecture.

---

## 25. Tests migration

Sur **vraies bases SQLite jetables**, via le pipeline de migrations réel (`Database.Migrate()`), jamais le provider
InMemory.

**Base neuve** — colonne `ResolvedAt` présente ; index unique filtré présent avec ses quatre termes ; modèle EF
aligné (`SqliteSchemaVerifier` ⇒ 0 divergence) ; migration inscrite dans `__EFMigrationsHistory` ; script de la
seule migration P3-8 ne touchant **aucune** table métier (`Customers`, `Products`, `Sales`, `SaleItems`, `Orders`,
`OrderItems`, `Prescriptions`, `StockMovements`, `WorkshopSheets`).

**Adoption depuis l'état précédent** — base migrée jusqu'à `AddWorkshopSheets`, absence de `ResolvedAt` vérifiée,
puis insertion d'un jeu hérité représentatif :

| Ids | Contenu | Attendu |
|---|---|---|
| 1, 2, 3 | produit 10 : deux alertes **lues**, une **non lue** (id 3, la **plus grande**) | gardien = **3** — le non-lu l'emporte sur l'ancienneté |
| 4, 5 | produit 20 : deux alertes **lues** | gardien = **4** — plus petit `NotificationId` |
| 6, 7, 8 | trois `OrderStatusChanged`, même commande | intacts, jamais résolus |
| 9, 10 | deux `Info` sans `EntityId` | intacts |

Vérifié après migration : exactement **une** alerte active par produit ; le gardien du produit 10 est bien **non
lu** ; départage par plus petit id confirmé sur le produit 20 ; les trois lignes fermées portent
`ResolvedAt == CreatedAt` ; **10 lignes avant, 10 après** ; faits historiques survivants et non résolus ; historique
EF enregistré.

**Contrainte après migration** — seconde alerte active sur la même clé ⇒ `SqliteException` ; alerte **résolue**
supplémentaire ⇒ acceptée ; `OrderStatusChanged`, `PaymentReceived` et `Info` multiples ⇒ acceptés.

**Rollback** — index et colonne supprimés, 10 lignes conservées, 3 alertes `LowStock` du produit 10 restaurées
(redevenues implicitement actives, cf. §8.1).

---

## 26. Résultats ciblés

```
dotnet test tests/MMV.Domain.Tests       → Passed: 508, Failed: 0, Skipped: 0
dotnet test tests/MMV.Application.Tests  → Passed: 446, Failed: 0, Skipped: 0
```

| Classe P3-8 | Résultat |
|---|---|
| `LowStockReconciliationTests` | 20 / 20 ✅ |
| `NotificationResolutionMigrationTests` | 16 / 16 ✅ |
| `NotificationsArchitectureTests` | 11 / 11 ✅ |
| `LowStockConcurrencyAndQueryCountTests` | 5 / 5 ✅ |
| `NotificationsApplicationArchitectureTests` | 4 / 4 ✅ |
| `EventNotificationsNonRegressionTests` | 3 / 3 ✅ |

---

## 27. Résultat complet

| Projet | Baseline | Après P3-8 | Δ |
|---|---:|---:|---:|
| `MMV.Domain.Tests` | 481 | **508** | +27 |
| `MMV.Application.Tests` | 414 | **446** | +32 |
| `MMV.App.Tests` | 239 | **239** | 0 |
| **Total** | **1134** | **1193** | **+59** |

```
Passed!  - Failed: 0, Passed: 239, Skipped: 0, Total: 239 - MMV.App.Tests.dll
Passed!  - Failed: 0, Passed: 446, Skipped: 0, Total: 446 - MMV.Application.Tests.dll
Passed!  - Failed: 0, Passed: 508, Skipped: 0, Total: 508 - MMV.Domain.Tests.dll
```

Build : **0 erreur / 0 avertissement**.

---

## 28. Sécurité et EF

| Contrôle | Résultat |
|---|---|
| `dotnet list package --vulnerable --include-transitive` | **0 vulnérabilité** sur les 7 projets |
| `dotnet ef migrations has-pending-model-changes` | « No changes have been made to the model since the last migration. » |
| `git diff --check` | (vide) |
| Injection SQL | Le seul SQL brut runtime est l'insertion atomique — **entièrement paramétrée** (`SqliteParameter`) ; aucune concaténation de valeur |
| SQL de migration | Constantes littérales, aucune entrée externe |
| `ExecuteUpdateAsync` | Prédicats LINQ traduits par EF, jamais de chaîne construite |
| Messages techniques | Aucun message SQLite/EF ne franchit la frontière Infrastructure → Application |

---

## 29. Architecture

- **Domain** — ne référence ni EF Core ni SQLite (test). Reçoit deux classes de constantes et un champ ; aucune
  logique de persistance.
- **Application** — ne référence ni EF Core ni SQLite (test). Orchestre des primitives **ensemblistes du port** ;
  `ExecuteUpdate` et `ON CONFLICT` restent invisibles depuis cette couche.
- **Ports provider-neutres** — aucune signature de `INotificationRepository` n'expose SQL, EF ou SQLite (test).
- **Infrastructure** — seule détentrice du SQL ; l'unique dépendance ajoutée est `Microsoft.Data.Sqlite` dans le
  repository, déjà présente transitivement.
- **Composition root** — **aucune modification** : `ITransactionRunner` était déjà enregistré en `Scoped`, et le use
  case est enregistré par type.
- **Constantes centralisées**, aucun littéral runtime dupliqué (test `[Theory]` par balayage des sources).
- **Aucune notification atelier/QC**, aucun use case de suppression ou de purge (tests).
- **Aucun fichier UI modifié.**

---

## 30. Reports explicites

| # | Sujet | Motif | Destination |
|---|---|---|---|
| R1 | Panneau, filtres, badge | Interdiction absolue de modifier l'UI en P3-8 | Redesign global UI |
| R2 | Génération déclenchée par le chemin de **lecture** du compteur (🟠-9) | Exigerait de modifier `MainWindowViewModel`. ⚠️ Et la génération **doit rester appelée** : elle est devenue l'organe de résolution. L'appel est désormais **sûr et idempotent** | Redesign global UI |
| R3 | Email / push / temps réel | Hors P3 (roadmap) | Hors P3 |
| R4 | Destinataires utilisateur | Modèle sans champ utilisateur | P3-10 |
| R5 | Notifications atelier / QC | **Aucune preuve métier** | Redesign métier futur |
| R6 | Purge / rétention | La volumétrie était causée par le doublon, corrigé | Futur |
| R7 | Localisation | Aucun besoin exprimé | Futur |
| R8 | Planificateur / génération périodique | Le déclenchement à la connexion suffit ; **aucun scheduler créé** | Hors périmètre |
| R9 | `Type` en `enum` + migration de données | Les constantes suffisent | Futur |
| R10 | Pagination des lectures | Superflue une fois la croissance illimitée corrigée | Futur |
| R11 | `PaymentReceived.EntityId` → `SaleId` | Rouvrirait P3-7 | Redesign métier futur |
| R12 | Champ `Priority` | N'a jamais existé | Futur |
| R13 | Provider serveur, index filtrés SQL Server (réserve `SET`) | `adr-prod-db-001` | Chantier production |
| R14 | Observabilité / logs | Aucun cadre en place | Futur |
| R15 | Dépendance Application → libellés UI (🟡-12) | Rouvrirait la matrice P3-6 | Redesign métier futur |
| R16 | **Normalisation `CreatedAt` (`Now` vs `UtcNow`, 🟡-3)** | Dette transverse ; l'aligner modifierait l'affichage des nouvelles lignes | Hors P3 |
| R17 | **`INotificationRepository` optionnel dans commande/paiement (🟡-11)** | Rouvrirait les matrices P3-6/P3-7 | Redesign métier futur |
| R18 | `StockOut` orphelin (🟡-7) | Conservé pour la lisibilité des bases de démonstration | Futur |

---

## 31. État Git final

```
 M src/MMV.Application/UseCases/Notifications/GenerateLowStockNotifications/GenerateLowStockNotificationsResult.cs
 M src/MMV.Application/UseCases/Notifications/GenerateLowStockNotifications/GenerateLowStockNotificationsUseCase.cs
 M src/MMV.Application/UseCases/Notifications/ListNotifications/ListNotificationsUseCase.cs
 M src/MMV.Application/UseCases/Notifications/ListNotifications/NotificationListItemDto.cs
 M src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs
 M src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceUseCase.cs
 M src/MMV.Domain/Entities/Notification.cs
 M src/MMV.Domain/Interfaces/Repositories/INotificationRepository.cs
 M src/MMV.Domain/Interfaces/Repositories/IProductRepository.cs
 M src/MMV.Infrastructure/Data/Configurations/NotificationConfiguration.cs
 M src/MMV.Infrastructure/Data/DbInitializer.cs
 M src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs
 M src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs
 M src/MMV.Infrastructure/Repositories/NotificationRepository.cs
 M src/MMV.Infrastructure/Repositories/ProductRepository.cs
 M tests/MMV.Application.Tests/UseCases/Notifications/NotificationUseCasesTests.cs
 M tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceUseCaseTests.cs
 M tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleProductAcquisitionOrderTests.cs
?? src/MMV.Domain/Constants/
?? src/MMV.Infrastructure/Migrations/20260720204330_AddNotificationResolution.cs
?? src/MMV.Infrastructure/Migrations/20260720204330_AddNotificationResolution.Designer.cs
?? tests/MMV.Application.Tests/Architecture/NotificationsApplicationArchitectureTests.cs
?? tests/MMV.Application.Tests/UseCases/Notifications/EventNotificationsNonRegressionTests.cs
?? tests/MMV.Application.Tests/UseCases/Notifications/LowStockConcurrencyAndQueryCountTests.cs
?? tests/MMV.Application.Tests/UseCases/Notifications/LowStockReconciliationTests.cs
?? tests/MMV.Domain.Tests/Data/NotificationResolutionMigrationTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/NotificationsArchitectureTests.cs
?? design-handoff/  ?? design/  ?? docs/ui/          (hors périmètre, non touchés)
```

`git diff --stat` : **18 fichiers suivis, +417 / −73**.

✅ Aucun fichier sous `src/MMV.App/**`. ✅ Une seule nouvelle migration. ✅ Aucune ancienne migration modifiée.
✅ `design-handoff/`, `design/`, `docs/ui/` intacts. ✅ **Aucun commit, aucun push.**

---

## 32. Verdict

| Critère (§20 de l'énoncé) | Statut |
|---|---|
| `IsRead` ne participe plus à l'anti-doublon | ✅ |
| `ResolvedAt` distingue la résolution | ✅ |
| Un seul épisode actif par produit | ✅ index unique filtré + tests |
| Marquer lu ne recrée aucune alerte | ✅ test prioritaire |
| Nouvelle baisse après résolution ⇒ nouvelle alerte | ✅ |
| N+1 supprimé | ✅ intercepteur EF |
| Produits inactifs exclus | ✅ |
| Alertes obsolètes résolues | ✅ 4 causes |
| Création concurrente sans doublon | ✅ |
| Migration normalise sans suppression | ✅ 10 lignes avant/après |
| Commande / paiement inchangés | ✅ |
| Tous les tests passent | ✅ 1202 après revue ciblée (1193 + 9), 0 échec, 0 ignoré |
| Aucune vulnérabilité | ✅ |
| Aucune migration en attente | ✅ |
| Rapport `.md` présent | ✅ |
| Aucune UI modifiée | ✅ |

---

## Revue ciblée avant commit

> Section ajoutée après une revue ciblée (migration + primitive atomique + enrôlement transactionnel + fenêtre de
> mesure du N+1), en amont du commit. Périmètre strictement limité aux quatre points ci-dessous — aucune revue
> générale, aucune deuxième migration, aucune UI.

### 1. Adoption partielle de la migration `AddNotificationResolution`

**Défaut concret trouvé et corrigé.** `SqliteDatabaseManager.AdoptHistoricalDatabase` traitait déjà l'adoption
partielle de quatre migrations additives antérieures (R-19, `AddDocumentSequences`, `AddCustomerArchivingAndProtect
History`, `AddWorkshopSheets`) par un physical check dédié : si l'effet physique manque, la migration est **exécutée**
plutôt que **baselinée**. **`AddNotificationResolution` n'avait reçu aucun traitement équivalent** — zéro référence à
`Notification` dans tout le fichier. Conséquence : une base historique antérieure à P3-8 (colonne `ResolvedAt` et
index absents, tolérés par `SqliteSchemaVerifier` au titre des ensembles additifs) aurait été **baselinée sans que la
migration s'exécute jamais** — `__EFMigrationsHistory` aurait affirmé la protection multi-poste des alertes de stock
bas appliquée alors qu'elle n'existerait pas physiquement (historique mensonger), sans qu'aucun test existant ne le
détecte : les tests d'adoption existants (`PrepareDatabase_HistoricalDatabaseOnCopy_IsAdopted_WithoutDataLoss`) et de
migration (`NotificationResolutionMigrationTests`) partent tous d'un état où le schéma P3-8 est déjà physiquement
complet (`EnsureCreated()` construit toujours le modèle courant) ou passent par le pipeline de migration direct —
aucun n'exerçait `SqliteDatabaseManager` sur une base réellement antérieure à P3-8.

**États testés physiquement** (colonne via `PRAGMA table_info`, index via `sqlite_master.sql` — jamais le seul nom) :

| Historique | Colonne `ResolvedAt` | Index exact | Comportement implémenté | Preuve |
|---|---|---|---|---|
| absent | absent | absent | migration **exécutée** normalement | `PrepareDatabase_Historical_MissingNotificationResolutionSchema_ExecutesMigration_WithoutDataLoss` |
| absent | présent | présent | **baseline** sans rejouer `AddColumn` | `PrepareDatabase_Historical_NotificationSchemaAlreadyComplete_BaselinesWithoutReplayingAddColumn` |
| absent | présent | absent | refus explicite (« schéma partiel ») | `PrepareDatabase_Historical_ResolvedAtPresentButIndexMissing_RefusesAdoption` |
| absent | présent | index présent mais filtre incomplet (sans restriction de type) | refus explicite | `PrepareDatabase_Historical_ResolvedAtPresentButIndexFilterWrong_RefusesAdoption` |
| absent | absent | index homonyme sans rapport | refus explicite (« incohérent ») | `PrepareDatabase_Historical_IndexHomonymPresentButResolvedAtMissing_RefusesAdoption` |
| présent (historique) | absent/index invalide après altération manuelle | — | refus explicite, même hors adoption (base déjà gérée par migrations) | `PrepareDatabase_ManagedDatabase_TamperedNotificationSchema_ThrowsInconsistentHistory` |

La vérification physique (`InspectActiveLowStockUniqueIndex`) lit le texte SQL réel de l'index (`sqlite_master.sql`)
et non les seules colonnes de `PRAGMA index_info`, qui n'exposent pas la clause `WHERE` — condition nécessaire pour
distinguer un index correctement filtré d'un homonyme mal défini (état qu'une migration transactionnelle normale ne
peut jamais produire, mais qu'une base altérée à la main peut présenter ; testé comme tel). La garantie de cohérence
colonne/index a en outre été rendue **inconditionnelle** dans `VerifyAfterPreparation` (partagée par les trois
chemins — installation neuve, base gérée, adoption), pour couvrir aussi une base déjà migrée puis altérée après
coup, cas qu'un test d'adoption seul ne peut pas reproduire.

**Fichiers touchés** : `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` (+~110 lignes),
`tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs` (+6 tests).

### 2. Primitive atomique `TryCreateActiveLowStockAsync`

**Aucun défaut trouvé sur le contrat existant.** Vérification physique des contraintes uniques réellement présentes
sur `Notifications` (`NotificationConfiguration.cs`) : une seule — l'index unique filtré P3-8. `ON CONFLICT DO
NOTHING` sans cible explicite ne peut donc absorber **aucune** autre violation d'unicité que celle visée ; employer
une forme ciblée (`ON CONFLICT ("Type","EntityType","EntityId") WHERE ... DO NOTHING`) n'aurait changé aucun
comportement observable sur le schéma actuel — non fait, conformément à l'instruction de ne pas modifier sur une
hypothèse future non prouvée par le schéma.

**Preuve complémentaire ajoutée** : une violation `NOT NULL` (`Title = null!`) n'est **pas** absorbée en `false`.
Le client ADO.NET (`Microsoft.Data.Sqlite`) rejette en réalité le paramètre non affecté par une
`InvalidOperationException` avant même d'atteindre le moteur SQLite (jamais un `SqliteException` de contrainte) —
comportement documenté et vérifié : ce qui importe pour le contrat (jamais absorbé, jamais confondu avec un refus
métier `false`) est prouvé indépendamment du type d'exception exact.

**Valeur de retour** : déjà prouvée par les tests existants (`DeuxTentativesFondeesSurLaMemeAbsence_
NeCreentQuUneSeuleAlerte` — `rowsAffected == 1` puis `== 0`), aucune dépendance à un comportement non testé du
provider.

**Fichier touché** : `tests/MMV.Application.Tests/UseCases/Notifications/LowStockConcurrencyAndQueryCountTests.cs`
(+1 test).

### 3. Enrôlement transactionnel

**Trou de preuve trouvé et comblé.** Le rapport affirmait l'atomicité résolution+création sans test dédié prouvant
que l'insertion SQL brute (`ExecuteSqlRawAsync`, hors suivi du `ChangeTracker` EF) et l'`ExecuteUpdateAsync`
appartiennent à la **même** transaction ouverte par `EfTransactionRunner`. Nouveau fichier
`LowStockTransactionalEnrollmentTests.cs` (2 tests) pilotant directement `EfTransactionRunner` + `NotificationRepository`
(sans passer par le use case, pour provoquer une exception déterministe après les deux écritures et avant le commit) :

- une résolution (produit A) et une création (produit B) suivies d'une exception volontaire avant commit
  laissent, après relecture depuis un **contexte neuf**, l'alerte A toujours active et l'alerte B inexistante — les
  deux écritures appartenaient bien à une seule transaction ;
- cas nominal (sans exception) : les deux sont commitées.

**Fichier créé** : `tests/MMV.Application.Tests/UseCases/Notifications/LowStockTransactionalEnrollmentTests.cs`.

### 4. Fenêtre de mesure du N+1

**Aucun défaut trouvé.** `LowStockConcurrencyAndQueryCountTests.MeasureAsync` crée l'intercepteur EF et le contexte
qui l'utilise **seulement** pour l'appel à `useCase.ExecuteAsync()` ; le seed (schéma, fournisseur, produits) passe
par des contextes distincts sans intercepteur, créés et disposés avant. Le compteur de commandes ne peut donc déjà
contenir aucune commande de préparation ou d'assertion — la reformulation littérale demandée par la revue
(« réinitialiser immédiatement avant, lire immédiatement après ») est **structurellement déjà vraie** par isolation
de contexte. Aucune modification.

### Résultats de la revue ciblée

```
dotnet build MMV.sln --no-restore -c Debug        → 0 erreur / 0 avertissement
dotnet test tests/MMV.Domain.Tests                → Passed: 514, Failed: 0, Skipped: 0
dotnet test tests/MMV.Application.Tests           → Passed: 449, Failed: 0, Skipped: 0
dotnet test tests/MMV.App.Tests                   → Passed: 239, Failed: 0, Skipped: 0
dotnet list MMV.sln package --vulnerable --include-transitive → 0 vulnérabilité (7 projets)
dotnet ef migrations has-pending-model-changes    → « No changes have been made to the model since the last migration. »
dotnet list MMV.Application reference             → MMV.Domain uniquement
git diff --check                                  → aucune erreur (avertissements CRLF/LF bénins uniquement)
```

**+9 tests** par rapport à l'implémentation initiale (1193 → 1202) : 6 sur l'adoption partielle P3-8
(`SqliteDatabaseManagerTests`), 1 sur la violation `NOT NULL` de la primitive atomique, 2 sur l'enrôlement
transactionnel. Aucun test existant modifié ni supprimé.

## Préparation du commit

| Contrôle | Valeur |
|---|---|
| HEAD de départ | `ae0e35d7409a7287af1881025843b26382b5ecfc` |
| CI de départ | run `29709378954` — `push`, `completed`, `success` |
| Total tests final | **1202** (Domain 514, Application 449, App 239), 0 échec, 0 ignoré |
| Build | 0 erreur / 0 avertissement |
| Sécurité | 0 vulnérabilité (7 projets, transitives incluses) |
| État EF | aucune migration en attente |
| Migrations P3-8 | une seule — `20260720204330_AddNotificationResolution` — aucune ancienne migration modifiée |
| UI | aucun fichier sous `src/MMV.App/**` modifié |
| Verdict local | **P3-8 = GO LOCAL** (confirmé après revue ciblée) |
| Étape suivante | commit isolé, push `p3-business-rules`, vérification CI du SHA exact — **sous réserve de la CI** |

---

## Correctif préalable à P3-12 — vérification physique de l'index LowStock

> Section ajoutée lors d'un correctif ciblé pré-P3-12, découvert et traité pendant la recette P3-11 (note
> roadmap). Périmètre strictement limité à `InspectActiveLowStockUniqueIndex` — aucune migration, aucun
> changement fonctionnel de Notifications, aucune UI. Détail complet, matrice d'états et preuves : [rapport de
> correction dédié](P3-8-low-stock-index-verification-correction-report.md).

### Défaut exact

`InspectActiveLowStockUniqueIndex` (`src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs`) concluait à
l'unicité réelle de l'index `idx_notifications_active_low_stock_unique` en cherchant le mot `UNIQUE`
(insensible à la casse) dans le SQL brut de `sqlite_master` :

```csharp
var valid = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) && …
```

Le nom de l'index se termine **lui-même** par `_unique`. Un index créé par `CREATE INDEX` (jamais
`CREATE UNIQUE INDEX`) portant ce même nom fait donc correspondre `sql.Contains("UNIQUE", ...)` sur le nom
cité entre guillemets dans le texte SQL — jamais sur un réel mot-clé `UNIQUE` — et était accepté à tort.

### Preuve du faux positif

Reproduit avant correction par un test réel (`PrepareDatabase_Historical_IndexHomonymNonUnique_SameColumnsAndFilter_RefusesAdoption`,
exécuté sur le code non corrigé) : un index **non unique**, même nom, mêmes colonnes, même filtre, était
accepté silencieusement — `PrepareDatabase` adoptait la base et inscrivait `AddNotificationResolution` dans
`__EFMigrationsHistory` sans lever d'exception, alors qu'aucune protection multi-poste n'existait
physiquement. Comportement observé confirmé : `Failed MMV.Domain.Tests.Data.SqliteDatabaseManagerTests.…
[FAIL] Expected a DatabaseMigrationException to be thrown, but no exception was thrown.`

### Remplacement de la recherche textuelle par PRAGMA

- **`PRAGMA index_list('Notifications')`** — autoritaire pour l'existence, le drapeau `unique` (colonne 2) et
  le drapeau `partial` (colonne 4). Ni l'un ni l'autre n'est plus jamais déduit du texte SQL ou du nom.
- **`PRAGMA index_info('idx_notifications_active_low_stock_unique')`** — colonnes et ordre exact
  `(Type, EntityType, EntityId)`, comparés un à un.
- **`sqlite_master.sql`** — utilisé **uniquement** pour vérifier le prédicat `WHERE` (aucun PRAGMA ne
  l'expose) : présence de `WHERE`, des littéraux `LowStock`/`Product` et des deux conditions
  `"EntityId" IS NOT NULL` / `"ResolvedAt" IS NULL`.

### Matrice des états testés

9 tests **nouveaux** (T2, T4, T5, T6, T7, T9, T3a, T3b, T11 — `SqliteDatabaseManagerTests.cs`) : homonyme non
unique mêmes colonnes/filtre (refusé — le test principal du correctif) ; mauvaises colonnes ; ordre des
colonnes différent ; colonne supplémentaire ; unique mais non partiel (sans `WHERE`) ; filtre pointant sur une
mauvaise valeur (`StockOut` au lieu de `LowStock`) ; preuve comportementale, 2 tests (l'index réel refuse une
deuxième alerte active, l'homonyme non unique laisse les deux coexister) ; historique mensonger sur base déjà
gérée par migrations (homonyme non unique après un `Migrate()` réel). Le cas « index valide (accepté) » (T1)
n'est **pas** un test nouveau : il était déjà couvert par un test préexistant
(`SchemaVerifier_ValidEnsureCreatedDatabase_IsCompatible`), qui continue de passer sans modification.

### Absence de migration, absence de changement fonctionnel Notifications

Aucune migration créée ni modifiée. Aucune règle métier de notification modifiée : seule la **méthode de
vérification physique** de la protection déjà décidée en P3-8 change. `MMV.Domain`, `MMV.Application`,
`MMV.App` non touchés.

### Résultats locaux

- `SqliteDatabaseManagerTests` : 33/33 verts (24 existants + 9 nouveaux — dont `T2` couvre aussi le cas
  d'adoption invalide history-absent/colonne-présente/index-homonyme). Le compte de 24 préexistants est
  vérifié directement (`git show HEAD:…SqliteDatabaseManagerTests.cs | grep -c '\[Fact\]'`), pas déduit d'un
  texte historique.
- Solution après le correctif initial : **1488 tests** (Domain 642 [+9], Application 607 [inchangé],
  App 239 [inchangé]), 0 échec, 0 ignoré.
- **Revue ciblée avant commit** (voir [rapport de correction
  dédié](P3-8-low-stock-index-verification-correction-report.md#revue-ciblée-avant-commit)) : un second défaut
  a été trouvé et corrigé dans `InspectActiveLowStockUniqueIndex` — le prédicat `WHERE` était validé par
  simple présence de fragments (`Contains`), acceptant à tort une disjonction, un regroupement différent ou
  une condition métier supplémentaire contenant les mêmes quatre termes. 5 tests supplémentaires ajoutés
  (`SqliteDatabaseManagerTests` : 38/38 verts, 24 existants + 9 + 5 nouveaux). **Total final : 1493 tests**
  (Domain 647, Application 607, App 239), 0 échec, 0 ignoré.
- 0 vulnérabilité (7 projets) ; aucun `pending model change` ; `MMV.Application` toujours pure (Domain
  uniquement).

**Sous réserve de commit et de CI** — voir [rapport de correction
dédié](P3-8-low-stock-index-verification-correction-report.md) pour le détail complet et le verdict.

---

# **P3-8 = GO LOCAL**

**Aucun commit. Aucun push. Aucune UI. STOP.**
