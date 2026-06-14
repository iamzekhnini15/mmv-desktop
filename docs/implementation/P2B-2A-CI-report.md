# P2B-2A-CI — Étendre les déclencheurs CI aux branches de travail — RAPPORT

> **Programme 2 — micro-phase d'outillage CI.** Phase **P2B-2A-CI**. Branche `p2b-architecture`.
> Date : 12 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : modifier **uniquement** le workflow GitHub Actions pour que la CI se déclenche aussi
> sur les branches de phase (`phase*`, `p2*`). **Aucun code `src/`/`tests/`, aucune migration, aucun autre
> job ni script touché.**

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2A-CI` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

---

## 2. Pourquoi la CI ne s'est pas déclenchée sur `p2b-architecture`

Lors de la clôture de P2B-2A, les 3 documents ont été committés (`19ace13`) puis la branche
`p2b-architecture` poussée. **Aucun run CI ne s'est déclenché** (vérifié via l'API GitHub Actions : aucun
run pour `head_branch = p2b-architecture` ni pour `19ace13`/`ea382c5`).

**Cause (factuelle, pas un échec de pipeline).** Le workflow [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
restreignait les déclencheurs `push` aux branches `main` et `phase2a-stabilization` :

```yaml
on:
  push:
    branches: [ main, phase2a-stabilization ]
  pull_request:
    branches: [ main ]
  workflow_dispatch:
```

Les phases P2A ont validé leur CI parce qu'elles vivaient sur `phase2a-stabilization` (branche listée). La
branche d'architecture `p2b-architecture` **n'étant pas** un déclencheur `push` et le push n'étant **pas**
une PR vers `main`, le pipeline **n'a pas tourné** (ni vert ni rouge : inexistant). Or le projet bascule
désormais sur des branches de phase (`p2b-architecture`, `p2b-application`, `p2c-...`) : la CI doit les couvrir.

---

## 3. Modification appliquée au trigger

**Seul** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) est modifié, **uniquement** le bloc
`on:` (déclencheurs). Aucun job, aucune commande, aucun script, aucun fichier de code n'est touché.

```diff
 on:
   push:
-    branches: [ main, phase2a-stabilization ]
+    branches:
+      - main
+      - 'phase*'
+      - 'p2*'
   pull_request:
     branches: [ main ]
   workflow_dispatch:
```

**Couverture des motifs :**

| Motif | Branches couvertes |
|---|---|
| `main` | branche principale |
| `'phase*'` | `phase2a-stabilization` (rétro-compatibilité) et toute future `phase…` |
| `'p2*'` | `p2b-architecture`, `p2b-application`, `p2c-…` et toute future branche de phase `p2…` |

`pull_request → main` et `workflow_dispatch` restent **inchangés**.

---

## 4. Fichiers modifiés

| Fichier | Changement |
|---|---|
| [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | déclencheurs `push` étendus à `main`, `phase*`, `p2*` (bloc `on:` uniquement) |
| [`docs/implementation/P2B-2A-CI-report.md`](P2B-2A-CI-report.md) | ce rapport (créé) |

**Aucun** fichier `src/`, **aucun** fichier `tests/`, **aucune** migration, **aucun** autre fichier.

---

## 5. Contrôles locaux

| Commande | Résultat |
|---|---|
| `git status --short` | ` M .github/workflows/ci.yml` (+ `?? docs/implementation/P2B-2A-CI-report.md`) |
| `git diff -- .github/workflows/ci.yml` | uniquement le bloc `on:` (cf. §3) |
| `dotnet restore MMV.sln` | ✅ à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avertissement** (`CS1998` préexistant, [OrderFormViewModel.cs:458](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **320** (Domain **223** + App **97**), 0 échec, 0 ignoré |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 package vulnérable** (5 projets) |
| `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | ✅ **false** |

> Le changement n'affecte que le déclenchement du pipeline : build/test/scan sont identiques (un fichier YAML
> de workflow n'entre pas dans la compilation .NET). La validation locale confirme l'absence de régression.

---

## 6. Commit / push (réalisés sur autorisation explicite)

**Autorisation `ALLOW_COMMIT=true`, `ALLOW_PUSH=true`.** Le changeset (workflow + ce rapport) a été committé
puis poussé (sans force-push) :

