# P2A-0B — Clôture Git et validation CI distante — RAPPORT D'IMPLÉMENTATION

> **Date : 10 juin 2026.** Phase d'outillage : mise à l'index Git des référentiels, exclusion ciblée
> des artefacts documentaires, **durcissement de l'audit NuGet** (dépôt + CI), vérification de
> `global.json`, clarification des anciens documents.
> Périmètre **strictement limité à `P2A-0B`** ([roadmap §P2A-0B](../roadmap/MMV-master-professionalization-roadmap.md)).
> **Aucun code métier modifié.** Aucune étape `P2A-1+` commencée.
>
> *Révision 2 (correction finale avant commit) : audit NuGet robuste (NU1903/NU1904 + JSON/sévérité)
> en remplacement du grep textuel GHSA ; archivage des anciens documents ; uniformisation
> `docs/prompt/` → `docs/prompts/` ; exclusions d'archives limitées à `docs/`.*

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-0B
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit**,
**aucun push**. La clôture Git est *préparée* (changeset **ajouté à l'index Git, en attente de
commit**), pas finalisée.

## 2. Étape exécutée

`P2A-0B` — « Clôture Git et validation CI distante » (Programme 1 — Stabiliser le socle existant).
But : rendre l'Étape 0 reproductible et réellement intégrée au dépôt.

## 3. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-0` possède un rapport | [phase-2a-step0-report.md](phase-2a-step0-report.md) présent | ✅ |
| Verdict `P2A-0` = `GO` | §15 : GO (193/193 tests, 0 vuln, `global.json` + CI créés) | ✅ |
| État réel du dépôt conforme au rapport | Baseline rejoué : restore/build OK, 193/193, 0 vuln | ✅ |
| Écarts dépôt ↔ rapport | Aucun écart fonctionnel ; les éléments « ouverts » de `P2A-0` (référentiels non suivis, ZIP présents, 1er run CI non confirmé) **sont** le périmètre de `P2A-0B` | ✅ |

Aucun prérequis manquant → poursuite autorisée (pas de `NO-GO` de prérequis).

## 4. État Git initial

- Branche : **`phase2a-stabilization`** · Remote : `origin = https://github.com/iamzekhnini15/mmv-desktop.git`
- `HEAD` : `81ee386 chore: checkpoint before phase 2` (inchangé en fin de phase)
- `git status --short` (avant écriture) : 7 fichiers `M`/`D` Étape 0 + référentiels `??`
  (`docs/architecture/`, `docs/domain/`, `docs/roadmap/`, `docs/prompt/`, `docs/implementation/`,
  `docs/PHASE0*.md`), archives `docs/architecture.zip`, `docs/domain.zip`, `global.json`, `.github/`.

## 5. Baseline (rejoué, SDK piloté par `global.json`)

| Commande | Code | Résultat |
|---|---|---|
| `dotnet --version` (racine) | 0 | **8.0.417** (≠ 10.0.103 par défaut → `global.json` actif) |
| `dotnet restore MMV.sln` | 0 | Succès |
| `dotnet build … -c Debug` | 0 | **Succès — 0 erreur** (1 avert. préexistant `CS1998`, hors périmètre) |
| `dotnet test … --no-build` | 0 | **193 ✅ / 0 ❌** (App 79 ; Domain 114) |
| `dotnet list … --vulnerable --include-transitive` | 0 | **0 package vulnérable** (5 projets) |

## 6. Contrat de phase

Affiché avant toute écriture. Périmètre = `.gitignore`, `.github/workflows/ci.yml`,
`Directory.Build.props` (audit NuGet), mise à l'index des référentiels `docs/**.md`, traitement des
anciens documents, vérification `global.json`, rapport. Interdits = code métier, valeurs
réglementaires, sauts majeurs Avalonia/EF, FluentAssertions v7+, refactor/formatage massif. Aucun
ADR requis (outillage/CI). Aucune migration.

## 7. Analyse

`P2A-0` (GO) a laissé trois éléments ouverts, repris ici : (1) référentiels non suivis →
Étape 0 non reproductible ; (2) archives ZIP redondantes ; (3) 1er run CI non confirmé. La présente
révision ajoute trois durcissements demandés à la revue : audit NuGet **non** basé sur un grep
textuel, clarification des anciens documents, et exclusions d'archives **non globales**.

