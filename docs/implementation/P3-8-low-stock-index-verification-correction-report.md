# Correctif P3-8 préalable à P3-12 — vérification physique fiable de l'index actif LowStock

> **MODE = IMPLEMENT_TARGETED_FIX_AND_WRITE_REPORT_NO_COMMIT** — aucun commit, aucun push, aucune UI, P3-12 non
> commencé.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Périmètre | Infrastructure SQLite ciblée (`SqliteDatabaseManager`) + tests + documentation |
| HEAD attendu | `8a6d5b3c1f8dc5dce80ddcc0dc35b6453844c1d4` |
| Run CI attendu | `29952285622` |
| Baseline attendue | 1479 tests |
| Répartition attendue | Domain 633 · Application 607 · App 239 |
| Date | 22 juillet 2026 |

---

## 2. État Git et CI de départ

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → 8a6d5b3c1f8dc5dce80ddcc0dc35b6453844c1d4
git rev-parse origin/…      → 8a6d5b3c1f8dc5dce80ddcc0dc35b6453844c1d4
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
git diff --check            → (vide)
```

`git log -8 --oneline` :

```
8a6d5b3 feat(P3-11): validate business acceptance journeys
8d4bf49 feat(P3-10): secure local user accounts
057df0f feat(P3-9): enforce supplier business rules
b30f774 feat(P3-8): secure business notifications
ae0e35d feat(P3-7): enforce sale business rules
53d689e feat(P3-6B): add versioned workshop sheets
a4d7380 feat(P3-6): enforce order workflow rules
3e7a1ab feat(P3-5): secure stock mutations
```

✅ Branche conforme. ✅ HEAD local = HEAD distant = SHA attendu. ✅ Aucun fichier suivi modifié avant travail.
✅ Seuls `design-handoff/`, `design/`, `docs/ui/` non suivis — tous hors périmètre et autorisés.

CI du SHA exact (`gh run view 29952285622 --json databaseId,headSha,headBranch,status,conclusion,event,url,jobs`) :

| Champ | Valeur |
|---|---|
| `databaseId` | `29952285622` |
| `headSha` | `8a6d5b3c1f8dc5dce80ddcc0dc35b6453844c1d4` |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | `success` |
| Jobs | `Restore / Build / Test / Scan` — tous les steps `success`, aucun échec |

**Porte Git/CI : ✅ PASSÉE.**

---

## 3. Baseline locale

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore MMV.sln` | OK | « All projects are up-to-date for restore. » | ✅ |
| `dotnet build --no-restore -c Debug` | 0 erreur / 0 avertissement | 0 erreur / 0 avertissement | ✅ |
| `MMV.Domain.Tests` | 633 | 633 | ✅ |
| `MMV.Application.Tests` | 607 | 607 | ✅ |
| `MMV.App.Tests` | 239 | 239 | ✅ |
| **Total** | **1479** | **1479** | ✅ |
| Échecs / ignorés | 0 / 0 | 0 / 0 | ✅ |
| `dotnet list package --vulnerable --include-transitive` | aucune | aucune sur les 7 projets | ✅ |
| `dotnet ef migrations has-pending-model-changes` | aucune | « No changes have been made to the model since the last migration. » | ✅ |
| Références `MMV.Application` | Domain uniquement | `..\MMV.Domain\MMV.Domain.csproj` | ✅ |
| Paquets `MMV.Application` | — | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 | ✅ |

**Baseline : ✅ conforme.**

---

## 4. Défaut confirmé

### 4.1 Localisation

`InspectActiveLowStockUniqueIndex` (`src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs`, avant correction)
concluait à l'unicité **réelle** de l'index `idx_notifications_active_low_stock_unique` en cherchant le mot
`UNIQUE` (insensible à la casse) dans le SQL brut de `sqlite_master` :

```csharp
var valid = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
    && sql.Contains("\"Type\"", StringComparison.Ordinal)
    && sql.Contains("\"EntityType\"", StringComparison.Ordinal)
    && sql.Contains("\"EntityId\"", StringComparison.Ordinal)
    && sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
    && sql.Contains(NotificationTypes.LowStock, StringComparison.Ordinal)
    && sql.Contains(NotificationEntityTypes.Product, StringComparison.Ordinal)
    && sql.Contains("\"EntityId\" IS NOT NULL", StringComparison.Ordinal)
    && sql.Contains("\"ResolvedAt\" IS NULL", StringComparison.Ordinal);
```

### 4.2 Piège exact

