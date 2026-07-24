# Rapport P4-1 — Spike comparatif PostgreSQL / SQL Server Express

> **MODE** : `EXECUTE_PROVIDER_COMPARISON_SPIKE_AND_WRITE_REPORT_NO_COMMIT`
> **Aucun provider n'est choisi.** Aucun score global n'est attribué.
> **Aucune** modification de `src/**`, `tests/**`, `Migrations/**`, UI, `MMV.sln` ou workflow CI.
> **Le dépôt réel et les sorties de commandes priment toujours sur ce document.**

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Date d'exécution | 2026-07-23 → 2026-07-24 |
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| HEAD local et distant | `1f54f3613119adcf3f46f38a61071a79f4b27482` ✅ |
| `origin/main` | `3ed883634dae83399d3e93dbc1dc65ffaabdf243` |
| CI de référence | run `30046062019` ✅ |
| Tests attendus | 1499 (Domain 649 · Application 611 · App 239) ✅ |
| Phase | P4-1 — Spike comparatif, aucun choix de provider |

---

## 2. Baseline vérifiée

### 2.1 Portes Git

```
git branch --show-current        → p4-multi-poste                                  ✅
git rev-parse HEAD               → 1f54f3613119adcf3f46f38a61071a79f4b27482        ✅
git rev-parse origin/p4-multi-poste → 1f54f3613119adcf3f46f38a61071a79f4b27482     ✅
git rev-parse origin/main        → 3ed883634dae83399d3e93dbc1dc65ffaabdf243        ✅
git status --short               → ?? design-handoff/  ?? design/  ?? docs/ui/      ✅
git diff --check                 → propre                                          ✅
```

Aucun fichier suivi modifié au démarrage. Seuls les trois dossiers non suivis autorisés étaient présents.

### 2.2 Porte CI — run `30046062019`

```
headSha    = 1f54f3613119adcf3f46f38a61071a79f4b27482   ✅
headBranch = p4-multi-poste   event = push                ✅
status     = completed        conclusion = success        ✅
Job « Restore / Build / Test / Scan » : 13 étapes, toutes success ✅
```

### 2.3 Baseline locale

| Contrôle | Résultat |
|---|---|
| `dotnet restore MMV.sln` | up-to-date ✅ |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 Warning · 0 Error** ✅ |
| `dotnet test MMV.sln --no-build -c Debug` | **1499** — Domain **649** · Application **611** · App **239** ; 0 échec ; 0 ignoré ✅ |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune vulnérabilité** (7 projets) ✅ |
| `dotnet ef migrations has-pending-model-changes` | *No changes have been made to the model since the last migration* (exit 0) ✅ |

**Baseline conforme.** Le spike a démarré sur un dépôt sain.

---

## 3. Protocole

1. Vérifier les portes Git et CI ; refuser de continuer en cas d'écart.
2. Reproduire la baseline locale (build, tests, audit, modèle EF).
3. Inventorier l'environnement hôte **sans rien installer ni modifier**.
4. Classer chaque provider (`READY_FOR_FULL_SPIKE` / `ENGINE_ONLY` / `NOT_AVAILABLE` / `UNSAFE_TO_USE`).
5. **Demander une autorisation explicite** avant toute installation ou toute utilisation d'une instance existante.
6. Provisionner des instances **jetables** sans donnée utilisateur, exposées sur **localhost uniquement**.
7. Construire un harness **isolé**, hors `MMV.sln`, exerçant le **vrai** `OpticDbContext`.
8. Exécuter E1 → E10 sur les deux providers, en consignant les **faits** (pas des jugements).
9. Vérifier les faits externes dans les **sources officielles** uniquement.
10. Contrôler la non-régression du dépôt et le périmètre Git.

Toute mesure contredisant une attente a été **conservée telle quelle** et signalée.

---

## 4. Sources officielles consultées

| Organisme | Titre | URL | Consulté le | Fait soutenu | Version concernée |
|---|---|---|---|---|---|
| Microsoft Learn | Editions and Supported Features of SQL Server 2022 | https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022 | 2026-07-24 | Limites d'échelle Express, fonctionnalités par édition | SQL Server 2022 (16.x) |
| PostgreSQL Global Development Group | Versioning Policy | https://www.postgresql.org/support/versioning/ | 2026-07-24 | Versions supportées, dates de fin de vie, politique 5 ans | PostgreSQL 14 → 18 |
| PostgreSQL Global Development Group | License | https://www.postgresql.org/about/licence/ | 2026-07-24 | Licence PostgreSQL, usage commercial sans redevance | Toutes |
| Npgsql | Entity Framework Core Provider | https://www.npgsql.org/efcore/ | 2026-07-24 | Correspondance provider ↔ EF Core 8 | Npgsql EF Core 8.x |

**Aucune valeur externe n'a été affirmée de mémoire.** Les faits mesurés localement (versions serveur, éditions,
isolation) proviennent de **requêtes serveur**, pas de la documentation.

### 4.1 Faits officiels retenus

**SQL Server 2022 — Express (Microsoft Learn, table « Scale limits ») :**

| Limite | Express |
|---|---|
| Capacité de calcul max. par instance | **le moindre de 1 socket ou 4 cœurs** |
| Mémoire max. du buffer pool par instance | **1 410 Mo** |
| Taille max. d'une base relationnelle | **10 Go** |
| Édition | « entry-level, **free** database » |

Fonctionnalités **absentes** d'Express, pertinentes pour l'exploitation en magasin (même table, sections
« Manageability » / « High availability ») :

- **SQL Server Agent : Non** → aucune planification native de sauvegarde ;
- **Backup compression : Non** ; **Encrypted backup : Non** ; **Log shipping : Non** ;
- **Database mail : Non** ; **Performance data collector : Non** ; **Standard performance reports : Non** ;
- **SQL Profiler : Non** (profilable depuis une édition Standard/Enterprise).

Présents en Express : `MERGE` et upsert, index filtrés, Query Store, snapshot de base, contained databases,
row-level security, SSMS, `sqlcmd`, SQL Server Configuration Manager.

**PostgreSQL (postgresql.org) :**

| Version | Dernier mineur | Fin de vie |
|---|---|---|
| 18 | 18.4 | 2030-11-14 |
| **17** | **17.10** | **2029-11-08** |
| 16 | 16.14 | 2028-11-09 |
| 15 | 15.18 | 2027-11-11 |
| 14 | 14.23 | 2026-11-12 |

Politique : **5 ans** de support par version majeure ; une majeure par an environ.
Licence **PostgreSQL License** (type BSD/MIT) : « Permission to use, copy, modify, and distribute this software
and its documentation for any purpose, **without fee**, and **without a written agreement** is hereby granted ».
**Aucune redevance, y compris en usage commercial.** Aucune limite de taille de base, de RAM ni de CPU imposée
par la licence.

> **Écart de limites, factuel** : la contrainte **10 Go / base** et **1 410 Mo de buffer pool** d'Express n'a
> **aucun équivalent** côté PostgreSQL. Ce fait n'est **pas** converti ici en recommandation : la volumétrie
> réelle de MMV n'a **pas** été mesurée pendant P4-1.

---

## 5. Environnement hôte

| Élément | Valeur relevée |
|---|---|
| OS | Windows 11 Home 10.0.26200, x64 |
| SDK .NET | **8.0.417** (verrouillé par `global.json`, `rollForward: latestFeature`) ; 10.0.102 / 10.0.103 également présents |
| Runtimes .NET | 8.0.23, 10.0.2, 10.0.3 (`Microsoft.NETCore.App`, `WindowsDesktop.App`, `AspNetCore.App`) |
| Docker | **29.4.1** ; Docker Compose **v5.1.3** ; Docker Desktop installé, **daemon initialement arrêté** |
| WSL | présent, distribution par défaut **Ubuntu**, version 2 |
| `psql` / `pg_dump` / `pg_restore` | **absents du PATH** ; binaires présents dans `C:\Program Files\PostgreSQL\17\bin` (`psql 17.4`) |
| `sqlcmd` | présent — `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE`, version 15.0.1300.359 |
| Services base de données | `postgresql-x64-17` **Running/Automatic** ; `SQLWriter` Running/Automatic |
| Instances SQL Server complètes | **aucune** (`HKLM\...\Instance Names\SQL` absent) |
| LocalDB | `MSSQLLocalDB` — automatique, **non créée** avant le spike |
| Ports en écoute | **5432** (PostgreSQL natif, `0.0.0.0` et `::`) ; 1433 **non** en écoute |
| Espace disque libre | **~17 Go** sur `C:` au démarrage |
| Session | **NON élevée** (`IsInRole(Administrator) = False`) |

### 5.1 Instance PostgreSQL native — NON UTILISÉE

Une instance PostgreSQL **17.4** native tourne sur le poste (port 5432, `pg_hba.conf` en `scram-sha-256`).

Elle **n'a pas été utilisée** :

