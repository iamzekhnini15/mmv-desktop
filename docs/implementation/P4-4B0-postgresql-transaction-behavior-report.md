# P4-4B0 — Comportement transactionnel PostgreSQL : rapport de mesure

**Lot :** P4-4B0 — mesure et verrouillage du comportement transactionnel PostgreSQL
**Nature :** passe de MESURE. **Aucun code de production n'a été modifié.**
**Statut :** `P4_4B0_TRANSACTION_MEASUREMENT = READY_FOR_B1_DECISION`

---

## 0. Convention de niveaux de preuve

Ce rapport n'emploie que les six étiquettes suivantes. Elles ne sont jamais mélangées, et aucune
conclusion ne s'appuie sur un niveau plus fort que celui réellement atteint.

| Étiquette | Signification exacte |
|---|---|
| `EXECUTED_SPIKE` | SQL brut exécuté par le harness contre un vrai PostgreSQL. Prouve le **serveur**, pas MMV. |
| `PRODUCTION_CODE_EXECUTED` | Une classe de `src/**` a été **instanciée et exécutée telle quelle** contre un vrai PostgreSQL. |
| `STATIC_CODE_PROOF` | Fait établi par lecture du code, sans exécution. Vrai par construction, non re-prouvé au runtime. |
| `GENERIC_EF_EVIDENCE` | Mécanique EF Core mesurée sur une table du harness. Ne prouve pas un use case MMV. |
| `BLOCKED_BY_P4_5_SCHEMA` | Mesure impossible sans le portage de schéma P4-5. **Aucun équivalent n'a été fabriqué.** |
| `NOT_PROVED` | Non mesuré et non déduit. |

---

## 1. Base Git

| Élément | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| HEAD local | `dc4c4924a88e6ba619baec32cdaaa64645ea2814` |
| `origin/p4-multi-poste` | `dc4c4924a88e6ba619baec32cdaaa64645ea2814` |
| Fichiers suivis modifiés à l'ouverture | aucun |
| Index (staged) à l'ouverture | vide |
| Non suivis préexistants | `design-handoff/`, `design/`, `docs/ui/` (intacts, non touchés) |

Contexte acquis : P4-3 = CLOSE ; P4-4A0 = RECORDED (`b84c7e3`) ; P4-4A1 = RECORDED (`dc4c492`,
CI `33436908075` SUCCESS sur le SHA exact). Baseline `MMV.sln` = **1565 tests**
(Domain 706 · Application 620 · App 239), 0 échec, 0 ignoré. **Cette baseline n'a pas été rejouée :
aucun fichier de `MMV.sln` n'a été touché.**

---

## 2. Environnement (sans secret)

| Élément | État |
|---|---|
| `MMV_P4_POSTGRES_CONNECTION` | **définie** (valeur jamais lue, jamais journalisée, jamais reproduite ici) |
| `MMV_P4_PG_CONTAINER` | `mmv-p4-pg` |
| Docker | serveur `29.4.1`, répond |
| Conteneur `mmv-p4-pg` | `Up 29 hours`, image `postgres:17` |
| PostgreSQL | `pg_isready` → *accepting connections* ; `PostgreSQL 17.10 (Debian 17.10-1.pgdg13+1) x86_64` |
| Bases utilisées | **jetables**, préfixe `mmv_p4_`, créées puis supprimées par test (`SpikeDatabase`) |

Aucun secret n'a été créé, régénéré, affiché ni écrit dans le dépôt.

---

## 3. Inventaire transactionnel actuel — `STATIC_CODE_PROOF`

### 3.1 `EfTransactionRunner` — [EfTransactionRunner.cs](src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs)

| Point | Constat |
|---|---|
| Type de contexte | `OpticDbContext` **concret** (champ `private readonly OpticDbContext _context`), non `DbContext` |
| Constructeur | `EfTransactionRunner(OpticDbContext context)` — garde `ArgumentNullException` ; **aucune autre dépendance** |
| Surface | `RunAsync(Func<CancellationToken,Task>, ct)` délègue à `RunAsync<TResult>` |
| Frontière imbriquée | `if (_context.Database.CurrentTransaction != null) return await operation(ct);` — **se rattache sans ouvrir de 2ᵉ transaction, et sans catch ni mapping** |
| Ouverture | `await using var transaction = await _context.Database.BeginTransactionAsync(ct)` |
| Succès | `await transaction.CommitAsync(ct)` |
| Catch | `catch (Exception ex)` — **attrape tout**, y compris `OperationCanceledException` et les exceptions métier |
| `SafeRollbackAsync` | `await transaction.RollbackAsync(CancellationToken.None)` dans un `try { } catch { }` best-effort |
| **Jeton du rollback** | **`CancellationToken.None`** — délibérément **pas** le jeton client |
| Appel au mapper | `var mapped = PersistenceErrorMapper.Map(ex); if (ReferenceEquals(mapped, ex)) throw; throw mapped;` — le `throw;` nu préserve la pile quand rien n'est traduit |

