# P2B-2A-CI-R2 — Ajouter le contrôle EF `has-pending-model-changes` dans la CI — RAPPORT

> **Programme 2 — micro-phase d'outillage CI.** Phase **P2B-2A-CI-R2**. Branche `p2b-architecture`.
> Date : 13 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : ajouter dans le workflow GitHub Actions le contrôle EF Core
> `dotnet ef migrations has-pending-model-changes`, afin d'aligner la CI distante sur les contrôles
> locaux exécutés à chaque phase. **Aucun code `src/`/`tests/`, aucune migration, aucun `*.csproj`/`*.sln`,
> aucun projet `MMV.Application`.**

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2A-CI-R2` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

**Périmètre autorisé** : `.github/workflows/ci.yml` et ce rapport, exclusivement.

---

## 2. Contexte P2B-2A-CI

La phase **P2B-2A-CI** est close (**GO définitif**) :

| Élément | Valeur |
|---|---|
| Run distant | **#19** — id `27441381061` |
| Commit testé | `3042e52` |
| Branche | `p2b-architecture` |
| Statut | ✅ completed / success (VERT) |

Le déclencheur CI couvre désormais les branches de phase :

```yaml
on:
  push:
    branches:
      - main
      - 'phase*'
      - 'p2*'
  pull_request:
    branches: [ main ]
  workflow_dispatch:
```

Le pipeline distant exécute aujourd'hui : **restore → build → test → audit NuGet (High/Critical, JSON + sévérité)**.
**Manque** un contrôle pourtant exécuté localement à chaque phase : `has-pending-model-changes`. C'est l'objet de P2B-2A-CI-R2.

---

## 3. Objectif du contrôle `has-pending-model-changes`

Depuis la correction **P2A-1R19**, le modèle EF Core compilé doit rester **strictement aligné** sur la
dernière migration appliquée. La commande :

```
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
```

compare le **snapshot du modèle** (issu de la dernière migration) au **modèle actuel** du `DbContext` :

- **exit code 0** → *« No changes have been made to the model since the last migration. »* ⇒ **false** (aucun écart) ;
- **exit code ≠ 0** → un changement de modèle (entité, propriété, configuration, clé…) **n'a pas encore été
  matérialisé** par une migration ⇒ **la CI échoue**.

Porter ce contrôle dans la CI distante **interdit la dérive** entre le code du modèle et l'historique de
migrations : un développeur qui modifie le `DbContext` sans générer la migration correspondante verra le
pipeline rougir, et non plus seulement le contrôle local.

`--no-build` réutilise le binaire **Debug** déjà produit par le step *Build* du même job : aucune
recompilation, pas de génération de migration.

---

## 4. Modification appliquée au workflow

**Seul** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) est modifié : **ajout de deux steps**
à la fin du job `build-test-scan`, **après** le step d'audit NuGet. Aucun job, runner, nom de workflow,
commande ou script existant n'est touché.

```diff
           Write-Host "Aucune vulnerabilite High/Critical detectee."