`global.json` — comportement vérifié : depuis la racine le SDK résolu est **8.0.417** (et non
10.0.103) ; `rollForward: latestFeature` maintient la branche **.NET 8** ; build/test verts.

## 8. Décisions / ADR

**Aucun ADR requis** (outillage Git/CI). Micro-décisions documentées :

- **Audit NuGet au niveau dépôt** via `Directory.Build.props` plutôt que par projet : source unique,
  s'applique aux 5 projets.
- **Durcissement conditionnel CI** (variable `CI=true` posée par GitHub Actions) plutôt que global :
  pas de friction pour le développeur local, blocage réservé à la CI.
- **Détection par sévérité (JSON)** plutôt que par grep `GHSA-` : on échoue sur High/Critical et sur
  tout scan **non concluant** (fail-closed).
- **Anciens documents archivés** (et non supprimés) : préservation de l'historique avec mention
  explicite de non-autorité.
- **Exclusions d'archives limitées à `docs/`** : ne pas interdire globalement les archives utiles
  aux tests/au produit.

## 9. Méthode finale d'audit NuGet (correction 1)

Le **grep textuel `GHSA-` a été retiré**. L'audit repose désormais sur deux mécanismes
complémentaires (défense en profondeur) :

**(a) Audit à la restauration — [`Directory.Build.props`](../../Directory.Build.props) :**

```xml
<NuGetAudit>true</NuGetAudit>
<NuGetAuditMode>all</NuGetAuditMode>           <!-- direct + transitif -->
<!-- Condition="'$(CI)' == 'true'" : -->
<WarningsAsErrors>…;NU1900;NU1903;NU1904;NU1905</WarningsAsErrors>  <!-- bloquant en CI -->
<WarningsNotAsErrors>…;NU1901;NU1902</WarningsNotAsErrors>          <!-- visible, non bloquant -->
```

| Code | Sévérité / nature | Local | CI |
|---|---|---|---|
| NU1901 | Low | avertissement | **avertissement (visible, non bloquant)** |
| NU1902 | Moderate | avertissement | **avertissement (visible, non bloquant)** |
| NU1903 | High | avertissement | **ERREUR (bloquant)** |
| NU1904 | Critical | avertissement | **ERREUR (bloquant)** |
| NU1900 / NU1905 | Échec de communication / base d'avis indisponible | avertissement | **ERREUR (bloquant)** |

→ Exigence « un échec de récupération de la base d'avis ne doit pas être interprété comme *aucune
vulnérabilité* » satisfaite : NU1900/NU1905 **bloquent** la CI.

**(b) Contrôle machine en CI — [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) :**
rapport lisible (logs) **+** rapport JSON officiel
`dotnet list MMV.sln package --vulnerable --include-transitive --format json --output-version 1`,
parsé en PowerShell. La CI échoue si une vulnérabilité **High/Critical** est présente, **ou** si le
scan est non concluant (commande en échec, sortie vide, JSON invalide). Une sortie lisible reste
imprimée dans les logs.

**Tests locaux du bloc (4 cas exigés) :**

| Cas | Entrée | Résultat attendu | Obtenu |
|---|---|---|---|
| 1 | rapport réel sans vulnérabilité | succès | ✅ `PASS (no High/Critical)` |
| 2 | JSON simulé avec un avis **High** | échec | ✅ `VULN-FAIL (1 High/Critical)` |
| 3 | commande en échec (code ≠ 0) | échec non concluant | ✅ `FAIL-NONCONCLUSIVE (exitcodes)` |
| 4 | JSON vide / invalide | échec non concluant | ✅ `FAIL-NONCONCLUSIVE (empty / invalid)` |

Vérification locale `Directory.Build.props` : `restore --force` (audit `all`) → **0 avis** ;
build `CI=true` → **0 erreur** (les NU-as-errors ne cassent pas un build sans vuln ; `CS1998` reste
un avertissement car non listé dans `WarningsAsErrors`).

## 10. Traitement des anciens documents (correction 2)

