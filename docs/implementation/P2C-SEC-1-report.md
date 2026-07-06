# Rapport P2C-SEC-1 — VERDICT : GO (remédiation réelle, 0 vulnérabilité)

> Phase sécurité dédiée. Objectif : lever le blocage `GHSA-2m69-gcr7-jv3q` /
> CVE-2025-6965 qui a mis P2C-GLOBAL en NO-GO baseline. **Aucun code métier, aucun
> ViewModel, aucune View, aucun DbContext, aucune migration touché.** Seul fichier de
> production modifié : `src/MMV.Infrastructure/MMV.Infrastructure.csproj` (+7 lignes,
> un pin de sécurité de paquet).

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-SEC-1 |
| `EXECUTION_MODE` | SECURITY_FIX |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

Aucun commit créé, aucun push effectué (conforme).

## 2. État Git initial

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `git branch --show-current` | `p2c-ui-cleanup` | `p2c-ui-cleanup` | ✅ |
| `git status --short` | propre (hors rapport P2C-GLOBAL) | seul `?? docs/implementation/P2C-GLOBAL-report.md` (untracked, artefact attendu §3) | ✅ |
| Présence P2C-4 applicatif | oui | `f52f4cd feat(P2C-4): move prescription writes to application use cases` | ✅ |
| Présence P2C-4 documentaire | oui | `d2961ff docs(P2C-4): record documentary commit CI validation (CI #54 success)` | ✅ |
| Présence rapport P2C-GLOBAL NO-GO | oui (si créé) | présent (untracked) | ✅ |

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

**Précondition SATISFAITE.** Aucun changement de code non commité en entrée : le seul
élément non suivi est le rapport documentaire P2C-GLOBAL, explicitement anticipé par le
brief §3 (« présence du rapport P2C-GLOBAL NO-GO baseline si déjà créé »). → Pas de NO-GO
précondition.

## 3. Baseline sécurité (avant modification)

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `dotnet restore MMV.sln` | OK | OK (5× warning NU1903 SQLite) | ✅ (avec vuln) |
| `dotnet build --no-restore -c Debug` | vert | vert, 0 erreur (warnings : NU1903 ×5 + CS1998 pré-existant `OrderFormViewModel`) | ✅ |
| `dotnet test --no-build -c Debug` | ≥ 463 | **463** (App 161 + Application 79 + Domain 223) | ✅ |
| `dotnet list ... --vulnerable --include-transitive` | 0 | **1 vuln HIGH transitive** | ❌ (cible de la phase) |
| `dotnet tool restore` | OK | OK (`dotnet-ef` 8.0.27) | ✅ |
| `dotnet ef ... has-pending-model-changes` | false | **false** | ✅ |
| `dotnet list Application reference` | Domain seul | `..\MMV.Domain\MMV.Domain.csproj` seul | ✅ |
| `dotnet list Application package` | DI.Abstractions seul | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul | ✅ |

## 4. Advisory et paquet impacté

| Attribut | Valeur |
|---|---|
| Avis | `GHSA-2m69-gcr7-jv3q` |
| CVE | `CVE-2025-6965` |
| Gravité | **High** (CVSS 7.2) |
| Paquet | `SQLitePCLRaw.lib.e_sqlite3` (binaire natif SQLite) |
| Versions affectées | ≤ 2.1.11 (SQLite < 3.50.2) |
| Correctif | SQLite ≥ 3.50.2 |
| Nature | **Transitive** — jamais référencée directement dans le dépôt |
| Origine | Avis amont publié entre la baseline P2C-4 (2026-06-16) et P2C-GLOBAL (2026-07-05) ; aucune régression de code MMV |

## 5. Chaîne transitive exacte (avant)

```
MMV.Infrastructure
  └─ Microsoft.EntityFrameworkCore.Sqlite 8.0.27
       └─ Microsoft.Data.Sqlite.Core 8.0.27
            └─ SQLitePCLRaw.bundle_e_sqlite3 2.1.6
                 ├─ SQLitePCLRaw.core 2.1.6
                 ├─ SQLitePCLRaw.provider.e_sqlite3 2.1.6
                 └─ SQLitePCLRaw.lib.e_sqlite3 2.1.6   ← VULNÉRABLE
```

Projets impactés par transitivité : Infrastructure, App, App.Tests, Domain.Tests,
Application.Tests. Projets **non** impactés : Domain, **Application** (couche restée pure).

## 6. Options testées

| Option | Description | Résultat |
|---|---|---|
| **A — Pin bundle** | `PackageReference SQLitePCLRaw.bundle_e_sqlite3 3.0.0` (top-level dans Infrastructure) | **RETENUE** — remonte `lib.e_sqlite3` à 3.50.3, 0 vulnérabilité, build/tests verts |
| B — Pin natif direct | `SQLitePCLRaw.lib.e_sqlite3 3.50.3` | Non nécessaire (A suffit) |
| C — Montée EF Core SQLite | bump `Microsoft.EntityFrameworkCore.Sqlite` | Non nécessaire (A suffit, évite tout risque de saut EF) |
| D — Exception documentée | suppression ciblée d'audit | Non nécessaire (remédiation réelle obtenue) |