Le nom de l'index se termine **lui-même** par `_unique` :
`idx_notifications_active_low_stock_unique`. Le SQL retourné par `sqlite_master` pour un index créé par
`CREATE INDEX "idx_notifications_active_low_stock_unique" ON …` (**jamais** `CREATE UNIQUE INDEX`) contient
donc littéralement la sous-chaîne `unique` — dans le nom cité entre guillemets, pas dans un mot-clé SQL réel.
`sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)` matche indifféremment les deux cas : il ne
distingue jamais un réel `CREATE UNIQUE INDEX` d'un `CREATE INDEX` homonyme.

### 4.3 Preuve par exécution (avant correction)

Test ajouté et exécuté **avant** toute modification de production
(`PrepareDatabase_Historical_IndexHomonymNonUnique_SameColumnsAndFilter_RefusesAdoption`,
`tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs`) :

1. base historique construite par `EnsureCreated()` (schéma courant, donc `ResolvedAt` et l'index réel déjà
   présents) ;
2. l'index réel est supprimé et recréé à l'identique **sauf** le mot-clé `UNIQUE`, avec exactement les mêmes
   colonnes `(Type, EntityType, EntityId)` et exactement le même filtre `WHERE` ;
3. `manager.PrepareDatabase(context)` est appelé sur cette base historique (adoption).

**Résultat observé, sur le code non corrigé :**

```
Failed MMV.Domain.Tests.Data.SqliteDatabaseManagerTests.PrepareDatabase_Historical_IndexHomonymNonUnique_SameColumnsAndFilter_RefusesAdoption
Error Message:
 Expected a <MMV.Infrastructure.Data.DatabaseMigrationException> to be thrown, but no exception was thrown.
```

**Aucune exception n'est levée : l'adoption réussit silencieusement.** `AddNotificationResolution` est
baselinée dans `__EFMigrationsHistory` comme si la protection multi-poste des alertes de stock bas existait
physiquement, alors qu'un index non unique — donc sans aucune garantie — porte simplement le même nom. Le
défaut est confirmé **par exécution**, pas seulement par inspection statique du code.

---

## 5. Implémentation actuelle (avant correction)

Voir §4.1. Un seul point de faux positif dans tout le dépôt : recherche exhaustive
(`rg -n 'Contains\("UNIQUE"|_unique' src/MMV.Infrastructure tests/MMV.Application.Tests`, détaillée en §11)
confirme que **`InspectActiveLowStockUniqueIndex` est la seule fonction** du dépôt à décider l'unicité par
recherche textuelle du mot `UNIQUE`. La fonction sœur `InspectNormalizedUsernameUniqueIndex` (P3-10, même
fichier) utilisait déjà `PRAGMA index_list`/`index_info` correctement — c'est le modèle repris ici.

---

## 6. Contrat physique attendu

La vérification finale de l'index doit combiner trois sources distinctes, aucune ne pouvant se substituer aux
autres :

