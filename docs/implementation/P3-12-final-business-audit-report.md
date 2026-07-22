# P3-12 — Audit final de clôture P3 et verdict de merge

> **Mode** : `FINAL_AUDIT_AND_WRITE_REPORT_ONLY`. Aucun code, test, migration, UI ou document existant n'a été
> modifié. Aucun commit, push, merge, rebase ou cherry-pick n'a été effectué. Ce rapport est le **seul** fichier
> créé.
>
> **Principe directeur appliqué** : *le dépôt réel prime sur la roadmap et sur les rapports*. Chaque affirmation
> importante ci-dessous est adossée à une commande exécutée, un fichier/ligne inspecté ou un test exécuté. Là où
> un rapport antérieur affirme une chose et où le code en dit une autre, **le code tranche** et l'écart est
> signalé (§8, §16, §32).
>
> ⚠️ **Portée de cet en-tête.** Le mode `FINAL_AUDIT_AND_WRITE_REPORT_ONLY` décrit le **premier cycle** — l'audit
> proprement dit, §1 à §35, qui n'a effectivement rien modifié. La **§36 consigne une remédiation postérieure**,
> exécutée sous un mandat distinct (`REMEDIATE_FINAL_P3_BLOCKER…`) : elle a modifié du code, des tests et des
> documents, et elle **révise les deux verdicts de §35**, laissés barrés et non réécrits. Lire §35 sans §36
> donnerait un verdict périmé.
>
> ⚠️ **La §37 est postérieure au commit et à la CI.** Elle consigne la validation CI définitive du correctif
> P3-12. Le verdict `GO LOCAL` de §36.8, prononcé **avant** commit, y est levé. **Le verdict opposable de ce
> document est celui de §37.**

---

## 1. Paramètres

