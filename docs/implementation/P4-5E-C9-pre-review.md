# P4-5E-C9 Pre Review

> **Statut : C9 FAIT LOCALEMENT. PRÉ-REVUE, AUCUN COMMIT, AUCUN PUSH.**
> Reprise après la validation architecte de C8. Périmètre : clôture documentaire, sans changement fonctionnel,
> de code, de modèle EF ni d'architecture.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> Ce document **complète** [la pré-revue C8](P4-5E-C8-pre-review.md). **Le dépôt réel prime sur ce document.**

---

## 1. Résumé exécutif

- **`SPRINTS.md` §2.4 corrigé** (Q12 de C8). Les deux commandes dangereuses, `migrations add … --startup-project
  src/MMV.App` et `database update … --startup-project src/MMV.App`, sont **supprimées**. Elles sont remplacées
  par un renvoi à `CONTRIBUTING.md`. Le reste du fichier est identique.
- **`ARCHITECTURE.md`** : vérification d'alignement complète. **Une seule** correction : l'arborescence de
  `MMV.Infrastructure`. Elle présentait `Migrations/` comme une chaîne unique, réduite à `InitialCreate`, ce qui
  contredisait la section « Migrations EF Core » du même document. Plus aucune commande EF dangereuse depuis C8.
  Les passages historiques ou illustratifs sont laissés en l'état et listés au §6 (Q18).
- **`README.md`** : analysé, **non modifié**. Il ne contient aucune commande EF. Sa procédure (`restore`,
  `build`, `run`) est compatible avec P4-5E.
- **Balayage du dépôt** : hors rapports historiques de `docs/implementation/`, **aucune** consigne active
  `--startup-project src/MMV.App` ni `dotnet ef database update/drop` ne subsiste. Les seules occurrences
  restantes sont les interdits eux-mêmes (`CONTRIBUTING.md` n° 1 et 2, `ARCHITECTURE.md:380`).
- **Périmètre** : seuls deux fichiers Markdown changent, en plus de ce rapport. Les 716 fichiers de code et de
  configuration ont une empreinte identique avant et après C9.
- **Tests** : build 0 avertissement, 0 erreur. **1953 / 1953 verts, 0 ignoré**, inchangé depuis C8.
- **Point bloquant pour le commit** (Q17) : C1 à C8 ne sont pas encore commités, et `ARCHITECTURE.md` porte à la
  fois le bloc C8 et le bloc C9. L'ordre des commits est à décider.

---

## 2. SHA avant

| | Valeur |
|---|---|
| HEAD | `440ddcb352b12e0c6900cfd5d6df550e234856c5` (`docs(P4-5D-R): close civil date migration preparation`). **Aucun commit en C9.** |
| Branche | `p4-multi-poste`, en avance de 7 commits sur `origin/p4-multi-poste` (état du début de session) |
| blob `SPRINTS.md` avant | `5c4c09a75e95e5658bdc452866361ca3b0ab4267`, identique à `HEAD:SPRINTS.md` |
| blob `ARCHITECTURE.md` avant | `3388495ea3955ee2e2f17cadd3fe89b5bef1753f`, **identique à l'état « après » de C8**. Recalculé depuis le fichier actuel, bloc C9 inversé |
| blob `README.md` | `7f945e098d0ce10e9f5119b1e80a53f264503210`, identique à `HEAD:README.md`, **avant et après** |
| blob `SPRINTS.md` après | `a4ba5848113191c85719afa4c6cfb87bee2a5137` · SHA-256 `b4f70c4a…aa70f` |
| blob `ARCHITECTURE.md` après | `33a45e498609e7d95ad876f5b7d054da01d28cf4` · SHA-256 `d9b5efd6…07848` |

`git status` et `git diff --stat` ont été capturés avant toute modification. L'arbre de travail contenait déjà
les changements non commités de C1 à C8 : 8 fichiers modifiés, dont `.cs`, `.csproj`, `.sln` et `ci.yml`, et
15 entrées non suivies. Chaque fichier modifié ou non suivi, et chaque fichier `.cs`, `.csproj`, `.sln`, `.yml`,
`.json`, `.props` et `.targets` hors `bin/` et `obj/`, a été haché (SHA-1) avant C9 pour permettre la
comparaison du §4.