| Source | Rôle | Ce qu'elle ne peut PAS établir |
|---|---|---|
| `PRAGMA index_list('Notifications')` | Existence, unicité réelle (`unique`), caractère partiel (`partial`) | Les colonnes, le filtre |
| `PRAGMA index_info('idx_…')` | Colonnes exactes et leur ordre | L'unicité, le filtre |
| `sqlite_master.sql` | Texte du prédicat `WHERE` (seule source qui l'expose) | L'unicité (aucun mot-clé n'y fait foi) |

---

## 7. Stratégie PRAGMA

### 7.1 Existence, unicité, caractère partiel

```sql
PRAGMA index_list('Notifications');
```

Colonnes retournées par SQLite : `seq, name, unique, origin, partial`. La ligne dont `name` correspond
exactement à `idx_notifications_active_low_stock_unique` (comparaison insensible à la casse) donne :

- `exists = true` dès qu'une ligne correspond ;
- `isUnique = (colonne 2 "unique") != 0` ;
- `isPartial = (colonne 4 "partial") != 0`.

Aucune décision d'unicité ou de caractère partiel n'est plus jamais tirée du nom ou du texte SQL.

### 7.2 Colonnes et ordre

```sql
PRAGMA index_info('idx_notifications_active_low_stock_unique');
```

Colonnes retournées : `seqno, cid, name`. Les lignes sont itérées dans l'ordre naturel du curseur (`seqno`
croissant), et chaque `name` (colonne 2) est ajouté à une liste ordonnée. La validité exige :

- exactement 3 colonnes ;
- `columns[0] == "Type"`, `columns[1] == "EntityType"`, `columns[2] == "EntityId"` (comparaison insensible à
  la casse, ordre strict).

Aucune colonne auxiliaire SQLite (`cid = -1`/`-2`, réservée aux index sur expression) n'est possible ici :
l'index attendu ne porte que des colonnes réelles, jamais une expression.

### 7.3 Filtre

```sql
SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @name;
```

Utilisé **uniquement** pour vérifier la présence de `WHERE` et des quatre termes exacts du prédicat
(`Type = 'LowStock'`, `EntityType = 'Product'`, `"EntityId" IS NOT NULL`, `"ResolvedAt" IS NULL`), via les
constantes `NotificationTypes.LowStock` / `NotificationEntityTypes.Product` — jamais de littéraux recopiés.
Le mot `UNIQUE` n'est plus jamais recherché dans ce texte.

---

## 8. Vérification des colonnes

Voir §7.2. Le choix d'un ordre **strict** (`Type, EntityType, EntityId`) reflète exactement l'ordre déclaré par
`NotificationConfiguration.cs` (`HasIndex(n => new { n.Type, n.EntityType, n.EntityId })`) et par la migration
`AddNotificationResolution` (`columns: new[] { "Type", "EntityType", "EntityId" }`) : un index correctement
créé par EF Core respecte toujours cet ordre. Un index homonyme dans un ordre différent, avec une colonne en
moins ou en plus, est désormais explicitement refusé (tests T4/T5/T6, §12).

---

## 9. Vérification du filtre

Le filtre n'est vérifié que par recherche textuelle des quatre termes exacts (aucun PRAGMA SQLite n'expose la
clause `WHERE` d'un index). Aucun parseur SQL général n'a été introduit : les termes recherchés sont les mêmes
qu'avant correction, seule la décision d'unicité et de partialité change de source. Un filtre incomplet (terme
manquant) ou portant une valeur différente (`StockOut` au lieu de `LowStock`) reste refusé (tests T8/T9 — le
premier déjà couvert par un test préexistant, `PrepareDatabase_Historical_ResolvedAtPresentButIndexFilterWrong_RefusesAdoption`).

---

## 10. États partiels

Le contrat exige **simultanément** `isUnique` **et** `isPartial`. Un index réellement `UNIQUE` mais **sans**
clause `WHERE` (donc non partiel) contraindrait toutes les lignes de la table — y compris les alertes déjà
résolues et les faits historiques portant un `EntityId` — et romprait la capacité prouvée par P3-8 d'ouvrir un
nouvel épisode après résolution. Un tel index est désormais explicitement refusé (test T7, §12).

---

## 11. Adoption historique

`AdoptHistoricalDatabase` et `VerifyAfterPreparation` appellent tous deux `InspectActiveLowStockUniqueIndex` et
n'ont **pas été modifiés** : leur logique de refus (schéma partiel, historique mensonger) reposait déjà
correctement sur le couple `(Exists, Valid)` retourné par l'inspecteur. Seule la façon dont `Valid` est calculé
change. Recherche exhaustive de motifs similaires :

```
rg -n 'Contains\("UNIQUE"|Contains\(.UNIQUE.|_unique"' src/MMV.Infrastructure tests/MMV.Application.Tests
```

Résultat classé :

| Occurrence | Classement |
|---|---|
| `SqliteDatabaseManager.cs:902` (`sql.Contains("UNIQUE", …)`) | **1. Chemin P3-8 — corrigé ici** |
| Toutes les autres occurrences de `_unique` dans le dépôt | Noms d'index déclarés via `HasDatabaseName(...)` — pas de détection textuelle, non concernées |
| `SqliteSchemaVerifier.ReadUniqueIndexes` | **2. Déjà correct** — utilise `PRAGMA index_list` (colonne `unique`) et `PRAGMA index_info`, jamais de recherche textuelle du mot `UNIQUE` |
| `InspectNormalizedUsernameUniqueIndex` (P3-10, même fichier) | **2. Déjà correct** (modèle repris pour ce correctif) |

**Aucune autre dette similaire trouvée.** Aucun refactoring général n'a été engagé : seule la fonction
identifiée en 1. a été modifiée.

---

## 12. Tests ajoutés

Tous dans `tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs`, sur de vraies bases SQLite jetables
(fichier temporaire par test), jamais le provider InMemory.

| # | Test | État construit | Attendu |
|---|---|---|---|
| T2 (principal) | `PrepareDatabase_Historical_IndexHomonymNonUnique_SameColumnsAndFilter_RefusesAdoption` | `CREATE INDEX` (non unique), même nom, mêmes colonnes, même filtre | Refus (« partiel ») — **confirmé rouge avant correction, vert après** |
| T4 | `PrepareDatabase_Historical_IndexHomonymWrongColumns_RefusesAdoption` | Unique + partiel, colonnes `(Type, EntityId)` — `EntityType` manquant | Refus |
| T5 | `PrepareDatabase_Historical_IndexHomonymColumnsWrongOrder_RefusesAdoption` | Unique + partiel + bon filtre, colonnes `(EntityType, Type, EntityId)` | Refus |
| T6 | `PrepareDatabase_Historical_IndexHomonymExtraColumn_RefusesAdoption` | Unique + partiel + bon filtre, colonnes `(Type, EntityType, EntityId, IsRead)` | Refus |
| T7 | `PrepareDatabase_Historical_IndexHomonymUniqueButNotPartial_RefusesAdoption` | `CREATE UNIQUE INDEX`, bonnes colonnes/ordre, **sans** `WHERE` | Refus |
| T9 | `PrepareDatabase_Historical_IndexHomonymFilterWrongValue_RefusesAdoption` | Unique + partiel + bonnes colonnes, filtre sur `'StockOut'` au lieu de `'LowStock'` | Refus |
| T3a | `ActiveLowStockUniqueIndex_WhenGenuinelyUnique_EnforcesSingleActiveAlertPerProduct` | Index réel (migration), 2 insertions actives même produit | `SqliteException` à la 2ᵉ |
| T3b | `ActiveLowStockUniqueIndex_WhenHomonymNonUnique_AllowsDuplicateActiveAlerts_DespiteIdenticalName` | Homonyme non unique, 2 insertions actives même produit | Les deux réussissent — 2 lignes actives coexistent |
| T11 | `PrepareDatabase_ManagedDatabase_TamperedNotificationSchema_HomonymNonUniqueIndex_ThrowsInconsistentHistory` | Base **gérée** par migrations, index réel remplacé après coup par l'homonyme non unique | `DatabaseMigrationException` (historique mensonger, même hors adoption) |

**T1** (index valide accepté), **T8** (filtre incomplet), **T10** (index absent) et **T12/T13** (adoption
valide / invalide sans historique) étaient déjà couverts par des tests préexistants
(`SchemaVerifier_ValidEnsureCreatedDatabase_IsCompatible`,
`PrepareDatabase_Historical_NotificationSchemaAlreadyComplete_BaselinesWithoutReplayingAddColumn`,
`PrepareDatabase_Historical_ResolvedAtPresentButIndexFilterWrong_RefusesAdoption`,
`PrepareDatabase_Historical_ResolvedAtPresentButIndexMissing_RefusesAdoption`) et continuent de passer sans
modification. **T13** (historique absent, colonne présente, index homonyme non unique) est structurellement
identique à **T2** — même scénario, même test.

Aucun `Thread.Sleep`. Aucun test dépendant de l'ordre. Aucune simulation du résultat PRAGMA : chaque test
construit un fichier SQLite réel et laisse SQLite répondre.

---

## 13. Résultats ciblés

```
dotnet test tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj --no-restore -c Debug --filter "FullyQualifiedName~SqliteDatabaseManagerTests"
→ Passed! - Failed: 0, Passed: 33, Skipped: 0, Total: 33

dotnet test tests/MMV.Application.Tests/MMV.Application.Tests.csproj --no-restore -c Debug --filter "FullyQualifiedName~LowStock"
→ Passed! - Failed: 0, Passed: 29, Skipped: 0, Total: 29

dotnet test tests/MMV.Application.Tests/MMV.Application.Tests.csproj --no-restore -c Debug --filter "FullyQualifiedName~Sqlite"
→ Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3
```

33 = 24 tests `SqliteDatabaseManagerTests` préexistants (compte vérifié par `git show HEAD:…SqliteDatabaseManagerTests.cs | grep -c '\[Fact\]'` avant tout ajout, donnant 24) + 9 nouveaux
(T2, T4, T5, T6, T7, T9, T3a, T3b, T11).
Les 29 tests `LowStock` (`MMV.Application.Tests`) et les 3 tests `Sqlite` (`MMV.Application.Tests`)
préexistants restent verts sans modification — aucune règle métier de notification n'a été touchée.

---

## 14. Résultats complets

```
dotnet build MMV.sln --no-restore -c Debug        → 0 erreur / 0 avertissement
dotnet test tests/MMV.Domain.Tests                → Passed: 642, Failed: 0, Skipped: 0
dotnet test tests/MMV.Application.Tests           → Passed: 607, Failed: 0, Skipped: 0
dotnet test tests/MMV.App.Tests                   → Passed: 239, Failed: 0, Skipped: 0
dotnet test MMV.sln --no-build -c Debug           → 1488 au total, 0 échec, 0 ignoré
```

| Projet | Baseline | Après correctif | Δ |
|---|---:|---:|---:|
| `MMV.Domain.Tests` | 633 | **642** | +9 |
| `MMV.Application.Tests` | 607 | **607** | 0 |
| `MMV.App.Tests` | 239 | **239** | 0 |
| **Total** | **1479** | **1488** | **+9** |

`MMV.App.Tests` reste **exactement** à 239 : aucune UI touchée. `MMV.Application.Tests` reste **exactement**
à 607 : aucune règle métier de notification touchée. Le total ne devait pas être imposé à un chiffre fixe :
**+9** tests ont été ajoutés, tous dans `MMV.Domain.Tests`.

---

## 15. Sécurité et EF

| Contrôle | Résultat |
|---|---|
| `dotnet list MMV.sln package --vulnerable --include-transitive` | 0 vulnérabilité sur les 7 projets |
| `dotnet ef migrations has-pending-model-changes` | « No changes have been made to the model since the last migration. » |
| Références `MMV.Application` | `MMV.Domain` uniquement |
| Build | 0 erreur / 0 avertissement |
| Migrations créées ou modifiées | **aucune** |
| Packages ajoutés ou modifiés | **aucun** |
| Projet ajouté | **aucun** |
| UI modifiée | **aucune** |
| Requêtes SQL brutes | Toutes paramétrées (`@name`) ; aucune concaténation de valeur externe |
| `DbConnection`/`DbCommand`/readers | Correctement disposés (`using`), `finally` referme la connexion si elle a été ouverte localement |

---

## 16. Fichiers modifiés

| Fichier | Nature | Détail |
|---|---|---|
| `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` | Modifié | `InspectActiveLowStockUniqueIndex` réécrite (PRAGMA `index_list`/`index_info` pour unicité/partialité/colonnes ; `sqlite_master.sql` réservé au filtre) ; commentaire XML mis à jour et dédoublonné |
| `tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs` | Modifié | +9 tests ciblés (T2 à T11, §12) |
| `docs/implementation/P3-8-low-stock-index-verification-correction-report.md` | Créé | Ce rapport |
| `docs/implementation/P3-8-notifications-business-rules-implementation-report.md` | Modifié | Section « Correctif préalable à P3-12 » ajoutée |
| `docs/architecture/P3-business-rules-roadmap.md` | Modifié | P3-11 marqué `P3-11-CI = GO` (commit/CI définitifs) ; dette P3-8 marquée corrigée localement, sous réserve de CI ; P3-12 conditionné |

**`SqliteSchemaVerifier.cs` non modifié** : déjà correct (§11), aucune duplication du faux positif.
Aucune migration, aucun snapshot EF, aucun fichier Infrastructure autre que `SqliteDatabaseManager.cs`, aucune
UI, aucun package, aucun nouveau projet.

---

## 17. Dettes inchangées

| # | Dette | État |
|---|---|---|
| `CreateOrderUseCase` produit une commande orpheline (`SaleId = 0`) | Inchangée — hors périmètre de ce correctif |
| Entité `Order` suivie laissée « modifiée » après une transition (P3-11 §7) | Inchangée |
| Double rôle d'`Order` (fournisseur ⇄ atelier) | Inchangée |
| Règlement impossible pour une vente sans commande | Inchangée |
| Lien `Prescription ↔ Sale` absent | Inchangée |
| Auteur du QC, `Sale.StaffId`, `StockMovement.PerformedByUserId` | Inchangée |
| Horloge non injectable (`Now` ⇄ `UtcNow`) | Inchangée |
| `DeleteData(Users, UserId = 1)` sans réinsertion (`AddCounterSaleFieldsToOrder`) | Inchangée |

Aucune de ces dettes n'appartenait au périmètre de ce correctif (vérification physique de l'index LowStock
uniquement) ; aucune n'a été rouverte ni aggravée.

