# ADR — Cycle de vie SQLite, chemin de base unique et adoption des migrations

> **Statut : ACCEPTÉ (P2A-1A).** Date : 11 juin 2026.
> Portée : phase `P2A-1A` (« Cycle de vie SQLite et chemin de base unique »).
> Cet ADR **opérationnalise** l'orientation différée [`adr-candidates.md` ADR-003](adr-candidates.md#adr-003)
> (« Persistance : stratégie de schéma, exécution des migrations & rollback ») et l'**Étape 1** de la
> [feuille de route de migration](migration-roadmap.md). Il ne traite **que** le cycle de vie SQLite et
> le chemin de base. Il ne décide **rien** sur le modèle monétaire (ADR-004), le concurrency token
> (ADR-010), la couche Application ni la suppression du seed de démonstration (R-16 / Étape 1F).

---

## 1. Contexte (état réel vérifié du dépôt, 11/06/2026)

Preuves relevées dans le code (et non dans d'anciens rapports) :

- **`EnsureCreated()` au runtime** : [`DbInitializer.cs:16`](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16)
  crée le schéma à partir du **modèle courant** et **contourne les 7 migrations**.
- **2 chemins physiques distincts répartis sur 4 sites de configuration** :
  | Site | Chemin | Statut runtime |
  |---|---|---|
  | [`App.axaml.cs:108`](../../src/MMV.App/App.axaml.cs#L108) | `Data Source=mmv-optic.db` (relatif) | **utilisé au runtime réel** |
  | [`OpticDbContext.OnConfiguring`](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L117-L139) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | fallback **non déclenché** (options fournies par la DI) |
  | [`OpticDbContextFactory`](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L16-L29) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | **design-time** (`dotnet ef`) |
  | [`DependencyInjection.ResolveConnectionString`](../../src/MMV.Infrastructure/DependencyInjection.cs#L53-L79) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | **jamais appelé** (`AddInfrastructure` mort) |
- **7 migrations** présentes (`20260127184542_InitialCreate` → `20260212220902_RestoreSaleOrderSeparation`)
  + `OpticDbContextModelSnapshot`.
- **Dérive modèle ↔ migrations** : `dotnet ef migrations has-pending-model-changes` répond
  *« Changes have been made to the model since the last migration »*. Analyse : cette dérive provient
  du **défaut figé `HasDefaultValue(DateTime.UtcNow)`** (anti-pattern R-19), qui change la **valeur par
  défaut** d'une colonne à chaque build — pas la **structure** (tables/colonnes/types/index). La preuve
  d'**équivalence structurelle** `Migrate()` ↔ `EnsureCreated()` est apportée par un test
  (cf. §6). Le correctif R-19 est **hors périmètre P2A-1A**.
- **Les 193 tests** existants s'appuient sur `EnsureCreated()` (in-memory / SQLite temporaire) :
  [`DbContextTests`](../../tests/MMV.Domain.Tests/Data/DbContextTests.cs), tous les `ServiceTests`,
  [`UserRepositoryTests`](../../tests/MMV.Domain.Tests/RepositoryTests/UserRepositoryTests.cs).
- **Aucune sauvegarde, aucune restauration, aucun journal de migration**, aucune détection d'une base
  historique (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`).

## 2. Problème à résoudre (et seulement celui-là)

Rendre la gestion SQLite **professionnelle et sûre** pour :

1. une **installation vierge** (aucun fichier de base) ;
2. une **base historique** créée par `EnsureCreated` **sans** `__EFMigrationsHistory` ;
3. une base **déjà gérée par migrations** (avec `__EFMigrationsHistory`) ;

avec **chemin unique**, **sauvegarde avant mutation**, **restauration**, **journal**, **échec
explicite** si la base est illisible, et **aucune suppression silencieuse de données**.

## 3. Forces & contraintes

- SQLite desktop = **un fichier** ; la restauration de données = **restauration du fichier sauvegardé**
  (ADR-003 §C : `Down()` n'est pas un mécanisme de restauration de données).
- Ne pas casser les **193 tests** (qui utilisent `EnsureCreated`).
- **Interdictions P2A-1A** : pas de migration destructive sans preuve ; pas de suppression prématurée
  d'`EnsureCreated` ; pas de modèle monétaire ; pas de refonte Application.
- Compatibilité **future SaaS** : le déclencheur de migration devra pouvoir basculer vers un pipeline
  de déploiement (ADR-003 §B option 4).

---

## 4. Options comparées

### Option 1 — Conserver `EnsureCreated()`
| Avantages | Inconvénients |
|---|---|
| Statu quo, aucun risque immédiat ; tests inchangés | **Contourne les migrations** ; évolution de schéma & migration de données **impossibles** ; ne crée jamais `__EFMigrationsHistory` ; mélange schéma/seed de démo. **Bloque R-04.** |

→ **Rejetée** : ne résout pas le problème (R-04 reste BLOCKER).

### Option 2 — Remplacer directement par `Database.Migrate()` partout
| Avantages | Inconvénients |
|---|---|
| Schéma piloté par migrations ; `__EFMigrationsHistory` présent | **Casse les bases historiques** : `Migrate()` sur une base `EnsureCreated` (tables déjà présentes, pas d'historique) **échoue** (`table already exists`) → app inutilisable, risque de perte ; **casse potentiellement les 193 tests** s'ils basculent sur `Migrate()` ; aucune sauvegarde/journal. Suppression d'`EnsureCreated` **interdite** en P2A-1A. |

→ **Rejetée** : non sûre pour les bases existantes, viole les interdictions.

### Option 3 — Adoption progressive avec détection des bases historiques *(retenue)*
Un **service de cycle de vie** au démarrage : chemin unique → sauvegarde → **détection d'état** →
(installation vierge : `Migrate()`) / (base historique sans historique : **baseline** = inscription des
migrations déjà appliquées dans `__EFMigrationsHistory`, **sans** ré-exécuter le DDL, puis migrations
restantes) / (base gérée : migrations en attente) → **journal** → **échec explicite** sinon.
`EnsureCreated` **reste** pour les tests (in-memory) ; en production il devient un **no-op** (la base
existe déjà après `Migrate`).

| Avantages | Inconvénients |
|---|---|
| Sûr pour **base vierge ET historique** ; aucune perte (baseline ne touche pas les données) ; `__EFMigrationsHistory` correct ; sauvegarde + restauration + journal ; tests préservés ; n'enfreint aucune interdiction ; prépare P2A-1B | Hypothèse : la base historique reflète le **modèle courant** (vrai pour les bases `EnsureCreated` de ce projet) ; baseline = « toutes migrations appliquées » (justifié par l'équivalence structurelle §6) ; une base à schéma **plus ancien** relève de **P2A-1B** (diagnostic de schéma). |

→ **Retenue.**

### Option 4 — Outil/service de migration dédié (installeur / pipeline) hors application
| Avantages | Inconvénients |
|---|---|
| Migration supervisée hors app ; idéal SaaS (ADR-003 §B opt. 4) | **Dépend du packaging/installeur — absent** (R-25) ; sur-dimensionné pour un desktop mono-poste aujourd'hui. |

→ **Différée** : trajectoire cible pour le SaaS ; le service de l'option 3 est **conçu pour être
réutilisé** par un futur installeur/pipeline (logique de cycle de vie isolée dans l'Infrastructure,
appelable hors UI).

---

## 5. Décision

**Option 3 — adoption progressive des migrations EF Core via un service de cycle de vie dédié au
démarrage desktop**, avec :

1. **Chemin de base unique** résolu par un seul composant
   ([`SqliteDatabasePathResolver`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs)),
   **configurable par environnement** (variable `MMV_DATABASE_PATH`, sinon défaut
   `%LOCALAPPDATA%\ManageMyVision\mmv.db`). Les 4 sites de configuration délèguent à ce résolveur.
2. **`SqliteDatabaseManager`** : ensure-directory → **sauvegarde** (copie horodatée du `.db` + `-wal`/`-shm`
   si présents) → **détection** (`Empty` / `HistoricalWithoutMigrationsHistory` / `MigrationsManaged`) →
   `Migrate()` ou **baseline + Migrate()** → **vérification** (`GetPendingMigrations()` vide) → **journal**.
3. **Échec explicite** (`DatabaseMigrationException`) si la base est illisible/corrompue ; **aucune
   suppression** du fichier (la sauvegarde et la base sont conservées pour reprise manuelle).
4. **Déclencheur** : **démarrage de l'application desktop** (ADR-003 §B option 1/2 pour desktop), la
   logique restant **réutilisable** par un installeur/pipeline (option 4) pour le futur SaaS.
5. **Rollback** : fondé sur la **sauvegarde de fichier** + **forward-fix** (ADR-003 §C), pas sur `Down()`.
6. **Seed** : le service **ne seede rien** (testé). Le seed admin/démo existant
   ([`DbInitializer`](../../src/MMV.Infrastructure/Data/DbInitializer.cs)) est **conservé inchangé**
   (suppression du seed de démo = R-16 / Étape 1F, hors périmètre).

## 5 bis. Schema compatibility gate before baseline adoption (P2A-1A-R2)

**Pourquoi l'adoption historique est dangereuse sans validation.** Baseliner = écrire dans
`__EFMigrationsHistory` que les migrations sont « déjà appliquées », **sans exécuter leur DDL**. Si le
schéma réel de la base ne correspond **pas** au modèle courant (base ancienne, partielle, modifiée à la
main, issue d'un autre produit), cette inscription transforme une base **incohérente** en base
faussement « migrée » : EF la considérerait à jour alors que des tables/colonnes/contraintes manquent →
corruption fonctionnelle silencieuse. **Une simple hypothèse « la base reflète le modèle courant » est
insuffisante.**

**Règle (obligatoire avant toute écriture d'historique).** Le service
([`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs)) :
1. la base est **déjà sauvegardée** (avant détection, cf. §5) ;
2. ouvre la base et **inspecte le schéma SQLite réel** (PRAGMA `table_info`/`foreign_key_list`/`index_list`) ;
3. **compare** au schéma **attendu** dérivé du modèle EF (`IModel.GetRelationalModel()`) ;
4. **refuse l'adoption** si une divergence significative est détectée
   (`DatabaseMigrationException`, journal motivé) ;
5. **n'écrit jamais** `__EFMigrationsHistory` si le schéma est incompatible.

**Divergences qui bloquent l'adoption** (comparaison pragmatique adaptée à SQLite — on ne signale que les
éléments **attendus manquants ou incompatibles**, les éléments **supplémentaires** sont tolérés pour
éviter les faux positifs) :
- table applicative attendue manquante ;
- colonne attendue manquante ;
- type incompatible (par **affinité SQLite** : TEXT/INTEGER/REAL/BLOB/NUMERIC) ;
- nullabilité incompatible (attendu `NOT NULL`, réel nullable) ;
- clé primaire incompatible ;
- clé étrangère essentielle manquante ;
- index unique essentiel manquant.

**Encapsulation de l'accès EF.** L'écriture de l'historique (`IHistoryRepository`) est **centralisée** en
un point unique ([`SqliteDatabaseManager.WriteMigrationHistory`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs)),
couvert par un test d'intégration **écriture + relecture** de `__EFMigrationsHistory`. La version EF Core
**n'est pas modifiée** dans cette phase.

**Vérifications post-adoption** : `__EFMigrationsHistory` présent ; aucune migration en attente ; agrégats
principaux (`Users`/`Customers`/`Products`) **réellement lisibles par EF**.

**Ce qui est différé à P2A-1B.** Diagnostic détaillé, rapport utilisateur, **stratégies de réparation**,
migration des schémas **plus anciens**, reprise de l'ancien fichier `mmv-optic.db`. **P2A-1A se contente
de refuser proprement** une base historique incompatible — il ne la « répare » pas et ne l'accepte pas.

**Récupération par l'utilisateur / le support.** En cas de refus : la base d'origine est **conservée
intacte**, une **sauvegarde horodatée** existe sous `…/backups/`, et le **journal** (`migration-journal.log`)
indique la **raison** précise. Le support peut restaurer la sauvegarde
([`SqliteDatabaseManager.Restore`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs)) et la base
incompatible sera traitée par l'outillage P2A-1B.

## 6. Conséquences & preuves attendues

- **Preuve d'équivalence structurelle** (`Migrate()` ↔ `EnsureCreated()`) : un test compare les
  tables/colonnes/types des deux schémas → identiques (hors `__EFMigrationsHistory`). C'est la **preuve**
  exigée par « aucune migration destructive sans preuve ».
- **Base vierge** : `Migrate()` crée le schéma + `__EFMigrationsHistory` + **aucune donnée démo**.
- **Base historique COMPATIBLE (sur copie)** : portail de schéma franchi → baseline →
  `__EFMigrationsHistory` complété → **données préservées**.
- **Base historique INCOMPATIBLE** (table/colonne/type/FK/index/nullabilité divergents) : portail
  **refuse** → `DatabaseMigrationException`, **aucune** inscription d'historique, base + sauvegarde
  conservées, journal motivé (testé pour chaque catégorie de divergence + cas « schéma partiellement ancien »).
- **Sauvegarde/restauration** : testées sur fichiers temporaires isolés.
- **Échec contrôlé** : fichier non-SQLite → `DatabaseMigrationException`, fichier **non supprimé**.

## 7. Risques résiduels (documentés, hors périmètre)

1. **Dérive R-19** (`HasDefaultValue(DateTime.UtcNow)`) — cosmétique (valeur par défaut), structurellement
   neutre ; correctif différé (Étape 1 / R-19).
2. **Bases à schéma antérieur/incompatible au modèle courant** — **refusées** par le portail de
   compatibilité (§5 bis), sans baseline, avec sauvegarde + journal. Leur **diagnostic, réparation et
   migration** relèvent de **P2A-1B** (non traités ici).
3. **Bascule du chemin runtime** `mmv-optic.db` → `%LOCALAPPDATA%\…\mmv.db` : un éventuel fichier
   `mmv-optic.db` préexistant (données de démo) n'est **pas supprimé** ; sa reprise éventuelle relève de
   **P2A-1B**. Le service **journalise** sa présence.
4. **Seed de démo en production** (R-16) — inchangé, différé Étape 1F.