Les deux fichiers ne sont **plus présentés comme référentiels actuels**. Ils conservent une valeur
historique (cartographie / étude exploratoire) → **archivés** (et non supprimés) avec en-tête :

> *Document historique — remplacé par les référentiels consolidés de docs/domain et docs/architecture.
> Ne pas utiliser comme source de vérité actuelle.*

| Avant | Après | Action |
|---|---|---|
| `docs/PHASE0-CARTOGRAPHIE.md` | `docs/archive/PHASE0-CARTOGRAPHIE.md` | `git mv` + en-tête |
| `docs/PHASE0.5-DOMAINE-OPTIQUE.md` | `docs/archive/PHASE0.5-DOMAINE-OPTIQUE.md` | `git mv` + en-tête + lien relatif corrigé (`domain/…` → `../domain/…`) |

**Uniformisation des prompts :** `docs/prompt/` (singulier) → **`docs/prompts/`** (pluriel) via
`git mv` ; l'ancien dossier vide a été supprimé → pas de coexistence `prompt/` + `prompts/`. Aucun
autre lien relatif concerné (la cartographie n'avait aucun lien ; les références internes du prompt
sont des chemins racine, inchangés).

## 11. Limitation des exclusions d'archives (correction 3)

[`.gitignore`](../../.gitignore) : les exclusions globales `*.zip *.7z …` ont été **remplacées** par
des règles **limitées à `docs/`** :

```gitignore
/docs/**/*.zip
/docs/**/*.7z
/docs/**/*.rar
/docs/**/*.tar
/docs/**/*.tar.gz
/docs/**/*.tgz
```

Vérifications : `git check-ignore docs/architecture.zip docs/domain.zip` → **ignorés** ✅ ;
`git check-ignore tests/fixtures/sample.zip` → **non ignoré** ✅ (les archives utiles aux tests / au
produit ailleurs dans le dépôt ne sont plus interdites). Les artefacts de build/test
(`bin/`, `obj/`, `*.trx`, `TestResults/`, `*.log`, `*.tmp`, `*.cache`, `*.db*`) restent couverts.

## 12. Fichiers modifiés / ajoutés (cette phase)

**Ajoutés :** `Directory.Build.props` ; `.github/workflows/ci.yml` ; `global.json` ; référentiels
`docs/architecture/**`, `docs/domain/**`, `docs/roadmap/**`, `docs/prompts/**`,
`docs/implementation/**` ; `docs/archive/PHASE0-CARTOGRAPHIE.md`,
`docs/archive/PHASE0.5-DOMAINE-OPTIQUE.md`.
**Modifiés :** `.gitignore` ; (Étape 0 préexistante) `*.csproj` ×4, fixtures de test ×3.
**Supprimés :** 4 fichiers `*.old` (Étape 0).
**Aucun fichier de code métier** (`src/**` hors `.csproj`) modifié dans cette phase.

## 13. Migrations

Aucune. La phase ne touche pas la base (`__EFMigrationsHistory`, schéma, `EnsureCreated` inchangés).

## 14. Tests

- Suite existante : **193 ✅ / 0 ❌** (inchangée — aucun test modifié dans cette phase).
- Bloc d'audit JSON : 4 cas validés localement (cf. §9).

## 15. Commandes (extraits)

```bash
# Contrôles
dotnet --version            # 8.0.417 (global.json actif)
dotnet restore MMV.sln ; dotnet build MMV.sln --no-restore -c Debug
dotnet test   MMV.sln --no-build -c Debug
dotnet list   MMV.sln package --vulnerable --include-transitive
dotnet list   MMV.sln package --vulnerable --include-transitive --format json --output-version 1

# Préparation (staging, SANS commit ni push)
git mv docs/PHASE0-CARTOGRAPHIE.md         docs/archive/PHASE0-CARTOGRAPHIE.md
git mv docs/PHASE0.5-DOMAINE-OPTIQUE.md    docs/archive/PHASE0.5-DOMAINE-OPTIQUE.md
git mv docs/prompt/CLAUDE-CODE-controlled-phase-executor.md \
       docs/prompts/CLAUDE-CODE-controlled-phase-executor.md
git add -A      # zips docs/ exclus par .gitignore

# Inspection
git status --short ; git diff --cached --name-status ; git diff --cached --stat ; git diff --cached --check
```