---

## 3. Fichiers modifiés

### `SPRINTS.md`

| | |
|---|---|
| Raison | Q12 de C8 : les seules consignes EF dangereuses encore actives du dépôt |
| Lignes | bloc `@@ -99,10 +99,9 @@`. Anciennes lignes **102-105** (bloc `bash` de 2 commandes) → nouvelles lignes **102-104** (renvoi). **+3 / −4**. 1310 → 1309 lignes |
| Format | CRLF conservé (1309 / 1309), sans BOM |

```diff
 #### 2.4 Migrations
-```bash
-dotnet ef migrations add InitialCreate --project src/MMV.Infrastructure --startup-project src/MMV.App
-dotnet ef database update --project src/MMV.Infrastructure --startup-project src/MMV.App
-```
+> Les commandes EF Core d'origine de ce sprint ont été retirées en P4-5E-C9. Elles prenaient `src/MMV.App` comme
+> projet de démarrage et utilisaient `dotnet ef database update`, deux pratiques désormais interdites. La procédure
+> officielle, pour les chaînes SQLite et PostgreSQL, est dans [CONTRIBUTING.md](CONTRIBUTING.md).
```

Le renvoi pointe vers le fichier, sans ancre : la procédure utile au lecteur de §2.4 couvre plusieurs sections
de `CONTRIBUTING.md`. Les autres mentions historiques du Sprint 2 sont **conservées** : case cochée l.65,
livrable `InitialCreate` l.74, ligne *Stack* l.4.

### `ARCHITECTURE.md`

| | |
|---|---|
| Raison | référence mono-chaîne SQLite dans une section descriptive, non historique. Elle contredisait la section « Migrations EF Core » du même document (deux chaînes, 14 migrations SQLite) |
| Lignes (delta C9 seul) | un bloc. Anciennes lignes **184-187** → nouvelles lignes **184-186**. **+3 / −4**. 1089 → 1088 lignes |
| Delta cumulé depuis HEAD | bloc C9 et bloc C8 (§ « Migrations EF Core », inchangé depuis la validation C8) |
| Format | CRLF conservé (1088 / 1088), sans BOM |

```diff
 │
-├── Migrations/                       # Migrations EF Core
-│   ├── 20260127184542_InitialCreate.cs
-│   ├── 20260127184542_InitialCreate.Designer.cs
-│   └── OpticDbContextModelSnapshot.cs
+├── Migrations/                       # Migrations EF Core : chaîne SQLite uniquement
+│                                     # Chaîne PostgreSQL : src/MMV.Infrastructure.PostgreSQL.Migrations/
+│                                     # Voir « Migrations EF Core » ci-dessous et CONTRIBUTING.md
 │
```

Aucun nombre de migrations n'est inscrit dans l'arborescence : il serait faux au prochain ajout.

**Vérifié et laissé en l'état**

| Lignes (après C9) | Contenu | Raison du maintien |
|---|---|---|
| 22 | schéma d'ensemble : « EF Core + SQLite + Repositories + Migrations » | illustration d'architecture, pas une consigne |
| 156 | `OpticDbContextFactory.cs # Factory design-time pour les migrations` | **exact** : c'est la factory design-time des deux chaînes |
| 208-238 | `appsettings.json` (`"AutoMigrate": true`) et `OnConfiguring` avec `UseSqlite` fixe | configuration d'exécution, pas une procédure EF. **Obsolète** : voir Q18 |
| 342-383 | section « Migrations EF Core » | corrigée et validée en C8 ; non retouchée |
| 385-397 | « Migration Initiale (Sprint 2) » | section historique, explicitement datée ; ne contient aucune commande |
| 788-789, 821 | `cd src/MMV.App` puis `dotnet run` ; `dotnet watch run --project src/MMV.App` | lancement **voulu** de l'application, pas une commande `dotnet ef`. L'usage de `dotnet ef` depuis ce dossier est couvert par l'interdit n° 1 de `CONTRIBUTING.md` |
| 1024-1031 | diagramme « Migrations : InitialCreate, AddOrders… » | diagramme d'architecture illustratif (`AddOrders` n'a jamais existé). Voir Q18 |
| 1056-1085 | second bloc `appsettings.json` (`"AutoMigrate": true`) | idem 208-238 |
| 5, 1088 | date du 27 janvier 2026 | choix validé en C8 : la date d'en-tête reste inchangée |

