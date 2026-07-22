# P3-12 — Remédiation du blocage final : conservation des ordonnances

> **Mode** : `REMEDIATE_FINAL_P3_BLOCKER_AND_WRITE_REPORT_NO_COMMIT`.
> Aucun commit, push, merge, rebase ou cherry-pick. Aucune migration, aucun snapshot EF, aucune UI, aucune
> Infrastructure, aucun paquet, aucun nouveau projet. P4 non commencé.
>
> ⚠️ **Portée de cet en-tête.** Il décrit le **mandat de rédaction initial**, où rien n'était encore committé.
> Le correctif a **depuis** été committé, poussé et validé en CI : voir **§18**, qui porte le verdict
> définitif. Les mentions « aucun commit » du corps décrivent l'état au moment de leur écriture et sont
> conservées comme trace.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | P3-12 (remédiation) |
| HEAD de départ | `4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365` |
| Run CI de départ | `29958212378` — `success` |
| Baseline de départ | 1493 tests (Domain 647 · Application 607 · App 239) |
| Portée | Correctif backend minimal + tests + documentation |

---

## 2. État Git et CI au démarrage

```
git branch --show-current              → p3-business-rules
git rev-parse HEAD                     → 4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365
git rev-parse origin/p3-business-rules → 4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365
git status --short                     → ?? design-handoff/  ?? design/  ?? docs/ui/
                                         ?? docs/implementation/P3-12-final-business-audit-report.md
git diff --check                       → (vide, exit 0)
git diff --stat                        → (vide)
git diff --name-status                 → (vide)
```

- HEAD local **et** distant identiques au SHA attendu.
- **Aucun fichier suivi modifié** au démarrage.
- Seul fichier nouveau suivi du périmètre P3-12 : le rapport d'audit final. Les trois dossiers
  `design-handoff/`, `design/`, `docs/ui/` sont hors périmètre et le restent.

CI (`gh run view 29958212378`) :

| Champ | Valeur |
|---|---|
| `databaseId` | `29958212378` |
| `headSha` | `4c2a1e56b55d6bfc32ec4a8a288ce7fd56cd8365` (exact) |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | **`success`** |

**Portes d'entrée : franchies.**

---

## 3. Baseline de départ

| Contrôle | Commande | Résultat |
|---|---|---|
| Restore | `dotnet restore MMV.sln` | OK |
| Build | `dotnet build MMV.sln --no-restore -c Debug` | **Build succeeded. 0 Warning(s). 0 Error(s).** |
| Solution | `dotnet test MMV.sln --no-build -c Debug` | App **239** · Application **607** · Domain **647** |

**Total : 1493 tests, 0 échec, 0 ignoré.** Conforme à l'attendu.

---

## 4. Blocage découvert par l'audit P3-12

L'audit final (`P3-12-final-business-audit-report.md`, §13) a lu intégralement
`src/MMV.Application/UseCases/Prescriptions/DeletePrescription/DeletePrescriptionUseCase.cs`. Son corps
exécutable était :

```csharp
var prescription = await _prescriptionRepository.GetByIdAsync(command.PrescriptionId, cancellationToken);
if (prescription is null)
    return new DeletePrescriptionResult { PrescriptionFound = false, … };
await _prescriptionRepository.DeleteAsync(prescription, cancellationToken);
await _unitOfWork.SaveChangesAsync(cancellationToken);
```

**La suppression était physique, définitive et inconditionnelle** : aucune vérification d'usage, aucun
archivage, aucune garde métier. P3-3C n'avait ajouté qu'une **confirmation** d'interface.

L'audit avait classé ce point « 🟡 dette acceptée, non bloquante », au motif que l'acte est délibéré, sans
cascade possible et sans perte de document dérivé. **Ce classement est révisé ici.** Le raisonnement était
exact sur l'impact borné, mais il excusait un critère de sortie énoncé sans exception — « suppression sûre
**partout** ». Une boîte de dialogue n'est pas une garantie backend : elle vit dans une ViewModel, ne protège
que le chemin qui la traverse, et tout appelant entrant par la couche Application l'ignore.

**Verdict d'entrée de cette remédiation : `P3-12 AUDIT = NO-GO` / `P3 = NO-GO MERGE`.**

---

## 5. Chemin public réel

Vérifié par inspection directe, pas par lecture de rapport :

| Question | Réponse prouvée |
|---|---|
| Enregistré en DI ? | **Oui** — `src/MMV.Application/DependencyInjection.cs:144` : `services.AddScoped<IDeletePrescriptionUseCase, DeletePrescriptionUseCase>()` |
| Appelé depuis l'UI ? | **Oui** — `CustomerPrescriptionsViewModel.cs:302`, dans `ExecuteDeleteAsync` |
| Atteignable par un utilisateur ? | **Oui** — `CustomersViewModel` → `CustomerDetailViewModel` → `CustomerPrescriptionsViewModel`, déclenché par `OnDeleteRequested` (`:340-345`) depuis la fiche client |
| Chemin mort ? | **Non.** Trois ViewModels le résolvent (`CustomersViewModel:49`, `CustomerDetailViewModel:33`, `CustomerPrescriptionsViewModel:40`) |
| Seule protection avant P3-12 ? | Une confirmation `IDialogService` (`:291-296`) |

