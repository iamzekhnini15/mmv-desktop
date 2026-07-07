# P3-1 — Standardisation des validations / `Result`

> Socle transverse de la phase **P3 (règles métier)**. Pose une convention minimale de **validation de
> commande** et de **résultat métier** côté Application, appliquée à un **domaine pilote** (Clients), sans
> réécrire les use cases existants ni ajouter de dépendance. **Le dépôt réel prime toujours sur ce document.**

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-1 |
| `EXECUTION_MODE` | IMPLEMENTATION_AND_REPORT |
| `SOURCE_BRANCH` | p3-business-rules |
| `ALLOW_CODE_CHANGES` | true |
| `ALLOW_TEST_CHANGES` | true |
| `ALLOW_DOC_CHANGES` | true |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : `p3-business-rules` ✅
- Working tree : **propre** au démarrage (`git status --short` vide) ✅
- HEAD : `870c81a docs(P3-0B): record multi-workstation DB strategy CI validation` ✅

**Précondition = GO.**

## 3. Baseline

| Contrôle | Attendu | Résultat |
|---|---|---|
| `dotnet restore MMV.sln` | OK | OK (à jour) ✅ |
| `dotnet build … -c Debug` | vert | **Réussi — 0 avertissement, 0 erreur** ✅ |
| `dotnet test … --no-build` | ≥ 585 | **585** (App 192 · Application 170 · Domain 223) ✅ |
| `dotnet list … --vulnerable --include-transitive` | 0 | **0 vulnérabilité** (7 projets) ✅ |
| `dotnet tool restore` | OK | `dotnet-ef` 8.0.27 ✅ |
| `dotnet ef migrations has-pending-model-changes` | false | **false** ✅ |
| `MMV.Application` référence projet | `MMV.Domain` seul | `..\MMV.Domain\MMV.Domain.csproj` seul ✅ |
| `MMV.Application` packages (top-level) | `DI.Abstractions` seul | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul ✅ |

**Baseline = GO.**

## 4. Audit des validations existantes

**Domain (4 validateurs FluentValidation `AbstractValidator<Entity>`)** :
`CustomerValidator`, `PrescriptionValidator`, `ProductValidator`, `UserValidator`. FluentValidation est une
dépendance **Domain** (`FluentValidation` **11.9.0**, licence Apache-2.0).

- `CustomerValidator` (pilote) : `FirstName`/`LastName` **requis** + `MaximumLength(100)` ; `Email` **valide
  si renseigné** (`.EmailAddress().When(non blanc)`) + `MaximumLength(200)` ; `Phone` `MaximumLength(20)` si
  renseigné ; `SocialSecurityNumber` `MaximumLength(30)`.

**Application** : **aucun** validateur avant P3-1 (`src/MMV.Application/**` ne contenait aucun `*Validator*`).
Les use cases d'écriture construisaient l'entité et persistaient **sans** revalider la commande.

**UI** : `CustomerFormViewModel.ValidateProperty` fait une **garde de saisie** ergonomique — `FirstName`/
`LastName` requis, `Email` « contient `@` et `.` », `Phone ≥ 10` caractères. C'est une prévention de surface
(présentation), pas l'autorité métier.

**Duplication constatée** : `FirstName`/`LastName` requis vivait à la fois en Domain (invariant) et en UI
(garde ergonomique) — toléré par l'ADR (§9 : l'UI *prévient* l'invariant, ne le *possède* pas). La validation
**Application** manquait entre les deux : une commande invalide atteignant directement le use case était
persistée sans contrôle.

## 5. Audit des `Result` existants

Chaque use case renvoie un `*Result` **dédié** (DTO plat, aucun type EF). Patterns observés :

- **« Introuvable » homogène** : drapeau booléen `*Found` (`CustomerFound`, `OrderFound`, `ProductFound`,
  `UserFound`, `SupplierFound`, `PrescriptionFound`) posé à `false` **sans écriture** quand l'entité ciblée
  n'existe pas (Update/Delete/Advance/Settle). Contrat déjà uniforme.