### `README.md` : analysé, non modifié

| Critère de la consigne | Constat |
|---|---|
| anciennes commandes EF | **aucune** : pas de `dotnet ef` dans le fichier |
| procédure incompatible avec P4-5E | **aucune** : `dotnet restore`, `dotnet build`, `dotnet run --project src/MMV.App`. Sans variable, l'application démarre sur SQLite, ce qui est compatible avec P4-5E |

La règle « sinon, ne rien modifier » s'applique. Les lacunes descriptives relevées sont au §6 (Q19).

### Ce rapport

`docs/implementation/P4-5E-C9-pre-review.md` : nouveau.

---

## 4. Vérification périmètre

**`git diff --name-only` après C9**

```
.github/workflows/ci.yml                                              ← C7, inchangé en C9
ARCHITECTURE.md                                                       ← C8 + C9
MMV.sln                                                               ← C1–C7, inchangé en C9
SPRINTS.md                                                            ← C9 (seule nouvelle entrée)
src/MMV.App/MMV.App.csproj                                            ← C1–C7, inchangé en C9
src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs     ← C1–C7, inchangé en C9
src/MMV.Infrastructure/Data/OpticDbContextFactory.cs                  ← C1–C7, inchangé en C9
tests/MMV.Domain.Tests/Configuration/DatabaseProviderResolverTests.cs ← C1–C7, inchangé en C9
tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj                        ← C1–C7, inchangé en C9
```

La liste brute contient des fichiers non Markdown : ce sont les changements **non commités** de C1 à C8, déjà
présents au début de la session. Elle ne peut donc pas être « Markdown seulement ». La preuve du périmètre de C9
est la comparaison avant / après :

| Contrôle | Résultat |
|---|---|
| `git diff --name-only`, avant → après | une seule ligne ajoutée : `SPRINTS.md` |
| empreintes des 27 fichiers modifiés ou non suivis | seul `ARCHITECTURE.md` change ; `SPRINTS.md` s'ajoute à la liste |
| empreintes des **716** fichiers `.cs`, `.csproj`, `.sln`, `.yml`, `.json`, `.props` et `.targets` | **identiques** avant C9, après les modifications et après les tests |
| `README.md` | `git diff --quiet` : **inchangé** |

**Absences confirmées explicitement**

| Élément | Constat |
|---|---|
| aucun code (`.cs`) | aucune empreinte modifiée |
| aucun `.csproj`, aucun `.sln` | aucune empreinte modifiée |
| aucune migration | `src/MMV.Infrastructure/Migrations/` : 0 fichier modifié par rapport à HEAD. `src/MMV.Infrastructure.PostgreSQL.Migrations/` (non suivi) : empreintes identiques |
| aucun ADR | `docs/architecture/` : 0 fichier modifié par rapport à HEAD. Le seul fichier non suivi du dossier (`P5-product-completion-roadmap.md`) a une empreinte identique |
| aucune CI | `.github/` : la seule différence avec HEAD est `ci.yml`, issue de C7. Son empreinte est **identique** avant et après C9 |
| aucune configuration d'exécution | aucun fichier `.json` ni `.props` modifié |

---

## 5. Tests

**Environnement.** Comme en C8, le `dotnet` placé en tête du `PATH` (`C:\Program Files (x86)\dotnet`) n'a aucun
SDK. Les commandes ont utilisé `C:\Program Files\dotnet`, **SDK 8.0.425**, accepté par `global.json` en
`latestFeature`. Ce SDK a été préfixé au `PATH` de chaque commande. Rien n'a été installé ni modifié sur le
poste.

**Build**