## 16. Résultats / contrôles finaux

| Contrôle | Résultat |
|---|---|
| `dotnet restore / build` | Succès — **0 erreur** |
| `dotnet test` | **193 ✅ / 0 ❌** |
| `dotnet list … --vulnerable` | **0 package vulnérable** |
| `git diff --cached --stat` | **35 fichiers, +5059 / −719** |
| `git diff --cached --check` | **3 espaces de fin** dans `docs/roadmap/…md` (l. 3–5) = **retours-ligne Markdown intentionnels** (`  ` en fin de ligne) d'un référentiel préexistant — **0 dans les fichiers de code ou ceux rédigés en P2A-0B**. Conservés (pas de reformatage massif). |
| ZIP / `*.db` / `bin` / `obj` / `*.trx` stagés | **Aucun** (vérifié `git ls-files --cached`) |
| Secrets dans les ajouts stagés | **Aucun** (scan motifs password/clé/token/connectionstring) |
| Données utilisateur / modif. métier hors Étape 0 | **Aucune** |

## 17. Vulnérabilités

**0 package vulnérable** (5 projets). L'audit durci échoue désormais aussi sur High/Critical (CI),
sur audit non concluant (NU1900/NU1905) et sur JSON manquant/invalide. Aucune vulnérabilité
High/Critical non traitée.

## 18. Risques résiduels

1. **Pipeline distant non observé** — `ALLOW_PUSH=false` → **`VALIDATION DISTANTE REQUISE`** (§20).
2. **NU1900/NU1905 bloquants en CI** — un incident transitoire de source NuGet fera échouer la CI
   (choix assumé : préférer un échec visible à un faux « 0 vuln » ; relancer le job si transitoire).
3. **Espaces de fin Markdown** dans la roadmap (retours-ligne intentionnels) — conservés ; cosmétique,
   hors code.
4. **Changeset à l'index non commité** — réversible par `git restore --staged .`. `HEAD` inchangé.
5. **`rollForward: latestFeature`** — sur un poste sans SDK 8.0.x le build échoue volontairement
   (la CI installe 8.0.417 via `setup-dotnet`).
6. Risques résiduels Étape 0 hérités (CS1998, vues dupliquées, packages outdated non vulnérables,
   pin Tmds.DBus) — **inchangés**, hors périmètre.

Aucun ne touche au métier, au réglementaire, ni aux interdictions de la phase.

## 19. Éléments différés

- **Commit** du changeset préparé → requiert `ALLOW_COMMIT=true`.
- **Push** + **confirmation du 1er run CI vert** → requiert `ALLOW_PUSH=true`.
- SHA-pinning des actions GitHub, élargissement des triggers CI → hors périmètre « scan NuGet »
  (candidats Programme 7 `P2H-8D`).

## 20. État Git final & verdict

**État Git final :** `HEAD` = `81ee386` (inchangé). Changeset **ajouté à l'index Git, en attente de
commit** — **35 fichiers** (liste exacte ci-dessous). `docs/architecture.zip` / `docs/domain.zip`
restent **non suivis et ignorés**. **Aucun commit, aucun push.**

### Liste exacte des fichiers stagés (`git diff --cached --name-status`)

