# P4-5E-C — PostgreSQL Migration Chain — PRE-REVIEW v2

> **Statut : C4 FAIT. PRÉ-REVUE, AUCUN COMMIT, AUCUN PUSH.**
> Reprise après la décision architecte **Q1 = option A**. La factory de découverte
> `OpticDbContextDesignTimeFactory` est ajoutée au projet de migrations PostgreSQL. La baseline
> `InitialPostgreSqlBaseline` est générée par la commande D-04, sans `--namespace` ni `--output-dir`. Les tests
> dépendants de la baseline sont écrits et verts.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> Ce document **complète** [la pré-revue v1](P4-5E-C-pre-review.md). La v1 reste la référence pour C1 à C3,
> pour le spike (annexe B) et pour l'incident (annexe C). **Le dépôt réel prime sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `EXÉCUTÉ` | commande ou test exécuté sur le dépôt, résultat observé |
| `MUTATION — HORS DÉPÔT` | exécuté sur une copie jetable des sources, hors du dépôt, supprimée ensuite. **Rien n'en a été reporté** |
| `STATIC_CODE_PROOF` | constaté par lecture du code |

---

## 1. Résumé exécutif

- **Q1 appliquée** : une classe `internal sealed` de **1 ligne de code** dans le projet PostgreSQL. Elle délègue
  à `OpticDbContextFactory`. Elle ne contient ni logique PostgreSQL, ni chaîne de connexion, ni secret, ni
  `Migrate()`, ni `EnsureCreated()`. Un test le vérifie **sur l'IL compilé**.
- **C4 fait** : `20260922001219_InitialPostgreSqlBaseline` est générée **sans connexion**. Le dépôt contient
  3 fichiers générés, dans `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/` seulement. **Aucune
  retouche.**
- **Les trois vérifications demandées sont vertes** : la migration est uniquement dans `PostgreSQL.Migrations`,
  l'instantané PostgreSQL est créé, et **aucun fichier SQLite n'est modifié** (empreinte identique, `git diff` vide).
- **La revue statique du DDL respecte la table D-03** sur tous les points (§6). Les conditions d'arrêt n°2, n°3
  et n°5 ne se déclenchent pas.
- **Tests** : **1953 / 1953 verts, 0 ignoré** (1947 en v1, 1915 à HEAD). Il y a 6 tests nouveaux et un test
  renforcé. Les nouveaux gardes **échouent** bien quand on introduit une régression (§7.3).
- **Toujours ouvert** : le **faux vert** de Q2 est désormais **reproduit dans le dépôt** (§5, P4 et P5). Il
  conditionne la forme du step CI (C7).

---

## 2. Décision appliquée et écart assumé

| Élément | Avant (P4-5E-B) | Après décision Q1 = A |
|---|---|---|
| **D-01.2** | le projet ne contient que des migrations et leur instantané, « aucune factory » | **une exception nommée** : `OpticDbContextDesignTimeFactory`. Elle est verrouillée par test (nom, forme, contenu IL) |
| **D-04** | la factory unique de `MMV.Infrastructure` est trouvée directement | un seul chemin **logique** de génération (`OpticDbContextFactory`), mais **deux points d'entrée**. EF journalise `Using DbContext factory 'OpticDbContextDesignTimeFactory'`, qui délègue |
| **D-05** | la chaîne visée dépend de `MMV_DESIGNTIME_DATABASE_PROVIDER` | **inchangé** : la délégation transmet `args`, et la sélection reste dans `OpticDbContextFactory` |

**Choix de visibilité : `internal sealed`** (à valider). EF découvre les types non publics
(`Found IDesignTimeDbContextFactory implementation 'OpticDbContextDesignTimeFactory'`, `EXÉCUTÉ`). `MMV.App`
référence cette assembly (R-3), mais ne voit donc pas la classe.

**Conséquence utile** : une régénération complète avant fusion (D-03.5) est maintenant reproductible depuis un
état propre du dépôt. `migrations remove` n'a pas été exécuté.

---

## 3. SHA avant / après

| | SHA |
|---|---|
| Avant | `440ddcb352b12e0c6900cfd5d6df550e234856c5` (P4-5D-R CLOSED) |
| Après | `440ddcb352b12e0c6900cfd5d6df550e234856c5`. **Aucun commit.** Toutes les modifications sont dans l'arbre de travail |

