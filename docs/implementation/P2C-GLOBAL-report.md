# Rapport P2C-GLOBAL — VERDICT : NO-GO baseline (CVE transitive SQLite)

> **Mission arrêtée à l'étape « baseline » sur décision explicite du demandeur.**
> Aucune modification de code n'a été effectuée. Aucun use case, aucun ViewModel, aucun
> garde-fou, aucune allowlist n'a été touché. Le seul artefact produit par cette mission est
> **ce rapport**.

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-GLOBAL |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

Aucun commit créé, aucun push effectué (conforme à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 2. État Git initial

| Contrôle | Attendu (brief) | Obtenu |
|---|---|---|
| `git branch --show-current` | `p2c-ui-cleanup` | **`p2c-ui-cleanup`** ✅ |
| `git status --short` | working tree propre | **propre** ✅ |
| Présence commit applicatif P2C-4 | oui | **`f52f4cd feat(P2C-4): move prescription writes to application use cases`** ✅ |
| Présence commit documentaire P2C-4 | oui | **`d2961ff docs(P2C-4): record documentary commit CI validation (CI #54 success)`** ✅ |

`git log -10 --oneline` :

```
d2961ff docs(P2C-4): record documentary commit CI validation (CI #54 success)
36fda91 docs(P2C-4): record prescription use cases CI validation
f52f4cd feat(P2C-4): move prescription writes to application use cases
da45c3d docs(P2C-3): record customer deletion CI validation
146ceb5 feat(P2C-3): move customer deletion to application use case
21fc1c2 docs(P2C-2): record documentary CI validation (CI #49 success)
ba4f30d docs(P2C-2): record customer use cases CI validation
e15c614 feat(P2C-2): move customer create update to application use cases
e3d2e9d docs(P2C-1): record UI guardrails CI validation
b292aff test(P2C-1): add UI persistence guardrails
```

**Précondition (§3 du brief) : SATISFAITE.** Branche correcte, working tree propre, P2C-4 applicatif **et** documentaire présents. → Il n'y a **pas** de NO-GO précondition. Le blocage vient de la **baseline** (étape suivante).

## 3. Baseline (§5 du brief)

Baseline exécutée intégralement **avant toute modification** (aucune modification n'a d'ailleurs été faite ensuite).

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore MMV.sln` | OK | **OK** | ✅ |
| `dotnet build MMV.sln --no-restore -c Debug` | vert | **vert** (0 erreur ; 1 warning pré-existant `OrderFormViewModel` CS1998, hors périmètre) | ✅ |
| `dotnet test MMV.sln --no-build -c Debug` | ≥ 463 | **463** (App 161 + Application 79 + Domain 223) | ✅ |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** | **1 vulnérabilité HIGH transitive** (voir §4) | ❌ **ÉCHEC** |
| `dotnet tool restore` | OK | **OK** (`dotnet-ef` 8.0.27) | ✅ |
| `dotnet ef migrations has-pending-model-changes` | false | **false** (`No changes have been made to the model since the last migration`) | ✅ |
| `dotnet list src/MMV.Application reference` | Domain seul | **`..\MMV.Domain\MMV.Domain.csproj` seul** | ✅ |
| `dotnet list src/MMV.Application package` | DI.Abstractions seul | **`Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul** | ✅ |

**Baseline = ÉCHEC** sur l'unique critère « 0 vulnérabilité ». Tous les autres critères de baseline sont verts.

## 4. Cause de l'échec — CVE transitive SQLite

### 4.1 Sortie brute (`dotnet list ... --vulnerable --include-transitive`)

```
Le projet spécifié 'MMV.Domain' n'a aucun package vulnérable ...
Le projet 'MMV.Infrastructure' comporte les packages vulnérables suivants
   [net8.0]:
   Package transitif                 Résolu   Gravité   URL d'avertissement
   > SQLitePCLRaw.lib.e_sqlite3      2.1.6    High      https://github.com/advisories/GHSA-2m69-gcr7-jv3q
Le projet 'MMV.App' comporte les packages vulnérables suivants
   > SQLitePCLRaw.lib.e_sqlite3      2.1.6    High      GHSA-2m69-gcr7-jv3q
Le projet spécifié 'MMV.Application' n'a aucun package vulnérable ...
Le projet 'MMV.Domain.Tests'      > SQLitePCLRaw.lib.e_sqlite3  2.1.6  High
Le projet 'MMV.App.Tests'         > SQLitePCLRaw.lib.e_sqlite3  2.1.6  High
Le projet 'MMV.Application.Tests' > SQLitePCLRaw.lib.e_sqlite3  2.1.6  High
```

### 4.2 Caractérisation

| Attribut | Valeur |
|---|---|
| Package | `SQLitePCLRaw.lib.e_sqlite3` |
| Version résolue | `2.1.6` |
| Gravité | **High** |
| Avis | `GHSA-2m69-gcr7-jv3q` |
| Nature | **Transitive** (jamais référencée directement dans le dépôt) |
| Point d'entrée | `MMV.Infrastructure` → `Microsoft.EntityFrameworkCore.Sqlite 8.0.27` → `Microsoft.Data.Sqlite.Core` → `SQLitePCLRaw.bundle_e_sqlite3 2.1.6` → **`SQLitePCLRaw.lib.e_sqlite3 2.1.6`** |
| Projets impactés | Infrastructure, App, Domain.Tests, App.Tests, Application.Tests (par transitivité) |
| Projets **non** impactés | Domain, **Application** (couche Application toujours pure) |

### 4.3 Pourquoi c'est nouveau (non détecté aux phases précédentes)

Le rapport **P2C-4** (baseline datée du 2026-06-16, cf. `docs/implementation/P2C-4-report.md` §3 et §14) a mesuré **0 vulnérabilité**. La baseline P2C-GLOBAL est exécutée le **2026-07-05**. L'avis `GHSA-2m69-gcr7-jv3q` a donc été **publié entre ces deux dates**, sur un package **transitif inchangé** (`SQLitePCLRaw.lib.e_sqlite3 2.1.6`, natif SQLite). **Aucune** modification du dépôt n'a introduit cette vulnérabilité : c'est un événement **externe** (nouvel avis de sécurité amont), pas une régression de code.

## 5. Conflit de contraintes du brief (pourquoi la question a été posée)

Le blocage n'est pas résoluble à l'intérieur du périmètre strict de P2C-GLOBAL :

- **§5 / §16 du brief** — « 0 vulnérabilité » est un **gate dur** de baseline **et** un critère de GO. En échec ⇒ « **STOP, P2C-GLOBAL = NO-GO baseline** ».
- **§8 du brief** — interdit de modifier `MMV.Infrastructure`, ses packages, et plus généralement de toucher aux dépendances de persistance. Or **le seul endroit** où l'on pourrait épingler/relever `SQLitePCLRaw` est précisément `MMV.Infrastructure` (ou une référence top-level ajoutée dans `MMV.App`), donc **hors périmètre**.

La remédiation (ou l'exception documentée) **sort donc du périmètre strict de P2C-GLOBAL**.

## 6. Décision (demandeur)

Décision explicite : **STOP — NO-GO baseline.**

- Ne pas continuer P2C-GLOBAL.
- Ne pas modifier le code UI.
- Ne pas contourner la règle « 0 vulnérabilité ».
- Produire ce rapport NO-GO baseline.
- La correction (ou l'exception documentée) sera traitée dans une **phase séparée `P2C-SEC-1`**, susceptible de toucher les packages Infrastructure/App, donc hors périmètre P2C-GLOBAL.
- Après `P2C-SEC-1`, P2C-GLOBAL sera **relancé depuis une baseline propre**.

## 7. Actions effectuées / non effectuées

**Effectué (lecture seule + contrôles) :**
- Précondition Git (branche / status / log) — vérifiée.
- Lecture des documents d'architecture et des rapports P2C-1..4 imposés (§4 du brief).
- Baseline complète (§5) exécutée en lecture seule (restore/build/test/vuln/tool/ef/refs/packages).
- Identification de la chaîne transitive SQLite.
- Rédaction de ce rapport.

**NON effectué (par décision NO-GO) :**
- ❌ Aucun inventaire de modification, aucune sous-phase A/B/C/D.
- ❌ Aucun use case command créé.
- ❌ Aucun query use case créé.
- ❌ Aucun ViewModel modifié.
- ❌ Aucun code-behind modifié.
- ❌ Aucune allowlist réduite (`AppUiPersistenceGuardrailTests.cs` **inchangé** : 48 repo + 17 UoW + 0 propriété + 2 code-behind = **67**).
- ❌ Aucun test créé/modifié.
- ❌ **`docs/architecture/P2D-read-application-roadmap.md` NON créé** (le brief §15 le conditionne à un P2C-GLOBAL vert — ce n'est pas le cas).
- ❌ Aucune migration, aucun modèle EF touché, aucun commit, aucun push.

## 8. État de l'allowlist au moment du STOP (inchangé)

Pour mémoire (aucune réduction réalisée par cette mission) :

| Allowlist (`AppUiPersistenceGuardrailTests.cs`) | Entrées |
|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 48 |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 17 |
| `AllowedPublicPersistenceProperties` | 0 |
| `AllowedCodeBehindPersistenceFiles` | 2 (`App.axaml.cs`, `Views/MainWindow.axaml.cs`) |
| **Total** | **67** |

## 9. Contrôles exécutés

```
git branch --show-current
git status --short
git log -10 --oneline
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
dotnet list src/MMV.Application/MMV.Application.csproj reference
dotnet list src/MMV.Application/MMV.Application.csproj package
dotnet list src/MMV.Infrastructure/MMV.Infrastructure.csproj package [--include-transitive]
```

## 10. Verdict

### **P2C-GLOBAL = NO-GO baseline.**

Motif unique : la baseline échoue au gate « 0 vulnérabilité » à cause de la CVE **transitive** `SQLitePCLRaw.lib.e_sqlite3 2.1.6` / `GHSA-2m69-gcr7-jv3q` (High), publiée en amont **après** la baseline verte de P2C-4, et **non corrigeable dans le périmètre strict de P2C-GLOBAL** (§8 interdit de toucher les packages Infrastructure/App).

Tous les autres indicateurs de baseline sont verts (build, 463 tests, EF sans changement pending, Application pure). Le blocage est **exclusivement** sécurité/dépendances, **sans rapport** avec le nettoyage UI/Application visé.

## 11. Prochaine étape recommandée — `P2C-SEC-1`

Phase de sécurité dédiée, **hors P2C-GLOBAL** (autorisée à toucher les packages), au choix :

1. **Remédiation** : épingler une version corrigée de `SQLitePCLRaw` (top-level `PackageReference` sur `SQLitePCLRaw.bundle_e_sqlite3` / `SQLitePCLRaw.lib.e_sqlite3` dans `MMV.Infrastructure`, ou montée de `Microsoft.EntityFrameworkCore.Sqlite`) une fois une version non affectée par `GHSA-2m69-gcr7-jv3q` disponible, puis re-vérifier `has-pending-model-changes = false`.
2. **Exception documentée** : si aucune version corrigée n'est disponible, formaliser une exception d'audit NuGet (ex. `NuGetAudit` / suppression ciblée dans le workflow CI) avec justification et échéance de revue.

Critère de reprise : `dotnet list MMV.sln package --vulnerable --include-transitive` = **0 vulnérabilité** (ou exception validée). **Après `P2C-SEC-1`, relancer P2C-GLOBAL depuis une baseline propre.**