---

## 18. État Git final

```
 M src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs
 M tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs
 M docs/implementation/P3-8-notifications-business-rules-implementation-report.md
 M docs/architecture/P3-business-rules-roadmap.md
?? docs/implementation/P3-8-low-stock-index-verification-correction-report.md
?? design-handoff/  ?? design/  ?? docs/ui/          (hors périmètre, non touchés)
```

`git diff --stat` (fichiers suivis) : `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` (+107/−28),
`tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs` (+206), plus les deux fichiers de documentation
modifiés.

`git diff --check` : vide.

**Aucun fichier sous `src/MMV.Domain/**` (hors le fichier de test correspondant dans `tests/MMV.Domain.Tests`),
`src/MMV.Application/**`, `src/MMV.App/**`, `src/MMV.Infrastructure/Migrations/**`, ni `*ModelSnapshot.cs`.**

**Aucun commit. Aucun push.**

---

## 19. Verdict

| Condition | État |
|---|---|
| Faux positif reproduit avant correction | ✅ §4.3 — test échoue sur le code non corrigé (aucune exception levée) |
| Index homonyme non unique refusé après correction | ✅ §4.3 / §12 — même test, vert après correction |
| Unicité exclusivement issue de `PRAGMA index_list` | ✅ §7.1 |
| Caractère partiel exclusivement issu de `PRAGMA index_list` | ✅ §7.1 / §10 |
| Colonnes vérifiées par `PRAGMA index_info` | ✅ §7.2 / §8 |
| SQL utilisé uniquement pour le filtre | ✅ §7.3 / §9 |
| États historiques mensongers refusés | ✅ T11, §12 |
| Adoptions valides restent fonctionnelles | ✅ T1/T12 préexistants toujours verts |
| Aucune migration créée ou modifiée | ✅ §15 / §16 |
| Aucun changement de comportement métier Notification | ✅ §13 — 29 tests `LowStock` inchangés, verts |
| Tous les tests passent | ✅ 1488/1488, 0 échec, 0 ignoré (§14) |
| Aucune vulnérabilité | ✅ §15 |
| Aucun changement EF en attente | ✅ §15 |
| Rapport Markdown complet | ✅ ce document |
| Aucune UI modifiée | ✅ `MMV.App.Tests` exactement à 239 |