---

## 4. Fichiers

**Ajoutés ou modifiés depuis la v1**

| Fichier | Nature | Lignes |
|---|---|---|
| [`OpticDbContextDesignTimeFactory.cs`](../../src/MMV.Infrastructure.PostgreSQL.Migrations/OpticDbContextDesignTimeFactory.cs) | **nouveau** (Q1 A) | 27, dont 1 ligne de code |
| [`MMV.Infrastructure.PostgreSQL.Migrations.csproj`](../../src/MMV.Infrastructure.PostgreSQL.Migrations/MMV.Infrastructure.PostgreSQL.Migrations.csproj) | commentaire d'en-tête : exception D-01.2 nommée. Aucune propriété ni référence changée | 44 |
| [`Migrations/20260922001219_InitialPostgreSqlBaseline.cs`](../../src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/20260922001219_InitialPostgreSqlBaseline.cs) | **généré**, non retouché | 809 |
| [`Migrations/20260922001219_InitialPostgreSqlBaseline.Designer.cs`](../../src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/20260922001219_InitialPostgreSqlBaseline.Designer.cs) | **généré**, non retouché | 1361 |
| [`Migrations/OpticDbContextModelSnapshot.cs`](../../src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/OpticDbContextModelSnapshot.cs) | **généré**, non retouché | 1358 |
| [`MigrationChainsTests.cs`](../../tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs) | 3 tests ajoutés, 1 test renforcé et renommé | 256 (210 en v1) |
| [`OpticDbContextDesignTimeFactoryTests.cs`](../../tests/MMV.Domain.Tests/Configuration/OpticDbContextDesignTimeFactoryTests.cs) | **nouveau**, 3 tests | 117 |
| `docs/implementation/P4-5E-C-pre-review-v2.md` | ce rapport | — |

Les fichiers de la v1 (§3 de la v1) sont inchangés depuis la v1, à l'exception des deux ci-dessus.

**Zones interdites intactes** (`EXÉCUTÉ`) : `git diff --stat 440ddcb --` renvoie une sortie vide sur
`src/MMV.Infrastructure/Migrations/`, `SqliteDatabaseManager.cs`, `App.axaml.cs`, `OpticDbContext.cs`,
`Data/Configurations/`, `docs/architecture/adr*` et `.github/`. Aucun paquet NuGet nouveau.
`ARCHITECTURE.md` n'est pas modifié (Q6).

---

## 5. C4 — Génération de la baseline

**Commande exécutée** (`EXÉCUTÉ`, code 0) :

```bash
dotnet tool restore                      # dotnet-ef 8.0.27, verrouillé (D-03.1)

MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql \
  dotnet ef migrations add InitialPostgreSqlBaseline \
    --project         src/MMV.Infrastructure.PostgreSQL.Migrations \
    --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
```

```
Build started...
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
```

> **Écart de forme à signaler.** La commande de la demande ne mentionne pas la variable. Je l'ai positionnée
> **pour cette seule commande**, conformément à la commande type de D-04. Sans elle, la factory délègue vers la
> branche SQLite. EF refuse alors l'écriture, car l'assembly cible ne correspond pas à l'assembly de migrations
> configurée (v1, S3d). Aucune autre option n'a été ajoutée : pas de `--namespace`, pas de `--output-dir`, pas de
> `--no-build`.

**Placement (D-03.6, consigné ici)**

| | Valeur |
|---|---|
| Dossier | `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/` |
| Espace de noms | `MMV.Infrastructure.PostgreSQL.Migrations.Migrations`, soit `<RootNamespace>.Migrations`, la même convention que SQLite |
| Identifiant | `20260922001219_InitialPostgreSqlBaseline` |
| `ProductVersion` | `8.0.27` |
| Migrations suivantes | même commande, sans option (v1, S4) |

**Découverte**, journal `--verbose` de `has-pending-model-changes` (`EXÉCUTÉ`) :

```
Finding IDesignTimeDbContextFactory implementations...
Found IDesignTimeDbContextFactory implementation 'OpticDbContextDesignTimeFactory'.
Found DbContext 'OpticDbContext'.
No application service provider was found.
Using DbContext factory 'OpticDbContextDesignTimeFactory'.
Using context 'OpticDbContext'.
No changes have been made to the model since the last migration.
```