+
+      # Outil EF Core verrouille par le manifeste local (.config/dotnet-tools.json) :
+      # restauration explicite avant tout appel `dotnet ef` sur le runner.
+      - name: Restore .NET tools
+        run: dotnet tool restore
+
+      # Contrôle EF Core (depuis P2A-1R19) : le modèle compilé doit être STRICTEMENT
+      # aligné sur la dernière migration. `has-pending-model-changes` sort en échec
+      # (exit code non nul) si un changement de modèle n'a pas encore été matérialisé
+      # par une migration. `--no-build` réutilise le binaire Debug déjà produit ci-dessus.
+      - name: Check EF Core pending model changes
+        run: dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
```

### Pourquoi `dotnet tool restore` est requis

`dotnet-ef` est un **outil local** verrouillé par le manifeste [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json)
(`dotnet-ef` **8.0.27**, `isRoot: true`). Sur un runner neuf, l'outil n'est **pas** présent tant que
`dotnet tool restore` ne l'a pas restauré. Le step *Restore .NET tools* est donc ajouté **avant** le contrôle EF,
conformément à la consigne.

**Ordre des steps (job `build-test-scan`)** : Checkout → Setup .NET → Diagnostic SDK → Restore → Build →
Test → Audit NuGet → **Restore .NET tools** → **Check EF Core pending model changes**. Le contrôle EF est
placé après *Build*, donc le binaire Debug existe : `--no-build` est valide.

---

## 5. Fichiers modifiés

| Fichier | Changement |
|---|---|
| [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | ajout de 2 steps en fin de job (`Restore .NET tools` + `Check EF Core pending model changes`) — **+12 lignes** |
| [`docs/implementation/P2B-2A-CI-R2-report.md`](P2B-2A-CI-R2-report.md) | ce rapport (créé) |

**Aucun** fichier `src/`, **aucun** `tests/`, **aucune** migration, **aucun** `*.csproj`/`*.sln`,
**aucun** projet `MMV.Application`.

---

## 6. Contrôles locaux

| Commande | Résultat |
|---|---|
| `git status --short` | ` M .github/workflows/ci.yml` (+ `?? docs/implementation/P2B-2A-CI-R2-report.md`) |
| `git diff --stat` | `.github/workflows/ci.yml \| 12 ++++++++++++` — **1 fichier, +12** |
| `git diff --check` | ✅ aucune erreur d'espaces / marqueurs de conflit |
| `git diff -- .github/workflows/ci.yml` | uniquement les 2 steps ajoutés (cf. §4) |
| `dotnet tool restore` | ✅ `dotnet-ef` (8.0.27) restauré |
| `dotnet restore MMV.sln` | ✅ tous les projets à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avertissement** (`CS1998` préexistant, [OrderFormViewModel.cs:458](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458), hors périmètre), 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **320** (Domain **223** + App **97**), 0 échec, 0 ignoré |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 package vulnérable** (5 projets) |
| `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | ✅ *« No changes have been made to the model since the last migration. »* — exit `0` ⇒ **false** |

---

## 7. Résultats

| Critère attendu | Valeur observée | Statut |
|---|---|---|
| Build vert | Succès, 1 avert. `CS1998` préexistant, 0 erreur | ✅ |
| Tests verts | **320** (223 + 97), 0 échec, 0 ignoré | ✅ |
| Audit NuGet | **0 vulnérabilité** (5 projets) | ✅ |
| `has-pending-model-changes` | **false** (exit 0) | ✅ |
| Aucun `src/` modifié | confirmé (git status) | ✅ |
| Aucun `tests/` modifié | confirmé | ✅ |
| Aucune migration | confirmé | ✅ |
| Aucun `*.csproj`/`*.sln` modifié | confirmé | ✅ |
| Aucun projet `MMV.Application` créé | confirmé | ✅ |

---

## 7 bis. Validation CI distante (run réel)

Commit `32bed23` poussé sur `p2b-architecture` (sans force) → run GitHub Actions déclenché par le motif `p2*`.

