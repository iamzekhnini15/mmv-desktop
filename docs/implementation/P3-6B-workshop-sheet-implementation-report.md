# P3-6B — Implémentation backend : fiche atelier de montage versionnée

> **Mode** : `IMPLEMENT_TEST_MIGRATE_AND_WRITE_REPORT_NO_COMMIT`. Backend uniquement. Aucune UI, aucun PDF,
> aucune impression, aucun commit, aucun push. Le code réel prime toujours sur les documents cités.

---

## 1. Paramètres et périmètre

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | **P3-6B** (Fiche atelier — backend uniquement) |
| Périmètre | Domain (entités, enum, optique, empreinte, politique, exception) / Application (5 use cases + intégration Orders) / Infrastructure (EF, repository, adoption de base) / Migration / Tests / Documentation |
| HEAD de départ | `a4d7380b374167f4b27809e9b4a7e56b28bf3c48` |

**Interdits respectés** : aucune modification sous `src/MMV.App/**`, `design-handoff/**`, `design/**`,
`docs/ui/**` ; aucune ancienne migration modifiée ; aucun commit ni push ; aucun PDF/impression/export.

---

## 2. État Git et CI de départ

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → a4d7380b374167f4b27809e9b4a7e56b28bf3c48
git rev-parse origin/…      → a4d7380b374167f4b27809e9b4a7e56b28bf3c48
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
                              ?? docs/implementation/P3-6B-workshop-sheet-audit-report.md
git diff --check            → (aucune sortie)
```

CI (`gh run view 29665945780`) : `headSha` = `a4d7380…`, `event = push`, `status = completed`,
`conclusion = success` ✅. Seul changement P3-6B présent au départ : le rapport d'audit (non suivi) ✅.

**Conditions de démarrage remplies.**

---

## 3. Baseline (avant implémentation)

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet build -c Debug` | 0 erreur / 0 warning | **0 / 0** | ✅ |
| `dotnet test` | 869 / 0 / 0 | **869** (Domain 333 + Application 297 + App 239) | ✅ |
| `ef migrations has-pending-model-changes` | aucune | *No changes … since the last migration* | ✅ |

---

## 4. Décisions finales

1. **Première fiche obligatoire pour toute commande neuve.** Création **automatique** dans
   `AdvanceOrderStatusUseCase`, dans la transaction, à `ToFabricate → InProgress` (nominal) et
   `InProgress → QualityCheck` (compatibilité). Idempotente. ⇒ aucune commande créée après P3-6B ne peut
   atteindre `QualityCheck` sans fiche.
2. **Compatibilité historique.** `QualityCheck → Ready` **autorisé** si aucune fiche n'existe. Aucun backfill,
   **aucun QC inventé**. Si une fiche existe : `Passed` **et** non obsolète exigés.
3. **Snapshot en agrégat** : `WorkshopSheet` + `WorkshopSheetItem` (une ligne par `OrderItem`), ordre figé.
4. **Immutabilité** : seules les colonnes QC évoluent, **une seule fois** (`Pending → Passed|Failed`).
5. **Génération manuelle** restreinte à `InProgress` / `QualityCheck`.
6. **Aucun auteur de QC** en V1 (identité non disponible proprement côté Application).
7. **Aucune règle QC ajoutée sur `Ready → Delivered`** : le passage par `Ready` suffit.

---

## 5. Modèle Domain

**Créés**

| Élément | Chemin | Rôle |
|---|---|---|
| `WorkshopSheet` | `src/MMV.Domain/Entities/WorkshopSheet.cs` | Racine d'agrégat : version, `IsCurrent`, empreinte, snapshot d'en-tête, QC |
| `WorkshopSheetItem` | `src/MMV.Domain/Entities/WorkshopSheetItem.cs` | Ligne snapshot : produit par valeur, optique source **et** transposée |
| `WorkshopSheetQcStatus` | `src/MMV.Domain/Enums/WorkshopSheetQcStatus.cs` | `Pending` / `Passed` / `Failed` |
| `WorkshopSheetPolicy` | `src/MMV.Domain/Services/WorkshopSheetPolicy.cs` | Règles pures + **messages métier stables en constantes** |
| `WorkshopSheetFactory` | `src/MMV.Domain/Services/WorkshopSheetFactory.cs` | Projection pure `Order` → agrégat fiche |
| `WorkshopSheetFingerprint` | `src/MMV.Domain/Services/WorkshopSheetFingerprint.cs` | Empreinte technique déterministe |
| `OpticalCorrection` | `src/MMV.Domain/Optics/OpticalCorrection.cs` | Value object immuable (sphère/cylindre/axe **seulement**) |
| `OpticalTranspositionResult` | `src/MMV.Domain/Optics/OpticalTranspositionResult.cs` | Résultat + drapeau « transposition produite » |
| `OpticalTranspositionService` | `src/MMV.Domain/Optics/OpticalTranspositionService.cs` | Transposition pure |
| `WorkshopSheetVersionConflictException` | `src/MMV.Domain/Exceptions/DomainExceptions.cs` | Conflit de version multi-poste (message métier, sans détail EF/SQLite) |

**Champs volontairement absents** (minimisation par construction, verrouillée par test) : téléphone, email,
adresse, montant, acompte, reste à payer, données fiscales, auteur utilisateur, rôle technicien.

---

## 6. Snapshot