| Paramètre | Valeur attendue | Valeur observée | Statut |
|---|---|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` | idem | ✅ |
| Branche | `p3-business-rules` | `p3-business-rules` | ✅ |
| HEAD | `4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365` | identique (local **et** distant) | ✅ |
| Run CI | `29958212378` | `success` sur le SHA exact | ✅ |
| Tests | 1493 | 1493 | ✅ |
| Domain / Application / App | 647 / 607 / 239 | 647 / 607 / 239 | ✅ |
| Tests au début de P3 | 585 | référence P3-0 | — |

---

## 2. Portes Git et CI

### 2.1 Git

```
git fetch origin main p3-business-rules --prune
git branch --show-current     → p3-business-rules
git rev-parse HEAD            → 4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365
git rev-parse origin/p3-business-rules → 4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365
git status --short            → ?? design-handoff/   ?? design/   ?? docs/ui/
git diff --check              → (vide, exit 0)
```

- HEAD local **et** distant sont identiques au SHA attendu : la branche est intégralement poussée.
- **Aucun fichier suivi modifié.** Les trois seuls éléments non suivis (`design-handoff/`, `design/`,
  `docs/ui/`) sont exactement ceux autorisés par le cadrage.
- `git diff --check` : aucune erreur d'espaces / marqueur de conflit.

`git log -12 --oneline --decorate` confirme la chaîne P3 terminale :
`4c2a1e5` (P3-8 correctif) ← `8a6d5b3` (P3-11) ← `8d4bf49` (P3-10) ← `057df0f` (P3-9) ← `b30f774` (P3-8) …

### 2.2 CI

```
gh run view 29958212378 --json databaseId,headSha,headBranch,status,conclusion,event,url,jobs
```

| Champ | Valeur |
|---|---|
| `databaseId` | `29958212378` |
| `headSha` | `4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365` (exact) |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | **`success`** |

Job unique **« Restore / Build / Test / Scan »** — `conclusion: success`. Étapes obligatoires toutes
`success`, y compris : **Restore** (5), **Build** (6), **Test** (7), **Audit des packages vulnérables**
(8), **Check EF Core pending model changes** (10). **Aucun job obligatoire en échec.**

**Porte 2 : franchie.**

---

## 3. Relation avec `main`

```
git merge-base origin/main HEAD            → 1e28f1e6a8e9de08ab75e042cad37800c0390495
git rev-parse origin/main                  → 1e28f1e6a8e9de08ab75e042cad37800c0390495
git rev-list --left-right --count origin/main...HEAD → 0    28
```

Réponses demandées :

1. **HEAD contient-il le dernier `origin/main` ?** — **Oui.** La base de fusion **est** `origin/main`
   (`1e28f1e`) : la branche descend directement de la pointe actuelle de `main`.
2. **Commits uniquement dans `main`** — **0**.
3. **Commits uniquement dans P3** — **28**.
4. **Le merge exige-t-il une mise à jour préalable de la branche ?** — **Non.** `main` n'a pas avancé depuis
   le point de départ ; le merge est un **fast-forward** possible.
5. **Changements concurrents sur les mêmes fichiers ?** — **Non**, par construction : `main` n'ayant aucun
   commit exclusif, aucun fichier n'a pu diverger.
6. **Taille réelle du diff P3** — `git diff --stat origin/main...HEAD` :
   **295 fichiers, 56 395 insertions, 2 192 suppressions.**
7. **Projets / domaines touchés** (`git diff --name-only`, agrégé) :

   | Zone | Fichiers |
   |---|---:|
   | `src/MMV.Application` | 75 |
   | `tests/MMV.Application.Tests` | 54 |
   | `src/MMV.Domain` | 42 |
   | `src/MMV.Infrastructure` | 39 |
   | `tests/MMV.Domain.Tests` | 34 |
   | `docs/implementation` | 28 |
   | `src/MMV.App` | 14 |
   | `tests/MMV.App.Tests` | 5 |
   | `docs/architecture` | 2 · `docs/domain` 1 · `.github/workflows/ci.yml` 1 |

8. **Migrations ajoutées pendant P3** — `git diff --name-status origin/main...HEAD -- src/MMV.Infrastructure/Migrations/` :
   **cinq migrations, toutes en statut `A` (ajoutées), aucune en `M`** ; seul
   `OpticDbContextModelSnapshot.cs` est modifié (attendu). Détail en §23.

### 3.1 Contrôle de conflit non mutatif

- **Version Git** : `git version 2.47.1.windows.2` (supporte `merge-tree --write-tree`).
- **Commande exacte** : `git merge-tree --write-tree origin/main HEAD`
- **Code de sortie** : **0**
- **Sortie** : `2e827ae4e1df0bbcef7ae470802d69f042710477` (un OID d'arbre seul, sans section
  `CONFLICT`/`Auto-merging`).
- **Conflits** : **aucun**. **Fichiers concernés** : aucun.

Un code de sortie 0 assorti d'un arbre écrit sans bloc de conflit signifie une fusion propre. Aucun
`git merge`, `git rebase` ni `git checkout main` n'a été exécuté.

---

## 4. Baseline finale

Toutes les commandes ont été exécutées sur le HEAD attendu.

| Contrôle | Commande | Résultat |
|---|---|---|
| Restore | `dotnet restore MMV.sln` | OK |
| Build | `dotnet build MMV.sln --no-restore -c Debug` | **Build succeeded. 0 Warning(s). 0 Error(s).** |
| Domain | `dotnet test tests/MMV.Domain.Tests/…` | **Passed! Failed: 0, Passed: 647, Skipped: 0** |
| Application | `dotnet test tests/MMV.Application.Tests/…` | **Passed! Failed: 0, Passed: 607, Skipped: 0** |
| App | `dotnet test tests/MMV.App.Tests/…` | **Passed! Failed: 0, Passed: 239, Skipped: 0** |
| Solution | `dotnet test MMV.sln --no-build -c Debug` | 239 + 607 + 647, **0 échec, 0 ignoré** |
| Vulnérabilités | `dotnet list MMV.sln package --vulnerable --include-transitive` | **« has no vulnerable packages »** pour les **7** projets |
| EF drift | `dotnet ef migrations has-pending-model-changes` | **« No changes have been made to the model since the last migration. »** |
| Pureté | `dotnet list src/MMV.Application/… reference` | **une seule** référence : `..\MMV.Domain\MMV.Domain.csproj` |
| Paquets Application | `dotnet list … package` | **un seul** : `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` |
| Espaces | `git diff --check` | vide |

**Total : 647 + 607 + 239 = 1493 tests, 0 échec, 0 ignoré.** Conforme à l'attendu, au test près.

### 4.1 Note honnête sur `dotnet ef migrations list`

La sortie marque `AddNotificationResolution` et `AddNormalizedUsernameAndSecureLocalUsers` comme
**`(Pending)`**. Cela **ne traduit pas** un écart de modèle : `has-pending-model-changes` répond
explicitement « No changes have been made to the model ». Le marqueur `(Pending)` décrit uniquement l'état de
**la base de développement locale du poste**, qui n'a pas exécuté ces deux migrations. Le schéma et le modèle
sont alignés ; **aucune migration n'est en attente au sens du critère de sortie P3**.

### 4.2 Progression depuis P3-0

| Mesure | Valeur |
|---|---:|
| Tests au début de P3 (P3-0, 2026-07-07) | 585 |
| Tests finaux (P3-12) | 1493 |
| **Delta exact** | **+908** |
| Croissance | ≈ **+155 %** |

> Le pourcentage est fourni **à titre informatif uniquement**. Il n'est pas utilisé comme mesure de qualité :
> un test ne vaut que par la propriété qu'il oppose. La valeur probante réelle de P3 tient aux tests exécutés
> sur **vrai SQLite** (migrations, adoption, index physiques, concurrence) et aux **11 scénarios de recette**,
> pas au volume.

---

## 5. Tests ciblés finaux

### 5.1 Acceptance

`dotnet test tests/MMV.Application.Tests/… --filter "FullyQualifiedName~Acceptance"`
→ **Passed! Failed: 0, Passed: 11, Skipped: 0.** Conforme (11 attendus).

### 5.2 Architecture / garde-fous

Les classes ont été identifiées avant exécution (aucun filtre unique ne les couvre toutes) :
`ApplicationArchitectureTests`, `UsersApplicationArchitectureTests`, `SuppliersApplicationArchitectureTests`,
`NotificationsApplicationArchitectureTests`, `NotificationsArchitectureTests`,
`OrderWorkflowArchitectureTests`, `SalesBusinessRulesArchitectureTests`, `AppUiPersistenceGuardrailTests`,
`OrderViewModelDependencyHygieneTests`.

| Filtre | Résultat |
|---|---|
| Domain `~Architecture` | **18 / 18** ✅ |
| Application `~Architecture` | **41 / 41** ✅ |
| App `~Architecture \| ~Guardrail \| ~Dependency` | **11 / 11** ✅ |

**70 tests d'architecture, 0 échec.**

### 5.3 Migrations et schéma SQLite

Classes réellement exécutées : `SqliteDatabaseManagerTests`, `SqliteDatabasePathResolverTests`,
`SqliteHistoricalDatabaseDiagnosticTests`, `SqliteHistoricalDateTimeDefaultsTests`,
`CustomerArchivingMigrationTests`, `DocumentSequencesAdoptionTests`, `DateTimeDefaultValuesMigrationTests`,
`NormalizedUsernameMigrationTests`, `NotificationResolutionMigrationTests`, `WorkshopSheetsAdoptionTests`,
`WorkshopSheetsMigrationTests`, `ProductNormalizedReferenceMigrationTests`.

| Filtre | Résultat |
|---|---|
| Domain `~Data` (schéma, adoption, migrations) | **155 / 155** ✅ |
| Application `~Migration` | **9 / 9** ✅ |

### 5.4 Correctif P3-8

`dotnet test tests/MMV.Domain.Tests/… --filter "FullyQualifiedName~SqliteDatabaseManagerTests"`
→ **Passed! Failed: 0, Passed: 38, Skipped: 0.**

**38** correspond exactement à l'hypothèse haute du cadrage : les **33** tests antérieurs **plus** les cinq cas
**F1–F5** du correctif. Aucun test ignoré.

---

## 6. Progression depuis P3-0

Voir §4.2 : **585 → 1493 (+908)**. Répartition finale : Domain 647 · Application 607 · App **239**.

`MMV.App.Tests` est **rigoureusement stable à 239** depuis P3-4, ce qui corrobore matériellement la décision
de périmètre « backend uniquement » (preuve indépendante en §26).

---

## 7. Registre des étapes P3

Construit à partir de la roadmap, des rapports d'audit et d'implémentation, et **recoupé** avec `git log`,
`git merge-base --is-ancestor` et `gh run view` (§8).

| Étape | Objectif | Rapport | Commit | CI | Tests à la clôture | Migration | UI | Verdict documenté |
|---|---|---|---|---|---:|---|---|---|
| **P3-0** | Audit métier initial | `P3-0-business-audit-report.md` | `a5a1fe8`, `9413cfa` | CI P3-0 | 585 | non | non | GO |
| **P3-0B** | ADR multi-poste / DB prod | `P3-0B-multi-poste-db-strategy-report.md` + ADR-PROD-DB-001 | `be65134`, `870c81a` | CI P3-0B | 585 | non | non | ADR acceptée |
| **P3-1** | Socle validation / `Result` | `P3-1-validation-result-report.md` | `6e39bd2`, `9d2a865` | CI P3-1 | 592 | non | non | GO |
| **P3-2** | Clients (A/B/C) | `P3-2A`, `P3-2B`, `P3-2C` | `30dce45`, `8a518eb`, `5ee8306`, `2f260de`, `8f019fb` | CI P3-2B, P3-2C | — | **oui** (`AddCustomerArchivingAndProtectHistory`) | **oui (P3-2C)** | GO |
| **P3-3** | Ordonnances (A/B/C) | `P3-3A`, `P3-3B`, `P3-3C` | `c9c7ffe`, `8dda9cc`, `982b349`, `c0c97bd` | CI P3-3B | — | non | **oui (P3-3C)** | « TERMINÉ localement » |
| *(hors étape)* | Refonte écran de connexion | *aucun rapport P3* | `d8af704` | — | — | non | **oui** | *non documenté dans la roadmap* |
| **P3-4** | Produits (A/B) | `P3-4A`, `P3-4B` | `2709bfe`, `c246ebc` | CI P3-4 | 799 | **oui** (`AddProductNormalizedReference…`) | non | GO LOCAL + CI |
| **P3-5** | Stock / mouvements | `P3-5` audit + impl. | `3e7a1ab` | `29623464755` ✅ | 819 | non | non | `P3-5-CI = GO` |
| **P3-6** | Commandes | `P3-6` audit + impl. | `a4d7380` | `29665945780` ✅ | 869 | non | non | `P3-6-CI = GO` |
| **P3-6B** | Fiche atelier / QC | `P3-6B` audit + impl. | `53d689e` | `29706846895` ✅ | 1031 | **oui** (`AddWorkshopSheets`) | non | `P3-6B-CI = GO` |
| **P3-7** | Ventes | `P3-7` audit + impl. | `ae0e35d` | `29709378954` ✅ | 1134 | non | non | `P3-7-CI = GO` |
| **P3-8** | Notifications | `P3-8` audit + impl. | `b30f774` | `29783755704` ✅ | 1202 | **oui** (`AddNotificationResolution`) | non | `P3-8-CI = GO` |
| **P3-9** | Fournisseurs | `P3-9` audit + impl. | `057df0f` | `29831430629` ✅ | 1308 | non | non | `P3-9-CI = GO` |
| **P3-10** | Utilisateurs locaux | `P3-10` audit + impl. | `8d4bf49` | `29938082120` ✅ | 1413 | **oui** (`AddNormalizedUsername…`) | non | `P3-10-CI = GO` |
| **P3-11** | Recette bout-en-bout | `P3-11` audit + impl. | `8a6d5b3` | `29952285622` ✅ | 1479 | non | non | `P3-11-CI = GO` |
| **Correctif P3-8** | Index unique vérifié physiquement | `P3-8-low-stock-index-verification-correction-report.md` | `4c2a1e5` | `29958212378` ✅ | **1493** | non | non | `GO LOCAL` *(doc à finaliser, §32)* |
| **P3-12** | Audit final | **le présent rapport** | *(aucun — non committé **au moment de l'audit** ; voir §37)* | *(aucune **à cet instant** ; voir §37)* | 1493 | non | non | §34 |

Aucun commit ni run CI n'a été inventé pour une sous-étape qui n'en possède pas : les cellules concernées
portent explicitement « — » ou le nom du run tel que déclaré dans la documentation.

---

## 8. Vérification de la chaîne commits / CI

### 8.1 Ancestralité des commits

`git cat-file -e <SHA>^{commit}` puis `git merge-base --is-ancestor <SHA> HEAD` — **17 SHA testés, 17 existent
et sont ancêtres de HEAD** :

`a5a1fe8`, `be65134`, `6e39bd2`, `8a518eb`, `2f260de`, `8dda9cc`, `c0c97bd`, `c246ebc`, `3e7a1ab`, `a4d7380`,
`53d689e`, `ae0e35d`, `b30f774`, `057df0f`, `8d4bf49`, `8a6d5b3`, `4c2a1e5` → tous **OK ancestor**.

### 8.2 Runs CI explicitement déclarés définitifs

| Étape | SHA | Ancêtre du HEAD ? | Run | SHA du run exact ? | Conclusion |
|---|---|---:|---:|---:|---|
| P3-5 | `3e7a1ab` | ✅ | `29623464755` | ✅ | `push` / `completed` / **success** |
| P3-6 | `a4d7380` | ✅ | `29665945780` | ✅ | `push` / `completed` / **success** |
| P3-6B | `53d689e` | ✅ | `29706846895` | ✅ | `push` / `completed` / **success** |
| P3-7 | `ae0e35d` | ✅ | `29709378954` | ✅ | `push` / `completed` / **success** |
| P3-8 | `b30f774` | ✅ | `29783755704` | ✅ | `push` / `completed` / **success** |
| P3-9 | `057df0f` | ✅ | `29831430629` | ✅ | `push` / `completed` / **success** |
| P3-10 | `8d4bf49` | ✅ | `29938082120` | ✅ | `push` / `completed` / **success** |
| P3-11 | `8a6d5b3` | ✅ | `29952285622` | ✅ | `push` / `completed` / **success** |
| Correctif P3-8 | `4c2a1e5` | ✅ (= HEAD) | `29958212378` | ✅ | `push` / `completed` / **success** |

**Neuf runs vérifiés, neuf conformes** : SHA exact, événement `push`, statut `completed`, conclusion
`success`. Aucun run ne pointe vers un SHA autre que celui documenté.

### 8.3 Incohérences documentaires (à distinguer d'une défaillance du code)

Ces points sont **documentaires** ; aucun ne traduit un défaut de code (classement complet en §32) :

1. Les étapes **P3-0 → P3-4** citent des CI dans leur prose sans identifiant de run numérique exploitable
   dans la roadmap. Leur **validité est néanmoins établie transitivement** : leurs commits sont ancêtres d'un
   HEAD dont la CI complète est verte.
2. La roadmap (ligne 175) fige P3-3 en **« TERMINÉ localement (sous réserve de CI distante) »** et n'a jamais
   été mise à jour, alors que `c0c97bd` est ancêtre d'un HEAD à CI verte.
3. La roadmap (ligne 721) déclare **P3-12 « non commencé »** et conditionne son entrée à « la CI verte du
   correctif P3-8 — commit et push restent à faire ». **Cette condition est désormais remplie**
   (`4c2a1e5` / run `29958212378` / `success`).
4. Le commit **`d8af704` (refonte de l'écran de connexion)** modifie l'UI pendant P3 sans être rattaché à
   aucune étape ni mentionné par la roadmap (cf. §26).

---

## 9. Audit des frontières d'architecture

### 9.1 Domain

- `MMV.Domain` ne référence **ni** Application, **ni** Infrastructure, **ni** App (vérifié par les 18 tests
  `~Architecture` du Domain et par l'absence de référence projet sortante).
- Aucune dépendance EF Core / SQLite : les politiques Domain (`SalePricingPolicy`,
  `SaleLineStockFlowPolicy`, `SaleLineOpticsPolicy`, `OrderStatusPolicy`, `WorkshopSheetPolicy`,
  `UserIdentityPolicy`, `OpticalTranspositionService`) sont des classes **statiques pures**.
- Aucune règle métier n'exige un `DbContext` : les besoins de persistance atomique passent par des **ports**
  (`IStockMutationService`, `ITransactionRunner`, repositories) déclarés dans
  `src/MMV.Domain/Interfaces/Persistence/`.

### 9.2 Application

- **`dotnet list src/MMV.Application/MMV.Application.csproj reference`** → **une seule** référence :
  `MMV.Domain`. **Preuve directe, pas déclarative.**
- **`dotnet list … package`** → **un seul** paquet : `Microsoft.Extensions.DependencyInjection.Abstractions`.
  Aucun EF Core, aucun SQLite, aucun Avalonia.
- Aucun `DbContext`, aucun SQL brut, aucun ViewModel, aucun service Avalonia. Les 41 tests
  `~Architecture` de `MMV.Application.Tests` opposent ces propriétés.
- Les ports restent **provider-neutres** : les use cases expriment l'atomicité (`ITransactionRunner`,
  décréments conditionnels) sans jamais nommer SQLite.

### 9.3 Infrastructure

- Seule propriétaire d'EF, de SQLite, du SQL brut et des migrations (`src/MMV.Infrastructure/Data/`,
  `Persistence/`, `Migrations/`).
- Aucune règle métier fondamentale cachée : les repositories exécutent des primitives (suppression
  conditionnelle, insertion `ON CONFLICT DO NOTHING`, décrément atomique) dont la **décision** appartient au
  Domain ou à l'Application.
- `PersistenceErrorMapper` reste **technique** : il classe des codes provider (`UniqueConstraint` 2067/1555
  vs `ConstraintViolation` 19) sans énoncer de règle métier.

### 9.4 App (UI)

- **Aucun accès direct au `DbContext` ni à un repository pour une écriture métier** : les 11 tests
  `AppUiPersistenceGuardrailTests` / `OrderViewModelDependencyHygieneTests` sont verts.
- Aucun contournement des use cases P3 n'a été trouvé : `ProductFormViewModel`, `OrderFormViewModel`,
  `SuppliersViewModel` passent tous par des interfaces `I*UseCase`.
- Les règles UI restantes sont **ergonomiques** (activation de bouton, watermark) ou explicitement
  historiques. Les 9 validations optiques locales ont été retirées en P3-3C au profit du Domain.

### 9.5 Violations

| # | Fichier / ligne | Nature | Chemin d'appel | Impact | Classification |
|---|---|---|---|---|---|
| A1 | `src/MMV.App/ViewModels/ProductFormViewModel.cs:669` | La VM transmet encore `StockQuantity` dans `UpdateProductCommand` | VM → `UpdateProductUseCase` | **Nul** : `UpdateProductUseCase.cs:82-115` **exclut explicitement** `StockQuantity` de l'`UPDATE` (P3-5). Le champ subsiste « pour la compatibilité des appelants, mais non persisté » | 🟠 **non bloquant** — piège de périmètre, sans effet |
| A2 | `CreateOrderUseCase.cs:59`, `UpdateOrderUseCase.cs:85` | Classification verre en ligne (`ItemType == LensOd \|\| LensOg`) au lieu d'appeler une politique Domain | VM commande manuelle | Décide quels champs optiques persister — **pas** le flux de stock ; sur un chemin déjà défaillant (§15) | 🟠 **non bloquant** — duplication mineure, non divergente du flux stock |

**Aucune violation bloquante d'architecture.**

---

## 10. Propriétaire unique des règles

Propriétaires réellement présents dans le code (`rg -l 'class …'`) :

| Règle | Propriétaire attendu | Fichier réel | Verdict |
|---|---|---|---|
| Validation métier d'entité | Domain | `src/MMV.Domain/Validators/*` (Customer, Prescription, Product, Supplier, User) | ✅ |
| Validation de commande | Application | `src/MMV.Application/Common/CommandValidation` (réutilise les validateurs Domain) | ✅ |
| Calcul monétaire vente | `SalePricingPolicy` | `src/MMV.Domain/Services/SalePricingPolicy.cs` | ✅ |
| Optiques de ligne | `SaleLineOpticsPolicy` | `src/MMV.Domain/Services/SaleLineOpticsPolicy.cs` | ✅ |
| Flux stock d'une ligne | `SaleLineStockFlowPolicy` | `src/MMV.Domain/Services/SaleLineStockFlowPolicy.cs` | ✅ |
| Transitions commande | `OrderStatusPolicy` | `src/MMV.Domain/Services/OrderStatusPolicy.cs` | ✅ |
| Fiche / QC | `WorkshopSheetPolicy` | `src/MMV.Domain/Services/WorkshopSheetPolicy.cs` | ✅ |
| Transposition optique | service Domain pur | `src/MMV.Domain/Optics/OpticalTranspositionService.cs` | ✅ |
| Normalisation référence produit | Domain (P3-4) | normalisation `Trim().ToUpperInvariant()` + `NormalizedReference` | ✅ |
| Normalisation login | `UserIdentityPolicy` | `src/MMV.Domain/Policies/UserIdentityPolicy.cs` | ✅ |
| Validation mot de passe | `UserValidator.ValidatePasswordPolicy` | `src/MMV.Domain/Validators/UserValidator.cs` | ✅ |
| Persistance atomique | Infrastructure derrière un port | `EfStockMutationService`, `EfTransactionRunner` | ✅ |

### 10.1 Recherche de copies divergentes

- **Calculs monétaires** : aucun recalcul parallèle. `RegisterSaleUseCase.cs:245-256` affecte **exclusivement**
  des valeurs issues de `pricing` (`SalePricingPolicy`) ; le commentaire du code l'énonce (« TOUS les montants
  proviennent de SalePricingPolicy, aucun de la commande »).
- **Matrices de statuts parallèles** : une seule (`OrderStatusPolicy`). La matrice morte d'`OrderService` a été
  supprimée en P3-6 — `rg` ne trouve plus aucun second tableau de transitions.
- **Classification verre/non-verre pour le stock** : **unifiée et vérifiée**. Les trois décisions autrefois
  divergentes interrogent la **même** politique :
  `RegisterSaleUseCase.cs:240` (`hasLenses`), `:314` (lignes versées dans l'`Order`), `:349`
  (saut du décrément), plus la garde `:414` et la garde de fabrication
  `AdvanceOrderStatusUseCase.cs:296-297`. **La revendication de P3-11 résiste à l'inspection du code.**
  Seule subsiste la duplication A2 (§9.5), qui porte sur les **champs optiques persistés**, pas sur le stock.
- **Normalisations utilisateur / produit** : un propriétaire chacune, réutilisé jusque dans le seed
  (`DbInitializer.cs:118` appelle `UserIdentityPolicy.NormalizeUsername`).
- **Validations uniquement en ViewModel sur un chemin d'écriture P3** : aucune trouvée.
- **Mutations directes de stock** : voir §14 — aucune en runtime.

**Aucune duplication réellement divergente. Rien de bloquant.**

---

## 11. P3-1 — validations et `Result`

- La convention `ValidationErrors` / `IsValid` (`src/MMV.Application/Common/ValidationError`) est présente sur
  les `*Result` d'écriture ; les indicateurs `*Found` (`CustomerFound`, `PrescriptionFound`, `OrderFound`…)
  expriment le contrat « introuvable » de façon homogène.
- Les refus **durs** passent par des exceptions typées Domain (`BusinessRuleException`,
  `InvalidOrderStatusTransitionException`), conformément à l'ADR frontières §8.
- **Aucun second framework `Result<T>`** : ni `Ardalis.Result`, ni monade maison — confirmé par la liste des
  paquets de `MMV.Application` (un seul, §4).
- Les validateurs Domain sont **réellement invoqués** : le cas emblématique est `PrescriptionValidator`, code
  mort avant P3-3B, désormais câblé sur Create **et** Update.
- Aucune validation résiduelle **uniquement UI** sur un chemin d'écriture livré pendant P3.

### 11.1 Commandes d'écriture ne suivant pas intégralement la convention

| Use case | Domaine | Raison | Portée | Impact | Verdict |
|---|---|---|---|---|---|
| `CreateOrderUseCase` / `UpdateOrderUseCase` | Commandes | Antérieurs à P3-1, jamais repris ; aucune validation de commande, aucun `ValidationErrors` | Historique (P2B), chemin défaillant par ailleurs (§15) | Nul en pratique : l'écriture échoue sur FK | 🟠 non bloquant |
| `DeletePrescriptionUseCase` | Ordonnances | Contrat `*Found` respecté, mais aucune précondition métier | Actuelle | Voir §13 | 🟡 dette |

P3-1 n'a jamais décidé d'uniformisation massive : son périmètre déclaré était « socle + un domaine pilote
(Clients) ». **Aucune sur-exigence n'est appliquée ici.**

---

## 12. P3-2 — Clients

Vérifié dans le code et par les tests :

- **Archivage / réactivation** : `SetCustomerArchivedUseCase` pose un état **absolu** (jamais un basculement).
- **Exclusion des listes** : `ListCustomersQuery.IncludeArchived`, filtré **en base**.
- **Suppression conditionnelle** : refusée si historique, avec message métier.
- **FK d'historique** : `Restrict` sur ordonnances **et** ventes (migration
  `AddCustomerArchivingAndProtectHistory`) — la base refuse elle-même la perte.
- **Refus d'ordonnance pour client archivé** : `CreatePrescriptionUseCase`, `BusinessRuleException` (P3-3B).
- **Refus de vente pour client archivé** : `RegisterSaleUseCase`, acquis de façon **atomique conditionnelle**
  dans la transaction (P3-7). L'exigence héritée de P3-2B est donc **honorée sur les deux flux**.
- **Lecture fraîche multi-poste** : rechargement depuis la source après toute mutation (P3-2C).

### 12.1 Suppression physique restante

- **États où elle est possible** : uniquement lorsque le client **n'a aucune** ordonnance ni vente. Dès qu'un
  historique existe, la FK `Restrict` **et** la garde applicative refusent.
- **Données perdues / anonymisées** : aucune, par construction — un client sans historique n'a pas de donnée
  historique à perdre. Les comportements P3-0 dénoncés (ordonnances cascade-supprimées, ventes détachées en
  `SetNull`) sont **fermés**.
- **Conformité au rapport final** : oui.
- **Course multi-poste** : la garde ultime est la **contrainte FK de la base**, pas une lecture applicative.
  Un « check-then-act » perdant se solde par un échec de contrainte traduit en erreur métier, jamais par une
  suppression d'historique.

**Aucun constat bloquant.**

---

## 13. P3-3 — Ordonnances

Vérifié :

- Validation **réellement branchée** sur Create **et** Update via `CommandValidation`.
- Règles croisées **cylindre ⇄ axe** et **prisme ⇄ base** (OD/OG), rejet des `NaN`/infinis, normalisation
  `Axis 0 → 180` par fonction Domain pure, ordonnance **partielle acceptée**.
- **Transposition = vue dérivée** : `OpticalTranspositionService` est pur et **n'expose même pas**
  addition/prisme/base/acuité — l'impossibilité de muter la source est **structurelle**, pas seulement
  conventionnelle.
- La fiche atelier ne mute jamais l'ordonnance (snapshot **par valeur** depuis `OrderItem`).

### 13.1 Audit de `DeletePrescriptionUseCase` (inspection directe)

Le fichier `src/MMV.Application/UseCases/Prescriptions/DeletePrescription/DeletePrescriptionUseCase.cs` a été
lu **intégralement**. Son corps exécutable est :

```csharp
var prescription = await _prescriptionRepository.GetByIdAsync(command.PrescriptionId, cancellationToken);
if (prescription is null)
    return new DeletePrescriptionResult { PrescriptionFound = false, … };
await _prescriptionRepository.DeleteAsync(prescription, cancellationToken);
await _unitOfWork.SaveChangesAsync(cancellationToken);
```

**Constat sans complaisance** : la suppression est **physique et inconditionnelle**. Aucune vérification
d'usage, aucun archivage, aucune garde métier. Le use case est **atteignable depuis l'UI**
(`CustomerPrescriptionsViewModel`). P3-3C n'a ajouté qu'une **confirmation**.

- **Absence de FK `SaleItem.PrescriptionId`** — confirmée par `rg 'PrescriptionId'` sur les entités et les
  configurations : la seule occurrence est la clé primaire de `Prescription` elle-même. **Le lien entre une
  vente et l'ordonnance qui l'a motivée est donc irrécupérable**, indépendamment de toute suppression.
- **Ce qui est réellement perdu** : l'enregistrement médical source. **Ce qui ne l'est pas** : les documents
  historiques. `SaleItem` porte ses propres `Sphere` / `Cylinder` / `Axis` (lignes 54-64) ; `OrderItem` et
  `WorkshopSheetItem` de même. La protection est une **copie par valeur**, pas une règle — mais elle est
  effective.
- **Aucune rupture référentielle** : sans FK, la suppression ne casse ni ne détache aucune vente.

> **La confirmation UI n'est pas traitée ici comme une garantie de conservation historique.** Elle ne fait que
> rendre l'acte délibéré.

### 13.2 Décision explicite exigée

**Verdict : 🟡 dette acceptée, explicitement documentée — non bloquante pour le merge P3.**

Justification par le code et la roadmap :

1. La suppression est **délibérée, unitaire et confirmée** : elle n'est **pas une perte silencieuse**, seul
   cas que le §34 érige en blocage.
2. Elle **ne peut pas** être déclenchée par ricochet : la suppression d'un client porteur d'ordonnances est
   refusée par la FK `Restrict` (P3-2B). Il n'existe aucune cascade.
3. Les **documents** dérivés survivent intégralement (copie par valeur, §13.1).
4. La roadmap P3-3 énonce **elle-même** cette limite comme « **Dette documentée, non résolue** » et n'a jamais
   promis l'immutabilité forte : « ni versionnement, ni verrouillage après usage, ni FK
   `SaleItem.PrescriptionId` ». Le critère de sortie n'est donc pas contredit **en catimini** — il l'est
   **ouvertement**, ce qui est le régime que P3 a assumé.

**Conséquence honnête sur le critère « suppression sûre partout » : il est satisfait pour Clients, Produits,
Fournisseurs, Commandes et Utilisateurs, et *partiellement* satisfait pour les Ordonnances.** Ce nuancement
est reporté tel quel dans la matrice §28 — il n'est pas dissous pour obtenir un GO.

---

## 14. P3-4 / P3-5 — Produits, stock et mouvements

### 14.1 Produits (P3-4)

- `NormalizedReference` (`Trim().ToUpperInvariant()`) + **index unique normalisé** ; unicité réelle en base.
- **Backfill « exact ou échec sûr »** : toute référence non-ASCII avorte la migration sans perte ni
  modification, migration non enregistrée.
- Cohérence **catégorie ⇄ détails** ; validation Create/Update branchée ; **fournisseur obligatoire et
  existant** (P3-9 a supprimé le `SupplierId ?? 0`).
- **Suppression protégée** (`BusinessRuleException`) + FK `SaleItem` / `OrderItem` / `StockMovement` en
  **`Restrict`** ; `SetProductActiveUseCase` idempotent ; produits inactifs **refusés** aux nouvelles ventes
  (acquis atomique conditionnel, P3-7) et filtrés des sélecteurs.
- **Écriture directe de `StockQuantity` dans Update produit : supprimée.** Vérifié ligne à ligne dans
  `UpdateProductUseCase.cs:82-115` (« l'édition catalogue NE réécrit PLUS StockQuantity »). Le résidu côté UI
  est le point A1 (§9.5), sans effet.

### 14.2 Stock (P3-5) — classement exhaustif de `rg -n 'StockQuantity\s*(=|\+=|-=|\+\+|--)' src`

| Catégorie | Occurrences | Verdict |
|---|---|---|
| **Seed / démonstration** | `DbSeeder.cs` ×7 ; `DbInitializer.cs` ×6 (init) **et `:868` (`-=`)** | **Seed uniquement**, cf. §14.3 |
| **Migration** | `InitialCreate.cs:140` (`defaultValue: 0`) | Schéma |
| **Projection DTO (lecture)** | `GetInventoryOverview`, `ListProducts`, `GetSaleFormReferenceData`, `CreateStockMovement` (`NewStockQuantity`) | Lecture seule |
| **Création d'entité** | `CreateProductUseCase.cs:105` (stock initial à la création) | Légitime |
| **UI** | `ProductFormViewModel.cs:470/669/702` | Lecture + champ ignoré (A1) |
| **Documentation / commentaires** | `IStockMutationService`, `EfStockMutationService`, `DomainExceptions` | Prose |
| **Mutation runtime** | `EfStockMutationService` (`SET StockQuantity = StockQuantity ± q`, `WHERE StockQuantity = lu`) | **Unique propriétaire** |

**Aucune mutation de stock runtime hors `IStockMutationService`.**

### 14.3 Le cas `DbInitializer.cs:868`

`product.StockQuantity -= quantity;` est un décrément **direct**. Vérification de son atteignabilité :

- Il vit dans `CreateSales`, appelée par `DbInitializer.Initialize`.
- Le **seul** appelant de `Initialize` est `DatabaseSeeder.Seed`, sous la garde `if
  (options.ShouldSeedDemoData)` (`DatabaseSeeder.cs:52-54`).
- En **Production**, cette branche n'est jamais prise : le flux part en `SecureBootstrap`.

**Verdict : mutation de seed de démonstration, hors runtime de production. Non bloquant.** Elle opère en
mémoire avant insertion et s'accompagne des `StockMovement` correspondants (`CreateSalesStockMovements`),
donc sans incohérence de traçabilité dans le jeu de démonstration.

### 14.4 Garanties P3-5 restantes

Convention de signe unique (`In` +q / `Out` −q / `Adjustment` delta) ; refus du négatif ; mouvement tracé pour
chaque mutation ; rollback transactionnel (`EfTransactionRunner`) ; concurrence déterministe (mise à jour
conditionnelle) ; **exactement un** chemin de consommation par ligne acceptée, garanti par
`SaleLineStockFlowPolicy` (jamais zéro, jamais deux) — opposé côté vente **et** côté fabrication.

---

## 15. P3-6 — Commandes

Acquis vérifiés : matrice linéaire stricte unique (`OrderStatusPolicy`) ; sauts, retours arrière, même statut
et sortie de `Delivered` refusés ; transition atomique conditionnelle (CAS) donc rejouable sans double effet ;
suppression limitée à `New` ; modification bloquée à partir de `InProgress` ; notifications non dupliquées.

### 15.1 Audit du défaut ouvert — `CreateOrderUseCase` et `SaleId`

> Le cadrage interdit de reprendre la conclusion de P3-11 sans preuve. Le code a donc été inspecté
> directement, et **la caractérisation de P3-11 s'avère incomplète**.

| # | Question | Réponse **prouvée** |
|---|---|---|
| 1 | Appelé depuis l'UI actuelle ? | **Oui.** `OrderFormViewModel.cs:638` — `await _createOrderUseCase.ExecuteAsync(BuildCreateOrderCommand())`, branche « création » de `SaveAsync`. |
| 2 | Enregistré en DI ? | **Oui.** `src/MMV.Application/DependencyInjection.cs:87` — `services.AddScoped<ICreateOrderUseCase, CreateOrderUseCase>()`. |
| 3 | Atteignable par un utilisateur ? | **Oui.** `OrdersViewModel.cs:173/185` — `_listViewModel.CreateOrderRequested += OnCreateOrderRequested` instancie le formulaire en mode création. **Ce n'est donc pas un chemin mort.** |
| 4 | Une base avec FK activées accepte-t-elle son écriture ? | **Non.** `CreateOrderUseCase.cs:45-52` construit l'`Order` **sans jamais affecter `SaleId`** ; `Order.SaleId` est un `long` **non nullable** (`Order.cs:25`), donc **0**. `OrderConfiguration.cs:43-46` définit `HasOne(o => o.Sale).HasForeignKey(o => o.SaleId)`. Aucune vente n'ayant l'identifiant 0, la FK est violée. |
| 5 | Produit-il une commande introuvable ou orpheline ? | **Non, sous FK actives** : l'écriture **échoue** (échec fermé). L'orphelin ne serait produit que FK désactivées. L'erreur est capturée par `OrderFormViewModel.cs:645` et affichée (`ErrorMessage`) — visible, non silencieuse. |
| 6 | Ses tests désactivent-ils artificiellement les FK ? | **Oui, et le code le dit.** `CreateOrderUseCaseTests.cs:68` : `Data Source={…};Pooling=False;Foreign Keys=False`, avec le commentaire lignes 59-64 : « *le flux de création d'origine crée une commande autonome en laissant `Order.SaleId = 0` … cela viole la clé étrangère Order → Sale … On relâche donc l'enforcement FK* ». |
| 7 | Chemin mort, historique ou public ? | **Public et atteignable, mais fonctionnellement rompu.** Défaut **préexistant** (P2B-2D, iso-fonctionnel), **non introduit par P3**. |
| 8 | Bloque-t-il le merge P3 ? | **Non.** Voir justification ci-dessous. |

**Justification du caractère non bloquant** — quatre éléments, tous vérifiables :

1. Le défaut **précède P3** et P3 ne l'a ni créé ni aggravé ; P3-6 l'a explicitement **reporté** au redesign
   métier (« `SaleId` de la création manuelle → redesign »).
2. Il **échoue fermé** : sous l'enforcement FK par défaut de Microsoft.Data.Sqlite — dont la réalité est
   opposée par la recette (`PRAGMA foreign_keys` attendu à `1`, `CompleteOpticianJourneyAcceptanceTests`) — rien
   n'est écrit. **Aucune perte d'historique, aucune corruption, aucun contournement de règle P3.**
3. Aucune règle métier introduite par P3 n'est franchie : le stock, les statuts et les montants ne sont pas
   atteints, l'écriture n'ayant pas lieu.
4. La garde de fabrication P3-11 (`AdvanceOrderStatusUseCase.cs:296`) neutralise le **danger de stock** d'une
   éventuelle commande hétérodoxe déjà présente en base.

**Correction de la documentation exigée** : P3-11 qualifie ce point de simple « dette ouverte » de chemin
secondaire. C'est **exact quant à l'impact, inexact quant à l'exposition** : le chemin est **public et
atteignable par l'utilisateur**, et la création manuelle de commande fournisseur est de fait **inopérante**.
→ classé **🟠 important non bloquant** (§30) avec correction documentaire (§32).

---

## 16. P3-6B — Fiche atelier et QC

Vérifié : fiche **persistée** (`WorkshopSheet` / `WorkshopSheetItem`) ; **versionnement** avec unicité
`(OrderId, Version)` ; **index unique filtré** garantissant **une seule** version courante ; snapshot **par
valeur** (prisme, usage, acuité récupérés) ; **minimisation client** (nom du porteur seul — absence
*structurelle* du téléphone, de l'e-mail et des montants) ; **empreinte technique déterministe** et
obsolescence ; transposition pure ; **QC atomique et définitif** ; **`Passed` requis avant `Ready`**, la
condition étant portée par l'`UPDATE` lui-même (`EXISTS`/`NOT EXISTS`) ; création de version par
**compare-and-swap** (deux demandes concurrentes ⇒ un succès, un conflit, **jamais une v3**) ; compatibilité
historique **sans faux QC** (commande déjà en `QualityCheck` sans fiche autorisée à avancer, aucune fiche
rétroactive inventée).

### 16.1 Transitions multiples dans le même `DbContext`

- **Portée réelle en production** : faible. L'UI ouvre une portée DI par action utilisateur ; le cas d'une
  seconde transition dans la **même** portée n'apparaît pas dans les flux applicatifs observés.
- **Risque de persistance d'un statut périmé** : la légalité de transition **et** la précondition de fiche
  sont réévaluées **dans l'`UPDATE` conditionnel**, pas depuis l'entité suivie. Une entité en mémoire périmée
  ne peut donc pas faire aboutir une transition illégale.
- **Écriture ultérieure involontaire** : c'est précisément la classe de défaut que P3-9 et P3-10 ont fermée
  par **détachement ciblé** après refus (`ExecuteDeleteAsync`, `UsersRepository.DetachIfTracked`). Le même
  patron n'a pas été généralisé aux transitions de commande.
- **Classification : 🟠 dette de robustesse, non bloquante.** Aucun scénario d'écriture erronée n'a pu être
  construit sur un chemin réel ; les 11 scénarios de recette utilisent une **portée neuve par action**, ce qui
  reflète le comportement de l'UI.

---

## 17. P3-7 — Ventes et paiements

Vérifié dans `RegisterSaleUseCase` :

- **Tous** les montants proviennent de `SalePricingPolicy` (lignes 245-256) ; les totaux fournis par
  l'appelant ne sont **ni lus, ni comparés**.
- `SaleItem.TotalPrice` calculé (jamais fourni) ; données optiques validées par `SaleLineOpticsPolicy`.
- **Client facultatif** (vente au comptoir) mais **archivé refusé** (acquis atomique conditionnel).
- **Produits inexistants ou inactifs refusés** (acquis atomique conditionnel, tri déterministe des prises).
- Vente / stock / commande **atomiques** (`ITransactionRunner`) ; `PaymentStatus` explicite
  (`Pending`/`Partial`/`Paid`), jamais laissé au défaut EF (qui marquait « payée » toute vente à crédit).
- **Règlement atomique et idempotent** : un seul encaissement, **une seule** notification.
- Aucun mot de passe ni détail technique exposé (messages métier stables).

### 17.1 `SaleStatus` — états réellement atteignables

`rg` sur l'ensemble de `src` ne trouve que **trois** écritures :

| Écriture | Emplacement |
|---|---|
| `Draft` | défaut EF (`SaleConfiguration.cs:57`) et initialiseur (`Sale.cs:79`) |
| `AwaitingLenses` / `Delivered` | `RegisterSaleUseCase.cs:261` — **unique** affectation métier |

**`InFabrication`, `Ready` et `Cancelled` ne sont écrits nulle part : ces trois valeurs d'énumération sont
inatteignables.** Aucune synchronisation avec `OrderStatus` n'existe.

- **Relation avec `OrderStatus`** : aucune. Ce sont deux axes indépendants.
- **Vente livrée et payée restant `AwaitingLenses`** : **confirmé par le code**. Une vente avec verres naît
  `AwaitingLenses` et « ce statut ne bougera plus » (commentaire du code, ligne 259). Après livraison de la
  commande et règlement intégral du solde, la vente **reste** `AwaitingLenses`.
- **Effet sur les lectures, écrans et rapports** : tout affichage ou filtrage fondé sur `SaleStatus` est
  **trompeur** pour les ventes avec verres. La vérité opérationnelle est portée par `OrderStatus` et
  `PaymentStatus`, tous deux corrects.
- **Contradiction avec les critères P3 ?** — **Non.** Le critère de sortie est « ventes aux **montants**
  fiables », satisfait. P3-7 a explicitement **refusé** d'introduire une seconde machine à états et a
  documenté la dette. Le seul état **contradictoire** (une vente en attente de verres naissant « livrée ») a
  été corrigé. → **🟡 dette assumée**, conforme au périmètre déclaré.

### 17.2 Vente sans commande — solde non réglable

Constat construit par inspection :

- Une commande fournisseur n'est créée que si `hasLenses` est vrai (`RegisterSaleUseCase.cs:240`).
- `SalePricingPolicy` peut produire `RemainingAmount > 0` avec `PaymentStatus = Partial` **sans** verres
  (vente d'accessoires/monture avec acompte partiel).
- Le résultat est alors : `Sale.RemainingAmount > 0`, `SaleStatus = Delivered`, **aucun `Order`**.
- Le **seul** use case de règlement est `SettleOrderBalanceUseCase`, **indexé sur la commande**
  (`IOrderRepository` en dépendance première). `ls src/MMV.Application/UseCases/Sales/` ne contient que
  `GetCustomerPurchaseHistory`, `GetSaleFormReferenceData`, `RegisterSale` : **il n'existe aucun règlement au
  niveau vente**.

| Question | Réponse |
|---|---|
| Solde positif possible ? | **Oui** |
| Absence d'`OrderId` ? | **Oui** |
| Règlement réellement possible ? | **Non** par l'application |
| Exposition UI | Le solde est enregistré et affichable ; aucune action de règlement n'est offerte |
| Comportement métier attendu | Un encaissement de solde devrait exister au niveau **vente** |
| Caractère | **🟠 important non bloquant** |

**Non bloquant** parce qu'il n'y a **ni perte, ni incohérence, ni contournement** : le montant dû est
correctement calculé, correctement persisté et correctement qualifié `Partial`. C'est une **fonctionnalité
manquante**, pas une règle violée — et P3-7 n'a jamais annoncé le règlement des ventes comptoir (« paiements
multiples / échéanciers » explicitement hors périmètre).

---

## 18. P3-8 — Notifications

Vérifié : séparation orthogonale `IsRead` (vu) / `ResolvedAt` (condition terminée) ; **une seule alerte active
par produit** ; nouvel épisode après réapprovisionnement ; résolution ensembliste (réappro, désactivation,
disparition, changement de seuil) ; compteur réparé ; **N+1 supprimé** (lectures en nombre constant) ;
insertion atomique `ON CONFLICT DO NOTHING` ; **enrôlement transactionnel prouvé** (rollback conjoint du SQL
brut et de la résolution) ; **aucune suppression d'historique** ; adoption de migration stricte.

### 18.1 Reconfirmation physique du correctif final

`InspectActiveLowStockUniqueIndex` (`SqliteDatabaseManager.cs:901-975`) a été **lu intégralement** :

- **`PRAGMA index_list("Notifications")`** — existence par comparaison de nom ; `reader.GetInt32(2)` = drapeau
  **unique** ; `reader.GetInt32(4)` = drapeau **partiel** (lignes 918-931).
- **`PRAGMA index_info(…)`** — colonnes réelles (lignes 940-951).
- **Filtre exact comparé après normalisation ciblée** : `ExtractNormalizedWhereClause` réduit les espaces et
  ne retire qu'une paire de parenthèses **englobant l'intégralité** du prédicat, puis comparaison
  `StringComparison.Ordinal` avec `ExpectedActiveLowStockFilterClause` (lignes 964-972).
- **Validité** = `isUnique && isPartial && 3 colonnes exactes (Type, EntityType, EntityId) && filtre
  identique`.
- **Recherche textuelle de « UNIQUE » : totalement absente.** `rg -n 'Contains\("UNIQUE"|"UNIQUE"'
  src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` → **aucun résultat**. La cause racine de la dette
  (le nom de l'index se terminant lui-même par `_unique`) ne peut plus produire de faux positif.
- **Homonyme non unique refusé** / **historique mensonger refusé** : lignes 301-321 (adoption) et 708-718
  (revérification **inconditionnelle** après préparation), avec refus **avant** toute écriture dans
  `__EFMigrationsHistory`.
- **F1 à F5** : présents et verts — `SqliteDatabaseManagerTests` = **38 tests** (33 + 5), 0 échec (§5.4).

### 18.2 État documentaire du correctif

La roadmap (ligne 721) et le rapport de correction (lignes 434 / 592 / 599) portent encore « **GO LOCAL** »,
« **sous réserve de commit et de CI** », « **Aucun commit. P3-12 non commencé.** ».

**Ces mentions sont désormais périmées** : le correctif est committé (`4c2a1e5`) et sa CI est verte
(`29958212378`, `success`). Conformément au cadrage, cet écart est classé **« documentation à finaliser », et
non défaut runtime** (§32). **La dette P3-8 est techniquement close** ; seule sa consignation reste à mettre à
jour.

---

## 19. P3-9 — Fournisseurs

Vérifié : `SupplierValidator` (nom obligatoire borné à 200 ; longueurs e-mail/téléphone/adresse/code
réellement appliquées — elles ne l'étaient nulle part, `HasMaxLength` ne générant aucune contrainte SQLite) ;
e-mail **facultatif mais validé s'il est renseigné**, sur le motif exact de `CustomerValidator` (aucune regex
maison) ; **normalisation commune** des cinq champs par un propriétaire unique partagé Create/Update ;
**suppression conditionnelle atomique** (`TryDeleteIfUnusedAsync` : condition et écriture dans **la même**
instruction SQL — le « check-then-act » est **fermé**, pas seulement documenté) ; produits **actifs et
inactifs** bloquants ; **diagnostic frais** (`ExistsFreshAsync`) ne décidant jamais de l'écriture ;
**détachement ciblé** après `ExecuteDeleteAsync` (le contournement du change tracker rendait un fournisseur
supprimé « ressuscitable » par un `FindAsync` ultérieur dans la même portée) ; fournisseur **obligatoire et
existant** aux écritures produit (`SupplierId ?? 0` supprimé) ; **FK `RESTRICT`** comme filet ultime, prouvée
jusqu'en SQL brut ; **aucune migration** (verrouillé par garde d'architecture).

**Aucun constat bloquant.**

---

## 20. P3-10 — Utilisateurs

Vérifié : politique de mot de passe appliquée aux **trois** chemins runtime (`CreateUserUseCase`,
`UpdateUserUseCase` si mot de passe fourni, `AuthenticationService.ChangePasswordAsync`), **avant tout
hachage** (prouvé par compteur d'appels BCrypt, pas par l'état final) ; **aucune écriture** pour un mot de
passe faible ; **BCrypt WF11** ; aucun mot de passe en clair persisté ; **login normalisé**
(`UserIdentityPolicy`, colonne `NormalizedUsername`) ; **index unique physique**
`idx_users_normalized_username_unique` vérifié par PRAGMA (`unique = 1`, une colonne métier, `partial = 0`),
l'ancien `idx_users_username_unique` — sans effet sous collation BINARY — étant supprimé ; **collision
historique en échec sûr** (migration avortée, rien inscrit dans `__EFMigrationsHistory`, **aucune ligne
supprimée, fusionnée ni renommée**) ; **rôle obligatoire et validé** (`IsInEnum`), le défaut `Admin` étant
supprimé (`Role` devient `UserRole?` : une omission est **refusée** au lieu de valoir le privilège maximal) ;
**comptes faibles neutralisés en totalité** ; **comptes inactifs non authentifiables** ; anti-énumération
conservée ; **aucun `DeleteUserUseCase`**, FK historiques (`Sale.StaffId`,
`StockMovement.PerformedByUserId`) laissées en `SetNull` — l'historique reste attribué ; **tracker nettoyé**
après collision d'unicité (`DetachIfTransient`/`DetachIfTracked`, patron P3-9).

**Seed Production** — relu intégralement dans `DatabaseSeeder.cs` :

- Le jeu de démonstration n'est appliqué que si `options.ShouldSeedDemoData` (ligne 52) ; **jamais** en
  Production, qui part en `SecureBootstrap`.
- **Toutes** les branches de `SecureBootstrap` neutralisent les comptes faibles (R4), y compris la branche
  « administrateur réel déjà présent » qui, auparavant, n'en neutralisait **aucun**.
- Le renommage vers le login bootstrap n'a lieu que si ce login est **libre**
  (`bootstrapLoginTakenByRealAccount`), ce qui empêche l'échec de démarrage par collision d'unicité.

### 20.1 Autorisation de l'acteur

**Toujours absente** : `MMV.Application` ne possède aucun `ICurrentUser` (aucun n'a été inventé ici). P3-10
valide la **valeur** du rôle, pas le **droit** de le demander.

- **Impact réel sur la sécurité locale** : un appelant entrant par la couche Application peut créer ou
  modifier un utilisateur de n'importe quel rôle. En **application desktop mono-processus**, la surface
  d'attaque est un opérateur ayant déjà accès à la machine et à la base.
- **Caractère** : **🟡 report explicite**, verrouillé par garde d'architecture et annoncé comme tel par P3-10.
  **Non bloquant** pour P3, dont le périmètre déclaré est « durcir la sécurité locale minimale », hors
  « rôles fins » et « audit trail complet ». À reprendre lorsqu'un acteur authentifié sera propagé jusqu'à
  l'Application.

---

## 21. P3-11 — Scénarios de recette

Les **11** tests `Acceptance` sont verts (§5.1). Vérifié dans le socle
(`tests/MMV.Application.Tests/Acceptance/`) :

- **base SQLite migrée indépendante par test** ; **FK actives** — opposées explicitement :
  `PRAGMA foreign_keys` lu et asserté à `1` (« *les scénarios de recette opposent l'intégrité référentielle
  réelle* ») ;
- **seed Production sûr** (aucune donnée de démonstration) ; **aucune mutation métier directe par
  `DbContext`** (mutations par use cases uniquement) ; **portée fraîche par action utilisateur** ; assertions
  par contexte neuf `AsNoTracking()` ;
- couverture **S1** (parcours nominal) → **S9**, plus une vérification de santé du socle : S2 vente sans
  client, S3 client archivé, S4 rollback fabrication, S5 QC, S6 rejeu de transition, S7 rejeu de règlement,
  S8 désactivation/historique, **S9 = deux refus de classification** ;
- **aucune séquence de numérotation consommée sur refus** ; au nominal, **3 mouvements de stock** et
  **6 notifications**.

### 21.1 Limites détectées par la recette et encore ouvertes

| Limite | Ouverte ? | Bloque P3 ? |
|---|---|---|
| Classification `ItemType` ⇄ `Category` | **Fermée** par `SaleLineStockFlowPolicy` (garde vente + garde fabrication) | Non |
| `CreateOrderUseCase` / `UpdateOrderUseCase` (chemins secondaires) | **Ouverte** — §15.1 | **Non** (échec fermé, danger de stock neutralisé) |
| `SaleStatus` non synchronisée | **Ouverte** — §17.1 | **Non** (dette assumée, hors périmètre déclaré) |

---

## 22. Persistance et migrations

### 22.1 Registre des migrations P3

`git diff --name-status origin/main...HEAD -- src/MMV.Infrastructure/Migrations/` : **5 fichiers en `A`**,
**0 en `M`** (hors snapshot).

| Migration | Étape | Additive / destructive | Backfill | `Down` | Adoption | Tests |
|---|---|---|---|---|---|---|
| `AddCustomerArchivingAndProtectHistory` | P3-2B | Additive (colonne `IsArchived`) + **durcissement FK** (`Cascade`/`SetNull` → `Restrict`) | non | oui | stricte | `CustomerArchivingMigrationTests` |
| `AddProductNormalizedReferenceAndProtectHistory` | P3-4B | Additive + index unique + FK → `Restrict` | **exact ou échec sûr** (non-ASCII ⇒ avortement sans perte) | oui | stricte | `ProductNormalizedReferenceMigrationTests` |
| `AddWorkshopSheets` | P3-6B | **Purement additive** (tables) | **aucun** | oui | tables additives tolérées + migration exécutée | `WorkshopSheetsMigrationTests`, `WorkshopSheetsAdoptionTests` |
| `AddNotificationResolution` | P3-8 | Additive (colonne nullable) + dédoublonnage déterministe + **index filtré** | dédoublonnage **sans aucune suppression de ligne** | oui | stricte, **vérifiée physiquement** (PRAGMA) | `NotificationResolutionMigrationTests`, `SqliteDatabaseManagerTests` (38) |
| `AddNormalizedUsernameAndSecureLocalUsers` | P3-10 | Additive (colonne) + index unique + suppression de l'ancien index sans effet | **exact ou échec sûr** (collision/vide/hors bornes/non-ASCII ⇒ avortement) | oui, **réversible** | stricte, revérifiée **inconditionnellement** | `NormalizedUsernameMigrationTests` |

### 22.2 Contrôles transverses

| Contrôle | Résultat |
|---|---|
| Aucune ancienne migration modifiée après son commit | ✅ — **aucun `M`** sur un fichier de migration existant |
| Snapshot aligné | ✅ — `has-pending-model-changes` : « No changes » |
| Ordre des migrations | ✅ — horodatages strictement croissants (`20260713…` → `20260721…`) |
| Base neuve migrable jusqu'à HEAD | ✅ — chaque test de migration/adoption construit une base **jetable réelle** et applique la chaîne (155 + 9 tests verts) |
| Adoptions historiques strictes | ✅ — états **partiels ou altérés refusés avant toute écriture** d'historique |
| Migration non baselinée si ses effets physiques manquent | ✅ — `SqliteDatabaseManager.cs:301-321` (refus) et `:708-725` (revérification inconditionnelle) |
| Aucun schéma mensonger accepté | ✅ — homonyme non unique et historique mensonger refusés (§18.1) |
| Defaults transitoires absents du schéma final | ✅ — P3-10 : le `''` exigé par `ALTER TABLE ADD COLUMN NOT NULL` est retiré par un `AlterColumn` final ; `dflt_value` final = `NULL`, prouvé par PRAGMA **et** par insertion brute |
| Index uniques physiquement vrais | ✅ — lus par PRAGMA `index_list` (`unique`, `partial`) et `index_info`, jamais par recherche textuelle |
| Filtres exacts | ✅ — comparaison ordinale après normalisation ciblée |

**Aucune base utilisateur n'a été modifiée** : tous les contrôles s'appuient sur les tests existants et sur la
lecture du code.

---

## 23. Sécurité et données sensibles

`rg -n 'password|Password|admin|secret|PasswordHash|BootstrapAdmin' src tests docs --glob '!**/bin/**' --glob
'!**/obj/**'` — classement (aucun secret n'est reproduit en clair ici) :

| Classe | Éléments | Verdict |
|---|---|---|
| **Secret runtime réel** | **Aucun.** Le mot de passe bootstrap provient d'une **configuration externe** (`SeedOptions.BootstrapAdminPassword`) ; `SeedResult` **n'expose jamais** de secret | ✅ |
| **Hash historique** | Deux hachages BCrypt de comptes par défaut faibles, en constantes dans `DatabaseSeeder.KnownDefaultAdminPasswordHashes`, et dans `InitialCreate` / `AddCounterSaleFieldsToOrder` (`InsertData`) | ⚠️ **connu et traité** : ils servent précisément à **détecter et neutraliser** ces comptes. Un hash BCrypt n'est pas un secret exploitable, et les comptes correspondants sont désactivés en Production |
| **Login par défaut** | `SeedOptionsResolver.DefaultBootstrapAdminUsername = "admin"` | Identifiant, non secret. Le compte n'est utilisable **que** si un mot de passe fort est fourni |
| **Donnée de test / démonstration** | `DbInitializer` (mot de passe « admin » commenté), jeux de clients/produits/ordonnances fictifs | Jamais appliqué en Production (§20) |
| **Documentation** | Rapports P3 décrivant la politique | ✅ |
| **Faux positif** | `RevealPassword="{Binding …}"` / `"False"` (Avalonia), `Password = "a"` cité **dans un commentaire** de `CreateUserUseCase` illustrant le défaut corrigé | ✅ |

Autres contrôles :

- **Données personnelles** : minimisation **structurelle** sur la fiche atelier (nom du porteur seul).
- **Données d'ordonnance** : jamais exposées hors des agrégats qui les portent légitimement.
- **Logs et exceptions** : les erreurs provider sont traduites en **messages métier stables** (P3-9 : plus de
  « *An error occurred while saving the entity changes.* » ; P3-10 : `UsernameTaken`). Aucune exception EF /
  SQLite brute n'atteint l'appelant sur les chemins durcis par P3.
- **Paquets vulnérables** : **0** sur les 7 projets (§4).
- **Fichiers d'identifiants** : aucun fichier de secrets committé.

**Aucun constat de sécurité bloquant.**

---

## 24. *(fusionné)* — voir §22 et §23

---

## 25. Dettes et chemins morts

`rg -n 'TODO|FIXME|HACK|NotImplementedException|NotSupportedException' src tests` — classement complet :

| Élément | Emplacement | Classe |
|---|---|---|
| `NotImplementedException` / `NotSupportedException` dans 8 converters Avalonia | `src/MMV.App/Converters/*` | **Faux positif** — `ConvertBack` non utilisé (usage unidirectionnel) |
| `NotSupportedException` dans les doubles de test | `CreateStockMovementUseCaseTests`, `SettleOrderBalanceUseCaseTests` | **Faux positif** — membres délibérément non sollicités |
| `TODO: Mettre à jour les seeds après la migration Sales/Orders` | `DbInitializer.cs:823` — `CreateOrders` renvoie une liste **vide** | **Dette connue**, seed de démonstration uniquement |
| `TODO P2C` ×4 | `AppUiPersistenceGuardrailTests.cs:47/71/86/99` | **Dette connue** — compteurs de garde-fous à réduire, hérités de P2C, **hors P3** |
| `TODO: Ouvrir le formulaire de nouvelle ordonnance` | `CustomersView.axaml.cs:67` | **Hors P3** — reporté au redesign UI |
| `TODO: Charger les données depuis les services` | `DashboardViewModel.cs:72` | **Hors P3** — redesign UI |
| `TODO: Implement export functionality` | `StockMovementsListViewModel.cs:285` | **Hors P3** |
| `TODO: Implémenter la gestion des catégories` | `ProductsListViewModel.cs:411` | **Hors P3** |

Autres recherches :

- **Services métier morts** : les deux principaux (`PrescriptionService`, matrice de statuts d'`OrderService`)
  ont été **supprimés** en P3-3B et P3-6.
- **Use cases sans appelant** : aucun trouvé ; `CreateOrderUseCase` **a** un appelant (§15.1).
- **Valeurs d'énumération inatteignables** : `SaleStatus.InFabrication`, `.Ready`, `.Cancelled` (§17.1) →
  **dette assumée**.
- **Propriétés jamais alimentées** : `UpdateProductCommand.StockQuantity` (conservée pour compatibilité,
  volontairement non persistée — §9.5 A1).
- **Dépendances optionnelles qui devraient être obligatoires** :
  `SettleOrderBalanceUseCase(INotificationRepository? notificationRepository = null)` — la notification de
  règlement est silencieusement omise si le port n'est pas injecté. En DI réelle il **est** enregistré. →
  **🟠 important non bloquant** (fragilité de conception, sans effet observé).

**Aucune correction n'a été ouverte.**

---

## 26. UI et frontière du redesign

> Le cadrage interdit explicitement de conclure « aucune UI modifiée pendant P3 ». Formulation exacte
> ci-dessous, corrigée par une découverte du dépôt.

- **L'UI a été modifiée volontairement pendant P3-2C et P3-3C**, avant la décision « backend uniquement à
  partir de P3-4 » — décision qui ne s'applique pas rétroactivement. P3-2C : confirmation avant suppression
  physique, refus métier affiché en clair, boutons **Archiver / Réactiver** (état absolu), filtre
  « Afficher les clients archivés », rechargement depuis la source. P3-3C : `PrismBase` en `ComboBox`,
  watermark erroné supprimé, affichage des `ValidationErrors`, capture spécifique de la
  `BusinessRuleException`, `*Found = false` traités, confirmation avant suppression, `DoctorName` aligné,
  **9 validations locales supprimées**.
- **Le backend-only est appliqué à partir de P3-4** — et **prouvé matériellement** :
  `git diff --name-only c246ebc~1..HEAD -- src/MMV.App/` retourne **une sortie vide**. **Aucun fichier de
  `src/MMV.App` n'a été modifié depuis P3-4A.** `MMV.App.Tests` reste **exactement à 239** sur toute cette
  période.
- **Aucune modification UI dans les étapes backend suivantes** (P3-4 → correctif P3-8).

### 26.1 Découverte — un troisième changement UI pendant P3

Le commit **`d8af704` — « feat(ui): redesign login screen »** se situe **entre P3-3C (`c0c97bd`) et P3-4A
(`2709bfe`)** et modifie 6 fichiers UI (`LoginView.axaml` +432/−145, `App.axaml.cs`, `Icons.axaml`,
`MMV.App.csproj`, un asset).

- Il **ne contredit pas** la règle « backend-only à partir de P3-4 » : il **précède** P3-4.
- Il n'est rattaché à **aucune étape P3**, ne fait l'objet d'**aucun rapport** et n'est **pas mentionné par la
  roadmap**.
- **Impact métier : nul** — écran de connexion uniquement, aucune règle métier déplacée.
- → **🟠 non bloquant**, mais **correction documentaire requise** (§32) : l'affirmation implicite « l'UI n'a
  été touchée que par P3-2C et P3-3C » est **inexacte**.

### 26.2 Règles métier critiques résidant uniquement dans l'UI

**Aucune.** P3-3C a supprimé les 9 dernières validations locales ; le Domain est propriétaire.

### 26.3 Reports vers le redesign

UI Produits (P3-4C jamais créée), impression / PDF de la fiche atelier, écrans Commandes et Ventes, dashboard,
export des mouvements de stock, gestion des catégories, formulaire d'ordonnance depuis la fiche client.

---

## 27. Multi-poste et production

### 27.1 Matrice des écritures sensibles

| Flux | Protection | Vérifié |
|---|---|---|
| Stock | Décrément **conditionnel** atomique (`SET … WHERE StockQuantity = lu`) | `EfStockMutationService` |
| Vente | Transaction unique (`ITransactionRunner`), prises produit/client atomiques et déterministes | `RegisterSaleUseCase` |
| Commande | **CAS de statut** (prise atomique conditionnelle, idempotente) | `AdvanceOrderStatusUseCase` |
| Fiche | Version + précondition + **unicité `(OrderId, Version)`** + index unique filtré ; création par compare-and-swap | `WorkshopSheet*` |
| QC | Décision **atomique et définitive**, condition portée par l'`UPDATE` | P3-6B §24bis |
| Paiement | Prise atomique idempotente (un seul encaissement, une seule notification) | `SettleOrderBalanceUseCase` |
| Notification | **Index unique filtré** + insertion `ON CONFLICT DO NOTHING`, enrôlée transactionnellement | P3-8 |
| Fournisseur | **Suppression conditionnelle** : condition et écriture dans la même instruction SQL | `TryDeleteIfUnusedAsync` |
| Utilisateur | **Index unique physique** + traduction de course en `UsernameTaken` + détachement ciblé | P3-10 |

**Les ports ne dépendent pas de SQLite** : `MMV.Application` ne référence que `MMV.Domain` (§4) ; les
interfaces de persistance sont déclarées en Domain et n'expriment que des **garanties** (atomicité,
conditionnalité), jamais un dialecte.

### 27.2 Les deux notions, rigoureusement séparées

**P3 merge-ready — OUI.**
Règles métier cohérentes et opposées ; interfaces provider-neutres ; atomicité **exprimée** dans les ports et
opposée par des tests sur **vrai SQLite** déterministes (sans `Thread.Sleep`, par états injectés) ;
documentation abondante et — après les corrections du §32 — cohérente.

**V1 production multi-poste ready — NON.**
Manquent encore, aucun n'étant du ressort de P3 : le **choix final** PostgreSQL ou SQL Server Express (P3-0B
l'a explicitement différé à un spike) ; l'implémentation du **provider serveur** ; les migrations/SQL
spécifiques au provider ; des **tests système sur base serveur** ; le déploiement et les sauvegardes ; la
validation de la **concurrence réelle multi-processus** (aujourd'hui opposée en SQLite mono-processus).

L'absence du provider serveur **ne bloque pas le merge P3** (report acté par ADR-PROD-DB-001), mais **interdit
formellement** de déclarer le produit déployable en production multi-poste.

---

## 28. Matrice des critères de sortie P3

Critères repris **littéralement** de la roadmap §6, sans adaptation.

| Critère officiel | Preuve code | Preuve test | Preuve doc | Verdict |
|---|---|---|---|---|
| Règles métier par domaine introduites | 7 politiques Domain + validateurs + gardes de use cases | 1493 tests | 15 rapports P3 | ✅ |
| Testées | — | 1493 verts, dont 11 de recette, 70 d'architecture, 164 de migration/schéma | §5 | ✅ |
| Documentées | — | — | roadmap + 28 fichiers `docs/implementation` | ✅ *(sous réserve §32)* |
| **Suppression sûre partout** | Clients (`Restrict` + garde), Produits (`Restrict` + garde), Fournisseurs (atomique), Commandes (`New` seul), Utilisateurs (aucune suppression) — **mais** `DeletePrescriptionUseCase` supprime physiquement sans condition | tests de refus pour chaque domaine ci-dessus ; **aucun** pour l'ordonnance | P3-3 qualifie la limite de « dette documentée, non résolue » | ⚠️ **partiellement satisfait** — voir §13.2 |
| Politique de stock unique | `IStockMutationService` seul propriétaire runtime ; aucune mutation directe (§14.2) | tests P3-5 + S1/S4/S9 | rapport P3-5 | ✅ |
| Workflow commande explicite | `OrderStatusPolicy`, matrice linéaire unique, seconde matrice supprimée | matrice de transitions + S6 | rapport P3-6 | ✅ |
| Fiche atelier versionnée avec QC | `WorkshopSheet` persistée, versionnée, QC bloquant avant `Ready` | tests P3-6B + S5 | rapport P3-6B | ✅ |
| Ventes aux montants fiables | `SalePricingPolicy` seul propriétaire, montants appelant ignorés | tests P3-7 + S1/S2/S7 | rapport P3-7 | ✅ |
| `MMV.Application` pure | `dotnet list … reference` → Domain seul | 41 tests d'architecture | ADR frontières | ✅ |
| Build / tests verts (> total P3-0) | 0 erreur / 0 avertissement | **1493 > 585** | §4 | ✅ |
| 0 vulnérabilité | — | 7 projets « no vulnerable packages » | §4 | ✅ |
| `has-pending-model-changes = false` (ou migrations assumées) | « No changes … » | 164 tests migration/schéma | 5 migrations documentées | ✅ |

### 28.1 Analyse explicite de la contradiction

Un seul critère officiel entre en tension avec une dette antérieurement « acceptée » : **« suppression sûre
partout »** contre la **suppression physique d'ordonnance** (§13).

Analyse, sans arrangement :

- La contradiction est **réelle** : le mot « partout » n'admet pas d'exception, et il en existe une.
- Elle est cependant **antérieurement et publiquement assumée** : la roadmap P3-3 ne se contente pas de la
  taire, elle l'énonce comme une **réserve honnête** sur la « source intangible » et la déclare **non
  résolue**. P3 n'a jamais prétendu la fermer.
- Son **impact réel est borné** : acte délibéré et confirmé, aucune cascade possible, aucun document
  historique perdu, aucune rupture référentielle (§13.1).
- Elle **n'est pas** une « perte silencieuse d'historique », seule catégorie que le §34 érige en blocage.

**Conclusion : le critère est classé « partiellement satisfait », et non « satisfait ».** Il ne fait pas
basculer le verdict en NO-GO, mais il est reporté **tel quel** — non neutralisé — et constitue une **entrée
prioritaire** du futur cadrage (§31).

---

## 29. Constats bloquants (🔴)

**Aucun.**

Chaque catégorie du §28 du cadrage a été recherchée activement et **aucune n'est prouvée** :

| Catégorie de blocage | Résultat |
|---|---|
| Règle P3 contournable par un chemin public | **Non prouvé.** Les gardes vente/fabrication reposent sur la catégorie **chargée en base** ; le chemin manuel des commandes échoue avant toute écriture (§15.1) |
| Perte silencieuse d'historique dans le périmètre P3 | **Non prouvée.** Les FK `Restrict` (clients, produits, fournisseurs), l'absence de `DeleteUserUseCase`, les migrations « exact ou échec sûr » et l'absence de suppression de notification l'excluent. La suppression d'ordonnance est **explicite et confirmée**, pas silencieuse (§13) |
| Écriture stock incohérente | **Non prouvée.** Une seule voie runtime, atomique et conditionnelle (§14) |
| Migration / adoption mensongère | **Non prouvée.** Vérification physique par PRAGMA, refus avant écriture d'historique, revérification inconditionnelle (§18.1, §22.2) |
| Build / test / architecture / sécurité en échec | **Non.** 0/0, 1493/0/0, 70 tests d'architecture verts, 0 vulnérabilité |
| Conflit avec `main` empêchant le merge | **Non.** 0 commit divergent, `merge-tree` exit 0 sans conflit (§3.1) |
| Critère officiel P3 non satisfait | **Un critère partiellement satisfait** (« suppression sûre partout »), analysé en §28.1 — assumé, borné, non silencieux |

---

## 30. Constats non bloquants

### 🟠 Important non bloquant

| # | Constat | Preuve | Impact |
|---|---|---|---|
| O1 | **`CreateOrderUseCase` laisse `Order.SaleId = 0`** sur un chemin **public et atteignable** ; la création manuelle de commande fournisseur est de fait inopérante sous FK actives | `CreateOrderUseCase.cs:45-52`, `Order.cs:25`, `OrderConfiguration.cs:43-46`, `OrderFormViewModel.cs:638`, `OrdersViewModel.cs:185`, `DependencyInjection.cs:87`, `CreateOrderUseCaseTests.cs:59-68` | Échec fermé, message affiché. Aucune perte. **Caractérisation P3-11 à corriger** |
| O2 | **Solde de vente sans commande non réglable** | §17.2 ; `ls src/MMV.Application/UseCases/Sales/` | Fonctionnalité manquante ; montants corrects |
| O3 | `INotificationRepository?` optionnel dans `SettleOrderBalanceUseCase` | `SettleOrderBalanceUseCase.cs:58-65` | Notification silencieusement omise si non injecté ; injecté en DI réelle |
| O4 | **Commit UI `d8af704` non documenté** dans la roadmap P3 | §26.1 | Documentaire |
| O5 | Duplication mineure de la classification verre dans `Create`/`UpdateOrderUseCase` | `:59` / `:85` | Champs optiques persistés ; sur chemin déjà rompu (O1) |
| O6 | `UpdateProductCommand.StockQuantity` conservée mais jamais persistée | `UpdateProductUseCase.cs:82-115` | Piège de périmètre sans effet |
| O7 | Transitions multiples dans le même `DbContext` : détachement ciblé non généralisé | §16.1 | Dette de robustesse, aucun scénario réel construit |

### 🟡 Dette assumée

`SaleStatus` (3 valeurs inatteignables, jamais synchronisée — §17.1) · double rôle d'`Order` (fournisseur
*et* atelier) · **attribution utilisateur / autorisation par acteur** (aucun `ICurrentUser` — §20.1) ·
**horloge** (mélange `DateTime.Now` / `UtcNow`, hors P3 sauf décision) · **lien `Prescription` ↔ `Sale`**
(aucune FK, lien passé irrécupérable — §13.1) · **suppression physique d'ordonnance** (§13.2) · provider
serveur (§27.2) · ergonomie UI (§26.3) · `TODO P2C` des garde-fous UI · seed `CreateOrders` vide.

### 🔵 Hors P3

SaaS · multi-tenant · `Organization`/`Store` · facturation légale · TVA · devis · paiement en ligne ·
multi-magasin · cloud · notifications push/email · signature électronique.

---

## 31. Entrées factuelles pour le futur cadrage P4

> Section **factuelle** uniquement. Aucune étape n'est nommée ou numérotée, aucun ordre n'est fixé, aucune
> fonctionnalité n'est annoncée, et **aucun choix de SGBD n'est arrêté**.

**État technique final de P3.** Branche `p3-business-rules` au commit `4c2a1e5`, CI verte
(`29958212378`). 1493 tests (Domain 647 · Application 607 · App 239), 0 échec, 0 ignoré, build 0/0, 0
vulnérabilité, modèle EF aligné, `MMV.Application` pure. 5 migrations P3, toutes additives ou protectrices,
avec adoption stricte des bases historiques. Fast-forward possible sur `origin/main`, sans conflit.

**Dettes ouvertes.** `SaleStatus` sans machine à états (3 valeurs inatteignables) · double rôle d'`Order` ·
suppression physique d'ordonnance sans garde ni archivage · absence de FK `Prescription` ↔ `Sale` ·
immutabilité forte et concurrence des ordonnances (deux postes corrigeant des yeux différents : la première
correction est écrasée en silence) · création manuelle de commande inopérante (`SaleId = 0`) · solde de vente
sans commande non réglable · notification optionnelle du règlement · horloge non injectable · compteurs de
garde-fous UI hérités de P2C.

**Choix non réalisés.** Provider serveur (PostgreSQL ou SQL Server Express) non tranché ni implémenté ·
autorisation par acteur (aucun `ICurrentUser` en Application) · mesures porteur, cotes de montage et points de
contrôle séparés de la fiche atelier · réconciliation `SaleStatus` ⇄ `OrderStatus` · séparation formelle
commande fournisseur / ordre de fabrication · statut `Cancelled` et archivage des commandes.

**Décisions ADR encore à implémenter.** ADR-PROD-DB-001 : base client/serveur pour la production
multi-poste — **acceptée, non implémentée**, choix final différé à un spike. ADR-005 (horloge injectable) :
évoquée en P2, jamais ouverte.

**Redesign UI reporté.** Écrans Produits (P3-4C jamais créée), Commandes, Ventes, dashboard · impression et
export PDF de la fiche atelier · export des mouvements de stock · gestion des catégories · formulaire
d'ordonnance depuis la fiche client. L'UI n'a pas été touchée depuis P3-4A (preuve §26).

**Risques nécessitant un spike.** Comportement réel de la concurrence multi-processus sur base serveur (les
garanties actuelles sont opposées en SQLite mono-processus) · portabilité des primitives SQL spécifiques
(`ON CONFLICT DO NOTHING`, index filtrés, `ExecuteDelete` conditionnel, PRAGMA de vérification physique)
vers un provider serveur · stratégie de migration des bases SQLite existantes vers le serveur · sauvegardes
et déploiement multi-poste.

**Dépendances entre ces sujets.** Le choix du provider conditionne la portabilité des primitives atomiques et
la stratégie de migration, donc tout test système multi-poste. L'autorisation par acteur conditionne
l'attribution de l'auteur du QC et un éventuel audit trail. La réconciliation `SaleStatus` ⇄ `OrderStatus`
dépend de la clarification du double rôle d'`Order`. Une FK `Prescription` ↔ `Sale` conditionne toute règle
d'immutabilité forte et rendrait la suppression d'ordonnance protégeable par le même patron `Restrict` que
les autres domaines. Le redesign UI dépend des contrats de use cases stabilisés par P3.

---

## 32. Corrections documentaires nécessaires

**Aucun document existant n'a été modifié pendant cette exécution.**

### 32.1 Correction obligatoire avant le commit P3-12

| # | Fichier | Ligne | Énoncé actuel | Réalité |
|---|---|---|---|---|
| C1 | `docs/architecture/P3-business-rules-roadmap.md` | 721 | « **P3-12 — Statut : non commencé.** Entrée conditionnée par la CI verte du correctif P3-8 — commit et push restent à faire » | Condition **remplie** : `4c2a1e5`, run `29958212378`, `success` |
| C2 | `docs/architecture/P3-business-rules-roadmap.md` | ~697-700 | Dette P3-8 « corrigée … **sous réserve de commit et de CI** » | Correctif committé et CI verte ⇒ **dette définitivement close** |
| C3 | `docs/architecture/P3-business-rules-roadmap.md` | 175 | « P3-3 **TERMINÉ localement** (sous réserve de CI distante) » | `c0c97bd` est ancêtre d'un HEAD à CI verte |
| C4 | `docs/architecture/P3-business-rules-roadmap.md` | §2 / §3 | Le commit UI `d8af704` (refonte de l'écran de connexion) n'apparaît nulle part | Changement UI réel pendant P3, entre P3-3C et P3-4A (§26.1) |
| C5 | `docs/architecture/P3-business-rules-roadmap.md` | 663-700 (P3-11) | `CreateOrderUseCase` présenté comme « chemin secondaire », dette ouverte | Chemin **public, enregistré en DI et atteignable par l'utilisateur** ; impact correctement décrit, **exposition sous-estimée** (§15.1) |
| C6 | roadmap §6 (critères de sortie) | — | « suppression sûre **partout** » présentée comme atteignable en l'état | **Partiellement satisfait** : exception ordonnances (§28.1) |

### 32.2 Amélioration facultative

| # | Objet |
|---|---|
| F1 | Consigner les identifiants de run CI numériques pour P3-0 → P3-4, aujourd'hui décrits en prose |
| F2 | Harmoniser les verdicts « GO LOCAL » des rapports P3-4B, P3-5, P3-6, P3-7, P3-8, P3-9, P3-10 en « GO (CI confirmée) », la CI étant vérifiée pour tous (§8.2) |
| F3 | Mentionner explicitement dans P3-7 que `SaleStatus.InFabrication`, `.Ready` et `.Cancelled` sont **inatteignables** (aujourd'hui implicite) |
| F4 | Documenter le solde non réglable des ventes sans commande (§17.2), absent de tous les rapports |

### 32.3 Historique volontaire à conserver

| # | Objet |
|---|---|
| H1 | Le verdict `P3-11 = NO-GO LOCAL` du premier cycle — trace d'un arbitrage réel, explicitement « conservé pour mémoire » |
| H2 | Les mentions « GO LOCAL » / « sous réserve de CI » **dans le corps** des rapports d'étape : elles décrivent fidèlement l'état au moment de leur rédaction. Seules les **synthèses de la roadmap** doivent refléter l'état final |
| H3 | Le rapport de correction P3-8 (« Aucun commit. P3-12 non commencé. STOP. ») : instantané exact de son exécution |

---

## 33. Merge readiness

Les trois questions sont traitées **séparément** et ne doivent jamais être confondues.

### 33.1 Le code P3 est-il métier/techniquement mergeable ?

**OUI.**

Preuves : build 0 erreur / 0 avertissement ; **1493 tests verts, 0 échec, 0 ignoré** ; 0 vulnérabilité sur 7
projets ; modèle EF aligné ; `MMV.Application` **pure** (référence Domain unique, prouvée par commande) ;
70 tests d'architecture verts ; 164 tests de migration/schéma verts ; **11 scénarios de recette** verts sur
bases migrées à FK actives ; règles P3 réellement opposées et à propriétaire unique (§10) ; **aucun constat
bloquant** (§29).

### 33.2 La branche est-elle immédiatement fusionnable dans `origin/main` ?

**OUI.**

`git rev-list --left-right --count origin/main...HEAD` → **`0  28`** : `main` n'a aucun commit exclusif, la
base de fusion **est** `origin/main`. `git merge-tree --write-tree origin/main HEAD` sort en **code 0** avec
un arbre écrit et **aucun conflit**. Le merge est un **fast-forward**, sans mise à jour préalable de la
branche.

### 33.3 Le produit est-il prêt pour une production V1 multi-poste ?

**NON.**

Le provider serveur exigé par ADR-PROD-DB-001 n'est **ni choisi ni implémenté** (spike différé) ; il manque
les migrations/SQL du provider, les tests système sur base serveur, la validation de la concurrence réelle
multi-processus, ainsi que le déploiement et les sauvegardes (§27.2). Les garanties d'atomicité sont
**exprimées de façon provider-neutre** et opposées en SQLite — ce qui rend le code **portable**, non
**déployé**.

### 33.4 Actions entre ce verdict d'audit et un merge réel

1. **Corrections documentaires C1 → C6** (§32.1) — obligatoires : roadmap P3 uniquement, aucun code.
2. *(facultatif)* F1 → F4 (§32.2).
3. **Commit P3-12** : le présent rapport **plus** les corrections de roadmap. Aucun fichier sous `src/**`,
   `tests/**`, `src/MMV.Infrastructure/Migrations/**`, `docs/architecture/**` *(hors la roadmap elle-même)*
   ni `docs/domain/**`.
4. **CI P3-12** : attendre `conclusion: success` sur le SHA du commit P3-12.
5. **Mise à jour de branche** : **non requise** (§33.2) — à revérifier si `origin/main` avance entre-temps.
6. **Merge** vers `main` (fast-forward), puis clôture de P3.

Les dossiers non suivis `design-handoff/`, `design/` et `docs/ui/` **ne doivent pas** être inclus dans le
commit P3-12 : ils relèvent du redesign, hors périmètre P3.

---

## 34. Validation du périmètre de cette exécution

```
git status --short   → ?? design-handoff/   ?? design/   ?? docs/ui/   ?? docs/implementation/P3-12-final-business-audit-report.md
git diff --check     → (vide)
git diff --stat      → (vide — aucun fichier suivi modifié)
git diff --name-status → (vide)
```

- **Aucun fichier existant modifié.**
- **Aucun fichier** sous `src/**`, `tests/**`, `src/MMV.Infrastructure/Migrations/**`, `docs/architecture/**`
  ou `docs/domain/**` touché.
- Seul nouveau fichier P3-12 : **`docs/implementation/P3-12-final-business-audit-report.md`**.
- Autres éléments non suivis, tous préexistants et autorisés : `design-handoff/`, `design/`, `docs/ui/`.
- Aucun commit, push, merge, rebase, cherry-pick. Aucune correction ouverte. Aucune roadmap P4 définie.

---

## 35. Verdict

Toutes les conditions énumérées au §34 du cadrage ont été vérifiées **par exécution**, non par lecture des
rapports antérieurs :

| Condition | État |
|---|---|
| Git et CI de départ conformes | ✅ HEAD exact local+distant ; run `29958212378` `success` |
| Build 0 erreur / 0 avertissement | ✅ |
| 1493 tests verts, 0 échec, 0 ignoré | ✅ 647 + 607 + 239 |
| Aucune vulnérabilité | ✅ 7 projets |
| Aucune migration en attente | ✅ « No changes … » |
| `MMV.Application` pure | ✅ Domain seul |
| Règles P3 réellement opposées | ✅ §10, §12-§21 |
| Aucun chemin public ne contourne une règle critique | ✅ §29 |
| Aucune perte silencieuse d'historique prouvée | ✅ §13, §29 |
| Critères de sortie satisfaits | ✅ **sauf « suppression sûre partout », partiellement satisfait et analysé sans neutralisation** (§28.1) |
| Migrations et adoptions cohérentes | ✅ §22 |
| Scénarios Acceptance verts | ✅ 11/11 |
| Dette P3-8 définitivement close | ✅ committée (`4c2a1e5`) + CI verte ; vérification physique par PRAGMA confirmée (§18.1) |
| Dettes restantes honnêtement classées | ✅ §30 |
| Code mergeable | ✅ §33.1 |
| Rapport existant | ✅ ce fichier |
| Aucun code/test/migration/UI modifié | ✅ §34 |

Le seul écart — le critère « suppression sûre **partout** » face à la suppression physique d'ordonnance — est
**antérieurement assumé et publiquement documenté** par la roadmap P3-3, **borné** dans son impact (acte
délibéré et confirmé, aucune cascade, aucun document historique perdu, aucune rupture référentielle) et
**n'appartient à aucune** des catégories bloquantes du §28 du cadrage. Il est reporté **tel quel**, classé
« partiellement satisfait », et constitue une entrée prioritaire du futur cadrage.

# ~~**P3-12 AUDIT = GO**~~ → révisé, voir §36

# ~~**P3 = GO MERGE CANDIDATE**~~ → révisé, voir §36

Verdicts complémentaires, à ne jamais confondre :

- **Branche immédiatement fusionnable dans `origin/main`** : **OUI** — fast-forward, 0 commit divergent,
  `merge-tree` sans conflit (§33.2).
- **Produit prêt pour une production V1 multi-poste** : **NON** — provider serveur non choisi ni implémenté,
  report acté par ADR-PROD-DB-001 (§33.3).

**Réserve procédurale** : le merge doit être précédé des corrections documentaires **C1 → C6** (§32.1), du
commit P3-12 et de sa CI verte.

Aucun commit. Aucun push. Aucune correction. Aucune roadmap P4. **STOP.**

---

## 36. Remédiation du blocage final — conservation des ordonnances

> **Section ajoutée après coup.** Tout ce qui précède est conservé **tel quel** : le défaut initial, son
> caractère public et atteignable, le classement « 🟡 dette acceptée » de §13.2 et le critère « suppression
> sûre partout » alors **partiellement satisfait** (§28.1). Ce rapport n'est pas réécrit comme si le défaut
> n'avait jamais existé — il a existé, il a été trouvé ici, et voici ce qui a été fait.

### 36.1 Révision du verdict de §13.2 et §28.1

L'arbitrage initial classait la suppression physique d'ordonnance en **dette acceptée non bloquante**, au
motif qu'elle est délibérée, confirmée, sans cascade et sans perte de document dérivé. Ce raisonnement était
exact sur l'**impact borné**, mais il **excusait** un critère de sortie explicitement énoncé au singulier :
« suppression sûre **partout** ». Le mot n'admet pas d'exception, et §28.1 le reconnaissait sans en tirer la
conséquence.

**Verdict révisé : le point était bloquant.** Une boîte de dialogue de confirmation n'est pas une garantie
backend : elle vit dans une ViewModel, elle ne protège que le chemin qui la traverse, et tout appelant entrant
par la couche Application l'ignore. Le seul rempart réel de l'enregistrement médical était donc une
convention d'interface.

### 36.2 Décision métier

En l'absence :

- de relation fiable `Prescription ↔ Sale` (aucune FK, §13.1) ;
- de champ d'archivage ;
- de versionnement ;
- de toute preuve permettant d'identifier une ordonnance réellement **inutilisée** ;

**la suppression physique d'une ordonnance est interdite dans tous les cas.**

Explicitement **écarté** : deviner l'usage d'après les dates ; chercher une vente aux valeurs optiques
ressemblantes ; autoriser la suppression au seul motif qu'aucune FK n'existe ; introduire un archivage
partiel ; créer une migration. Une heuristique qui se trompe détruit un dossier médical sans trace : en
l'absence de preuve, la conservation prime.

### 36.3 Correctif

- **Propriétaire Domain** : `src/MMV.Domain/Policies/PrescriptionRetentionPolicy.cs` — classe statique pure,
  porteuse du message métier stable. Elle **n'expose aucun prédicat** : la règle étant inconditionnelle, un
  `CanDelete(…)` répondant invariablement `false` laisserait croire qu'une condition existe.
- **Point de refus** : `DeletePrescriptionUseCase` lève une `BusinessRuleException` dès que l'ordonnance
  existe — **avant** toute mutation. Ni `DeleteAsync`, ni `SaveChangesAsync`, ni `ExecuteDeleteAsync`, ni SQL
  brut. L'`IUnitOfWork` a été **retiré du constructeur** : plus rien n'est à valider, et le use case ne détient
  plus aucune dépendance capable d'écrire. *(Formulation corrigée : une version antérieure de cette ligne
  décrivait un `IUnitOfWork` « validé puis non conservé », c'est-à-dire encore reçu en paramètre. Le dépôt
  prime — `DeletePrescriptionUseCase` déclare un unique constructeur à un unique paramètre
  `IPrescriptionRepository`, propriété opposée par le test
  `Constructor_DeclaresOnlyThePrescriptionRepository`.)*
- **Contrat « introuvable » préservé** : `PrescriptionFound = false`, aucune exception, aucune écriture. Ce
  cas n'est pas un refus métier mais un état multi-poste banal, que la ViewModel traite par un rechargement.
- **Use case conservé, non supprimé** : trois ViewModels le résolvent par DI. Le retirer aurait cassé la
  composition de l'UI, hors périmètre. Il devient le **gardien** du chemin public.

### 36.4 Résultat UI — sans aucune modification

`src/MMV.App/**` et `tests/MMV.App.Tests/**` sont **inchangés** (App reste exactement à **239** tests).
Vérifié par inspection : `CustomerPrescriptionsViewModel.ExecuteDeleteAsync` enveloppe l'appel dans un
`try/catch (Exception ex)` (lignes 300-318) qui affecte
`ErrorMessage = "Erreur lors de la suppression : {message}"`. Le refus est donc **absorbé et affiché**, sans
exception non gérée. Le bouton de suppression reste visible ; remplacer ou masquer ce bouton est une
amélioration **UX**, reportée au redesign. **La sécurité de la donnée ne dépend plus de l'UI.**

### 36.5 Absence de migration

Aucune migration, aucun `ModelSnapshot`, aucune entité, aucune configuration EF, aucun repository, aucun
paquet. Le correctif est **purement comportemental** : `has-pending-model-changes` répond toujours
« No changes have been made to the model since the last migration ».

### 36.6 Nouveau total

| Projet | Avant | Après |
|---|---:|---:|
| Domain | 647 | **649** |
| Application | 607 | **611** |
| App | 239 | **239** |
| **Total** | **1493** | **1499** |

Soit +2 (policy Domain), +5 (use case : refus, non-écriture prouvée par doubles stricts, conservation sur
vrai SQLite, refus répété, non-régression P3-2B) et **−1** : l'ancien test nominal
`ExecuteAsync_DeletesExistingPrescription_AndPersists` **gravait** la suppression physique et devenait
contradictoire avec la règle. Il a été **remplacé**, pas ignoré. 0 échec, 0 ignoré.

### 36.7 Critère de sortie

Le critère « **suppression sûre partout** », classé « partiellement satisfait » en §28, est **désormais
pleinement satisfait** : Clients, Produits, Fournisseurs, Commandes, Utilisateurs et **Ordonnances** opposent
tous une protection backend. Inventaire complet : roadmap §6.1.

**Réserve d'honnêteté maintenue** : la conservation n'est **pas** une immutabilité. Une ordonnance reste
modifiable, deux postes qui la corrigent concurremment s'écrasent toujours en silence, et le lien passé entre
une vente et l'ordonnance qui l'a motivée demeure irrécupérable faute de FK. Ces dettes restent **ouvertes**
(§30, §31) — P3-12 ferme uniquement la perte **définitive** de l'enregistrement.

### 36.8 Verdict révisé

# **P3-12 REMÉDIATION = GO LOCAL**

# **P3 = GO MERGE CANDIDATE LOCAL**

**Sous réserve de commit et de CI verte.** Aucun commit, aucun push, aucun merge à ce stade.

> **Étape intermédiaire, datée.** Ces deux verdicts décrivent fidèlement l'état **avant** commit. Ils sont
> conservés comme trace ; la réserve est **levée** en §37, qui porte le verdict définitif.

---

## 37. Validation CI définitive de la remédiation P3-12

> Section postérieure au commit et au push. Elle ne réécrit **rien** de ce qui précède : le défaut découvert
> (§13.2), le premier verdict `NO-GO` de la remédiation, le classement initial « 🟡 dette acceptée », la
> remédiation (§36) et le verdict local `GO LOCAL` (§36.8) restent lisibles **tels quels**. Elle ajoute la
> seule chose qui manquait : la preuve CI sur le SHA exact.

### 37.1 Commit

| Champ | Valeur |
|---|---|
| SHA | `c1751007b8f8304154306381b2986f628a256c28` |
| Message | `fix(P3-12): preserve prescription history` |
| Branche | `p3-business-rules` |
| Fichiers | **9** — 5 modifiés, 4 créés, 0 supprimé (production 3 · tests 2 · documentation 4) |

Les neuf fichiers ont été **indexés un à un** (jamais `git add .` ni `git add -A`). Les trois dossiers
`design-handoff/`, `design/` et `docs/ui/` sont restés **non indexés et non suivis**.

### 37.2 Run CI

`gh run view 29964844698 --json databaseId,headSha,headBranch,status,conclusion,event,url,jobs`

| Champ | Valeur |
|---|---|
| `databaseId` | `29964844698` |
| `headSha` | `c1751007b8f8304154306381b2986f628a256c28` **(SHA exact du commit)** |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | **`success`** |

Job unique **« Restore / Build / Test / Scan »** — `success`. Étapes obligatoires toutes `success` :
**Restore** (5), **Build** (6), **Test** (7), **Audit des packages vulnérables** (8), **Check EF Core pending
model changes** (10). **Aucun job obligatoire en échec.**

### 37.3 Totaux finaux et contrôles

| Contrôle | Résultat |
|---|---|
| Domain | **649** |
| Application | **611** |
| App | **239** |
| **Total** | **1499**, **0 échec**, **0 ignoré** |
| Build | **0 erreur / 0 avertissement** |
| Vulnérabilités | **0** — « no vulnerable packages » sur les **7** projets |
| Modèle EF | **« No changes have been made to the model since the last migration. »** |
| Pureté Application | `MMV.Application` → **`MMV.Domain` seulement** (référence projet unique) |
| Migration | **aucune** |
| Infrastructure | **aucune** modification |
| UI | **aucune** modification (`MMV.App.Tests` exactement à 239) |

### 37.4 État Git après push

```
git rev-parse HEAD                     → c1751007b8f8304154306381b2986f628a256c28
git rev-parse origin/p3-business-rules → c1751007b8f8304154306381b2986f628a256c28
git status --short                     → ?? design-handoff/   ?? design/   ?? docs/ui/
git diff --check                       → (vide, exit 0)
```

HEAD local et distant **identiques**. Aucun fichier suivi modifié. Seuls les trois dossiers hors périmètre
restent non suivis.

### 37.5 Relation avec `origin/main` — inchangée

| Contrôle | Résultat |
|---|---|
| `git rev-parse origin/main` | `1e28f1e6a8e9de08ab75e042cad37800c0390495` |
| `git merge-base origin/main HEAD` | `1e28f1e6…` — **la base de fusion est `origin/main`** |
| `git rev-list --left-right --count origin/main...HEAD` | **`0  29`** |
| `git merge-tree --write-tree origin/main HEAD` | **exit 0**, arbre `e53e2c83518dad1ebddd78f92bdcf75839a785ff`, **aucun conflit** |
| Nature | **fast-forward possible** |

`origin/main` n'a pas avancé. Le décompte passe de **28** (§3, mesuré au HEAD `4c2a1e5`) à **29** : le seul
commit ajouté est `c175100`. Aucun conflit détecté.

### 37.6 Aucun merge effectué

**Le merge vers `main` n'a pas été réalisé.** Aucun `git merge`, `git rebase`, `git cherry-pick` ni
`gh pr merge`. `origin/main` reste à `1e28f1e`. **P4 n'est pas commencé** et aucune roadmap P4 n'est définie.

### 37.7 Verdict définitif

| Condition | État |
|---|---|
| CI verte sur le **SHA exact** du correctif | ✅ `29964844698` — `push` / `completed` / `success` |
| 1499 tests (649 · 611 · 239), 0 échec, 0 ignoré | ✅ |
| Build 0 erreur / 0 avertissement | ✅ |
| 0 vulnérabilité, aucun `pending model change` | ✅ |
| `MMV.Application` pure (Domain seulement) | ✅ |
| Aucune migration, Infrastructure ni UI | ✅ |
| Critère « suppression sûre partout » pleinement satisfait | ✅ roadmap §6.1 |
| Branche fusionnable en fast-forward, sans conflit | ✅ §37.5 |
| Aucun merge effectué | ✅ §37.6 |

# **P3-12-CI = GO**

# **P3 = GO MERGE CANDIDATE**

**Réserve maintenue, à ne jamais confondre avec ce verdict** : *P3 merge-ready* n'est **pas**
*V1 production multi-poste ready* (§27.2, §33.3). Le provider serveur exigé par ADR-PROD-DB-001 n'est ni
choisi ni implémenté. Les dettes de §30 et §31 restent **ouvertes**, notamment l'immutabilité forte et la
concurrence des ordonnances : la conservation empêche la perte, elle n'est pas une immutabilité.