Ordre : **rollback d'abord, mapping ensuite**. Le mapper ne peut donc jamais s'exécuter pendant que la
transaction est encore avortée.

### 3.2 `UnitOfWork` — [UnitOfWork.cs](src/MMV.Infrastructure/Repositories/UnitOfWork.cs)

| Point | Constat |
|---|---|
| `BeginTransactionAsync(ct)` | `_transaction = await _context.Database.BeginTransactionAsync(ct)` |
| `CommitAsync(ct)` | `try { SaveChangesAsync(ct); _transaction?.CommitAsync(ct); DisposeAsync(); _transaction = null; } catch { await RollbackTransactionAsync(ct); throw; }` |
| `RollbackTransactionAsync(ct)` *(privée)* | `await _transaction.RollbackAsync(ct)` — **relaie le jeton CLIENT** |
| `RollbackAsync(ct)` *(publique, interface)* | **N'annule aucune transaction** : le corps est `await _context.DisposeAsync();` — le paramètre `ct` est **inutilisé** |
| Appelants de production | **AUCUN** pour `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`. Vérifié par recherche sur `src/**` : les seules occurrences hors `UnitOfWork.cs` sont des **commentaires historiques** et l'enregistrement DI `services.AddScoped<IUnitOfWork, UnitOfWork>()`. Seul **`SaveChangesAsync`** est réellement appelé en production. |

**Conséquence structurelle :** en production `_transaction` reste toujours `null`, donc le `catch` de
`CommitAsync` et `RollbackTransactionAsync` sont **du code mort en production**. Le défaut mesuré au
§9 est **réel mais non atteignable par le produit actuel**.

### 3.3 `OrderRepository.CreateNextWorkshopSheetVersionAsync` — [OrderRepository.cs:210-260](src/MMV.Infrastructure/Repositories/OrderRepository.cs#L210-L260)

Commandes **avant** le catch, dans l'ordre :

1. `ExecuteUpdateAsync` (compare-and-swap `IsCurrent → false`) — **uniquement si** `precondition.ExpectsExistingVersion` ;
   `switched != 1` ⇒ `throw new WorkshopSheetVersionConflictException(...)` sans écriture supplémentaire ;
2. `WorkshopSheetFactory.Create(...)` — **Domain pur, aucune I/O** ;
3. `_context.WorkshopSheets.AddAsync(sheet, ct)` — opération ChangeTracker, **aucune commande SQL** ;
4. `await _context.SaveChangesAsync(ct)` dans un `try`.

Catch concerné :

```csharp
catch (DbUpdateException)
{
    throw new WorkshopSheetVersionConflictException(order.OrderId, nextVersion);
}
```

Commandes **après** le catch : **ZÉRO**. Le corps du catch est une unique instruction `throw`.
`WorkshopSheetVersionConflictException` est une `DomainException` pure
([DomainExceptions.cs](src/MMV.Domain/Exceptions/DomainExceptions.cs)) : interpolation de chaîne +
deux affectations de propriétés, **aucun accès base**. `return sheet;` est hors du catch et n'est
atteint qu'en succès.