`rg 'DeletePrescriptionUseCase|IDeletePrescriptionUseCase|DeletePrescriptionCommand'` sur `src` et `tests` ne
révèle **aucun autre** appelant de production.

### 5.1 Revue finale — recherche exhaustive des chemins de suppression

Recherche élargie (`DeletePrescription`, `DeleteAsync`, `ExecuteDelete`, `Remove(`, `RemoveRange`,
`Prescriptions.Remove`, `DELETE FROM … Prescriptions`) sur `src` et `tests`, `bin`/`obj` exclus. Chaque
occurrence classée :

| Constat | Preuve |
|---|---|
| **Aucun service parallèle** | `IPrescriptionService` / `PrescriptionService` ont été **supprimés en P3-3B** (second chemin d'écriture dormant) — `MMV.Infrastructure/DependencyInjection.cs:67` |
| **Aucune ViewModel n'appelle un repository** | `CustomerPrescriptionsViewModel` passe par `IDeletePrescriptionUseCase` ; `PrescriptionDetailViewModel.ExecuteDelete` ne fait que **lever un événement** (`DeleteRequested`), sans persistance |
| **Aucun SQL brut, aucun `ExecuteDeleteAsync`** | Les seules occurrences visent les **fournisseurs** (`SupplierRepository`) et des **tests** ; aucune ne cible `Prescriptions` |
| **Aucun second use case** | `IPrescriptionRepository` n'expose aucune primitive de suppression propre ; `DeleteAsync` n'est hérité que de `BaseRepository`, et **aucun appelant** ne l'invoque pour une ordonnance |
| **Aucune cascade client** | `Customer → Prescriptions` est en **`DeleteBehavior.Restrict`** des deux côtés (`CustomerConfiguration.cs:70`, `PrescriptionConfiguration.cs:79`). Supprimer un client ne détruit aucune ordonnance — et P3-2B refuse déjà cette suppression |
| **Doubles de test uniquement** | Les occurrences de `MMV.App.Tests` sont des `SpyDeletePrescriptionUseCase` implémentant l'**interface** ; elles ne touchent aucune base |

**Conclusion : `DeletePrescriptionUseCase` est le seul chemin runtime de suppression d'ordonnance du dépôt.**
Le fermer ferme la totalité de la surface — la règle n'a pas de contournement.

---

## 6. Perte de données réelle

- **Ce qui était perdu** : l'enregistrement médical source — sphères, cylindres, axes, additions, prismes,
  acuités, prescripteur, date d'émission.
- **Ce qui ne l'était pas** : les documents dérivés. `SaleItem`, `OrderItem` et `WorkshopSheetItem` portent
  leurs **propres** valeurs optiques (copie **par valeur**). La protection était donc réelle mais
  **accidentelle** — une conséquence du modèle, pas une règle.
- **Aucune rupture référentielle** : il n'existe **aucune** clé étrangère `SaleItem → Prescription` ni
  `OrderItem → Prescription`. `rg 'PrescriptionId'` sur les entités et configurations ne trouve que la clé
  primaire de `Prescription` elle-même.
- **Conséquence décisive** : cette absence de FK signifie qu'**il est impossible de savoir si une ordonnance
  a servi**. Le lien entre une vente et l'ordonnance qui l'a motivée n'est pas représenté. « Inutilisée »
  n'est pas une propriété décidable dans ce modèle.

---

## 7. Décision métier

En l'absence de relation fiable `Prescription ↔ Sale`, de champ d'archivage, de versionnement et de toute
preuve d'usage : **la suppression physique d'une ordonnance est interdite dans tous les cas.**

Explicitement **écarté**, et pour quelle raison :

| Piste écartée | Raison |
|---|---|
| Déduire l'usage des dates | Heuristique ; une erreur détruit un dossier médical sans trace |
| Chercher une vente aux valeurs optiques ressemblantes | Heuristique ; deux yeux peuvent légitimement porter les mêmes valeurs |
| Autoriser au seul motif qu'aucune FK n'existe | Inverse la charge de la preuve : l'absence de lien n'est pas une preuve de non-usage |
| Ajouter un archivage partiel | Un demi-archivage donne une fausse impression de réversibilité |
| Créer une migration | Hors périmètre ; la règle est comportementale, le schéma n'a rien à dire ici |

Archivage, FK `Prescription ↔ Sale`, versionnement et immutabilité forte restent des **entrées du futur
cadrage** — ils ne sont pas implémentés ici.

---

## 8. Implémentation

### 8.1 Propriétaire Domain — `PrescriptionRetentionPolicy`

**Nouveau fichier** : `src/MMV.Domain/Policies/PrescriptionRetentionPolicy.cs` — classe **statique pure**,
sans dépendance EF, SQLite, Application ni UI. Voisine de `UserIdentityPolicy`, dont elle suit la convention.

```csharp
public const string PhysicalDeletionForbiddenMessage =
    "La suppression physique d'une ordonnance est interdite afin de préserver l'historique médical.";
```

**Elle n'expose délibérément aucun prédicat.** La règle est inconditionnelle ; un `CanDelete(…)` répondant
invariablement `false` laisserait croire au lecteur qu'une condition existe et invitera un jour à
l'assouplir. Ce n'est pas un framework : une constante, sa documentation, et rien d'autre.

**Pourquoi Domain et non le use case.** Le patron P3-2B plaçait le message sur le use case
(`DeleteCustomerUseCase.CustomerHasHistoryMessage`). Ici la règle n'est pas une garde d'orchestration mais une
**affirmation sur la nature de la donnée** — un enregistrement médical se conserve. Le Domain en est le
propriétaire légitime, et l'y loger la rend opposable indépendamment du use case qui la porte.

### 8.2 Point de refus — `DeletePrescriptionUseCase`

```csharp
var prescription = await _prescriptionRepository.GetByIdAsync(command.PrescriptionId, cancellationToken);
if (prescription is null)
    return new DeletePrescriptionResult { PrescriptionFound = false, PrescriptionId = command.PrescriptionId };

// P3-12 : refus dur, avant toute mutation. Ni DeleteAsync, ni SaveChangesAsync, ni SQL brut.
throw new BusinessRuleException(PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage);
```

- **`BusinessRuleException`** : convention ADR §8 / P3-1 (refus dur = exception typée Domain), identique à
  `DeleteCustomerUseCase`.
- **Aucune mutation** : `DeleteAsync`, `SaveChangesAsync`, `ExecuteDeleteAsync` et le SQL brut sont
  **absents** du chemin « trouvée ».
- **`IUnitOfWork` entièrement retiré du constructeur.** La revue finale a recherché toute construction manuelle
  (`new DeletePrescriptionUseCase(…)`) sur `src` et `tests` : les seules sont la **DI**
  (`AddScoped<IDeletePrescriptionUseCase, DeletePrescriptionUseCase>()`, qui résout le constructeur
  automatiquement et n'a donc pas été touchée) et les **tests**. **Aucun appelant runtime** ne le construit à la
  main — l'UI passe exclusivement par l'interface injectée. La dépendance a donc pu disparaître plutôt que d'être
  conservée morte. C'est un renforcement, pas une simple propreté : l'absence d'écriture cesse d'être
  **comportementale** (« `SaveChangesAsync` n'est pas appelé ») pour devenir **structurelle** — le use case ne
  détient plus aucune dépendance capable d'écrire. Le use case ne dépend plus que de ce qu'il utilise réellement
  pour distinguer « ordonnance absente » de « ordonnance existante et conservée ».
- **Lecture conservée** : le `GetByIdAsync` ne sert plus qu'à distinguer « déjà absente » de « existante,
  donc conservée ». Aucune des deux branches n'écrit.

### 8.3 Contrat « introuvable » — inchangé

`PrescriptionFound = false`, **aucune exception**, aucune écriture. Ce cas n'est pas un refus métier : il
décrit une ordonnance déjà absente (supprimée depuis un autre poste avant P3-12, ou identifiant périmé), que
la ViewModel traite par un rechargement depuis la source. Le distinguer du refus évite de transformer un état
multi-poste banal en erreur métier.

### 8.4 Use case conservé, non supprimé

Trois ViewModels le résolvent par DI. Le retirer aurait cassé la composition de l'UI — hors périmètre
(backend uniquement). Il devient donc le **gardien** du chemin public : ce qui protégeait la donnée était une
boîte de dialogue ; c'est désormais une règle Domain.

### 8.5 Aucune fuite technique

Le message ne contient ni « SQLite », ni « SQL », ni « EntityFramework », ni « DbContext », ni nom de table,
ni nom de clé étrangère, ni pile d'appels — propriété **opposée par un test** (§9, T2b).

---

## 9. Tests

### Domain — `tests/MMV.Domain.Tests/PolicyTests/PrescriptionRetentionPolicyTests.cs` (nouveau, 2 tests)

| Test | Propriété opposée |
|---|---|
| `PhysicalDeletionForbiddenMessage_IsStable` | Le message est comparé à un **littéral recopié**, pas à lui-même : c'est sa réécriture silencieuse qui doit échouer |
| `PhysicalDeletionForbiddenMessage_IsBusinessWording_WithoutTechnicalDetail` | Non vide, et **sans** « SQLite », « SQL », « EntityFramework », « DbContext », « FOREIGN KEY », « constraint », « Exception » |

### Application — `DeletePrescriptionUseCaseTests.cs` (9 tests : 5 nouveaux, 4 conservés, 1 remplacé)

| # | Test | Attendu — et ce qu'il prouve |
|---|---|---|
| **T1a** | `ExecuteAsync_PrescriptionNotFound_ReturnsNotFound_WithoutThrowing_AndDoesNotWrite` | Vrai SQLite : `PrescriptionFound = false`, **aucune exception**, l'ordonnance voisine survit |
| **T1b** | `ExecuteAsync_PrescriptionNotFound_NeverDeletesNorSaves` | Doubles `MockBehavior.Strict` : `DeleteAsync` et `SaveChangesAsync` `Times.Never` |
| **T2a** | `ExecuteAsync_ExistingPrescription_ThrowsBusinessRule_WithStableMessage` | `BusinessRuleException` au message exact **et** — preuve décisive — **aucune primitive d'écriture sollicitée**, vérifiée sur doubles stricts plutôt que déduite de l'état final |
| **T2b** | *(Domain)* | cf. tableau ci-dessus : le message reste métier |
| **T3** | `ExecuteAsync_OnRealSqlite_KeepsPrescriptionAndCustomerIntact` | **Vraie base SQLite** : après refus, relecture par contexte **neuf** `AsNoTracking()` — 1 ordonnance, identifiants, `DoctorName` et valeurs optiques (`OdSphere`, `OdCylinder`, `OdAxis`) **intacts** ; le client survit ; aucune autre table touchée |
| **T4** | `ExecuteAsync_RepeatedAttempts_AreRefusedEveryTime_WithoutSideEffect` | Deux tentatives, **portée neuve à chaque fois** (comme l'UI) : deux refus, ni suppression ni duplication. Interdit qu'un tracker rémanent explique le résultat |
| **T5** | `DeletingCustomer_WithSurvivingPrescription_IsStillRefused` | **Non-régression P3-2B** : le client porteur de l'ordonnance conservée reste **non supprimable** (`CustomerHasHistoryMessage`). La conservation n'ouvre aucune brèche latérale |

Contrats de garde inchangés et conservés : commande nulle → `ArgumentNullException` ; constructeur sans
repository → `ArgumentNullException`.

**Garde de constructeur remplacée.** `Constructor_WithoutUnitOfWork_Throws` a été **retirée avec le paramètre
qu'elle protégeait** — elle n'avait plus d'objet. Elle est remplacée par
`Constructor_DeclaresOnlyThePrescriptionRepository`, qui oppose par réflexion une propriété **strictement plus
forte** : le use case déclare un unique constructeur, à un unique paramètre, de type `IPrescriptionRepository`.
Réintroduire une dépendance d'écriture — unité de travail, `DbContext`, exécuteur de transaction — fait échouer ce
test. Ce n'est pas un test ajouté sans propriété nouvelle : il énonce l'inatteignabilité structurelle de
l'écriture, que l'ancienne garde ne disait pas.

### Test supprimé — et pourquoi

`ExecuteAsync_DeletesExistingPrescription_AndPersists` **gravait explicitement la suppression physique**
(`verify.Prescriptions.Should().BeEmpty()`). Il devenait contradictoire avec la règle. Il a été **remplacé**
par T2a/T3, qui opposent la propriété inverse — **il n'a pas été ignoré ni neutralisé**. C'est le seul test
existant modifié, et il entrait exactement dans le cas autorisé par le périmètre.

### Garde-fou d'architecture

**Aucun ajouté.** La propriété visée — « ce use case n'appelle aucune primitive de persistance » — est déjà
opposée **comportementalement** par T1b et T2a sur doubles stricts, ce qui est plus fort qu'un scan textuel
et ne peut pas devenir un faux positif. Ajouter un scan fragile aurait été de la cérémonie.

---

## 10. Non-régression

| Contrôle | Résultat |
|---|---|
| `MMV.App.Tests` | **239 → 239**, exactement stable (aucune UI modifiée) |
| Suppression client protégée (P3-2B) | ✅ opposée par T5 |
| Filtre `~DeletePrescription` (Application) | **9 / 9** ✅ (5 avant → 9 : +5 nouveaux, −1 remplacé) |
| Filtre `~Prescription` (Domain) | **64 / 64** ✅ (62 avant + 2 nouveaux) |
| Filtre `~Customer` (Application) | **69 / 69** ✅ |
| Solution complète | **1499 / 1499** ✅, 0 échec, 0 ignoré |

---

## 11. Documentation

| Fichier | Nature |
|---|---|
| `docs/implementation/P3-12-final-business-audit-report.md` | **§36 ajoutée** — révision du verdict §13.2/§28.1, décision, correctif, UI, absence de migration, nouveau total, critère satisfait, verdict révisé. **L'historique est conservé** : le défaut, son caractère public et le classement initial ne sont pas réécrits ; les deux verdicts d'origine sont barrés et renvoient à §36 |
| `docs/implementation/P3-12-prescription-deletion-remediation-report.md` | **Ce rapport** (nouveau) |
| `docs/architecture/P3-business-rules-roadmap.md` | **C1 → C6** (§12 ci-dessous) |
| `docs/implementation/P3-3B-…-report.md` | **Note finale datée 2026-07-23**, ajoutée en fin de fichier. Aucun constat historique modifié |

### Corrections de roadmap appliquées

| # | Correction |
|---|---|
| **C1** | P3-12 n'est plus « non commencé » : audit exécuté, blocage trouvé sur la suppression d'ordonnance, correctif appliqué et validé localement. *(La réserve « sous réserve de commit et CI » alors portée par la roadmap a depuis été **levée** : commit `c175100`, run `29964844698`, `success` — cf. §18.)* |
| **C2** | Correctif P3-8 inscrit définitivement : commit `4c2a1e5…`, run `29958212378`, `success`, 1493 tests, **dette close**, verdict `CORRECTIF P3-8 PRÉ-P3-12-CI = GO` |
| **C3** | P3-3 n'est plus « sous réserve de CI distante » : implémentation incorporée dans le HEAD P3 final, validée par les CI ultérieures de la branche. **Aucun ancien run n'est inventé** ; la complétion par P3-12 est signalée |
| **C4** | Note factuelle sur le commit UI `d8af704` (redesign de l'écran de connexion), antérieur à la décision backend-only de P3-4, sans règle métier concernée, **non rattaché** à une sous-étape inexistante |
| **C5** | `CreateOrderUseCase` documenté honnêtement : chemin **public**, en DI, atteignable depuis l'UI, **inopérant** sous FK actives (`SaleId = 0`), **échec fermé**, dette importante reportée, danger de stock fermé par la garde P3-11 |
| **C6** | Nouveau §6.1 : inventaire des six domaines et de leur protection ; le critère « suppression sûre partout » devient **pleinement satisfait**, avec la réserve explicite que conservation ≠ immutabilité |
| **C7** | Section P3-3 : la phrase au présent « la suppression physique d'une ordonnance existe toujours » décrivait un état désormais faux et rendait la roadmap contradictoire avec le dépôt. Elle est **datée** (« à la clôture de P3-3, … constituait une dette ouverte ») puis **fermée** par un renvoi à P3-12. **L'histoire n'est pas effacée** : le constat d'origine reste lisible, et la réserve sur l'immutabilité forte — non traitée par P3-12 — est maintenue entière |

Améliorations facultatives F1–F4 de l'audit : **non traitées** (hors périmètre de cette remédiation).

---

## 12. Résultats complets

| Contrôle | Commande | Résultat |
|---|---|---|
| Restore | `dotnet restore MMV.sln` | OK |
| Build | `dotnet build MMV.sln --no-restore -c Debug` | **0 Warning(s). 0 Error(s).** |
| Domain | `dotnet test tests/MMV.Domain.Tests/…` | **Passed! Failed: 0, Passed: 649, Skipped: 0** |
| Application | `dotnet test tests/MMV.Application.Tests/…` | **Passed! Failed: 0, Passed: 611, Skipped: 0** |
| App | `dotnet test tests/MMV.App.Tests/…` | **Passed! Failed: 0, Passed: 239, Skipped: 0** |
| Solution | `dotnet test MMV.sln --no-build -c Debug` | 649 + 611 + 239, **0 échec, 0 ignoré** |
| Vulnérabilités | `dotnet list MMV.sln package --vulnerable --include-transitive` | « has no vulnerable packages » pour les **7** projets |
| EF drift | `dotnet ef migrations has-pending-model-changes --no-build` | **« No changes have been made to the model since the last migration. »** |
| Pureté Application | `dotnet list … reference` | **une seule** référence : `..\MMV.Domain\MMV.Domain.csproj` |
| Paquets Application | `dotnet list … package` | **un seul** : `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` |
| Espaces | `git diff --check` | **exit 0** (seuls des avis LF→CRLF, propres à Windows, aucun défaut d'espace) |

### Progression

| Projet | Baseline | Final | Δ |
|---|---:|---:|---:|
| Domain | 647 | **649** | +2 |
| Application | 607 | **611** | +4 |
| App | 239 | **239** | **0** |
| **Total** | **1493** | **1499** | **+6** |

Détail du +4 en Application : **+6** nouveaux tests, **−2** tests retirés.

| Mouvement | Test | Motif |
|---|---|---|
| **−1** | `ExecuteAsync_DeletesExistingPrescription_AndPersists` | Gravait la suppression physique ; contredisait la règle. Remplacé par T2a/T3 |
| **−1** | `Constructor_WithoutUnitOfWork_Throws` | Retiré **avec** le paramètre qu'il protégeait ; sans objet |
| **+6** | T1b · T2a · T3 · T4 · T5 · `Constructor_DeclaresOnlyThePrescriptionRepository` | cf. §9 |

Aucun test n'a été ignoré, neutralisé ni assoupli : les deux retraits accompagnent la disparition de ce qu'ils
décrivaient, et chacun est remplacé par une propriété opposée plus forte.

---

## 13. Sécurité et EF

- **Aucune migration**, aucun `ModelSnapshot`, aucune entité, aucune configuration EF, aucun repository
  touchés. Le correctif est **purement comportemental** — vérifié par `has-pending-model-changes`.
- **Aucun paquet** ajouté, retiré ou mis à jour ; **0 vulnérabilité** sur les 7 projets.
- **`MMV.Application` reste pure** : une seule référence projet, `MMV.Domain`. Aucun EF, SQLite ou Avalonia.
- **Aucune fuite technique** : le message de refus est métier et stable, propriété opposée par un test.
- **Aucune donnée personnelle ou médicale exposée** ; le refus n'écrit rien, donc ne journalise rien.
- **Surface d'écriture réduite** : ce chemin ne peut plus écrire du tout. C'est une réduction stricte des
  droits, jamais une extension.

---

## 14. Fichiers modifiés

### Production (2 modifiés, 1 nouveau)

| Fichier | Nature |
|---|---|
| `src/MMV.Domain/Policies/PrescriptionRetentionPolicy.cs` | **Nouveau** — propriétaire Domain de la règle |
| `src/MMV.Application/UseCases/Prescriptions/DeletePrescription/DeletePrescriptionUseCase.cs` | Refus dur ; suppression retirée ; `IUnitOfWork` **retiré du constructeur** |
| `src/MMV.Application/UseCases/Prescriptions/DeletePrescription/IDeletePrescriptionUseCase.cs` | Contrat documenté : `<exception>` `BusinessRuleException`, cas introuvable inchangé |

### Tests (1 modifié, 1 nouveau)

| Fichier | Nature |
|---|---|
| `tests/MMV.Domain.Tests/PolicyTests/PrescriptionRetentionPolicyTests.cs` | **Nouveau** — 2 tests |
| `tests/MMV.Application.Tests/UseCases/Prescriptions/DeletePrescriptionUseCaseTests.cs` | T1–T5 ; test de suppression physique remplacé ; garde `IUnitOfWork` remplacée par la garde structurelle |

### Documentation (2 modifiés, 2 créés)

| Fichier | Nature |
|---|---|
| `docs/architecture/P3-business-rules-roadmap.md` | **Modifié** — C1 → C6, plus la reformulation de la dette P3-3 (voir ci-dessous) |
| `docs/implementation/P3-3B-…-report.md` | **Modifié** — note finale datée 2026-07-23 |
| `docs/implementation/P3-12-final-business-audit-report.md` | **Créé** — §36 et note d'en-tête sur la portée du mode |
| `docs/implementation/P3-12-prescription-deletion-remediation-report.md` | **Créé** — ce rapport |

> **Correction.** Une version antérieure de ce tableau annonçait « 2 modifiés, 1 nouveau » et rangeait
> `P3-12-final-business-audit-report.md` parmi les fichiers modifiés. C'est **faux** : ce fichier est **non suivi**
> — il est créé par cette même vague de travail, jamais présent dans un commit antérieur. Le décompte correct est
> **2 modifiés, 2 créés**. L'erreur venait de `git diff --stat`, qui **ignore les fichiers non suivis** ; le
> décompte ci-dessous croise donc `git status --short` **et** `git ls-files --others --exclude-standard`.

### Bilan global du périmètre P3-12

Établi en croisant `git status --short` et `git ls-files --others --exclude-standard` :

| Catégorie | Modifiés | Créés | Supprimés | Total |
|---|---:|---:|---:|---:|
| Production | 2 | 1 | 0 | **3** |
| Tests | 1 | 1 | 0 | **2** |
| Documentation | 2 | 2 | 0 | **4** |
| **Total** | **5** | **4** | **0** | **9** |

Le retrait d'`IUnitOfWork` n'a créé ni supprimé aucun fichier : il modifie deux fichiers déjà comptés, et laisse
le total de tests **inchangé** (−1 garde retirée, +1 garde structurelle ⇒ Application reste à **611**).

Les trois dossiers non suivis `design-handoff/`, `design/`, `docs/ui/` sont **hors périmètre**, ne sont pas
indexés et ne figurent dans aucun décompte ci-dessus.

### Non touchés — vérifié par `git status`

`src/MMV.Infrastructure/**` · `src/MMV.Infrastructure/Migrations/**` · `*ModelSnapshot.cs` ·
`src/MMV.App/**` · `tests/MMV.App.Tests/**` · tout `.csproj` · `docs/ui/**` · `design/**` ·
`design-handoff/**` · `docs/architecture/**` *(hors la roadmap)* · `docs/domain/**`.

---

## 15. Dettes reportées — inchangées, non résolues ici

- **Immutabilité et concurrence des ordonnances** : une ordonnance reste **modifiable** ; deux postes
  corrigeant des yeux différents s'écrasent en silence. **Ouverte.**
- **Absence de FK `Prescription ↔ Sale`** : le lien passé entre une vente et l'ordonnance qui l'a motivée
  demeure **irrécupérable**. La conservation empêche la perte future ; elle ne reconstitue pas le passé.
- **Archivage d'ordonnance** : non implémenté. Une ordonnance obsolète ne peut être ni retirée des listes ni
  marquée périmée — seule la modification est offerte.
- **Ergonomie** : le bouton de suppression reste visible et produit désormais un refus. Le masquer ou le
  remplacer par une action de correction relève du **redesign UI**.
- **Hors périmètre, inchangés** : `CreateOrderUseCase` / `SaleId = 0` · `SaleStatus` non synchronisée et ses
  trois valeurs inatteignables · solde de vente sans commande non réglable · `INotificationRepository?`
  optionnel · autorisation par acteur (aucun `ICurrentUser`) · horloge injectable · provider serveur
  (ADR-PROD-DB-001).

---

## 16. État Git final

```
git status --short
 M docs/architecture/P3-business-rules-roadmap.md
 M docs/implementation/P3-3B-prescription-validation-and-archived-customer-report.md
 M src/MMV.Application/UseCases/Prescriptions/DeletePrescription/DeletePrescriptionUseCase.cs
 M src/MMV.Application/UseCases/Prescriptions/DeletePrescription/IDeletePrescriptionUseCase.cs
 M tests/MMV.Application.Tests/UseCases/Prescriptions/DeletePrescriptionUseCaseTests.cs
?? docs/implementation/P3-12-final-business-audit-report.md
?? docs/implementation/P3-12-prescription-deletion-remediation-report.md
?? src/MMV.Domain/Policies/PrescriptionRetentionPolicy.cs
?? tests/MMV.Domain.Tests/PolicyTests/PrescriptionRetentionPolicyTests.cs
?? design-handoff/   ?? design/   ?? docs/ui/      ← préexistants, hors périmètre

git diff --stat → 5 fichiers, 331 insertions, 70 suppressions
git diff --check → exit 0
```

**État au moment de la rédaction initiale : aucun commit.** HEAD était encore `4c2a1e5…`.

> **Mise à jour — revue finale (2026-07-23).** Une passe ultérieure
> (`TARGETED_REVIEW_FINALIZE_COMMIT_PUSH_AND_VERIFY_CI`) a revu ce correctif, retiré la dépendance `IUnitOfWork`
> devenue morte, corrigé les décomptes documentaires ci-dessus, puis **indexé les neuf fichiers un à un** (jamais
> `git add .` ni `git add -A`), créé un commit unique `fix(P3-12): preserve prescription history` et poussé
> `p3-business-rules`. Les trois dossiers hors périmètre `design-handoff/`, `design/`, `docs/ui/` sont restés
> **non indexés et non suivis**. **Aucun merge vers `main`. P4 non commencé.**

---

## 17. Verdict

| Condition de sortie | État |
|---|---|
| Une ordonnance existante ne peut plus être supprimée physiquement | ✅ T2a, T3, T4 |
| Le refus est opposé en **Application**, adossé à un propriétaire **Domain** | ✅ `PrescriptionRetentionPolicy` |
| Message métier stable, sans détail technique | ✅ opposé par test Domain |
| Aucune mutation ni sauvegarde exécutée | ✅ doubles stricts, `Times.Never` |
| Aucune dépendance d'écriture n'est même **injectable** | ✅ `IUnitOfWork` retiré ; `Constructor_DeclaresOnlyThePrescriptionRepository` |
| `DeletePrescriptionUseCase` est le **seul** chemin runtime de suppression | ✅ §5.1 — aucun service parallèle, aucun SQL brut, aucune cascade client |
| Cas « introuvable » correctement traité | ✅ T1a, T1b — contrat P2C-4 intact |
| Le client reste protégé par la présence de l'ordonnance | ✅ T5 (non-régression P3-2B) |
| L'UI absorbe proprement le refus **sans modification** | ✅ `catch` existant, `ErrorMessage` affiché ; App stable à 239 |
| Tous les tests passent | ✅ **1499**, 0 échec, 0 ignoré |
| Build 0 erreur / 0 avertissement | ✅ |
| Aucune vulnérabilité | ✅ 7 projets |
| Aucune migration, aucun `pending model change` | ✅ |
| Aucune UI, aucune Infrastructure, aucun paquet | ✅ `git status` |
| Critère « suppression sûre partout » pleinement satisfait | ✅ roadmap §6.1 |
| Rapport et roadmap corrigés (C1 → C7) | ✅ |
| Décomptes documentaires exacts (5 modifiés, 4 créés, 9 au total) | ✅ §14, croisé `git status` + `git ls-files --others` |

# **P3-12 REMÉDIATION = GO LOCAL**

# **P3 = GO MERGE CANDIDATE LOCAL**

**Verdict local au moment de la rédaction** — sous réserve de commit et de CI verte. Cette réserve est **levée**
par le §18 : le correctif a été commité, poussé, et la CI du SHA exact est verte.

Aucun merge vers `main`. P4 non commencé. **STOP.**

---

## 18. Commit et validation CI

> Section postérieure à la rédaction initiale. Les mentions « aucun commit » / « sous réserve de CI » du corps
> ci-dessus décrivent fidèlement le début de l'exécution et sont **conservées**. Seules les formulations
> **finales** devenues périmées sont levées ici.

### 18.1 Commit

| Champ | Valeur |
|---|---|
| SHA complet | `c1751007b8f8304154306381b2986f628a256c28` |
| Message | `fix(P3-12): preserve prescription history` |
| Branche | `p3-business-rules` |

### 18.2 Périmètre committé — 9 fichiers

| Catégorie | Modifiés | Créés | Supprimés | Total |
|---|---:|---:|---:|---:|
| Production | 2 | 1 | 0 | **3** |
| Tests | 1 | 1 | 0 | **2** |
| Documentation | 2 | 2 | 0 | **4** |
| **Total** | **5** | **4** | **0** | **9** |

Décompte confirmé par `git show --stat c175100` : **9 fichiers, 2 340 insertions, 85 suppressions**. Les neuf
fichiers ont été **indexés un à un** ; `design-handoff/`, `design/` et `docs/ui/` sont restés non indexés.

### 18.3 Ce que le commit contient de structurant

- **Retrait d'`IUnitOfWork`** du constructeur de `DeletePrescriptionUseCase` : l'absence d'écriture cesse d'être
  comportementale pour devenir **structurelle**. Le use case déclare un unique constructeur, à un unique
  paramètre `IPrescriptionRepository` — propriété opposée par `Constructor_DeclaresOnlyThePrescriptionRepository`.
- **Recherche exhaustive des chemins de suppression** (§5.1) : aucun service parallèle, aucune ViewModel
  appelant un repository, aucun SQL brut ni `ExecuteDeleteAsync` visant `Prescriptions`, aucun second use case,
  aucune cascade client (`Restrict` des deux côtés). `DeletePrescriptionUseCase` est le **seul chemin runtime**
  de suppression d'ordonnance du dépôt.
- **Résultat UI observé, sans modification d'UI** : `CustomerPrescriptionsViewModel.ExecuteDeleteAsync` enveloppe
  l'appel dans un `try/catch (Exception ex)` existant qui affecte `ErrorMessage`. Le refus est **absorbé et
  affiché**, sans exception non gérée. `src/MMV.App/**` et `tests/MMV.App.Tests/**` sont **inchangés** (App reste
  exactement à **239**).

### 18.4 CI

| Champ | Valeur |
|---|---|
| Run | [`29964844698`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29964844698) |
| `headSha` | `c1751007b8f8304154306381b2986f628a256c28` **(exact)** |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | **`success`** |

Job unique « Restore / Build / Test / Scan » : **Restore**, **Build**, **Test**, **Audit des packages
vulnérables** et **Check EF Core pending model changes** tous `success`. **Aucun job obligatoire en échec.**

| Contrôle | Résultat |
|---|---|
| Tests | **1499** (Domain 649 · Application 611 · App 239), 0 échec, 0 ignoré |
| Build | 0 erreur / 0 avertissement |
| Vulnérabilités | 0 (7 projets) |
| Modèle EF | aucun `pending model change` |

### 18.5 Ce que le commit ne contient pas

**Aucune migration. Aucune Infrastructure. Aucune UI.** Aucun `ModelSnapshot`, aucune entité, aucune
configuration EF, aucun repository, aucun paquet, aucun projet.

### 18.6 Merge et suite

**Aucun merge vers `main` n'a été effectué** — ni `merge`, ni `rebase`, ni `cherry-pick`, ni `gh pr merge`.
`origin/main` reste à `1e28f1e`, dont la branche descend directement : le merge serait un **fast-forward**,
`merge-tree` sortant en code 0 sans conflit. **P4 n'est pas commencé.**

### 18.7 Verdict définitif

# **P3-12-CI = GO**

# **P3 = GO MERGE CANDIDATE**

Le verdict `GO LOCAL` du §17 est **levé, non effacé** : il décrivait exactement l'état avant commit. La CI du
SHA exact fait désormais foi. **P3 n'est pas `MERGED`** — la branche en est candidate.
