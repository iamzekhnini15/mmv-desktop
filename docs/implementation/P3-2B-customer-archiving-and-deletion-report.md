# P3-2B — Sécurisation métier **Clients** : suppression conditionnelle et archivage

> **Mode : `IMPLEMENTATION_AND_REPORT`.** Cœur métier + persistance de P3-2. **Aucune modification UI**
> (`src/MMV.App/**` et `tests/MMV.App.Tests/**` intacts — P3-2C). **Aucun commit, aucun push.**
> **Le dépôt réel prime toujours sur ce document.**
>
> Branche : `p3-business-rules`. Étape précédente : **P3-2A** (audit, `GO local`).
> Cadré par la [roadmap P3-2](../architecture/P3-business-rules-roadmap.md),
> l'[ADR-PROD-DB-001 multi-poste](../architecture/adr-prod-db-001-multi-poste-database-strategy.md) et
> l'[ADR frontières §8](../architecture/adr-application-boundaries.md).

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-2B |
| `EXECUTION_MODE` | `IMPLEMENTATION_AND_REPORT` |
| `SOURCE_BRANCH` | `p3-business-rules` |
| `ALLOW_CODE_CHANGES` | `true` |
| `ALLOW_TEST_CHANGES` | `true` |
| `ALLOW_DOC_CHANGES` | `true` |
| `ALLOW_MIGRATION` | `true` |
| `ALLOW_UI_CHANGES` | **`false`** |
| `ALLOW_COMMIT` / `ALLOW_PUSH` | `false` / `false` |

---

## 2. Baseline (avant implémentation)

| Contrôle | Attendu | Résultat |
|---|---|---|
| Branche | `p3-business-rules` | ✅ |
| Working tree | rapport P3-2A non commité uniquement | ✅ (`?? docs/implementation/P3-2A-…md`) |
| `dotnet build -c Debug` | vert | ✅ **0 avertissement, 0 erreur** |
| `dotnet test --no-build` | ≥ 592 | ✅ **592** (App 192 · Application 177 · Domain 223) |
| `dotnet list … --vulnerable` | 0 | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK | ✅ `dotnet-ef` 8.0.27 |
| `ef migrations has-pending-model-changes` | false | ✅ « No changes … since the last migration » |

**Baseline = GO.**

---

## 3. Décisions métier (imposées, appliquées telles quelles)

1. **« Historique »** = **au moins une `Prescription` OU au moins une `Sale`**.
2. **Suppression physique** autorisée **uniquement** si le client n'a **aucun** historique (nettoyage des
   doublons / erreurs de saisie).
3. **Si un historique existe** : suppression physique **refusée** (aucune écriture) → **archiver** à la place.
4. **Archivage réversible** : un client peut être archivé **et réactivé** ; opérations **idempotentes**.
5. **Clients archivés exclus par défaut** des listes et **toujours** des sélecteurs.
6. **Ordonnances et ventes toujours conservées** — plus aucune destruction ni anonymisation.
7. **La base est le dernier rempart** contre les courses multi-poste (§10).

**Non implémenté (hors périmètre explicite)** : fusion de doublons, blocage téléphone/email, RGPD/export, UI
d'archivage, soft delete global, `HasQueryFilter`, provider PostgreSQL/SQL Server, jeton de concurrence global.

---

## 4. Modèle Domain

[`src/MMV.Domain/Entities/Customer.cs`](../../src/MMV.Domain/Entities/Customer.cs) :

```csharp
public bool IsArchived { get; private set; }

public void Archive()    => IsArchived = true;   // idempotent
public void Reactivate() => IsArchived = false;  // idempotent
```

- **Setter privé** : l'état d'archivage ne se modifie **que** par les deux méthodes métier. EF Core matérialise
  sans difficulté une propriété à setter privé, et aucun test existant ne construisait `Customer` avec ce champ.
- **`UpdatedAt` reste géré par l'Application** (les méthodes du domaine ne lisent **pas** l'horloge), conforme à
  la convention du dépôt (R-19 / futur `IClock`) et à `UpdateCustomerUseCase`.
- **Compromis documenté** : le reste de l'entité conserve ses **setters publics** (POCO anémique existant). P3-2B
  n'entreprend **pas** l'encapsulation générale de `Customer` (churn hors périmètre) ; seul **l'état nouveau** est
  encapsulé. Style mixte assumé, dans le sens de l'enrichissement progressif du domaine.
