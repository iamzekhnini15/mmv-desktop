# P4-4B1 — Rollback défensif de `UnitOfWork.CommitAsync`

**Statut : IMPLEMENTED LOCALLY — PENDING HUMAN REVIEW / COMMIT / CI**
Aucun `git add`, aucun commit, aucun push n'a été effectué dans ce lot.

---

## 1. Base Git

| Élément | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| HEAD local au démarrage | `d5a365669990d2d393421dcc88797805fc817f84` |
| `origin/p4-multi-poste` | `d5a365669990d2d393421dcc88797805fc817f84` |
| Commit de base (P4-4B0) | `d5a3656` — `test(P4-4B0): record PostgreSQL transaction behavior evidence` |
| CI de la base | `33908628731` — SUCCESS |
| Baseline tests | 1565 (Domain 706 · Application 620 · App 239), 0 échec, 0 ignoré |

Aucun fichier suivi n'était modifié, rien n'était indexé, seuls `design-handoff/`,
`design/`, `docs/ui/` étaient non suivis avant le lot.

---

## 2. Preuve P4-4B0 à l'origine du changement

Mesuré et enregistré en P4-4B0 (spike E21, PostgreSQL réel) :

- PostgreSQL abandonne la transaction après une erreur (`25P02`) tant qu'aucun rollback
  n'est effectué ;
- `EfTransactionRunner` de production : **SAFE_AS_IS** — il annule réellement, ne laisse
  aucune écriture partielle, préserve l'erreur initiale et utilise `CancellationToken.None`
  pour son rollback défensif ;
- `UnitOfWork` : un rollback exécuté avec le `cancellationToken` client **déjà annulé** a
  réellement **échoué avant d'effectuer le rollback** ; la transaction est restée active ;
  l'exception du rollback pouvait **masquer l'exception initiale**.

Verdict B0 retenu : `UNITOFWORK ROLLBACK TOKEN = CHANGE_REQUIRED`
(surface actuellement **sans appelant de production** pour ses méthodes transactionnelles
explicites — défaut réel mais latent).

---

## 3. Structure réellement observée avant modification

Fichier : `src/MMV.Infrastructure/Repositories/UnitOfWork.cs`

| Élément | Constat |
|---|---|
| Champ de transaction | `private IDbContextTransaction? _transaction;` |
| `BeginTransactionAsync` | `_transaction = await _context.Database.BeginTransactionAsync(cancellationToken);` |
| `CommitAsync` | `SaveChangesAsync(ct)` puis `_transaction.CommitAsync(ct)` + `DisposeAsync()` |
| `catch` de `CommitAsync` | `await RollbackTransactionAsync(cancellationToken); throw;` |
| Rollback défensif (privé) | `RollbackTransactionAsync` → `await _transaction.RollbackAsync(cancellationToken);` |
| `RollbackAsync` public | ne touche pas la transaction : `await _context.DisposeAsync();` |
| `DisposeAsync` | libère la transaction puis le contexte |

Appelants de production des méthodes transactionnelles explicites
(`BeginTransactionAsync` / `CommitAsync` / `RollbackAsync` de `UnitOfWork`) dans `src/**` :
**aucun**. Les seules occurrences de `IUnitOfWork.CommitAsync` sous `src/MMV.Application`
sont des commentaires XML de documentation, pas des appels.

La structure observée correspond exactement au défaut décrit en P4-4B0.

---

## 4. Défaut exact et correction exacte

**Défaut** — dans le `catch` de `CommitAsync` :

```csharp
await RollbackTransactionAsync(cancellationToken);   // → _transaction.RollbackAsync(cancellationToken)
```

Un jeton client déjà annulé fait échouer le rollback **avant** son exécution, laisse la
transaction active et remplace l'exception initiale par une `OperationCanceledException`.

**Correction** :

```csharp
await RollbackTransactionAsync(CancellationToken.None);
```

Le jeton d'annulation client n'atteint plus le rollback défensif. C'est exactement la
garantie déjà prouvée SAFE_AS_IS dans `EfTransactionRunner.SafeRollbackAsync`
(`transaction.RollbackAsync(CancellationToken.None)`).

---

## 5. Nombre exact de lignes fonctionnelles modifiées