- **Règle métier ponctuelle** : `UpdateUserResult.UsernameTaken` (booléen) — seul cas d'un refus métier porté
  par un `Result` ; ailleurs les refus durs passent par exceptions typées.
- **Exceptions métier typées** (Domain) : `DomainException`, `BusinessRuleException`, `EntityNotFoundException`,
  `DuplicateEntityException`, `InsufficientStockException`, `NumberSequenceException` — propagées inchangées
  (ADR §8 : succès via `Result`, échecs métier/techniques via exceptions typées).
- **Aucune** monade `Result<T>`, **aucune** liste d'erreurs de validation exposée avant P3-1.

## 6. Évaluation framework / dépendance — P3-1

### 6.1 Validation

| Axe | Constat |
|---|---|
| **Solution existante dans le code** | Domain expose déjà des validateurs FluentValidation par entité (dont `CustomerValidator`). Application : rien. UI : gardes de saisie. |
| **Framework standard possible** | (a) Ajouter FluentValidation en **top-level** à Application + écrire des `*CommandValidator` ; (b) réutiliser les validateurs **Domain** existants depuis Application ; (c) gardes explicites codées à la main. |
| **Choix retenu** | **(b) Réutiliser les validateurs Domain existants.** Le use case construit le candidat normalisé et le valide via un helper unique `Common/CommandValidation`, qui exécute le `CustomerValidator` Domain et projette le résultat vers des `ValidationError` neutres. |
| **Justification** | Le moins intrusif : la règle reste **propriétaire du Domain** (ADR §9, pas de duplication), aucun `*CommandValidator` parallèle à maintenir, aucun framework maison. Écarté (a) : dupliquerait les règles et ajouterait une dépendance top-level inutile. Écarté (c) : ré-encoderait à la main des règles déjà exprimées et testées. |
| **Impact NuGet** | **Aucun paquet top-level ajouté.** FluentValidation transite **déjà** depuis le Domain (`--include-transitive`) ; `dotnet list package` (top-level) reste `DI.Abstractions` seul. |
| **Impact licence** | FluentValidation 11.9.0 = **Apache-2.0** (libre, usage commercial autorisé). *(N.B. : le pin « 6.x » de la mémoire concerne **FluentAssertions** — bibliothèque de test distincte — pas FluentValidation.)* |
| **Impact architecture** | `MMV.Application` reste pure : référence projet `MMV.Domain` seule ; aucun EF/Avalonia/MVVM ; le contrat public des `*Result` n'expose **pas** de type FluentValidation (isolé dans `CommandValidation`). |

### 6.2 Result / erreurs métier

| Axe | Constat |
|---|---|
| **Solution existante dans le code** | `*Result` dédiés ; « introuvable » = `*Found=false` homogène ; refus métier durs = exceptions typées Domain ; `UsernameTaken` ponctuel. |
| **Framework standard possible** | `Ardalis.Result` (Result générique + statuts NotFound/Invalid/…). |
| **Choix retenu** | **Ne pas ajouter `Ardalis.Result` en P3-1.** Standardiser progressivement l'existant : conserver le drapeau `*Found`, ajouter une liste `ValidationErrors` + `IsValid` sur les `*Result` d'écriture concernés. |
| **Justification** | Introduire une monade transverse imposerait un refactor de tous les use cases (churn massif) pour un desktop à faible volume — explicitement hors périmètre P3-1. L'existant (`*Found`) est déjà homogène ; on l'étend au lieu de le remplacer. |
| **Impact NuGet** | **Aucun.** |
| **Impact licence** | Sans objet. |
| **Impact architecture** | Additif et rétro-compatible : les `*Result` gagnent des membres, aucun contrat retiré ; l'UI existante (`result.CustomerFound`) compile et se comporte à l'identique. |

## 7. Choix retenu (synthèse)

- **Validation** : réutilisation des **validateurs FluentValidation du Domain** depuis Application via
  `Common/CommandValidation` ; **aucune** dépendance ajoutée ; **aucun** framework maison.
- **Result** : conservation du drapeau `*Found` + ajout d'une liste `ValidationErrors` neutre et d'un
  `IsValid` calculé sur les `*Result` d'écriture ; **pas** de monade `Result<T>`, **pas** d'`Ardalis.Result`.

