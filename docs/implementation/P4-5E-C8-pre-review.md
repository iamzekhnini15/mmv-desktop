# P4-5E-C — C8 : documentation de contribution — PRÉ-REVUE

> **Statut : C8 FAIT LOCALEMENT. PRÉ-REVUE, AUCUN COMMIT, AUCUN PUSH.**
> Reprise après la validation architecte de C7. Périmètre : **C8 uniquement**, soit D-19 à l'emplacement
> retenu par Q6. `CONTRIBUTING.md` est créé à la racine. Dans `ARCHITECTURE.md`, seul le bloc de commandes
> de la section « Migrations EF Core » est remplacé. Aucun autre fichier n'est modifié.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> Ce document **complète** [la pré-revue C7](P4-5E-C7-pre-review.md) et [la pré-revue v2](P4-5E-C-pre-review-v2.md).
> **Le dépôt réel prime sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `EXÉCUTÉ` | commande exécutée sur le dépôt, résultat observé. Toutes en lecture : aucun fichier suivi n'est écrit |
| `MUTATION — HORS DÉPÔT` | exécuté sur une copie jetable, hors du dépôt, supprimée ensuite. **Rien n'en a été reporté** |
| `STATIC_CODE_PROOF` | constaté par lecture du code |
| `EF_BEHAVIOR — NON EXÉCUTÉ` | comportement documenté d'EF Core, **volontairement non exécuté** ici |

---

## 1. Résumé exécutif

- **`CONTRIBUTING.md` créé à la racine** (368 lignes), selon Q6. Il contient la table « quelle chaîne ? », les
  deux factories, les variables, la **règle de double migration**, les commandes des deux chaînes (Bash et
  PowerShell), les interdits, une procédure de revue avant commit en 12 points et une table de dépannage.
  L'intitulé « Règle de double migration » est celui que cite déjà le `.csproj` PostgreSQL.
- **`ARCHITECTURE.md` corrigé** : un seul bloc de diff, `+35 / −18`, dans la section « Migrations EF Core ». Les
  trois commandes `--startup-project src/MMV.App` et `database update` sont **supprimées**, puis remplacées par
  les commandes des deux chaînes. Le reste du fichier est **identique**.
- **Chaque commande documentée a été exécutée.** Les commandes en lecture ont tourné sur le dépôt ; `migrations
  add`, `remove` et les refus croisés, sur une copie jetable. La base `mmv.db` du poste est **inchangée**
  (SHA-256 identique). **L'application n'a jamais été lancée** : le danger `MMV.App` est établi par l'incident
  de la v1 et par R8, sans reproduction (§7).
- **Trois constats faits pendant C8 ont modifié le document** :
  1. sans `--startup-project`, `dotnet ef` prend **le projet du dossier courant** (R8). D'où la règle « toujours
     `--startup-project` », y compris sur SQLite ;
  2. `migrations remove` sur la chaîne PostgreSQL **tente une connexion** et échoue sans `--force` (C6) ;
  3. `migrations remove` **réécrit l'instantané** avec des écarts de forme (`ToTable("X", (string)null)`). D'où
     la recommandation d'annuler par `git` avant commit.