L'option A a été validée dès le premier essai. B/C/D non appliquées car superflues.

## 7. Option retenue

**Option A** — pin de sécurité top-level `SQLitePCLRaw.bundle_e_sqlite3 3.0.0` dans
`src/MMV.Infrastructure/MMV.Infrastructure.csproj`. Le bundle 3.0.0 dépend de
`SQLitePCLRaw.lib.e_sqlite3 >= 3.50.3` ; NuGet remonte donc le binaire natif au-dessus du
correctif SQLite 3.50.2. Technique identique au pin `System.Text.Json` (Étape 0) déjà
présent dans le même fichier. Aucun changement EF, aucune migration.

## 8. Fichiers modifiés

| Fichier | Nature | Détail |
|---|---|---|
| `src/MMV.Infrastructure/MMV.Infrastructure.csproj` | modifié (+7 lignes) | ajout du `PackageReference` de pin + commentaire de sécurité |
| `docs/security/P2C-SEC-1-sqlite-cve.md` | créé | note de sécurité en français simple |
| `docs/implementation/P2C-SEC-1-report.md` | créé | ce rapport |

Aucun autre fichier de production touché. Aucun ViewModel, View, code-behind, entité
Domain, DbContext, repository, migration, workflow CI modifié.

## 9. Contrôles exécutés (après remédiation)

```
git status --short
git diff --stat
git diff --check
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test  MMV.sln --no-build   -c Debug
dotnet list  MMV.sln package --vulnerable --include-transitive
dotnet list  src/MMV.Infrastructure/MMV.Infrastructure.csproj package --include-transitive
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
dotnet list  src/MMV.Application/MMV.Application.csproj reference
dotnet list  src/MMV.Application/MMV.Application.csproj package
```

## 10. Résultats

| Contrôle | Attendu | Obtenu | Statut |
|---|---|---|---|
| `git diff --stat` | 1 fichier prod | `MMV.Infrastructure.csproj \| 7 +++++++` | ✅ |
| `git diff --check` | pas d'erreur d'espace | aucune | ✅ |
| `dotnet restore` | sans NU1903 | aucun NU1903 | ✅ |
| `dotnet build` | vert | vert, 0 erreur, 1 warning (CS1998 pré-existant, hors périmètre) | ✅ |
| `dotnet test` | ≥ 463 | **463** (161 + 79 + 223) | ✅ |
| `--vulnerable --include-transitive` | **0** | **0 vulnérabilité** sur les 7 projets | ✅ |
| `lib.e_sqlite3` résolu | ≥ 3.50.2 | **3.50.3** | ✅ |
| `has-pending-model-changes` | false | false | ✅ |
| Application reference | Domain seul | Domain seul | ✅ |
| Application package | DI.Abstractions seul | DI.Abstractions seul | ✅ |

Versions SQLite résolues après correction :

```
SQLitePCLRaw.bundle_e_sqlite3      3.0.0   (top-level désormais)
SQLitePCLRaw.core                  3.0.0
SQLitePCLRaw.provider.e_sqlite3    3.0.0
SQLitePCLRaw.config.e_sqlite3      3.0.0
SQLitePCLRaw.lib.e_sqlite3         3.50.3  (était 2.1.6, vulnérable)
Microsoft.EntityFrameworkCore.Sqlite   8.0.27 (inchangé)
Microsoft.Data.Sqlite.Core             8.0.27 (inchangé)
```

## 11. Migrations créées ou non

**Aucune migration créée.** `has-pending-model-changes = false` : le modèle EF est
inchangé, aucune divergence introduite.

## 12. Impact sur Application / Domain / UI

- **Domain** : aucun impact (aucune vuln, aucun code touché).
- **Application** : reste **pure** — référence `MMV.Domain` seule, paquet
  `Microsoft.Extensions.DependencyInjection.Abstractions` seul.
- **UI (App / ViewModels / Views)** : aucun fichier modifié.
- **Infrastructure** : uniquement le manifeste de paquets (pin de sécurité), aucun code C#,
  aucun DbContext, aucun repository, aucune migration.

## 13. Verdict

### **P2C-SEC-1 = GO (remédiation réelle, 0 vulnérabilité).**

La CVE `GHSA-2m69-gcr7-jv3q` / CVE-2025-6965 est **réellement corrigée** (pas d'exception,
pas de contournement d'audit) : `SQLitePCLRaw.lib.e_sqlite3` passe de 2.1.6 (vulnérable) à
3.50.3 (corrigé). Build vert, 463 tests verts, EF sans changement pending, aucune
migration, aucun modèle EF modifié, Application pure, UI intacte.

## 14. Condition de reprise P2C-GLOBAL

**P2C-GLOBAL peut être relancé** depuis une baseline propre : le gate « 0 vulnérabilité »
est désormais satisfait. Séquence de reprise recommandée :

1. (facultatif, sur décision du demandeur) committer le pin de sécurité P2C-SEC-1 +
   rapports, **puis**
2. relancer P2C-GLOBAL, dont la baseline `dotnet list ... --vulnerable` renverra
   désormais **0 vulnérabilité**.

> Note : cette phase respecte `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`. La
> modification du `.csproj` et les rapports sont laissés **non commités** dans le working
> tree pour décision du demandeur.