```
> dotnet build MMV.sln -c Debug
  MMV.Domain -> …\src\MMV.Domain\bin\Debug\net8.0\MMV.Domain.dll
  MMV.Application -> …\src\MMV.Application\bin\Debug\net8.0\MMV.Application.dll
  MMV.Infrastructure -> …\src\MMV.Infrastructure\bin\Debug\net8.0\MMV.Infrastructure.dll
  MMV.Application.Tests -> …\tests\MMV.Application.Tests\bin\Debug\net8.0\MMV.Application.Tests.dll
  MMV.Infrastructure.PostgreSQL.Migrations -> …\bin\Debug\net8.0\MMV.Infrastructure.PostgreSQL.Migrations.dll
  MMV.Domain.Tests -> …\tests\MMV.Domain.Tests\bin\Debug\net8.0\MMV.Domain.Tests.dll
  MMV.App -> …\src\MMV.App\bin\Debug\net8.0\MMV.App.dll
  MMV.App.Tests -> …\tests\MMV.App.Tests\bin\Debug\net8.0\MMV.App.Tests.dll

La génération a réussi.
    0 Avertissement(s)
    0 Erreur(s)
Temps écoulé 00:00:14.90
```

**Tests** (commande exacte de la consigne, sans `--no-build`)

```
> dotnet test MMV.sln -c Debug
Réussi!  - échec : 0, réussite :  260, ignorée(s) : 0, total :  260, durée :  1 s - MMV.App.Tests.dll (net8.0)
Réussi!  - échec : 0, réussite :  628, ignorée(s) : 0, total :  628, durée : 42 s - MMV.Application.Tests.dll (net8.0)
Réussi!  - échec : 0, réussite : 1065, ignorée(s) : 0, total : 1065, durée : 54 s - MMV.Domain.Tests.dll (net8.0)
```

| Attendu | Obtenu |
|---|---|
| ≥ 1953 tests | **1953** (260 + 628 + 1065) |
| 0 erreur | **0** échec, 0 erreur de build |
| 0 warning | **0** avertissement (build ; aucun avertissement dans la sortie de `dotnet test`) |
| 0 ignoré | **0** |

Le compte est identique à celui de C8. C9 ne modifie aucun fichier compilé et n'ajoute aucun test. Les tests
n'ont modifié aucun fichier du dépôt : les empreintes sont identiques avant et après leur exécution.

---

## 6. Questions ouvertes architecte

**Q17 — Ordre et découpage des commits (bloquant pour le commit C9).** HEAD est encore `440ddcb` (P4-5D-R) :
aucune des phases C1 à C8 n'est commitée. Trois conséquences :

1. `ARCHITECTURE.md` porte le bloc C8 **et** le bloc C9. Un `git add ARCHITECTURE.md` fait pour C9 embarquerait
   aussi C8.
2. Le renvoi de `SPRINTS.md` vise `CONTRIBUTING.md`, et le bloc C9 d'`ARCHITECTURE.md` nomme
   `src/MMV.Infrastructure.PostgreSQL.Migrations/`. Les deux sont **non suivis**. Commité seul sur HEAD, C9
   renverrait vers des fichiers absents.
3. Tout `git add -A` ou `git commit -a` embarquerait les changements de code de C1 à C7.

**Proposition** : commiter d'abord C1 à C8, selon les découpages déjà validés. Commiter ensuite C9 avec
**exactement** trois fichiers : `SPRINTS.md`, `ARCHITECTURE.md` (dont le diff ne contiendra alors que le bloc
C9) et ce rapport. Décision nécessaire : l'ordre, et le lot qui embarque les rapports de pré-revue antérieurs
(`P4-5E-C-pre-review.md`, `-v2`, `C7`, `C8`).

**Q18 — Passages obsolètes restants dans `ARCHITECTURE.md`** (suite de Q16 ; seule l'arborescence a été traitée
en C9) :

- l.208-238 et l.1056-1085 : `appsettings.json` avec `"AutoMigrate": true`, et `OnConfiguring` avec
  `UseSqlite` fixe. **Le code ne correspond pas** : aucun `appsettings.json` n'existe sous `src/`, la clé
  `AutoMigrate` n'existe nulle part, et `OnConfiguring` choisit le provider par `MMV_DATABASE_PROVIDER` (P4-3).
  `"AutoMigrate": true` peut aussi se lire comme une application automatique des migrations, que P4-6 n'a pas
  encore tranchée pour PostgreSQL ;
- l.1024-1031 : diagramme avec une chaîne unique et une migration fictive (`AddOrders…`).

Ces passages ne sont pas des procédures EF. Ils sortent donc de C9. **Proposition** : un lot documentaire
distinct, après la décision P4-6 sur l'application des migrations PostgreSQL, pour ne pas documenter deux fois.