```
A  .github/workflows/ci.yml
M  .gitignore
A  Directory.Build.props
A  docs/architecture/adr-candidates.md
A  docs/architecture/dependency-map.md
A  docs/architecture/migration-roadmap.md
A  docs/architecture/phase-1-technical-audit.md
A  docs/architecture/phase-1b-validation-report.md
A  docs/architecture/risk-register.md
A  docs/archive/PHASE0-CARTOGRAPHIE.md
A  docs/archive/PHASE0.5-DOMAINE-OPTIQUE.md
A  docs/domain/backlog-by-market.md
A  docs/domain/country-capability-matrix.md
A  docs/domain/glossary-multicountry.md
A  docs/domain/phase-0.5-multicountry.md
A  docs/domain/phase-0.5c-validation-report.md
A  docs/domain/phase-0.5d-consistency-report.md
A  docs/domain/phase-1-technical-audit-prompt.md
A  docs/domain/regulatory-source-register.md
A  docs/implementation/P2A-0B-report.md
A  docs/implementation/phase-2a-step0-report.md
A  docs/prompts/CLAUDE-CODE-controlled-phase-executor.md
A  docs/roadmap/MMV-master-professionalization-roadmap.md
A  global.json
M  src/MMV.App/MMV.App.csproj
D  src/MMV.App/Views/CustomerFormView.axaml.old
D  src/MMV.App/Views/ProductDetailView.old.axaml.cs
M  src/MMV.Infrastructure/MMV.Infrastructure.csproj
D  tests/MMV.App.Tests/Integration/CustomerRepositoryTests.cs.old
M  tests/MMV.App.Tests/MMV.App.Tests.csproj
D  tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs.old
M  tests/MMV.Domain.Tests/Data/DbContextTests.cs
M  tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj
M  tests/MMV.Domain.Tests/RepositoryTests/UserRepositoryTests.cs
M  tests/MMV.Domain.Tests/ServiceTests/ProductServiceTests.cs
```

### Message de commit proposé (non exécuté)

```text
chore(P2A-0B): track referentials, archive legacy docs, harden CI NuGet audit

- NuGet audit au niveau dépôt (Directory.Build.props: NuGetAuditMode=all ;
  NU1903/NU1904 + NU1900/NU1905 bloquants en CI ; NU1901/NU1902 visibles)
- CI: remplace le grep texte GHSA par un parse JSON/sévérité
  (échec sur High/Critical ; fail-closed si scan non concluant)
- Suit les référentiels docs (architecture/domain/roadmap/prompts/implementation)
- Archive les documents obsolètes dans docs/archive/ (en-tête « document historique »)
- Renomme docs/prompt/ -> docs/prompts/ (corrige le lien relatif)
- .gitignore: exclut les archives de documentation, portée limitée à docs/
- Finalise le changeset Étape 0 (pins de paquets, fixtures de test, nettoyage .old)

Aucun code métier/réglementaire modifié. Pipeline distant en attente (VALIDATION DISTANTE REQUISE).
```

### Verdict — **GO** (local) — avec gate explicite **`VALIDATION DISTANTE REQUISE`**

| Critère de sortie `P2A-0B` | État |
|---|---|
| Référentiels **ajoutés à l'index Git, en attente de commit** | ✅ |
| ZIP / artefacts exclus (portée `docs/`) | ✅ |
| Scan NuGet CI durci (audit dépôt + JSON/sévérité) | ✅ (4 cas validés) |
| Anciens documents clarifiés (archivés + en-tête) | ✅ |
| Prompts uniformisés (`docs/prompts/`) | ✅ |
| Comportement `global.json` vérifié | ✅ |
| Commit propre préparé | ✅ **non commité** |
| Aucun code métier modifié | ✅ |
| Rapport `P2A-0B-report.md` | ✅ |
| Pipeline distant vert **ou** mention explicite | ⏳ **`VALIDATION DISTANTE REQUISE`** |

Pas de `NO-GO` : aucun prérequis manquant, aucun test rouge, aucune perte de données, aucune CI
rouge, aucune règle réglementaire en jeu, aucune modification hors périmètre. Seule réserve : la
confirmation distante, **différée par conception** (`ALLOW_PUSH=false`).

**Condition de passage à `P2A-1A` :** la revue humaine doit (1) autoriser le commit, (2) pousser,
(3) **confirmer le 1er run GitHub Actions vert**.

## 21. Prochaine étape candidate (NON exécutée)

`P2A-1A` — « Cycle de vie SQLite et chemin de base unique » (ADR adoption des migrations, chemin DB
unique, détection des bases `EnsureCreated`, sauvegarde/restauration testées). **Non commencée.**
Ne pas démarrer sans verdict `P2A-0B` confirmé **et** pipeline distant vert, puis `TARGET_PHASE_ID = P2A-1A` explicite.

---

**Arrêt obligatoire.** Fin de `P2A-0B`. Aucun commit, aucun push, aucune amorce de `P2A-1A`.