```
CORRECTIF P3-8 PRÉ-P3-12 = GO LOCAL
```

**Ce que ce verdict ne couvre pas.** Il est **local** : commit et push restent à faire, et la dette P3-8 ne
sera considérée définitivement close qu'après une CI verte sur le SHA de ce correctif. P3-12 reste **non
commencé**.

---

## Revue ciblée avant commit

> Dernière passe avant commit/push/CI (mode `TARGETED_REVIEW_FINALIZE_COMMIT_PUSH_AND_VERIFY_CI`). Portée
> strictement identique au correctif (§1) : `InspectActiveLowStockUniqueIndex` et les tests directement
> associés. Aucune autre dette ouverte, aucun Domain/Application/UI, aucune migration.

### Défaut supplémentaire trouvé

**Oui — un second défaut concret**, distinct de celui du §4, dans la même fonction. Après la correction du
§4 (unicité et partialité lues via `PRAGMA index_list`), la vérification du prédicat `WHERE` restait fondée
sur une **présence de fragments** (`sql.Contains(...)` répété quatre fois — voir §7.3/§9 tels qu'écrits avant
cette revue). Cette vérification acceptait à tort tout filtre contenant les quatre termes textuels attendus,
**quelle que soit leur relation logique réelle** : une disjonction toujours vraie, un regroupement différent,
ou une condition métier supplémentaire passaient tous la vérification.

