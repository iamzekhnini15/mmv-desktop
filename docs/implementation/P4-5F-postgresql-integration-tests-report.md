# P4-5F — Tests d'intégration PostgreSQL — rapport d'implémentation

> **Statut : IMPLÉMENTÉ ET VÉRIFIÉ LOCALEMENT — NON COMMITÉ — CI NON EXÉCUTÉE.**
> Branche `p4-5f-integration-tests`, créée depuis `68a3b819b99e79c7bde7998292868df25f7af6f5` (HEAD de
> `p4-net10`, identique à `origin/p4-net10`). Date : 4 octobre 2026.
> Spécification : [ADR-PROD-DB-008](../architecture/adr-prod-db-008-postgresql-integration-testing.md)
> (Q1 … Q7, corpus N1 … N8). Arbitrages architecte appliqués : **A1** (PostgreSQL 17, image exacte
> `postgres:17.10`, Q8 non tranchée), **A2** (O12 : preuve CI Linux seulement), **A3** (branche dédiée).
> **Ni la roadmap ni aucune ADR n'ont été modifiées.**

---

## 1. Synthèse

| Élément | Résultat |
|---|---|
| Projet `tests/MMV.Infrastructure.PostgreSQL.IntegrationTests` inscrit dans `MMV.sln` | ✅ (7 lignes ajoutées à la solution) |
| Corpus N1 … N8 contre PostgreSQL **17.10** réel (conteneur local jetable) | ✅ **74 / 74**, 0 échec, **0 ignoré** (mode `MMV_INTEGRATION_REQUIRED=true`) |
| Suite unitaire existante | ✅ **1953 / 1953** (App 260 · Application 628 · Domain 1065), 0 ignoré — **comptée séparément** |
| Build `MMV.sln` | ✅ 0 avertissement, 0 erreur |
| Dérive EF SQLite / PostgreSQL | ✅ « No changes have been made to the model » sur les deux chaînes |
| Migrations et instantanés | ✅ **identiques à HEAD** (`git diff` vide) — 14 SQLite + 1 PostgreSQL |
| Vulnérabilités | ✅ aucun paquet vulnérable sur les 9 projets |
| Paquet NuGet nouveau | ✅ **aucun** — les 4 références du projet existent déjà, aux mêmes versions, dans les projets de test |
| `database update` sur une base réelle | ✅ **jamais** — seules des bases `mmv_it_<guid>` jetables, supprimées (0 résiduelle vérifiée) |
| Défaut de production révélé | ⚠️ **1**, corrigé sur autorisation explicite (§6) |
| **Job CI PostgreSQL** | ⏳ **écrit, JAMAIS EXÉCUTÉ** — exige un push (non autorisé à ce stade) |

**P4-5F n'est pas `CLOSE`** : le critère « CI verte sur le SHA exact » n'est pas atteint tant que le job n'a
pas tourné sur GitHub Actions.

---

## 2. Fichiers

| Fichier | Nature |
|---|---|
| `tests/MMV.Infrastructure.PostgreSQL.IntegrationTests/**` | **nouveau** — 16 fichiers `.cs` + `.csproj` |
| `MMV.sln` | +7 lignes (entrée projet, configurations `Any CPU`, rattachement au dossier `tests`) |
| `.github/workflows/ci.yml` | job `postgresql-integration` ajouté ; job Windows : **un** `--filter` ajouté (§4.2) |
| `src/MMV.Infrastructure/Repositories/ProductRepository.cs` | **1 ligne** (§6) — seule modification de production |

Fichiers non suivis préexistants (`design/`, `design-handoff/`, `docs/ui/`,
`docs/implementation/P4-6B-implementation-plan.md`) : **non touchés**.

---

## 3. Socle (F0) — Q1, Q2, Q5, S5

- **Contrat de connexion** : `MMV_TEST_POSTGRESQL_CONNECTION_STRING`, distincte de
  `MMV_DATABASE_CONNECTION_STRING` (test dédié).
- **Anti-faux-vert, verrou local** : sans variable, `[PostgreSqlFact]` / `[PostgreSqlTheory]` ignorent le
  test avec un message actionnable (« Comportement PostgreSQL NON PROUVÉ … Renseignez … »). Vérifié :
  sans serveur, 8 tests sans serveur passent, les autres sont ignorés avec ce message.
- **Anti-faux-vert, verrou CI** : avec `MMV_INTEGRATION_REQUIRED=true`, aucun skip — un serveur absent fait
  **échouer** les tests avec un message explicite (vérifié).