**Q19 — Lacunes descriptives de `README.md`**, non modifié conformément à la consigne. La structure liste 3 des
5 projets de `src/` : `MMV.Application` et `MMV.Infrastructure.PostgreSQL.Migrations` manquent. Elle liste 1
des 3 projets de tests. Aucun lien vers `CONTRIBUTING.md` (GitHub le présente toutefois, puisqu'il est à la
racine). **Proposition** : traiter dans le même lot que Q18.

**Q11 (C7), toujours ouverte.** Quand le push est-il autorisé ? La CI n'a encore jamais tourné sur le SHA exact
des changements C1 à C9. Ses deux contrôles de dérive restent donc à observer après le push (procédure de revue
de `CONTRIBUTING.md`, contrôle n° 12).

---

## 7. État P4-5E

| Phase | État |
|---|---|
| C1 | **terminé** |
| C4 | **terminé** |
| C7 | **terminé** |
| C8 | **terminé** |
| C9 | **prêt pour validation** |

« Terminé » signifie validé par l'architecte. **Aucune** de ces phases n'est encore commitée (Q17).

Questions de C8 traitées par C9 : **Q12** résolue (`SPRINTS.md`) ; **Q16** traitée en partie (arborescence
d'`ARCHITECTURE.md`), le reste étant renvoyé à Q18 et Q19.

**ADR-005 G6 et ADR-007 S6.** C8 prévoyait leur fermeture formelle en C9, une fois l'emplacement de la règle
référencé par le rapport de clôture. L'emplacement est
[`CONTRIBUTING.md` § Règle de double migration](../../CONTRIBUTING.md#règle-de-double-migration). Il est
référencé par `ARCHITECTURE.md` (l.186, 345, 368), `SPRINTS.md` (l.104) et le `.csproj` PostgreSQL (l.15).
**Proposition** : prononcer la fermeture à la validation de C9. Les ADR eux-mêmes ne sont pas modifiés.

```
P4-5E-C9                     = PRE-REVIEW — C9 DONE LOCALLY — AWAITING VALIDATION
SPRINTS.md                   = §2.4 — 2 DANGEROUS EF COMMANDS REMOVED — REFERENCE TO CONTRIBUTING.md (+3 / -4)
ARCHITECTURE.md              = INFRASTRUCTURE TREE ONLY — SINGLE-CHAIN "Migrations/" ALIGNED (+3 / -4)
README.md                    = ANALYSED — NO EF COMMAND — UNCHANGED
REPO SWEEP                   = NO ACTIVE MMV.App STARTUP / database update INSTRUCTION LEFT
CODE / CSPROJ / SLN / CI     = UNCHANGED (716 FILES, IDENTICAL HASHES)
MIGRATIONS / ADR             = UNCHANGED
TESTS                        = BUILD 0 WARNING 0 ERROR — 1953 / 1953 GREEN, 0 SKIPPED
BLOCKING FOR COMMIT          = Q17 — C1..C8 NOT COMMITTED; ARCHITECTURE.md MIXES C8 + C9 HUNKS
COMMIT / PUSH                = NONE
```

---

## Annexe A — Proposition de commit (non créé)

À créer **après** les commits C1 à C8 (Q17), en indexant exactement ces trois fichiers :

```bash
git add SPRINTS.md ARCHITECTURE.md docs/implementation/P4-5E-C9-pre-review.md
git diff --cached --stat   # attendu : 3 fichiers, ARCHITECTURE.md limité au bloc de l'arborescence
```

Message proposé :

```
docs(P4-5E-C9): align EF migration documentation

- SPRINTS.md §2.4: remove the Sprint 2 EF commands that used
  src/MMV.App as startup project and `dotnet ef database update`;
  point to CONTRIBUTING.md, the official procedure for the SQLite
  and PostgreSQL chains
- ARCHITECTURE.md: the Infrastructure tree now states that
  Migrations/ holds the SQLite chain only and names the PostgreSQL
  migrations project
- README.md: reviewed, no EF command, unchanged
- add the P4-5E-C9 pre-review report

No code, migration, ADR, project, solution or CI change.
Build: 0 warning, 0 error. Tests: 1953 passed, 0 skipped.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```
