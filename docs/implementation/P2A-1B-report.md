# P2A-1B — Migration et diagnostic des données historiques — RAPPORT D'IMPLÉMENTATION

> **Date : 11 juin 2026.** Branche : `phase2a-stabilization`.
> Périmètre **strictement limité à `P2A-1B`** ([roadmap §P2A-1B](../roadmap/MMV-master-professionalization-roadmap.md),
> [migration-roadmap §Étape 1](../architecture/migration-roadmap.md)). S'appuie sur l'ADR opérationnel
> [`adr-sqlite-lifecycle.md`](../architecture/adr-sqlite-lifecycle.md) (§5 bis « différé à P2A-1B » et
> risques résiduels §7.2/§7.3) et sur [`P2A-1A-report.md`](P2A-1A-report.md) (risques résiduels §16.2).
> **Aucun modèle monétaire, aucune couche Application, aucune entité métier, aucune valeur réglementaire,
> aucune migration EF, aucun `DbInitializer`/configuration EF n'a été touché.** Aucune phase `P2A-1C+` commencée.

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-1B
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit, aucun
push**. Le changeset reste dans l'arbre de travail.

## 2. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-1A` = GO définitif | [P2A-1A-report.md §19](P2A-1A-report.md) : commit `a85bc51`, CI run #27332553670 vert, 218/218 | ✅ |
| Dépôt propre au démarrage | `git status --short` **vide** | ✅ |
| Baseline reproductible | restore/build OK, **218/218**, **0 vuln** (cf. §4) | ✅ |
| SDK piloté par `global.json` | `dotnet --version` = **8.0.417** | ✅ |
| Infrastructure de cycle de vie P2A-1A présente | `SqliteDatabaseManager`, `SqliteSchemaVerifier`, `SqliteDatabasePathResolver`, `MigrationJournal` lus | ✅ |

Aucun prérequis manquant → poursuite autorisée (pas de `NO-GO` de prérequis).

## 3. État Git initial