## 8. Convention cible (socle P3-1)

Types neutres dans `src/MMV.Application/Common/` :

- `ValidationError(string PropertyName, string Message)` — erreur de validation de commande, sans fuite de
  FluentValidation dans le contrat public.
- `CommandValidation.Validate<T>(IValidator<T>, T)` — exécute un validateur Domain, renvoie
  `IReadOnlyList<ValidationError>` (vide = valide).

Contrat des `*Result` d'écriture :

| Cas | Signalisation |
|---|---|
| **Commande invalide** | `ValidationErrors` renseignée, `IsValid=false`, **aucune écriture** (nouveau P3-1) |
| **Entité introuvable** | `*Found=false`, **aucune écriture** (inchangé) |
| **Règle métier refusée** (invariant dur) | exception typée `DomainException`/`BusinessRuleException` (ADR §8) — phases suivantes |
| **Succès** | `*Result` peuplé, `ValidationErrors` vide, `*Found=true` |

## 9. Domaine pilote choisi

**Clients** : `CreateCustomerUseCase` et `UpdateCustomerUseCase`. Retenu car domaine simple, `CustomerValidator`
Domain déjà présent, faible risque métier, et prochaine étape métier P3-2 = Clients.

## 10. Modifications réalisées

**Créés :**

- `src/MMV.Application/Common/ValidationError.cs` — DTO neutre d'erreur de validation.
- `src/MMV.Application/Common/CommandValidation.cs` — helper unique validateur Domain → `ValidationError[]`.

**Modifiés :**

- `src/MMV.Application/Common/README.md` — documente la convention adoptée.
- `CreateCustomerUseCase.cs` — valide le candidat normalisé **avant** toute écriture ; commande invalide ⇒
  aucun accès dépôt, `ValidationErrors` renvoyée.
- `CreateCustomerResult.cs` — `ValidationErrors` + `IsValid`.
- `UpdateCustomerUseCase.cs` — après le contrôle « introuvable » existant, valide la commande **avant**
  `UpdateAsync`/`SaveChangesAsync` ; commande invalide ⇒ aucune écriture (`CustomerFound=true`, `IsValid=false`).
- `UpdateCustomerResult.cs` — `ValidationErrors` + `IsValid`.