| Élément | Valeur réelle |
|---|---|
| **Run** | **#21** — id **`27447845006`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27447845006 |
| **Commit testé** | **`32bed23`** (`head_sha = 32bed23ba12e9a4a5a3221eb4c2e5e93b358ce5d`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*`) |
| Événement | `push` |
| Workflow / job | `CI` / `Restore / Build / Test / Scan` |
| Runner | `windows-latest` (GitHub-hosted) |
| Durée | ≈ 3 min 05 s (22:57:45 → 23:00:50 UTC) |
| Setup .NET (SDK `global.json`) | ✅ success |
| **Restore** | ✅ **success** |
| **Build** | ✅ **success** (`-c Debug`, `CS1998` préexistant non bloquant) |
| **Test** | ✅ **success** (suite **320** confirmée localement) |
| **Audit NuGet** (High/Critical, JSON + sévérité) | ✅ **success** — **0 vulnérabilité** |
| **Restore .NET tools** (step #9) | ✅ **success** (`dotnet-ef` 8.0.27 restauré) |
| **Check EF Core pending model changes** (step #10) | ✅ **success** — `has-pending-model-changes` = **false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps sont en conclusion `success` : *Set up job, Checkout, Setup .NET, Diagnostic SDK, Restore,
Build, Test, Audit des packages vulnérables, **Restore .NET tools**, **Check EF Core pending model changes**,
Post-steps, Complete job*. **Le nouveau contrôle EF s'exécute bien dans la CI distante et passe au vert.**

---

## 8. Risques résiduels

| Risque | Évaluation | Mitigation |
|---|---|---|
| ~~Validation distante non encore réalisée~~ | **LEVÉ** : run distant **#21** (`27447845006`) **vert**, commit `32bed23`, branche `p2b-architecture`. Le step `Check EF Core pending model changes` s'est exécuté et a réussi (cf. §7 bis). | Gate « CI distante » **levée**. |
| `dotnet ef --no-build` sans build préalable | Sur le runner, le step est placé **après** *Build* (`-c Debug`) : le binaire Debug existe. `dotnet ef` cible Debug par défaut → cohérent. | Ordre des steps explicite (cf. §4). |
| Coût/latence du `dotnet tool restore` distant | Marginal (1 outil, `dotnet-ef` 8.0.27 figé par manifeste). | Acceptable. |
| Faux négatif possible si la config EF runtime diffère du modèle | Identique au contrôle local déjà éprouvé depuis P2A-1R19. | Aucun changement de comportement : la CI reproduit le contrôle local. |
| Avertissement `CS1998` préexistant | Hors périmètre, déjà présent et non bloquant (build sans `-warnaserror`). | Documenté, inchangé. |

Aucun risque introduit dans le code applicatif : le changement est **strictement** confiné au YAML de workflow.

---

## 9. État Git final

**Autorisation `ALLOW_COMMIT=true`, `ALLOW_PUSH=true`.** Le changeset (workflow + ce rapport) a été committé
puis poussé (sans force-push) :

```
git add .github/workflows/ci.yml docs/implementation/P2B-2A-CI-R2-report.md
git commit -m "ci(P2B-2A): check EF pending model changes"
  → 32bed23  2 files changed, 231 insertions(+)
git push origin p2b-architecture
  → 0815bcf..32bed23  p2b-architecture -> p2b-architecture (sans force)
```

Le commit `32bed23` ne contient **que** `.github/workflows/ci.yml` (+12) et ce rapport. Un second commit
**documentaire** consigne le résultat réel du run CI (cf. §7 bis) — il ne touche que ce rapport.

> ⚠️ Subtilité GitHub Actions : le workflow exécuté est **celui présent sur le commit poussé**. Le commit
> `32bed23` contenant lui-même les nouveaux steps, son push a **bien déclenché** le pipeline avec le contrôle EF.

---

## 10. Verdict — GO / NO-GO

| Critère d'acceptation | Statut |
|---|---|
| Le workflow contient le step `has-pending-model-changes` | ✅ |
| `dotnet tool restore` ajouté (outil local requis) | ✅ |
| Build local vert | ✅ (1 avert. `CS1998` préexistant) |
| Tests locaux verts (**320**) | ✅ |
| Audit NuGet local = 0 vulnérabilité | ✅ |
| `has-pending-model-changes` local = false | ✅ |
| Aucun code `src/` modifié | ✅ |
| Aucun `tests/` modifié | ✅ |
| Aucune migration | ✅ |
| Aucun `*.csproj`/`*.sln` modifié | ✅ |
| Aucun projet `MMV.Application` créé | ✅ |
| Rapport complet (11 sections) | ✅ |
| Commit CI poussé (`32bed23`, sans force) | ✅ |
| CI déclenchée sur `p2b-architecture` (`p2*`) | ✅ run **#21** |
| Step `Check EF Core pending model changes` exécuté en CI | ✅ step #10 success |
| Workflow distant vert | ✅ success (run `27447845006`, commit `32bed23`) |

### ✅ **P2B-2A-CI-R2 = GO DÉFINITIF** — workflow distant **vert** (run #21, `27447845006`, commit `32bed23`, branche `p2b-architecture`). La CI exécute désormais `restore → build → test → audit NuGet → **restore .NET tools** → **has-pending-model-changes**`, et le nouveau contrôle EF passe au vert à distance.

---

## 11. Prochaine étape candidate : P2B-2B

**P2B-2B** (couche application — `MMV.Application`, `RegisterSaleUseCase`/`EnregistrerVente`) **n'est pas
commencée**. Aucun projet `MMV.Application`, aucune règle métier, aucune migration n'a été créé dans cette phase.

Étape candidate : démarrage de **P2B-2B** sous gate explicite (paramètres dédiés, périmètre `src/` rouvert),
**après** confirmation distante du présent contrôle CI si requise.