**Réconciliation avec l'audit P4-4 :** les **deux** appelants de cette méthode s'exécutent à
l'intérieur du **vrai** `ITransactionRunner` —
[GenerateWorkshopSheetUseCase.cs:89](src/MMV.Application/UseCases/WorkshopSheets/GenerateWorkshopSheet/GenerateWorkshopSheetUseCase.cs#L89)
(dans `RunAsync`) et
[AdvanceOrderStatusUseCase.cs:163](src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs#L163)
via `WorkshopSheetOperations.EnsureCurrentSheetAsync` (dans le `RunAsync` ouvert ligne 93).
La `WorkshopSheetVersionConflictException` remonte donc au `catch (Exception ex)` du runner, qui
**rollbacke**, puis `PersistenceErrorMapper.Map` la renvoie **inchangée** (branche G : ce n'est pas
une erreur de persistance) et le `throw;` nu préserve la pile. **La `PostgresException` d'origine est
consommée dans le repository et n'atteint jamais le mapper — c'est intentionnel, et sans risque de
`25P02` puisque le rollback a lieu quoi qu'il arrive.**

### 3.4 `CreateProduct` / `UpdateProduct` — [CreateProductUseCase.cs:126-166](src/MMV.Application/UseCases/Products/CreateProduct/CreateProductUseCase.cs#L126-L166) · [UpdateProductUseCase.cs:107-142](src/MMV.Application/UseCases/Products/UpdateProduct/UpdateProductUseCase.cs#L107-L142)

Les deux ont la **même forme**, et c'est cette forme qui portait le risque le plus élevé de l'audit :

```csharp
try
{
    var supplierMissing = await _transactionRunner.RunAsync(async ct =>
    {
        if (!await _supplierRepository.ExistsFreshAsync(supplierId, ct)) return true;
        await _productRepository.CreateAsync(product, ct);   // ou UpdateCatalogAsync
        await _unitOfWork.SaveChangesAsync(ct);
        return false;
    }, cancellationToken);
    ...
}
catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.ConstraintViolation)
{
    if (await _supplierRepository.ExistsFreshAsync(supplierId, cancellationToken)) throw;   // ← REQUÊTE APRÈS ÉCHEC
    return ... SupplierNotFoundMessage;
}
catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.UniqueConstraint)
{
    return ... DuplicateReferenceMessage;   // aucune requête
}
```

**Le point critique** est le `ExistsFreshAsync` du catch `ConstraintViolation` : il s'exécute **après**
un `SaveChangesAsync` échoué, **sur le même `OpticDbContext`** (portée DI `Scoped` partagée), donc sur
la même connexion. Sur PostgreSQL, si la transaction était encore avortée, cette requête déclencherait
`25P02` et transformerait un message métier propre en panne opaque.

`ExistsFreshAsync` — [SupplierRepository.cs:84-90](src/MMV.Infrastructure/Repositories/SupplierRepository.cs#L84-L90) — **NIVEAU A (code)** :

| Question | Réponse |
|---|---|
| `AsNoTracking` ? | **OUI** |
| Requête unique ? | **OUI** — `AnyAsync(s => s.SupplierId == supplierId, ct)`, un seul `SELECT` |
| Appelle `SaveChanges` ? | **NON** |
| Dépend du ChangeTracker ? | **NON** — `AsNoTracking` court-circuite l'identity map ; c'est précisément ce pour quoi la méthode a été créée en P3-9 (`ExistsAsync` hérité passait par `FindAsync` et pouvait répondre depuis le tracker sans toucher la base) |

---

## 4. B0.1 — Contrôle positif `25P02` — `EXECUTED_SPIKE`

Test : `E21_PostgreSqlTransactionBehaviorTests.B01_aborted_transaction_really_yields_25P02`.
SQL brut, aucune classe MMV. Table de sonde `p4_tx_probe (id identity PK, code text NOT NULL UNIQUE)`.

| Étape | Attendu | **Mesuré** |
|---|---|---|
| `BEGIN` + `INSERT code='A'` | accepté | accepté |
| 2ᵉ `INSERT code='A'` | `PostgresException` `23505` | **`Npgsql.PostgresException`, `SqlState=23505`, contrainte `ux_p4_tx_probe_code`, table `p4_tx_probe`** |
| `SELECT` **sans rollback** | `PostgresException` `25P02` | **`Npgsql.PostgresException`, `SqlState=25P02`** |
| `ROLLBACK` puis `SELECT` | réussit | **réussit** |
| Lignes `'A'` persistées | 0 | **0** |

**Ce contrôle est la condition de validité de tout le reste :** il établit que le laboratoire
*détecterait* réellement l'usage d'une transaction avortée. Les absences de `25P02` constatées aux
§5-§8 sont donc des observations, non des angles morts.

---

## 5. B0.2 — `EfTransactionRunner` réel sur PostgreSQL — `PRODUCTION_CODE_EXECUTED`

`EfTransactionRunner` **a bien pu être exercé sans construire le schéma de production**, et il l'a été
**tel quel**, sans copie ni réécriture : le runner ne touche jamais au modèle EF — il n'utilise que
`Database.CurrentTransaction`, `BeginTransactionAsync`, `CommitAsync` et `RollbackAsync`. La frontière
transactionnelle mesurée est donc **exactement** celle de la production.

Montage : vrai `OpticDbContext` (`new OpticDbContext(options.UseNpgsql(...))`), vrai
`new EfTransactionRunner(context)`. **`EnsureCreated()` et `Migrate()` ne sont jamais appelés.**
Callback : deux `INSERT` du même `code='B'`, exception laissée remonter.

| Observation | **Mesuré** |
|---|---|
| Chaîne d'exception en sortie | `MMV.Domain.Exceptions.PersistenceException` → `Npgsql.PostgresException` |
| Type final | **`PersistenceException`** |
| `Category` | **`UniqueConstraint`** |
| `SqlState` conservé | **`23505`** |
| `ConstraintName` conservé | `ux_p4_tx_probe_code` |
| `InnerException` d'origine | **préservée et accessible** |

Le mapper P4-4A1 reçoit donc bien l'erreur **réelle** lorsqu'elle transite par le runner, et la classe
correctement — sur signal structuré (`SqlState`), jamais sur le texte.

### 5.1 État des données après rollback

| Vérification | **Mesuré** |
|---|---|
| Lignes `code='B'` après sortie du runner (connexion **neuve**) | **0** |

L'`INSERT` **#1**, qui avait pourtant réussi, a été annulé lui aussi : la transaction a réellement été
rollbackée, **aucune écriture partielle ne subsiste**.

### 5.2 Réutilisation du `DbContext` après rollback

| Vérification | **Mesuré** |
|---|---|
| `context.Database.CurrentTransaction` | **`null`** |
| Requête sur le **même** `DbContext` après le rollback | **acceptée, aucun `25P02`** |
| Résultat | 0 ligne, cohérent avec le rollback |

Le contexte n'est plus accroché à une transaction PostgreSQL avortée : il est **réutilisable**.

---

## 6. B0.3 — Annulation cliente après écriture — `PRODUCTION_CODE_EXECUTED`

Vrai runner. Dans `RunAsync` : `INSERT code='C'` réussi, puis `cts.Cancel()`, puis
`throw new OperationCanceledException(cts.Token)` — le jeton est donc **déjà annulé au moment où le
runner tente son rollback**.

| Attendu | **Mesuré** |
|---|---|
| Rollback tenté avec `CancellationToken.None` | **abouti** |
| Lignes `code='C'` (connexion neuve) | **0** |
| Comportement d'annulation préservé | **OUI** — sortie `System.OperationCanceledException`, `annulation demandée = OUI` |
| Jamais transformée en erreur de persistance | **OUI** — la sortie n'est **pas** une `PersistenceException` (branche B du mapper : annulation réellement demandée ⇒ relayée inchangée) |
| `CurrentTransaction` après sortie | **`null`** |

**Ce scénario démontre pourquoi le rollback ne doit pas dépendre du jeton client** : le §9 montre
qu'avec ce même jeton annulé, le rollback **échoue**.

---

## 7. B0.4 — Commandes exécutées après l'erreur — `PRODUCTION_CODE_EXECUTED` (+ limite assumée)

Instrumentation : `DbCommandInterceptor` **du harness**, ajouté via
`DbContextOptionsBuilder.AddInterceptors` — **aucune modification de production**. Il n'enregistre que
le **verbe SQL** (premier mot-clé), l'**ordre** et le **succès/échec**. Jamais un paramètre, jamais une
valeur, jamais une chaîne de connexion, jamais un message.

| Observation | **Mesuré** |
|---|---|
| Séquence complète du callback B0.2 | `1.INSERT(OK) → 2.INSERT(ECHEC)` |
| Commandes applicatives **après** l'échec, dans la transaction | **0** |
| Callback B0.7 après l'échec | **0** |
| Requête du catch B0.7 (`ExistsFreshAsync`) | `1.SELECT(OK)` — **0 commande d'écriture** |

**LIMITE ASSUMÉE, non contournée :** `BEGIN`, `COMMIT` et `ROLLBACK` transitent par l'API ADO.NET de
transaction (`DbTransaction`), **pas** par `DbCommand` ; un `DbCommandInterceptor` **ne peut pas** les
observer. Aucune prétention contraire n'est faite ici. La preuve du rollback repose sur les **données**
(§5.1, §6, §9), pas sur cet intercepteur. Le rollback transactionnel n'est de toute façon pas une
« commande applicative illégale ».

---

## 8. B0.7 — ChangeTracker et `ExistsFreshAsync` après `SaveChanges` échoué

**Niveau : `PRODUCTION_CODE_EXECUTED` pour le comportement, sur une table du harness.**

Code de production réellement instancié et exécuté : `OpticDbContext`, `EfTransactionRunner`,
`PersistenceErrorMapper`, `SupplierRepository.ExistsFreshAsync`, et le chemin EF
`DbSet.Add` + `SaveChangesAsync` **du modèle de production** (entité `Supplier`, mapping
`SupplierConfiguration` inchangé).

> **LIMITE EXPLICITE — aucun schéma de production n'est revendiqué.** La table `"Suppliers"` a été
> créée **à la main** par SQL brut dans une base jetable, alignée sur le mapping EF. La contrainte
> `UNIQUE` posée sur `"Name"` est un **instrument de mesure** : elle **n'existe pas** dans le modèle
> MMV. Aucune migration n'a été produite, `EnsureCreated`/`Migrate` n'ont jamais été appelés. La
> portabilité du schéma reste **entièrement à faire en P4-5**.

Scénario, calqué sur la forme exacte du use case : dans le vrai `RunAsync` →
`ExistsFreshAsync(id)` (lecture fraîche dans la transaction, production) → `Add` d'un doublon →
`SaveChangesAsync` → échec serveur.

| Observation | **Mesuré** |
|---|---|
| Chaîne d'exception | `PersistenceException` → **`Microsoft.EntityFrameworkCore.DbUpdateException`** → `Npgsql.PostgresException` |
| `SqlState` | **`23505`** (contrainte `ux_harness_supplier_name`, table `Suppliers`) |
| `Category` | **`UniqueConstraint`** |
| **`Entry(entity).State` après rollback** | **`Added`** |
| Entrées ChangeTracker en attente après rollback | **1** |
| `CurrentTransaction` après rollback | **`null`** |
| `ExistsFreshAsync` **après** le rollback, **même contexte** | **exécutée, aucun `25P02`** |
| Commandes émises par ce catch | **`1.SELECT(OK)`** |
| Commandes d'**écriture** déclenchées par ce catch | **0** |
| Résultat | `True` — **provient bien de la base** |
| Lignes persistées après rollback | **1** (la seule ligne semée) — **aucune écriture partielle** |

**Réponse à la question 9 : le chemin est SÛR.** L'entité fautive **reste effectivement `Added`** dans
le ChangeTracker après le rollback — le risque identifié par l'audit est donc **réel dans son
prémisse** —, mais il **ne se matérialise pas** : EF Core ne « flushe » pas le ChangeTracker avant une
requête (contrairement à un ORM à *auto-flush*), et `AsNoTracking().AnyAsync(...)` n'émet **qu'un
`SELECT`**. Aucune réécriture n'est tentée, et le résultat n'est pas contaminé par l'entité en attente.

Deux conditions rendent cela sûr, et il faut les nommer car **P4-4B1 ne doit pas les casser** :

1. le runner **rollbacke avant** que le catch ne s'exécute (§5.1) — sans quoi ce `SELECT` recevrait `25P02` ;
2. `ExistsFreshAsync` est en `AsNoTracking` et **n'appelle pas** `SaveChanges`.

---

## 9. B0.5 — `UnitOfWork` et le jeton annulé — `PRODUCTION_CODE_EXECUTED`

Le comportement **a pu être mesuré proprement** : il n'y a donc **pas** de `NOT_PROVED_RUNTIME` ici.

### 9.1 Primitives Npgsql / EF Core

| Primitive (jeton **déjà annulé**) | **Mesuré** |
|---|---|
| `IDbContextTransaction.CommitAsync(cancelledToken)` | **lève `System.OperationCanceledException`**, `annulation demandée = OUI` |
| Lignes `'P1'` persistées après libération | **0** (le commit n'a pas eu lieu) |
| `IDbContextTransaction.RollbackAsync(cancelledToken)` | **lève `System.OperationCanceledException`**, `annulation demandée = OUI` |
| **État immédiatement après cette exception** | **`CurrentTransaction` = ENCORE PRÉSENTE** ; la **même session voit encore sa propre écriture non validée (1 ligne `'P2'`)** ⇒ **le `ROLLBACK` N'A PAS EU LIEU, la transaction est restée OUVERTE** |
| `RollbackAsync(CancellationToken.None)` de rattrapage | **ABOUTI** |
| Lignes `'P2'` après libération | **0** |

### 9.2 Composite — le vrai `UnitOfWork`

`new UnitOfWork(context)` → `BeginTransactionAsync()` → écriture `'U'` → jeton annulé →
`CommitAsync(cts.Token)`.

| Observation | **Mesuré** |
|---|---|
| Exception sortie de `CommitAsync` | `System.OperationCanceledException`, `annulation demandée = OUI` |
| **État AVANT toute libération** | **`CurrentTransaction` = ENCORE PRÉSENTE** ; **1 ligne `'U'` encore visible par la même session** ⇒ **le `catch` de `CommitAsync` n'a PAS annulé** |
| Lignes `'U'` après libération (`DisposeAsync`) | **0** |

### 9.3 Réponses aux trois hypothèses de l'audit

| Hypothèse | Verdict |
|---|---|
| **A. Le jeton annulé peut empêcher le rollback** | **CONFIRMÉ (mesuré).** `RollbackAsync(cancelledToken)` lève **avant** d'annuler : la transaction reste **ouverte**, la session voit toujours son écriture non validée. Seule la **libération** du contexte finit par l'annuler. |
| **B. Il peut lever une nouvelle `OperationCanceledException`** | **CONFIRMÉ (mesuré).** |
| **C. Il peut masquer l'exception qui a déclenché le `catch`** | **CONFIRMÉ.** Combinaison d'un fait **mesuré** (l'appel lève) et d'un fait de **langage** : dans `catch { await RollbackTransactionAsync(ct); throw; }`, si l'`await` lève, le `throw;` est **inatteignable** et l'exception d'origine est **remplacée**. Aucune extrapolation. |

### 9.4 Portée réelle du défaut

**Le défaut est réel mais aujourd'hui inatteignable par le produit** : `BeginTransactionAsync` /
`CommitAsync` / `RollbackAsync` de `UnitOfWork` n'ont **aucun appelant de production** (§3.2), donc
`_transaction` y est toujours `null`. Le risque est **latent** : il se matérialiserait dès qu'un futur
use case utiliserait cette API.

**Contraste décisif :** avec le **même** jeton annulé, `EfTransactionRunner` **rollbacke correctement**
(§6) — précisément parce que `SafeRollbackAsync` emploie `CancellationToken.None` et enveloppe l'appel
dans un `catch` best-effort. **La ligne `CancellationToken.None` du runner n'est pas cosmétique : elle
est la seule raison mesurée pour laquelle le rollback aboutit sous annulation.**

---

## 10. B0.6 — `OrderRepository` — `STATIC_CODE_PROOF` + `BLOCKED_BY_P4_5_SCHEMA`

**Preuve statique (§3.3) : ZÉRO commande base dans le `catch (DbUpdateException)`.** Re-vérifiée : le
corps du catch est une unique instruction `throw` d'une `DomainException` pure.

**La méthode n'a PAS pu être exercée réellement sur PostgreSQL**, et **aucun faux équivalent n'a été
fabriqué**. Blocages, tous du ressort de P4-5 :

1. **Index unique filtré non porté** — `WorkshopSheetConfiguration` déclare
   `HasFilter("\"IsCurrent\" = 1")`
   ([WorkshopSheetConfiguration.cs:62](src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs#L62)),
   littéral SQLite entier-booléen invalide sur PostgreSQL (`42883`, mesuré en P4-1/E3-E4). Or c'est
   **exactement** cet index (`idx_workshop_sheets_current_unique`) qui arbitre le conflit que le catch
   traduit : sans lui, la mesure ne prouverait rien.
2. **Agrégat complet requis** — `WorkshopSheetFactory.Create` lit `order.OrderItems` et
   `order.Sale?.Customer` : il faudrait `Orders`, `OrderItems`, `WorkshopSheets`,
   `WorkshopSheetItems`, `Sales`, `Customers`.
3. **Mapping `DateTime` non résolu** — les instantanés (`OrderDateSnapshot`,
   `EstimatedDeliverySnapshot`) relèvent du constat P4-1 encore ouvert.
4. **`ExecuteUpdateAsync`** s'appuie sur le modèle de production, non matérialisable avant P4-5.

Le résultat statique **ZÉRO commande après le catch** reste une **preuve de code**, et non une
re-preuve de primitive runtime.

---

## 11. Commandes après erreur — synthèse

| Chemin | Commandes applicatives après l'erreur, dans la transaction | Niveau |
|---|---|---|
| Callback `EfTransactionRunner` (B0.2, SQL brut) | **0** (mesuré) | `PRODUCTION_CODE_EXECUTED` |
| Callback `EfTransactionRunner` (B0.7, `SaveChangesAsync`) | **0** (mesuré) | `PRODUCTION_CODE_EXECUTED` |
| Catch `CreateProduct`/`UpdateProduct` | **1 `SELECT`, 0 écriture**, **hors** transaction (déjà rollbackée) | `PRODUCTION_CODE_EXECUTED` |
| Catch `OrderRepository` | **0** | `STATIC_CODE_PROOF` |

---

## 12. Classification finale des preuves

| # | Élément | Niveau |
|---|---|---|
| 1 | `25P02` réellement produit après erreur en transaction non rollbackée | `EXECUTED_SPIKE` |
| 2 | Le vrai `EfTransactionRunner` rollbacke sur violation d'unicité | `PRODUCTION_CODE_EXECUTED` |
| 3 | Aucune écriture partielle après rollback | `PRODUCTION_CODE_EXECUTED` |
| 4 | Le mapper reçoit et traduit l'erreur réelle via le runner (`23505` → `UniqueConstraint`) | `PRODUCTION_CODE_EXECUTED` |
| 5 | Annulation cliente après écriture : rollback abouti, annulation préservée | `PRODUCTION_CODE_EXECUTED` |
| 6 | `DbContext` réutilisable après rollback | `PRODUCTION_CODE_EXECUTED` |
| 7 | `UnitOfWork` : le jeton annulé empêche le rollback **et** masque l'exception | `PRODUCTION_CODE_EXECUTED` (A, B) + `STATIC_CODE_PROOF` (C) |
| 8 | `UnitOfWork` sans appelant de production | `STATIC_CODE_PROOF` |
| 9 | `OrderRepository` : zéro commande dans le catch | `STATIC_CODE_PROOF` |
| 10 | `OrderRepository` exercé réellement sur PostgreSQL | `BLOCKED_BY_P4_5_SCHEMA` |
| 11 | `ExistsFreshAsync` post-rollback sûre (`AsNoTracking`, 1 `SELECT`, 0 écriture) | `PRODUCTION_CODE_EXECUTED` (table du harness) |
| 12 | ChangeTracker conserve l'entité `Added` après rollback | `PRODUCTION_CODE_EXECUTED` |
| 13 | Portabilité du schéma de production PostgreSQL | `NOT_PROVED` — hors périmètre, relève de P4-5 |
| 14 | `ROLLBACK` observable via `DbCommandInterceptor` | `NOT_PROVED` — impossible par construction (API `DbTransaction`) |

---

## 13. Décision par élément

| Élément | **Verdict** | Fondement |
|---|---|---|
| **`EFTRANSACTIONRUNNER`** | **`SAFE_AS_IS`** | §5, §5.1, §5.2, §6, §7 — rollback effectif, aucune écriture partielle, contexte réutilisable, annulation préservée, mapping correct, zéro commande après erreur. |
| **`UNITOFWORK ROLLBACK TOKEN`** | **`CHANGE_REQUIRED`** (défaut **latent**, non atteignable aujourd'hui) | §9 — A, B et C confirmés. Aucun appelant de production ⇒ **aucune urgence**, mais le code est faux tel qu'écrit. |
| **`ORDERREPOSITORY CATCH`** | **`SAFE_AS_IS`** sur preuve statique · **`BLOCKED_BY_P4_5_SCHEMA`** pour la re-preuve runtime | §3.3, §10 — zéro commande dans le catch ; les deux appelants sont dans le runner, donc rollback garanti. |
| **`CREATE/UPDATE PRODUCT POST-ROLLBACK QUERY`** | **`SAFE_AS_IS`** | §8 — mesuré sur le vrai contexte, le vrai runner et la vraie `ExistsFreshAsync` : aucun `25P02`, un seul `SELECT`, zéro écriture, résultat issu de la base. |

---

## 14. Changements de production recommandés pour P4-4B1

### 14.1 `EfTransactionRunner` — **AUCUN CHANGEMENT**

Les mesures montrent qu'il est **déjà correct** sur PostgreSQL. Le `CancellationToken.None` de
`SafeRollbackAsync`, le `catch (Exception)` large et l'ordre *rollback puis mapping* sont exactement ce
qu'il faut. **Aucune modification ne doit être inventée pour justifier un commit de code.**

Trois invariants sont désormais **mesurés** et doivent être protégés de toute régression future :

1. le rollback utilise `CancellationToken.None` — **pas** le jeton client ;
2. le rollback précède le mapping ;
3. le `catch` capture `Exception`, pas seulement les erreurs de persistance.

### 14.2 `UnitOfWork` — un changement **minimal**, ou aucun

Le défaut est **réel** (§9) mais **inatteignable** : aucun appelant de production. Deux options
défendables, à trancher par le pilote :

- **Option 1 (recommandée, à changement minimal) — aligner le rollback sur le runner.**
  Dans `RollbackTransactionAsync`, remplacer le jeton client par `CancellationToken.None` et envelopper
  l'appel en best-effort, comme `SafeRollbackAsync`. Corrige A, B et C d'un seul geste, sur **une seule
  ligne de comportement**. Le `RollbackAsync` public qui **n'annule rien** (§3.2) mérite au minimum
  d'être signalé dans le même lot.
- **Option 2 — ne rien changer** et consigner le défaut comme dette explicite, l'API n'ayant aucun
  appelant.

**Ce que P4-4B1 ne doit PAS faire :** modifier `EfTransactionRunner`, `PersistenceErrorMapper`,
`OrderRepository` ou les use cases Produit. Les mesures ne le justifient pas.

### 14.3 Changements **NON nécessaires** — établi par mesure

- Aucune politique de retry, aucun `EnableRetryOnFailure`.
- Aucun `lock_timeout`, `statement_timeout` ni `CommandTimeout` de production.
- Aucun `TransactionScope`, aucun changement de niveau d'isolation.
- Aucune remise en cause du classement `25P02 → Unknown` de P4-4A1 : `25P02` n'a **jamais** été
  observé sur un chemin de production pendant cette passe — le contrôle positif §4 prouve qu'il
  l'aurait été s'il s'était produit.
- Aucun `ChangeTracker.Clear()` après rollback : §8 montre qu'il n'est pas nécessaire.

---

## 15. Blocages P4-5

| Blocage | Effet sur P4-4B0 |
|---|---|
| `HasFilter("\"IsCurrent\" = 1")` — littéral booléen SQLite | Empêche la re-preuve runtime de `OrderRepository` (§10) |
| `HasFilter` `Notifications` (`"Type" = 'LowStock' AND …`) | À réévaluer en P4-5 ; hors périmètre ici |
| Mapping monétaire `REAL` | Non touché ; perte de valeur connue depuis P4-1 |
| Mapping `DateTime` (`Kind=Local` refusé par PostgreSQL) | Non touché ; constat P4-1 toujours ouvert |
| Absence de migration PostgreSQL | `EnsureCreated`/`Migrate` interdits et non appelés |

---

## 16. Limites de cette passe

1. **Aucun schéma de production n'est revendiqué.** Les tables `p4_tx_probe` et `"Suppliers"` sont des
   instruments du harness, créés par SQL brut dans des bases jetables.
2. La contrainte `UNIQUE` sur `"Suppliers"."Name"` **n'existe pas** dans le modèle MMV.
3. `OrderRepository` n'a **pas** été exercé au runtime (§10).
4. Le `ROLLBACK` n'est **pas** observable via `DbCommandInterceptor` (§7) ; la preuve vient des données.
5. Un seul serveur (PostgreSQL 17.10 conteneurisé), une seule plateforme, aucune mesure multi-poste
   réelle ni sous charge.
6. **Aucune conclusion de compatibilité complète.** MMV **ne tourne pas** sur PostgreSQL : le schéma
   n'est pas porté.
7. Ces tests appartiennent au harness de spike, **hors `MMV.sln`** : ils ne sont **pas** de la CI.

---

## 17. Aucun retry

**Aucune re-tentative n'a été introduite, mesurée comme souhaitable, ni recommandée.** Aucun
`EnableRetryOnFailure`, aucune boucle de retry, aucune stratégie d'exécution. Le constat P4-1 —
après une violation de contrainte, la transaction PostgreSQL est perdue et doit être rejouée depuis
le début par l'appelant — reste **entier et non traité**.

---

## 18. V1 MULTI-POSTE

**`V1 MULTI-POSTE = NOT GO`.**

P4-4B0 mesure la frontière transactionnelle ; elle ne rend MMV ni portable ni déployable en
multi-poste. Le schéma PostgreSQL n'est pas porté (P4-5), les migrations n'existent pas, et le
mapping `DateTime` comme le mapping monétaire restent ouverts.

---

## 19. Fichiers de ce lot

| Fichier | Nature |
|---|---|
| `spikes/P4.ProviderComparison/E21_PostgreSqlTransactionBehaviorTests.cs` | Nouveau — harness de spike, hors `MMV.sln` |
| `docs/implementation/P4-4B0-postgresql-transaction-behavior-report.md` | Nouveau — ce rapport |

**Aucun fichier sous `src/**` n'a été modifié. Aucun fichier sous `tests/**` n'a été modifié.**
Aucun paquet, aucune migration, aucun workflow, aucune roadmap. Aucun helper P4-4A0 modifié.

**Tests exécutés :** uniquement `E21` — 5/5 **PASSED**, 0 échec, 0 ignoré. Le harness complet n'a pas
été rejoué ; `MMV.sln` n'a pas été rejoué (aucun de ses fichiers n'a été touché). **Aucun de ces tests
n'est de la CI.**

---

## VERDICT

```
P4_4B0_TRANSACTION_MEASUREMENT = READY_FOR_B1_DECISION

EFTRANSACTIONRUNNER                    = SAFE_AS_IS
UNITOFWORK ROLLBACK TOKEN              = CHANGE_REQUIRED (latent, aucun appelant de production)
ORDERREPOSITORY CATCH                  = SAFE_AS_IS (statique) / BLOCKED_BY_P4_5_SCHEMA (runtime)
CREATE/UPDATE PRODUCT POST-ROLLBACK    = SAFE_AS_IS

P4-4B1 = NOT STARTED
P4-5   = NOT STARTED
V1 MULTI-POSTE = NOT GO
```
