# P3-0B — Rapport : ADR multi-poste / stratégie base de données production

> Étape **P3-0B** de la phase P3 (règles métier). Production d'une **décision d'architecture** :
> [ADR-PROD-DB-001](../architecture/adr-prod-db-001-multi-poste-database-strategy.md). **Aucun code source,
> aucun test, aucune migration modifiés.** Seuls des documents ont été créés/mis à jour.

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-0B |
| `EXECUTION_MODE` | ADR_AND_ROADMAP_UPDATE |
| `SOURCE_BRANCH` | p3-business-rules |
| `ALLOW_CODE_CHANGES` | false |
| `ALLOW_DOC_CHANGES` | true |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : `p3-business-rules` ✅
- Working tree : **propre** au démarrage (`git status --short` vide) ✅
- P3-0 présent : `a5a1fe8 docs(P3-0): add business rules roadmap and audit` ✅
- CI P3-0 documentée : `9413cfa docs(P3-0): record business audit CI validation` (HEAD) ✅
- CI activée pour p3 : `21bdfb0 ci(P3): enable CI for p3 branches` ✅

**Précondition = GO.**

## 3. Baseline

| Contrôle | Attendu | Résultat |
|---|---|---|
| `dotnet restore MMV.sln` | OK | OK (à jour) ✅ |
| `dotnet build … -c Debug --no-restore` | vert | **Réussi — 0 avertissement, 0 erreur** ✅ |
| `dotnet test … --no-build -c Debug` | ≥ 585 | **585 réussis / 0 échec / 0 ignoré** ✅ |
| → détail | — | App.Tests 192 · Application.Tests 170 · Domain.Tests 223 |
| `dotnet list … --vulnerable --include-transitive` | 0 | **0 vulnérabilité** (7 projets) ✅ |
| `dotnet tool restore` | OK | dotnet-ef 8.0.27 restauré ✅ |
| `dotnet ef migrations has-pending-model-changes` | false | **false** (*No changes … since the last migration*) ✅ |
| Aucune migration ajoutée | — | aucune ✅ |
| `MMV.Application` référence projet | `MMV.Domain` seul | `..\MMV.Domain\MMV.Domain.csproj` seul ✅ |
| `MMV.Application` packages | `DI.Abstractions` seul | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul ✅ |

**Baseline = GO.**

## 4. Audit DB actuel du dépôt

Résultat des scans (`rg` sur `src`/`tests`/`docs`) et lecture des fichiers d'infrastructure.

### 4.1 Usage actuel de SQLite