- **Non ajouté** : `DeletedAt`, `HasQueryFilter`, dépendance externe, logique EF dans le Domain.

---

## 5. Repositories — existence légère

| Interface (Domain) | Méthode ajoutée | Implémentation (Infrastructure) |
|---|---|---|
| `IPrescriptionRepository` | `ExistsByCustomerIdAsync(long, CancellationToken)` | `GetQueryable().AnyAsync(p => p.CustomerId == id, ct)` |
| `ISaleRepository` | `ExistsByCustomerIdAsync(long, CancellationToken)` | `GetQueryable().AnyAsync(s => s.CustomerId == id, ct)` |
| `ICustomerRepository` | `ListAsync(bool includeArchived, CancellationToken)` | `GetQueryable()` + `Where(c => !c.IsArchived)` si exclusion |

- **`AnyAsync`** : la requête s'arrête à la première ligne, **aucune collection matérialisée**.
  `GetByCustomerIdAsync` (qui charge **toute** la collection) n'est **pas** réutilisé pour le garde-fou.
- **`CancellationToken`** présent partout, conforme aux interfaces existantes.
- **Aucune requête N+1** : le use case exécute **deux** requêtes d'existence bornées.
- **`ListAsync`** : le filtre d'archivage est poussé **en SQL** — aucun client archivé n'est chargé puis écarté en
  mémoire (pattern de lecture P2D respecté).

---

## 6. Suppression conditionnelle

[`DeleteCustomerUseCase`](../../src/MMV.Application/UseCases/Customers/DeleteCustomer/DeleteCustomerUseCase.cs) —
ordre d'exécution exact :

1. commande non nulle (`ArgumentNullException` sinon) ;
2. `GetByIdAsync` ;
3. introuvable ⇒ `CustomerFound = false`, **aucune écriture** ;
4. `ExistsByCustomerIdAsync` (prescriptions) ;
5. `ExistsByCustomerIdAsync` (ventes) ;
6. **historique ⇒ `BusinessRuleException`** — **sans** `DeleteAsync`, **sans** `SaveChangesAsync` ;
7. sinon `DeleteAsync(entity)` + `SaveChangesAsync`.

**Signalisation du refus** : `BusinessRuleException` (Domain), conforme à l'**ADR frontières §8** et au choix
**P3-1** (« refus métier dur = exception typée » ; les `*Result` portent l'« introuvable » et les
`ValidationErrors`, pas les refus durs). Le message est **stable et testé**, exposé en constante pour que l'UI
(P3-2C) et les tests s'y réfèrent sans duplication :

```csharp
public const string CustomerHasHistoryMessage =
    "Ce client possède un historique et ne peut pas être supprimé. Archivez-le à la place.";
```

Aucune `DbUpdateException` brute n'est exposée comme résultat nominal : le chemin nominal du refus est **toujours**
la `BusinessRuleException` applicative (la contrainte base ne se déclenche que sur course réelle, cf. §12).

---

## 7. Archivage / réactivation

Nouveau use case
[`SetCustomerArchived`](../../src/MMV.Application/UseCases/Customers/SetCustomerArchived/) (miroir de
`SetUserActiveUseCase`), enregistré dans
[`MMV.Application/DependencyInjection.cs`](../../src/MMV.Application/DependencyInjection.cs) — **la composition
root UI n'est pas touchée** (les use cases sont enregistrés dans la couche Application).

| Élément | Contenu |
|---|---|
| `SetCustomerArchivedCommand` | `CustomerId`, `IsArchived` (**état absolu**, pas un basculement) |
| `SetCustomerArchivedResult` | `CustomerFound`, `CustomerId`, `IsArchived` |
| Comportement | introuvable ⇒ `CustomerFound = false` (aucune écriture) ; **état déjà atteint ⇒ no-op** (cf. ci-dessous) ; sinon `Archive()` / `Reactivate()` puis `UpdatedAt = UtcNow`, `UpdateAsync` + `SaveChangesAsync` |
| **Idempotence (stricte)** | si `customer.IsArchived == command.IsArchived`, le use case **retourne un succès sans aucune écriture** : **pas d'`UpdateAsync`**, **pas de `SaveChangesAsync`**, et **`UpdatedAt` reste inchangé** |
| Suppression | **aucune** |
| `ITransactionRunner` | **non requis** — au plus une écriture (un `Update`, un `SaveChangesAsync`), cohérent avec `SetUserActiveUseCase` / `UpdateCustomerUseCase` |