### Résultat de F1 à F5

Cinq tests adverses ajoutés à `SqliteDatabaseManagerTests.cs`, chacun construisant un index réellement
`UNIQUE` et partiel, sur les trois bonnes colonnes dans le bon ordre — seul le texte du prédicat `WHERE`
varie :

| # | Scénario | Résultat attendu | Résultat obtenu (code corrigé) |
|---|---|---|---|
| F1 | `(quatre termes attendus) OR 1 = 1` | Refus | ✅ Refus (`DatabaseMigrationException`, « partiel ») |
| F2 | `... AND ("EntityId" IS NOT NULL OR "ResolvedAt" IS NULL)` (OR au lieu de AND) | Refus | ✅ Refus |
| F3 | `... AND "ResolvedAt" IS NULL AND "IsRead" = 0` (condition métier supplémentaire) | Refus | ✅ Refus |
| F4 | `... AND NOT ("EntityId" IS NULL) AND ...` (négation non équivalente textuellement) | Refus | ✅ Refus |
| F5 | Filtre exact de la migration, espaces superflus (variation bénigne) | Accepté | ✅ `WasAdopted = true`, aucun `ADOPT REFUSED` |

**Preuve que le défaut était réel, pas seulement théorique** : F1, F2 et F3 ont été rejoués contre l'ancienne
vérification par fragments (le code de la fonction temporairement remis à l'état `Contains(...)`, sans
modifier les tests). Résultat observé :