**Commandes `dotnet ef` après génération** (toutes `--no-build`, après `dotnet build MMV.sln`, `EXÉCUTÉ`)

| # | Commande | Variable | Résultat |
|---|---|---|---|
| P1 | `has-pending-model-changes`, projet PostgreSQL (forme du step C7) | `postgresql` | **vert**, code 0 |
| P2 | `migrations list --no-connect`, projet PostgreSQL | `postgresql` | `20260922001219_InitialPostgreSqlBaseline` **seule** |
| P3 | `has-pending-model-changes --project src/MMV.Infrastructure` (step CI SQLite actuel) | absente | **vert**, code 0 |
| P4 | `has-pending-model-changes`, projet PostgreSQL | **absente** | **vert**, code 0 : **faux vert (Q2)** |
| P5 | `migrations list --no-connect`, projet PostgreSQL | **absente** | liste les **14 migrations SQLite** : c'est la chaîne SQLite qui est contrôlée |

P4 et P5 confirment **dans le dépôt** le constat S3b du spike. L'option A ne ferme pas ce risque, contrairement à
A'. Voir Q2.

**Aucune connexion** : l'hôte design-time est `mmv-design-time.invalid`, qui ne se résout jamais. La base
SQLite du profil local, créée lors de l'incident de la v1, n'a pas été touchée : taille et horodatage
`mmv.db` sont identiques avant et après, à `2026-09-21 23:42:47Z` (`EXÉCUTÉ`).

---

## 6. Vérifications après génération

| Vérification demandée | Résultat | Preuve |
|---|---|---|
| Migration uniquement dans `PostgreSQL.Migrations` | **OUI**. Les 3 seuls fichiers nouveaux hors `docs/` et `tests/` sont sous `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/` | `git status --untracked-files=all` (`EXÉCUTÉ`) |
| Instantané PostgreSQL créé | **OUI**. `OpticDbContextModelSnapshot`, `[DbContext(typeof(OpticDbContext))]`, annotations Npgsql, `MaxIdentifierLength` 63 | fichier ; test `EachChain_HasItsOwnSnapshot_InItsOwnAssembly` |
| Aucun fichier SQLite modifié | **OUI**. L'empreinte SHA-256 des 29 fichiers de `src/MMV.Infrastructure/Migrations/` est `87c63645…5e35` **avant et après**. `git diff` est vide. Le contrôle de dérive SQLite est vert | `EXÉCUTÉ` |
| Aucune mention SQLite dans la chaîne PostgreSQL | **0** occurrence de `Sqlite` dans les 3 fichiers générés | `EXÉCUTÉ` |

### 6.1 Revue statique du DDL (D-03, D-08)

Le script de revue (D-03.7) est produit **sans connexion** par
`MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations script --project … --startup-project … --no-build`.
Il fait 391 lignes, SHA-256 `04c36a9e02c7580702a8140a0dc8a5cdafbcc65ea22157f45ebea48f1cad9511`. Il est
**conservé hors dépôt** et régénérable à l'identique par cette commande (Q8). **C'est une lecture, pas une preuve
d'exécution** (P4-5F).

| Doit contenir | Mesure | | Ne doit pas contenir | Mesure |
|---|---|---|---|---|
| `numeric(12,2)` | **14** | | `REAL` / `real` | **0** / **0** |
| `WHERE "IsCurrent"` sur `idx_workshop_sheets_current_unique` | **présent** | | `"IsCurrent" = 1` | **0** |
| filtre LowStock portable | **identique** au `HasFilter` du modèle | | `GLOB`, `CHECK` | **0**, **0** |
| `timestamp with time zone` | 19 | | mention `sqlite`, casse ignorée | **0** |
| `date` (`BirthDate`, `IssueDate`) | 2 | | `CREATE SCHEMA`, `public.`, `EnsureSchema` | **0**, **0**, **0** |
| `boolean` · identity | 7 · 16 | | `InsertData` utilisateur | **0** (un seul `InsertData`, sur `DocumentSequences`) |
| `DocumentSequences` | `ORDER`/`CMD` et `SALE`/`VTE`, `TIMESTAMPTZ '2026-01-01T00:00:00Z'` | | | |
| clés étrangères | `RESTRICT` 7 · `CASCADE` 10 · `SET NULL` 3 | | | |

