# P3-9 — Fournisseurs — Rapport d'implémentation backend

> **MODE = IMPLEMENT_TEST_AND_WRITE_REPORT_NO_COMMIT** — aucun commit, aucun push, aucune migration, aucune UI.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | P3-9 |
| Périmètre | Backend uniquement (Domain / Application / Infrastructure) |
| HEAD de départ | `b30f7748cf6d3eb69cc88c4a6457dea170be4e8a` |
| Run CI de départ | `29783755704` |
| Tests baseline | 1202 |
| **Tests après P3-9** | **1302** |

---

## 2. État Git et CI de départ

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → b30f7748cf6d3eb69cc88c4a6457dea170be4e8a
git rev-parse origin/…      → b30f7748cf6d3eb69cc88c4a6457dea170be4e8a
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
                              ?? docs/implementation/P3-9-suppliers-business-rules-audit-report.md
git diff --check            → (vide)
```

```json
{"databaseId":29783755704,"headSha":"b30f7748cf6d3eb69cc88c4a6457dea170be4e8a",
 "headBranch":"p3-business-rules","event":"push","status":"completed","conclusion":"success"}
```

✅ Branche conforme. ✅ HEAD local = HEAD distant = SHA attendu. ✅ CI du SHA exact `push / completed / success`.
✅ Aucun fichier suivi modifié. ✅ Seul changement P3-9 présent : le rapport d'audit. ✅ Aucun fichier sous
`src/MMV.App/**`.

**Porte Git/CI : PASSÉE.**

---

## 3. Baseline

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore` | OK | OK | ✅ |
| `dotnet build -c Debug` | 0 erreur / 0 avertissement | 0 / 0 | ✅ |
| `MMV.Domain.Tests` | 514 | 514 | ✅ |
| `MMV.Application.Tests` | 449 | 449 | ✅ |
| `MMV.App.Tests` | 239 | 239 | ✅ |
| **Total** | **1202** | **1202** | ✅ |
| Échecs / ignorés | 0 / 0 | 0 / 0 | ✅ |
| Vulnérabilités (incl. transitives) | 0 | 0 sur les 7 projets | ✅ |
| Migrations en attente | 0 | « No changes have been made to the model since the last migration. » | ✅ |
| Références `MMV.Application` | Domain uniquement | `..\MMV.Domain\MMV.Domain.csproj` | ✅ |

**Baseline : CONFORME.**

---

## 4. Décisions finales

| # | Décision | Motif |
|---|---|---|
| 1 | **Option A** — refus de suppression + validation, sans migration | La seule relation du fournisseur est `Product`, déjà protégée par une FK `RESTRICT` effective |
| 2 | **Aucun `IsActive` / `IsArchived`** | Aucun besoin métier prouvé ; coût réel = colonne + migration + backfill + garde produit + filtrage + tests |
| 3 | **Aucune unicité** (nom, e-mail, téléphone, `ReferenceCode`) | Serait **inventée** ; bloquerait des cas légitimes (agences d'un groupe, standard partagé) |
| 4 | **Aucune migration** | Rien n'est ajouté au schéma ; `has-pending-model-changes` reste vide |
| 5 | **Normalisation à propriétaire unique**, partagée par Create et Update | Create et Update ne peuvent pas diverger |
| 6 | **Validation dans le Domain**, appliquée par l'Application avant écriture | Convention P3-1 ; la règle reste propriétaire du Domain |
| 7 | **Suppression conditionnelle atomique** | Ferme le TOCTOU au lieu de le documenter |
| 8 | **FK `RESTRICT` conservée** comme filet ultime | Protège contre les chemins alternatifs et les régressions de configuration |
| 9 | **Fournisseur obligatoire et existant** dans les écritures produit | Moitié « produits » de l'objectif d'intégrité |
| 10 | **Lost update non traité** | Dette transverse à toutes les entités ; le traiter ici serait incohérent |
| 11 | **Code mort conservé** | Documenté comme dette, mais sans rapport avec le défaut corrigé |
| 12 | **Aucune UI** | Interdiction absolue en P3-9 |

---

## 5. Modèle `Supplier`

**Inchangé.** Aucune propriété ajoutée, retirée ou modifiée ; aucun setter restreint ; aucune méthode ajoutée.

```
Suppliers(SupplierId INTEGER PK AUTOINCREMENT, Name TEXT NOT NULL,
          ContactEmail TEXT NULL, Phone TEXT NULL, Address TEXT NULL, ReferenceCode TEXT NULL)
```

Le point établi par l'audit reste vrai et fonde la conception : **`HasMaxLength(n)` n'impose rien en SQLite**. La
colonne générée est `TEXT`, sans `CHECK` ni `VARCHAR(n)`. Les longueurs déclarées n'étaient que de la
documentation ; les `MaximumLength` du validateur les rendent **réellement effectives pour la première fois**,
côté Application — plus strictes que la base, jamais plus permissives, donc compatibles avec un futur provider
serveur.

L'entité reste volontairement anémique : P3-9 place la règle dans un validateur, comme `Customer` et `Product`,
plutôt que d'introduire un style d'entité que le dépôt n'emploie pas.

---

## 6. Normalisation

`src/MMV.Application/UseCases/Suppliers/Common/SupplierInputNormalizer.cs` — composant **pur**, sans persistance,
sans état, sans dépendance EF/SQLite.

| Champ | Traitement | Nullable après |
|---|---|---|
| `Name` | `Trim()` ; `null` → `""` | non (jamais `null`) |
| `ContactEmail` | `Trim()` ; vide ou blanc → `null` | oui |
| `Phone` | `Trim()` ; vide ou blanc → `null` | oui |
| `Address` | `Trim()` ; vide ou blanc → `null` | oui |
| `ReferenceCode` | `Trim()` ; vide ou blanc → `null` | oui |

**Ce que la normalisation ne fait pas**, délibérément : aucun changement de casse (ni e-mail, ni `ReferenceCode`),
aucun reformatage de numéro de téléphone, aucune comparaison de doublon. Chacun supposerait une règle métier que le
dépôt ne prouve nulle part et modifierait une valeur saisie intentionnellement.

`CreateSupplierUseCase` et `UpdateSupplierUseCase` appellent **littéralement la même fonction** : la divergence est
structurellement impossible, et `SupplierInputNormalizerTests` le verrouille.

### Correction apportée à l'audit

L'audit §9.5 justifiait le `Trim` en affirmant que `" a@b.fr "` **échouerait** `EmailAddress()`. **C'est faux** :
FluentValidation 11 utilise `EmailValidationMode.AspNetCoreCompatible`, qui tolère les espaces environnants. Le
test écrit pour prouver l'affirmation l'a **réfutée**, et a été retourné en test de la réalité. La normalisation
reste retenue — pour ses raisons réelles : ne pas persister d'espaces parasites, unifier `""` et `NULL`, et mesurer
les longueurs sur le contenu réel. L'argument « sinon la saisie serait bloquée » est retiré.

---

## 7. Validation

`src/MMV.Domain/Validators/SupplierValidator.cs` — calqué sur `CustomerValidator`, appliqué via
`CommandValidation` (socle P3-1). Aucune regex maison, aucun paquet ajouté.

| Champ | Règle | Message stable (constante publique) |
|---|---|---|
| `Name` | `NotEmpty` | `NameRequiredMessage` — « Le nom du fournisseur est obligatoire. » |
| `Name` | `MaximumLength(200)` | `NameTooLongMessage` |
| `ContactEmail` | `EmailAddress()` si renseigné | `EmailInvalidMessage` |
| `ContactEmail` | `MaximumLength(254)` | `EmailTooLongMessage` |
| `Phone` | `MaximumLength(20)` | `PhoneTooLongMessage` |
| `Address` | `MaximumLength(500)` | `AddressTooLongMessage` |
| `ReferenceCode` | `MaximumLength(50)` | `ReferenceCodeTooLongMessage` |

**Portée réelle d'`EmailAddress()`, énoncée sans la surestimer.** Le mode `AspNetCoreCompatible` se réduit à
« exactement un `@`, ni en première ni en dernière position ». Il attrape les fautes grossières (`"pas-un-email"`)
mais accepte `"a@b"`. C'est **exactement** le niveau de garantie déjà accepté pour `Customer` ; prétendre davantage
serait mentir sur la protection obtenue. Verrouillé par un test dédié.

**Aucune unicité, aucune normalisation de casse** — verrouillé par deux tests qui prouvent que deux fournisseurs
strictement identiques restent tous deux valides.

---

## 8. `CreateSupplierUseCase`

Flux : garde `command != null` → normalisation → **candidat détaché** → validation → si invalide, retour avec
`ValidationErrors` et **aucune** écriture → sinon `CreateAsync` + `SaveChangesAsync`.

`CreateSupplierResult` étendu **additivement** : `ValidationErrors` + `IsValid` (convention P3-1, identique à
`CreateProductResult`). `SupplierId` reste à 0 en cas de refus.

Une saisie invalide est une **erreur de saisie**, renvoyée dans le résultat — jamais une `BusinessRuleException`.
`ITransactionRunner` n'est pas ajouté : le flux reste mono-écriture, donc intrinsèquement atomique, et aucun besoin
de traduction n'existe ici (cohérent avec `CreateCustomerUseCase`).

---

## 9. `UpdateSupplierUseCase`

Flux : garde `command != null` → normalisation → chargement → si introuvable, `SupplierFound = false` →
**validation d'un candidat détaché** → si invalide, retour avec `ValidationErrors` et **aucune entité suivie
mutée** → sinon `ApplyTo` + `UpdateAsync` + `SaveChangesAsync`.

`UpdateSupplierResult` étendu additivement de la même façon. Les deux axes restent lisibles séparément :
`SupplierFound` (existence) et `IsValid` (saisie).

**Écart assumé avec l'ordre littéral de l'énoncé.** L'énoncé §8 plaçait la validation *avant* le chargement.
L'implémentation charge d'abord, puis valide un candidat **détaché**. L'invariant exigé — « aucune entité suivie
modifiée avant validation réussie » — est intégralement respecté, et il l'est **structurellement** : les valeurs
candidates ne peuvent pas atteindre le change tracker avant d'avoir été acceptées. Valider d'abord aurait en
revanche obligé à renvoyer un `SupplierFound` affirmant une existence **jamais vérifiée** — une information
fausse. L'ordre retenu est aussi celui d'`UpdateProductUseCase`. Test dédié :
`Update_CommandeInvalide_NeModifieAucuneValeurExistante`, qui déclenche même un `SaveChangesAsync` opportuniste
sur la portée pour prouver qu'il ne persiste rien.

**Aucun patch partiel** : les cinq champs sont remplacés par la commande complète ; les champs optionnels vidés
deviennent `null`. Comportement historique conservé sciemment. **Le lost update n'est pas traité** et le rapport ne
prétend pas le contraire.

---

## 10. Suppression atomique

### Primitive

```csharp
// ISupplierRepository — port provider-neutre
Task<bool> TryDeleteIfUnusedAsync(long supplierId, CancellationToken cancellationToken = default);
Task<bool> ExistsFreshAsync(long supplierId, CancellationToken cancellationToken = default);
```

Implémentation (`SupplierRepository`) :

```csharp
var rowsAffected = await _context.Suppliers
    .Where(s => s.SupplierId == supplierId && !s.Products.Any())
    .ExecuteDeleteAsync(cancellationToken);
return rowsAffected == 1;
```

EF traduit en `DELETE FROM "Suppliers" WHERE "SupplierId" = @p AND NOT EXISTS (SELECT 1 FROM "Products" …)`.
**Une seule instruction** : la condition et l'écriture sont évaluées ensemble. Aucun `Thread.Sleep`, aucun SQL
provider-spécifique n'a été nécessaire — la traduction EF fonctionne et est prouvée par des tests directs de la
primitive.

`s.Products` **n'applique aucun filtre `IsActive`** : les produits inactifs comptent, puisqu'ils conservent leur
ligne et donc leur clé étrangère. `ExecuteDeleteAsync` contourne le change tracker : la décision porte sur l'état
réel de la base, jamais sur une navigation déjà chargée.

### Use case

```
1. garde command != null
2. TryDeleteIfUnusedAsync, enveloppé dans ITransactionRunner
3. true  → SupplierFound = true
4. false → lecture fraîche de DIAGNOSTIC :
           absent  → SupplierFound = false
           présent → BusinessRuleException(SupplierHasProductsMessage)
```

**La lecture ne décide jamais de l'écriture** : elle intervient après que la suppression a été tranchée et close,
et sert uniquement à départager les deux causes possibles d'un `false`.

`ExistsFreshAsync` — et non l'`ExistsAsync` hérité — parce que ce dernier repose sur `FindAsync`, qui sert
l'entité **suivie** sans requêter la base. Après une suppression concurrente, il répondrait « existe » à tort. La
distinction est prouvée par `ExistsFresh_LitLaBase_EtNonLeChangeTracker`, qui montre les deux méthodes répondant
**différemment** sur le même identifiant.

### Frontière transactionnelle

`ITransactionRunner` est injecté non pour rendre l'instruction atomique — elle l'est déjà — mais parce qu'il est
le **seul** mécanisme du dépôt qui traduit une erreur de persistance en `PersistenceException` neutre via
`PersistenceErrorMapper`. `IUnitOfWork` a été retiré du constructeur : `ExecuteDeleteAsync` écrit directement,
aucun `SaveChangesAsync` n'est requis. Le constructeur n'injecte donc que ce qui est réellement nécessaire.

Message stable :

```csharp
public const string SupplierHasProductsMessage =
    "Ce fournisseur ne peut pas être supprimé car il est associé à un ou plusieurs produits.";
```

---

## 11. Relation `Supplier` / `Product`

`SupplierConfiguration` portait :

```csharp
builder.HasMany(s => s.Products).WithOne(p => p.Supplier)
    .HasForeignKey(p => p.SupplierId)
    .OnDelete(DeleteBehavior.SetNull);   // ← retiré en P3-9
```

Ce `SetNull` **n'a jamais eu d'effet** : le modèle effectif porte `Restrict`, configuré par `ProductConfiguration`
(`IsRequired()` + `OnDelete(Restrict)`). Il était de surcroît **inapplicable**, `SetNull` exigeant une FK nullable
alors que `Product.SupplierId` est un `long` non nullable. Code trompeur retiré ; la relation reste configurée
**d'un seul côté**.

**Vérification exigée** : `dotnet ef migrations has-pending-model-changes` → *« No changes have been made to the
model since the last migration. »* Le retrait ne produit **aucun** changement de modèle, ce qui confirme
empiriquement qu'il était sans effet. Aucune migration créée, aucun snapshot modifié.

Trois gardes verrouillent l'état final : `DeleteBehavior == Restrict` + `IsRequired` + FK de type `long` ; aucun
`SetNull` ni `Cascade` ; **une seule** FK `Product → Supplier` dans le modèle.

---

## 12. `CreateProductUseCase`

`SupplierId ?? 0` **supprimé**. Nouveau flux :

1. `command.SupplierId is not > 0` → `ValidationError(SupplierRequiredMessage)`, aucune écriture, **jamais de 0** ;
2. construction du produit avec la valeur validée ;
3. validation `ProductValidator` et unicité de référence — **inchangées** ;
4. **dans** le `ITransactionRunner` existant : `ExistsFreshAsync` → si absent, sortie par drapeau,
   `ValidationError(SupplierNotFoundMessage)`, transaction validée à vide (aucun produit persisté) ;
5. sinon `CreateAsync` + `SaveChangesAsync`.

Le `?? 0` transformait une donnée **manquante** en violation d'intégrité : un produit sans fournisseur écrivait un
identifiant qu'aucune ligne ne porte, et l'utilisateur recevait une `PersistenceException` au lieu d'un message de
saisie. C'était le symétrique exact, côté produit, du défaut de suppression.

---

## 13. `UpdateProductUseCase`

Mêmes règles, avec deux précisions :

- le produit est chargé **d'abord** : « produit introuvable » conserve son propre axe (`ProductFound = false`) et
  prime sur la garde fournisseur ;
- la vérification a lieu **même si l'identifiant est identique au fournisseur actuel** — le fournisseur doit
  exister *au moment de la sauvegarde*, pas au moment où le formulaire a été ouvert. Prouvé par un repository
  compteur : la garde est consultée (`ExistsCallCount == 1`) y compris pour un fournisseur inchangé.

**Aucune autre règle P3-4 / P3-5 n'est modifiée** : l'exclusion de `StockQuantity` de l'`UPDATE` catalogue,
l'unicité normalisée de la référence et la cohérence catégorie/détails sont intactes. L'édition d'un produit
**inactif** reste possible.

---

## 14. Concurrence

| Acte | Protection | Résiduel |
|---|---|---|
| Supprimer un fournisseur | **Primitive atomique** — condition et écriture indissociables | aucun |
| Supprimer vs créer un produit | Primitive atomique **+** FK `RESTRICT` | aucun ; les deux ordres testés |
| Supprimer vs modifier un produit | idem | aucun |
| Double suppression | Idempotent — 2ᵉ appel ⇒ `SupplierFound = false`, aucune exception | aucun |
| Créer/modifier un produit vs supprimer son fournisseur | Garde **dans** la transaction **+** FK, traduite | aucun ; erreur métier stable |
| Modifier un fournisseur concurremment | **aucune** | 🟠 **lost update assumé, non traité** (R11) |
| Créer deux fournisseurs identiques | **aucune** (voulu) | doublon possible — conséquence de l'absence d'unicité, décidée |

### Protocole de test — énoncé honnêtement

**SQLite sérialise les écritures** : deux transactions ne s'entrelacent pas réellement, et une course temporelle
serait à la fois fragile et non probante. **Aucun `Thread.Sleep`, aucun thread concurrent** n'est utilisé. Les
invariants sont établis par deux moyens déterministes :

1. **État reconstruit** — on met la base dans l'état exact que la course produirait, puis on agit. Ainsi
   `Course_ProduitCreeApresObservationDAbsence_LaTentativeAtomiqueEchoue` observe d'abord l'absence de produit
   (ce que ferait une garde « check-then-act »), laisse un produit être créé, puis tente la suppression : la
   primitive **refuse**, là où le couple `HasProducts` + `Delete` aurait supprimé.
2. **Résultat périmé injecté** — un décorateur de port renvoie un `true` d'existence périmé au premier appel, puis
   la vérité ensuite : exactement la chronologie « garde exécutée avant la disparition, diagnostic après ». La FK
   arbitre, l'erreur est traduite, aucune écriture partielle ne subsiste.

Ce que ces tests **ne** prouvent pas, et qui n'est pas revendiqué : le comportement sous entrelacement réel d'un
provider serveur. Renvoyé au chantier production (`adr-prod-db-001`).

---

## 15. Persistance

| Vérification | Résultat |
|---|---|
| FK `Products.SupplierId → Suppliers.SupplierId` | présente, **non nullable**, `RESTRICT` |
| `PRAGMA foreign_keys` | `1` — les FK sont réellement appliquées |
| Suppression forcée **en SQL brut**, hors EF et hors use case, avec produit lié | **refusée** — `SqliteException` code 19 ; fournisseur et produit conservés |
| Détachement d'une relation requise par EF | **refusé** — `InvalidOperationException` ; aucune mise à null silencieuse |
| Création d'un produit vers un fournisseur inexistant | **refusée** — aucun orphelin possible |
| Index unique sur `Suppliers` | **aucun** (aucune unicité inventée) |
| Migrations en attente | **aucune** |

Le premier test mérite un mot : passer par le `DbContext` ne suffisait pas à prouver la protection **de la base**,
car EF intercepte en amont. La preuve est donc faite en **SQL brut**, qui contourne totalement le change tracker,
la configuration de modèle et la couche Application. Seule la contrainte réellement présente dans le fichier SQLite
peut encore refuser — et elle refuse.

Fait établi en écrivant les tests : **l'état « un produit référence un fournisseur inexistant » est
infabricable**, même en contournant l'Application. L'audit le décrivait comme un risque théorique ; il est en
réalité impossible.

---

## 16. Code mort maintenu

Conservés **en l'état**, conformément à l'énoncé §16 : `ISupplierRepository.GetByNameAsync`,
`ISupplierRepository.GetByReferenceCodeAsync`, `BaseRepository.DeleteAsync(TId)`. Ils constituent une dette
documentée (🟡-8, 🟠-5), mais ne causent pas le défaut P3-9 ; les retirer élargirait le périmètre sans rien
sécuriser. Un test verrouille explicitement leur **présence**, pour que leur éventuel retrait soit une décision
consciente et non un effet de bord.

La FK reste le filet contre le chemin alternatif que `BaseRepository.DeleteAsync(TId)` représente.

---

## 17. Fichiers créés / modifiés

### Domain (2)

| Fichier | Nature |
|---|---|
| `src/MMV.Domain/Validators/SupplierValidator.cs` | **créé** |
| `src/MMV.Domain/Interfaces/Repositories/ISupplierRepository.cs` | modifié — 2 primitives ajoutées |

### Application (8)

| Fichier | Nature |
|---|---|
| `src/MMV.Application/UseCases/Suppliers/Common/SupplierInputNormalizer.cs` | **créé** |
| `.../Suppliers/CreateSupplier/CreateSupplierUseCase.cs` | modifié — normalisation + validation |
| `.../Suppliers/CreateSupplier/CreateSupplierResult.cs` | modifié — `ValidationErrors` / `IsValid` |
| `.../Suppliers/UpdateSupplier/UpdateSupplierUseCase.cs` | modifié — normalisation + validation |
| `.../Suppliers/UpdateSupplier/UpdateSupplierResult.cs` | modifié — `ValidationErrors` / `IsValid` |
| `.../Suppliers/DeleteSupplier/DeleteSupplierUseCase.cs` | modifié — suppression atomique + message stable |
| `.../Products/CreateProduct/CreateProductUseCase.cs` | modifié — garde fournisseur, `?? 0` supprimé |
| `.../Products/UpdateProduct/UpdateProductUseCase.cs` | modifié — garde fournisseur, `?? 0` supprimé |

### Infrastructure (2)

| Fichier | Nature |
|---|---|
| `src/MMV.Infrastructure/Repositories/SupplierRepository.cs` | modifié — 2 primitives implémentées ; détachement ciblé ajouté en revue avant commit |
| `src/MMV.Infrastructure/Data/Configurations/SupplierConfiguration.cs` | modifié — `SetNull` trompeur retiré |

### Tests (9)

| Fichier | Nature |
|---|---|
| `tests/MMV.Domain.Tests/ValidatorTests/SupplierValidatorTests.cs` | **créé** — 23 |
| `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierInputNormalizerTests.cs` | **créé** — 14 |
| `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierBusinessRulesTests.cs` | **créé** — 29 |
| `tests/MMV.Application.Tests/UseCases/Products/ProductSupplierGuardTests.cs` | **créé** — 18 |
| `tests/MMV.Application.Tests/Architecture/SuppliersApplicationArchitectureTests.cs` | **créé** — 16 |
| `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierDeletionSqlAndTrackerTests.cs` | **créé (revue ciblée)** — 6 |
| `tests/MMV.Application.Tests/UseCases/Suppliers/SupplierUseCasesTests.cs` | modifié — constructeur `DeleteSupplierUseCase` |
| `tests/MMV.Application.Tests/UseCases/Products/ProductUseCasesTests.cs` | modifié — constructeurs produit |
| `tests/MMV.Application.Tests/UseCases/Products/ProductBusinessRulesTests.cs` | modifié — constructeurs produit |

### Documentation (3)

`docs/implementation/P3-9-suppliers-business-rules-audit-report.md` (section « Décisions retenues »),
`docs/implementation/P3-9-suppliers-business-rules-implementation-report.md` (ce fichier),
`docs/architecture/P3-business-rules-roadmap.md`.

**Aucune migration. Aucun snapshot. Aucun `SqliteSchemaVerifier`. Aucun `SqliteDatabaseManager`. Aucun fichier
sous `src/MMV.App/**`.**

---

## 18. Tests Domain — `SupplierValidatorTests` (23)

Nominal ; nom vide ; nom en espaces seuls après normalisation ; nom 200 accepté / 201 refusé ; e-mail `null`
accepté ; e-mail vide traité comme absent ; e-mail valide accepté ; e-mail invalide refusé ; e-mail 254 accepté /
255 refusé ; téléphone 20 / 21 ; adresse 500 / 501 ; référence 50 / 51 ; tous champs optionnels nuls acceptés ;
aucun format de téléphone imposé ; **aucune unicité** ; **aucune normalisation de casse** ; portée réelle
d'`EmailAddress()` énoncée ; e-mail entouré d'espaces accepté avant comme après normalisation (**test correctif**).

---

## 19. Tests Application — Fournisseur (43)

`SupplierInputNormalizerTests` (14) : `Trim` du nom ; nom `null` → `""` ; espaces seuls → `""` ; quatre champs
optionnels vides/blancs/`null`/tabulation → `null` ; champs renseignés trimés ; **aucune casse modifiée** ; aucun
téléphone reformaté ; candidat détaché correct ; `ApplyTo` remplace les cinq champs (aucun patch partiel) ; clé
jamais touchée ; entité nulle rejetée ; **Create et Update produisent des valeurs identiques**.

`SupplierBusinessRulesTests` (29), **vraies bases SQLite** :

- **Create** — valeurs normalisées persistées ; optionnels vides persistés `null` ; nom vide et nom en espaces
  refusés sans aucune écriture ; e-mail invalide refusé ; champ trop long refusé **côté Application** (la base ne
  l'aurait pas refusé) ; résultat P3-1 cohérent ; **aucune unicité imposée**.
- **Update** — valeurs normalisées persistées ; optionnels vidés → `null` ; commande invalide ne modifie **aucune**
  valeur existante, même après un `SaveChangesAsync` opportuniste ; fournisseur introuvable → `SupplierFound =
  false` sur son propre axe.
- **Delete** — sans produit : supprimé ; introuvable : `SupplierFound = false` ; **avec produit actif** et **avec
  produit inactif** : même message métier stable, fournisseur **et** produit conservés ; aucun message provider
  exposé (`SQLITE`, `constraint`, `entity changes`, `inner exception` absents) ; **le `catch` UI existant recevrait
  le message métier** ; rejeu idempotent ; suppression redevient possible après retrait du produit ; commande nulle
  et dépendances nulles rejetées.
- **Primitive** — `true` + ligne supprimée sans produit ; `false` avec produit actif **et** inactif ; `false` si
  absent ; aucun fournisseur voisin touché ; `ExistsAsync` vs `ExistsFreshAsync` **répondent différemment** sur le
  même identifiant.

---

## 20. Tests Application — Produit (18)

`ProductSupplierGuardTests`, **vraies bases SQLite** :

- **CreateProduct** — `SupplierId` `null`, `0` et négatif refusés avant écriture ; **aucun `SupplierId = 0` jamais
  écrit** ; identifiant inexistant refusé **sans `PersistenceException`** ; fournisseur existant accepté ; aucun
  produit persisté lors d'un refus ; messages stables.
- **UpdateProduct** — mêmes cas ; produit **inchangé** lors du refus ; produit introuvable prime sur la garde
  fournisseur ; produit **inactif** reste modifiable ; garde consultée même pour un fournisseur inchangé.
- **Honnêteté du diagnostic** — une violation de contrainte **sans rapport** (unicité de référence) n'est **pas**
  étiquetée « fournisseur introuvable ».
- Constructeurs exigeant le port fournisseur.

---

## 21. Tests de concurrence (inclus ci-dessus)

- Produit créé après observation d'absence → tentative atomique refuse ; fournisseur et produit subsistent.
- Suppression atomique réussie, puis création de produit vers l'identifiant disparu → refusée par la FK ; **aucun
  orphelin**.
- Vérification périmée en création → FK arbitre → `SupplierNotFoundMessage` ; `ExistsCallCount == 2` (garde périmée
  puis diagnostic) ; **aucune écriture partielle**.
- Vérification périmée en modification → FK arbitre → message stable ; rollback prouvé (le produit conserve son
  fournisseur d'origine).
- Double suppression → idempotente.

---

## 22. Résultats ciblés

| Suite ciblée | Résultat |
|---|---|
| `SupplierValidatorTests` | 23 / 23 ✅ |
| `SupplierInputNormalizerTests` | 14 / 14 ✅ |
| `SupplierBusinessRulesTests` | 29 / 29 ✅ |
| `ProductSupplierGuardTests` | 18 / 18 ✅ |
| `SuppliersApplicationArchitectureTests` | 16 / 16 ✅ |
| **Total nouveaux** | **100** |

---

## 23. Résultat complet

```
dotnet build MMV.sln --no-restore -c Debug   → Build succeeded. 0 Warning(s) 0 Error(s)

MMV.Domain.Tests        Failed: 0, Passed: 537, Skipped: 0, Total: 537
MMV.Application.Tests   Failed: 0, Passed: 526, Skipped: 0, Total: 526
MMV.App.Tests           Failed: 0, Passed: 239, Skipped: 0, Total: 239
                                          ─────────────────────────────
TOTAL                                     1302   (0 échec, 0 ignoré)
```

| Suite | Baseline | Après P3-9 | Δ |
|---|---:|---:|---:|
| Domain | 514 | 537 | +23 |
| Application | 449 | 526 | +77 |
| App | 239 | 239 | **0** |
| **Total** | **1202** | **1302** | **+100** |

`MMV.App.Tests` reste **exactement** à 239 : aucune UI n'a été touchée.

> **Chiffres ci-dessus = état à l'implémentation initiale (avant la revue ciblée).** Voir « Revue ciblée avant
> commit » pour les totaux **finaux**, après les 6 tests supplémentaires ajoutés lors de la revue
> (`SupplierDeletionSqlAndTrackerTests`) : **1308** au total.

---

## 24. Sécurité

```
dotnet list MMV.sln package --vulnerable --include-transitive
→ aucun paquet vulnérable sur les 7 projets
```

Aucun paquet NuGet ajouté. `MMV.Application` ne porte toujours qu'une seule référence
(`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1). FluentValidation reste une dépendance du **Domain**
(11.9.0, Apache-2.0), conformément au socle P3-1. FluentAssertions reste en 6.x.

Aucun message technique de provider ne franchit la frontière Application, conformément à
`adr-application-boundaries` — c'est précisément le défaut 🔴-1 corrigé, et quatre assertions distinctes le
vérifient.

---

## 25. EF et migrations

```
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
→ No changes have been made to the model since the last migration.
```

**Aucune migration P3-9.** Aucun fichier sous `src/MMV.Infrastructure/Migrations/**` n'est modifié, snapshot
compris. `SqliteSchemaVerifier` et `SqliteDatabaseManager` sont intacts : rien n'étant ajouté au schéma, aucune
entrée d'adoption additive n'est requise — contrairement à `Customers.IsArchived` (P3-2B),
`Products.NormalizedReference` (P3-4B) ou `AddNotificationResolution` (P3-8).

Le retrait du `DeleteBehavior.SetNull` **ne produit aucun changement de modèle**, ce qui confirme empiriquement
qu'il était sans effet.

**Conséquence directe** : aucune base historique n'a à être adoptée, aucun backfill n'est nécessaire, aucun état
partiel n'est possible.

---

## 26. Architecture

```
dotnet list src/MMV.Application/MMV.Application.csproj reference
→ ..\MMV.Domain\MMV.Domain.csproj   (seule référence)
```

- `MMV.Application` ne référence ni EF Core, ni SQLite, ni Infrastructure, ni App, ni Avalonia ;
- `ISupplierRepository` reste **provider-neutre** : aucune signature n'expose EF, SQLite ni `IQueryable` —
  verrouillé par réflexion sur les types réellement exposés ;
- les invariants structurels de P3-9 sont verrouillés par 16 gardes : FK `Restrict` non nullable, relation
  configurée d'un seul côté, aucun `SetNull`/`Cascade`, primitives déclarées sur le port, dépendance des use cases
  produit au port fournisseur, runner transactionnel sur la suppression, **absence** d'`IsActive`/`IsArchived`,
  **absence** de use case d'archivage, **absence** d'index unique, résultats conformes P3-1, messages stables sans
  vocabulaire technique, code mort conservé.

---

## 27. Reports explicites

| # | Sujet | Motif | Destination |
|---|---|---|---|
| R1 | Toute évolution UI fournisseur | Interdiction absolue en P3-9 | Redesign global UI |
| R2 | `IsActive` / `IsArchived` fournisseur | Aucun besoin métier prouvé ; l'option A garantit l'intégrité sans migration | Redesign métier futur |
| R3 | Unicité `Name` / `ContactEmail` / `Phone` / `ReferenceCode` | Serait inventée | Futur, sur preuve métier |
| R4 | Fusion de doublons | Dépend de R3 | Futur |
| R5 | Commandes fournisseur, catalogue, tarifs, délais | Hors P3 | Hors P3 |
| R6 | Contacts multiples | Exigerait une nouvelle table | Redesign métier futur |
| R7 | `Address` structurée | Aucun besoin exprimé ; migration de données | Futur |
| R8 | Format de téléphone | Aucune règle ni pays de référence documenté | Futur |
| R9 | `CreatedAt` / `UpdatedAt` | Migration additive sans besoin exprimé | Futur |
| R10 | Audit utilisateur | Modèle sans champ utilisateur | **P3-10** |
| R11 | **Lost update / `RowVersion`** | Dette transverse à **toutes** les entités du dépôt | Futur, transverse |
| R12 | Pagination et tri SQL des fournisseurs | 25 lignes ; recherche en mémoire acceptable | Futur |
| R13 | Compteur de produits dans la liste | Changerait une **valeur observable** par l'UI | Redesign global UI |
| R14 | `BaseRepository.DeleteAsync(TId)` non gardé | Refactor transverse ; la FK reste le filet | Futur |
| R15 | `GetWithProductsAsync` sans `AsNoTracking` | Correction isolée hors périmètre | Futur |
| R16 | Longueurs contraignantes, provider serveur | `adr-prod-db-001` | Chantier production |
| R17 | E-mail automatique / notifications fournisseur | Hors P3 | Hors P3 |
| R18 | Multi-magasin | Hors P3 | Hors P3 |

---

## 28. État Git final

```
git status --short
 M src/MMV.Application/UseCases/Products/CreateProduct/CreateProductUseCase.cs
 M src/MMV.Application/UseCases/Products/UpdateProduct/UpdateProductUseCase.cs
 M src/MMV.Application/UseCases/Suppliers/CreateSupplier/CreateSupplierResult.cs
 M src/MMV.Application/UseCases/Suppliers/CreateSupplier/CreateSupplierUseCase.cs
 M src/MMV.Application/UseCases/Suppliers/DeleteSupplier/DeleteSupplierUseCase.cs
 M src/MMV.Application/UseCases/Suppliers/UpdateSupplier/UpdateSupplierResult.cs
 M src/MMV.Application/UseCases/Suppliers/UpdateSupplier/UpdateSupplierUseCase.cs
 M src/MMV.Domain/Interfaces/Repositories/ISupplierRepository.cs
 M src/MMV.Infrastructure/Data/Configurations/SupplierConfiguration.cs
 M src/MMV.Infrastructure/Repositories/SupplierRepository.cs
 M docs/architecture/P3-business-rules-roadmap.md
 M tests/MMV.Application.Tests/UseCases/Products/ProductBusinessRulesTests.cs
 M tests/MMV.Application.Tests/UseCases/Products/ProductUseCasesTests.cs
 M tests/MMV.Application.Tests/UseCases/Suppliers/SupplierUseCasesTests.cs
?? src/MMV.Application/UseCases/Suppliers/Common/
?? src/MMV.Domain/Validators/SupplierValidator.cs
?? tests/MMV.Application.Tests/Architecture/SuppliersApplicationArchitectureTests.cs
?? tests/MMV.Application.Tests/UseCases/Products/ProductSupplierGuardTests.cs
?? tests/MMV.Application.Tests/UseCases/Suppliers/SupplierBusinessRulesTests.cs
?? tests/MMV.Application.Tests/UseCases/Suppliers/SupplierInputNormalizerTests.cs
?? tests/MMV.Domain.Tests/ValidatorTests/SupplierValidatorTests.cs
?? docs/implementation/P3-9-suppliers-business-rules-audit-report.md
?? docs/implementation/P3-9-suppliers-business-rules-implementation-report.md
?? design-handoff/   ?? design/   ?? docs/ui/     (hors périmètre, préexistants)

git diff --check → (vide)
```

✅ Aucun fichier sous `src/MMV.App/**`. ✅ Aucun fichier sous `src/MMV.Infrastructure/Migrations/**`. ✅
`SqliteSchemaVerifier` et `SqliteDatabaseManager` intacts. ✅ `design-handoff/`, `design/`, `docs/ui/` inchangés.

**Aucun commit. Aucun push.**

---

## 29. Verdict

| Critère (§26 de l'énoncé) | Statut |
|---|---|
| Nom fournisseur réellement validé | ✅ §7, §18 — vide, espaces seuls et > 200 refusés |
| E-mail facultatif réellement validé | ✅ §7 — `null`/vide acceptés, invalide refusé, portée énoncée sans surestimation |
| Longueurs réellement appliquées | ✅ §5, §19 — la base ne les imposait pas ; l'Application les impose |
| Entrées normalisées | ✅ §6 — propriétaire unique, `Trim`, `""` → `null` |
| Résultats P3-1 cohérents | ✅ §8, §9 — `ValidationErrors` + `IsValid` additifs |
| Suppression atomique impossible avec tout produit lié | ✅ §10 — actif **et** inactif, primitive testée directement |
| Aucun message EF/SQLite exposé | ✅ §10, §19 — quatre assertions distinctes |
| Fournisseur obligatoire dans Create/Update Product | ✅ §12, §13 |
| `SupplierId ?? 0` supprimé | ✅ §12, §13 — plus aucun `0` écrit, verrouillé par test |
| Course de suppression couverte par la primitive et la FK | ✅ §14, §21 — deux ordres, sans `Thread.Sleep` |
| FK reste `RESTRICT` | ✅ §11, §15 — prouvé jusqu'en SQL brut |
| Aucune désactivation ni unicité inventée | ✅ §4 — verrouillé par gardes d'architecture |
| Tous les tests passent | ✅ §23 — 1302, 0 échec, 0 ignoré |
| Aucune vulnérabilité | ✅ §24 |
| Aucune migration en attente | ✅ §25 |
| Rapport `.md` présent | ✅ ce fichier |
| Aucune UI modifiée | ✅ §17, §23 — `MMV.App.Tests` exactement à 239 |

---

---

## Revue ciblée avant commit

> Revue portant exclusivement sur quatre points : la primitive `TryDeleteIfUnusedAsync`, l'état du change tracker
> après `ExecuteDeleteAsync`, la traduction tardive des violations de contrainte dans `CreateProductUseCase` /
> `UpdateProductUseCase`, et la cohérence documentaire. Aucune règle rouverte, aucune migration créée, aucune UI
> touchée.

### 1. SQL réellement produit par la suppression conditionnelle

Capturé par un intercepteur EF (`DbCommandInterceptor`, même mécanisme que `LowStockConcurrencyAndQueryCountTests`
en P3-8) dans `SupplierDeletionSqlAndTrackerTests` :

```sql
DELETE FROM "Suppliers" AS "s"
WHERE "s"."SupplierId" = @__supplierId_0 AND NOT EXISTS (
    SELECT 1
    FROM "Products" AS "p"
    WHERE "s"."SupplierId" = "p"."SupplierId")
```

- **une seule commande** exécutée pour toute l'opération, dans les deux cas (produit lié ou non) ;
- **aucune lecture préalable** : la condition et l'écriture sont la même instruction ;
- **aucun chargement de navigation** (`s.Products` compile en `EXISTS`, jamais en `Include`) ;
- **requête paramétrée** — `@__supplierId_0` porte l'identifiant, jamais concaténé en littéral (vérifié par
  regex négative sur le texte de commande) ;
- **aucun `SaveChangesAsync`** après `ExecuteDeleteAsync` — confirmé par l'absence de deuxième commande dans la
  liste interceptée ;
- **aucune dépendance SQLite exposée dans le port** — le port (`ISupplierRepository`) ne référence ni EF, ni
  SQLite, ni `IQueryable` (verrouillé par `SuppliersApplicationArchitectureTests`, inchangé).

**Conclusion : conforme à la structure attendue.** Aucune modification nécessaire ; test ajouté pour en faire une
preuve reproductible plutôt qu'une lecture de code.

### 2. Change tracker après `ExecuteDeleteAsync`

**Défaut concret trouvé.** `ExecuteDeleteAsync` contourne le change tracker par construction (documenté dans le
rapport initial), mais l'implication concrète n'avait pas été testée : une entité `Supplier` chargée par une
méthode suivie (`GetByIdAsync` → `FindAsync`) **avant** l'appel à `TryDeleteIfUnusedAsync`, dans la **même**
portée `DbContext`, restait marquée comme suivie après une suppression physique réussie. Un `FindAsync` ultérieur
sur ce même contexte consultait le cache local **avant** d'interroger la base, et renvoyait donc l'instance
**périmée** au lieu de `null` — sans exécuter la moindre requête SQL.

Reproduit et prouvé par `TryDeleteIfUnused_ApresSucces_AucuneInstancePerimeeNEstResuscitee_ParFindAsync` : avant
correction, `ctx.Suppliers.FindAsync(id)` après un `TryDeleteIfUnusedAsync` réussi renvoyait l'entité vivante ;
après correction, `null`. Un nouveau contexte confirmait déjà l'absence physique — le défaut ne concernait que la
cohérence d'un contexte **long-vécu**, pas l'atomicité de l'écriture SQL elle-même (qui était et reste correcte).

**Correction appliquée** — `SupplierRepository.TryDeleteIfUnusedAsync`, détachement **ciblé** :

```csharp
var deleted = rowsAffected == 1;
if (deleted)
{
    var tracked = _context.ChangeTracker.Entries<Supplier>()
        .FirstOrDefault(e => e.Entity.SupplierId == supplierId);
    if (tracked != null)
    {
        tracked.State = EntityState.Detached;
    }
}
return deleted;
```

- **pas de `ChangeTracker.Clear()` global** — seule l'entrée `Supplier` portant l'identifiant supprimé est
  détachée ;
- **aucun produit suivi affecté** — vérifié par
  `TryDeleteIfUnused_ApresEchec_AucuneInstanceNEstDetachee` (un refus ne détache rien) et
  `TryDeleteIfUnused_ApresSucces_NeDetacheAucunAutreFournisseurSuivi` (un fournisseur voisin suivi dans la même
  portée reste intact) ;
- **aucune requête SQL supplémentaire** — l'opération est purement en mémoire sur `ChangeTracker.Entries<T>()`,
  confirmé par le test SQL de la section 1 (toujours une seule commande interceptée) ;
- **aucun détachement quand la suppression échoue** — le chemin « produit lié » ne touche jamais le tracker.

Ce nettoyage concerne la **cohérence d'un contexte long-vécu après une écriture ensembliste** ; il ne remet pas en
cause l'atomicité SQL prouvée en section 1, ni l'analyse de concurrence multi-poste du rapport initial (§14), qui
portait sur des **contextes distincts** — un axe orthogonal.

### 3. Diagnostic après suppression refusée, avec instance périmée

Ajouté au niveau **use case** (`DeleteSupplierUseCase_AvecInstanceSuivieDejaSupprimeeAilleurs_ProduitSupplierFoundFalse_SansFauxMessage`) :
un fournisseur chargé (suivi) dans une portée, supprimé physiquement par un **autre** contexte, puis
`DeleteSupplierUseCase.ExecuteAsync` invoqué dans la portée d'origine. `TryDeleteIfUnusedAsync` réévalue l'état
réel (0 ligne affectée : déjà absent), et le diagnostic (`ExistsFreshAsync`, jamais l'`ExistsAsync` hérité basé
sur `FindAsync`) confirme l'absence réelle. Résultat : `SupplierFound = false`, **jamais** de
`BusinessRuleException` fabriquée sur la foi d'une instance suivie périmée. La lecture diagnostique ne déclenche
et ne provoque **aucune** seconde tentative de suppression — le use case ne fait qu'un seul appel à
`TryDeleteIfUnusedAsync`.

**Conclusion : conforme.** Aucune modification nécessaire ; le use case utilisait déjà la primitive fraîche
correcte. Test ajouté pour couvrir explicitement ce scénario au niveau use case (auparavant prouvé seulement au
niveau repository par `ExistsFresh_LitLaBase_EtNonLeChangeTracker`).

### 4. Traduction tardive — `CreateProductUseCase` / `UpdateProductUseCase`

**Analyse de classification.** `PersistenceErrorMapper.Classify` distingue les violations SQLite par **code
d'erreur étendu** : `SQLITE_CONSTRAINT_UNIQUE` (2067) / `SQLITE_CONSTRAINT_PRIMARYKEY` (1555) →
`UniqueConstraint` ; tout autre `SQLITE_CONSTRAINT` (code primaire 19, ce qui inclut `SQLITE_CONSTRAINT_FOREIGNKEY`)
→ `ConstraintViolation`. Ces deux catégories sont donc **déjà disjointes au niveau SQLite lui-même**, avant même
d'atteindre le use case : une violation d'unicité de référence ne peut jamais être classée `ConstraintViolation`,
et une violation de FK ne peut jamais être classée `UniqueConstraint`.

- **Scénario « fournisseur réellement disparu »** — couvert par
  `Create_VerificationPerimee_LaFkArbitre_EtLErreurEstTraduite` /
  `Update_VerificationPerimee_LaFkArbitre_EtLErreurEstTraduite` (garde périmée, FK arbitre, message stable, aucune
  écriture partielle, rollback prouvé).
- **Scénario « fournisseur toujours présent, référence dupliquée »** — couvert par
  `UneViolationDeContrainteSansRapport_NEstPasEtiqueteeFournisseurIntrouvable` (pré-check applicatif) et, pour le
  cas **concurrent** (deux postes, collision détectée seulement à l'écriture), par les tests P3-4B
  `Create_ConcurrentCollision_ReturnsSameStableDuplicateResult_NoException` /
  `Update_ConcurrentCollision_ReturnsSameStableDuplicateResult_NoException` — catégorie `UniqueConstraint`, jamais
  interceptée par le `catch (ConstraintViolation)` fournisseur.
- **Scénario « fournisseur toujours présent, autre violation FK »** — **non constructible dans le schéma actuel** :
  `Product` ne porte qu'une seule clé étrangère écrite par ces deux use cases (`SupplierId`). Aucune autre
  contrainte `SQLITE_CONSTRAINT` (hors unicité, déjà exclue) ne peut se déclencher sur l'entité `Product` telle
  qu'elle existe aujourd'hui — les contraintes de validation (prix, longueurs) sont interceptées par
  `ProductValidator` **avant** d'atteindre la base. Ce point est **noté honnêtement plutôt que simulé** : la
  garde `if (await _supplierRepository.ExistsFreshAsync(...)) throw;` dans les deux use cases reste correcte et
  nécessaire en défense en profondeur pour toute contrainte FK future, mais elle est aujourd'hui **structurellement
  inatteignable** avec un fournisseur présent, faute d'une seconde FK sur `Product`.

**Conclusion : aucune écriture partielle possible, aucune mauvaise classification observée ou constructible.**
Aucune modification de code nécessaire.

### 5. Preuve FK — ordres A et B

Déjà couverts, sans `Thread.Sleep`, dans `SupplierBusinessRulesTests` :

- **Ordre A** (produit créé, puis suppression tentée) —
  `Course_ProduitCreeApresObservationDAbsence_LaTentativeAtomiqueEchoue` : observation périmée d'absence, produit
  créé par un autre poste, tentative atomique réévaluée au moment de l'écriture → `false`.
- **Ordre B** (suppression réussie, puis création tentée avec l'identifiant disparu) —
  `Course_SuppressionPuisCreationDeProduit_EstRefuseeSansOrphelin` : suppression atomique réussie, tentative de
  création vers l'identifiant disparu → `DbUpdateException` (FK), aucun orphelin.
- **Preuve FK en SQL brut**, hors EF et hors use case —
  `SuppliersApplicationArchitectureTests.LaBase_RefuseUneSuppressionForceeHorsUseCase` : `ExecuteSqlRawAsync`
  direct, `SqliteErrorCode == 19`, fournisseur et produit tous deux conservés après l'échec.
- `PRAGMA foreign_keys == 1` vérifié dans le même fichier de test — sans ce réglage, aucune des garanties
  ci-dessus ne serait réelle.

**Conclusion : conforme, aucune modification nécessaire.** Le comportement exact sous PostgreSQL / SQL Server
reste à vérifier lors du chantier provider (`adr-prod-db-001`) ; l'invariant repose également sur une FK
standard, portable par construction.

### 6. Validation et normalisation — Create vs Update

Vérifié par lecture directe de `SupplierInputNormalizer.Normalize` (méthode statique **unique**, appelée
littéralement à l'identique par `CreateSupplierUseCase` et `UpdateSupplierUseCase`), de `SupplierValidator`
(inchangé), et de l'ordre d'exécution dans `UpdateSupplierUseCase` (candidat détaché validé **avant** que
`normalized.ApplyTo(supplier)` ne touche l'entité suivie). Aucune divergence, aucune casse modifiée, aucune
unicité ajoutée, aucun état actif/archivé introduit. **Conforme — aucune modification nécessaire.**

### 7. Résumé des tests ajoutés lors de la revue

`tests/MMV.Application.Tests/UseCases/Suppliers/SupplierDeletionSqlAndTrackerTests.cs` — **6 tests, nouveaux** :

| Test | Point couvert |
|---|---|
| `TryDeleteIfUnused_SansProduit_ProduitUneSeuleCommandeDelete_AvecConditionNotExists` | §1 — SQL exact, sans produit lié |
| `TryDeleteIfUnused_AvecProduit_ProduitUneSeuleCommandeDelete_QuiNAffecteAucuneLigne` | §1 — SQL exact, refus |
| `TryDeleteIfUnused_ApresSucces_AucuneInstancePerimeeNEstResuscitee_ParFindAsync` | §2 — défaut trouvé et corrigé |
| `TryDeleteIfUnused_ApresEchec_AucuneInstanceNEstDetachee` | §2 — contre-preuve (pas de détachement sur refus) |
| `TryDeleteIfUnused_ApresSucces_NeDetacheAucunAutreFournisseurSuivi` | §2 — détachement ciblé, pas de collatéral |
| `DeleteSupplierUseCase_AvecInstanceSuivieDejaSupprimeeAilleurs_ProduitSupplierFoundFalse_SansFauxMessage` | §3 — diagnostic honnête au niveau use case |

### 8. Total après revue

```
MMV.Domain.Tests        Failed: 0, Passed: 537, Skipped: 0, Total: 537
MMV.Application.Tests   Failed: 0, Passed: 532, Skipped: 0, Total: 532
MMV.App.Tests           Failed: 0, Passed: 239, Skipped: 0, Total: 239
                                          ─────────────────────────────
TOTAL                                     1308   (0 échec, 0 ignoré)
```

| Suite | Avant revue | Après revue | Δ |
|---|---:|---:|---:|
| Domain | 537 | 537 | 0 |
| Application | 526 | 532 | **+6** |
| App | 239 | 239 | 0 |
| **Total** | **1302** | **1308** | **+6** |

`dotnet list MMV.sln package --vulnerable --include-transitive` → aucun paquet vulnérable (7 projets). `dotnet ef
migrations has-pending-model-changes` → « No changes have been made to the model since the last migration. »
`dotnet list src/MMV.Application/MMV.Application.csproj reference` → `MMV.Domain` uniquement. `MMV.App.Tests`
reste exactement à 239 : aucune UI modifiée par la revue.

---

## Préparation du commit

| Paramètre | Valeur |
|---|---|
| HEAD de départ | `b30f7748cf6d3eb69cc88c4a6457dea170be4e8a` |
| CI de départ | run `29783755704`, `push` / `completed` / `success` |
| Build final | `dotnet build MMV.sln -c Debug` → 0 erreur, 0 avertissement |
| Domain | 537 / 537 |
| Application | 532 / 532 |
| App | 239 / 239 |
| **Total** | **1308**, 0 échec, 0 ignoré |
| Vulnérabilités | 0 (7 projets, packages directs + transitifs) |
| État EF | `has-pending-model-changes` → aucun changement |
| Migration | **aucune** |
| UI | **aucune** modification (`MMV.App.Tests` inchangé à 239, aucun fichier sous `src/MMV.App/**`) |
| Périmètre du commit | Domain (2), Application (8), Infrastructure (2), Tests (9), Documentation (3) — 24 fichiers |

**Verdict : P3-9 = GO LOCAL.** Sous réserve de la CI du commit à venir.

---

# **P3-9 = GO LOCAL**

**Aucun commit. Aucun push. Aucune UI. STOP.**