- Branche : `phase2a-stabilization` · `HEAD` : `334b728 docs(P2A-1A): record green CI run 27332553670 (GO definitif)`
- `git status --short` : **vide** (arbre propre).
- `git diff` / `git diff --stat` : **vides**.

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | **Succès** |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur** (1 avert. préexistant `CS1998` [OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **218 ✅ / 0 ❌** (App **79** ; Domain **139**) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list … --no-build --no-connect` | **7 migrations** (`InitialCreate` → `RestoreSaleOrderSeparation`) |
| `dotnet ef migrations has-pending-model-changes … --no-build` | **true** (« Changes have been made to the model… ») — bruit R-19, cf. §11 |

> Note outillage : `dotnet-ef` est un **outil global** (`10.0.2`) non exposé sur le `PATH` de la session ;
> il est invoqué via `~/.dotnet/tools/dotnet-ef.exe`. Le ciblage du projet `MMV.Infrastructure` (net8.0)
> reste correct.

## 5. Analyse obligatoire (recherche dans le dépôt réel)

Recherche globale (`grep` sur `*.cs`). Pour chaque élément : fichier, rôle, risque, décision P2A-1B.

| Élément | Fichier(s) | Rôle | Risque | Décision P2A-1B |
|---|---|---|---|---|
| `mmv-optic.db` | [App.axaml.cs:107](../../src/MMV.App/App.axaml.cs#L107) (commentaire) ; aucune chaîne de connexion active | **Ancien** chemin runtime relatif (avant P2A-1A) | Fichier de données réelles **orphelin** après l'unification du chemin | **Détecté, diagnostiqué et repris** par `LegacyDatabaseRecoveryService` (nouveau) ; jamais supprimé/écrasé |
| `mmv.db` | [SqliteDatabasePathResolver.cs:26](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs#L26) | **Nouveau** chemin courant unique (`%LOCALAPPDATA%\ManageMyVision\mmv.db`) | — | Cible de la reprise ; diagnostiqué avant toute copie |
| `__EFMigrationsHistory` | [SqliteDatabaseManager.cs](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) (lecture/écriture encapsulée) | Table d'historique des migrations EF | Une base historique n'en a pas | Le diagnostic la lit (présence + migrations inscrites) ; **jamais écrite** par le diagnostic (lecture seule) |
| `EnsureCreated` | [DbInitializer.cs:16](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16) + tests | Crée le schéma au runtime/aux tests (contourne les migrations) | Produit des bases **historiques sans historique** | **Inchangé** (interdiction de suppression). Source des bases que P2A-1B sait diagnostiquer/adopter |
| `.Migrate()` | [SqliteDatabaseManager.cs:221,239,298](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L221) | Applique/baseline les migrations | — | **Réutilisé** (le service de reprise délègue l'adoption au manager P2A-1A) |
| `SqliteDatabaseManager` | [SqliteDatabaseManager.cs](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) | Cycle de vie (sauvegarde/détection/adoption/restauration) | — | **Réutilisé** (`Backup`, `PrepareDatabase`, `Journal`) — pas de duplication |
| `SqliteSchemaVerifier` | [SqliteSchemaVerifier.cs](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs) | Portail de compatibilité de schéma (P2A-1A-R2) | Faux positif/négatif théorique (cf. §15) | **Réutilisé** par le diagnostic pour classer compatible/incompatible |
| `DbInitializer` | [DbInitializer.cs](../../src/MMV.Infrastructure/Data/DbInitializer.cs) | Seed admin + démo (R-16) | Données de démo en prod | **Inchangé** (hors périmètre, Étape 1F) |
| `HasDefaultValue(DateTime.UtcNow)` | `UserConfiguration:44`, `CustomerConfiguration:49,52`, `SaleConfiguration:26`, `OrderConfiguration:26`, `PrescriptionConfiguration:72`, `StockMovementConfiguration:31` (7 occurrences, 6 fichiers) | Valeur par défaut figée au build (R-19) | `has-pending-model-changes` = true ; horodatage erroné | **Documenté** (§11) ; **non corrigé** (hors périmètre, aucune migration créée) |

## 6. Diagnostic de l'ancien `mmv-optic.db`

Composant ajouté : [`SqliteHistoricalDatabaseDiagnostic`](../../src/MMV.Infrastructure/Data/SqliteHistoricalDatabaseDiagnostic.cs) 
(lecture seule). `Diagnose(OpticDbContext)` produit `HistoricalDatabaseDiagnosticResult` :

- **chemin** de la base ; **existence** du fichier ; **taille** (octets) ; **date de modification** (UTC) ;
- **validité SQLite** ; **présence de tables applicatives** ; **présence de `__EFMigrationsHistory`** ;
- **migrations appliquées** (lues dans `__EFMigrationsHistory`) ; **migrations manquantes** (définies − appliquées) ;
- **compatibilité avec `SqliteSchemaVerifier`** + **divergences** détectées ;
- **nombre de lignes par table principale** (`Users`, `Customers`, `Products`, `Prescriptions`, `Orders`, `Sales`) — **métadonnée uniquement** ;
- **décision recommandée** (`HistoricalDatabaseDecision`) :
  `NoDatabaseFound` · `RejectInvalidDatabase` · `UseAsCurrentDatabase` ·
  `AdoptCompatibleHistoricalDatabase` · `CopyLegacyDatabaseToCurrentPath` · `ManualRepairRequired`.

**Lecture seule garantie** : si le fichier est absent, `Diagnose` retourne `NoDatabaseFound` **sans ouvrir
de connexion** (donc **sans créer le fichier**) ; si le fichier est illisible, la `SqliteException` est
capturée → `RejectInvalidDatabase` **sans modifier les octets** (testé). **Aucune donnée personnelle** n'est
lue ni journalisée : seules des **métadonnées techniques** (noms de tables, comptes, identifiants de migration).

**Cas particulier traité — bascule de chemin runtime (P2A-1A résiduel #2/#3).** L'ancien runtime utilisait
`Data Source=mmv-optic.db` (relatif au dossier de travail) ; le nouveau utilise
`%LOCALAPPDATA%\ManageMyVision\mmv.db`. Le chemin de l'ancien fichier est résolu par
[`SqliteDatabasePathResolver.ResolveLegacyDatabasePath()`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs)
(constante `LegacyRelativeFileName = "mmv-optic.db"`).

## 7. Diagnostic du nouveau chemin `mmv.db`

Le même diagnostic (lecture seule) est appliqué au chemin courant **avant toute reprise**, par
[`LegacyDatabaseRecoveryService`](../../src/MMV.Infrastructure/Data/LegacyDatabaseRecoveryService.cs). Il
distingue : chemin courant **absent** (`NoDatabaseFound`, feu vert à la copie), **présent** (base existante
→ **conflit**, cf. §8), gérée par migrations (`UseAsCurrentDatabase`), ou historique compatible
(`AdoptCompatibleHistoricalDatabase`).

## 8. Stratégie de reprise (procédure contrôlée)

Orchestrée par `LegacyDatabaseRecoveryService.Recover(legacyPath, currentPath, contextFactory)`. Matrice de décision :

| Ancien `mmv-optic.db` | Chemin courant `mmv.db` | Décision | Copie | Sauvegarde |
|---|---|---|---|---|
| absent | quelconque | `NoDatabaseFound` | non | n/a |
| invalide (non-SQLite) | quelconque | `RejectInvalidDatabase` | non | non |
| valide mais **vide** | quelconque | `NoDatabaseFound` | non | n/a |
| valide **incompatible** | quelconque | `ManualRepairRequired` | **non** | non |
| valide **compatible** | **absent** | `CopyLegacyDatabaseToCurrentPath` | **oui** | **oui** |
| valide **compatible** | **présent** | `UseAsCurrentDatabase` (**conflit**) | **non** | non |

**Reprise sûre (compatible + courant absent)** — séquence :

1. **sauvegarde** de l'ancien fichier (`SqliteDatabaseManager.Backup`) → copie horodatée restaurable, **avant toute mutation** ;
2. **copie contrôlée** `mmv-optic.db` → `mmv.db` (`File.Copy(overwrite: false)`, + annexes `-wal`/`-shm` éventuelles). Le **courant étant absent**, aucun écrasement n'est possible ;
3. **adoption** de la copie au chemin courant via `SqliteDatabaseManager.PrepareDatabase` (sauvegarde + portail de compatibilité + baseline + vérification post-adoption P2A-1A-R2) ;
4. **re-diagnostic** du chemin courant → comptes de lignes **après** ;
5. **journal** + **rapport avant/après**.

**Garanties :** l'ancien fichier est **uniquement lu et copié** — **jamais supprimé ni écrasé** ; une base
compatible n'est reprise **qu'avec sauvegarde** ; les comptes de lignes avant/après sont **identiques**
(aucune perte ni création silencieuse).

**Câblage runtime (additif, sûr).** [`App.axaml.cs`](../../src/MMV.App/App.axaml.cs) appelle `Recover(...)`
**avant** `PrepareDatabase`, de sorte que le cas ne soit **jamais invisible** (toujours journalisé). En
développement/CI où aucun `mmv-optic.db` n'existe, l'appel est un **no-op** (`NoDatabaseFound`). Les 79 tests
`MMV.App` (ViewModels) n'exécutent pas `ConfigureServices` : **aucune régression** (cf. §13).

## 9. Stratégie de refus

- **Fichier illisible/non-SQLite** → `RejectInvalidDatabase` : aucune copie, **ancien fichier conservé intact** (octets inchangés, testé), journal `LEGACY rejected`.
- **Schéma ancien/incompatible/partiel** → `ManualRepairRequired` : aucune copie, **aucune écriture d'historique**, ancien fichier conservé, **divergences** listées dans le rapport, journal `LEGACY refused`. (Réutilise le portail `SqliteSchemaVerifier` : table/colonne/type-affinité/nullabilité/PK/FK/index unique manquants ou incompatibles.)
- **Conflit ancien/nouveau** (les deux présents) → **ni copie ni écrasement** : la base courante est conservée et utilisée, l'ancien fichier reste intact (octets inchangés, testé), journal `LEGACY conflict`. Une éventuelle fusion relève d'une décision support (non automatisée — pas de perte).

Dans tous les cas de refus : **aucune perte**, fichier(s) conservé(s), décision journalisée, rapport produit.

## 10. Rapport avant/après

[`HistoricalMigrationReport`](../../src/MMV.Infrastructure/Data/HistoricalMigrationReport.cs) — `Render()`
produit un texte support contenant **uniquement des métadonnées techniques** :

- **décision** appliquée ; **fichier source** ; **fichier cible** ; **sauvegarde** créée (+ chemin) ;
- **copie effectuée** ; **conflit détecté** ;
- **tables** recensées ; **colonnes/éléments de schéma divergents** ;
- **comptes de lignes avant/après** par table principale + indicateur **« comptes conservés »** (`RowCountsPreserved`) ;
- **erreurs** éventuelles ; **résultat final** lisible.

**Aucune donnée personnelle** : par construction, seuls des **noms de tables**, des **comptes agrégés** et des
**identifiants de migration** sont exposés — jamais le contenu d'un enregistrement. Un test dédié vérifie
qu'un nom client seedé **n'apparaît pas** dans le rapport rendu (§13, test #10).

## 11. Traitement de R-19 (`HasDefaultValue(DateTime.UtcNow)`)

- **Constat reproduit** : `dotnet ef migrations has-pending-model-changes` ⇒ **true**
  (« Changes have been made to the model since the last migration »). **Inchangé** par cette phase.
- **Origine** : 7 occurrences de `HasDefaultValue(DateTime.UtcNow)` (6 fichiers de configuration, cf. §5).
  La constante est figée **au build**, ce qui modifie la **valeur par défaut** d'une colonne à chaque
  compilation — **pas la structure** (tables/colonnes/types/index). La preuve d'**équivalence structurelle**
  `Migrate()` ↔ `EnsureCreated()` est apportée par le test P2A-1A `Migrate_And_EnsureCreated_ProduceEquivalentSchema`.
- **Périmètre** : conformément à la consigne (§9 du prompt) et à l'ADR (§5 bis, §7.1), **aucune migration
  métier n'est créée ici** pour corriger R-19. Cette phase n'ajoute **aucune** migration (7 inchangées).
- **Recommandation (claire)** : **R-19 doit être corrigé AVANT toute nouvelle migration structurelle métier**
  (remplacer `HasDefaultValue(DateTime.UtcNow)` par une valeur applicative / `getutcdate`), afin qu'une future
  migration ne capte pas ce bruit. Correctif sûr possible (sans migration destructive) mais **non appliqué**
  ici (hors périmètre, requiert décision explicite + nouvelle migration → Étape 1 / R-19).

## 12. Commandes exécutées (principales)

```powershell
# Baseline + analyse
git status --short ; git branch --show-current ; git log -5 --oneline ; git diff --stat ; git diff
dotnet --version ; dotnet restore MMV.sln ; dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
& ~/.dotnet/tools/dotnet-ef.exe migrations list --project src/MMV.Infrastructure --no-build --no-connect
& ~/.dotnet/tools/dotnet-ef.exe migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build

# Implémentation : diagnostic + reprise + rapport + tests (fichiers temporaires uniquement)

# Contrôles finaux (après implémentation)
dotnet build MMV.sln --no-restore -c Debug
dotnet test  MMV.sln --no-build  -c Debug
dotnet list  MMV.sln package --vulnerable --include-transitive
& ~/.dotnet/tools/dotnet-ef.exe migrations list … ; … has-pending-model-changes …
git status --short ; git diff --stat
```

## 13. Tests ajoutés (15, fichiers temporaires isolés)

`SqliteHistoricalDatabaseDiagnosticTests` (**7**) :

| Test | Cas |
|---|---|
| `Diagnose_NoFile_ReturnsNoDatabaseFound_AndDoesNotCreateFile` | fichier absent + **lecture seule** (aucune création) |
| `Diagnose_InvalidFile_ReturnsRejectInvalidDatabase_AndPreservesBytes` | fichier non-SQLite + octets préservés |
| `Diagnose_EmptyValidSqlite_ReturnsNoDatabaseFound` | base valide mais vide |
| `Diagnose_CompatibleHistorical_RecommendsAdoption_WithTechnicalMetadata` | historique compatible + métadonnées (taille, date, comptes, migrations manquantes) |
| `Diagnose_MigrationsManagedDatabase_RecommendsUseAsCurrent` | base gérée + migrations appliquées listées |
| `Diagnose_IncompatibleHistorical_RecommendsManualRepair_WithDifferences` | historique incompatible + divergences |
| `Diagnose_HistoricalDatabase_DoesNotMutateIt` | **non-mutation** (historique toujours absent, comptes intacts) |

`LegacyDatabaseRecoveryServiceTests` (**8**) — couvre les 12 scénarios obligatoires :

| Test | Scénario(s) obligatoire(s) |
|---|---|
| `Recover_NoLegacyFile_DoesNothing_AndDoesNotCreateCurrent` | **1, 2** (aucun fichier / ancien absent) |
| `Recover_InvalidLegacyFile_RefusesAndPreservesIt` | **3** (ancien présent mais invalide) |
| `Recover_ValidCompatibleLegacy_CurrentAbsent_CopiesAdoptsAndBacksUp` | **4, 8, 11** (compatible → copie ; **sauvegarde** ; **comptes conservés**) |
| `Recover_IncompatibleLegacy_RefusesManualRepair_WithoutCopyingOrWritingHistory` | **5, 12** (incompatible → refus contrôlé, **sans historique**) |
| `Recover_BothFilesPresent_DetectsConflict_KeepsBothUntouched` | **6, 7** (nouveau déjà présent / **conflit**) |
| `Recover_ProducesReadableBeforeAfterReport` | **9** (rapport avant/après) |
| `Recover_Report_ContainsNoPersonalData` | **10** (aucun contenu personnel) |
| `Recover_EmptyValidLegacy_DoesNothing` | ancien valide mais vide (rien à reprendre) |

Tous les tests utilisent **uniquement** des fichiers temporaires (`Path.GetTempPath()` + GUID), nettoyés en
`Dispose`. Non-régression des 218 : §14.

## 14. Résultats / contrôles finaux

| Contrôle | Résultat |
|---|---|
| `dotnet restore` | **Succès** |
| `dotnet build -c Debug` | **Succès — 0 erreur, 0 avertissement introduit** (le `CS1998` préexistant réapparaît car `MMV.App` est recompilé ; **non introduit** par P2A-1B) |
| `dotnet test -c Debug` | **233 ✅ / 0 ❌** — App **79/79** ; Domain **154/154** (139 + **15** nouveaux) |
| Non-régression des 218 | ✅ (79 App + 139 Domain préservés) |
| `dotnet list … --vulnerable` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list` | **7 migrations** (inchangées — aucune migration créée) |
| `dotnet ef migrations has-pending-model-changes` | **true** (bruit R-19 inchangé, cf. §11 ; **aucune** nouvelle dérive introduite) |
| `git diff --stat` (suivi) | 2 fichiers modifiés (+36) ; 5 fichiers ajoutés (3 src + 2 tests) + ce rapport |

## 15. Vulnérabilités

**0 package vulnérable** (5 projets), audit transitif inclus. Aucune dépendance ajoutée
(les composants n'utilisent que `Microsoft.Data.Sqlite`/`Microsoft.EntityFrameworkCore` déjà présents).
Aucune vulnérabilité High/Critical.

## 16. Risques résiduels (documentés, hors périmètre)

1. **R-19 / `has-pending-model-changes` = true** — cosmétique (valeur par défaut), structurellement neutre.
   **À corriger AVANT toute nouvelle migration structurelle métier** (§11). Non corrigé ici (hors périmètre).
2. **Reprise automatique au runtime** — le câblage `App.axaml.cs` ne copie l'ancien fichier **que si** le
   chemin courant est **absent** et l'ancien **valide+compatible**. Tout fichier courant **présent** (même
   vide) ⇒ **conflit** (aucune copie) : choix **conservateur** (jamais d'écrasement). Une fusion ancien↔courant
   reste une **décision support** non automatisée (pas de perte ; les deux fichiers conservés).
3. **Comparaison de schéma pragmatique** (héritée de P2A-1A-R2) — ne signale que le **manquant/incompatible
   attendu** (affinité SQLite). Un schéma divergent **non couvert** par les contrôles resterait théoriquement
   classé compatible ; atténué par la preuve d'équivalence et l'anti-faux-positif. Analyse fine = au-delà de P2A-1B.
4. **Ancien `mmv-optic.db` détectable seulement depuis le dossier de travail historique ou via chemin
   explicite support** — le chemin de l'ancien fichier est résolu **relatif au dossier de travail courant**
   (comportement du runtime historique `Data Source=mmv-optic.db`). Si l'ancien runtime était lancé depuis
   un autre dossier, l'ancien fichier peut résider ailleurs : le diagnostic le signale alors comme **absent**
   (`NoDatabaseFound`) sans risque ; une reprise dirigée reste possible via `Recover(legacyPath, …)`
   **explicite** (support).
5. **Chemins de fichiers locaux dans les rapports support** — les rapports/journal peuvent contenir des
   **chemins de fichiers locaux** (ex. `%LOCALAPPDATA%\Users\…`), métadonnées techniques mais potentiellement
   identifiantes. **L'anonymisation des chemins sera à traiter dans une future phase sécurité/support** (hors
   périmètre P2A-1B ; aucune donnée métier/personnelle d'enregistrement n'est exposée).
6. **Verrou mono-instance / WAL** — la reprise copie aussi `-wal`/`-shm` si présents, et `ClearAllPools` est
   appelé avant les opérations fichier ; un accès concurrent à l'ancien fichier pendant la reprise (très
   improbable au démarrage mono-poste) n'est pas verrouillé (R-25, hors périmètre).
7. **`CS1998`** préexistant — inchangé, hors périmètre.

Aucun de ces risques ne touche au métier, au réglementaire, ni aux interdictions de la phase.

## 17. État Git final (après autorisation commit + push)

`ALLOW_COMMIT=true`, `ALLOW_PUSH=true` → changeset **commité** puis **poussé** (sans force-push).

- Commit : **`a39c855`** — `feat(P2A-1B): add legacy SQLite database diagnostics and recovery`
  (+ trailer `Co-Authored-By`). Sur `334b728`.
- Push : `git push origin phase2a-stabilization` (sans force) → `334b728..a39c855`.
- `git status --short` après commit : **vide** (arbre propre).
- `git diff --check` : **0 anomalie** d'espaces (seul un avis LF→CRLF, normal sous Windows).

Contenu commité (`git show --stat`), **8 fichiers, +1688** :

```
A  docs/implementation/P2A-1B-report.md
M  src/MMV.App/App.axaml.cs
A  src/MMV.Infrastructure/Data/HistoricalMigrationReport.cs
A  src/MMV.Infrastructure/Data/LegacyDatabaseRecoveryService.cs
M  src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs
A  src/MMV.Infrastructure/Data/SqliteHistoricalDatabaseDiagnostic.cs
A  tests/MMV.Domain.Tests/Data/LegacyDatabaseRecoveryServiceTests.cs
A  tests/MMV.Domain.Tests/Data/SqliteHistoricalDatabaseDiagnosticTests.cs
```

**Aucun** fichier `bin/`/`obj/`, `*.db` (dont l'`mmv-optic.db` réel présent dans `src/MMV.App/`, **`!!`
ignoré**), `*.zip`, `*.trx`, temporaire ou secret n'est inclus (vérifié `git add -A --dry-run` + `--ignored`).

**Modifiés (2) :**
- [`src/MMV.App/App.axaml.cs`](../../src/MMV.App/App.axaml.cs) — appel additif de la reprise contrôlée avant `PrepareDatabase` (+16 lignes).
- [`src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) — résolution du chemin de l'ancien fichier (+20 lignes).

**Ajoutés — implémentation (3) :**
- [`SqliteHistoricalDatabaseDiagnostic.cs`](../../src/MMV.Infrastructure/Data/SqliteHistoricalDatabaseDiagnostic.cs) — diagnostic structuré + décision.
- [`LegacyDatabaseRecoveryService.cs`](../../src/MMV.Infrastructure/Data/LegacyDatabaseRecoveryService.cs) — reprise contrôlée + sauvegarde + conflit.
- [`HistoricalMigrationReport.cs`](../../src/MMV.Infrastructure/Data/HistoricalMigrationReport.cs) — rapport avant/après sans donnée personnelle.

**Ajoutés — tests + rapport (3) :** `SqliteHistoricalDatabaseDiagnosticTests.cs`,
`LegacyDatabaseRecoveryServiceTests.cs`, `docs/implementation/P2A-1B-report.md` (ce document).

**Aucune entité, aucun ViewModel, aucune configuration EF, aucun `DbInitializer`, aucune migration,
aucune valeur réglementaire** modifiés. Aucun `bin/`/`obj/`/`*.db`/artefact temporaire dans le changeset.

## 18. Validation CI distante (run réel)

Push sur `phase2a-stabilization` ⇒ workflow `CI` déclenché sur l'événement `push`. Détails consignés
depuis l'API GitHub Actions :

| Élément | Valeur réelle |
|---|---|
| Run | **#27343372219** (run number 5) |
| Lien | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27343372219 |
| Commit testé | **`a39c855`** (`head_sha = a39c855ba94494d7a159fac14ed1762fc3d1e655`) |
| Workflow / job | `CI` / `Restore / Build / Test / Scan` |
| Runner / durée | `windows-latest` (GitHub-hosted) — ≈ 2 min 08 s (11:24:18 → 11:26:26 UTC) |
| SDK utilisé | **8.0.417** (step « Setup .NET (SDK verrouillé par global.json) » → success) |
| Restore | ✅ **success** |
| Build | ✅ **success** |
| Test | ✅ **success** (suite **233** ; le step échoue si un test échoue) |
| Audit NuGet (JSON + sévérité) | ✅ **success** (0 High/Critical, scan concluant) |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps (`Set up job`, `Checkout`, `Setup .NET`, `Diagnostic SDK`, `Restore`, `Build`, `Test`,
`Audit des packages vulnérables`, `Complete job`) sont en conclusion `success`.

## 19. Verdict — GO / NO-GO

### **P2A-1B = GO DÉFINITIF** (CI distante verte, run #27343372219)

| Critère d'acceptation (consigne §13) | État |
|---|---|
| Build vert | ✅ 0 erreur, 0 avertissement introduit |
| Tests verts (> 218) | ✅ **233/233** (218 préservés + 15) |
| Aucune vulnérabilité High/Critical | ✅ 0 |
| Ancien `mmv-optic.db` diagnostiqué | ✅ diagnostic + détection + métadonnées (§6) |
| Conflit ancien/nouveau chemin traité | ✅ détecté, aucune copie/écrasement, les deux conservés (§8/§9) |
| Base historique **compatible** reprise **uniquement avec sauvegarde** | ✅ sauvegarde avant copie, testée (§13 #4/#8) |
| Base **incompatible** refusée proprement | ✅ `ManualRepairRequired`, sans copie ni historique (§9, §13 #5/#12) |
| Rapport avant/après produit | ✅ `HistoricalMigrationReport.Render()` (§10) |
| Aucun contenu personnel exposé | ✅ métadonnées seulement, testé (§13 #10) |
| Aucune nouvelle migration destructive | ✅ **aucune migration créée** (7 inchangées) |
| R-19 clairement documenté | ✅ §11 (origine, neutralité structurelle, recommandation) |
| Aucun début de P2A-1C ou autre phase | ✅ |
| **CI distante verte** | ✅ run **#27343372219** = success (commit `a39c855`) |

La gate `VALIDATION DISTANTE REQUISE` est **levée** : le pipeline distant est **vert** sur le commit testé.

## 20. Prochaine étape candidate (NON exécutée)

`P2A-1C` — « Transactions, idempotence et erreurs de persistance » (frontière transactionnelle d'écriture,
protection double-soumission, erreurs utilisateur maîtrisées). **Non commencée.** Ne pas démarrer sans
verdict `P2A-1B` confirmé par revue humaine, puis `TARGET_PHASE_ID = P2A-1C` explicite.

> **Arrêt obligatoire.** Fin de `P2A-1B` — commit `a39c855` poussé, **CI distante verte (#27343372219)**,
> verdict **GO définitif**. Aucune amorce de `P2A-1C`.