**Non touché** : `Infrastructure`, `App`/UI, `.github`, migrations, provider DB, `.csproj`. La signature des
constructeurs des use cases est **inchangée** (validateur instancié en statique, pas d'injection nouvelle).

## 11. Tests ajoutés / modifiés

Ajoutés dans les fichiers de tests existants (vrai SQLite temporaire, comme les tests d'origine) :

**`CreateCustomerUseCaseTests`** (+4) :
- `BlankFirstName_FailsValidation_AndDoesNotWrite` — `IsValid=false`, `CustomerId=0`, base vide.
- `BlankLastName_FailsValidation_AndDoesNotWrite`.
- `InvalidEmail_FailsValidation_AndDoesNotWrite` (email renseigné mais invalide).
- `BlankEmail_RemainsValid_AndPersists` — **comportement existant conservé** (email vide autorisé).

**`UpdateCustomerUseCaseTests`** (+3) :
- `BlankFirstName_FailsValidation_AndDoesNotWrite` — `CustomerFound=true`, `IsValid=false`, ligne **inchangée**.
- `BlankLastName_FailsValidation_AndDoesNotWrite`.
- `InvalidEmail_FailsValidation_AndDoesNotWrite` — email initial conservé.

Les tests valident le **comportement réel du use case** (persistance / non-persistance observée en base),
pas seulement la classe de validation isolée. Cas valides, « introuvable » et `ArgumentNullException`
existants : **conservés, verts**.

## 12. Contrôles exécutés

`git branch --show-current` · `git status --short` · `git log` · `git diff --stat` · `git diff --check` ·
`dotnet restore` · `dotnet build -c Debug` · `dotnet test --no-build -c Debug` ·
`dotnet list … --vulnerable --include-transitive` · `dotnet tool restore` ·
`dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` ·
`dotnet list src/MMV.Application/MMV.Application.csproj reference` / `… package`.

## 13. Résultats (contrôles finaux)

| Contrôle | Attendu | Résultat |
|---|---|---|
| Build | vert | **Réussi — 0 avertissement, 0 erreur** ✅ |
| Tests | > 585 | **592** (App 192 · Application **177** · Domain 223) ✅ |
| Vulnérabilités | 0 | **0** (7 projets) ✅ |
| EF `has-pending-model-changes` | false | **false** ✅ |
| Migration ajoutée | aucune | aucune ✅ |
| `MMV.Application` référence | `MMV.Domain` seul | ✅ |
| `MMV.Application` package top-level | `DI.Abstractions` seul | ✅ |
| `git diff --check` | propre | propre ✅ |
| Zones touchées | Application + Application.Tests seules | ✅ (aucun Infrastructure/App/.github/migration) |

## 14. Risques résiduels

1. **Durcissement de l'email** (conscient, documenté) : Application applique désormais `.EmailAddress()`
   (validateur Domain) là où la garde UI se contentait de « `@` et `.` ». Un email malformé qui franchissait
   l'UI (ex. `a@b.`) est maintenant **refusé sans écriture** au niveau use case. C'est l'objet même de P3-1
   (l'Application devient l'autorité de validation) ; aucun test existant ne l'utilisait, aucun ne casse.
2. **UI non recâblée sur `ValidationErrors`** : l'UI conserve sa garde de saisie et n'affiche pas encore les
   `ValidationErrors` de l'Application (modification `MMV.App` hors périmètre P3-1). Pour les flux valides, le
   comportement observable est identique ; l'exploitation UI des erreurs applicatives relève d'une phase UI.
3. **Convention appliquée au pilote seulement** : les autres domaines gardent leur `*Result` actuel jusqu'à
   leur étape dédiée (extension progressive assumée, pas de churn transverse).
4. **`BusinessRuleError` non matérialisé** : les refus de règle métier dure restent des exceptions typées
   (ADR §8) ; le premier cas concret (garde de suppression client avec historique) arrive en P3-2.

## 15. Verdict

**P3-1 = GO local.**

- Audit validations réalisé ✅ · audit `Result` réalisé ✅
- Choix framework documenté (validation + Result) ✅ · aucun framework maison inutile ✅
- Aucune dépendance NuGet ajoutée (FluentValidation déjà transitif Domain) ✅
- Convention validation/Result définie et documentée ✅
- Domaine pilote **Clients** appliqué (`Create`/`Update`) ✅
- Build vert · **592 tests** · 0 vulnérabilité · EF vert · `MMV.Application` pure ✅

## 16. Validation CI

| Élément | Valeur |
|---|---|
| Run CI | [actions/runs/28905699337](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/28905699337) |
| Identifiant run | `28905699337` |
| Commit testé | `6e39bd27eaa75973b70217f058a117660d14eab8` |
| Branche testée | `p3-business-rules` |
| Événement | `push` |
| Restore | ✅ réussi (tous projets restaurés) |
| Build | ✅ réussi |
| Test | ✅ réussi — **592 tests** (App **192** · Application **177** · Domain **223**), 0 échec |
| Audit NuGet (vulnérabilités) | ✅ **0 vulnérabilité** (7 projets scannés) |
| Restore .NET tools | ✅ `dotnet-ef` 8.0.27 restauré |
| Check EF Core pending model changes | ✅ « No changes have been made to the model since the last migration. » |
| Statut final du workflow | ✅ `completed` / `success` (durée 3m15s) |

**P3-1 = GO DÉFINITIF (commit code).**

## 17. Prochaine étape candidate

**P3-2 — Clients** : garde-fou de suppression avec historique (archivage vs refus documenté, cascade EF à
trancher), politique de doublon souple. La convention P3-1 (`ValidationErrors`/`IsValid` + `*Found` + exceptions
métier typées pour les refus durs) y sert de socle. **Ne pas démarrer P3-2 avant la CI verte du commit
documentaire P3-1** (§16 de ce document, second commit).