- aucune donnée d'authentification n'était disponible (aucune variable d'environnement, aucun `PGPASSWORD`) ;
- son contenu n'a **pas** pu être inspecté, donc la présence de données utilisateur n'a pas pu être exclue ;
- l'utilisateur a explicitement interdit de s'y connecter ou de l'inspecter.

**Aucune connexion n'a été tentée vers cette instance.** Aucun service existant n'a été modifié.

### 5.2 LocalDB — créée par une sonde, non retenue comme preuve

Une requête `sqlcmd -S (localdb)\MSSQLLocalDB` a **créé automatiquement** l'instance automatique LocalDB
(elle était annoncée « not created »). Relevé par requête serveur :

```
Edition = Express Edition (64-bit) | EngineEdition = 4 | ProductVersion = 17.0.925.4 | ProductLevel = RC1
```

LocalDB est donc bien le **moteur Express**, mais **n'écoute aucun port TCP** et s'exécute en mode utilisateur.
Conformément à la consigne, **LocalDB n'est utilisée comme preuve d'aucune capacité multi-poste**. Effet de bord
à nettoyer : instance LocalDB automatique créée (voir §27.3).

---

## 6. Disponibilité PostgreSQL

**Classement : `READY_FOR_FULL_SPIKE`** (après autorisation, via conteneur jetable).

| Élément | Valeur |
|---|---|
| Mode d'obtention | Conteneur Docker `postgres:17`, **autorisé explicitement** par l'utilisateur |
| Version relevée **par requête serveur** | `PostgreSQL 17.10 (Debian 17.10-1.pgdg13+1) on x86_64-pc-linux-gnu, compiled by gcc 14.2.0, 64-bit` |
| Exposition | **`127.0.0.1:5433` uniquement** (jamais sur le réseau externe) |
| Bases présentes à la création | `postgres`, `template0`, `template1` — **aucune donnée utilisateur** ✅ |
| Rôle dédié | `mmv_spike` (LOGIN, CREATEDB), mot de passe généré aléatoirement |
| Base jetable | `mmv_p4_spike` + une base par expérimentation, supprimée en fin de test |
| Isolation par défaut relevée | `read committed` |
| Connexion | **TCP réelle** depuis l'hôte Windows vers le conteneur |

---

## 7. Disponibilité SQL Server Express

**Classement : `READY_FOR_FULL_SPIKE` pour le moteur et l'édition ; `NOT_TESTED` pour l'installation Windows native**
(voir §19 et §26).

| Élément | Valeur |
|---|---|
| Mode d'obtention | Conteneur `mcr.microsoft.com/mssql/server:2022-latest` avec **`MSSQL_PID=Express`** |
| Édition relevée **par requête serveur** | **`Express Edition (64-bit)`** — `EngineEdition = 4` |
| Version relevée | `16.0.4265.3` — `Microsoft SQL Server 2022 (RTM-CU26) (KB5093420)`, `ProductLevel = RTM` |
| Système hôte du moteur | **Linux (Ubuntu 22.04.5 LTS)** — *pas* Windows |
| Exposition | **`127.0.0.1:14333` uniquement** |
| Bases présentes à la création | `master`, `model`, `msdb`, `tempdb` — **aucune donnée utilisateur** ✅ |
| Base jetable | `mmv_p4_spike` + une base par expérimentation |
| Isolation par défaut relevée | `ReadCommitted` |

> **Portée exacte de cette preuve.** L'édition **Express** est vérifiée par requête serveur : les résultats E1→E10
> valent donc pour le **moteur Express** et pour ses **limites d'édition**. Ils **ne valent pas** comme preuve de
> l'**installation Windows native** d'Express, de son service Windows, de son pare-feu, de son SQL Server
> Configuration Manager ni de son exploitation en magasin. Cette distinction est maintenue partout dans ce rapport.

### 7.1 Artefacts d'environnement rencontrés (non imputables aux providers)

Deux blocages ont été diagnostiqués puis contournés ; ils relèvent de **Docker Desktop sous Windows**, pas de
SQL Server :

1. **`localhost` vs `127.0.0.1`** — le conteneur est publié en **IPv4 seulement**, or `localhost` résout d'abord
   `::1`. Toute connexion `Server=localhost,14333` restait sans réponse jusqu'au timeout. **Correctif : adresse
   IPv4 littérale.** Le port répondait pourtant à `Test-NetConnection` (proxy Docker), ce qui rend le symptôme
   trompeur.
2. **Latence du premier handshake TDS** à travers le NAT Docker : `Connect Timeout` relevé à 60 s.

Ces deux points sont **des artefacts de la méthode de provisionnement** et ne doivent pas être portés au débit de
SQL Server Express dans une décision d'ADR.

---

## 8. Versions réellement testées

| Provider | Version/édition relevée par requête serveur | Hôte du moteur |
|---|---|---|
| PostgreSQL | **17.10** (Debian build) | Linux (conteneur) |
| SQL Server | **16.0.4265.3**, SQL Server 2022 RTM-CU26, **Express Edition (64-bit)**, `EngineEdition=4` | Linux (conteneur) |
| *(non retenu comme preuve)* LocalDB | 17.0.925.4 RC1, Express Edition (64-bit), `EngineEdition=4` | Windows (mode utilisateur, sans TCP) |
| *(non utilisé)* PostgreSQL natif | 17.4 (client `psql` relevé ; serveur non interrogé) | Windows |

---

## 9. Harness

Emplacement : **`spikes/P4.ProviderComparison/`** — **hors de `MMV.sln`**, jamais ajouté à la solution.

| Fichier | Rôle |
|---|---|
| `MMV.P4.ProviderComparison.csproj` | Projet de test isolé (net8.0) |
| `Support/SpikeEnvironment.cs` | Lecture des chaînes **depuis l'environnement uniquement** + description non sensible |
| `Support/SpikeDatabase.cs` | Création/suppression de bases **jetables**, contexte réel, SQL natif |
| `Support/AdaptedOpticDbContext.cs` | `OpticDbContext` réel + adaptations **minimales** mesurées |
| `Support/SpikeModelCacheKeyFactory.cs` | Clé de cache EF par provider + variante (voir §9.2) |
| `Support/ErrorFacts.cs` | Extraction des faits d'erreur provider, sans secret |
| `Support/SpikeLog.cs` | Journal de preuves **hors dépôt** |
| `E1_ModelCreationTests.cs` … `E10_BackupRestoreTests.cs` | Expérimentations |
| `Worker/MMV.P4.Worker.csproj` + `Worker/Program.cs` | **Processus indépendant** pour E9 |

### 9.1 Packages du harness (verrouillés)

| Package | Version | Justification |
|---|---|---|
| `xunit` | 2.9.3 | aligné sur les projets de test du dépôt |
| `xunit.runner.visualstudio` | 2.8.2 | idem |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | idem |
| `Xunit.SkippableFact` | 1.4.13 | **SKIPPED explicite** quand un provider n'est pas configuré |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **8.0.11** | dernier 8.0.x — la majeure suit EF Core 8 |
| `Microsoft.EntityFrameworkCore.SqlServer` | **8.0.27** | **exactement** la version EF Core du dépôt |
| `Microsoft.EntityFrameworkCore.Relational` | 8.0.27 | idem |
| *(worker)* `Npgsql` | 8.0.7 | ADO.NET seul |
| *(worker)* `Microsoft.Data.SqlClient` | 5.2.2 | ADO.NET seul |

Références projet : `MMV.Domain`, `MMV.Infrastructure` — **en lecture seule, sans aucune modification**.

**Aucun secret dans le projet.** Les chaînes proviennent de `MMV_P4_POSTGRES_CONNECTION` /
`MMV_P4_SQLSERVER_CONNECTION`. Comportement vérifié sans variables :

```
Skipped! - Failed: 0, Passed: 0, Skipped: 2, Total: 2
```

→ le harness **n'invente jamais un succès** en l'absence de provider.

### 9.2 Défauts du harness détectés et corrigés (transparence)

Trois défauts du **harness** ont produit des mesures fausses ou des blocages, ont été diagnostiqués, corrigés,
puis les expérimentations ont été **rejouées**. Ils sont consignés parce qu'ils conditionnent la validité des
chiffres :

| # | Défaut | Symptôme | Correctif |
|---|---|---|---|
| 1 | **Cache de modèle EF par type de contexte** | La variante « mapping `REAL` » était mesurée comme `numeric(18,2)` : la 1re variante exécutée gagnait et les suivantes réutilisaient son modèle → **§11 aurait été entièrement faux** | `SpikeModelCacheKeyFactory` : clé = (type, provider, mapping) |
| 2 | **`Barrier.SignalAndWait()` avant le premier `await`** | E5 bloqué indéfiniment (le 1er appel bloque le thread, le 2e participant n'est jamais créé) | `Task.Run(Attempt)` — thread propre par participant |
| 3 | **`.Replace()` lié au dernier littéral d'une concaténation** | E10 SQL Server : `Incorrect syntax near '|'` (l'opérateur `||` n'était remplacé que dans le dernier segment) | Opérateur de concaténation construit explicitement par provider |

Un quatrième point, propre à PostgreSQL et **non** au harness, est documenté en §21 : après `dropdb --force`, les
connexions encore en pool lèvent `57P01` ; le pool doit être purgé.

---

## 10. Modèle EF — stratégie retenue

**Stratégie A du brief (§12) : modèle RÉEL `OpticDbContext` + `EnsureCreated`.**

Elle est possible parce que `OpticDbContext.OnConfiguring` ne retombe sur SQLite que si
`!optionsBuilder.IsConfigured` ([OpticDbContext.cs:133-141](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L133-L141)) :
fournir des options configurées suffit à exercer le modèle de production **sans le modifier**.

> `EnsureCreated` n'est **PAS** une stratégie de migration. Elle sert ici uniquement à matérialiser le modèle pour
> l'observer. Aucune migration SQLite n'a été appliquée à un serveur ; aucune chaîne de migrations serveur n'a été
> créée.

### 10.1 E1 — modèle réel, sans aucune adaptation

| Provider | Résultat | Détail |
|---|---|---|
| **PostgreSQL 17.10** | ❌ **ÉCHEC** en 2,9 s | `Npgsql.PostgresException` — **`42883: operator does not exist: boolean = integer`** |
| **SQL Server Express 16.0.4265.3** | ✅ **SUCCÈS** en 0,5 s | 21 tables · 51 index · 20 clés étrangères |

**Cause de l'échec PostgreSQL** : l'index filtré `idx_workshop_sheets_current_unique` porte un filtre **SQL
littéral** écrit pour SQLite — `HasFilter("\"IsCurrent\" = 1")`
([WorkshopSheetConfiguration.cs:60-63](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs#L60-L63)).
PostgreSQL possède un **booléen natif** et refuse `boolean = integer`. SQL Server mappe `bool` sur `bit`, donc
`= 1` y est valide. Le risque annoncé par P4-0 §11 est **confirmé expérimentalement**.

### 10.2 E1b — modèle réel + adaptations minimales

Une seule adaptation a suffi, et **uniquement pour PostgreSQL** :

```
Index 'idx_workshop_sheets_current_unique' : filtre réécrit « "IsCurrent" = 1 » -> « "IsCurrent" »
```

| Provider | Résultat | Schéma obtenu |
|---|---|---|
| PostgreSQL 17.10 | ✅ SUCCÈS | **21 tables · 51 index · 20 FK** |
| SQL Server Express | ✅ SUCCÈS (aucune adaptation requise) | **21 tables · 51 index · 20 FK** |

**Les deux providers produisent une structure identique en nombre.** La dette de portage du **schéma** se réduit,
à ce stade et pour ce modèle, à **un seul filtre d'index** côté PostgreSQL.

---

## 11. Précision monétaire (E2) — **BLOQUANT pour le mapping actuel**

Le modèle mappe des `decimal` métier sur `HasColumnType("REAL")`
([SaleConfiguration.cs:28-44](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L28-L44)).
Protocole par valeur : écriture EF → relecture par **nouveau contexte** → relecture par **SQL natif** →
comparaison à la valeur exacte.

### 11.1 Mapping ACTUEL (`REAL`, tel quel dans `src/**`)

| Provider | Mapping testé | Type physique | Valeur écrite | Valeur relue (EF) | Valeur relue (SQL) | Exact ? |
|---|---|---|---|---|---|---|
| PostgreSQL | `REAL` | **`real`** (float4) | 0,01 | 0,010 | 0.01 | ✅ |
| PostgreSQL | `REAL` | `real` | 0,10 | 0,1 | 0.1 | ✅ |
| PostgreSQL | `REAL` | `real` | 0,30 | 0,3 | 0.3 | ✅ |
| PostgreSQL | `REAL` | `real` | 19,99 | 19,99 | 19.99 | ✅ |
| PostgreSQL | `REAL` | `real` | **999999,99** | **1000000** | **1e+06** | ❌ |
| SQL Server | `REAL` | **`real(24,0)`** (float 24 bits) | 0,01 | 0,010 | 0.01 | ✅ |
| SQL Server | `REAL` | `real(24,0)` | 0,10 | 0,1 | 0.1 | ✅ |
| SQL Server | `REAL` | `real(24,0)` | 0,30 | 0,3 | 0.3 | ✅ |
| SQL Server | `REAL` | `real(24,0)` | 19,99 | 19,99 | 19.99 | ✅ |
| SQL Server | `REAL` | `real(24,0)` | **999999,99** | **1000000** | **1e+006** | ❌ |

**Bilan : 4/5 sur chaque provider.** `REAL` est traduit en **flottant simple précision** des deux côtés
(`real` / `float4`, 24 bits de mantisse) : au-delà d'environ 7 chiffres significatifs, **le montant est altéré
silencieusement**. Une vente à 999 999,99 est relue **1 000 000**.

### 11.2 Mapping candidat exact (mesure de comparaison seulement)

| Provider | Mapping testé | Type physique | Résultat |
|---|---|---|---|
| PostgreSQL | `numeric(18,2)` | `numeric(18,2)` | **5/5 exactes** ✅ |
| SQL Server | `decimal(18,2)` | `decimal(18,2)` | **5/5 exactes** ✅ |

### 11.3 Scénario composite (3 lignes + remise + acompte + reste)

3 × 0,10 € − remise 0,07 € − acompte 0,05 € ⇒ final attendu 0,23 € ; reste attendu 0,18 €.

| Provider | Mapping | Somme serveur `FinalAmount` | Reste relu | Exact ? |
|---|---|---|---|---|
| PostgreSQL | `numeric(18,2)` | 0.23 | 0,18 | ✅ |
| PostgreSQL | `REAL` | 0.23 | 0,18 | ✅ |
| SQL Server | `decimal(18,2)` | 0.23 | 0,18 | ✅ |
| SQL Server | `REAL` | 0.23 | 0,18 | ✅ |

> **Résultat contraire à l'attente, conservé tel quel.** Sur ce scénario, `REAL` donne aussi le bon résultat sur
> les deux providers : à cette **faible magnitude**, l'aller-retour `decimal → float32 → decimal` retombe sur la
> valeur exacte. **Cela n'innocente pas `REAL`** — l'altération est démontrée à magnitude élevée (§11.1) — mais
> cela montre que **le défaut est silencieux et dépendant des montants** : il ne se manifestera pas sur les
> petites ventes de test, seulement sur les montants réels élevés. C'est ce qui le rend dangereux.

> **Conclusion factuelle §11** : le mapping `REAL` est **bloquant sur les deux providers**. Le risque « élevé »
> annoncé par P4-0 §17 est **démontré**. Un mapping décimal exact restitue toutes les valeurs testées sur les
> deux providers. **Aucune précision finale n'est choisie ici** : le couple type/précision relève de P4-2, et
> `18,2` n'a servi que de témoin de comparaison. **Ce point ne départage pas les providers** — ils échouent et
> réussissent identiquement.

---

## 12. Index filtrés / partiels (E1b, E3, E4)

### 12.1 DDL réellement produit

**PostgreSQL** (après adaptation du filtre booléen) :

```sql
CREATE UNIQUE INDEX idx_notifications_active_low_stock_unique ON public."Notifications"
  USING btree ("Type", "EntityType", "EntityId")
  WHERE ((("Type")::text = 'LowStock'::text) AND (("EntityType")::text = 'Product'::text)
     AND ("EntityId" IS NOT NULL) AND ("ResolvedAt" IS NULL));

CREATE UNIQUE INDEX idx_workshop_sheets_current_unique ON public."WorkshopSheets"
  USING btree ("OrderId") WHERE "IsCurrent";
```

**SQL Server Express** (aucune adaptation) :

```
idx_notifications_active_low_stock_unique
  filter = ([Type]='LowStock' AND [EntityType]='Product' AND [EntityId] IS NOT NULL AND [ResolvedAt] IS NULL)
idx_workshop_sheets_current_unique
  filter = ([IsCurrent]=(1))
```

Booléens physiques : PostgreSQL **`boolean`** · SQL Server **`bit`**.

### 12.2 E3 — alerte LowStock active unique

| Étape | PostgreSQL | SQL Server Express |
|---|---|---|
| 1re alerte active | ACCEPTÉ | ACCEPTÉ |
| 2e alerte, même produit | **REFUSÉ** — `23505`, contrainte `idx_notifications_active_low_stock_unique` | **REFUSÉ** — `Number=2601`, index `idx_notifications_active_low_stock_unique` |
| Résolution | 1 ligne | 1 ligne |
| Nouvel épisode après résolution | **ACCEPTÉ** | **ACCEPTÉ** |
| État final | 2 alertes, 1 active — **historique conservé** | 2 alertes, 1 active — **historique conservé** |

### 12.3 E4 — fiche atelier courante unique

| Étape | PostgreSQL | SQL Server Express |
|---|---|---|
| v1 courante | ACCEPTÉ | ACCEPTÉ |
| v2 courante **sans bascule** | **REFUSÉ** — `23505` / `idx_workshop_sheets_current_unique` | **REFUSÉ** — `2601` / `idx_workshop_sheets_current_unique` |
| Bascule atomique v1→v2 en transaction | **SUCCÈS** | **SUCCÈS** |
| Doublon `(OrderId, Version)` | **REFUSÉ** — `idx_workshop_sheets_order_version_unique` | **REFUSÉ** — `2601` |
| État final | 2 versions, **1 seule courante** | 2 versions, **1 seule courante** |

**Les deux garanties structurelles tiennent sur les deux providers**, une fois le filtre booléen adapté côté
PostgreSQL.

---

## 13. Upsert de l'alerte active (E5)

Primitive de production : `INSERT … ON CONFLICT DO NOTHING` + `SqliteParameter`
([NotificationRepository.cs:115-133](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L115-L133))
— **non portable telle quelle**. Deux stratégies mesurées, **10 tours chacune**, deux connexions réelles
synchronisées par barrière (jamais de `check-then-act` comme solution finale) :

| Provider | Stratégie | Forme SQL réellement employée | Tours conformes | Anomalies | Deadlocks non gérés |
|---|---|---|---|---|---|
| PostgreSQL | Primitive native | `INSERT … ON CONFLICT DO NOTHING` | **10/10** | 0 | 0 |
| PostgreSQL | Insertion + capture | `INSERT` puis capture `23505` | **10/10** | 0 | 0 |
| SQL Server | Primitive native | `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))` | **10/10** | 0 | 0 |
| SQL Server | Insertion + capture | `INSERT` puis capture `2601` | **10/10** | 0 | 0 |

« Conforme » = exactement 1 insertion, 1 refus, **1 seule alerte active** en base, aucune erreur inattendue.

**Fait de portabilité** : PostgreSQL dispose de la primitive `ON CONFLICT DO NOTHING` **déjà utilisée par le code
actuel**. SQL Server **n'a pas** cet opérateur ; la forme équivalente testée exige un `WHERE NOT EXISTS` avec des
**indices de verrouillage explicites** (`UPDLOCK, HOLDLOCK`). Les deux tiennent la garantie ; **le coût de
réécriture n'est pas le même**.

---

## 14. Contraintes et erreurs (E6)

`PersistenceErrorMapper` **n'a pas été modifié**. Faits relevés :

| Provider | Erreur provoquée | Exception | Code / état | Contrainte nommée | Classification P4 **candidate** |
|---|---|---|---|---|---|
| PostgreSQL | Unicité (index filtré) | `PostgresException` | **`23505`** | `idx_notifications_active_low_stock_unique` | Duplicate |
| PostgreSQL | Clé primaire | `PostgresException` | `23505` | `PK_ProductCategories` | Duplicate |
| PostgreSQL | Clé étrangère | `PostgresException` | **`23503`** | `FK_Orders_Sales_SaleId` | ConstraintViolation (FK) |
| PostgreSQL | NOT NULL | `PostgresException` | **`23502`** | *(colonne `Type`)* | ConstraintViolation (NOT NULL) |
| PostgreSQL | Timeout de commande | `NpgsqlException` | **aucun `SqlState`** | — | ⚠️ **Unknown** |
| PostgreSQL | `CancellationToken` | `OperationCanceledException` → `PostgresException` | **`57014`** | — | Timeout (query canceled) |
| PostgreSQL | Connexion refusée | `NpgsqlException` | **aucun `SqlState`** | — | ⚠️ **Unknown** |
| PostgreSQL | Base inexistante | `PostgresException` | **`3D000`** | — | DatabaseUnavailable |
| SQL Server | Unicité (index filtré) | `SqlException` | **`2601`** | `idx_notifications_active_low_stock_unique` | Duplicate |
| SQL Server | Clé primaire | `SqlException` | **`2627`** | `PK_ProductCategories` | Duplicate |
| SQL Server | Clé étrangère | `SqlException` | **`547`** | *(colonne `SaleId`)* | ConstraintViolation (FK) |
| SQL Server | NOT NULL | `SqlException` | **`515`** | *(colonne `Type`)* | ConstraintViolation (NOT NULL) |
| SQL Server | Timeout de commande | `SqlException` | **`-2`** | — | Timeout |
| SQL Server | `CancellationToken` | `SqlException` | **`Number=0`** | — | ⚠️ **Unknown** |
| SQL Server | Connexion refusée | `SqlException` | **`10061`** | — | DatabaseUnavailable |
| SQL Server | Base inexistante | `SqlException` | **`4060`** | — | DatabaseUnavailable |

**Différences à retenir pour P4-2 :**

1. **Nommage des contraintes** — PostgreSQL expose un champ **structuré** `PostgresException.ConstraintName`
   (plus `TableName`, `ColumnName`). SQL Server **n'a pas d'équivalent structuré** : le nom n'existe que dans le
   **texte** du message, à une position qui dépend du code d'erreur (`2601` → 2e littéral ; `2627`/`547` → 1er).
   Toute classification SQL Server dépendante du nom de contrainte reposera donc sur du **parsing de message**,
   sensible à la langue et à la version.
2. **Trous de classification** — 3 cas ne sont **pas** classifiables en l'état : timeout PostgreSQL et connexion
   refusée PostgreSQL (aucun `SqlState`), et annulation SQL Server (`Number=0`). Ils demanderaient une détection
   par type/état de connexion, pas par code.
3. Les codes de duplication diffèrent **entre les deux erreurs SQL Server** (`2601` index unique vs `2627`
   contrainte UNIQUE/PK) : les deux doivent être traités.

---

## 15. Transactions (E7)

| Comportement | PostgreSQL 17.10 | SQL Server Express |
|---|---|---|
| Rollback EF + **SQL brut enrôlé** | **0 ligne subsistante** — CONFORME | **0 ligne subsistante** — CONFORME |
| Savepoint / transaction imbriquée | **SUPPORTÉ** (`SP-OUTER`=1, `SP-INNER`=0) | **SUPPORTÉ** (idem) |
| Erreur NOT NULL dans la transaction | classifiée `ConstraintViolation (NOT NULL)` | idem |
| **Transaction utilisable APRÈS une erreur** | ❌ **NON** — `25P02` *(in_failed_sql_transaction)* | ✅ **OUI** (lecture ultérieure réussie) |
| Annulation par `CancellationToken` | `57014` (voir §14) | `Number=0` (voir §14) |

> **Différence sémantique majeure, à porter à l'ADR.** Sur **PostgreSQL**, toute erreur **avorte la transaction
> entière** : toute commande ultérieure échoue en `25P02` jusqu'au `ROLLBACK`. Sur **SQL Server**, la transaction
> reste utilisable après une erreur de contrainte. Tout code MMV qui **rattrape** une erreur à l'intérieur d'une
> transaction et **poursuit** fonctionnera sur SQL Server et **échouera sur PostgreSQL**. Ce comportement n'est
> pas couvert par les tests P3 actuels (SQLite mono-écrivain) et **doit être audité en P4-2**.

---

## 16. Isolation

| Provider | Isolation par défaut **relevée sur le serveur** | Méthode de relevé |
|---|---|---|
| PostgreSQL 17.10 | **`read committed`** | `SHOW transaction_isolation;` dans une transaction ouverte |
| SQL Server Express | **`ReadCommitted`** | `sys.dm_exec_sessions.transaction_isolation_level` (= 2) |

Les deux valeurs sont **mesurées**, pas déduites de la documentation. `EfTransactionRunner` n'exprimant aucun
niveau explicite (P4-0 §14), c'est bien ce défaut serveur qui s'appliquera.

### 16.1 `READ_COMMITTED_SNAPSHOT` — **mesuré au lot B**

La mesure initiale avait échoué sur un **conflit de collation** non diagnostiqué. Le lot B a reproduit puis
**identifié précisément** la cause : ce n'est pas la colonne `sys.databases.name`
(`SQL_Latin1_General_CP1_CI_AS`, identique au serveur) mais la colonne **descriptive**
`snapshot_isolation_state_desc`, typée **`Latin1_General_CI_AS_KS_WS`**. La concaténer avec un littéral lève
**`Msg 451`**. Le contournement est trivial — sélectionner les colonnes séparément, ou forcer
`COLLATE DATABASE_DEFAULT`.

| Mesure | **RCSI = OFF** (défaut d'Express) | **RCSI = ON** |
|---|---|---|
| `is_read_committed_snapshot_on` | **0** | **1** |
| Lecture pendant écriture non commitée | ❌ **BLOQUÉE** puis expirée (`Number = -2`) après **5 022 ms** | ✅ **NON BLOQUANTE** — valeur commitée renvoyée en **9 ms** |
| Cohérence après rollback de l'écrivain | préservée ✅ | préservée ✅ |
| Primitive CAS (décrément du dernier article) | `[1, 0]`, stock **0** — **aucune survente** ✅ | `[1, 0]`, stock **0** — **aucune survente** ✅ |

**RCSI supprime le blocage des lecteurs sans affaiblir la primitive CAS.** Express est livré avec
**RCSI = OFF**, donc les lecteurs bloquent par défaut ; PostgreSQL ne bloque jamais les lecteurs en
`read committed` (MVCC natif). Détail complet : lot B §15.

---

## 17. Concurrence — même processus (E8)

Primitives exécutées via les **implémentations de production réelles** (`EfStockMutationService`,
`EfNumberSequenceService`, EF `ExecuteUpdateAsync`/`ExecuteDeleteAsync`), deux connexions réelles synchronisées :

| Primitive | PostgreSQL | SQL Server Express | Garantie obtenue | Différence observée |
|---|---|---|---|---|
| Décrément du **dernier** article | 1 succès / stock 0 | 1 succès / stock 0 | **Aucune survente** | aucune |
| Numérotation documentaire | `SP-000001`, `SP-000002` | `SP-000001`, `SP-000002` | **Unicité stricte** | aucune |
| Règlement du solde | lignes affectées `[1, 0]` | `[1, 0]` | **Un seul règlement** | aucune |
| Suppression fournisseur si inutilisé (`NOT EXISTS`) | 0 supprimé | 0 supprimé | **Historique préservé** | aucune |
| Unicité du login normalisé | 1 créé / 1 refusé (Duplicate) | 1 créé / 1 refusé (Duplicate) | **Aucun doublon** | codes d'erreur distincts (§14) |

> **Correction apportée par le lot B.** L'affirmation initiale « 5 primitives sur les 15 recensées » était
> inexacte sur **les deux termes** :
>
> - le dénominateur contient un **doublon conceptuel** — la ligne 15 de P4-0 §15
>   (« Numérotation — table `DocumentSequences` ») ne porte ni port, ni implémentation, ni mécanisme : elle
>   décrit la **table de stockage** de la primitive « Numérotation », pas une primitive distincte. Le total
>   réel est **14** ;
> - sur les 5 « exécutées », **2 seulement** passaient par l'implémentation de production
>   (`DecrementStockAsync`, `NextNumberAsync`). Les 3 autres — règlement du solde, suppression de fournisseur,
>   unicité du login — étaient exercées par du code **réécrit dans le harness**, pas par les méthodes de
>   production.
>
> Après le lot B : **13 des 14 primitives sont exercées via les implémentations de production**, la 14e
> (`TryCreateActiveLowStockAsync`) étant en **échec mesuré** sur les deux providers. Inventaire réconcilié
> complet : [lot B §6](P4-1-technical-completion-lot-b-report.md) ; résultats : lot B §7.

---

## 18. Concurrence multi-processus (E9) — **exécutée**

Deux **exécutables indépendants** (`MMV.P4.Worker.exe`), PID distincts, connexions distinctes, départ donné par un
**événement nommé Windows** (signal externe réel ; **aucun `Thread.Sleep`** comme mécanisme de synchronisation).
Le secret transite par l'**environnement** du processus enfant, jamais par la ligne de commande.

| Scénario | PostgreSQL | SQL Server Express | Résultat |
|---|---|---|---|
| Deux postes vendent le **dernier** article | PID 21176 `LOST` / PID 2936 `WON` — stock final **0** | PID 6216 `WON` / PID 40132 `LOST` — stock final **0** | **Aucune survente** |
| Deux postes créent la **même** alerte LowStock | PID 39360 `LOST` / PID 31412 `WON` — **1** alerte active | PID 39708 `LOST` / PID 6700 `WON` — **1** alerte active | **Anti-doublon tenu** |
| Deux postes tirent un **numéro** | deux `WON` — compteur final **2** | deux `WON` — compteur final **2** | **Aucun numéro perdu ni dupliqué** |

C'est la forme de preuve **absente de tout le corpus P3** (P4-0 §23/§24). Elle est désormais disponible **sur les
deux providers**, pour trois scénarios.

---

## 19. Installation et exploitation Windows

| Élément | PostgreSQL | SQL Server Express | Niveau de preuve |
|---|---|---|---|
| Installation Windows native | *(instance 17.4 présente mais interdite d'usage)* | **tentée — voir §19.1** | `NOT_TESTED` / en cours |
| Service Windows | `postgresql-x64-17`, **Running / Automatic** (observé) | non observé (aucune instance) | `EXECUTED` (observation seule) / `NOT_TESTED` |
| Démarrage automatique | observé (`StartMode = Auto`) | `NOT_TESTED` | — |
| Écoute réseau | observée : **5432 sur `0.0.0.0` et `::`** | `NOT_TESTED` en natif | — |
| Authentification | `pg_hba.conf` en **`scram-sha-256`** (lu) | `NOT_TESTED` en natif | `OFFICIAL_DOCUMENTATION` |
| Pare-feu | `NOT_TESTED` | `NOT_TESTED` | — |
| Outil d'administration | `psql` 17.4 présent | `sqlcmd` 15.0.1300.359 présent ; SSMS non installé | `EXECUTED` (présence) |
| Emplacement des données | `C:\Program Files\PostgreSQL\17\data` (observé) | `NOT_TESTED` | — |
| Planification des sauvegardes | hors produit (tâche planifiée) | **SQL Server Agent : Non en Express** | `OFFICIAL_DOCUMENTATION` |
| Mise à jour / désinstallation | `NOT_TESTED` | `NOT_TESTED` | — |

**Aucune installation n'est qualifiée de « simple » ou « facile »** : aucun protocole d'installation complet n'a
été exécuté et chronométré de bout en bout.

### 19.1 Installation native SQL Server Express — état à la rédaction

Autorisée explicitement par l'utilisateur, avec obligation de s'arrêter si une élévation ou un redémarrage est
nécessaire. La session **n'est pas élevée** ; la question a été posée et l'utilisateur a retenu
« **Docker d'abord, natif ensuite** ».

Étapes réalisées :

| # | Étape | Résultat |
|---|---|---|
| 1 | Résolution du lien officiel `https://go.microsoft.com/fwlink/p/?linkid=2216019` | redirige aujourd'hui vers **`https://aka.ms/sql2025express`** — Microsoft sert désormais **SQL Server 2025** |
| 2 | Téléchargement du bootstrapper (4,4 Mo) | **signature Authenticode `Valid`**, `CN=Microsoft Corporation, …, C=US` |
| 3 | `/ACTION=Download /MEDIATYPE=Core /QUIET` | média récupéré : `SQLEXPR_x64_FRA.exe`, **753 Mo**, signature `Valid` |
| 4 | Extraction `/Q /X:` | OK — `setup.exe` **version `17.0.1000.7`**, `2025.0170.1000.07 (sql2025_rtm)` |
| 5 | Installation élevée (UAC approuvée) — instance nommée `MMVP4`, `FEATURES=SQLEngine`, `SECURITYMODE=SQL`, `TCPENABLED=1`, démarrage automatique | ❌ **ÉCHEC — code de sortie `-2067529714` (`0x84C4000E`)** |
| 6 | Recherche des journaux de setup | **aucun `Setup Bootstrap\Log` produit** → échec **avant** le démarrage du moteur d'installation |

**L'installation Windows native de SQL Server Express a été tentée avec élévation, mais elle a échoué avant la
création d'une instance ou d'un service exploitable.**

**Cause racine non déterminée. La coexistence de composants SQL Server 2025 RC et du média RTM est une hypothèse
plausible, mais non démontrée faute de journal d'installation exploitable.** Les faits réellement relevés sont
les suivants : le poste porte des composants **SQL Server 2025 en version RC** — LocalDB relevé à
**`17.0.925.4`, `ProductLevel = RC1`** (§5.2), plus `C:\Program Files\Microsoft SQL Server\170\`
(`LocalDB`, `Tools`, `Shared`) — alors que le média téléchargé est le **RTM `17.0.1000.7`** ; et aucun
répertoire `Setup Bootstrap\Log` n'a été produit, ce qui prive l'analyse de toute trace du programme
d'installation.

**Aucune désinstallation n'est recommandée.** Établir la cause racine suppose d'abord d'obtenir un journal
d'installation exploitable (option `/LOG`), ce qui n'a pas été fait.

> Conformément à la consigne explicite — *« Si des privilèges administrateur ou un redémarrage de la machine sont
> nécessaires, arrêtez-vous et demandez avant de continuer »* — **le spike s'ARRÊTE ici** sur ce point. Aucun
> composant existant n'a été désinstallé, aucun service n'a été modifié, aucun redémarrage n'a été provoqué.

Conséquence directe : **`Installation Windows` reste `NOT_TESTED` pour SQL Server Express** et le candidat
SQL Server Express **n'est pas prêt pour l'ADR opérationnelle** (§26).

> **Mise à jour — lot B (2026-07-24).** Le lot technique B a été exécuté sur conteneurs jetables sans
> reprendre l'installation Windows : voir
> [P4-1-technical-completion-lot-b-report.md](P4-1-technical-completion-lot-b-report.md). Ce point
> reste le seul critère manquant du gate minimal ; il demeure inchangé.

---

## 20. Configuration et secrets

- Chaînes fournies **exclusivement** par `MMV_P4_POSTGRES_CONNECTION` / `MMV_P4_SQLSERVER_CONNECTION`
  (+ `MMV_P4_SA_PASSWORD` pour E10, `MMV_P4_WORKER_CONNECTION` pour le worker).
- **Aucun fichier `.env` suivi.** Les mots de passe générés aléatoirement résident dans le **répertoire temporaire
  de session**, hors du dépôt, et sont supprimés au nettoyage (§27.3).
- Le harness n'expose que : provider, hôte (`localhost`/`127.0.0.1`), port, base jetable, **présence** du secret.
  Exemple réel de sortie :

  ```
  Postgres: host=localhost port=5433 database=mmv_p4_spike user=présent password=PRÉSENT (masqué)
  SqlServer: server=127.0.0.1,14333 database=mmv_p4_spike user=présent password=PRÉSENT (masqué)
  ```

- **Aucun mot de passe, token, chaîne complète ou identifiant sensible n'apparaît dans ce rapport ni dans
  `spikes/**`.**

---

## 21. Sauvegarde et restauration (E10) — exécutée sur les deux

Protocole : base jetable → jeu cohérent (FK, montants exacts, index filtré) → **outil officiel** → suppression de
la base → restauration → recomptage, vérification FK/index/montants → **reconnexion applicative**.

| Élément | PostgreSQL | SQL Server Express |
|---|---|---|
| Outil de sauvegarde | **`pg_dump -F c`** | **`BACKUP DATABASE … TO DISK` (`WITH INIT, FORMAT`)** |
| Outil de restauration | **`pg_restore`** (après `dropdb --force` + `createdb`) | **`RESTORE DATABASE … WITH RECOVERY`** |
| Sortie | `OK` | `Processed 560 pages …` |
| Taille indicative | **60 Ko** | **4,5 Mo** |
| Durée indicative (sauvegarde + restauration) | **0,9 s** | **3,9 s** |
| Lignes avant → après (S/P/V/N) | `1/1/1/1` → `1/1/1/1` ✅ | `1/1/1/1` → `1/1/1/1` ✅ |
| FK / index avant → après | `20 FK, 51 index` → identique ✅ | `20 FK, 51 index` → identique ✅ |
| Somme des montants avant → après | `999999.99` → `999999.99` ✅ | `999999.99` → `999999.99` ✅ |
| Réconciliation | **IDENTIQUE** | **IDENTIQUE** |
| Connexion applicative après restauration | **OK**, montant relu `999999,99` — **EXACT** | **OK**, montant relu `999999,99` — **EXACT** |

Étape manuelle notable côté PostgreSQL : la base doit être **supprimée et recréée** avant `pg_restore`, et le
**pool de connexions doit être purgé** — sinon `57P01 (terminating connection due to administrator command)`.
Côté SQL Server, `RESTORE` exige le passage en `SINGLE_USER` avec `ROLLBACK IMMEDIATE`.

> **Portée** : sauvegarde/restauration exécutée sur le **moteur** Express en conteneur Linux. Ce n'est **pas** une
> validation de l'exploitation d'une installation Express **Windows** (planification, droits de service,
> emplacement des fichiers, absence de SQL Server Agent).

---

## 22. Migration SQLite → serveur

> **Mise à jour — lot B : un prototype RÉDUIT a été exécuté et réconcilié sur les deux providers.**
> Source SQLite **jetable** créée par le harness (aucune base MMV utilisateur lue),
> **10 tables**, identifiants conservés, montants exacts, dates, booléens, enums, chaînes normalisées et
> `NULL` préservés, séquences resynchronisées, donnée invalide **refusée** avec rollback propre, et
> `__EFMigrationsHistory` **jamais importé** (vérifié absent sur la cible). Trois obstacles ont été
> découverts par la mesure : données de référence `HasData` présentes **des deux côtés** (collision de PK),
> `IDENTITY_INSERT` impossible sur une PK naturelle, et `DateTimeKind` non préservé côté SQL Server.
> Détail : [lot B §16](P4-1-technical-completion-lot-b-report.md). Le paragraphe ci-dessous décrit l'état
> **avant** le lot B.

**Non exécutée au moment de la rédaction initiale.** Aucun prototype d'import n'avait été construit, aucune
base MMV réelle n'a été lue, et `__EFMigrationsHistory` n'a jamais été importé.

Ce qui est néanmoins **acquis** pour préparer ce travail :

- le modèle réel se matérialise sur les deux serveurs avec **une seule adaptation de schéma** (§10.2) ;
- l'ordre d'insertion imposé par les FK a été exercé de fait (Supplier → Product ; Sale → Order → WorkshopSheet) ;
- les identifiants `long` sont préservés (auto-incrément côté serveur ; `IDENTITY_INSERT` requis sur SQL Server
  pour forcer une PK explicite — observé en E6) ;
- **les montants ne peuvent pas être importés à l'identique tant que le mapping est `REAL`** (§11) : c'est le
  **prérequis** de tout import fidèle.

Statut **après le lot B** : **`EXECUTED` (prototype réduit)** pour les deux providers — sans validation à
volumétrie réelle, sans mesure de durée, et sans test de reprise sur import interrompu.

---

## 23. Récapitulatif E1 → E10

| # | Expérimentation | PostgreSQL 17.10 | SQL Server Express 16.0.4265.3 |
|---|---|---|---|
| E1 | Modèle réel, sans adaptation | ❌ `42883 boolean = integer` | ✅ 21/51/20 |
| E1b | Modèle réel + adaptations minimales | ✅ 21/51/20 — **1 adaptation** | ✅ 21/51/20 — **0 adaptation** |
| E2 | Précision monétaire (`REAL` actuel) | ❌ **4/5** | ❌ **4/5** |
| E2 | Précision monétaire (décimal exact) | ✅ 5/5 | ✅ 5/5 |
| E3 | Index LowStock actif | ✅ | ✅ |
| E4 | Index fiche courante + `(OrderId,Version)` | ✅ | ✅ |
| E5 | Upsert concurrent (2 stratégies × 10 tours) | ✅ 10/10 et 10/10 | ✅ 10/10 et 10/10 |
| E6 | Classification des erreurs | ✅ 6/8 classifiables | ✅ 7/8 classifiables |
| E7 | Transactions | ✅ *(sauf reprise après erreur)* | ✅ |
| E8 | Primitives P3 (5 exécutées) | ✅ 5/5 | ✅ 5/5 |
| E9 | **Multi-processus** (3 scénarios) | ✅ 3/3 | ✅ 3/3 |
| E10 | Sauvegarde / restauration | ✅ | ✅ |

### 23.1 Expérimentations ajoutées par le lot B (E11 → E17)

| # | Expérimentation | PostgreSQL 17.10 | SQL Server Express 16.0.4265.3 |
|---|---|---|---|
| E11 | Primitives P3 restantes (implémentations de production) | ✅ 12/13 · 1 **échec mesuré** | ✅ 12/13 · 1 **échec mesuré** |
| E12 | **Deadlock croisé provoqué** | ✅ `40P01` · victime unique · transaction **perdue** | ✅ `1205` · victime unique · transaction **utilisable** |
| E13 | Timeouts (client, annulation, verrou, connexion) | ✅ 4 mesurés · 2 sans `SqlState` | ✅ 4 mesurés · annulation `Number=0` |
| E14 | Perte de connexion, redémarrage, reconnexion | ✅ reprise **0,5 s** · purge du pool requise | ✅ reprise **3,1 s** · purge du pool requise |
| E15 | Retry (harness) + idempotence | ✅ reprise en **2** tentatives | ✅ reprise en **5** tentatives · code **`10054`** |
| E16 | `READ_COMMITTED_SNAPSHOT` | *(sans objet — MVCC natif)* | ✅ **OFF et ON mesurés** |
| E17 | Import SQLite réduit | ✅ réconciliation **complète** | ✅ réconciliation **complète** |

Détail complet : [P4-1-technical-completion-lot-b-report.md](P4-1-technical-completion-lot-b-report.md).

---

## 24. Limites et éléments non testés

| Élément | Statut | Raison |
|---|---|---|
| **Installation Windows native de SQL Server Express** | `NOT_TESTED` | **tentée avec élévation, échouée** (`0x84C4000E`) avant création d'une instance ou d'un service exploitable ; cause racine **non déterminée** (§19.1) |
| Installation Windows native de PostgreSQL | `NOT_TESTED` | instance 17.4 présente mais **interdite d'usage** (contenu inconnu) |
| Pare-feu, accès depuis un **autre poste** du réseau | `NOT_TESTED` | tout est resté sur `localhost` (exigence de l'utilisateur) |
| ~~10 primitives P3 sur 15~~ | **`EXECUTED` (lot B)** | inventaire réconcilié à **14** primitives distinctes ; **13 exercées via les implémentations de production**, 1 en **échec mesuré** (lot B §6, §7) |
| `READ_COMMITTED_SNAPSHOT` (SQL Server) | **`EXECUTED` (lot B)** | cause du conflit de collation identifiée puis contournée ; OFF **et** ON mesurés (§16.1) |
| Deadlocks provoqués volontairement | **`EXECUTED` (lot B)** | scénario croisé déterministe ; `40P01` / `1205` (lot B §8) |
| Retry / execution strategy | **`EXECUTED` (lot B, harness seulement)** | politique bornée expérimentale ; **le produit reste sans retry** (lot B §12) |
| Perte de connexion / redémarrage serveur en pleine transaction | **`EXECUTED` (lot B)** | coupure brutale, état final vérifié après redémarrage (lot B §11) |
| Import SQLite → serveur | **`EXECUTED` (lot B, prototype réduit)** | 10 tables, réconciliation complète ; **pas** de validation à volumétrie réelle (lot B §16) |
| Volumétrie réelle d'un import, durée, reprise d'un import interrompu | `NOT_TESTED` | prototype réduit uniquement |
| Volumétrie réelle de MMV vs limite **10 Go** d'Express | `NOT_TESTED` | aucune mesure de volumétrie |
| Performance, montée en charge | `NOT_TESTED` | hors périmètre du spike |
| Comportement sous Windows du moteur SQL Server | `NOT_TESTED` | moteur testé **sur Linux** |

---

## 25. Matrice comparative finale

Niveaux : `EXECUTED` · `OFFICIAL_DOCUMENTATION` · `ENGINE_ONLY` · `NOT_TESTED` · `BLOCKED`.

| Critère | PostgreSQL | SQL Server Express | Niveau de preuve |
|---|---|---|---|
| Instance réelle testée | Oui — conteneur jetable, TCP `127.0.0.1:5433` | Oui — conteneur jetable, TCP `127.0.0.1:14333` | `EXECUTED` |
| Édition exacte testée | **17.10** (relevée par `version()`) | **Express Edition (64-bit)**, `EngineEdition=4`, **16.0.4265.3** (relevée par `SERVERPROPERTY`) | `EXECUTED` |
| EF Core modèle réel | ❌ échec sans adaptation ; ✅ avec **1** adaptation | ✅ **sans aucune adaptation** | `EXECUTED` |
| Précision monétaire | ❌ `REAL` = `real` → 999999,99 → 1000000 ; ✅ `numeric(18,2)` = 5/5 | ❌ `REAL` = `real(24,0)` → idem ; ✅ `decimal(18,2)` = 5/5 | `EXECUTED` |
| Index LowStock | ✅ index partiel, `23505` nommé | ✅ index filtré, `2601` | `EXECUTED` |
| Index WorkshopSheet | ✅ après réécriture `"IsCurrent" = 1` → `"IsCurrent"` | ✅ `[IsCurrent]=(1)` accepté tel quel | `EXECUTED` |
| Upsert atomique | ✅ `ON CONFLICT DO NOTHING` natif — 10/10 | ✅ `WHERE NOT EXISTS` + `UPDLOCK, HOLDLOCK` — 10/10 | `EXECUTED` |
| Erreurs classifiables | 6/8 — `ConstraintName` **structuré** | 7/8 — nom de contrainte **par parsing de message** | `EXECUTED` |
| Isolation | `read committed` (observée) | `ReadCommitted` (observée) | `EXECUTED` |
| Transactions | ✅ ; ⚠️ **transaction avortée après erreur (`25P02`)** | ✅ ; transaction réutilisable après erreur | `EXECUTED` |
| Multi-processus | ✅ 3 scénarios, PID distincts | ✅ 3 scénarios, PID distincts | `EXECUTED` |
| Sauvegarde/restauration | ✅ `pg_dump`/`pg_restore` — réconciliation identique | ✅ `BACKUP`/`RESTORE` — réconciliation identique | `EXECUTED` (moteur) |
| Installation Windows | Service natif **observé** (non installé par le spike) | **Installation tentée et ÉCHOUÉE** (§19.1) | `NOT_TESTED` |
| Administration | `psql` présent ; pas d'ordonnanceur intégré | `sqlcmd` présent ; **SQL Server Agent absent en Express** | `OFFICIAL_DOCUMENTATION` |
| Import SQLite réduit | ✅ réconciliation complète, PK conservées, séquences resynchronisées | ✅ idem ; `IDENTITY_INSERT` conditionnel requis | `EXECUTED` (prototype réduit) |
| Deadlock | ✅ `40P01` ; transaction victime **perdue** (`25P02`) ; 933 ms | ✅ `1205` ; transaction victime **encore utilisable** ; 1 295 ms | `EXECUTED` |
| Timeouts | ⚠️ client et verrou **sans `SqlState`** — non distinguables par code | ⚠️ client et verrou tous deux `-2` ; annulation `Number=0` | `EXECUTED` |
| Perte de connexion / redémarrage | ✅ aucune écriture partielle ; reprise **0,5 s** | ✅ aucune écriture partielle ; reprise **3,1 s** | `EXECUTED` |
| Reprise par retry borné (5 essais) | ✅ **2 tentatives** | ⚠️ **5 tentatives** (budget entier) ; code **`10054`** distinct de `10061` | `EXECUTED` (harness) |
| Blocage des lecteurs | jamais bloqués (MVCC natif) | **bloqués par défaut** (RCSI OFF) ; non bloquants avec RCSI ON | `EXECUTED` |
| Limites / licence vérifiées | **PostgreSQL License**, sans redevance, aucune limite produit ; v17 supportée jusqu'au **2029-11-08** | **Gratuit** ; **10 Go/base**, **1 410 Mo buffer pool**, **1 socket ou 4 cœurs** | `OFFICIAL_DOCUMENTATION` |

**Aucune note globale n'est attribuée. Aucun provider n'est désigné.**

---

## 26. Candidats pour l'ADR

Critères du brief §26 :

| Critère requis | PostgreSQL | SQL Server Express |
|---|---|---|
| Instance réelle testée | ✅ | ✅ |
| Provider EF réel | ✅ | ✅ |
| Mapping monétaire exact | ✅ *(prouvé avec un mapping décimal)* | ✅ *(idem)* |
| Index filtrés/partiels prouvés | ✅ | ✅ |
| Upsert concurrent prouvé | ✅ | ✅ |
| Erreurs identifiables | ✅ | ✅ |
| Transactions exécutées | ✅ | ✅ |
| ≥ 1 scénario multi-processus | ✅ (3) | ✅ (3) |
| Sauvegarde/restauration exécutée | ✅ | ✅ *(moteur uniquement)* |
| **Installation Windows documentée ET testée** | ❌ | ❌ |
| Limites/licence vérifiées officiellement | ✅ | ✅ |

**Conclusion :**

**Le seul critère manquant du gate minimal « CANDIDATE READY FOR ADR » défini par le protocole initial est
l'installation Windows documentée et testée.** Il manque pour **les deux** providers.

**Le spike complet reste néanmoins incomplet tant que les primitives restantes, l'import SQLite, les pannes,
les deadlocks, les retries et les tests réseau ne sont pas exécutés.** Le lot B a traité tout ce qui pouvait
l'être sur conteneurs jetables — primitives, deadlocks, timeouts, pannes, retries, idempotence, RCSI, import
SQLite réduit — **mais pas** les tests réseau depuis un autre poste, qui restent à faire.

- **PostgreSQL — PAS ENCORE `CANDIDATE READY FOR ADR`** : installation Windows non testée ; accès depuis un
  autre poste non testé.
- **SQL Server Express — PAS ENCORE `CANDIDATE READY FOR ADR`** : installation Windows **tentée et échouée**,
  cause racine non déterminée ; accès depuis un autre poste non testé ; preuves d'exploitation (service,
  pare-feu, planification) manquantes. Le **moteur** Express est techniquement prêt ; **le candidat
  opérationnel ne l'est pas.**

**Aucun provider n'est choisi. Aucun n'est éliminé.**

---

## 27. Fichiers créés

### 27.1 Dans le dépôt

```
docs/implementation/P4-1-provider-comparison-spike-report.md   (ce rapport)

spikes/P4.ProviderComparison/MMV.P4.ProviderComparison.csproj
spikes/P4.ProviderComparison/Support/SpikeEnvironment.cs
spikes/P4.ProviderComparison/Support/SpikeDatabase.cs
spikes/P4.ProviderComparison/Support/AdaptedOpticDbContext.cs
spikes/P4.ProviderComparison/Support/SpikeModelCacheKeyFactory.cs
spikes/P4.ProviderComparison/Support/ErrorFacts.cs
spikes/P4.ProviderComparison/Support/SpikeLog.cs
spikes/P4.ProviderComparison/E1_ModelCreationTests.cs
spikes/P4.ProviderComparison/E1b_AdaptedModelTests.cs
spikes/P4.ProviderComparison/E2_MoneyPrecisionTests.cs
spikes/P4.ProviderComparison/E3_E4_FilteredIndexTests.cs
spikes/P4.ProviderComparison/E5_UpsertConcurrencyTests.cs
spikes/P4.ProviderComparison/E6_ErrorClassificationTests.cs
spikes/P4.ProviderComparison/E7_E8_TransactionsAndPrimitivesTests.cs
spikes/P4.ProviderComparison/E9_MultiProcessTests.cs
spikes/P4.ProviderComparison/E10_BackupRestoreTests.cs
spikes/P4.ProviderComparison/Worker/MMV.P4.Worker.csproj
spikes/P4.ProviderComparison/Worker/Program.cs
```

Ajoutés par le **lot B** :

```
docs/implementation/P4-1-technical-completion-lot-b-report.md

spikes/P4.ProviderComparison/E11_RemainingPrimitivesTests.cs
spikes/P4.ProviderComparison/E12_DeadlockTests.cs
spikes/P4.ProviderComparison/E13_TimeoutTests.cs
spikes/P4.ProviderComparison/E14_ConnectionLossTests.cs
spikes/P4.ProviderComparison/E15_RetryAndIdempotenceTests.cs
spikes/P4.ProviderComparison/E16_ReadCommittedSnapshotTests.cs
spikes/P4.ProviderComparison/E17_SqliteImportTests.cs
spikes/P4.ProviderComparison/Support/SpikeConnections.cs
spikes/P4.ProviderComparison/Support/DockerControl.cs
spikes/P4.ProviderComparison/Support/SpikeRetryPolicy.cs
spikes/P4.ProviderComparison/Support/SpikeSerialCollection.cs
```

**Aucun autre fichier du dépôt n'est créé ou modifié.**

### 27.2 Hors dépôt (session uniquement)

Script de lancement, mots de passe générés, journaux de preuves et média d'installation résident dans le
répertoire temporaire de session. **Rien de tout cela n'est dans le dépôt.**

### 27.3 Nettoyage à effectuer

| Élément | État final |
|---|---|
| Bases jetables par expérimentation | ✅ **supprimées automatiquement** en fin de chaque test |
| Conteneur `mmv-p4-pg` + volume | ✅ **supprimé** (`docker rm -f -v`) |
| Conteneur `mmv-p4-mssql` + volume | ✅ **supprimé** (`docker rm -f -v`) |
| Vérification | ✅ `docker ps -a --filter name=mmv-p4` → **aucun conteneur** |
| Fichiers de mots de passe temporaires | ✅ **supprimés** (0 fichier restant) |
| Instance LocalDB créée par la sonde | ✅ **supprimée** (`sqllocaldb delete MSSQLLocalDB` → *deleted*), état antérieur restauré |
| Instance PostgreSQL native 17.4 | ✅ **jamais contactée, jamais modifiée** |
| Images `postgres:17`, `mcr.microsoft.com/mssql/server:2022-latest` | conservées en cache Docker (~3 Go) — suppression possible sur demande |
| Média SQL Server Express (753 Mo + extraction) | conservé dans le **répertoire temporaire de session** — à supprimer si l'installation native n'est pas reprise |

---

### 27.4 Commandes reproductibles (sans secret)

Provisionnement (les mots de passe sont générés aléatoirement et fournis par variables d'environnement) :

```bash
# PostgreSQL — conteneur jetable, localhost uniquement
docker run -d --name mmv-p4-pg \
  -e POSTGRES_PASSWORD="$PG_PW" -e POSTGRES_DB=postgres \
  -p 127.0.0.1:5433:5432 postgres:17
docker exec -u postgres mmv-p4-pg psql -c "CREATE ROLE mmv_spike LOGIN PASSWORD '…';"
docker exec -u postgres mmv-p4-pg psql -c "ALTER ROLE mmv_spike CREATEDB;"
docker exec -u postgres mmv-p4-pg psql -c "CREATE DATABASE mmv_p4_spike OWNER mmv_spike;"

# SQL Server Express — conteneur jetable, localhost uniquement
docker run -d --name mmv-p4-mssql \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$SA_PW" -e MSSQL_PID=Express \
  -p 127.0.0.1:14333:1433 mcr.microsoft.com/mssql/server:2022-latest
```

Variables attendues par le harness (jamais committées) :

```
MMV_P4_POSTGRES_CONNECTION  = Host=localhost;Port=5433;Database=mmv_p4_spike;Username=mmv_spike;Password=…
MMV_P4_SQLSERVER_CONNECTION = Server=127.0.0.1,14333;Database=mmv_p4_spike;User Id=sa;Password=…;
                              TrustServerCertificate=True;Encrypt=False;Connect Timeout=60
MMV_P4_SA_PASSWORD          = …            (E10 uniquement)
MMV_P4_SPIKE_LOG_DIR        = <hors dépôt> (journal de preuves)
```

> ⚠️ `Server=127.0.0.1,…` et **non** `localhost` : voir §7.1.

Exécution et nettoyage :

```bash
dotnet restore spikes/P4.ProviderComparison/MMV.P4.ProviderComparison.csproj
dotnet build   spikes/P4.ProviderComparison/Worker/MMV.P4.Worker.csproj -c Debug      # requis par E9
dotnet build   spikes/P4.ProviderComparison/MMV.P4.ProviderComparison.csproj --no-restore -c Debug
dotnet test    spikes/P4.ProviderComparison/MMV.P4.ProviderComparison.csproj --no-build -c Debug

docker rm -f -v mmv-p4-pg mmv-p4-mssql
```

---

## 28. État Git

```
git status --short
 ?? design-handoff/      (préexistant, hors périmètre)
 ?? design/              (préexistant, hors périmètre)
 ?? docs/ui/             (préexistant, hors périmètre)
 ?? docs/implementation/P4-1-provider-comparison-spike-report.md
 ?? docs/implementation/P4-1-technical-completion-lot-b-report.md   (lot B)
 ?? spikes/

git diff --check   → propre
git diff --stat    → aucun fichier SUIVI modifié
```

- **Aucun fichier suivi n'est modifié** : les ajouts du spike sont entièrement **non suivis**.
- **Aucun commit. Aucun push.**
- Interdits respectés : `src/**`, `tests/**`, `Migrations/**`, `MMV.App/**`, `.github/**`, `MMV.sln`,
  tout `*.csproj` hors `spikes/P4.ProviderComparison/**`, `docs/ui/**`, `design/**`, `design-handoff/**`.

---

## 29. Verdict

```
P4-1 SPIKE = INCOMPLETE
P4-1       = NOT READY FOR ADR
```

**Le seul critère manquant du gate minimal « CANDIDATE READY FOR ADR » défini par le protocole initial est
l'installation Windows documentée et testée.** Il manque des deux côtés :

- l'installation native de **SQL Server Express** **a été tentée avec élévation, mais elle a échoué avant la
  création d'une instance ou d'un service exploitable** ; la **cause racine n'est pas déterminée** (§19.1) ;
- l'installation native de **PostgreSQL** n'a pas été réalisée par le spike, l'instance 17.4 présente sur le poste
  étant **interdite d'usage**.

**Le spike complet reste néanmoins incomplet tant que les primitives restantes, l'import SQLite, les pannes,
les deadlocks, les retries et les tests réseau ne sont pas exécutés.** Le lot B en a exécuté la quasi-totalité ;
les **tests réseau depuis un autre poste** restent à faire.

**Preuves exécutées (lot A)** : E1, E1b, E2, E3, E4, E5, E6, E7, E8, E9 (multi-processus, 3 scénarios),
E10 (sauvegarde/restauration) — **sur les deux providers**, avec versions et éditions relevées par requête serveur.

**Preuves exécutées (lot B)** : E11 (primitives restantes, implémentations de production), E12 (deadlock réel),
E13 (timeouts), E14 (perte de connexion, redémarrage, reconnexion), E15 (retry borné + idempotence),
E16 (RCSI OFF et ON), E17 (import SQLite réduit) — **sur les deux providers**.

**Preuves manquantes** : installation Windows native (les deux) · pare-feu et accès **depuis un autre poste** ·
comportement du moteur SQL Server **sous Windows** · volumétrie réelle et import à l'échelle · performance.

**Un point reste en échec, non contourné** : `NotificationRepository.TryCreateActiveLowStockAsync` ne s'exécute
sur **aucun** des deux providers (paramètres `SqliteParameter`) — sa garantie serveur n'est donc **pas prouvée**.

**Prochaine action minimale — décision utilisateur requise.** L'installation native de SQL Server Express a
échoué (`0x84C4000E`) **sans produire de journal exploitable** ; sa **cause racine n'est pas déterminée**
(§19.1). **Aucune désinstallation n'est recommandée.** Voies possibles, sans préconisation :

1. rejouer l'installation **en collectant un journal** (`/LOG`), afin d'établir une cause racine réelle ;
2. réaliser l'installation Windows d'Express sur une **machine de laboratoire dédiée** (laisse ce poste intact) ;
3. installer PostgreSQL en Windows natif sur une machine de laboratoire, pour combler le même critère de son côté ;
4. traiter d'abord le volet **réseau** (accès depuis un autre poste, pare-feu), indépendant de ce blocage.

Les travaux **non bloqués** identifiés à la rédaction initiale — primitives restantes et prototype d'import
SQLite — **ont été exécutés par le lot B**
([P4-1-technical-completion-lot-b-report.md](P4-1-technical-completion-lot-b-report.md)).

**Confirmations finales :**

- ✅ aucun provider n'est choisi ;
- ✅ aucun score global n'est attribué ;
- ✅ aucune donnée utilisateur n'a été lue, écrite ou exposée ;
- ✅ aucun code, test, migration, snapshot EF, UI ou workflow de production n'est modifié ;
- ✅ aucun secret n'apparaît dans le dépôt ni dans ce rapport ;
- ✅ aucun commit, aucun push.
---

## 30. Enregistrement du commit et de la CI (Lots A + B)

Les preuves des Lots A et B — ce rapport, le
[rapport Lot B](P4-1-technical-completion-lot-b-report.md) et `spikes/P4.ProviderComparison/**` — sont
enregistrées **sans choix de provider** :

| Élément | Valeur |
|---|---|
| Commit | **`0039e5c410f85937a3eb70f29f2dd5545f285f1d`** — `test(P4-1): capture provider spike evidence` |
| Fichiers | **32** — `M` roadmap P4 · `A` 2 rapports P4-1 · `A` `spikes/P4.ProviderComparison/**` (29 fichiers) ; **aucun** `src/**`, `tests/**`, `Migrations/**`, UI, `.github/**`, `MMV.sln` |
| CI | run **`30128760083`** · `event=push` · `headSha` **exact** · `status=completed` · **`conclusion=success`** |
| URL | <https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30128760083> |
| Job | *Restore / Build / Test / Scan* ✅ — Restore · Build · Test · Audit vulnérabilités · Contrôle EF, tous verts |
| Baseline | **1499** (Domain 649 · Application 611 · App 239), 0 échec, 0 ignoré — reproduits en CI |
| Harness | validé **localement** sans provider (45 tests *Skipped*, aucun secret) ; **hors `MMV.sln`**, non couvert par la CI |
| Production | **aucune modification** |

```
P4-1 LOT A     = GO
P4-1 LOT B     = GO
P4-1 LOT B CI  = GO
P4-1 SPIKE     = INCOMPLETE
P4-2 ADR       = BLOCKED
P4-1 LOT C     = READY
```

**P4-1 reste incomplète** (installation Windows native et tests réseau réels non exécutés — Lot C requis) ;
**P4-2 (ADR) reste bloquée** ; **aucun provider n'est choisi ni éliminé.**