- **Provider unique = SQLite**, partout (production **et** tests). Aucun autre provider dans le code.
- Production : `options.UseSqlite(...)` dans
  [App.axaml.cs:119-120](../../src/MMV.App/App.axaml.cs#L119-L120) (composition root réel).
- Infrastructure : `AddInfrastructure` (inerte, non appelée) fait aussi `UseSqlite`
  ([DependencyInjection.cs:37](../../src/MMV.Infrastructure/DependencyInjection.cs#L37)).
- Tests : SQLite fichier ou `:memory:` (ex. `SqliteHistoricalDate*`, `DbContextTests`, services).

### 4.2 Où la connection string est configurée

- Source unique : [`SqliteDatabasePathResolver`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs).
  Ordre : chemin explicite → variable `MMV_DATABASE_PATH` → configuration `OpticDatabase` (Data Source=…) →
  défaut `%LOCALAPPDATA%\ManageMyVision\mmv.db`.
- **Conséquence multi-poste** : par défaut, **chemin local par machine** → **chaque poste a sa propre base
  isolée**. Aucun partage de données entre postes aujourd'hui.

### 4.3 Où les migrations sont appliquées / vérifiées

- [`SqliteDatabaseManager.PrepareDatabase`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) :
  détection d'état (vierge / historique / migrée), sauvegarde préalable, `context.Database.Migrate()`,
  adoption baseline, journal. Appelée au démarrage ([App.axaml.cs:212](../../src/MMV.App/App.axaml.cs#L212)).
- **Conçu pour un seul processus / une base locale.** En multi-poste, plusieurs postes ne doivent pas
  appliquer une migration concurremment (cf. ADR §6).

### 4.4 Où les transactions sont gérées

- `ITransactionRunner` / [`EfTransactionRunner`](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs) :
  frontière transactionnelle réutilisable partageant le `DbContext` de la portée.

### 4.5 Où le stock atomique est protégé

- [`EfStockMutationService`](../../src/MMV.Infrastructure/Persistence/EfStockMutationService.cs) : **UPDATE
  conditionnel atomique** (`SET StockQuantity = StockQuantity - @q WHERE StockQuantity >= @q` via
  `ExecuteUpdateAsync`). Correct au niveau SQL (la base décide) — **déjà favorable multi-poste** — mais
  documenté sous l'**hypothèse SQLite « écrivain unique »**.
- Numérotation : [`EfNumberSequenceService`](../../src/MMV.Infrastructure/Persistence/EfNumberSequenceService.cs)
  (incrément atomique conditionnel).
- **Défaut connu** : `AdvanceOrderStatusUseCase` décrémente en **direct** (`Product.StockQuantity -= …`) hors
  service de mutation (cf. [audit P3-0 §7](P3-0-business-audit-report.md)) → à corriger en P3-5, **critique**
  en multi-poste.

### 4.6 Provider PostgreSQL / SQL Server

- **Absent du code.** `Npgsql` / `UseNpgsql` / `UseSqlServer` : **0** occurrence dans `src`/`tests`.
- Présents **uniquement** dans des documents (SaaS/roadmap/archive), à titre prospectif.

### 4.7 Stratégie de concurrence

- **Aucune au niveau ligne** : `0` occurrence `RowVersion` / `IsRowVersion` / `IsConcurrencyToken`.
- `PersistenceException.Concurrency` (enum) existe mais **aucun jeton** n'est configuré.
- Sujet déjà cadré comme candidat dans [adr-candidates §concurrence](../architecture/adr-candidates.md)
  (SQLite sans `rowversion` natif ; options jeton `Guid`/entier, update conditionnel, stratégie SGBD serveur).

### 4.8 Risques actuels pour le multi-poste

1. **Données non partagées** (base locale par poste) → incohérence si on branche naïvement plusieurs postes.
2. **SQLite sur réseau** = risque de **corruption** (verrouillage de fichiers non fiable sur SMB/NFS).
3. **Migrations par poste** non sérialisées vis-à-vis d'une base centrale.
4. **Pas de vérification version app ↔ schéma DB** au démarrage.
5. **Pas de jeton de concurrence** activé.
6. **Décrément direct** de stock (`AdvanceOrderStatusUseCase`) hors service atomique.

## 5. Décision produit multi-poste V1

**MMV V1 production doit être multi-poste.** Ce n'est pas une option future : c'est indispensable pour un vrai
magasin d'optique (plusieurs personnes, plusieurs ordinateurs partageant clients, ordonnances, stock,
commandes, ventes, fiches atelier, utilisateurs). Le multi-poste **n'est pas** du multi-tenant (toujours hors
périmètre) : **un magasin, une base centrale, plusieurs postes**.

## 6. Options comparées

| Option | Description | V1 prod multi-poste | Verdict |
|---|---|---|---|
| **A** | SQLite local, un fichier par poste | non (données isolées) | **OK dev/test/démo/solo — pas prod multi-poste** |
| **B** | SQLite partagé sur dossier réseau | non (corruption) | **Non supporté** |
| **C** | PostgreSQL local magasin (client/serveur) | excellente | **Candidat principal** |
| **D** | SQL Server Express local magasin | bonne (limites Express) | **Candidat secondaire** |
| **E** | Cloud / API centrale / SaaS | surdimensionné V1 | **Hors V1 — plus tard** |

Détail complet : [ADR-PROD-DB-001 §4](../architecture/adr-prod-db-001-multi-poste-database-strategy.md).

## 7. Décision ADR

- Production multi-poste ⇒ **base client/serveur obligatoire**.
- **SQLite local** conservé pour **dev / test / démo / mono-poste local**.
- **SQLite réseau non supporté** officiellement.
- **Candidat principal = PostgreSQL** ; **secondaire = SQL Server Express**.
- **Choix final différé à un spike technique** (non tranché en P3-0B).
- **Cloud / API / SaaS reportés.**

## 8. Impacts P3

- Toute règle métier P3 **compatible multi-poste** : pas d'hypothèse mono-utilisateur, transactions robustes,
  stock atomique obligatoire (pas de décrément direct), pas de cache local trompeur, conflits concurrents
  prévus, fiches atelier centralisées/versionnées, workflow commande multi-poste, vente atomique avec rollback.
- **Étapes critiques** : **P3-5 Stock**, **P3-6 Commandes**, **P3-6B Fiche atelier** (persistée/centralisée),
  **P3-7 Ventes**.

## 9. Impacts P4 / production

- Updates applicatives **par poste** ; migrations DB sur **base centrale** ; **backup obligatoire avant
  migration** ; **une seule** application de migration à la fois ; **vérification version app ↔ schéma DB** au
  démarrage, **blocage** d'un client trop ancien face à un schéma trop récent.
- Infrastructure future : configuration provider, support PostgreSQL/SQL Server, migrations provider-specific,
  installation serveur local, backup/restore central, tests multi-poste, gestion de concurrence, licence
  par boutique/poste (plus tard).

## 10. Fichiers modifiés

**Créés (docs uniquement) :**

- `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md`
- `docs/implementation/P3-0B-multi-poste-db-strategy-report.md` (ce fichier)

**Mis à jour (docs uniquement) :**

- `docs/architecture/P3-business-rules-roadmap.md` (cadre production V1, principe §1.7, ordre des étapes
  avec P3-0B + mentions « multi-poste safe », détail P3-0B, point dur transverse §4.7).

**Aucun** fichier sous `src/**`, `tests/**`, `migrations/**`, `.github/**`, ni `*.csproj`/`*.sln`/packages modifié.

## 11. Contrôles exécutés

- `git branch --show-current` / `git status --short` / `git log -10 --oneline`
- `dotnet restore MMV.sln` · `dotnet build … --no-restore -c Debug` · `dotnet test … --no-build -c Debug`
- `dotnet list MMV.sln package --vulnerable --include-transitive` · `dotnet tool restore`
- `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build`
- `dotnet list src/MMV.Application/MMV.Application.csproj reference` / `… package`
- Scans code : `UseSqlite|Sqlite|…|OpticDbContext|ITransactionRunner|IStockMutationService` ;
  `Npgsql|PostgreSQL|SqlServer|UseSqlServer|UseNpgsql` ; `RowVersion|Concurrency|IsRowVersion|…`.
- Lecture ciblée : `SqliteDatabasePathResolver`, `SqliteDatabaseManager`, `EfStockMutationService`,
  `DependencyInjection` (App + Infra), `adr-candidates §concurrence`.
- Contrôles finaux (§12) après rédaction des docs.

## 12. Résultats (contrôles finaux)

Working tree = **seuls les docs P3-0B créés/mis à jour** ; `git diff --check` propre ; build vert ;
**585 tests** ; 0 vulnérabilité ; `has-pending-model-changes = false` ; aucune migration ; `MMV.Application`
pure (Domain + `DI.Abstractions`) ; aucun code source modifié.

## 13. Risques résiduels

- **Effort d'infrastructure futur acté** (provider serveur, migrations provider-specific, installation,
  backup, versioning, concurrence) — assumé, séquencé hors P3.
- **Choix PostgreSQL vs SQL Server Express non tranché** — laissé ouvert au spike (risque de report).
- **Décrément direct de stock** encore présent (`AdvanceOrderStatusUseCase`) — corrigé en P3-5.
- **Tests actuels mono-poste** (SQLite) — ne couvrent pas encore la concurrence réelle multi-connexion.

## 14. Verdict

**P3-0B = GO local.**

- ADR multi-poste créée ([ADR-PROD-DB-001](../architecture/adr-prod-db-001-multi-poste-database-strategy.md)) ✅
- Roadmap P3 mise à jour (P3-0B, ordre, principes, points durs) ✅
- Rapport P3-0B créé (ce fichier) ✅
- MMV V1 production multi-poste documentée comme **obligatoire** ✅
- SQLite réseau **explicitement non supporté** ✅
- PostgreSQL / SQL Server Express **comparés** (principal / secondaire) ✅
- Aucun code source / test / migration modifié ✅
- Build vert · 585 tests · 0 vulnérabilité · EF vert · `MMV.Application` pure ✅

## 15. Validation CI distante

| Élément | Valeur |
|---|---|
| Commit testé | `be65134` — docs(P3-0B): define multi-workstation database strategy |
| Branche testée | `p3-business-rules` |
| Run CI | [28903769464](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/28903769464) |
| Workflow | `CI` (événement `push`) |
| Restore | ✅ OK |
| Build | ✅ vert — 0 avertissement, 0 erreur |
| Test | ✅ **585 réussis / 0 échec / 0 ignoré** (App.Tests 192 · Application.Tests 170 · Domain.Tests 223) |
| Audit NuGet | ✅ 0 vulnérabilité High/Critical (7 projets) |
| Restore .NET tools | ✅ `dotnet-ef` restauré |
| EF Core pending model changes | ✅ `false` (*No changes have been made to the model since the last migration*) |
| Statut final du workflow | ✅ `completed` / `success` |

**P3-0B = GO DÉFINITIF COMPLET.**

## 16. Prochaine étape candidate

**P3-1 — Standardisation des validations / `Result`** : poser le socle de validation **côté Application**
(aujourd'hui absent) + convention `Result`/exceptions, avec un domaine pilote, **avant** les domaines métier.
Ne **pas** démarrer P3-1 dans ce cycle : P3-0B s'arrête ici.