Construit **depuis `Order` + `OrderItem` + `Product` déjà chargés**, jamais depuis `OrderDetailsDto` — ce
dernier perd `UsageType`, `PrismValue`, `PrismBase` et `VisualAcuity` (constat central de l'audit §7). Un test
dédié verrouille la présence de ces quatre champs dans le snapshot.

| Bloc | Traitement |
|---|---|
| En-tête commande | numéro / date / livraison estimée / instructions **copiés** |
| Porteur | **nom seul**, via `Sale.Customer` ; libellé neutre stable si absent (`Client non renseigné`) |
| Produit | référence / désignation / catégorie **copiées** ; `SourceProductId` informatif, **sans FK** |
| Optique | notation **source** conservée + notation **atelier transposée** à côté |
| Lignes | une par `OrderItem`, `Position` figée (ordre déterministe, indépendant de la base) |

Robustesse : ligne sans produit **conservée** avec valeurs produit neutres (aucune `NullReferenceException`) ;
commande sans ligne ⇒ **refus métier** au niveau du use case.

---

## 7. Transposition

Service **pur**, statique, sans état ni dépendance. Formule : `sphère' = sphère + cylindre` ;
`cylindre' = −cylindre` ; `axe' = axe + 90`, `> 180 ⇒ −180`, puis normalisation `0 → 180` déléguée à
`OpticalAxisNormalizer` (source unique de cette convention depuis P3-3B).

| Cas | Comportement |
|---|---|
| Cylindre absent | source inchangée, `IsTransposed = false` |
| Cylindre nul | identité, `IsTransposed = false` |
| Cylindre non nul **sans axe** | source inchangée, **aucun axe inventé**, `IsTransposed = false` |
| Cylindre non nul + axe | transposée, `IsTransposed = true` |
| Valeur non finie (`NaN`/∞) | non transposable |
| Sphère absente | reste absente (pas de `0.00` inventé) |

**Garanties prouvées** : involution (double transposition ⇒ correction canonique initiale) ; exactitude au quart
de dioptrie **sans arrondi** ; axe résultant toujours dans `[1, 180]` (jamais `0`) ; addition / prisme / base /
acuité **hors d'atteinte du service** — garantie **structurelle**, `OpticalCorrection` ne les expose pas (test de
réflexion verrouillant la liste exacte des propriétés) ; source jamais modifiée.

---

## 8. Empreinte

`SHA-256` hexadécimal minuscule d'un encodage canonique **injectif**, préfixé du tag de format `WSFP1`.

- **Inclus** : `Order.Notes` ; par ligne triée (`OrderItemId`, puis type, produit, quantité) — type, produit,
  quantité, usage, sphère, cylindre, axe, addition, prisme, base, acuité.
- **Exclus** : téléphone, email, montants, nom actuel du produit relu du catalogue, **statut de commande**,
  **état QC** (les inclure rendrait toute fiche obsolète à la transition suivante).
- **Encodage** : ordre de propriétés fixe ; `double` au format aller-retour `"R"` en culture invariante (**pas**
  `F2`, qui écraserait une différence réelle) ; entiers invariants ; enums par nom ; marqueur de nul `~`
  **distinct** de la chaîne vide (`s0:`) ; chaînes **préfixées de leur longueur** ⇒ aucune collision possible
  par caractère séparateur.

Tests : déterminisme, sensibilité à chaque donnée technique (9 cas paramétrés) et aux instructions, insensibilité
au statut et à l'en-tête non technique, insensibilité à l'ordre de restitution, non-collision sur séparateurs et
sur découpage, `null` ≠ chaîne vide, `null` ≠ `0`.

---

## 9. Persistance

`DbSet<WorkshopSheet>` / `DbSet<WorkshopSheetItem>` + deux configurations EF dédiées.

| Élément | Valeur |
|---|---|
| Enums | `HasConversion<string>` (`QcStatus`, `ItemType`, `UsageType`, `PrismBase`) |
| Optique | `REAL` (cohérent avec `OrderItemConfiguration`) |
| `idx_workshop_sheets_order_version_unique` | **unique** `(OrderId, Version)` |
| `idx_workshop_sheets_current_unique` | **unique filtré** `(OrderId) WHERE "IsCurrent" = 1` |
| `idx_workshop_sheet_items_sheet_id` | index sur la FK de ligne |
| FK `WorkshopSheet → Order` | **`Restrict`** — jamais de cascade sur un document historique |
| FK `WorkshopSheetItem → WorkshopSheet` | `Cascade` — **uniquement** racine d'agrégat → ses lignes |
| FK vers `Product` / `Customer` / `Prescription` | **aucune** (snapshot par valeur) |

> **Point corrigé en cours d'implémentation** : un index simple sur `OrderId` avait d'abord été déclaré en plus
> de l'index filtré. EF considère qu'il s'agit du **même** index (même propriété) et le second écrasait
> silencieusement le premier. Déclaration retirée : l'index unique `(OrderId, Version)` porte `OrderId` en
> colonne de tête et sert déjà les recherches par commande.

**Aucun use case de suppression de fiche n'a été créé.**

---

## 10. Versionnement atomique

> **Section remplacée par le §24bis.** La conception décrite ci-dessous (`MAX(Version) + 1` puis bascule non
> ciblée) laissait deux demandes concurrentes créer une v2 **et** une v3. Le contrat prend désormais une
> **précondition de version** (compare-and-swap). Le texte est conservé pour l'historique de la décision.

`IOrderRepository.CreateNextWorkshopSheetVersionAsync` (implémenté dans `OrderRepository`) — **conception
initiale, corrigée au §24bis** :

1. lecture `MAX(Version)` pour la commande ;
2. `UPDATE … SET IsCurrent = 0 WHERE OrderId = @id AND IsCurrent` (conditionnel, hors change tracker) ;
3. construction du snapshot par `WorkshopSheetFactory` (Domain pur) ;
4. `INSERT` de la nouvelle version (`IsCurrent = true`, QC `Pending`) ;
5. `SaveChanges` — **la base arbitre**.