**Définition retenue de l'idempotence** — et c'est un point de contrat, pas un détail : réappliquer l'état courant
n'est pas seulement « sans effet **observable sur `IsArchived`** », c'est **sans écriture du tout**. Une
implémentation qui ré-écrirait la ligne serait *convergente* mais **pas idempotente** : elle produirait un `UPDATE`
inutile et, surtout, **écraserait `UpdatedAt`** — faisant passer une commande **sans effet** pour une modification
réelle de la fiche. `UpdatedAt` doit refléter la dernière modification **réelle** du client. Bénéfice multi-poste
supplémentaire : deux postes qui archivent le même client **convergent**, et le second **n'écrit rien** (aucune
écriture concurrente inutile sur la base centrale).

L'**état absolu** (et non un toggle) rend par ailleurs l'opération naturellement sûre en multi-poste.

---

## 8. Filtrage listes et picker

| Use case | Comportement P3-2B | Rétrocompatibilité |
|---|---|---|
| `ListCustomersUseCase` | `ListCustomersQuery.IncludeArchived` (**`false` par défaut**) → `ListAsync(includeArchived)` | ✅ les appels existants (`new ListCustomersQuery()`) excluent les archivés **sans changer de signature** |
| `ListCustomersForPickerUseCase` | archivés **toujours** exclus (`ListAsync(includeArchived: false)`) — un sélecteur ne doit jamais proposer de rattacher un document à un client archivé | ✅ signature inchangée |

- `CustomerListItemDto` expose désormais **`IsArchived`** (permettra à P3-2C de distinguer actifs/archivés quand
  `IncludeArchived = true`). `CustomerPickerItemDto` **n'en a pas besoin** (les archivés n'y figurent jamais).
- **Filtrage en SQL**, pas en mémoire.
- **Aucun appel existant cassé** : valeur par défaut rétrocompatible, et aucun client n'est archivé avant P3-2C.

---

## 9. Migration

[`20260713215132_AddCustomerArchivingAndProtectHistory`](../../src/MMV.Infrastructure/Migrations/20260713215132_AddCustomerArchivingAndProtectHistory.cs)

```
AddColumn    Customers.IsArchived  INTEGER NOT NULL DEFAULT 0
DropFK/AddFK FK_Prescriptions_Customers_CustomerId   Cascade  → Restrict
DropFK/AddFK FK_Sales_Customers_CustomerId           SetNull  → Restrict
+ snapshot mis à jour
```

**Inspection du SQL réellement généré** (`dotnet ef migrations script`) — la migration a été **relue, pas acceptée
sur confiance** :

- `ALTER TABLE "Customers" ADD "IsArchived" INTEGER NOT NULL DEFAULT 0;` → **backfill neutre** : tous les clients
  existants restent **actifs**.
- Changement de FK sous SQLite ⇒ **reconstruction de table** (imposée par SQLite, pas un choix) :
  `CREATE TABLE "ef_temp_…"` → `INSERT INTO "ef_temp_…" (…) SELECT (…) FROM "…"` → `DROP TABLE "…"` →
  `ALTER TABLE "ef_temp_…" RENAME TO "…"`, le tout encadré par `PRAGMA foreign_keys = 0/1`.
- **Toutes les colonnes sont recopiées** (`INSERT … SELECT` exhaustif) : **aucune perte de ligne ni de valeur**.
- **Aucune** suppression de table ni de données non prévue. `Sale.StaffId` conserve son `SET NULL` (inchangé) ;
  `Sale.CustomerId` reste **nullable** (vente au comptoir sans client) — seul le **comportement de suppression**
  change.
- Non-destructivité **prouvée par test** sur une base **peuplée** (§11, `CustomerArchivingMigrationTests`).

### 9.1 Effet de bord traité : adoption des bases historiques

L'ajout d'une colonne a fait apparaître une **régression réelle** (détectée par la suite de tests, pas supposée) :
le **portail de compatibilité** de [`SqliteDatabaseManager`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs)
**refusait** d'adopter toute base historique (`EnsureCreated`, sans `__EFMigrationsHistory`) au motif
`colonne manquante: Customers.IsArchived`. Une base utilisateur ancienne serait devenue **non adoptable**.