- **Base jetable** : `PostgreSqlDatabase` crée `mmv_it_<guid>`, la construit par `Database.MigrateAsync()`
  à travers **`DatabaseProviderResolver.Configure`** (provider + assembly de migrations de production), puis
  la supprime (`DROP DATABASE … WITH (FORCE)`). **Une base par test** (`PostgreSqlTestBase`).
- **S5** : `NoEnsureCreatedGuardTests` refuse tout appel `EnsureCreated`/`EnsureDeleted` dans le projet ;
  sa capacité à échouer a été vérifiée par une sonde temporaire.
- xunit 2.9.3 n'offrant pas de skip dynamique, l'ignorance passe par un `FactAttribute` dérivé :
  **aucun paquet** (`Xunit.SkippableFact`) n'a été ajouté.

---

## 4. CI (F1) — Q3, Q4, Q7

### 4.1 Nouveau job `postgresql-integration`

`ubuntu-latest` (les services conteneurisés n'existent pas sur `windows-latest`), service
**`postgres:17.10`** (tag exact, vérifié présent sur Docker Hub ; c'est la version relevée par le spike
P4-1), identifiants de test éphémères non secrets, `MMV_INTEGRATION_REQUIRED=true`, version du serveur
journalisée, logger TRX, puis **garde machine sur le TRX** : échec si aucun TRX, si zéro résultat, ou si
un seul résultat n'est pas `Passed` (ignoré compris). Garde vérifiée localement sur trois cas :
tests ignorés → échec · tous verts → succès · pas de TRX → échec.

> ⏳ **Ce job n'a jamais tourné.** Sa première exécution réelle est la preuve qui manque à P4-5F.

### 4.2 Job Windows existant — une ligne changée

`dotnet test MMV.sln` exécutait désormais aussi le projet d'intégration : sans serveur, ses tests s'y
seraient ignorés et leur nombre se serait **additionné** à la baseline (interdit par Q7). Ajout de
`--filter "FullyQualifiedName!~MMV.Infrastructure.PostgreSQL.IntegrationTests"`. Vérifié : exit 0,
**1953** tests, le projet d'intégration est seulement signalé « aucun test ne correspond au filtre ».
Les étapes d'audit et de dérive EF ne sont pas modifiées.

---

## 5. Corpus N1 … N8 — ce qui est prouvé

| # | Tests | Résultat | Prouve | Obligations |
|---|---|---|---|---|
| socle | 8 | ✅ | contrat, skip, base jetable, garde S5 | Q1, Q2, Q4, Q5, S5 |
| **N1** | 2 | ✅ | deux bases neuves migrées indépendamment : **empreinte catalogue identique** (colonnes, types, défauts, identités, contraintes, index, historique) ; re-migration sans effet | ADR-005 G5, ADR-007 S4 |
| **N2** | 6 | ✅ | **schéma physique = modèle EF relationnel** : tables, chaque colonne (type + nullabilité), PK/uniques/FK **avec `ON DELETE`**, index (unicité, présence de filtre) ; **les 14 colonnes monétaires d'ADR-003 sont exactement les `numeric(12,2)`** ; prédicats exacts des **deux index filtrés** (`pg_index`) | ADR-006 X5, ADR-007 S3, ADR-003 M4 |
| **N3** | 12 | ✅ | aller-retour exact à 2 décimales (0,00 → 9 999 999 999,99) ; dépassement **rejeté** (`22003`), jamais tronqué ; montants arrondis par `Money` (ToEven) persistés inchangés ; `RemainingAmount > 0` (0,01 / 0,00 / NULL) ; `SUM(FinalAmount)` exact | ADR-003 M5, M6 |
| **N4** | 6 | ✅ | instant UTC relu en `Kind=Utc`, à la **microseconde** ; instant stocké vérifié côté serveur ; `Local`/`Unspecified` **refusés** sans rien écrire ; ordre chronologique et bornes inclusives ; `DateOnly` sans décalage | ADR-004 T7 |
| **N5** | 12 | ✅ | CRUD des **21 entités**, chaque étape dans un contexte neuf, via les repositories de production (ou l'agrégat) | critère 3 |
| **N6** | 15 | ✅ | **les 14 primitives** (matrice P4-1 Lot B §6.1) via leurs implémentations de production, cas nominal **et** cas refusé | O12 (partiel, §8), critère 5 |
| **N7** | 11 | ✅ | `25P02` après erreur, rollback, connexion réutilisable ; **les 12 `catch`** recensés par P4-5A §6 exercés au runtime (§5.1) ; stable sur **10 exécutions** | O6, critère 6 |
| **N8** | 2 | ✅ | `lower()` replie ASCII **et** majuscules accentuées ; reste **sensible aux accents** ; `LC_CTYPE` relevé et cité | ADR-006 X6 (côté PostgreSQL) |
| **Total** | **74** | ✅ | | |

### 5.1 Les 12 `catch` de P4-5A §6 au runtime

| Site | Comment il est atteint | Résultat |
|---|---|---|
| `CreateProductUseCase` — `UniqueConstraint` | un autre poste insère la même référence entre la garde et l'écriture | doublon métier stable |
| `CreateProductUseCase` — `ConstraintViolation` | le fournisseur disparaît entre la vérification et l'écriture | « fournisseur introuvable » — **la requête rouverte après rollback aboutit sur PostgreSQL** (point signalé par P4-5A) |
| `UpdateProductUseCase` — les 2 | mêmes courses sur `UpdateCatalogAsync` | idem, produit inchangé |
| `DeleteSupplierUseCase` | **vraie course à deux connexions** : insertion non validée → la suppression attend le verrou → validation → FK | `BusinessRuleException` ; une sonde prouve que c'est **le `catch`** qui répond, pas le chemin de diagnostic |
| `CreateUserUseCase`, `UpdateUserUseCase` | login concurrent | `UsernameTaken` ; la portée reste utilisable (`DetachIfTracked`) |
| `OrderRepository.CreateNextWorkshopSheetVersionAsync` | deux v1 sans version préalable, dans une transaction | `WorkshopSheetVersionConflictException`, une seule fiche |
| `EfTransactionRunner` — `catch (Exception)` | l'opération avale une erreur et continue d'écrire | `PersistenceException` (`25P02`), **rien** n'est validé, commande suivante OK |
| `EfTransactionRunner` — `SafeRollbackAsync` | **non provoqué** : le rollback d'une transaction avortée réussit sur PostgreSQL ; ce filet « best effort » n'est pas sollicité | constat |
| `UnitOfWork.CommitAsync` | violation d'unicité au commit | rollback complet, connexion réutilisable |

Les courses sont pilotées par `InterleavedCall<T>` (`DispatchProxy` de la BCL, aucun paquet) : il ne simule
aucune logique, il insère seulement l'action d'un « autre poste » avant l'appel réel au repository.

---

## 6. Défaut de production révélé et corrigé

| | |
|---|---|
| **Fichier** | `src/MMV.Infrastructure/Repositories/ProductRepository.cs` (`GetQueryable()` privé, ligne 150) |
| **Défaut** | `.Include(p => p.Category)` — `Category` est l'**enum** `ProductCategoryEnum`, pas une navigation |
| **Effet** | `InvalidOperationException` à la compilation de la requête, sur **les deux providers** (prouvé sur SQLite par sonde temporaire) |
| **Méthodes touchées** | `SearchByNameAsync`, `GetActiveProductsAsync`, `GetByReferenceAsync`, `GetByCategoryAsync`, `GetBySupplierAsync`, `GetLowStockProductsAsync` |
| **Exposition** | **latente** — aucun appelant atteignable : `ProductService` (seul appelant de deux d'entre elles) est enregistré en DI mais **aucun code ne consomme `IProductService`** |
| **Pourquoi invisible jusqu'ici** | aucun test n'appelait ces méthodes |
| **Correction** (autorisée par l'architecte) | `.Include(p => p.Category)` → `.Include(p => p.ProductCategory)` — **1 ligne**, aucun changement de modèle |
| **Régression** | `N8_product_search_folds_accented_capitals_on_active_products` : **rouge avant**, **vert après** |
| **Vérification des 6 méthodes** | sonde temporaire (non conservée, pour ne pas altérer les comptes) : **6/6 OK sur PostgreSQL et sur SQLite** |

---

## 7. Constats pour l'architecte

1. **PostgreSQL n'arrondit pas au pair.** Une valeur à plus de deux décimales est arrondie **au plus loin
   de zéro** (`0,125` → `0,13`, là où `ToEven` donne `0,12`) — test de caractérisation N3. L'arrondi
   bancaire repose **entièrement** sur `Money` ; rien n'impose ce passage aux chemins qui écrivent un
   `decimal` brut.
2. **`lower()` dépend du `LC_CTYPE` du serveur.** L'image CI est en `en_US.utf8` ; une base créée en
   `C` ne replierait pas `É`. La locale d'exploitation relève de **P4-8** (comme Q8).
3. **« Sur les deux providers » (M6, X6) : côté SQLite non couvert.** Aucun test SQLite n'exerce
   `GetTotalSalesAsync` ni la recherche `lower()`. Les ajouter modifierait la baseline 1953 : **non fait**,
   décision à prendre.
4. **Entités sans repository** : `Supplement`, `GlassSupplement`, `GlassPricingTier` n'ont pas de
   repository de production ; N5 les exerce par l'`OpticDbContext` de production. `DocumentSequence` n'est
   créée que par la migration et modifiée que par `EfNumberSequenceService`.
5. **Restrict voulu** : une commande portant une fiche atelier **ne peut pas** être supprimée (FK
   `Restrict`, `WorkshopSheetConfiguration`) — verrouillé par N5.
6. **Précision temporelle** : `timestamptz` conserve la microseconde ; les 100 ns de .NET sont perdus.
7. **`IProductService` est enregistré mais inutilisé** — signalé, non traité (hors périmètre).
8. **`dotnet sln add` (SDK 10)** ajoutait des configurations `x64`/`x86` aux 9 projets : rejeté, l'ajout a été
   fait à la main (7 lignes).

---

## 8. O12 — périmètre exact de la preuve

> **Preuve CI Linux réalisée ; preuve Windows native restant séparée.**

**Précision** : à la date de ce rapport, la preuve est réalisée **localement** contre un conteneur
PostgreSQL 17.10 (Linux, Debian 17.10-1.pgdg13+1) ; sa reproduction **en CI** n'aura lieu qu'au premier
push. **Ni ce conteneur ni la CI Linux ne prouvent quoi que ce soit de l'installation PostgreSQL native
Windows** du Lot C (`MMV-SRV`). Cette re-preuve, exigée par O12, se fera en lançant **le même projet** avec
`MMV_TEST_POSTGRESQL_CONNECTION_STRING` pointant sur `MMV-SRV` ; elle conditionne la clôture d'**O12 /
P4-4**, pas la validation technique de P4-5F (arbitrage A2).

Hors périmètre, conformément à ADR-008 §5.11 : concurrence **multi-processus**, perte réseau,
reconnexion (**P4-10**). La course N7 de `DeleteSupplier` est multi-connexion, **mono-processus**.

---

## 9. Environnement de vérification

| Élément | Valeur |
|---|---|
| SDK / runtime | .NET SDK 10.0.401, `net10.0` |
| EF Core / Npgsql | 10.0.12 / 10.0.3 |
| Serveur | conteneur `mmv-p4-5f-pg`, image `postgres:17.10` (`sha256:7958605b…`), `version()` = `PostgreSQL 17.10 (Debian 17.10-1.pgdg13+1)`, exposé sur **127.0.0.1:5434 uniquement**, mot de passe aléatoire hors dépôt, volume anonyme |
| Bases | une `mmv_it_<guid>` par test ; **0** résiduelle après la suite |

---

## 10. Critères de sortie

| Critère | État |
|---|---|
| Projet d'intégration existant et inscrit | ✅ |
| N1 à N8 verts | ✅ localement (74/74) |
| Aucun test ignoré en mode CI | ✅ localement (`MMV_INTEGRATION_REQUIRED=true`) |
| Suite existante à 1953 | ✅ |
| Migrations bit à bit inchangées | ✅ |
| Aucun `database update` sur une base réelle | ✅ |
| Contrôles de dérive verts | ✅ |
| **CI PostgreSQL réellement exécutée, verte sur le SHA exact** | ⏳ **NON** — commit + push requis |
| Rapport produit | ✅ (ce document) |

```
P4-5F IMPLEMENTATION          = DONE — LOCAL VERIFICATION PASS (74/74 + 1953/1953)
P4-5F CI                      = NOT RUN — REQUIRES COMMIT + PUSH AUTHORIZATION
P4-5F                         = NOT CLOSE
PRODUCTION FIX                = ProductRepository.cs, 1 LINE — AUTHORIZED
O12                           = LINUX PROOF LOCAL (CI PENDING) — WINDOWS NATIVE PROOF SEPARATE, NOT DONE
Q8 (CI VERSION = TARGET)      = OPEN — P4-8
P4-6B                         = NOT STARTED — REQUIRES P4-5F GREEN IN CI
```