**La valeur lue en (1) ne garantit rien** : l'unicité vient des contraintes de schéma. Une `DbUpdateException`
est traduite en `WorkshopSheetVersionConflictException` (message métier, **aucun** détail EF/SQLite ne franchit
l'Infrastructure). **Aucune re-tentative opaque** dans la transaction : après violation de contrainte, la
transaction n'est plus utilisable ; l'appelant peut relancer explicitement.

Le tout s'exécute dans un `ITransactionRunner` : un échec annule aussi la désactivation de l'ancienne version,
qui **redevient courante** — jamais de commande sans version courante.

---

## 11. Génération automatique

Point commun unique : `WorkshopSheetOperations.EnsureCurrentSheetAsync` (Application, `internal`), réutilisé par
`GenerateWorkshopSheetUseCase` **et** `AdvanceOrderStatusUseCase`. Aucune duplication de la règle « une seule
version courante, snapshot complet, QC repartant à zéro » — exactement la seconde source de vérité que P3-6
venait d'éliminer sur le workflow.

Ces opérations **n'ouvrent aucune transaction** : elles s'exécutent dans celle de leur appelant, ce qui rend la
génération automatique annulable avec la transition.

---

## 12. Use cases

| Use case | Type | Préconditions | Transaction | Résultat / exception |
|---|---|---|---|---|
| `GenerateWorkshopSheetUseCase` | Command | commande existe ; statut `InProgress`/`QualityCheck` ; ≥ 1 ligne | `ITransactionRunner` | version créée ; `OrderFound=false` ; `BusinessRuleException` ; `WorkshopSheetVersionConflictException` |
| `GetCurrentWorkshopSheetUseCase` | Query | — | lecture | fiche + lignes + `IsUpToDate` ; `SheetFound=false` |
| `ListWorkshopSheetVersionsUseCase` | Query | — | lecture | historique **version décroissante** |
| `ValidateWorkshopSheetQcUseCase` | Command | fiche courante, non obsolète, `Pending` | `ITransactionRunner` | `Passed` ; `BusinessRuleException` |
| `RejectWorkshopSheetQcUseCase` | Command | idem + **commentaire non vide** | `ITransactionRunner` | `Failed` ; `BusinessRuleException` |

Les cinq sont enregistrés en `Scoped` dans `MMV.Application/DependencyInjection.cs`. Les DTO (`WorkshopSheetDto`,
`WorkshopSheetItemDto`) sont plats : aucune entité EF suivie ne franchit la frontière applicative, et aucune
donnée n'est relue du catalogue ou du client pour l'affichage.

---

## 13. Contrôle qualité

Logique partagée : `WorkshopSheetQcDecision.ApplyAsync` — **deux gardes complémentaires**, sur le modèle P3-6 :

1. **Préconditions métier** (version courante, fiche à jour, décision non prise, commentaire si refus) ⇒ message
   métier précis.
2. **Prise atomique conditionnelle** :
   `UPDATE … WHERE WorkshopSheetId = @id AND IsCurrent AND QcStatus = 'Pending'`. Aucune comparaison en mémoire
   ne la remplace ; elle tranche le cas concurrent (deux postes, ou version rendue non courante entre la
   vérification et l'écriture) ⇒ `BusinessRuleException` si 0 ligne affectée.

Une décision est **définitive** : aucune transition `Passed → Failed`, `Failed → Passed`, ni retour à `Pending`.
Une reprise crée une **nouvelle version** `Pending` ; le résultat QC n'est jamais recopié.

---

## 14. Intégration au workflow

`AdvanceOrderStatusUseCase` — **tout le flux P3-5/P3-6 est conservé** : `OrderStatusPolicy` (légalité),
`TryTransitionStatusAsync` (prise atomique), décrément atomique, mouvements, notification, transaction unique.

Ajouts, **dans la transaction** :

| Transition | Ajout P3-6B |
|---|---|
| `ToFabricate → InProgress` | garantit une fiche courante (crée la v1 si absente), **après** la prise de statut |
| `InProgress → QualityCheck` | idem (filet de compatibilité pour une commande déjà `InProgress`) |
| `QualityCheck → Ready` | **garde QC**, exécutée **avant** la prise de statut |
| `Ready → Delivered` | **aucun ajout** |

> **Affirmation corrigée (§24bis).** Une version antérieure de ce rapport affirmait ici que « la garde QC lit la
> version courante dans la transaction : une régénération concurrente ne peut pas se glisser entre la
> vérification et la transition ». **C'était faux** : lire dans la transaction ne verrouille rien, et la prise de
> statut ne portait que sur `Order.Status`. Le durcissement décrit au **§24bis** lie désormais la transition à la
> fiche autoritaire dans la **même** instruction que l'écriture.

Nouveau champ de résultat : `HasCreatedWorkshopSheet`.

**Atomicité prouvée** : stock insuffisant ⇒ `InsufficientStockException` ⇒ rollback total ⇒ **ni fiche, ni
lignes, ni mouvement, ni statut avancé**.

---

## 15. Compatibilité historique

| Situation | Comportement |
|---|---|
| Commande déjà `Ready`/`Delivered` | jamais re-transitionnée ⇒ **aucun impact** |
| Commande déjà `QualityCheck`, sans fiche | `→ Ready` **autorisé** ; aucune fiche créée, **aucun QC inventé** |
| Commande déjà `InProgress`, sans fiche | obtient sa fiche au passage `→ QualityCheck` |
| Commande créée après P3-6B | fiche garantie avant `QualityCheck` ⇒ soumise à la garde |
| Base antérieure à P3-6B | tables créées par migration exécutée à l'adoption (§16) ; **zéro fiche** backfillée |

---

## 16. Migration

`20260719003826_AddWorkshopSheets` — **purement additive** : deux `CreateTable`, trois `CreateIndex`. Aucune
table métier existante modifiée, aucun statut touché, **aucune fiche ni aucun QC créés**. `Down` supprime les
deux tables. `has-pending-model-changes = false` après génération.

**Adoption des bases historiques (ajout non prévu par l'audit).** Les tests d'adoption existants ont révélé une
régression réelle : le portail de compatibilité refusait toute base antérieure, les deux nouvelles tables étant
vues comme des divergences bloquantes. Correction, sur le **précédent exact de `DocumentSequences`**
(ADR-numbering §4.5) :

- `SqliteSchemaVerifier` : `WorkshopSheets` et `WorkshopSheetItems` ajoutées aux **tables additives tolérées**
  quand absentes ;
- `SqliteDatabaseManager` : `AddWorkshopSheets` est **exécutée** (et non baselinée) si `WorkshopSheets` est
  physiquement absente, **plus** une vérification physique **post-adoption** (échec explicite si les tables
  manquent alors que la migration est inscrite — pas de *baseline mensonger*).

---

## 17. Fichiers modifiés et créés

**Créés — Domain (9)** : `Entities/WorkshopSheet.cs`, `Entities/WorkshopSheetItem.cs`,
`Enums/WorkshopSheetQcStatus.cs`, `Services/WorkshopSheetPolicy.cs`, `Services/WorkshopSheetFactory.cs`,
`Services/WorkshopSheetFingerprint.cs`, `Optics/OpticalCorrection.cs`, `Optics/OpticalTranspositionResult.cs`,
`Optics/OpticalTranspositionService.cs`.

**Créés — Application (12)** : `UseCases/WorkshopSheets/` — `WorkshopSheetDto.cs`, `WorkshopSheetItemDto.cs`,
`WorkshopSheetOperations.cs`, `WorkshopSheetQcDecision.cs`, `GenerateWorkshopSheet/` (4 fichiers),
`GetCurrentWorkshopSheet/` (2), `ListWorkshopSheetVersions/` (2), `ValidateWorkshopSheetQc/` (1),
`RejectWorkshopSheetQc/` (1).

**Créés — Infrastructure (3)** : `Data/Configurations/WorkshopSheetConfiguration.cs`,
`Data/Configurations/WorkshopSheetItemConfiguration.cs`, migration `20260719003826_AddWorkshopSheets(.Designer).cs`.

**Créés — Tests (6)** : `MMV.Domain.Tests/OpticsTests/OpticalTranspositionServiceTests.cs`,
`ServiceTests/WorkshopSheetFingerprintTests.cs`, `ServiceTests/WorkshopSheetFactoryTests.cs`,
`ServiceTests/WorkshopSheetPolicyTests.cs`, `Data/WorkshopSheetsMigrationTests.cs`,
`MMV.Application.Tests/UseCases/WorkshopSheets/` (`WorkshopSheetTestBase.cs`,
`GenerateWorkshopSheetUseCaseTests.cs`, `WorkshopSheetQcUseCaseTests.cs`,
`AdvanceOrderStatusWorkshopSheetTests.cs`).

**Modifiés (10)**

- `src/MMV.Domain/Exceptions/DomainExceptions.cs` (nouvelle exception)
- `src/MMV.Domain/Interfaces/Repositories/IOrderRepository.cs` (5 opérations de fiche)
- `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs` (génération auto + garde QC)
- `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusResult.cs` (`HasCreatedWorkshopSheet`)
- `src/MMV.Application/DependencyInjection.cs` (5 enregistrements)
- `src/MMV.Infrastructure/Data/OpticDbContext.cs` (2 `DbSet` + 2 configurations)
- `src/MMV.Infrastructure/Repositories/OrderRepository.cs` (5 implémentations)
- `src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs` (tables additives tolérées)
- `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` (exécution + vérification post-adoption)
- `src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs` (snapshot EF, régénéré)
- `docs/implementation/P3-6B-workshop-sheet-audit-report.md` (§27) et `docs/architecture/P3-business-rules-roadmap.md`

**Aucune ancienne migration modifiée. Aucun fichier `src/MMV.App/**`.**

---

## 18. Tests ajoutés

**Domain — transposition (14)** : formule (exemple de référence, cylindre +/−), bornes d'axe (7 cas paramétrés),
axe jamais nul sur les 181 valeurs, cylindre absent / nul / sans axe, valeurs non finies (3), sphère absente,
involution (5 cas dont axe `0 → 180`), exactitude au quart de dioptrie, source non modifiée, `OpticalCorrection`
limité à 3 propriétés, service sans dépendance.

**Domain — empreinte (17)** : déterminisme, format SHA-256, commande vide, sensibilité sphère / sphère infime /
quantité / instructions / 9 données techniques paramétrées, insensibilité au statut et à l'en-tête, ordre de
restitution, collisions de séparateurs (2), `null` ≠ vide, `null` ≠ `0`, argument nul.

**Domain — fabrique (16)** : v1 courante et `Pending`, en-tête copié, empreinte renseignée, version invalide,
argument nul, nom seul, **absence structurelle** de téléphone/email/adresse/montant, client absent / sans nom,
lignes et ordre, champs perdus par le DTO, produit par valeur, ligne sans produit, commande sans ligne, deux
notations, ligne sans cylindre, source jamais modifiée.

**Domain — politique (6)** et **migration (14)** : tables, colonnes, absence de colonne personnelle, colonnes
optiques source + transposée, index unique `(OrderId, Version)`, index unique **filtré**, FK `Restrict`, FK
lignes `Cascade`, absence de FK catalogue/client, aucun backfill, aucun statut modifié, script ne touchant aucune
table métier, `Down` supprimant les deux tables sans altérer le reste.

**Application — génération et lecture (26)** : v1 courante, toutes les lignes, prisme/usage/acuité, transposition
stockée **et source intacte en base**, nom seul, commande introuvable, 4 statuts refusés + 2 autorisés, commande
sans ligne, argument nul, v2 rend v1 non courante, reprise après QC refusé, une seule version courante, refus
base de deux versions courantes, refus base du doublon `(OrderId, Version)`, **génération concurrente préservant
les invariants**, lecture sans fiche, lecture avec lignes ordonnées, obsolescence signalée, **snapshot figé face
aux changements de catalogue et de client**, historique décroissant, commande introuvable.

**Application — contrôle qualité (13)** : validation, sans commentaire, fiche introuvable, refus avec
commentaire, refus sans commentaire (3 cas), double validation, refus après validation, validation après refus,
**validation concurrente ⇒ une seule réussite**, version non courante, fiche obsolète, snapshot non modifié par
le QC, argument nul.

**Application — workflow (13)** : génération auto v1, même transaction que le stock, **échec de stock annulant
aussi la fiche**, pas de seconde fiche, fiche pour commande historique `InProgress`, **impossibilité d'atteindre
`QualityCheck` sans fiche**, `Ready` refusé si `Pending` / `Failed` / obsolète, `Ready` accepté si `Passed`,
`Ready` autorisé sans fiche (compatibilité), `Delivered` sans seconde garde, transition illégale sans effet,
conflit de statut sans fiche créée.

---

## 19. Résultats ciblés

| Cible | Réussis / Échec / Ignoré |
|---|---|
| Tests Application `~WorkshopSheets` | **55** / 0 / 0 |
| `WorkshopSheetsMigrationTests` (Domain) | **14** / 0 / 0 |

---

## 20. Résultat complet

`dotnet test MMV.sln --no-build -c Debug` :

| Assembly | Réussis | Échec | Ignoré |
|---|---|---|---|
| `MMV.Domain.Tests` | 426 | 0 | 0 |
| `MMV.Application.Tests` | 352 | 0 | 0 |
| `MMV.App.Tests` | 239 | 0 | 0 |
| **Total** | **1017** | **0** | **0** |

Évolution vs baseline (869) : **+148**. Détail : Domain 333 → 426 (+93) ; Application 297 → 352 (+55) ;
App 239 (**inchangé** — aucune UI touchée).

- `dotnet build` : **0 erreur / 0 avertissement**
- `dotnet list package --vulnerable --include-transitive` : **aucun package vulnérable** (7 projets)
- `ef migrations has-pending-model-changes` : *No changes have been made to the model since the last migration*
- `git diff --check` : aucune erreur d'espaces (seuls des avertissements informatifs LF→CRLF)

---

## 21. Architecture

```
dotnet list src/MMV.Application/MMV.Application.csproj reference
→ ..\MMV.Domain\MMV.Domain.csproj   (Domain uniquement)
dotnet list src/MMV.Application/MMV.Application.csproj package
→ Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1   (inchangé)
```

- **Domain sans EF** : entités, enum, optique, empreinte et politique sont purs ; les services de transposition
  et d'empreinte sont statiques et sans champ d'instance (verrouillé par test).
- **Application → Domain seul** ; aucun service UI injecté ; **aucune** dépendance à `ISessionService`.
- **Infrastructure** porte EF, les mises à jour conditionnelles et l'adoption de base.
- **Aucune règle dans un ViewModel** ; aucun fichier `MMV.App` modifié ; aucun package ajouté.

> **Écart assumé et documenté** : les opérations de fiche vivent sur `IOrderRepository` plutôt que sur un
> `IWorkshopSheetRepository` dédié. Les repositories sont enregistrés dans `src/MMV.App/App.axaml.cs` — **hors
> périmètre backend** — donc un nouveau contrat n'aurait pas pu être résolu en production, et la fiche aurait été
> inerte. La fiche étant un agrégat strictement possédé par la commande (accès uniquement par `OrderId`, jamais
> supprimée), le placement reste cohérent. Extraction possible quand le redesign touchera la composition root.

---

## 22. Reports explicites

| Sujet | Étape |
|---|---|
| UI de la fiche, impression, PDF, export, mise en page | **redesign global** |
| Retrait du montant et du téléphone de la vue `FabricationSheetView` | **redesign global** (le modèle persisté ne les porte déjà pas) |
| Auteur du contrôle qualité (`ICurrentUserService`, rôles fins) | **P3-10** / chantier transverse |
| Points de contrôle QC séparés (montage / finition) | **V2 fiche** |
| Mesures porteur, cotes de montage, décentrement | **V2 fiche** (absentes du modèle, non nécessaires au backend minimal) |
| Validations monétaires vente, client archivé en vente, `SaleStatus` ↔ `OrderStatus` | **P3-7** |
| Règles générales de notifications | **P3-8** |
| Jeton de concurrence natif, index partiel natif, provider serveur | **chantier production** (ADR-PROD-DB-001) |
| Immutabilité forte de l'ordonnance, FK `PrescriptionId` | **dette P3-3, non résolue** |

**Réserves honnêtes** :

- ~~La **génération concurrente** est prouvée par un test d'**invariant de sûreté** …, pas par un entrelacement
  forcé.~~ **Réserve levée au §24bis** : l'ordre est désormais forcé de façon déterministe en transmettant la
  précondition de version qu'un poste « avait lue », ce qui rend l'issue de la course prévisible (un succès, un
  conflit) sans dépendre de l'ordonnanceur. SQLite reste à écrivain unique : les tests prouvent la **correction**
  du compare-and-swap, pas le comportement en charge.
- L'index unique **filtré** est vérifié sur SQLite ; son équivalent natif (index partiel PostgreSQL) devra être
  reconfirmé lors du changement de provider.
- `Order` reste **de facto** un ordre de fabrication portant des attributs fournisseur inertes (P3-6, non traité).

---

## 23. État Git final

```
git status --short
 M docs/architecture/P3-business-rules-roadmap.md
 M src/MMV.Application/DependencyInjection.cs
 M src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusResult.cs
 M src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs
 M src/MMV.Domain/Exceptions/DomainExceptions.cs
 M src/MMV.Domain/Interfaces/Repositories/IOrderRepository.cs
 M src/MMV.Infrastructure/Data/OpticDbContext.cs
 M src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs
 M src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs
 M src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs
 M src/MMV.Infrastructure/Repositories/OrderRepository.cs
?? docs/implementation/P3-6B-workshop-sheet-audit-report.md
?? docs/implementation/P3-6B-workshop-sheet-implementation-report.md
?? src/MMV.Application/UseCases/WorkshopSheets/
?? src/MMV.Domain/Entities/WorkshopSheet.cs
?? src/MMV.Domain/Entities/WorkshopSheetItem.cs
?? src/MMV.Domain/Enums/WorkshopSheetQcStatus.cs
?? src/MMV.Domain/Optics/OpticalCorrection.cs
?? src/MMV.Domain/Optics/OpticalTranspositionResult.cs
?? src/MMV.Domain/Optics/OpticalTranspositionService.cs
?? src/MMV.Domain/Services/WorkshopSheetFactory.cs
?? src/MMV.Domain/Services/WorkshopSheetFingerprint.cs
?? src/MMV.Domain/Services/WorkshopSheetPolicy.cs
?? src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs
?? src/MMV.Infrastructure/Data/Configurations/WorkshopSheetItemConfiguration.cs
?? src/MMV.Infrastructure/Migrations/20260719003826_AddWorkshopSheets.cs
?? src/MMV.Infrastructure/Migrations/20260719003826_AddWorkshopSheets.Designer.cs
?? tests/MMV.Application.Tests/UseCases/WorkshopSheets/
?? tests/MMV.Domain.Tests/Data/WorkshopSheetsMigrationTests.cs
?? tests/MMV.Domain.Tests/OpticsTests/OpticalTranspositionServiceTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/WorkshopSheetFactoryTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/WorkshopSheetFingerprintTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/WorkshopSheetPolicyTests.cs
?? design-handoff/  ?? design/  ?? docs/ui/   (préexistants, non touchés)
```

Changements strictement limités au périmètre autorisé. **Aucun commit. Aucun push. Aucune UI.**

---

## 24bis. Durcissement concurrence avant commit

> Mode `TARGETED_CONCURRENCY_HARDENING_NO_COMMIT`. Deux invariants multi-poste corrigés **avant** le commit
> P3-6B, plus une vérification ciblée de l'adoption partielle. Aucune UI, aucun commit, aucun push.

### 1. Preuve du risque initial

**Course QC (confirmée par le code, pas supposée).** Dans `AdvanceOrderStatusUseCase`, la garde
`EnsureWorkshopSheetAllowsReadyAsync` lisait la fiche courante *puis* `TryTransitionStatusAsync` appliquait un
`UPDATE … WHERE OrderId = @id AND Status = @expected` — **sans aucune condition sur la fiche**. Les deux
opérations étaient bien dans la même transaction, mais une transaction SQLite en lecture ne pose **aucun
verrou** sur les lignes lues : le scénario suivant passait donc intégralement.

| # | Poste A | Poste B | État réel |
|---|---|---|---|
| 1 | lit la v1 : `IsCurrent`, `Passed`, à jour | — | v1 courante `Passed` |
| 2 | — | régénère : v1 → non courante, **v2 `Pending` courante** | v2 courante `Pending` |
| 3 | `UPDATE Orders SET Status='Ready' WHERE Status='QualityCheck'` ⇒ **1 ligne** | — | **commande `Ready`** |

Résultat : la commande passait « Prête » alors que la fiche **autoritaire** n'avait jamais été contrôlée. Le
contrôle qualité invoqué portait sur une version devenue non courante.

**Double génération (confirmée).** `CreateNextWorkshopSheetVersionAsync` relisait `MAX(Version) + 1` et basculait
la version courante par `WHERE OrderId = @id AND IsCurrent` — condition qui ne désigne **pas** la version que
l'appelant avait lue. Deux demandes parties de la v1 produisaient donc une v2 **et** une v3 : la seconde
s'appuyait silencieusement sur une régénération qu'elle n'avait jamais vue, au lieu de recevoir un conflit.
L'unicité `(OrderId, Version)` n'y pouvait rien — les deux numéros étaient distincts.

L'affirmation du §14 (« une régénération concurrente ne peut pas se glisser entre la vérification et la
transition ») et la conception du §10 sont **corrigées en place** ci-dessus.

### 2. Primitive atomique retenue

`IOrderRepository.TryTransitionWithWorkshopSheetAsync` — une **seule** instruction conditionnelle, où l'exigence
de fiche est un `EXISTS` / `NOT EXISTS` corrélé évalué **par le moteur, au moment de l'écriture** :

```
UPDATE Orders SET Status = @next
WHERE  OrderId = @id
  AND  Status  = @expected
  AND  ( @expectedSheetId IS NULL
         ? NOT EXISTS (SELECT 1 FROM WorkshopSheets WHERE OrderId = @id)
         : EXISTS (SELECT 1 FROM WorkshopSheets
                   WHERE WorkshopSheetId = @expectedSheetId AND OrderId = @id
                     AND IsCurrent AND QcStatus = 'Passed'
                     AND TechnicalFingerprint = @expectedFingerprint) )
```

Aucune vérification lecture-puis-écriture séparée ne subsiste sur ce chemin. L'empreinte calculée reste comparée
à celle du snapshot (garde métier, message précis), **et** la condition atomique exige en plus que la fiche
vérifiée soit toujours la fiche courante attendue.

### 3. Distinction conflit de statut / QC non satisfait

`OrderReadyTransitionOutcome` : `Taken` / `StatusConflict` / `WorkshopSheetRequirementNotMet`. Quand zéro ligne
est affectée, **la décision est déjà prise** (rien n'a été écrit) ; une lecture du statut réel sert uniquement à
*diagnostiquer* laquelle des deux conditions a cédé, afin de renvoyer le bon message :

- `StatusConflict` ⇒ `OrderStatusConflictException` (rafraîchir la commande) ;
- `WorkshopSheetRequirementNotMet` ⇒ `BusinessRuleException(WorkshopSheetPolicy.ReadyRequiresUnchangedCurrentSheetMessage)`.

Aucun texte EF ou SQLite ne franchit l'Infrastructure ; le contrat Domain/Application reste sans EF (l'enum et la
précondition sont des types Domain purs).

### 4. Cas historique sans fiche

L'autorisation historique est elle aussi **atomique** : `expectedWorkshopSheetId = null` déclenche un
`NOT EXISTS` réel au moment de l'`UPDATE`. Une fiche `Pending` créée entre la lecture « aucune fiche » et
l'écriture **invalide** la transition. `Ready` n'est jamais accordé sur la foi d'une ancienne lecture.

### 5. CAS de génération de version

`CreateNextWorkshopSheetVersionAsync` reçoit une `WorkshopSheetVersionPrecondition` (identité **et** numéro de la
version courante lue). Plus aucun `MAX(Version)` :

1. `UPDATE WorkshopSheets SET IsCurrent = 0 WHERE WorkshopSheetId = @expectedId AND OrderId = @orderId AND IsCurrent` ;
2. exactement **une** ligne affectée, sinon `WorkshopSheetVersionConflictException` ;
3. nouvelle version = `expectedVersion + 1` ;
4. insertion protégée par l'unique `(OrderId, Version)` et l'unique filtré de version courante.

Aucune re-tentative opaque dans la transaction. `WorkshopSheetVersionPrecondition.None` (aucune version lue)
vise la v1 : il n'y a rien à basculer, ce sont les contraintes uniques qui arbitrent — conflit contrôlé, jamais
un doublon. `EnsureCurrentSheetAsync` reste idempotente lorsqu'une fiche existe, et la prise atomique du statut
continue d'empêcher deux générations automatiques sur la même transition.

**Deux générations concurrentes parties de la v1** : une seule v2, un seul succès, un
`WorkshopSheetVersionConflictException` pour l'autre, **aucune v3**, une seule version courante.

### 6. Validation QC

Vérifiée, **inchangée** : `TryTakeWorkshopSheetQcDecisionAsync` prend déjà la décision par un `UPDATE`
conditionnel sur `WorkshopSheetId` + `IsCurrent` + `QcStatus = 'Pending'`. Aucune condition ne manquait ; aucune
décision définitive n'est rouverte.

### 7. Schémas partiels

Les quatre états sont désormais traités explicitement, **avant** toute écriture d'historique :

| `WorkshopSheets` | `WorkshopSheetItems` | Comportement |
|---|---|---|
| absente | absente | migration `AddWorkshopSheets` **exécutée** |
| présente | présente | extension considérée présente (baseline) |
| présente | absente | **échec explicite** de schéma partiel |
| absente | présente | **échec explicite** de schéma partiel |

Le contrôle est placé juste après le portail de compatibilité, donc **avant** `WriteMigrationHistory` : un état
partiel n'écrit **aucune** entrée dans `__EFMigrationsHistory`. Auparavant, `présente/absente` était baselinée
puis rattrapée seulement par la vérification post-adoption (l'historique était déjà écrit), et `absente/présente`
tentait la migration, faisant échouer un `CreateTable` avec un message technique brut. La vérification physique
post-adoption est **conservée** comme filet. Aucune ancienne migration modifiée.

### 8. Fichiers modifiés

**Créés (4)** — `src/MMV.Domain/Enums/OrderReadyTransitionOutcome.cs`,
`src/MMV.Domain/Entities/WorkshopSheetVersionPrecondition.cs`,
`tests/MMV.Application.Tests/UseCases/WorkshopSheets/WorkshopSheetConcurrencyHardeningTests.cs`,
`tests/MMV.Domain.Tests/Data/WorkshopSheetsAdoptionTests.cs`.

**Modifiés (6)** — `src/MMV.Domain/Interfaces/Repositories/IOrderRepository.cs` (primitive de transition liée à
la fiche ; précondition de version), `src/MMV.Domain/Services/WorkshopSheetPolicy.cs` (message de refus
concurrent), `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs` (prise
enrichie + distinction des deux échecs), `src/MMV.Application/UseCases/WorkshopSheets/WorkshopSheetOperations.cs`
et `.../GenerateWorkshopSheet/GenerateWorkshopSheetUseCase.cs` (transmission de la précondition),
`src/MMV.Infrastructure/Repositories/OrderRepository.cs` (les deux primitives),
`src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` (refus de schéma partiel avant baseline).

**Aucune nouvelle migration** ; le modèle persisté est inchangé (`has-pending-model-changes = false`). Aucun
fichier sous `src/MMV.App/**`, `design-handoff/**`, `design/**`, `docs/ui/**`.

### 9. Tests ajoutés (+14)

**Course QC / transition — `WorkshopSheetConcurrencyHardeningTests` (10)** : v2 `Pending` devenue courante ⇒
transition refusée, commande toujours `QualityCheck`, v2 toujours courante ; fiche `Passed` inchangée ⇒ succès ;
statut réel obsolète ⇒ `StatusConflict` ; historique sans fiche puis fiche `Pending` créée avant la prise ⇒
refus ; aucune fiche au moment atomique ⇒ autorisation ; use case complet avec régénération forcée entre la
garde et la prise ⇒ `BusinessRuleException`, **aucune notification**, statut inchangé ; message sans détail
technique ; deux générations parties de la v1 ⇒ une v2 + un conflit, **aucune v3**, une seule version courante ;
deux créations concurrentes de v1 ⇒ une seule fiche ; génération automatique idempotente.

**Adoption — `WorkshopSheetsAdoptionTests` (4)** : les quatre combinaisons de présence, les deux états partiels
échouant proprement **sans** table `__EFMigrationsHistory` créée.

> **Déterminisme de l'entrelacement.** SQLite n'admet qu'un seul écrivain : écrire depuis une seconde connexion
> pendant que la transaction du use case est ouverte produit un verrou, pas une course. Les tests forcent donc
> l'ordre exact soit en appelant la primitive du repository avec la précondition qu'un poste « avait lue »
> (connexions réellement distinctes, hors transaction), soit via un décorateur de repository qui déclenche la
> régénération à un point choisi du use case. **Aucun `Task.Delay`, aucune dépendance à l'ordonnanceur.**

### 10. Résultats exacts

| Assembly | Réussis | Échec | Ignoré |
|---|---|---|---|
| `MMV.Domain.Tests` | 430 | 0 | 0 |
| `MMV.Application.Tests` | 362 | 0 | 0 |
| `MMV.App.Tests` | 239 | 0 | 0 |
| **Total** | **1031** | **0** | **0** |

- `dotnet build MMV.sln -c Debug` : **0 erreur / 0 avertissement**
- `dotnet list package --vulnerable --include-transitive` : **aucune vulnérabilité** (7 projets)
- `dotnet ef migrations has-pending-model-changes` : *No changes have been made to the model since the last migration*
- `MMV.Application` → `MMV.Domain` **uniquement** ; package inchangé
- `git diff --check` : aucune erreur d'espaces ; aucun fichier UI modifié

**P3-6B HARDENED = GO LOCAL** (sous réserve de la CI du futur commit).

---

## Préparation du commit

- **HEAD de départ** : `a4d7380b374167f4b27809e9b4a7e56b28bf3c48`.
- **Total exact** : **1031 tests** (`MMV.Domain.Tests` 430, `MMV.Application.Tests` 362, `MMV.App.Tests` 239), **0
  échec, 0 ignoré** — reconfirmé par exécution directe (`dotnet test`, chaque assembly puis `MMV.sln` complet),
  pas déduit.
- **Build** : `dotnet build MMV.sln -c Debug` → **0 erreur / 0 avertissement**.
- **Vulnérabilités** : `dotnet list package --vulnerable --include-transitive` → **aucune** sur les 7 projets.
- **Migrations en attente** : `dotnet ef migrations has-pending-model-changes` → *No changes have been made to
  the model since the last migration*.
- **Migration P3-6B** : `20260719003826_AddWorkshopSheets`, **purement additive**, seule migration nouvelle ;
  aucune ancienne migration modifiée.
- **UI** : aucun fichier sous `src/MMV.App/**` modifié ; `MMV.App.Tests` inchangé à 239.
- **Architecture** : `MMV.Application` référence `MMV.Domain` uniquement ; package inchangé
  (`Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1`).
- **Périmètre prévu du commit** :
  - **Domain** : `WorkshopSheet`, `WorkshopSheetItem`, `WorkshopSheetVersionPrecondition`,
    `WorkshopSheetQcStatus`, `OrderReadyTransitionOutcome`, `WorkshopSheetPolicy`, `WorkshopSheetFactory`,
    `WorkshopSheetFingerprint`, `OpticalCorrection`, `OpticalTranspositionResult`,
    `OpticalTranspositionService`, `DomainExceptions.cs` (modifié), `IOrderRepository.cs` (modifié).
  - **Application** : `UseCases/WorkshopSheets/**`, `AdvanceOrderStatusUseCase.cs` / `AdvanceOrderStatusResult.cs`
    (modifiés), `DependencyInjection.cs` (modifié).
  - **Infrastructure** : `Data/Configurations/WorkshopSheetConfiguration.cs`,
    `Data/Configurations/WorkshopSheetItemConfiguration.cs`, `OpticDbContext.cs` (modifié),
    `OrderRepository.cs` (modifié), `SqliteSchemaVerifier.cs` (modifié), `SqliteDatabaseManager.cs` (modifié),
    `Migrations/20260719003826_AddWorkshopSheets(.Designer).cs`, `OpticDbContextModelSnapshot.cs` (modifié).
  - **Tests** : `tests/MMV.Domain.Tests/OpticsTests/OpticalTranspositionServiceTests.cs`,
    `ServiceTests/WorkshopSheetFactoryTests.cs`, `ServiceTests/WorkshopSheetFingerprintTests.cs`,
    `ServiceTests/WorkshopSheetPolicyTests.cs`, `Data/WorkshopSheetsMigrationTests.cs`,
    `Data/WorkshopSheetsAdoptionTests.cs`, `tests/MMV.Application.Tests/UseCases/WorkshopSheets/**`.
  - **Documentation** : `docs/implementation/P3-6B-workshop-sheet-audit-report.md`,
    `docs/implementation/P3-6B-workshop-sheet-implementation-report.md`,
    `docs/architecture/P3-business-rules-roadmap.md`.
- **Hors périmètre** : `design-handoff/`, `design/`, `docs/ui/` restent non suivis ; aucun fichier
  `src/MMV.App/**`.

## **P3-6B HARDENED = GO LOCAL**

(sous réserve de la CI du commit à venir)

---

## 24. Verdict

- Toute commande créée après P3-6B obtient une fiche avant `QualityCheck` ✅ (test dédié)
- Commandes historiques non bloquées rétroactivement, aucun QC inventé ✅
- Snapshot complet (prisme/usage/acuité inclus) et minimal en données personnelles ✅
- Versions immuables ; une seule version courante (index unique filtré) ✅
- Contrôle qualité atomique et définitif ✅
- `QualityCheck → Ready` bloqué si la fiche existante n'est pas `Passed` et à jour ✅, et **atomiquement lié** à
  la fiche courante attendue — y compris le cas historique sans fiche (§24bis) ✅
- Deux demandes concurrentes de nouvelle version ⇒ un succès, un conflit, **aucune v3** (§24bis) ✅
- Schémas partiels de fiche atelier refusés sans faux baseline (§24bis) ✅
- Transposition **pure**, source jamais modifiée (garantie structurelle + tests) ✅
- Migration additive, sans backfill ; adoption des bases historiques corrigée ✅
- **1031 tests** réussis / 0 échec / 0 ignoré (1017 + 14 au durcissement) ; 0 vulnérabilité ; aucune migration
  en attente ✅
- Rapport `.md` présent ✅ ; aucune UI modifiée ✅

## **P3-6B HARDENED = GO LOCAL**

(subordonné au vert de la CI du commit — **non** marqué définitivement terminé)

Aucun commit. Aucun push. Aucune UI. STOP.