**1 ligne fonctionnelle** (`UnitOfWork.cs`, l'appel du rollback défensif dans le `catch`).

Diff total du fichier de production : `4 insertions(+), 1 deletion(-)`
= 1 ligne fonctionnelle remplacée + 3 lignes de commentaire explicatif.

Aucune autre logique n'a changé : `BeginTransactionAsync`, `SaveChangesAsync`, le commit,
le `RollbackAsync` public, `RollbackTransactionAsync` lui-même, `Dispose`/`DisposeAsync`
et l'API publique sont inchangés. Aucune exception n'est avalée, mappée ou retypée ;
aucun `catch` supplémentaire ; aucun `SafeRollbackAsync` ajouté.

---

## 6. Tests ajoutés

Aucun fichier de tests dédié à `UnitOfWork` n'existait sous `tests/**`.
**Un seul** fichier a été créé : `tests/MMV.Domain.Tests/Repositories/UnitOfWorkTests.cs`
(xunit + FluentAssertions 6.12 déjà présents ; **aucun nouveau package**).

Doubles de test locaux (aucun serveur, aucune connexion ouverte) :
une transaction enregistreuse `IDbContextTransaction` qui mémorise les jetons reçus et
reproduit le comportement réel mesuré en B0 (un jeton déjà annulé fait échouer
`RollbackAsync` avant l'annulation), une `DatabaseFacade` qui la fournit, et un
`OpticDbContext` dérivé dont `SaveChangesAsync` est pilotable.

| Test | Objet | Résultat |
|---|---|---|
| B1-1 `CommitAsync_WhenClientTokenAlreadyCancelled_RollsBackWithNonCancellableToken` | jeton client déjà annulé → le rollback est tout de même exécuté, **exactement une fois**, avec un jeton `IsCancellationRequested == false` et égal à `CancellationToken.None` ; transaction libérée | PASS |
| B1-2 `CommitAsync_WhenClientTokenAlreadyCancelled_PreservesOriginalException` | l'exception finale est **la même instance** que l'exception sentinelle initiale, et non une `OperationCanceledException` issue du rollback | PASS |
| B1-3 `CommitAsync_WhenClientTokenNotCancelled_RollsBackWithTokenIndependentOfClient` | jeton client normal → le rollback reçoit un jeton `CanBeCanceled == false`, donc indépendant du jeton client (empêche la réintroduction de `RollbackAsync(cancellationToken)`) | PASS |
| B1-4 `CommitAsync_WhenSaveSucceeds_CommitsAndDoesNotRollBack` | commit réussi → 1 commit, 0 rollback | PASS |

### Preuve que les tests verrouillent bien le correctif

Le défaut a été **temporairement réintroduit** en local
(`RollbackTransactionAsync(CancellationToken.None)` → `RollbackTransactionAsync(cancellationToken)`),
la solution reconstruite et le fichier de tests réexécuté :

```
Failed!  - Failed: 3, Passed: 1, Skipped: 0, Total: 4 - MMV.Domain.Tests.dll (net8.0)
```

B1-1, B1-2 et B1-3 échouent avec le défaut ; B1-4 (commit réussi) reste vert, comme attendu.
Le correctif a ensuite été restauré et la solution entièrement revalidée.

---

## 7. Total des tests avant / après

| | Domain | Application | App | Total |
|---|---|---|---|---|
| Avant (baseline `d5a3656`) | 706 | 620 | 239 | **1565** |
| Après (P4-4B1 local) | 710 | 620 | 239 | **1569** |

**+4 nouveaux tests**, 0 échec, 0 ignoré.

---

## 8. Validations exécutées

| Contrôle | Commande | Résultat |
|---|---|---|
| Restore | `dotnet restore MMV.sln` | succès |
| Build | `dotnet build MMV.sln -c Debug --no-restore` | **0 erreur, 0 avertissement** |
| Tests | `dotnet test MMV.sln -c Debug --no-build` | **1569 passés, 0 échec, 0 ignoré** |
| Outils | `dotnet tool restore` | `dotnet-ef` 8.0.27 restauré |
| Modèle EF | `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | `No changes have been made to the model since the last migration.` |
| Migrations | — | **aucune migration ajoutée/modifiée** |
| Audit | `dotnet list MMV.sln package --vulnerable --include-transitive` | aucune vulnérabilité sur les 7 projets |
| Hygiène diff | `git diff --check` | propre (exit 0) |
| Index | `git diff --cached --name-status` | vide (rien d'indexé) |

---

## 9. Surfaces explicitement NON modifiées

- `src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs` — lu en référence uniquement
  (précédent SAFE_AS_IS), **inchangé** ;
- `src/MMV.Infrastructure/Repositories/OrderRepository.cs` — **inchangé** ;
- `CreateProductUseCase` — **inchangé** ;
- `UpdateProductUseCase` — **inchangé** ;
- `PersistenceErrorMapper` — **inchangé** ;
- `UnitOfWork.RollbackAsync` **public** — **inchangé** ;
- `src/MMV.Domain/**` et `src/MMV.Application/**` — **aucun changement** ;
- aucun nouveau test serveur PostgreSQL : E21 reste la preuve serveur.

---

## 10. Dette restante

Le `RollbackAsync` **public** de `UnitOfWork` se contente de disposer le `DbContext` ; il
n'annule pas la transaction courante. Ce comportement est **connu et non corrigé dans
P4-4B1** : il est hors périmètre de ce lot, et cette surface reste **sans appelant de
production** (`EfTransactionRunner` a repris le flux transactionnel protégé, cf. R-23).
Cette dette est enregistrée ici, non traitée.

De même, `RollbackTransactionAsync` conserve son paramètre `CancellationToken` (valeur par
défaut) ; son unique appelant, le `catch` de `CommitAsync`, lui passe désormais
`CancellationToken.None`.

---

## 11. Aucune nouvelle politique

Recherche effectuée sur l'intégralité du change-set (diff de production + fichier de tests) :

`EnableRetryOnFailure`, `retry`/`Retry`, `lock_timeout`, `statement_timeout`,
`CommandTimeout`, `TransactionScope`, `IsolationLevel`, `Migrate`, `EnsureCreated`,
`DateTime`, `HasColumnType`, `HasFilter` → **aucune occurrence**.

Aucun retry, aucun timeout de production, aucun changement d'isolation, aucun mapping,
aucune primitive métier, aucun secret.

---

## 12. Distinction des preuves

| Preuve | Nature |
|---|---|
| E21 (`spikes/P4.ProviderComparison/E21_PostgreSqlTransactionBehaviorTests.cs`) | **EXECUTED_SPIKE LOCAL** — serveur PostgreSQL réel, hors `MMV.sln`, non exécuté en CI |
| Tests B1 (`tests/MMV.Domain.Tests/Repositories/UnitOfWorkTests.cs`) | **MMV.sln sans serveur** — doubles de test, exécutables en CI |

Les tests B1 ne rejouent pas la preuve serveur : ils verrouillent le contrat de jeton du
rollback défensif, dont la nécessité a été établie par E21.

---

## 13. Limites

- P4-4B1 **ne re-prouve pas** `OrderRepository` sur PostgreSQL ; le `catch` interne de
  `OrderRepository` reste **SAFE_AS_IS statique / RUNTIME BLOCKED BY P4-5**.
- P4-4B1 corrige **uniquement** le risque mesuré (jeton client déjà annulé → rollback
  empêché → exception initiale masquée). Il ne prétend pas traiter tout échec de rollback
  imaginable (perte de connexion, transaction déjà avortée côté serveur, etc.).
- Le défaut corrigé est **latent** : aucune méthode transactionnelle explicite de
  `UnitOfWork` n'a d'appelant de production aujourd'hui.

---

## 14. État

| Jalon | État |
|---|---|
| P4-4A0 | RECORDED |
| P4-4A1 | RECORDED |
| P4-4B0 | RECORDED |
| **P4-4B1** | **IMPLEMENTED LOCALLY — PENDING HUMAN REVIEW / COMMIT / CI** |
| P4-4 | **NOT CLOSE** |
| P4-5 | **NOT STARTED** |
| V1 MULTI-POSTE | **NOT GO** |

La roadmap n'a pas été modifiée par ce lot.

---

## 15. Périmètre du change-set

```
M  src/MMV.Infrastructure/Repositories/UnitOfWork.cs
A  tests/MMV.Domain.Tests/Repositories/UnitOfWorkTests.cs
A  docs/implementation/P4-4B1-unitofwork-defensive-rollback-report.md
```

3 fichiers (1 M + 2 A). Aucun autre fichier suivi n'est touché ; en dehors du change-set,
seuls `design-handoff/`, `design/` et `docs/ui/` restent non suivis. Rien n'est indexé.

**VERDICT : `P4_4B1_IMPLEMENTATION_LOCAL = READY_FOR_HUMAN_REVIEW`**