```
git add .github/workflows/ci.yml docs/implementation/P2B-2A-CI-report.md
git commit -m "ci(P2B-2A): run workflow on phase branches"
  → 3042e52  2 files changed, 147 insertions(+), 1 deletion(-)
git push origin p2b-architecture
  → ea382c5..3042e52  p2b-architecture -> p2b-architecture (sans force)
```

Le commit ne contient **que** `.github/workflows/ci.yml` (bloc `on:`) et ce rapport. Working tree propre
après commit. Un second commit documentaire consigne la validation CR (cf. §6 bis / §7).

> ⚠️ Subtilité GitHub Actions confirmée : le workflow exécuté est **celui présent sur le commit poussé**.
> Le commit `3042e52` contenant lui-même le trigger élargi (`p2*`), son push a **bien déclenché** le pipeline
> (cf. §6 bis).

---

## 6 bis. Validation CI distante (run réel)

| Élément | Valeur réelle |
|---|---|
| **Run** | **#19** — id **`27441381061`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27441381061 |
| **Commit testé** | **`3042e52`** (`head_sha = 3042e52…`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*` — **le trigger élargi fonctionne**) |
| Événement | `push` |
| Workflow / job | `CI` / `Restore / Build / Test / Scan` |
| Runner | `windows-latest` (GitHub-hosted) |
| Durée | ≈ 2 min 56 s (20:32:16 → 20:35:12 UTC) |
| SDK | **verrouillé par `global.json`** (8.0.x) — step *Setup .NET* ✅ |
| Restore | ✅ **success** |
| Build | ✅ **success** (`-c Debug`, sans `-warnaserror` ; `CS1998` préexistant visible, non bloquant) |
| Test | ✅ **success** (`dotnet test --no-build`, suite **320** confirmée localement ; `.trx` produit) |
| Audit NuGet (High/Critical, JSON + sévérité) | ✅ **success** — **0 vulnérabilité** |
| `has-pending-model-changes` | **non exécuté en CI** (hors workflow) — **confirmé localement = false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps sont en conclusion `success` : *Set up job, Checkout, Setup .NET, Diagnostic SDK, Restore,
Build, Test, Audit des packages vulnérables, Post-steps, Complete job*.

**Couverture P2B-2A.** Le commit testé `3042e52` est le **tip** de `p2b-architecture` et a pour ancêtres
`19ace13` (ADR frontières + plan + rapport P2B-2A) et `ea382c5` (mise à jour CI du rapport P2B-2A). Le run
vert **couvre donc** l'ensemble des documents P2B-2A présents sur la branche ⇒ la gate « pipeline distant
vert » de P2B-2A est **levée** (cf. mise à jour de [P2B-2A-report §17 / §17 bis](P2B-2A-report.md)).

---

## 7. Verdict — GO / NO-GO

| Critère | Statut |
|---|---|
| Seul `.github/workflows/ci.yml` modifié (hors ce rapport) | ✅ |
| Aucun `src/` modifié | ✅ |
| Aucun `tests/` modifié | ✅ |
| Aucune migration | ✅ |
| Build vert | ✅ (1 avert. `CS1998` préexistant) |
| Tests verts (**320**) | ✅ |
| 0 vulnérabilité High/Critical | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Bloc `on:` modifié comme spécifié | ✅ (`main`, `phase*`, `p2*`) |
| Aucun autre job/commande/script touché | ✅ |
| CI déclenchée sur `p2b-architecture` (`p2*`) | ✅ run **#19** |
| Workflow distant vert | ✅ success (run `27441381061`, commit `3042e52`) |

### ✅ **P2B-2A-CI = GO DÉFINITIF** — workflow distant **vert** (run #19, `27441381061`, commit `3042e52`, branche `p2b-architecture`). Le trigger élargi (`p2*`) déclenche désormais la CI sur les branches de phase.

---

## 8. Arrêt obligatoire

Commit `3042e52` (workflow + ce rapport) + commit documentaire de consignation CI, poussés sur
`p2b-architecture` (sans force). **P2B-2B non commencé. `MMV.Application` non créé. `EnregistrerVente` non
commencé.**
