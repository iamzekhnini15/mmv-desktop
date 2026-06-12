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

## 6. Commit / push

**Non réalisés** (`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`). Le changeset reste dans l'arbre de travail :
- `.github/workflows/ci.yml` (modifié) ;
- `docs/implementation/P2B-2A-CI-report.md` (ajouté).

**Effet attendu après autorisation de push** : un push de `p2b-architecture` (ou de toute branche `phase*`/`p2*`)
déclenchera désormais le workflow `CI`, ce qui permettra d'obtenir la **validation distante** de P2B-2A
(actuellement `VALIDATION DISTANTE REQUISE`, cf. [P2B-2A-report §17 bis](P2B-2A-report.md)).

> ⚠️ Subtilité GitHub Actions : le workflow exécuté est **celui présent sur le commit poussé**. Le trigger
> élargi prendra effet pour les **pushs effectués après** que ce `ci.yml` est sur la branche. Le premier push
> contenant cette modification déclenchera donc le pipeline.

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

### ✅ **P2B-2A-CI = GO (validation locale)** — prêt à commit/push sur autorisation explicite.

---

## 8. Arrêt obligatoire

Aucun commit, aucun push. **P2B-2B non commencé. `MMV.Application` non créé. `EnregistrerVente` non
commencé.** Le changeset (workflow + ce rapport) attend l'autorisation `ALLOW_COMMIT/PUSH`.