- **Trouvé hors périmètre, non modifié** : [SPRINTS.md:103-104](../../SPRINTS.md#L103-L104) contient encore
  `--startup-project src/MMV.App` et `database update` (Q12).
- **Tests** : 1953 / 1953 verts, 0 ignoré, inchangé. `ci.yml` est identique à C7. Aucun fichier `.cs`.

---

## 2. Décisions appliquées

| Élément exigé | Où dans `CONTRIBUTING.md` | Source |
|---|---|---|
| Règle en 4 étapes : modèle → SQLite → PostgreSQL → deux chaînes testées | *Règle de double migration*, encadré, avec la commande `migrations add` de chaque chaîne | D-19, ADR-005 §5.7 |
| Exception « un seul moteur », avec preuve que l'autre chaîne est inchangée | *Règle de double migration* | D-19, ADR-005 §5.7, ADR-007 §5.6 |
| Table « quelle chaîne ? » : dossier, projet, commande, step CI | *Un modèle, deux chaînes de migrations* | ADR-005 §7.2, D-19 |
| Migration de données écrite pour chaque moteur | *À respecter aussi* | D-19, U-8 |
| Ni retouche de migration générée, ni `migrations remove` après fusion | *À respecter aussi*, *Interdits* n° 3 et 4 | D-03.2, D-03.5 |
| Même nom de migration sur les deux chaînes | *À respecter aussi* ; vérifié par C1 et C2 (§7) | D-19 |
| `OpticDbContextFactory`, `OpticDbContextDesignTimeFactory`, `MMV_DESIGNTIME_DATABASE_PROVIDER` | *Comment `dotnet ef` choisit la chaîne* | D-05, Q1 option A |
| Interdiction de `MMV.App` comme projet de démarrage | *Interdits* n° 1, *Dépannage* | D-04, v1 annexe C |
| Procédure de revue avant commit | *Procédure de revue avant commit* | demande C8 |
| **Q6** : emplacement `CONTRIBUTING.md` à la racine | fichier créé | Q6 |
| **Q6** : correction de `ARCHITECTURE.md` | §6 | Q6 |

**Critère d'emplacement de D-19** : « un contributeur qui modifie une configuration d'entité doit la trouver
sans connaître les ADR ». Le fichier est à la racine, où GitHub le présente. `ARCHITECTURE.md` y renvoie, et le
`.csproj` PostgreSQL le citait déjà. Les identifiants de décision (D-xx) sont regroupés dans *Références* et ne
sont pas nécessaires à la lecture.

**Choix de réalisation à valider**

| Choix | Raison |
|---|---|
| `--startup-project` **explicite aussi sur SQLite** : écart de lettre avec D-19, qui écrit `--project src/MMV.Infrastructure` seul | R8 : sans lui, le projet de démarrage est celui du dossier courant. Depuis `src/MMV.App`, ce serait l'application. La CI n'est pas modifiée : sa forme est équivalente depuis la racine (R3). Voir Q15 |
| forme **PowerShell `try` / `finally`** en plus de la forme Bash | le poste cible est Windows. `$env:` dure toute la session : le `finally` retire la variable, même en cas d'échec (R4b). D-04 ne donnait que la forme Bash |
| annulation avant commit **par `git`** ; `migrations remove` gardé, avec ses précautions | constats C6, D1 et D2 (§7). Voir Q13 |
| **ajouts au-delà de D-19** : adoption des bases historiques SQLite, mise à jour de `MigrationChainsTests`, interdit de `database update`, dépannage | faits vérifiés dans le code ou exécutés, utiles à qui ajoute une migration. Voir Q14 |
| intitulés **non numérotés** | ancre stable `#règle-de-double-migration`, citée par `ARCHITECTURE.md` et nommée par le `.csproj` |
| `ARCHITECTURE.md` : **date d'en-tête inchangée**, note datée sur la section | la section est à jour, le reste du document date de janvier 2026. Changer l'en-tête laisserait croire le contraire |

---

## 3. SHA avant / après

| | Valeur |
|---|---|
| HEAD | `440ddcb352b12e0c6900cfd5d6df550e234856c5`. **Aucun commit.** |
| blob `ARCHITECTURE.md` avant | `b08e0c11e67093639d8f2c31c1cf54dbc26ce512`, identique à `HEAD:ARCHITECTURE.md` |
| blob `ARCHITECTURE.md` après | `3388495ea3955ee2e2f17cadd3fe89b5bef1753f` (`git hash-object`, donc après normalisation LF) |
| SHA-256 `ARCHITECTURE.md`, fichier de travail | avant `85e81334…0e39`, après `022cba42…0eac` |
| blob `CONTRIBUTING.md` | `cd281e7b4db2a77542e797f872162880775448ce` · SHA-256 `796cda1d…7353` |
| `ci.yml` | SHA-256 `3189a7bb…`, **identique** à l'état « après » de C7 |

---

## 4. Fichiers

| Fichier | Nature | Lignes |
|---|---|---|
| [`CONTRIBUTING.md`](../../CONTRIBUTING.md) | **nouveau** | 368 |
| [`ARCHITECTURE.md`](../../ARCHITECTURE.md#L343-L385) | **un** bloc `@@ -342,30 +342,47 @@`, section « Migrations EF Core » | 1072 → 1089 (+35, −18) |
| `docs/implementation/P4-5E-C8-pre-review.md` | ce rapport | — |

`ARCHITECTURE.md` garde sa convention : 1089 fins de ligne CRLF sur 1089, sans BOM.

**Zones interdites intactes** (`EXÉCUTÉ`) : `git diff 440ddcb` est vide sur
`src/MMV.Infrastructure/Migrations/`, `SqliteDatabaseManager.cs`, `App.axaml.cs`, `OpticDbContext.cs`,
`Data/Configurations/`, `docs/architecture/` (ADR compris), `SPRINTS.md` et `README.md`. `ci.yml` est identique
à C7. Aucun fichier `.cs`, aucune migration, aucun ADR, aucun workflow. `git status` est celui du début de
session, plus les deux documents.

**Hors dépôt, non versionné** : trois scripts de vérification (`c8_readonly.ps1`, `c8_copy.ps1`,
`c8_remove.ps1`) et les scripts SQL produits, dans le dossier de travail temporaire. La copie jetable et la
copie de `mmv.db` sont **supprimées**.

---

## 5. Contenu de `CONTRIBUTING.md`

| Section | Contenu | Preuve |
|---|---|---|
| *Prérequis* | SDK fixé par `global.json` ; `dotnet-ef 8.0.27` par `dotnet tool restore` ; build avant `--no-build` | `EXÉCUTÉ` : `dotnet ef --version` = `8.0.27` |
| *Un modèle, deux chaînes* | pourquoi deux chaînes ; table « quelle chaîne ? » ; règles de structure (aucune migration PostgreSQL dans `MMV.Infrastructure`, aucune référence inverse) | R1, R4 ; D-01 |
| *Comment `dotnet ef` choisit la chaîne* | les 4 cas de la variable ; chaîne factice `.invalid` ; étanchéité runtime / design-time ; rôle et limites de la factory de délégation ; table des variables ; forme PowerShell et forme Bash | `STATIC_CODE_PROOF` sur les deux factories ; R4b |
| *Les garde-fous, et le piège qui reste* | 3 refus d'EF, puis le faux vert, avec son message exact | C3a, C3b, C7 T4 ; v2 P4 et P5 |
| *Règle de double migration* | D-19 intégral, commandes `add` comprises ; exception ; même nom ; données ; figement ; tests d'énumération ; bases historiques SQLite | C1, C2 ; `STATIC_CODE_PROOF` |
| *Commandes EF de référence* | `add`, `list --no-connect`, `has-pending-model-changes`, `script <Précédente> <Nom>`, sur chaque chaîne ; annulation | R1 à R7, C1 à D2 |
| *Interdits* | 9 interdits, chacun avec sa raison | v1 annexe C ; D-01, D-03, D-05 ; C7 §6.3 |
| *Procédure de revue avant commit* | 12 contrôles, avec la commande de chacun | — |
| *Dépannage* | 8 symptômes avec leur message réel, leur cause et leur correction | messages copiés des sorties de §7 |

**Adoption des bases historiques SQLite** (`STATIC_CODE_PROOF`). Une base sans `__EFMigrationsHistory` est
adoptée par `SqliteDatabaseManager`, qui inscrit comme appliquées les migrations précédant la première dont il
détecte l'absence ([SqliteDatabaseManager.cs:585](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L585)).
Les six migrations additives depuis `AddDocumentSequences` figurent toutes dans les listes d'éléments tolérés de
[SqliteSchemaVerifier.cs:44-98](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs#L44-L98). Cinq d'entre
elles ont aussi une détection par suffixe ([SqliteDatabaseManager.cs:671-737](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L671-L737)) ;
`AddProductNormalizedReferenceAndProtectHistory` n'en a pas. Le document demande d'**évaluer et de signaler**
l'effet d'une migration sur ce mécanisme. Il ne prescrit aucune modification de ces deux fichiers.

---

## 6. Correction de `ARCHITECTURE.md`

| Avant | Après | Raison |
|---|---|---|
| `migrations add … --startup-project src/MMV.App` | `migrations add` sur chaque chaîne, avec `--project` et `--startup-project` sur le même projet | `MMV.App` comme projet de démarrage **exécute l'application** (v1 annexe C) et ne peut pas viser la chaîne PostgreSQL (v1 S1b) |
| `database update … --startup-project src/MMV.App` | **supprimé**, remplacé par une phrase qui dit pourquoi | lance l'application, et contournerait `SqliteDatabaseManager` sur SQLite ; sur PostgreSQL, voir D-05.7 et P4-6 |
| `migrations remove … --startup-project src/MMV.App` | **supprimé** ; la procédure d'annulation est dans `CONTRIBUTING.md` | lance l'application ; précautions propres à chaque chaîne (§7) |
| `migrations script --project src/MMV.Infrastructure --output migration.sql` | **supprimé** ; le script de revue est dans `CONTRIBUTING.md` | il ne visait qu'une chaîne et écrivait dans le dépôt |
| — | table des deux chaînes ; `has-pending-model-changes` sur chacune ; `list --no-connect` PostgreSQL | commandes compatibles avec l'architecture actuelle ; exécutées telles quelles (A1 à A3) |

**Observé dans `ARCHITECTURE.md`, hors périmètre, non corrigé** (Q16) : l'arborescence de `MMV.Infrastructure`
ne liste que `InitialCreate` sous `Migrations/`, alors qu'il y a 14 migrations. Le projet
`MMV.Infrastructure.PostgreSQL.Migrations` n'y figure pas. L'en-tête date du 27 janvier 2026. Le bloc
« Migration Initiale (Sprint 2) » est illustratif et ne contient pas de commande. Le `README.md` ne liste pas non
plus le projet PostgreSQL.

---

## 7. Vérification des commandes documentées

### 7.1 Environnement

Le `dotnet` placé en tête du `PATH` de ce poste (`C:\Program Files (x86)\dotnet`) n'a **aucun SDK**. Les
commandes ont donc utilisé `C:\Program Files\dotnet` (SDK 8.0.425, accepté par `global.json` en
`latestFeature`), en le préfixant au `PATH` de chaque commande. Rien n'a été installé ni modifié sur le poste.
Au préalable : `dotnet tool restore` (`dotnet-ef` 8.0.27) et `dotnet build MMV.sln -c Debug` (0 avertissement,
0 erreur). Aucune variable `MMV_*` n'était posée dans l'environnement du poste.

### 7.2 Sur le dépôt, en lecture (`EXÉCUTÉ`)

| # | Commande, sous sa forme documentée | Code | Résultat |
|---|---|---|---|
| R1 | SQLite `migrations list --no-connect`, `--startup-project` explicite | 0 | **14** migrations, de `20260127184542_InitialCreate` à `20260721134634_AddNormalizedUsernameAndSecureLocalUsers` |
| R2 | SQLite `has-pending-model-changes`, `--startup-project` explicite | 0 | `No changes have been made to the model since the last migration.` |
| R3 | SQLite `has-pending-model-changes`, forme CI (sans `--startup-project`) | 0 | idem R2 : les deux formes sont **équivalentes depuis la racine** |
| R4 | PostgreSQL `migrations list --no-connect`, forme PowerShell `try` / `finally` | 0 | `20260922001219_InitialPostgreSqlBaseline` **seule** |
| R4b | variable après le `finally` | — | **absente** |
| R5 | PostgreSQL `has-pending-model-changes` | 0 | `No changes have been made…` |
| R6 | PostgreSQL `migrations script`, complet | 0 | 391 lignes, SHA-256 `04c36a9e…9511`, **identique** à la v2 §6.1 : le script de revue est reproductible |
| R7 | SQLite `migrations script`, complet | 0 | 2141 lignes, aucune connexion. Les avertissements EF sur les reconstructions de table des migrations historiques sont préexistants |
| R8 | depuis `src/MMV.Domain`, `--project ../MMV.Infrastructure` **sans** `--startup-project` | 1 | `Using startup project '…\src\MMV.Domain\MMV.Domain.csproj'`, puis `Your startup project 'MMV.Domain' doesn't reference Microsoft.EntityFrameworkCore.Design` |
| A1 | `ARCHITECTURE.md`, SQLite `has-pending-model-changes` (Git Bash, texte copié) | 0 | vert |
| A2 | `ARCHITECTURE.md`, PostgreSQL `list --no-connect`, forme Bash en préfixe | 0 | baseline seule |
| A3 | `ARCHITECTURE.md`, PostgreSQL `has-pending-model-changes`, forme Bash en préfixe | 0 | vert |
| A4 | `CONTRIBUTING.md`, bloc Bash avec `$PG` | 0 | baseline seule ; variable **absente** du shell ensuite |

**R8 établit le mécanisme sans lancer l'application.** `MMV.Domain` est une bibliothèque sans point d'entrée :
EF s'arrête sur l'absence du paquet Design. Depuis `src/MMV.App`, le même mécanisme retiendrait `MMV.App`, qui
référence ce paquet, et EF exécuterait son point d'entrée. C'est l'incident de la v1 (annexe C). **Il n'a pas été
reproduit, volontairement.**

### 7.3 Sur une copie jetable (`MUTATION — HORS DÉPÔT`)

Copie de `src/` (sans `bin/` ni `obj/`), `.config/`, `global.json` et `Directory.Build.props` dans le dossier
de travail temporaire. `MMV_DATABASE_PATH` y désigne un fichier jetable : la branche SQLite de la factory ne
peut pas atteindre la base du poste.

| # | Commande | Code | Résultat |
|---|---|---|---|
| C1 | SQLite `migrations add C8Probe`, sans variable | 0 | fichiers générés dans `src/MMV.Infrastructure/Migrations/` ; aucune base créée |
| C2 | PostgreSQL `migrations add C8Probe`, **même nom** | 0 | fichiers générés dans le dossier PostgreSQL. Les deux chaînes compilent ensemble (build de C4) |
| C3a | variable posée, `migrations add` visant `src/MMV.Infrastructure` | 1 | `Could not load file or assembly 'MMV.Infrastructure.PostgreSQL.Migrations'` |
| C3b | variable absente, `migrations add` visant le projet PostgreSQL | 1 | `Your target project 'MMV.Infrastructure.PostgreSQL.Migrations' doesn't match your migrations assembly 'MMV.Infrastructure'…` |
| C4 | PostgreSQL `migrations script InitialPostgreSqlBaseline C8Probe` | 0 | seule la ligne d'historique de `C8Probe` : la forme `<Précédente> <Nom>` exclut bien la première migration |
| C5 | PostgreSQL `has-pending-model-changes` après C2 | 0 | vert |
| C6 | PostgreSQL `migrations remove`, **sans** `--force` | **1** | pile `HistoryRepository.GetAppliedMigrations` → `NpgsqlDatabaseCreator.Exists` → `Hôte inconnu.` |
| D1 | PostgreSQL `migrations remove --force` | 0 | `Unable to check if the migration '…_C8Probe' has been applied to the database… Hôte inconnu.`, puis `Removing migration…`, `Reverting the model snapshot.` Fichiers retirés |
| C7 | SQLite `migrations remove`, base design-time **absente** | 0 | fichiers retirés ; **aucune base créée** |
| D2 | SQLite `add C8Probe2` puis `remove`, base design-time **présente** (copie de `mmv.db`) | 0 | EF ouvre la base (`Disposing connection to database 'main' on server '…probe.db'`) ; empreinte **inchangée** ; fichiers retirés |
| — | instantanés de la copie après `add` puis `remove` | — | **différents** du dépôt sur les deux chaînes : `b.ToTable("Customers")` devient `b.ToTable("Customers", (string)null)`, et de même pour les autres tables |

**Non exécuté, volontairement**

| Point | Statut | Traitement dans le document |
|---|---|---|
| `migrations remove --force` sur SQLite, migration appliquée | `EF_BEHAVIOR — NON EXÉCUTÉ` : EF documente qu'il défait alors la migration dans la base | interdit : « N'utilisez jamais `--force` sur SQLite » |
| `database update`, `database drop` | non exécutés (D-05.7) | interdits n° 2 |
| toute commande depuis `src/MMV.App` ou avec `--startup-project src/MMV.App` | non exécuté : lancerait l'application | interdit n° 1 ; mécanisme établi par R8 |
| script SQLite incrémental `<Précédente> <Nom>` | non exécuté ; même commande EF que C4, et script SQLite complet exécuté (R7) | documenté |

### 7.4 Aucun effet de bord sur le poste (`EXÉCUTÉ`)

| | Avant | Après |
|---|---|---|
| `%LOCALAPPDATA%\ManageMyVision\mmv.db` | 258 048 octets, `2026-09-21 23:42:47Z`, SHA-256 `28430b85…96f4` | **identique** |
| `migration-journal.log` | 892 octets, `2026-09-21 23:38:09Z` | **identique** |

---

## 8. Tests (`EXÉCUTÉ`)

| Commande | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | **0 avertissement, 0 erreur** |
| `dotnet test MMV.sln --no-build -c Debug` | **1953 réussis, 0 échec, 0 ignoré**. Détail : App 260, Application 628, Domain 1065 |

Le compte est inchangé depuis la v2. C8 ne modifie aucun fichier compilé et n'ajoute aucun test.

---

## 9. Validation des décisions (delta C7 → C8)

| Décision ou critère | C7 | **C8** | Preuve |
|---|---|---|---|
| **D-19** | NON COMMENCÉ | **DOCUMENTÉ**, en pré-revue | §2, §5 |
| **Q6** | ouverte | **APPLIQUÉE** : `CONTRIBUTING.md` à la racine ; `ARCHITECTURE.md` corrigé | §4, §6 |
| **ADR-005 G6**, **ADR-007 S6** | ouverts | **documentés** ; fermeture formelle en C9, quand le rapport de clôture référencera l'emplacement | — |
| **ADR-005 §7.2** (« quelle chaîne ? ») | — | **FAIT** | table de *Un modèle, deux chaînes* |
| Clôture n°5 : règle documentée et référencée | non | **documentée** ; référencée par `ARCHITECTURE.md` et par le `.csproj` PostgreSQL | §2 |
| **D-05.6**, **D-06.6** | CONFORME | **CONFORME** : `ci.yml` identique | §3 |
| C8 : « document relu » | — | **en attente de la revue architecte** | — |

---

## 10. Limitations

1. **Le danger `MMV.App` n'est pas reproduit** en C8, volontairement. Il repose sur l'incident de la v1 et sur le
   mécanisme prouvé par R8.
2. **`--force` sur SQLite** : comportement documenté par EF, non exécuté. Le document l'interdit.
3. Les formes PowerShell ont tourné sous **Windows PowerShell 5.1** seulement. C7 couvrait 7.4 et 7.6, mais pour
   le script CI, pas pour ces blocs. La forme Bash a tourné sous **Git Bash** seulement.
4. Le message `Hôte inconnu` est celui d'un poste en français. Son équivalent anglais n'a pas été observé ; le
   document le signale.
5. Le script SQLite incrémental n'a pas été exécuté (§7.3).
6. **Inchangé depuis C7** : la CI n'a jamais tourné sur le SHA exact (Q11).
7. `ARCHITECTURE.md` garde des passages anciens hors du bloc corrigé (§6, Q16).

---

## 11. Questions ouvertes

**Q12 — `SPRINTS.md` §2.4.** Les lignes [103-104](../../SPRINTS.md#L103-L104) contiennent les deux commandes
dangereuses (`migrations add … --startup-project src/MMV.App`, `database update … --startup-project
src/MMV.App`). Le fichier est hors du périmètre autorisé pour C8. Un balayage complet du dépôt ne trouve **aucune
autre** consigne active de ce type : les autres occurrences sont dans des rapports historiques ou dans des
documents qui les interdisent. **Proposition** : remplacer ces deux lignes par un renvoi à `CONTRIBUTING.md`, dans
C8 ou en C9. Autorisation nécessaire.

**Q13 — Annulation d'une migration.** Faut-il valider (a) l'annulation par `git` comme méthode recommandée avant
commit, et (b) `migrations remove --force` pour la chaîne PostgreSQL avant fusion ? D-05.7 exclut de la procédure
les commandes qui se connectent. `remove` **tente** une connexion, qui ne peut jamais aboutir : l'hôte est
`.invalid`. **Proposition** : garder (a) et (b) tels que documentés.

**Q14 — Ajouts au-delà de la lettre de D-19.** Adoption des bases historiques SQLite, mise à jour de
`MigrationChainsTests`, interdit de `database update`, table de dépannage. **Proposition** : les garder. Chacun
est vérifié (§5, §7) et sert directement à qui ajoute une migration.

**Q15 — `--startup-project` explicite sur SQLite.** C'est un écart de lettre avec D-19, étape 2. La CI garde sa
forme, équivalente depuis la racine (R3). **Proposition** : valider l'écart.

**Q16 — Autres passages anciens de `ARCHITECTURE.md` et du `README.md`** (§6). Faut-il les corriger en C9, dans
un lot distinct, ou les laisser ?

**Q11 (C7), toujours ouverte** : quand le push est-il autorisé, et faut-il enchaîner C9 ensuite, toujours en
pré-revue sans commit ?

```
P4-5E-C8                     = PRE-REVIEW — C8 DONE LOCALLY — AWAITING VALIDATION
CONTRIBUTING.md              = CREATED AT ROOT (368 LINES) — Q6 LOCATION APPLIED
D-19 DOUBLE MIGRATION RULE   = DOCUMENTED — SECTION "Règle de double migration" (NAMED BY PG .csproj)
ARCHITECTURE.md              = ONE HUNK IN "Migrations EF Core" (+35 / -18) — MMV.App COMMANDS AND database update REMOVED
DOCUMENTED COMMANDS          = ALL EXECUTED — READ-ONLY ON REPO / add, remove, CROSS-GUARDS ON THROWAWAY COPY
MMV.App HAZARD               = NOT REPRODUCED BY DESIGN — MECHANISM PROVEN VIA cwd DEFAULT (R8)
NEW FINDINGS                 = cwd STARTUP DEFAULT · PG remove NEEDS --force · remove REWRITES SNAPSHOT
OUT OF SCOPE FOUND           = SPRINTS.md §2.4 STILL HAS MMV.App COMMANDS (Q12) — NOT MODIFIED
CI FILE                      = UNCHANGED SINCE C7
CODE / MIGRATIONS / ADR      = NONE TOUCHED
TESTS                        = 1953 / 1953 GREEN, 0 SKIPPED (UNCHANGED)
LOCAL mmv.db                 = UNCHANGED (SHA-256 IDENTICAL)
C9 CLOSURE                   = NOT STARTED
COMMIT / PUSH                = NONE
```

---

## Annexe A — Diff de `ARCHITECTURE.md`

```diff
@@ -342,30 +342,47 @@ CREATE INDEX idx_prescriptions_issue_date ON prescriptions(issue_date DESC);
 
 ### Migrations EF Core
 
+> **Section mise à jour en P4-5E-C8 (22 septembre 2026).** La procédure complète, les interdits et la revue
+> avant commit sont dans [CONTRIBUTING.md](CONTRIBUTING.md#règle-de-double-migration), qui fait référence.
+
+Le modèle EF est unique. Les migrations forment **deux chaînes**, une par moteur, qui ne se mélangent jamais :
+
+| Chaîne | Dossier des migrations | `--project` et `--startup-project` | Variable design-time |
+|---|---|---|---|
+| SQLite | `src/MMV.Infrastructure/Migrations/` | `src/MMV.Infrastructure` | aucune |
+| PostgreSQL | `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/` | `src/MMV.Infrastructure.PostgreSQL.Migrations` | `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` |
+
+`src/MMV.App` n'est **jamais** le projet de démarrage de `dotnet ef` : EF exécuterait l'application, ce qui
+ouvre la fenêtre et prépare réellement la base locale.
+
 #### Commandes Principales
 
+Depuis la racine du dépôt, après `dotnet tool restore` (dotnet-ef 8.0.27) et `dotnet build MMV.sln -c Debug` :
+
 ```bash
-# Ajouter une nouvelle migration
+# Chaîne SQLite : sans variable
 dotnet ef migrations add NomMigration \
-  --project src/MMV.Infrastructure \
-  --startup-project src/MMV.App
-
-# Appliquer les migrations
-dotnet ef database update \
-  --project src/MMV.Infrastructure \
-  --startup-project src/MMV.App
-
-# Annuler la dernière migration
-dotnet ef migrations remove \
-  --project src/MMV.Infrastructure \
-  --startup-project src/MMV.App
-
-# Générer un script SQL
-dotnet ef migrations script \
-  --project src/MMV.Infrastructure \
-  --output migration.sql
+  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure
+dotnet ef migrations has-pending-model-changes \
+  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build
+
+# Chaîne PostgreSQL : variable limitée à la commande (forme PowerShell dans CONTRIBUTING.md)
+MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations add NomMigration \
+  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
+  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
+MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations list --no-connect \
+  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
+  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations --no-build
+MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations has-pending-model-changes \
+  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
+  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations --no-build
 ```
 
+Aucune de ces commandes ne se connecte à une base. `dotnet ef database update` n'est pas utilisé. La base
+SQLite locale est préparée par l'application au démarrage (`SqliteDatabaseManager`). L'application des
+migrations PostgreSQL n'est pas encore décidée (P4-6). Toute évolution du modèle exige une migration sur
+**chaque** chaîne, dans le même commit.
+
 #### Migration Initiale (Sprint 2)
```

## Annexe B — Plan de `CONTRIBUTING.md`

```
Contribuer à ManageMyVision
├── Prérequis
├── Un modèle, deux chaînes de migrations            (table « quelle chaîne ? »)
├── Comment `dotnet ef` choisit la chaîne
│   ├── OpticDbContextFactory : la seule logique design-time
│   ├── OpticDbContextDesignTimeFactory : le point d'entrée de la chaîne PostgreSQL
│   ├── Les variables d'environnement               (formes PowerShell et Bash)
│   └── Les garde-fous, et le piège qui reste       (faux vert)
├── Règle de double migration                       (D-19 — intitulé nommé par le .csproj PostgreSQL)
├── Commandes EF de référence
│   ├── Chaîne SQLite (sans variable)
│   ├── Chaîne PostgreSQL (variable postgresql)
│   └── Annuler une migration pas encore fusionnée
├── Interdits                                       (9)
├── Procédure de revue avant commit                 (12 contrôles)
├── Dépannage                                       (8 symptômes)
└── Références
```