Correction, **strictement calquée sur le mécanisme existant** (`DocumentSequences`, P2A-1E) :

1. [`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs) — `Customers.IsArchived`
   rejoint les **éléments additifs tolérés quand absents** (nouveau `AdditiveColumnsToleratedWhenAbsent`, pendant
   au niveau colonne de `AdditiveTablesToleratedWhenAbsent`). Toute **autre** colonne manquante reste **bloquante**.
2. `SqliteDatabaseManager` — la migration P3-2B est **exécutée** (et **non** baselinée) quand `Customers.IsArchived`
   est physiquement absente. Un baseline mensonger aurait marqué la migration comme appliquée alors que les FK
   seraient restées `Cascade`/`SetNull` : **l'historique métier serait resté destructible**.
3. **Vérification physique post-adoption** : si la colonne est absente après adoption, l'adoption **échoue
   explicitement** (base + sauvegarde conservées) — même contrat que pour `DocumentSequences`.

---

## 10. Contraintes FK réelles

| Relation | Avant P3-2B | Après P3-2B | Effet |
|---|---|---|---|
| `Prescription → Customer` | **`Cascade`** | **`Restrict`** | l'ordonnance (donnée médicale) n'est **plus détruite** |
| `Sale → Customer` | **`SetNull`** | **`Restrict`** | la vente n'est **plus anonymisée** — elle reste rattachée |

Les deux configurations Fluent sont alignées **des deux côtés** de chaque relation (`CustomerConfiguration` **et**
`PrescriptionConfiguration` / `SaleConfiguration`), pour qu'aucune configuration résiduelle ne réintroduise l'ancien
comportement.

**Preuve SQL réelle** (`PRAGMA foreign_key_list`, base migrée) : `Prescriptions → Customers = RESTRICT`,
`Sales → Customers = RESTRICT`. **PRAGMA `foreign_keys` = 1** vérifié explicitement par test : sans cela, les
contraintes ne protégeraient rien.

---

## 11. Tests

**626 tests** (592 → **+34**), tous verts.

### Domain — `tests/MMV.Domain.Tests/Entities/CustomerArchivingTests.cs` (+6)
client initialement **non archivé** · `Archive()` · `Reactivate()` · **idempotence** d'`Archive` · **idempotence**
de `Reactivate` · archivage/réactivation **ne touchent pas** aux navigations d'historique.

### Application — `DeleteCustomerUseCaseTests` (+7)
client **introuvable** · client **vierge supprimé** · **refus** avec ordonnance (ordonnance **conservée**) ·
**refus** avec vente (vente **conservée et toujours rattachée**) · **aucun `DeleteAsync`** et **aucun
`SaveChangesAsync`** lors du refus (ports mockés `MockBehavior.Strict`, 3 combinaisons d'historique) · **type et
message d'exception stables** · gardes de constructeur.

### Application — `SetCustomerArchivedUseCaseTests` (+11, nouveau)
archivage **persisté** · réactivation **persistée** · introuvable (aucune écriture) · l'archivage **conserve**
ordonnance **et** vente · gardes · **idempotence stricte** :

- **archiver un client déjà archivé** ⇒ `CustomerFound = true`, `IsArchived = true`, **`UpdatedAt` inchangé** ;
- **réactiver un client déjà actif** ⇒ `CustomerFound = true`, `IsArchived = false`, **`UpdatedAt` inchangé** ;
- dans les deux cas, **`UpdateAsync` jamais appelé** et **`SaveChangesAsync` jamais appelé** — prouvé par ports
  mockés `MockBehavior.Strict` (le `UpdatedAt` observé en base prouve en outre l'absence d'écriture réelle).

### Application — listes et picker (+3)
`ListCustomers` : archivés **exclus par défaut** · `IncludeArchived = true` renvoie **actifs + archivés** avec
`IsArchived` **correctement projeté**. Picker : archivés **exclus**, actifs **présents**.

### Infrastructure / **vrai SQLite** — `CustomerDeletionConstraintsTests.cs` (+5, nouveau)
Ces tests **court-circuitent délibérément le garde applicatif** (suppression **directe** via `OpticDbContext`) pour
prouver le comportement **SQL réel** :

- PRAGMA `foreign_keys` **actif** (sinon les tests ne prouveraient rien) ;
- `DELETE` d'un client **avec ordonnance** ⇒ **échec FK** ; **ordonnance conservée** ; **client conservé** ;
- `DELETE` d'un client **avec vente** ⇒ **échec FK** ; **vente conservée** (et `CustomerId` **intact**) ; **client
  conservé** ;
- `DELETE` d'un client **sans historique** ⇒ **succès** ;
- **course multi-poste** : historique créé **après** la vérification ⇒ le `DELETE` échoue **quand même**.

### Migration sur base représentative — `CustomerArchivingMigrationTests.cs` (+2, nouveau)
Base migrée à l'état **d'avant P3-2B** et **peuplée** (client + ordonnance + vente insérés en SQL brut, au schéma de
l'époque) :

- **état initial prouvé destructeur** : `Prescriptions → CASCADE`, `Sales → SET NULL`, colonne `IsArchived` absente ;
- après `Migrate()` : migration appliquée, **aucune dérive de modèle**, **les 3 lignes conservées** (montants,
  numéro de vente, rattachement client intacts), **backfill `IsArchived = false`**, les deux FK en **`RESTRICT`**,
  et la suppression du client **désormais refusée par la base**.

### Tests existants réparés (non affaiblis)
4 tests de bases **historiques** échouaient parce que leurs fixtures inséraient un `Customer` **via le modèle EF
courant** dans une base migrée à un état **antérieur** — donc dépourvue de la colonne. Les fixtures insèrent
désormais la ligne en **SQL brut, au schéma de l'époque** (ce qu'une base « historique » exige de toute façon).
**Aucune assertion n'a été affaiblie** ; le couplage au modèle courant est supprimé, ce qui évite la même casse à
chaque future colonne.

---

## 12. Multi-poste

- **Contrôle applicatif** (`ExistsByCustomerIdAsync` ×2) ⇒ fournit le **message métier** clair. C'est un
  « check-then-act » **non atomique** : à lui seul, il **perdrait la course**.
- **Les deux FK `Restrict`** ⇒ **rempart atomique**. La base refuse le `DELETE`, quel que soit l'entrelacement des
  postes. **Prouvé** par le test de course (§11).
- **Fenêtre résiduelle assumée** : si un historique est créé **entre** le contrôle et le `DELETE`, la suppression
  échoue au niveau **SQL** — une **`DbUpdateException`** (contrainte FK) remonte, **et non** la
  `BusinessRuleException`. Le client n'est **pas** supprimé et **aucune donnée n'est perdue** : l'issue est
  **sûre**, seul le **message** diffère.
- **Archivage** : `UPDATE` d'un booléen, **idempotent**, convergent — sûr sans jeton de concurrence
  (ADR-PROD-DB-001).

### Audit de la traduction des violations FK (demandé §14 du prompt)

**Constat vérifié dans le dépôt** : [`PersistenceErrorMapper`](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs)
sait classer `SQLITE_CONSTRAINT` (19) en `PersistenceErrorCategory.ConstraintViolation`, **mais il n'est appelé que
par [`EfTransactionRunner`](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs)**. Or
`DeleteCustomerUseCase` est **mono-écriture** et n'utilise **pas** de runner : son `SaveChangesAsync` passe par
`UnitOfWork`, qui **ne traduit rien**. La `DbUpdateException` remonterait donc **brute**.

**Décision : ne pas traduire en P3-2B.** Rendre ce cas distinguable proprement exigerait soit d'ajouter un
`ITransactionRunner` à une opération mono-écriture (contraire à la convention du dépôt, et un runner
« décoratif » ne serait qu'un prétexte au mapping), soit de faire remonter la traduction dans `UnitOfWork` — un
changement **transverse** touchant **tous** les use cases d'écriture, très au-delà du périmètre Clients. Par
ailleurs, `PersistenceErrorMapper` ne distingue pas **quelle** contrainte a été violée (FK client vs autre) sans un
**parsing du message SQLite**, explicitement **interdit** par le prompt (fragile, non provider-neutral).

**Risque résiduel documenté** (§14, non inventé) : dans la fenêtre de course, l'utilisateur verrait un message
technique de sauvegarde plutôt que « ce client possède un historique ». **Aucune donnée n'est perdue.** La
traduction propre relève d'une étape transverse « erreurs de persistance » (candidate : normaliser la traduction
dans `UnitOfWork.SaveChangesAsync`, ce qui bénéficierait à **tous** les domaines).

---

## 13. Dépendances

- **Aucun paquet NuGet ajouté** (top-level ou transitif). Rappel : `FluentAssertions` reste **6.x** (v7+
  commercial) — **non touché**.
- **Aucune nouvelle référence projet.**
- `MMV.Application` reste **pure** : référence projet **`MMV.Domain` seule** ; package top-level
  **`Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul**. Aucun type EF/Avalonia dans Application.

---

## 14. Fichiers modifiés

**Domain (4)** — `Entities/Customer.cs` (+`IsArchived`, `Archive`, `Reactivate`) ·
`Interfaces/Repositories/ICustomerRepository.cs` (+`ListAsync`) · `IPrescriptionRepository.cs` ·
`ISaleRepository.cs` (+`ExistsByCustomerIdAsync`).

**Application (8)** — `DeleteCustomer/DeleteCustomerUseCase.cs` + `IDeleteCustomerUseCase.cs` ·
`ListCustomers/{ListCustomersUseCase,ListCustomersQuery,CustomerListItemDto}.cs` ·
`ListCustomersForPicker/{ListCustomersForPickerUseCase,IListCustomersForPickerUseCase}.cs` ·
`DependencyInjection.cs`.
**Créés (4)** : `SetCustomerArchived/{SetCustomerArchivedUseCase,ISetCustomerArchivedUseCase,SetCustomerArchivedCommand,SetCustomerArchivedResult}.cs`.

**Infrastructure (6 + migration)** — `Data/Configurations/{Customer,Prescription,Sale}Configuration.cs` (FK
`Restrict` + colonne) · `Repositories/{Customer,Prescription,Sale}Repository.cs` · `Data/SqliteDatabaseManager.cs`
et `Data/SqliteSchemaVerifier.cs` (adoption des bases historiques, §9.1) · `Migrations/OpticDbContextModelSnapshot.cs`.
**Créés** : `Migrations/20260713215132_AddCustomerArchivingAndProtectHistory.cs` (+ `.Designer.cs`).

**Tests (6 modifiés, 4 créés)** — modifiés : `DeleteCustomerUseCaseTests`, `ListCustomersUseCaseTests`,
`ListCustomersForPickerUseCaseTests`, `DateTimeDefaultValuesMigrationTests`, `DocumentSequencesAdoptionTests`,
`SqliteHistoricalDateTimeDefaultsTests` · créés : `SetCustomerArchivedUseCaseTests`, `CustomerArchivingTests`,
`CustomerDeletionConstraintsTests`, `CustomerArchivingMigrationTests`.

**Docs (2)** — `docs/architecture/P3-business-rules-roadmap.md` (correction factuelle + exigences P3-3 / P3-7) ·
ce rapport.

**Intacts** : `src/MMV.App/**` · `tests/MMV.App.Tests/**` · `.github/**` · `*.csproj` / `*.sln`.

---

## 15. Contrôles exécutés

`git branch --show-current` · `git status --short` · `git diff --stat` · `git diff --check` ·
`dotnet restore` · `dotnet build --no-restore -c Debug` · `dotnet test --no-build -c Debug` ·
`dotnet list … --vulnerable --include-transitive` · `dotnet tool restore` ·
`dotnet ef migrations has-pending-model-changes` · `dotnet ef migrations script` (inspection du SQL) ·
`dotnet list src/MMV.Application … reference` / `… package`.

---

## 16. Résultats

| Contrôle | Attendu | Résultat |
|---|---|---|
| Build | vert | ✅ **0 avertissement, 0 erreur** |
| Tests | > 592 | ✅ **626** (App **192** · Application **198** · Domain **236**) — **+34** |
| Vulnérabilités | 0 | ✅ **0** (7 projets) |
| `has-pending-model-changes` **après** migration | false | ✅ « No changes … since the last migration » |
| `MMV.Application` pure | Domain + DI.Abstractions | ✅ |
| Modification UI | aucune | ✅ `src/MMV.App/**` et `tests/MMV.App.Tests/**` **intacts** |
| Modification CI | aucune | ✅ `.github/**` intact |
| Nouvelle dépendance | aucune | ✅ |
| `git diff --check` | propre | ✅ |

---

## 17. Risques résiduels

1. **Aucun garde-fou à l'écriture pour un client archivé** *(le plus important — assumé et documenté)*.
   `CreatePrescriptionUseCase` et `RegisterSaleUseCase` **ne vérifient pas** `IsArchived`. L'exclusion des listes et
   du picker rend le cas **improbable par l'UI**, mais **ne l'empêche pas** (identifiant fourni directement, écran
   resté ouvert, sélection obsolète). **Il ne faut donc pas prétendre que l'archivage empêche toute nouvelle vente
   ou ordonnance : ce n'est pas codé.** Implémenter le refus ici aurait débordé sur **P3-3** (Ordonnances) et
   **P3-7** (Ventes) — dont `RegisterSaleUseCase`, le cœur transactionnel — **sans leur audit** ; et ne le faire que
   pour les ordonnances aurait créé une protection **trompeuse** (ventes toujours ouvertes). **Exigence obligatoire
   inscrite dans la roadmap** pour P3-3 et P3-7.
   *Conséquence si le risque se réalise* : un document est créé sur un client archivé — **aucune perte de donnée**,
   situation corrigeable par réactivation.
2. **Course multi-poste : message dégradé** (§12). Dans la fenêtre entre le contrôle et le `DELETE`, une
   `DbUpdateException` (contrainte FK) remonte au lieu de la `BusinessRuleException`. **Issue sûre** (rien n'est
   supprimé), **message technique**. Traduction propre = étape transverse « erreurs de persistance ».
3. **UI non recâblée** : `CustomersListViewModel` ne gère **pas encore** le refus (il tomberait dans son `catch`
   générique), n'offre ni bouton **Archiver/Réactiver** ni filtre. **C'est précisément P3-2C.**
4. **Pas d'index sur `IsArchived`** : volumétrie d'un magasin faible ; à mesurer plutôt qu'à supposer.
5. **Style d'entité mixte** : `Customer` combine setters publics (existant) et état encapsulé (nouveau) — §4.
6. **Reconstruction de table SQLite** par la migration : non destructive et **prouvée par test** sur base peuplée,
   mais toute base utilisateur doit être **sauvegardée avant mise à jour** (le `SqliteDatabaseManager` le fait déjà).
7. **Portage futur PostgreSQL/SQL Server** : `Restrict`/`NoAction` sont provider-neutres, mais les FK devront être
   re-vérifiées au portage (ADR-PROD-DB-001).

---

## 18. Verdict

**P3-2B = GO local.**

- Modèle Domain : `IsArchived` + `Archive`/`Reactivate` idempotents ✅
- Requêtes d'existence légères (`AnyAsync`, aucune matérialisation) ✅
- Suppression **refusée** si historique, **autorisée** si client vierge ✅
- Archivage / réactivation non destructifs, **strictement idempotents** (état déjà atteint ⇒ **aucune écriture**,
  `UpdatedAt` intact) ✅
- Archivés exclus des listes (par défaut) et **toujours** du picker, **filtrés en SQL** ✅
- Migration inspectée, **non destructive**, backfill neutre, **prouvée sur base peuplée** ✅
- **Les deux FK en `Restrict`** — rempart atomique **prouvé sur vrai SQLite**, y compris en course ✅
- Adoption des bases historiques préservée (régression détectée **et** corrigée) ✅
- Build vert · **626 tests** · 0 vulnérabilité · EF vert · `MMV.Application` pure ✅
- **Aucune modification UI, CI ou dépendance** ✅
- Risque résiduel n°1 **explicitement documenté**, pas dissimulé ✅

**Aucun commit, aucun push.**

---

## 19. Prochaine étape — P3-2C (UI Clients)

1. **Confirmation** avant suppression (aujourd'hui : suppression immédiate, sans dialogue).
2. **Message de refus** : afficher `DeleteCustomerUseCase.CustomerHasHistoryMessage` (attraper
   `BusinessRuleException`) et **proposer l'archivage** — le `catch` générique actuel afficherait un message
   d'erreur brut.
3. **Bouton Archiver / Réactiver** câblé sur `ISetCustomerArchivedUseCase`.
4. **Filtre actifs / archivés** via `ListCustomersQuery.IncludeArchived` + affichage de `CustomerListItemDto.IsArchived`.
5. **Rafraîchissement multi-poste** : recharger la liste après refus/archivage (la VM ne fait aujourd'hui qu'un
   retrait local).

> Puis **P3-3** et **P3-7** devront honorer l'**exigence obligatoire** inscrite dans la roadmap : refuser la
> création d'une **ordonnance** / d'une **vente** pour un client **archivé**, **au moment de l'écriture**.