```
Failed ...IndexFilterF1_DisjunctionTriviallyTrue_RefusesAdoption
Failed ...IndexFilterF2_WrongLogicalGrouping_RefusesAdoption
Failed ...IndexFilterF3_ExtraBusinessCondition_RefusesAdoption
 Expected a <DatabaseMigrationException> to be thrown, but no exception was thrown.
Failed: 3, Passed: 2, Skipped: 0, Total: 5
```

F1, F2 et F3 échouaient tous les trois (adoption acceptée à tort) sur le code par fragments — confirmant que
la vérification par `Contains` était bien un faux positif exploitable, pas une inquiétude purement théorique.
F4 et F5 passaient déjà sur l'ancienne vérification (par coïncidence textuelle pour F4, par construction pour
F5) : ils ne démontrent pas le défaut, mais valident que la nouvelle méthode ne régresse pas leur verdict.
Le code a ensuite été restauré à l'implémentation corrigée (ci-dessous), et les 5 tests repassent au vert.

### Méthode exacte de comparaison du filtre

Remplacement des quatre `Contains(...)` par une **égalité littérale** entre le prédicat normalisé et un
filtre canonique attendu, sans introduire d'analyseur SQL général :

1. extraction de la sous-chaîne suivant le **premier** mot-clé `WHERE` (recherche insensible à la casse,
   un seul `WHERE` possible dans ce texte de `CREATE INDEX`) ;
2. normalisation des espaces : la sous-chaîne est découpée sur toute séquence d'espaces/tabulations/retours
   à la ligne puis rejointe par un espace unique (`ExtractNormalizedWhereClause`) ;
3. retrait d'une éventuelle paire de parenthèses extérieures **qui englobe la totalité** du prédicat
   (`StripBalancedOuterParentheses`) : la parenthèse ouvrante en position 0 doit se refermer exactement à la
   dernière position ; une parenthèse qui se referme avant la fin (donc n'englobant qu'un sous-groupe, comme
   dans F1 ou F2) n'est **jamais** retirée — condition vérifiée caractère par caractère avec un compteur de
   profondeur ;
4. comparaison **ordinale** (`string.Equals(..., StringComparison.Ordinal)`) avec le filtre exact attendu
   (`ExpectedActiveLowStockFilterClause`, construit depuis `NotificationTypes.LowStock` /
   `NotificationEntityTypes.Product` — jamais de littéraux recopiés).

Confirmé empiriquement (test diagnostique jetable, exécuté puis retiré avant tout commit) que SQLite conserve
le texte du prédicat **verbatim**, sans le reformater : `sqlite_master.sql` restitue exactement
`"Type" = 'LowStock' AND "EntityType" = 'Product' AND "EntityId" IS NOT NULL AND "ResolvedAt" IS NULL`, ce qui
rend la comparaison littérale fiable pour le filtre réellement produit par la migration et par le modèle
(`NotificationConfiguration.HasFilter(...)`, texte identique caractère pour caractère).

Aucune dépendance ajoutée, aucun changement de la migration, aucune prétention d'équivalence SQL générale
(F4 le montre explicitement : une négation logiquement équivalente reste refusée, car seule une comparaison
**syntaxique** est faite).

### Preuve que le SQL ne décide jamais de l'unicité

Inchangé depuis le §7.1 de ce rapport et reconfirmé par cette revue : `isUnique` et `isPartial` proviennent
exclusivement des colonnes `unique` et `partial` de `PRAGMA index_list('Notifications')`. Le texte de
`sqlite_master.sql` n'est lu que pour extraire le prédicat `WHERE` (étape 4 ci-dessus) ; il n'intervient à
aucun moment dans le calcul de `isUnique` ou `isPartial`. Les tests T2/T3b/T11 (homonyme non unique, même nom,
mêmes colonnes, même filtre) restent tous verts après cette revue, prouvant que la distinction repose
toujours sur PRAGMA et jamais sur le nom ou le texte SQL.

### Résultat de l'adoption valide