Autres mesures : 22 tables, 30 index, 22 colonnes `double precision` (optique), historique
`__EFMigrationsHistory` au nom par défaut et sans schéma. **Tous les chiffres sont identiques à la baseline du
spike** (v1, annexe B.2).

Les 14 colonnes monétaires sont : `SupplementPrice`, `PurchasePrice`, `SalePrice`, `RecommendedPrice`,
`TotalAmount`, `DiscountAmount`, `FinalAmount`, `DepositAmount`, `RemainingAmount`, `UnitPrice` ×2, `TotalPrice`,
`PurchasePriceGrid` et `SalePriceGrid`.

**Observation, sans écart** : `Products."TechnicalSpecs"` est émis en `TEXT` majuscule, car le modèle déclare
`HasColumnType("TEXT")` ([ProductConfiguration.cs:74-75](../../src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs#L74-L75)).
Ce n'est pas une colonne monétaire, et PostgreSQL accepte ce type. Aucune modification du modèle n'est
nécessaire (condition d'arrêt n°5 non déclenchée). Les autres chaînes sont en `text`, en minuscules.

---

## 7. Tests

### 7.1 Résultats (`EXÉCUTÉ`)

| Commande | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | **0 avertissement, 0 erreur** |
| `dotnet test MMV.sln --no-build -c Debug` | **1953 réussis, 0 échec, 0 ignoré**. Détail : App 260, Application 628, Domain 1065 |
| Évolution | HEAD 1915, puis v1 1947, puis **v2 1953** (+6) |

### 7.2 Tests ajoutés ou modifiés

| Test | Exigence | Rôle |
|---|---|---|
| `MigrationChainsTests.PostgreSqlChain_IsExactlyTheBaseline` | **C6-b**, D-03 | Npgsql : `GetMigrations()` = la baseline **seule**, par identifiant exact |
| `MigrationChainsTests.PostgreSqlChain_HasNoPendingModelChanges` | **C6-c** côté PostgreSQL, D-03.4 | même helper que le test SQLite, équivalent API EF 8 de la CLI |
| `MigrationChainsTests.EachChain_HasItsOwnSnapshot_InItsOwnAssembly` | **D-01.4** | un instantané par assembly, chacun dans la sienne |
| `MigrationChainsTests.PostgreSqlMigrationsAssembly_ContainsOnlyMigrationsSnapshotAndTheDelegatingFactory` | **D-01.2** + exception Q1 | remplace `…ContainsOnlyMigrationsAndSnapshots`. Le seul type hors migration ou instantané doit être la factory nommée. Exactement 1 instantané, au moins 1 migration : **il n'est plus trivialement vrai** |
| `OpticDbContextDesignTimeFactoryTests.Factory_IsTheOnlyDesignTimeFactory_…` | Q1 | seule factory de l'assembly ; `sealed` ; non publique |
| `OpticDbContextDesignTimeFactoryTests.Factory_HoldsNoState_AndDeclaresNothingButCreateDbContext` | Q1 : aucune chaîne de connexion | aucun champ, aucune propriété, une seule méthode |
| `OpticDbContextDesignTimeFactoryTests.CreateDbContext_OnlyDelegatesToTheInfrastructureFactory` | Q1 : délégation obligatoire ; ni `Migrate()`, ni `EnsureCreated()`, ni logique PostgreSQL | **lecture de l'IL** : les seuls appels sont `OpticDbContextFactory..ctor()` et `OpticDbContextFactory.CreateDbContext(String[])`, et aucun `ldstr` |

**Pourquoi l'IL plutôt qu'une exécution** : exécuter la factory lirait l'environnement réel du processus. Les
tests existants ne le modifient jamais (parallélisme xUnit). Sur la branche SQLite, l'exécution créerait aussi
le dossier de la base dans le profil du poste. L'IL prouve ce que le code compilé appelle, sans dépendre des
commentaires du source.

**Identifiant épinglé** : `PostgreSqlBaselineId` fige `20260922001219_InitialPostgreSqlBaseline`. Une
régénération avant fusion (D-03.5) change l'horodatage et impose de mettre à jour cette constante, ce qui est
voulu. Après fusion, l'identifiant est gelé comme les 14 identifiants SQLite.

### 7.3 Les gardes échouent bien (`MUTATION — HORS DÉPÔT`)

Copie jetable de `src/`, `tests/MMV.Domain.Tests`, `global.json`, `Directory.Build.props` et `.config/`, avec
deux mutations simultanées, puis suppression de la copie.

| Mutation | Test | Résultat |
|---|---|---|
| factory : `var c = new OpticDbContextFactory().CreateDbContext(args); c.Database.EnsureCreated(); return c;` | `CreateDbContext_OnlyDelegatesToTheInfrastructureFactory` | **rouge** : `… contains 2 item(s) too many` (`DbContext::get_Database()`, `DatabaseFacade::EnsureCreated()`) |
| modèle : `Customer.Phone` `HasMaxLength(20)` → `25` | `PostgreSqlChain_HasNoPendingModelChanges` | **rouge** |
| même mutation | `SqliteChain_HasNoPendingModelChanges`, `DateTimeDefaultValuesMigrationTests.Model_HasNoPendingModelChanges_AfterFixMigration` | **verts** (`TEXT` des deux côtés) |

Ce résultat reproduit S5 au niveau des tests unitaires : la chaîne PostgreSQL détecte une dérive que la chaîne
SQLite ne voit pas.

---

## 8. Validation des décisions (delta v1 → v2)

| Décision | v1 | **v2** | Preuve |
|---|---|---|---|
| **D-01** | PARTIEL | **CONFORME, avec l'exception D-01.2 décidée en Q1** | D-01.4 : test d'instantané. D-01.5 : chaîne PostgreSQL = baseline seule. D-01.2 : exception nommée et verrouillée |
| **D-03** | BLOQUÉ | **CONFORME** | D-03.1 : outil 8.0.27. D-03.2 : aucune retouche. D-03.3 : baseline seule. D-03.6 : §5. D-03.7 : script de revue produit sans connexion (§6.1, emplacement Q8) |
| **D-04** | BLOQUÉ à l'amorçage | **CONFORME, avec deux points d'entrée** (Q1 A) | commande D-04 exécutée telle quelle ; journal de découverte §5 |
| **D-05** | CONFORME | **CONFORME** | inchangé. D-05.7 : aucune commande connectée |
| **D-06** | CONFORME | **CONFORME** | empreinte des migrations SQLite identique ; dérive SQLite verte (CLI et test) |
| **D-07** | CONFORME | **CONFORME** | tests de coexistence verts |
| **D-08** | CONFORME dans le code | **CONFORME** | DDL : 0 `CREATE SCHEMA`, 0 `public.`, 0 `EnsureSchema`, historique au nom par défaut |
| **D-19** | NON COMMENCÉ | **NON COMMENCÉ** | C8, en attente de Q6 |

---

## 9. Limitations restantes

1. **Aucun serveur PostgreSQL contacté**, par construction. L'application réelle de la baseline, sa
   reproductibilité et la conformité physique du schéma relèvent de **P4-5F**.
2. **Step CI (C7) non ajouté.** Il est maintenant **débloqué** (P1 vert), mais sa forme dépend de Q2. `ci.yml`
   est intact.
3. **Le faux vert Q2 est confirmé dans le dépôt** (P4, P5). L'option A ne le ferme pas.
4. **Garde-fou de démarrage** : toujours prouvé statiquement seulement (Q5, inchangé). `App.axaml.cs` n'est pas
   modifié.
5. **État inerte au runtime** : `MMV.App` embarque désormais une assembly contenant la baseline et une factory
   non publique. Le garde-fou bloque toujours PostgreSQL au démarrage, et le runtime SQLite est inchangé (suite
   verte).
6. **C8 (D-19) et C9 (clôture, roadmap) non commencés.**
7. Les fichiers `mmv.db` et `migration-journal.log` créés dans `%LOCALAPPDATA%\ManageMyVision\` pendant
   l'incident de la v1 sont **toujours présents**. Ils n'ont pas été touchés. Leur suppression reste à la main
   de l'utilisateur du poste.

---

## 10. Questions ouvertes

**Résolues par la décision de reprise**

- **Q1** : option A, appliquée (§2).
- **Q3** : commande D-04 **sans option**. Dossier et espace de noms consignés au §5.
- **Q7** : l'arbre de travail C1 à C3 est conservé ; reprise à C4.

**Toujours ouvertes**

**Q2 — Faux vert du contrôle de dérive PostgreSQL.** Il est reproduit dans le dépôt (P4 : vert sans variable ;
P5 : 14 migrations SQLite listées). L'option A étant retenue, **proposition** pour le step C7 : garder
`has-pending-model-changes` et le faire précéder, dans le **même** step et sous le même `env:`, d'une assertion.
`dotnet ef migrations list --no-connect` doit contenir `_InitialPostgreSqlBaseline` et **aucun**
`_InitialCreate`. À valider, car cela élargit la lettre de D-05.6 (« un seul step »), sans en changer l'esprit.

**Q4 — Chaîne design-time** `Host=mmv-design-time.invalid;Database=mmv_design_time`. Inchangée depuis la v1,
à confirmer.

**Q5 — Preuve statique du garde-fou** (`ServerStartupGuardTests`). Inchangée depuis la v1, à confirmer.

**Q6 — Emplacement de D-19** (`CONTRIBUTING.md` proposé) et correction de `ARCHITECTURE.md` §« Migrations EF
Core ». Inchangée depuis la v1. Les commandes documentées lancent l'application (v1, annexe C).

**Q8 (nouvelle) — Script SQL de revue (D-03.7).** Il est aujourd'hui hors dépôt (§6.1). Faut-il le versionner
comme pièce du rapport, par exemple `docs/implementation/P4-5E-C-baseline-review.sql` avec un en-tête
« NE PAS EXÉCUTER » ? Ou suffit-il de consigner sa commande et son SHA-256 ?

**Q9 (nouvelle) — Suite.** Faut-il reprendre C7 (après Q2), C8 (après Q6) et C9 dans cet ordre, toujours en
pré-revue sans commit ?

```
P4-5E-C                      = PRE-REVIEW v2 — C4 DONE — AWAITING VALIDATION
Q1                           = APPLIED — OPTION A (OpticDbContextDesignTimeFactory, internal sealed, delegating)
C1 PROJECT                   = DONE
C2 SELECTION                 = DONE
C3 FACTORY                   = DONE
C4 BASELINE                  = DONE — 20260922001219_InitialPostgreSqlBaseline, NO MANUAL EDIT, NO CONNECTION
C5 SQLITE INTEGRITY          = VERIFIED (29 FILES, SAME SHA-256, git diff EMPTY, DRIFT GREEN)
C6 UNIT TESTS                = DONE — 6 NEW + 1 STRENGTHENED SINCE v1; MUTATION-CHECKED
C7 CI                        = NOT ADDED — UNBLOCKED, AWAITING Q2
C8 CONTRIBUTING (D-19)       = NOT STARTED — AWAITING Q6
C9 CLOSURE                   = NOT STARTED
TESTS                        = 1953 / 1953 GREEN, 0 SKIPPED (v1: 1947, HEAD: 1915)
SQLITE MIGRATION CHAIN       = 14 MIGRATIONS INTACT
PG MIGRATION CHAIN           = BASELINE ONLY, DRIFT GREEN
D-03 DDL REVIEW              = CONFORMANT (STATIC READING, NOT EXECUTION)
PG STARTUP                   = BLOCKED BY DESIGN (App.axaml.cs) — UNCHANGED
COMMIT / PUSH                = NONE
```

---

## Annexe — Extraits du script de revue

```sql
CREATE UNIQUE INDEX idx_workshop_sheets_current_unique ON "WorkshopSheets" ("OrderId") WHERE "IsCurrent";

CREATE UNIQUE INDEX idx_notifications_active_low_stock_unique ON "Notifications" ("Type", "EntityType", "EntityId") WHERE "Type" = 'LowStock' AND "EntityType" = 'Product' AND "EntityId" IS NOT NULL AND "ResolvedAt" IS NULL;

INSERT INTO "DocumentSequences" ("SequenceName", "CurrentValue", "Prefix", "UpdatedAt")
VALUES ('ORDER', 0, 'CMD', TIMESTAMPTZ '2026-01-01T00:00:00Z');
INSERT INTO "DocumentSequences" ("SequenceName", "CurrentValue", "Prefix", "UpdatedAt")
VALUES ('SALE', 0, 'VTE', TIMESTAMPTZ '2026-01-01T00:00:00Z');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260922001219_InitialPostgreSqlBaseline', '8.0.27');
```