F5 (`PrepareDatabase_Historical_IndexFilterF5_ExactFilter_IsAdopted`) : base historique avec le filtre exact
recréé manuellement (espaces superflus inclus) → `WasAdopted = true`, journal sans aucune entrée
`ADOPT REFUSED`, `GetPendingMigrations()` vide, donnée cliente pré-existante conservée. Les tests préexistants
d'adoption valide (`SchemaVerifier_ValidEnsureCreatedDatabase_IsCompatible`,
`PrepareDatabase_Historical_NotificationSchemaAlreadyComplete_BaselinesWithoutReplayingAddColumn`) restent
verts sans modification.

### Résultat des historiques mensongers

`PrepareDatabase_ManagedDatabase_TamperedNotificationSchema_HomonymNonUniqueIndex_ThrowsInconsistentHistory`
et `PrepareDatabase_ManagedDatabase_TamperedNotificationSchema_ThrowsInconsistentHistory` (base **gérée** par
migrations, schéma altéré après coup) restent verts sans modification : la nouvelle comparaison littérale du
filtre ne change rien à ce chemin, qui dépendait déjà de `(Exists, Valid)` et non de la méthode interne de
calcul de `Valid`.

### Résultat comportemental unique/non unique

`ActiveLowStockUniqueIndex_WhenGenuinelyUnique_EnforcesSingleActiveAlertPerProduct` et
`ActiveLowStockUniqueIndex_WhenHomonymNonUnique_AllowsDuplicateActiveAlerts_DespiteIdenticalName` restent
verts sans modification : preuve comportementale inchangée (l'index réel refuse la deuxième alerte active,
l'homonyme non unique laisse les deux coexister), sur deux bases SQLite distinctes.

### Correction des nombres de tests

L'arithmétique « 33 = 25 préexistants + 9 nouveaux » du §13 était **impossible** (25 + 9 = 34 ≠ 33) et a été
corrigée en amont de cette revue (§13) : **33 = 24 préexistants + 9 nouveaux**. Le compte de 24 est vérifié
directement (`git show HEAD:tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs | grep -c '\[Fact\]'`
→ 24, sur le commit `8a6d5b3` avant tout ajout de ce correctif), pas déduit d'un texte historique.

Cette revue ajoute **5 tests nouveaux** (F1, F2, F3, F4, F5) — aucun test préexistant réutilisé pour cette
partie. Total dans la classe : **38** (24 préexistants + 9 du correctif initial + 5 de cette revue).

### Liste finale des fichiers

| Fichier | Nature |
|---|---|
| `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` | Modifié — comparaison littérale du filtre normalisé remplace les quatre `Contains(...)` |
| `tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs` | Modifié — 5 tests F1 à F5 ajoutés |
| `docs/implementation/P3-8-low-stock-index-verification-correction-report.md` | Modifié — arithmétique corrigée (§13) et cette section ajoutée |
| `docs/implementation/P3-8-notifications-business-rules-implementation-report.md` | Modifié — arithmétique corrigée, résultats locaux mis à jour |
| `docs/architecture/P3-business-rules-roadmap.md` | Modifié — section correctif P3-8 mise à jour avec le total final |

Aucun fichier `src/MMV.Domain/**`, `src/MMV.Application/**`, `src/MMV.App/**`,
`src/MMV.Infrastructure/Migrations/**`, `*ModelSnapshot.cs`, aucune UI, aucun package, aucun nouveau projet.

### Total final par projet

| Projet | Avant cette revue | Après cette revue | Δ |
|---|---:|---:|---:|
| `MMV.Domain.Tests` | 642 | **647** | +5 |
| `MMV.Application.Tests` | 607 | **607** | 0 |
| `MMV.App.Tests` | 239 | **239** | 0 |
| **Total** | **1488** | **1493** | **+5** |

0 échec, 0 ignoré. Build : 0 erreur / 0 avertissement. `dotnet list MMV.sln package --vulnerable
--include-transitive` : aucune vulnérabilité sur les 7 projets. `dotnet ef migrations
has-pending-model-changes` : « No changes have been made to the model since the last migration. »
`MMV.Application` référence exclusivement `MMV.Domain`. `MMV.App.Tests` reste exactement à 239 : aucune UI
modifiée.

### Verdict local

```
CORRECTIF P3-8 PRÉ-P3-12 = GO LOCAL
```

**Sous réserve de commit et de CI.** Le verdict précédent (§19) reste valide dans son principe ; cette section
le complète avec le défaut supplémentaire trouvé, sa correction, et les totaux définitifs (1493, et non 1488)
qui seront ceux du commit à venir.

**Aucun commit. Aucun push. P3-12 non commencé. STOP.**
