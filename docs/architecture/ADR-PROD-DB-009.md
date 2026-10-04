# ADR-PROD-DB-009 — Application des migrations et compatibilité de version en multi-poste PostgreSQL

> **Statut : ACCEPTED — revue P4-6A close le 22 septembre 2026. AUCUNE implémentation.**
>
> **Mise à jour du 22 septembre 2026 — revue d'acceptation P4-6A.** La rédaction initiale n'arrêtait **aucun**
> choix ; la consolidation du même jour en avait tranché huit sur dix. La **revue de l'architecte ferme les
> trois derniers points** et emporte l'acceptation :
>
> | Condition | Question | Réponse de la revue |
> |---|---|---|
> | **AC-1** | **Q-3** — politique de compatibilité | **fenêtre limitée N-1**, avec discipline « étendre → migrer → contracter » (DP-3) |
> | **AC-2** | **Q-14** — journal des migrations serveur | **journal dédié**, table en base doublée d'une trace locale, champs minimaux fixés (DP-8) |
> | **AC-3** | **Q-2 / QD-14** — identité de l'opérateur | **modèle opérateur mixte** selon contrat ; rôle migrateur PostgreSQL obligatoire (DP-9) |
>
> **Les dix points de décision du §5.2 sont désormais tous arrêtés.** Le **§4 est conservé tel quel** : il reste
> la trace de l'instruction, et les options non retenues y sont lisibles. Les questions du §9.2 qui subsistent
> sont **des questions de mise en œuvre**, propriété de P4-6B, P4-8 ou P4-9 : aucune ne conditionne plus
> l'acceptation. Voir le rapport d'acceptation
> ([`docs/implementation/P4-6A-adr-acceptance-report.md`](../implementation/P4-6A-adr-acceptance-report.md))
> et, pour l'historique, le rapport de consolidation
> ([`docs/implementation/P4-6-architecture-consolidation-report.md`](../implementation/P4-6-architecture-consolidation-report.md)).
>
> Cet ADR prépare l'obligation **O8** d'[ADR-PROD-DB-002 §15](adr-prod-db-002-server-database-provider-selection.md) :
> **sérialiser** l'application des migrations sur la base PostgreSQL centrale et **bloquer proprement** un
> poste dont la version ne correspond pas au schéma. Il instruit les décisions reportées **D-10, D-11, D-16,
> D-17, D-18** ([P4-5E-B, *Deferred Decisions*](../implementation/P4-5E-B-architecture-decisions.md)). Il **ne
> modifie aucun fichier `.cs`, aucun test, aucune migration, aucune configuration et aucune CI.** Il n'est pas
> commité.
>
> Date : 22 septembre 2026. Branche : `p4-multi-poste`. HEAD de référence :
> `ccbeb3259444d26264e20323058764dd59318e1c` (= `origin/p4-multi-poste`). RECON-B-light est appliqué localement,
> **non commité**.
> Entrées : [ADR-PROD-DB-001 §6](adr-prod-db-001-multi-poste-database-strategy.md) ·
> [ADR-PROD-DB-002 §15](adr-prod-db-002-server-database-provider-selection.md) ·
> [ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md) ·
> [ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md) ·
> [ADR-PROD-DB-008](adr-prod-db-008-postgresql-integration-testing.md) ·
> [roadmap P4 §P4-6](P4-multi-poste-roadmap.md) ·
> [audit P4-5E-A](../implementation/P4-5E-A-postgresql-migration-architecture-audit-report.md) ·
> [décisions P4-5E-B](../implementation/P4-5E-B-architecture-decisions.md) ·
> [rapport de transition P4-6](../implementation/P4-6-transition-audit-report.md) ·
> [rapport RECON-B-light](../implementation/P4-RECON-B-light-report.md) · code à HEAD.
> **Le dépôt réel prime toujours sur ce document.**
>
> **Note de numérotation.** [adr-candidates.md](adr-candidates.md#adr-009) contient un « ADR-009 — Audit &
> données de santé ». C'est une autre série. Le présent document appartient à la série **PROD-DB** et ne le
> remplace pas.

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `CODE` | constaté par lecture du code à HEAD |
| `DOC` | énoncé d'un document du dépôt (ADR, roadmap, rapport) |
| `EF_BEHAVIOR — À CONFIRMER` | comportement documenté d'EF Core / Npgsql, **non exécuté ici** |
| `PG_BEHAVIOR — À CONFIRMER` | comportement documenté de PostgreSQL, **non exécuté ici** |
| `EXTERNE` | fait extérieur au dépôt, non vérifiable depuis le dépôt |
| `UNKNOWN` | absent du code et de la documentation : rien n'est inventé |

---

## 1. Status

**ACCEPTED — 22 septembre 2026, revue d'acceptation P4-6A.** — **Addendum P4-6B du 4 octobre 2026** (§11) : forme du verrou (session unique), calcul de la métadonnée (ancre = état physique vérifié), Q-6 … Q-24 fermées. Aucune décision du §5.2 rouverte.

Le §5 *Decision* contient deux choses :

1. les **contraintes déjà imposées** par des ADR acceptées, rappelées sans être rediscutées (§5.1) ;
2. les **dix points de décision**, tous arrêtés (§5.2).

**Conditions d'acceptation — toutes satisfaites.**

> *Les conditions d'acceptation sont numérotées **AC-n**. Ne pas les confondre avec les constats **A-1** et
> **A-2** du rapport RECON-B-light, cités par Q-18.*

| # | Condition | État | Réponse |
|---|---|---|---|
| **AC-1** | Réponse à **Q-3** : égalité stricte (VS-1) ou fenêtre de compatibilité | **SATISFAITE** | **fenêtre limitée N-1** + « étendre → migrer → contracter » (§5.2 DP-3) |
| **AC-2** | Réponse à **Q-14** : où vit le journal des migrations serveur (DP-8) | **SATISFAITE** | **journal dédié** : table en base, doublée d'une trace locale ; champs minimaux fixés (§5.2 DP-8) |
| **AC-3** | Réponse à **Q-2** (DR-6) : qui est l'opérateur autorisé | **SATISFAITE** | **modèle mixte selon contrat** — propriétaire du magasin ou support MMV ; rôle migrateur obligatoire (§5.2 DP-9) |

**Ce que l'acceptation autorise.** P4-6B est **spécifiable et écrivable** sur cette base, y compris sa garde de
version, qui était le seul élément encore suspendu. Son **préalable technique** demeure **P4-5F** : sans serveur
PostgreSQL en CI, ni le verrou ni la garde ne se prouvent (critères S-1 à S-4, §8.3). **L'acceptation ne remplace
pas cette dépendance.**

**Ce que l'acceptation ne fait pas.** Elle n'implémente rien, ne modifie aucun fichier `.cs`, aucune migration,
aucune CI. Les questions qui subsistent au §9.2 sont des **questions de mise en œuvre** — forme exacte du verrou,
stratégie de maintenance, noms des rôles, emplacement de la version — dont les propriétaires sont P4-6B, P4-8 et
P4-9. **Aucune ne rouvre une décision du §5.2.**

---

## 2. Context

### 2.1 Mandat

| Source | Exigence |
|---|---|
| [ADR-001 §6](adr-prod-db-001-multi-poste-database-strategy.md) | les postes sont mis à jour **un par un**. **Un seul poste ou processus** applique une migration à la fois. **Sauvegarde avant migration**, adaptée au serveur. Un **client trop ancien** face à un schéma **trop récent** est **bloqué**, avec un message clair et sans écriture hasardeuse |
| [ADR-002 O8](adr-prod-db-002-server-database-provider-selection.md) | sérialiser l'application des migrations en multi-poste **et** bloquer proprement un client trop ancien. Obligation bloquante, phase P4-6 |
| [ADR-005 §5.10, G7](adr-prod-db-005-migration-architecture.md) | un `Migrate()` par poste est **inadapté** à une base centrale. Le sujet appartient à P4-6 |
| [ADR-007 §5.7, S7](adr-prod-db-007-schema-drift-prevention.md) | dérive D-C : un client dont les migrations ne sont pas toutes appliquées, ou dont le schéma est plus récent que l'application, **doit être bloqué proprement** |
| [Roadmap §P4-6](P4-multi-poste-roadmap.md) | **autorisé** : verrou, poste désigné, phase de maintenance, garde de version. **Interdit** : migration automatique concurrente par tous les postes. **Tests** : migration pendant qu'un poste est connecté ; client obsolète bloqué proprement |
| [P4-5E-B](../implementation/P4-5E-B-architecture-decisions.md) | reportées vers P4-6 : **D-10** (rôles), **D-11** (qui applique la baseline), **D-16** (version de la base), **D-17** (numérotation de MMV), **D-18** (montée de version d'une base existante) |
| [P4-5D-R, D-B4 / RR6](../implementation/P4-5D-R-closure-report.md) | sauvegarde non atomique si un autre poste écrit pendant le premier démarrage. Rattaché à P4-6 |

### 2.2 État du code à HEAD

| Constat | Preuve |
|---|---|
| Tout démarrage PostgreSQL est **bloqué volontairement** par un garde-fou placé **avant** le `try`, pour qu'aucun `catch` générique ne le masque | `CODE` [App.axaml.cs:200-214](../../src/MMV.App/App.axaml.cs#L200-L214) |
| Ce garde-fou lève une exception dans `ConfigureServices`, appelée **avant** l'écran de connexion. Aucun message n'est affiché | `CODE` [App.axaml.cs:41-46](../../src/MMV.App/App.axaml.cs#L41-L46) |
| La branche PostgreSQL désigne déjà la chaîne de migrations dédiée. **Elle n'exécute rien** | `CODE` [DatabaseProviderResolver.cs:133-135](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L133-L135) |
| La chaîne PostgreSQL contient **une** migration, `20260922001219_InitialPostgreSqlBaseline`. Instantané en `ProductVersion 8.0.27` | `CODE` [Migrations/](../../src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/) |
| `MMV.App` référence l'assembly de migrations PostgreSQL. Elle est donc **déployée** avec l'application | `CODE` [MMV.App.csproj:51](../../src/MMV.App/MMV.App.csproj#L51) |
| Chemin SQLite : sauvegarde de fichier, détection d'état, puis `Migrate()` **par poste** | `CODE` [SqliteDatabaseManager.cs:89-95](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L89-L95), l. 316, l. 334 |
| La vérification post-préparation ne contrôle que les migrations **en attente**. Une migration appliquée mais **inconnue** de l'application n'est pas détectée (U-1) | `CODE` [SqliteDatabaseManager.cs:770](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L770) |
| **Aucun verrou** de migration : 0 occurrence de `pg_advisory`, `advisory` ou `MigrationLock` dans `src/` et `tests/` | `CODE` (`grep`) |
| **Aucune version explicite de MMV** : ni `<Version>` ni `AssemblyVersion` dans [Directory.Build.props](../../Directory.Build.props) ou dans les `.csproj`. Le SDK applique `1.0.0` par défaut. L'écran « À propos » affiche un `1.0.0` **écrit en dur** | `CODE` [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) |
| Le journal de migration est un **fichier local**, à côté du fichier SQLite | `CODE` [App.axaml.cs:222-224](../../src/MMV.App/App.axaml.cs#L222-L224) |
| Le seed tourne **sur chaque poste, à chaque démarrage**. Il crée l'administrateur bootstrap si le secret est positionné | `CODE` [App.axaml.cs:260](../../src/MMV.App/App.axaml.cs#L260) ; `DOC` P4-5E-A, constat (b) |
| `DatabaseMigrationException` est relancée **sans affichage**. Toute autre erreur de préparation est **avalée**, et l'application continue | `CODE` [App.axaml.cs:267-278](../../src/MMV.App/App.axaml.cs#L267-L278) |
| `EnsureCreated()` subsiste sur le chemin du seed de démonstration (contradiction C-1 avec ADR-007 §5.5) | `CODE` [DbInitializer.cs:19](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L19) |
| EF Core **8.0.27** et Npgsql EF **8.0.11**. EF Core 8 n'a **pas de verrou de migration natif**. EF Core 9 en introduit un | `CODE` [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj) ; `DOC` P4-5E-A U-2, `EF_BEHAVIOR — À CONFIRMER` |
| Seuls des **rôles de laboratoire** existent (`mmv_app`, `mmv_spike` avec `CREATEDB`). ADR-002 les qualifie de « raccourci jetable » | `DOC` [ADR-002 l. 963](adr-prod-db-002-server-database-provider-selection.md), [P4-1 Lot C](../implementation/P4-1-windows-network-lot-c-report.md) |
| MMV n'impose **aucun** `Application Name`, délai ni option à la chaîne de connexion Npgsql | `DOC` P4-5E-A, *Connection Configuration Review* |

### 2.3 Manques à combler (audit P4-5E-A)

| # | Manque | Traité ici ? |
|---|---|---|
| **U-1** | schéma plus récent que l'application : non détecté | **oui** (§4.3, §4.4) |
| **U-2** | aucune sérialisation des migrations | **oui** (§4.1, §4.2) |
| **U-3** | aucune sauvegarde serveur avant migration | principe seulement (K-5). Mise en œuvre : P4-9 |
| **U-4** | droits DDL au runtime | **oui** (§4.5) |
| **U-5** | fenêtre de versions mixtes ; verrous `ALTER TABLE` pendant que d'autres postes travaillent | **oui** (§4.2.4, §4.4) |
| **U-6** | échec partiel : base à une version intermédiaire | **oui** (§4.6.3) |
| **U-7** | aucun retour arrière : seule la restauration est réaliste | procédure seulement (§4.6.3). Outillage : P4-9 |

### 2.4 Dépendances

- **P4-5F** (tests d'intégration PostgreSQL, `NEXT`) : toute preuve serveur des mécanismes décrits ici en
  dépend ([ADR-008](adr-prod-db-008-postgresql-integration-testing.md)).
- **P4-5G** (levée du garde-fou et chaîne de préparation serveur) : **absorbé par P4-6C**. Depuis la
  consolidation du 22/09/2026, ce découpage **est inscrit** dans [la roadmap P4](P4-multi-poste-roadmap.md),
  §P4-6, avec les états P4-6A / P4-6B / P4-6C. L'inscription vit dans l'**arbre de travail** : le résidu **R-1**
  ne sera clos qu'au **commit** de la roadmap (Q-18).
- **P4-8** : création de la base (D-09), stockage des secrets (D-13), protection (D-14), politique d'échec au
  démarrage (D-15), premier administrateur (D-12).
- **P4-9** : sauvegarde et restauration serveur (O9).

### 2.5 Hors périmètre de cet ADR

Création et provisioning de la base, secrets, TLS, outillage de sauvegarde, import SQLite (P4-7), tests de
résilience généraux (P4-10). Également hors périmètre : les sept sujets multi-poste sans lot attribué (*lost
update*, invalidation de session, autorisation dans Application, fraîcheur des données, audit métier, niveau
d'isolation, recoupement avec la roadmap P5). Le rapport de transition P4-6 (§3.3) en demande le classement V1
en P4-6A. **Ce classement n'est pas fait ici** (Q-17).

---

## 3. Decision drivers

| # | Critère | Source |
|---|---|---|
| **DR-1** | **Intégrité** : jamais deux applications concurrentes du DDL sur la base centrale | ADR-001 §6 ; O8 |
| **DR-2** | **Aucun poste ne travaille sur un schéma incompatible** : blocage clair, sans écriture | ADR-001 §6 ; O8 ; ADR-007 §5.7 |
| **DR-3** | **Moindre privilège** : un modèle de permissions réel remplace le `db_owner` de laboratoire | O10 |
| **DR-4** | **Sauvegarde avant migration** | ADR-001 §6 ; O9 |
| **DR-5** | **Prouvabilité** : chaque garantie doit être testée sur un vrai serveur. Un mécanisme plus simple se prouve plus facilement | ADR-008 ; critères S-1 … S-7 du rapport de transition |
| **DR-6** | **Exploitabilité en magasin**. Le profil de la personne qui installe et met à jour MMV (gérant, technicien, prestataire) est **`UNKNOWN`** : aucun document ne le définit. Ce critère pèse lourdement sur §4.1 | aucun document |
| **DR-7** | **Fenêtre de versions mixtes** : les postes sont mis à jour un par un. Un écart de version entre postes est donc **l'état normal** pendant un déploiement, pas une exception | ADR-001 §6 ; P4-5E-A U-1 |
| **DR-8** | **Non-régression SQLite** : les 14 migrations et le cycle de vie SQLite restent inchangés | ADR-005 G4 ; critère S-5 |
| **DR-9** | **Provider-neutralité** de Domain et Application | O14 |
| **DR-10** | **Coût V1** : nombre d'artefacts nouveaux. UI minimale seulement | roadmap §3 |
| **DR-11** | **Récupération** : après un échec, retour à un état sûr. Le seul retour arrière réaliste est la restauration | P4-5E-A U-6, U-7 |
| **DR-12** | **Visibilité** : un échec ou un blocage doit être vu, jamais avalé | P4-5E-A R-E8 ; critère S-6 |

---

## 4. Options considered

> **Comment lire ce paragraphe après l'acceptation.** Le §4 est **conservé tel quel** : il est le registre des
> options examinées, et sa valeur est de montrer ce qui a été comparé et écarté. Il n'est **pas** l'énoncé des
> décisions — celui-ci est au **§5.2**. Deux conséquences de lecture :
>
> - là où le §4 écrit qu'un point « reste ouvert » (par exemple l'état **E3** du §4.4.1), lire le §5.2 : **les dix
>   points sont arrêtés** depuis la revue du 22/09/2026 ;
> - les formes conditionnelles « si VS-2 » sont **résolues** par DP-3 : la **fenêtre** est retenue, mais la
>   métadonnée qui la porte vit **hors du modèle EF** et n'est écrite que par `MMV.DatabaseManager` (§5.2.3.5).
>   Elle n'est donc **pas** la « table de version de schéma MMV » du §4.3 telle que ce paragraphe l'envisageait.
>
> Les **déroulés opérationnels du §4.6 sont, eux, mis à jour** : ce sont des procédures destinées à être suivies,
> non des options.

### 4.1 Autorité de migration (D-11, D-18)

> **Déjà exclu :** chaque poste appelle `Migrate()` au démarrage, sans coordination. C'est le comportement
> SQLite actuel transposé au serveur. ADR-005 §5.10 et la roadmap P4-6 l'excluent (voir §7, RA-1).

#### MA-A — L'application exécute les migrations automatiquement au démarrage

**Variantes**

- **MA-A1** — n'importe quel poste, **sous verrou**. Le premier qui obtient le verrou migre. Les autres
  attendent, puis revérifient.
- **MA-A2** — un **poste désigné** par configuration migre. Les autres ne font que vérifier.

| Axe | Analyse |
|---|---|
| **Avantages** | aucune étape manuelle ; un seul binaire à distribuer ; proche du comportement SQLite déjà connu et testé ; la baseline s'applique d'elle-même sur une base vide (D-11) |
| **Inconvénients** | la migration part quand **le premier poste mis à jour** démarre, éventuellement en pleine journée (U-5) ; un verrou est obligatoire (§4.2) ; la sauvegarde doit être lancée **depuis un poste** (`pg_dump` exige outillage et droits sur chaque poste, U-3) ; le démarrage devient long et faillible pour l'utilisateur qui déclenche la migration ; la concurrence au démarrage est la plus coûteuse à prouver (critère S-1). MA-A2 : si le poste désigné est éteint, tous les autres restent bloqués |
| **Impact sécurité** | le rôle utilisé par l'application doit **pouvoir modifier le schéma**. MA-A1 : **chaque poste** détient un identifiant capable de DDL, et la compromission d'un seul poste donne le contrôle du schéma. Tension directe avec O10. MA-A2 : l'exposition est limitée à un poste, mais l'identifiant y reste **stocké en permanence** |
| **Impact opérationnel** | mise à jour « sans intervention », mais **moment non maîtrisé**. La règle « tous les postes fermés » (D-B4) ne peut rester qu'une procédure. En cas d'échec, c'est un utilisateur de comptoir qui voit l'erreur |
| **Impact V1** | pas d'artefact nouveau, mais des mécanismes plus nombreux **dans l'application** : verrou, garde de version, sauvegarde depuis un poste, rôle DDL. Surface de test maximale (S-1, S-3, S-4) |

#### MA-B — Un outil de migration externe à l'application

**Variantes**

- **MA-B1** — **bundle de migrations EF** (`dotnet ef migrations bundle`), un exécutable autonome
  (`EF_BEHAVIOR — À CONFIRMER`).
- **MA-B2** — **console d'administration MMV dédiée** (nouveau projet). Elle appelle `Migrate()` et ajoute
  les gardes MMV : verrou, contrôle de sauvegarde, journal, vérifications post-migration. Elle peut construire
  le contexte par `DatabaseProviderResolver.Configure`, donc avec des variables **runtime** et non
  design-time.
- **MA-B3** — **script SQL idempotent** généré par `dotnet ef migrations script --idempotent`, exécuté par
  `psql`. **Contraint** : P4-5E-B D-03.7 interdit d'exécuter le script de la baseline (voir K-7). Pour les
  migrations suivantes, aucun document ne tranche (Q-7).

| Axe | Analyse |
|---|---|
| **Avantages** | l'application **ne fait jamais de DDL**. U-2 et U-4 sont résolus **par construction** côté poste (rapport de transition §2.5) ; le poste se réduit à une garde de version **en lecture seule** ; le moment de la migration est **choisi** ; la sauvegarde s'insère naturellement avant l'exécution (D-18 (c)) ; c'est la voie la plus simple à prouver |
| **Inconvénients** | un **artefact de plus** à construire, versionner, distribuer et documenter ; il faut quelqu'un pour le lancer (DR-6, `UNKNOWN`) ; tant qu'il n'a pas tourné, **tous** les postes mis à jour sont bloqués. MA-B1 : le bundle crée le contexte par la factory design-time. La branche PostgreSQL de cette factory dépend de `MMV_DESIGNTIME_DATABASE_PROVIDER` et d'une chaîne factice (P4-5E-B D-05). L'utiliser en production mélangerait design-time et exploitation (Q-8, `EF_BEHAVIOR — À CONFIRMER`) |
| **Impact sécurité** | l'identifiant DDL n'est utilisé **qu'au moment de la migration**, par l'opérateur, et peut n'être stocké sur aucun poste. Les postes reçoivent un rôle **DML seulement**. C'est le mieux aligné sur O10 |
| **Impact opérationnel** | une **procédure** de mise à jour en deux temps : la base, puis les postes. Fenêtre de maintenance explicite. Documentation opérateur obligatoire (critère P4 n° 13) |
| **Impact V1** | un livrable nouveau (MA-B2 : un projet dans `MMV.sln`, couvert par les tests d'architecture), plus une documentation. En échange, le code applicatif P4-6 est **plus petit** : ni verrou ni DDL côté poste. Pour une console, aucun design n'est requis (rapport de transition §4) |

#### MA-C — Approche hybride

**Variantes**

- **MA-C1** — **même binaire, mode d'administration explicite**. Le démarrage normal ne fait que vérifier.
  La migration n'est lancée que sur une action explicite (option de ligne de commande, ou écran dédié), avec un
  identifiant migrateur **fourni à ce moment-là**.
- **MA-C2** — l'application applique **la baseline seule** sur une base **vide** (installation). Les montées de
  version passent par un outil (MA-B).
- **MA-C3** — l'application applique automatiquement les migrations classées **compatibles** (additives).
  Les autres passent par un outil. Cela exige une **classification** de chaque migration.

| Axe | Analyse |
|---|---|
| **Avantages** | MA-C1 : un seul binaire à distribuer, avec un moment de migration maîtrisé. MA-C2 : installation simple, puis montées de version contrôlées. MA-C3 : pas d'intervention pour les cas les plus fréquents |
| **Inconvénients** | deux chemins d'exécution à tester. MA-C1 : le code DDL vit dans l'application, même s'il est rarement emprunté ; un verrou reste nécessaire, car rien n'empêche deux postes de lancer le mode d'administration. MA-C2 : la première installation exige un identifiant DDL sur un poste, au moins temporairement. MA-C3 : la classification repose sur la discipline du développeur, et une erreur de classement réintroduit exactement le risque que O8 interdit ; c'est la variante la plus complexe |
| **Impact sécurité** | MA-C1 : équivalent à MA-B **si** l'identifiant migrateur est saisi et jamais stocké ; équivalent à MA-A2 s'il est stocké. MA-C2 et MA-C3 : un identifiant DDL sur les postes, au moins pour un temps |
| **Impact opérationnel** | MA-C1 : procédure proche de MA-B, sans outil séparé. MA-C2 : installation sans outil, montées de version avec outil. MA-C3 : comportement difficile à expliquer à un opérateur (« parfois automatique ») |
| **Impact V1** | MA-C1 : UI ou option de ligne de commande supplémentaire, autorisée par la roadmap §3 (« une opération de migration clairement décidée »). MA-C3 : hors de portée raisonnable en V1 (**avis de lecture, non décision**) |

#### Synthèse

| | MA-A1 | MA-A2 | MA-B1 | MA-B2 | MA-C1 | MA-C2 |
|---|---|---|---|---|---|---|
| DDL depuis l'application | oui | oui (1 poste) | non | non | oui (mode admin) | oui (installation) |
| Identifiant DDL stocké sur les postes | tous | un | aucun | aucun | aucun si saisi | un, temporairement |
| Verrou indispensable | **oui** | recommandé | défensif | défensif | **oui** | **oui** à l'installation |
| Moment de migration maîtrisé | non | partiel | oui | oui | oui | oui (montées) |
| Artefact nouveau | non | non | bundle | projet | non | outil |
| Sauvegarde préalable intégrable | difficile | difficile | procédure | **dans l'outil** | dans le mode admin | outil |
| Charge de preuve (S-1) | forte | moyenne | faible | faible | moyenne | moyenne |

### 4.2 Verrouillage des migrations PostgreSQL

#### 4.2.1 Mécanismes

**LK-1 — Verrou consultatif PostgreSQL de session** (`pg_advisory_lock(clé)`, `pg_try_advisory_lock`,
`pg_advisory_unlock`) — `PG_BEHAVIOR — À CONFIRMER`.

- La clé est un entier 64 bits constant, propre à MMV. Le verrou n'est lié à **aucune table** : il fonctionne
  sur une base **vide**, avant la baseline.
- Il est libéré explicitement, ou **automatiquement à la fin de la session** serveur.
- Les migrations EF avec Npgsql s'exécutent **une transaction par migration** (P4-5E-A U-6,
  `EF_BEHAVIOR — À CONFIRMER`). Le verrou doit donc être tenu au **niveau session**, sur une connexion ouverte
  pendant tout le `Migrate()`, et utilisée par celui-ci (`EF_BEHAVIOR — À CONFIRMER`).
- **Pool de connexions** : une connexion rendue au pool doit être réinitialisée. Npgsql le ferait par défaut à
  la restitution (`EF_BEHAVIOR — À CONFIRMER`). Sinon, un verrou oublié survivrait dans une connexion du pool.
- **Coupure réseau** : si le poste disparaît sans fermer sa connexion, le serveur ne libère le verrou qu'après
  avoir détecté la session morte (délais *keepalive* TCP). Le délai réel est `UNKNOWN` pour la configuration
  cible.

**LK-2 — Verrou consultatif de transaction** (`pg_advisory_xact_lock`), libéré à la fin de la transaction
(`PG_BEHAVIOR — À CONFIRMER`). Il ne couvre **qu'une** migration, puisque EF ouvre une transaction par
migration. Il ne sérialise donc pas une montée de version en plusieurs migrations. Ce n'est pas un
substitut à LK-1.

**LK-3 — Table de verrou applicative** : par exemple une ligne unique qui porte le détenteur, l'horodatage et un
battement de cœur (*heartbeat*).

- Elle doit **exister avant** la migration qu'elle protège. Sur une base vide, elle ne peut donc pas être
  créée par la chaîne EF qu'elle doit sérialiser. Il faut un DDL **hors chaîne**, ce que K-7 et ADR-005 §5.4
  rendent délicat, ou bien accepter que l'installation ne soit pas protégée.
- Si le processus plante, la ligne **reste** : il faut une expiration (TTL), fondée sur l'heure du **serveur**
  (`now()`) et non sur celle du poste (ADR-004 ; T9, NTP), ou une libération manuelle.
- Le variant « verrou de ligne tenu par `SELECT … FOR UPDATE` » exige une transaction englobante. C'est a priori
  incompatible avec les transactions par migration d'EF (`EF_BEHAVIOR — À CONFIRMER`).
- Si la table entre dans le modèle EF, c'est un changement de modèle : règle de double migration, contrôles de
  dérive, et une table sans objet côté SQLite.

**LK-4 — `LOCK TABLE "__EFMigrationsHistory"`** (variante de LK-3 sur la table d'historique). Mêmes limites :
la table n'existe pas sur une base vide, et il faut une transaction englobante. Citée pour mémoire.

**LK-5 — Verrou natif d'EF Core ≥ 9.** Il exige une montée de version d'EF Core et de Npgsql, hors du périmètre
de P4-6 tel que documenté. Son implémentation par Npgsql est `UNKNOWN` ici (Q-6).

**LK-6 — Orchestration externe (procédure)** : aucune garde technique. **Un seul acteur**, désigné par
procédure, lance la migration pendant une fenêtre de maintenance, tous les postes fermés. C'est la forme
actuelle de D-B4 pour le premier démarrage (P4-5D-R).

#### 4.2.2 Comparaison

| Critère | LK-1 consultatif session | LK-3 table de verrou | LK-6 orchestration externe |
|---|---|---|---|
| **Plantage du migrateur** | libéré à la fin de la session. Délai de détection d'une session morte : `UNKNOWN` | **verrou orphelin** jusqu'à expiration ou intervention manuelle | sans objet : la reprise est humaine |
| **Concurrence** | exclusion garantie par le serveur, attente ou échec immédiat au choix (`try`) | exclusion garantie **si** l'acquisition est atomique (`INSERT` ou `UPDATE` conditionnel, même motif que les 14 primitives) | **non garantie** : repose sur la discipline |
| **Base vide** | fonctionne | ne fonctionne pas sans DDL hors chaîne | fonctionne |
| **Complexité** | faible : quelques appels SQL bruts sur une connexion tenue ouverte | moyenne : schéma, expiration, horloge serveur, nettoyage | faible en code, **élevée en procédure** |
| **Maintenance** | constante de clé à ne jamais changer ; dépend du comportement du pool (`À CONFIRMER`) | table à faire vivre sur deux chaînes, ou hors modèle | documentation et formation de l'opérateur |
| **Dépendance au provider** | spécifique PostgreSQL, confinée à Infrastructure (O14) | portable | aucune |
| **Observabilité** | `pg_locks` (droits de lecture : `À CONFIRMER`) | lisible par une simple requête | journal de procédure |

#### 4.2.3 Nécessité du verrou selon l'autorité

| Autorité | Verrou |
|---|---|
| MA-A1 | **indispensable** : plusieurs postes peuvent migrer |
| MA-A2, MA-C1, MA-C2 | **nécessaire** : rien n'empêche techniquement deux lancements |
| MA-B1, MA-B2 | **défensif** : deux opérateurs, ou deux exécutions de l'outil. Question A-3 du rapport de transition |

#### 4.2.4 Verrou entre le migrateur et les postes connectés (U-5, D-B4)

Le verrou de migration sérialise **les migrateurs entre eux**. Il n'empêche **pas** des postes déjà connectés de
travailler pendant la migration. Un `ALTER TABLE` prend un verrou exclusif sur la table ; en attendant, il
bloque aussi les requêtes suivantes sur cette table (`PG_BEHAVIOR — À CONFIRMER`, P4-5E-A U-5). Options :

| # | Option | Limites |
|---|---|---|
| **CX-1** | aucune garde : délais de verrou côté migrateur (`lock_timeout`, `PG_BEHAVIOR — À CONFIRMER`) et échec propre si le délai est dépassé | les postes subissent des attentes ; le schéma change sous un client déjà démarré |
| **CX-2** | verrou consultatif **partagé** tenu par chaque poste pendant toute sa session (`pg_advisory_lock_shared`). Le migrateur prend le verrou **exclusif** et attend donc que tous les postes soient fermés | chaque poste doit tenir une **connexion dédiée** hors du pool ; un poste planté retient le verrou jusqu'à la détection de sa session morte (`À CONFIRMER`) |
| **CX-3** | **indicateur de maintenance** en base : les postes refusent de démarrer, voire le relisent périodiquement | aucune revérification en cours de session n'existe aujourd'hui ; c'est un changement de modèle |
| **CX-4** | inventaire des sessions par `pg_stat_activity`, avec un `Application Name` imposé par MMV, avant de migrer | droits de lecture sur les sessions des autres rôles : `À CONFIRMER` ; MMV n'impose aujourd'hui aucun `Application Name` |
| **CX-5** | procédure « tous les postes fermés » (état actuel de D-B4) | non vérifiée techniquement |

### 4.3 Gestion de la version du schéma (D-16, D-17)

Notation : **Appliquées** = lignes de `__EFMigrationsHistory` ; **Connues** = migrations compilées dans
l'assembly de la chaîne du poste.

#### VS-1 — Historique EF seul

Règle : si Appliquées = Connues, le poste démarre. Si Appliquées ⊂ Connues, des migrations sont **en attente**
(poste plus récent). Si Appliquées ⊄ Connues, la base contient des migrations **inconnues** (poste plus ancien,
ou chaîne divergente).

- **Avantages** : aucune table ni source de vérité nouvelle ; aucun changement de modèle ; API EF existante
  (`GetAppliedMigrations`, `GetMigrations`) ; s'applique **aux deux chaînes**, y compris au rétrogradage d'un
  poste SQLite (U-1).
- **Inconvénients** : la compatibilité est une **égalité stricte**. Toute migration, même purement additive,
  bloque tous les postes non mis à jour. Cela impose une mise à jour **synchronisée** de tous les postes, ce qui
  contredit une montée progressive (DR-7). Les identifiants de migration ne sont pas des versions de MMV, et
  `ProductVersion` est la version **d'EF**. Le message affiché ne peut pas dire « installez la version X ».
- **Prérequis** : droit de lecture sur `__EFMigrationsHistory` pour le rôle applicatif.

#### VS-2 — Table de version de schéma MMV

Par exemple une ligne unique : version de schéma (entier croissant), **version minimale d'application
acceptée**, date et auteur de la dernière migration. Elle est tenue à jour par les migrations elles-mêmes, dans
la même transaction que le DDL (`EF_BEHAVIOR — À CONFIRMER`).

- **Avantages** : permet une **fenêtre de compatibilité**. Un poste plus ancien reste accepté si sa version est
  supérieure ou égale à la version minimale déclarée. Combinée à la discipline « étendre puis contracter »
  (D-16 (c)), elle autorise une montée progressive. Lisible par un humain et par un message d'erreur.
- **Inconvénients** : **seconde source de vérité**, qui peut diverger de l'historique si une migration oublie de
  la mettre à jour (un test dédié serait nécessaire). La version minimale est choisie par le développeur :
  **une erreur de sa part autorise un poste ancien sur un schéma incompatible**, soit exactement le risque que
  O8 interdit. Elle **exige D-17**. Emplacement à trancher : dans le modèle EF (deux chaînes, règle de double
  migration) ou hors modèle, en SQL PostgreSQL seul (invisible des contrôles de dérive). Elle ne peut pas
  naître dans la baseline, qui ne se retouche pas (D-03.2) : ce serait une migration ultérieure, sous
  l'exception « migration de données » d'ADR-005 §5.4.

#### VS-3 — Table des versions d'application

Un registre alimenté **par les postes** : identité du poste, version de MMV, dernière migration connue, dernier
passage en UTC serveur.

- **Avantages** : rend **visible** la fenêtre de versions mixtes ; permet au migrateur de **refuser** de migrer
  tant que des postes incompatibles sont actifs (lien avec CX-2 à CX-4) ; aide au diagnostic.
- **Inconvénients** : **ne décide pas seul** de la compatibilité et doit être combiné à VS-1 ou VS-2 ; écriture
  à chaque démarrage (droit DML) ; lignes périmées ; horloge serveur à utiliser ; changement de modèle (deux
  chaînes) ou table hors modèle ; exige D-17.

#### Scénarios

| Scénario | VS-1 historique seul | VS-2 table de version | VS-3 registre des postes (avec VS-1 ou VS-2) |
|---|---|---|---|
| **Poste ancien, base récente** (App 1.4, schéma 1.5) | Appliquées ⊄ Connues ⇒ **blocage** systématique | blocage **si** 1.4 < version minimale ; sinon le poste démarre | décision identique à VS-1 ou VS-2 ; le poste est **enregistré**, donc visible |
| **Poste récent, base ancienne** (App 1.5, schéma 1.4) | en attente ⇒ migration par l'autorité (§4.1), ou blocage | en attente ⇒ idem. La table de version dit en plus « de 1.4 vers 1.5 » | le migrateur voit quels postes 1.4 sont actifs avant de migrer |
| **Base vide** | aucune ligne ⇒ installation (D-11) ou blocage | table absente ⇒ idem | idem |
| **Chaîne divergente** (Appliquées et Connues incomparables) | blocage | blocage si la table le détecte ; sinon **risque** de faux accord | idem |
| **Montée progressive des postes** | **impossible** : tous les postes doivent être mis à jour ensemble | **possible** avec « étendre puis contracter » | **observable** |
| **Sources de vérité** | 1 | 2 (à maintenir cohérentes) | 1 ou 2, plus un registre |

### 4.4 Comportement des postes au démarrage

#### 4.4.1 États possibles

| # | État | Exemple | Nature de la réponse |
|---|---|---|---|
| **E1** | schéma = application | App 1.5, schéma 1.5 | démarrage normal |
| **E2** | application **plus récente** | App 1.5, schéma 1.4 | **ouvert** : SB-1, SB-2 ou SB-3 (§4.4.2) |
| **E3** | application **plus ancienne** | App 1.4, schéma 1.5 | **imposé** : blocage si incompatible (K-3). Seule la **définition** d'« incompatible » reste ouverte (VS-1 strict ou VS-2 fenêtre) |
| **E4** | base **vide** | installation | **ouvert** : qui applique la baseline (D-11) |
| **E5** | migration **en cours** | verrou détenu par un autre acteur | **ouvert** : attente bornée puis nouvelle vérification, ou blocage « maintenance en cours » |
| **E6** | serveur injoignable, ou préparation en échec | — | **D-15, P4-8**. Contrainte : jamais avalé (K-13) |
| **E7** | tables présentes **sans** `__EFMigrationsHistory` | base étrangère ou créée à la main | `UNKNOWN` : aucun document. L'adoption par suffixe est propre à SQLite ; l'historique PostgreSQL est neuf (ADR-005 §5.8). Q-12 |

#### 4.4.2 Stratégies pour E2 (App 1.5, schéma 1.4)

**SB-1 — Montée automatique.** Le poste obtient le verrou, sauvegarde si possible, migre, vérifie, puis
démarre. Les postes 1.5 suivants trouvent E1. Les postes restés en 1.4 passent en E3.

- *Prérequis* : MA-A ou MA-C2 ; rôle DDL sur les postes ; verrou.
- *Pour* : aucune intervention.
- *Contre* : moment non maîtrisé ; un poste 1.4 **déjà démarré** ne revérifie rien aujourd'hui (la garde ne
  s'exécute qu'au démarrage) et peut échouer en cours de vente (U-5) ; sauvegarde depuis un poste (U-3).

**SB-2 — Approbation par un administrateur.** Le poste détecte des migrations en attente, affiche « mise à jour
de la base requise » et n'exécute la migration qu'après une approbation explicite, avec un identifiant
migrateur fourni à ce moment-là.

- *Prérequis* : MA-C1 ; verrou ; écran ou option dédiés (autorisés par la roadmap §3).
- *Pour* : moment choisi ; une étape « sauvegarde faite ? » peut précéder l'exécution.
- *Contre* : saisie d'un secret dans l'application (P4-8). **Piège** : la garde s'exécute **avant**
  l'authentification ([App.axaml.cs:41-46](../../src/MMV.App/App.axaml.cs#L41-L46)). Une approbation par un
  utilisateur MMV de rôle Admin obligerait à lire `Users` sur un schéma **non encore vérifié**. De plus, un rôle
  applicatif MMV n'est **pas** un droit PostgreSQL.

**SB-3 — Écran de blocage.** Le poste affiche « la base doit être mise à jour par l'administrateur », puis se
ferme. Aucune migration depuis l'application.

- *Prérequis* : MA-B ; UI minimale.
- *Pour* : aucune voie DDL dans l'application ; le plus simple à prouver (critères S-2, S-4).
- *Contre* : dépend de la disponibilité de l'opérateur ; tous les postes mis à jour restent bloqués jusqu'au
  passage de l'outil.

#### 4.4.3 Points transverses

- **Aucune revérification après le démarrage** n'existe aujourd'hui. Un poste démarré avant une migration
  continue de travailler sur un schéma qui a changé. Faut-il une revérification périodique ou à la connexion
  (CX-3) ? Q-11.
- **Aujourd'hui, aucun blocage n'est visible** : les exceptions de démarrage ne produisent aucun message
  ([App.axaml.cs:205-214](../../src/MMV.App/App.axaml.cs#L205-L214), l. 267-278). Quelle que soit la
  stratégie, E2 à E5 exigent **un affichage**, soit trois états au minimum selon le rapport de transition
  (poste obsolète, maintenance en cours, poste non autorisé à migrer).

### 4.5 Modèle de permissions PostgreSQL (D-10)

#### 4.5.1 Rôles possibles

Privilèges indicatifs, tous `PG_BEHAVIOR — À CONFIRMER`.

| Rôle | Privilèges | Détenteur | Utilisé quand |
|---|---|---|---|
| **Administrateur** (superutilisateur, ou `CREATEDB` + `CREATEROLE`) | crée la base et les rôles, accorde les droits ; sauvegarde et restauration | installateur ou opérateur | installation, provisioning, reprise après sinistre. **Jamais** par l'application |
| **Migrateur / propriétaire** | propriétaire des objets du schéma ; `CREATE` sur `public`, qui n'est plus accordé à tous depuis PostgreSQL 15 (P4-5E-B D-08) ; privilèges par défaut accordés au rôle applicatif (`ALTER DEFAULT PRIVILEGES`) | opérateur (MA-B, MA-C1) ou postes (MA-A) | application des migrations |
| **Applicatif** | `CONNECT` ; `USAGE` sur `public` ; `SELECT`, `INSERT`, `UPDATE`, `DELETE` sur les tables ; lecture de `__EFMigrationsHistory` (garde de version) ; droits sur les séquences d'identité : `UNKNOWN` | chaque poste | DML au runtime, garde de version |

#### 4.5.2 Configurations

| # | Configuration | Conséquences pour V1 |
|---|---|---|
| **RL-1** | **un rôle propriétaire unique**, utilisé par tous les postes | le plus simple : un secret, compatible avec MA-A1. Mais chaque poste détient le DDL, en **tension** avec le moindre privilège d'O10. Revenir plus tard à RL-2 exigerait un transfert de propriété de tous les objets (`REASSIGN OWNED`, `À CONFIRMER`) |
| **RL-2** | **migrateur + applicatif** | l'application n'a plus de DDL. Chaque nouvelle table doit être accordée au rôle applicatif : soit par **privilèges par défaut** (aucune retouche de migration), soit par des `GRANT` ajoutés à chaque migration. Cette seconde voie serait une retouche manuelle hors du cas « migration de données » d'ADR-005 §5.4. Deux secrets. L'administration est faite par le migrateur ou par le superutilisateur d'installation |
| **RL-3** | **administrateur + migrateur + applicatif** | séparation nette : l'administrateur ne sert qu'à l'installation et à la reprise. Trois secrets à distribuer et à protéger (D-13, D-14). Documentation opérateur plus lourde. S'aligne sur une procédure de provisioning P4-8 |

#### 4.5.3 Compatibilité avec l'autorité de migration

| | RL-1 | RL-2 | RL-3 |
|---|---|---|---|
| MA-A1 | **cohérent** | incohérent (le poste doit détenir le migrateur, ce qui annule RL-2) | incohérent (même raison) |
| MA-A2 | cohérent | partiel (migrateur sur un seul poste) | partiel |
| MA-B1, MA-B2 | possible, sans bénéfice | **cohérent** | **cohérent** |
| MA-C1 | possible | **cohérent** si le migrateur est saisi | **cohérent** si le migrateur est saisi |
| MA-C2 | possible | cohérent (migrateur à l'installation seulement) | cohérent |

#### 4.5.4 Conséquences à noter, quelle que soit la configuration

- **La propriété des objets se fige à la première installation réelle**, comme le schéma `public` s'est figé
  dans la baseline (P4-5E-B D-08.4). La changer ensuite coûte une intervention sur chaque base installée.
- **Npgsql peut créer la base si elle manque**. Il faut alors `CREATEDB` et un accès à la base de maintenance
  (D-09, `EF_BEHAVIOR — À CONFIRMER`). Sous RL-2 ou RL-3, ce chemin est fermé au rôle applicatif.
- **`search_path`** : un schéma portant le nom du rôle applicatif capterait les tables non qualifiées
  (P4-5E-B D-08). À interdire au provisioning.
- **`EnsureCreated()` du seed de démonstration** ([DbInitializer.cs:19](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L19))
  sous un rôle sans DDL : comportement `UNKNOWN` (C-1).
- **Nombre de chaînes de connexion** : l'application ne lit aujourd'hui qu'une variable,
  `MMV_DATABASE_CONNECTION_STRING`. La source de l'identifiant migrateur (MA-B, MA-C1) est `UNKNOWN`.

### 4.6 Déroulé opérationnel

Les étapes marquées **[DP-n]** dépendent d'un point de décision du §5.2. Les lots cités sont les propriétaires
documentés.

#### 4.6.1 Nouvelle installation

1. **Installer le serveur PostgreSQL** au magasin. Aujourd'hui, la procédure n'existe qu'en laboratoire
   (P4-1 Lot C). Procédure de production : P4-8.
2. **Provisionner** avec l'identifiant administrateur : base, rôles et droits **[DP-5]**, sans schéma au nom du
   rôle applicatif (D-08), avec les privilèges par défaut si RL-2 ou RL-3. Lot P4-8 (D-09).
3. **Appliquer la baseline `InitialPostgreSqlBaseline`** **[DP-1]** : premier poste sous verrou (MA-A1), poste
   désigné (MA-A2), outil (MA-B), mode d'administration (MA-C1), ou application sur base vide (MA-C2). Toujours
   sous verrou **[DP-2]**, sauf LK-6.
4. **Vérifier** : historique = baseline seule ; aucune migration en attente ; invariants physiques (portage de
   `VerifyAfterPreparation`, équivalent serveur P4-5G/P4-6C) ; **métadonnée de compatibilité initialisée**, avec
   un minimum supporté égal à la version de la release installée — rien de plus ancien n'a jamais existé
   **[DP-3]** ; **entrée de journal** de résultat **[DP-8]**.
5. **Créer le premier administrateur MMV** : secret bootstrap sur **un seul** poste. Deux postes démarrant
   ensemble avec le secret entrent en course, et l'index unique en rejette un (P4-5E-A, constat (b)). Lot P4-8
   (D-12).
6. **Configurer les postes** : fournisseur et chaîne de connexion **du rôle applicatif** (D-13).
7. **Démarrer les postes** : la garde de version trouve E1, puis l'écran de connexion s'affiche.
8. **Retirer le secret bootstrap** du poste qui le portait.

#### 4.6.2 Montée de version (schéma 1.4 → 1.5)

1. **Annoncer la maintenance** ; recenser ou fermer les postes connectés **[DP-6]** (CX-1 à CX-5).
2. **Sauvegarder** la base (`pg_dump`). Principe imposé (K-5) ; outillage P4-9.
3. **Acquérir le verrou de migration** **[DP-2]**.
4. **Appliquer les migrations en attente** **[DP-1]**.
5. **Vérifier** : aucune migration en attente ; Appliquées ⊆ Connues pour le binaire 1.5 ; invariants
   physiques ; **métadonnée de compatibilité mise à jour** — version de schéma = 1.5, minimum supporté = 1.4,
   **calculé** à partir du journal **[DP-3]** ; **entrée de journal** de résultat **[DP-8]**.
6. **Libérer le verrou** et clore la maintenance.
7. **Mettre à jour les binaires** sur chaque poste (ADR-001 §6). **Sous la fenêtre N-1 [DP-3], les postes restés
   en 1.4 continuent de travailler** (état **E3a**) pendant toute la tournée : le magasin ne s'arrête pas. La
   tournée doit néanmoins **se terminer avant la prochaine release porteuse de schéma** — dont le *contract*
   supprimerait ce que 1.4 utilise (**H15**, ADR-APP-DISTRIBUTION-001 **OI-13**).
8. **Redémarrer les postes** : E1.

#### 4.6.3 Reprise après échec

1. **Échec pendant une migration** : la transaction de la migration fautive est annulée. La base reste à la
   dernière migration réussie, dans un état **intermédiaire mais cohérent** (U-6, `EF_BEHAVIOR — À CONFIRMER`).
   **Exception** : une opération qui ne peut pas s'exécuter en transaction laisserait un état partiel
   (`PG_BEHAVIOR — À CONFIRMER`). Une règle de rédaction des migrations est à décider (Q-15).
2. **Libération du verrou** : automatique à la fin de la session (LK-1) ; expiration ou intervention manuelle
   (LK-3).
3. **État des postes après l'échec** : un poste 1.5 voit des migrations en attente et **reste bloqué** (E2). Un
   poste 1.4 voit des migrations inconnues : **il est servi si le minimum supporté l'y autorise** (E3a), bloqué
   sinon (E3b). Comme la base est restée à la dernière migration réussie, la métadonnée de compatibilité **n'a
   pas été avancée** : la fenêtre du poste 1.4 est donc celle de l'état réel de la base, pas celle qu'on
   visait — **[DP-3]**.
4. **Diagnostiquer** à partir du journal de migration serveur **[DP-8]**, dont l'emplacement est `UNKNOWN`
   aujourd'hui.
5. **Choisir la reprise** :
   - **en avant** : corriger la cause et relancer. EF ne rejoue pas les migrations déjà inscrites dans
     l'historique (`EF_BEHAVIOR — À CONFIRMER`) ;
   - **par restauration** de la sauvegarde de l'étape 2 de §4.6.2. Les `Down()` ne sont pas outillés, et celui
     de la baseline supprime tout (U-7). Lot P4-9.
6. **Revérifier** (étape 5 de §4.6.2), puis rouvrir.
7. **Plantage du migrateur** (poste ou outil) en cours de migration : le serveur annule la transaction ouverte
   et libère le verrou de session, une fois la session morte détectée (`PG_BEHAVIOR — À CONFIRMER`). On revient
   au point 3.

---

## 5. Decision

> **ACCEPTED — 22/09/2026, revue P4-6A.** Ce paragraphe rappelle les contraintes que des ADR acceptées imposent
> déjà (§5.1), puis énonce les décisions (§5.2), **toutes arrêtées**. Les §5.3 et §5.4 restent des **aides de
> lecture** ; §5.3 indique quel profil est retenu.
>
> **Note de numérotation des points de décision.** La demande de consolidation énumérait huit décisions dans un
> ordre qui **ne coïncide pas** avec les identifiants **DP-n** déjà posés par ce document. Ces identifiants sont
> cités par le §4.6 (déroulés opérationnels), le §8, et par ADR-APP-DISTRIBUTION-001 (DP-1, DP-5, DP-7).
> **Les renuméroter casserait ces renvois.** Les identifiants du dépôt sont donc **conservés**, et les deux
> sujets réellement nouveaux reçoivent des identifiants neufs, **DP-9** et **DP-10**. Correspondance :
>
> | Intitulé de la demande | Identifiant retenu ici |
> |---|---|
> | 1 — Qui réalise les migrations | **DP-1** (autorité de migration) |
> | 2 — Déclenchement | **DP-9** *(nouveau)* |
> | 3 — Verrou migration | **DP-2** |
> | 4 — Version schéma | **DP-3** + **DP-7** (numérotation MMV) |
> | 5 — Compatibilité postes | **DP-4** (comportement au démarrage) |
> | 6 — Sauvegarde | **DP-10** *(nouveau)* |
> | 7 — Utilisateurs connectés | **DP-6** |
> | 8 — Rôles PostgreSQL | **DP-5** |

### 5.1 Contraintes déjà imposées (non rediscutées)

| # | Contrainte | Source |
|---|---|---|
| **K-1** | **Un seul acteur à la fois** applique du DDL sur la base centrale | ADR-001 §6 ; O8 ; roadmap P4-6 (« Interdit ») |
| **K-2** | Pas de `Migrate()` par poste **sans coordination** sur la base centrale | ADR-005 §5.10 |
| **K-3** | Un client trop ancien face à un schéma trop récent est **bloqué**, avec un message clair et **sans écriture** | ADR-001 §6 ; O8 ; ADR-007 §5.7 |
| **K-4** | Un client ne **travaille** pas sur une base dont ses migrations ne sont pas toutes appliquées. S'il peut d'abord migrer lui-même dépend de DP-1 (Q-10) | ADR-007 §5.7 |
| **K-5** | **Sauvegarde avant migration**, en principe. Outillage en P4-9 | ADR-001 §6 ; O9 |
| **K-6** | Modèle de permissions **réel, au moindre privilège**, en remplacement du `db_owner` de laboratoire. O10 **ne fixe pas** le nombre de rôles | O10 ; ADR-002 l. 963 |
| **K-7** | Migrations **générées par l'outillage EF**. Le script SQL de la baseline est une pièce de revue, **jamais une source d'exécution** | ADR-005 §5.4 (Option 3 rejetée) ; P4-5E-B D-03.7 |
| **K-8** | Le schéma de production ne se modifie **jamais à la main** | ADR-007 §5.8 |
| **K-9** | `EnsureCreated()` est **interdit** dans tout chemin de production (C-1 à qualifier) | ADR-007 §5.5 |
| **K-10** | `__EFMigrationsHistory` PostgreSQL **neuf**, jamais importé depuis SQLite | ADR-005 §5.8 ; O11 |
| **K-11** | Domain et Application restent **provider-neutres**. Verrou et garde vivent dans Infrastructure et dans la composition root | O14 |
| **K-12** | Chaîne SQLite et cycle de vie SQLite **inchangés**. Le `Migrate()` par poste reste légitime pour SQLite local | ADR-005 G4, §5.10 ; critère S-5 |
| **K-13** | Aucune garde de démarrage n'est masquée par un `catch` générique | conception P4-3 ([App.axaml.cs:200-204](../../src/MMV.App/App.axaml.cs#L200-L204)) ; critère S-6 |
| **K-14** | UI **minimale** seulement, limitée à la configuration de connexion, à l'indisponibilité du serveur et à une opération de migration clairement décidée | roadmap §3 |

### 5.2 Décisions

#### DP-1 — Autorité de migration (D-11, D-18) — **ARRÊTÉE : MA-B2**

1. Un **outil séparé**, `MMV.DatabaseManager`, est **seul responsable** de l'application des migrations sur la
   base PostgreSQL centrale. Il applique la baseline à l'installation (D-11) comme les migrations ultérieures
   (D-18).
2. **`MMV.App` ne lance jamais de migration PostgreSQL**, ni automatiquement au démarrage, ni sur action d'un
   utilisateur. Aucun chemin DDL PostgreSQL n'existe dans l'application.
3. Le chemin **SQLite reste inchangé** : `Migrate()` par poste y demeure légitime (K-12, DR-8). MA-B2 ne vise
   que la base centrale.

*Motifs* : U-2 et U-4 sont résolus **par construction** côté poste ; l'identifiant DDL n'a besoin d'être
stocké sur **aucun** poste, seul alignement réel sur O10 (§4.5.3) ; le moment de la migration est choisi, donc
la sauvegarde (DP-10) et la fenêtre de maintenance (DP-6) s'insèrent naturellement ; c'est la charge de preuve
la plus faible (DR-5, critère S-1).

*Ce qui est accepté en contrepartie* : un artefact de plus à construire, versionner et distribuer ; tant que
l'outil n'a pas tourné, les postes porteurs de la nouvelle version sont bloqués (§4.1, *Inconvénients*).
**ADR-APP-DISTRIBUTION-001 DI-5** traite la distribution de cet outil : même paquet, même version, même
assembly de migrations que `MMV.App`.

#### DP-9 — Déclenchement d'une migration *(nouveau)* — **ARRÊTÉE**

1. Une migration de la base centrale exige une **action explicite d'un opérateur autorisé**. Elle ne se
   déclenche ni au démarrage d'un poste, ni sur un minuteur, ni sur détection d'une version.
2. « Autorisé » s'entend au sens **PostgreSQL** : l'opérateur détient l'identifiant du **rôle migrateur**
   (DP-5). **Aucun rôle applicatif MMV nouveau n'est créé**, et aucune autorisation métier MMV n'intervient.
   **Le rôle migrateur PostgreSQL est obligatoire** : il n'existe aucune autre voie d'autorisation.
3. **L'identité de l'opérateur suit un modèle mixte, fixé par contrat** (Q-2 et QD-14, tranchées par la revue
   P4-6A du 22/09/2026) : le **propriétaire du magasin** ou le **support MMV** peut déclencher une migration,
   selon le contrat de service qui lie le client à l'éditeur. **Aucun utilisateur applicatif standard ne migre**,
   dans aucun des deux modèles.

*Motif* : un rôle applicatif MMV ne serait pas un droit PostgreSQL (§4.4.2, piège de SB-2), et la garde de
version s'exécute **avant** l'authentification ([App.axaml.cs:41-46](../../src/MMV.App/App.axaml.cs#L41-L46)).
Faire porter l'autorisation par le rôle base de données évite de lire `Users` sur un schéma non vérifié.

**Ce que le modèle mixte impose au produit.** Puisque les deux modèles doivent rester servis par le **même**
binaire, **l'identité de l'opérateur n'est jamais câblée dans MMV** — ni dans le code, ni dans la configuration
livrée. Trois conséquences, toutes portées ailleurs :

1. l'identifiant migrateur est **fourni au moment de la migration**, jamais stocké sur un poste (DP-5) ;
2. l'outil exige au lancement une **référence d'opérateur**, qu'il enregistre au journal (DP-8). C'est la seule
   trace qui, après coup, distingue une migration faite par le magasin d'une migration faite par le support ;
3. le **partage du rôle migrateur** entre le client et l'éditeur est une clause **contractuelle**, pas une
   décision d'architecture : elle relève de la procédure P4-8, qui doit rester compatible avec les deux modèles
   **et** avec un modèle qui change en cours de vie du contrat.

#### DP-2 — Verrou de migration — **ARRÊTÉE : mécanisme natif PostgreSQL (famille LK-1)**

1. La sérialisation repose sur un **mécanisme de verrouillage natif de PostgreSQL**, et non sur une table.
2. **LK-3 et LK-4 sont écartés** : aucune **table de verrou applicative** n'est créée. Elles n'existeraient pas
   sur une base vide, exigeraient un DDL hors chaîne (K-7), une expiration fondée sur l'horloge serveur et un
   nettoyage — pour une garantie que le serveur offre déjà.
3. Le verrou reste **nécessaire** bien que DP-1 retire le DDL des postes : il protège contre **deux exécutions
   de l'outil** (deux opérateurs, ou un double lancement). Son rôle est défensif, pas nominal (§4.2.3).
4. **La forme exacte est arrêtée en P4-6B** : clé, portée session (`pg_advisory_lock`) contre portée
   transaction, attente bornée contre échec immédiat (`pg_try_advisory_lock`), comportement du pool Npgsql,
   délai de détection d'une session morte. Tous ces points sont `PG_BEHAVIOR — À CONFIRMER` (§4.2.1) et se
   prouvent sur serveur, donc après P4-5F.

*Réserve explicite* : LK-2 (verrou de transaction) **ne peut pas** tenir ce rôle — EF ouvre une transaction par
migration, donc il ne sérialise pas une montée de version en plusieurs migrations (§4.2.1).

#### DP-3 — Version du schéma et compatibilité des postes (D-16) — **ARRÊTÉE : fenêtre limitée N-1**

**Q-3 est tranchée (revue P4-6A, 22/09/2026) : la compatibilité n'est pas une égalité stricte, mais une fenêtre
d'un seul cran.** L'égalité stricte est **rejetée** (RB-14).

**5.2.3.1 — Ce qui ne change pas**

1. La **version applicative** de MMV suit le **versionnage sémantique** (SemVer). Voir DP-7.
2. L'**état du schéma** reste déterminé par les **migrations EF** : `__EFMigrationsHistory` (Appliquées) comparé
   aux migrations compilées dans l'assembly du binaire (Connues). **VS-1 reste la source de vérité de l'état** —
   ce qui est appliqué, et ce que le binaire connaît.
3. Le rôle applicatif doit pouvoir **lire** `__EFMigrationsHistory` (DP-5).

**5.2.3.2 — Ce qui change : l'état ne vaut plus verdict**

La compatibilité n'est plus **déduite** de l'égalité des deux ensembles. Elle est **bornée par une fenêtre d'un
cran**, et le verdict devient explicite :

| # | Situation | Version applicative du poste | Verdict |
|---|---|---|---|
| **C-1** | Appliquées **=** Connues | — | **E1** — démarrage normal |
| **C-2** | Connues **⊄** Appliquées : le poste connaît des migrations **non appliquées** | — | **E2 — blocage strict** : « la base doit être mise à jour par l'opérateur ». **Aucune fenêtre dans ce sens** |
| **C-3** | Appliquées **⊋** Connues : la base est **en avance** | **≥** minimum supporté | **démarrage normal, en fenêtre** |
| **C-4** | Appliquées **⊋** Connues | **<** minimum supporté | **E3 — blocage** : « ce poste doit être mis à jour » |
| **C-5** | ensembles **incomparables** (chaîne divergente) | — | **blocage inconditionnel** |

**La fenêtre est asymétrique, et ce n'est pas un oubli.** Un poste **plus ancien** sur un schéma **plus récent**
(C-3) est sûr parce que la release récente n'a fait qu'**ajouter** — c'est ce que garantit la discipline du
§5.2.3.4. Un poste **plus récent** sur un schéma **plus ancien** (C-2) ne l'est jamais : son modèle attend des
colonnes qui n'existent pas encore. **Aucune fenêtre ne peut couvrir C-2**, et l'ordre « base d'abord » d'
ADR-APP-DISTRIBUTION-001 DI-4 existe précisément pour que C-2 ne se produise pas.

**5.2.3.3 — Définition de « N-1 »**

| Terme | Définition |
|---|---|
| **N** | version applicative de la release qui a produit l'**état courant** du schéma |
| **N-1** | version applicative de la **release précédente qui a modifié le schéma**. Une release **sans migration ne consomme aucun cran** : trois correctifs successifs sans DDL laissent la fenêtre intacte |
| **minimum supporté** | **N-1**. Sur une base neuve, où seule la baseline est appliquée, le minimum vaut **N** : rien de plus ancien n'a jamais existé |

Un poste en **N-2** est donc bloqué, et c'est voulu : au-delà d'un cran, la discipline du §5.2.3.4 ne garantit
plus rien, puisque la release N a pu **supprimer** ce que N-2 utilisait.

**5.2.3.4 — Discipline obligatoire : étendre → migrer → contracter**

La fenêtre n'est **sûre que sous cette discipline**. Elle devient une **règle de rédaction des migrations**,
inscrite dans [CONTRIBUTING.md](../../CONTRIBUTING.md) par P4-6B :

1. **Étendre** — une release qui touche au schéma ne procède que par **ajouts compatibles** : nouvelle colonne
   *nullable* ou avec valeur par défaut, nouvelle table, nouvel index. Jamais une suppression, jamais un
   renommage, jamais un resserrement de contrainte sur ce que la version précédente écrit.
2. **Migrer** — la reprise de données éventuelle suit, sur le schéma étendu.
3. **Contracter** — la **suppression** de ce que la version précédente utilisait n'a lieu qu'à la **release
   suivante**, jamais dans la même.

**Conséquence dure, à ne pas perdre de vue : deux releases porteuses de schéma ne peuvent pas se déployer dans
la même tournée de postes.** Le *contract* de N+1 n'est sûr que si **tous** les postes sont déjà au moins en N.
C'est le prix de la fenêtre, et il est opérationnel, pas technique : il borne la tournée dans le temps
(obligation **H15**, ADR-APP-DISTRIBUTION-001 **OI-13**).

**5.2.3.5 — Où vit le minimum supporté**

Le verdict C-3 / C-4 exige que le **schéma déclare** le minimum qu'il accepte : le binaire N-1 voit des
migrations inconnues, mais **rien dans l'historique EF ne lui dit si elles valent un cran ou cinq**. Une
métadonnée est donc nécessaire — et elle est délibérément **minimale** :

1. Elle vit **hors du modèle EF**, dans une table de métadonnées MMV de la base centrale, **propriété du rôle
   migrateur**. Conséquence : **ni règle de double migration, ni angle mort de contrôle de dérive, ni table sans
   objet côté SQLite.** Le précédent est `__EFMigrationsHistory` lui-même, qu'EF crée et entretient hors du
   modèle.
2. Elle est écrite **exclusivement par `MMV.DatabaseManager`** (DP-1), dans la **même opération verrouillée**
   que la migration (DP-2). Aucune migration ne l'écrit dans son corps : rien à oublier dans un `Up()`.
3. **Le minimum est calculé par l'outil**, à partir du journal (DP-8) — c'est la version applicative de la
   dernière exécution qui a modifié le schéma avant celle-ci. **Il n'est jamais choisi à la main par un
   développeur.** C'est ce qui retire à VS-2 son objection principale (§4.3 : « une erreur du développeur
   autorise un poste ancien sur un schéma incompatible »). La valeur n'est pas une déclaration, c'est un
   **constat**.
4. Le rôle applicatif y a un droit de **lecture seule** (DP-5).
5. **Emplacement exact — schéma dédié, noms des objets, type des colonnes — arrêté en P4-6B**, comme la forme
   du verrou. Cette métadonnée et le journal de DP-8 relèvent du même espace de noms MMV.

**Ce que DP-3 retient donc de VS-2** : la **fenêtre**, et elle seule. Pas la table dans le modèle, pas le
minimum choisi par un humain, pas la seconde source de vérité sur l'**état** — l'historique EF reste seul juge de
ce qui est appliqué. **QD-15 d'ADR-APP-DISTRIBUTION-001 est tranchée pour le multi-poste** : le minimum est
déclaré **par le schéma**, calculé par l'outil, selon la règle N-1.

**VS-3** (registre des postes) n'est **pas retenu en V1** : il ne décide jamais seul de la compatibilité, exige
une écriture à chaque démarrage et un changement de modèle, pour un bénéfice de diagnostic. Réexaminable si
DP-6 impose un inventaire des sessions.

#### DP-4 — Comportement des postes au démarrage — **ARRÊTÉE : SB-3**

Un poste dont la version applicative ne correspond pas au schéma, au sens de la politique retenue en DP-3 :

1. **n'écrit rien** — ni DML, ni DDL, ni seed ;
2. **affiche un message explicite** : ce qui ne va pas, et ce qu'il faut faire ;
3. **demande la mise à jour** du poste, ou signale que la base doit être mise à jour par l'opérateur, selon
   l'état ;
4. **ne migre jamais de lui-même** — conséquence directe de DP-1.

États (§4.4.1), **révisés par la fenêtre N-1 de DP-3** :

| État | Situation | Comportement |
|---|---|---|
| **E1** | schéma = application (C-1) | démarrage normal |
| **E2** | application **plus récente** que le schéma (C-2) | **blocage** — « la base doit être mise à jour par l'opérateur ». **SB-1 et SB-2 sont écartés** ; aucune fenêtre dans ce sens |
| **E3a** | application **plus ancienne**, **dans** la fenêtre (C-3) | **démarrage normal.** Nouveau : cet état n'était pas servi avant la revue P4-6A |
| **E3b** | application **plus ancienne**, **hors** fenêtre (C-4) | **blocage** — « ce poste doit être mis à jour » |
| **E3c** | chaîne **divergente** (C-5) | **blocage inconditionnel** |
| **E4** | base vide | **blocage** : la baseline appartient à l'outil (DP-1) |
| **E5** | migration en cours | message « maintenance en cours » |
| **E6** | serveur injoignable ou préparation en échec | relève de D-15 (P4-8), **jamais avalé** (K-13) |
| **E7** | tables **sans** historique EF | reste `UNKNOWN` — **Q-12 ouverte** |

**E3a est le seul état que la revue P4-6A ajoute au comportement nominal**, et il porte tout le bénéfice de la
fenêtre : c'est lui qui empêche le magasin de s'arrêter pendant la tournée des postes. Un poste en E3a
**travaille normalement** — il n'est ni dégradé, ni en lecture seule. **Il ne connaît pas** les colonnes ajoutées
par la release N, et c'est sans conséquence : la discipline « étendre → migrer → contracter » (§5.2.3.4) garantit
qu'aucune de ses écritures n'échoue faute d'une colonne obligatoire apparue après lui.

**L'écran de blocage compte donc au minimum trois messages distincts** — E2, E3b, E5 — auxquels
ADR-APP-DISTRIBUTION-001 **DI-3.4** ajoute le blocage par plancher de version applicative, **qui ne se confond
pas** avec E3b : E3b dit « ce poste est trop ancien **pour cette base** », DI-3.4 dit « ce poste est trop ancien
**pour être servi** ».

**Revérification en cours de session : non décidée ici (Q-11).** Aujourd'hui, la garde ne s'exécute qu'au
démarrage : un poste démarré avant une migration continue de travailler sur un schéma qui a changé (§4.4.3).
DP-6 est la réponse de V1 à ce risque ; une revérification périodique reste une option de P4-6B.

#### DP-10 — Sauvegarde avant migration *(nouveau)* — **ARRÊTÉE**

1. **Aucune migration** de la base centrale ne s'exécute sans **sauvegarde préalable**.
2. La sauvegarde est **vérifiée** avant que la migration ne commence. Une sauvegarde non vérifiée vaut absence
   de sauvegarde.
3. En cas d'échec de la sauvegarde ou de sa vérification, la migration **ne démarre pas**.

DP-10 durcit **K-5**, qui n'imposait le principe que « en principe ». **Ce qu'est une vérification suffisante,
et l'outillage qui la produit, appartiennent à P4-9** (O9) ; DP-10 en fait une **condition d'exécution**, pas
un conseil de procédure. C'est aussi la seule voie de retour arrière réaliste (U-7, DR-11), et
ADR-APP-DISTRIBUTION-001 DI-7 en dépend.

#### DP-6 — Postes connectés pendant une migration (U-5, D-B4) — **ARRÊTÉE dans son principe**

1. Une migration qui **change le schéma** impose un **état de maintenance** : elle ne s'exécute pas pendant que
   des postes travaillent.
2. **La stratégie exacte est arrêtée en P4-6B**, parmi CX-1 à CX-5 (§4.2.4) ou une combinaison. Le choix dépend
   de mesures serveur non disponibles aujourd'hui (droits sur `pg_stat_activity`, comportement des verrous
   `ALTER TABLE`, détection des sessions mortes — tous `PG_BEHAVIOR — À CONFIRMER`).
3. **D-B4 cesse d'être une simple recommandation** : « tous les postes fermés » devient une **exigence** de
   l'état de maintenance. Qu'elle soit tenue par une garde technique ou par une procédure vérifiée est
   précisément ce que P4-6B tranche.

#### DP-5 — Rôles PostgreSQL (D-10, partagé avec P4-8) — **ARRÊTÉE : RL-3**

Trois rôles distincts :

| Rôle | Ce qu'il fait | Qui le détient |
|---|---|---|
| **Administrateur d'installation** | crée la base et les rôles, accorde les droits, sauvegarde et restaure | installateur. **Jamais** l'application, **jamais** l'outil de migration en fonctionnement nominal |
| **Migrateur** | propriétaire des objets du schéma ; applique les migrations ; **seul** à écrire la métadonnée de compatibilité (DP-3) et le journal (DP-8) | l'opérateur, **au moment de la migration seulement** (DP-9). N'est stocké sur **aucun** poste client |
| **Applicatif** | `CONNECT`, `USAGE`, `SELECT`/`INSERT`/`UPDATE`/`DELETE`, **lecture de `__EFMigrationsHistory`** (garde de version, DP-3) et **lecture seule de la métadonnée de compatibilité** (DP-3). **Aucun accès au journal** (DP-8) | chaque poste. **Aucun droit DDL** |

**RL-1 est écarté** : il donnerait le DDL à chaque poste, en tension frontale avec O10, et le retour vers RL-2
ou RL-3 coûterait un transfert de propriété de tous les objets sur chaque base installée (§4.5.4).
**RL-2 est écarté** : il confond l'installation et la migration dans un même rôle propriétaire, alors que P4-8
doit pouvoir provisionner sans détenir le migrateur.

**Les noms définitifs des rôles restent ouverts** et appartiennent à P4-8 (D-09, D-13). *Tranché le 04/10/2026
par [ADR-PROD-DB-010](adr-prod-db-010-provisioning-and-connection-security.md) : les noms sont des **paramètres**
du provisioning, et RL-3 reçoit un **rôle de sauvegarde** en lecture seule (la restauration reste à
l'administrateur).* `mmv_app` et
`mmv_spike` sont des rôles de laboratoire, explicitement « jetables » (RA-8) : ils ne préjugent de rien.

*Conséquences à porter* : privilèges par défaut (`ALTER DEFAULT PRIVILEGES`) à poser au provisioning, sans quoi
chaque nouvelle table serait inaccessible au rôle applicatif (§4.5.2) ; trois secrets à distribuer et protéger
(D-13, D-14) ; `EnsureCreated()` du seed de démonstration sous un rôle sans DDL reste `UNKNOWN` (C-1, Q-16).

#### DP-7 — Numérotation de MMV (D-17) — **ARRÊTÉE**

1. MMV porte une **version applicative unique, en SemVer**, déclarée en **une seule source** dans le dépôt.
2. Cette version est celle de **tous** les artefacts de la release, `MMV.App` et `MMV.DatabaseManager`
   compris (ADR-APP-DISTRIBUTION-001 DI-5).
3. Elle est **affichée** à l'utilisateur et **journalisée**. Le `1.0.0` **écrit en dur** de
   [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) est supprimé au profit de la version
   réelle.
4. **Emplacement et règle d'incrément** : [Directory.Build.props](../../Directory.Build.props) est l'option
   documentée par P4-5E-A ; le fichier ne contient aucune propriété de version aujourd'hui (`CODE`). Le choix
   exact appartient à **P4-6B**, qui l'implémente.

*Motif* : sans version unique lisible à l'exécution, aucun message de blocage ne peut dire « installez la
version X » (§4.3, VS-1, *Inconvénients*), et DP-3 comme DI-3.4 deviennent inexprimables.

#### DP-8 — Journal des migrations serveur — **ARRÊTÉE : journal dédié (Q-14 tranchée)**

**La revue P4-6A du 22/09/2026 retient « les deux », avec une division de rôles explicite.**

1. Le journal **autoritatif** est une **table dédiée de la base centrale**, **hors du modèle EF**, propriété du
   rôle migrateur, écrite **exclusivement** par `MMV.DatabaseManager` (DP-1). Elle vit dans le même espace de
   noms MMV que la métadonnée de compatibilité de DP-3.
2. Elle est **doublée d'une trace locale** écrite par l'outil. La trace locale n'est pas une commodité : elle
   seule peut enregistrer ce qui **n'a jamais atteint la base** — serveur injoignable, authentification refusée,
   sauvegarde absente ou non vérifiée (DP-10). Un journal uniquement en base serait muet précisément sur les
   échecs les plus précoces.
3. **Les écritures du journal sont hors de la transaction de la migration** : une entrée à l'**ouverture**, une
   entrée de **résultat** à la fin. Sans cette séparation, l'annulation de la transaction d'une migration en
   échec **effacerait la trace de son propre échec**, et l'étape 4 de la reprise (§4.6.3) resterait sans preuve —
   exactement le manque que Q-14 devait combler.

**Champs minimaux** — obligatoires ; la forme exacte (noms, types, table unique ou deux tables) appartient à
P4-6B :

| Champ | Contenu | Pourquoi il est minimal |
|---|---|---|
| **version applicative** | SemVer de l'outil qui exécute (DP-7) | identifie la release N ; c'est la source du calcul du minimum supporté (DP-3) |
| **migrations appliquées** | identifiants EF, état **avant → après** | rattache l'entrée à `__EFMigrationsHistory`, seule source de vérité de l'état |
| **date et heure** | **UTC**, **horloge du serveur** (`now()`), à l'ouverture **et** à la clôture | [ADR-PROD-DB-004](adr-prod-db-004-datetime-strategy.md) ; l'horloge d'un poste n'est pas fiable (T9, NTP) |
| **opérateur** | rôle PostgreSQL effectif (`current_user`) **et** référence d'opérateur fournie au lancement | le modèle opérateur est **mixte** (DP-9) : l'identité ne peut pas être câblée, elle doit être **enregistrée** |
| **résultat** | succès ou échec, avec la cause en cas d'échec | étape 4 de la reprise après échec (§4.6.3) |
| **référence de sauvegarde** | identifiant de la sauvegarde **vérifiée** | DP-10 en fait une condition d'exécution ; c'est aussi l'unique point de retour arrière réaliste (U-7, DI-7) |

**Le rôle applicatif ne lit pas le journal.** Il n'a besoin que de `__EFMigrationsHistory` et de la métadonnée de
compatibilité (DP-3, DP-5). Le journal est un instrument d'exploitation, pas une donnée applicative.

*Conséquence* : **DP-8 ne bloque plus la clôture de P4-6B.** Ce qui reste à P4-6B est la **forme**, non le
principe.

#### Récapitulatif

| # | Décision | Statut | Option retenue | Reste à P4-6B |
|---|---|---|---|---|
| **DP-1** | autorité de migration | **ARRÊTÉE** | **MA-B2** — `MMV.DatabaseManager` | construction de l'outil |
| **DP-9** | déclenchement | **ARRÊTÉE** | action explicite d'un opérateur autorisé ; **modèle mixte selon contrat** | référence d'opérateur exigée au lancement |
| **DP-2** | verrou | **ARRÊTÉE** | natif PostgreSQL (LK-1), pas de table | forme exacte, clé, portée |
| **DP-3** | version du schéma et compatibilité | **ARRÊTÉE** | VS-1 pour l'état ; **fenêtre N-1** ; étendre → migrer → contracter | emplacement de la métadonnée ; règle de rédaction dans CONTRIBUTING.md |
| **DP-4** | démarrage des postes | **ARRÊTÉE** | **SB-3** — blocage sans écriture ; **E3a servi** | trois messages ; revérification (Q-11) |
| **DP-10** | sauvegarde | **ARRÊTÉE** | obligatoire et vérifiée | forme de la vérification (P4-9) |
| **DP-6** | postes connectés | **PRINCIPE** | état de maintenance imposé | stratégie CX-n |
| **DP-5** | rôles PostgreSQL | **ARRÊTÉE** | **RL-3** — trois rôles | noms (P4-8) |
| **DP-7** | numérotation MMV | **ARRÊTÉE** | SemVer, source unique | emplacement, incrément |
| **DP-8** | journal serveur | **ARRÊTÉE** | **journal dédié** : table en base + trace locale ; six champs minimaux | forme exacte (noms, types) |

**Les dix points sont arrêtés.** Ce qui figure en colonne « Reste à P4-6B » est de la **mise en œuvre** : aucune
de ces lignes ne rouvre un choix d'architecture.

### 5.3 Combinaisons cohérentes — **P-β est le profil retenu**

> **Le §5.2 retient le profil P-β**, avec trois précisions : DP-3 retient **VS-1 pour l'état** et la **fenêtre
> N-1** pour le verdict, **sans VS-3** et sans table de version dans le modèle EF ; DP-6 reste au principe (état
> de maintenance) sans choisir entre CX-4 et CX-5 ; DP-8 retient le journal dédié. P-α et P-γ sont **écartés** :
> ils supposent tous deux un chemin DDL dans l'application, que DP-1 supprime. Le tableau est conservé pour
> montrer ce qui a été comparé.

| Profil | DP-1 | DP-2 | DP-3 | DP-4 | DP-5 | DP-6 | Ce qu'il faut accepter |
|---|---|---|---|---|---|---|---|
| **P-α Automatique sous verrou** | MA-A1 | LK-1 | VS-1 ou VS-2 | SB-1 | RL-1 | CX-1 ou CX-2 | DDL sur chaque poste (tension O10) ; moment non maîtrisé ; sauvegarde depuis un poste |
| **P-β Outil d'administration** | MA-B2 (ou B1) | LK-1, défensif | VS-1 (+ VS-3 éventuel) | SB-3 | RL-3 (ou RL-2) | CX-4 ou CX-5 | un livrable de plus ; un opérateur requis ; postes bloqués jusqu'au passage de l'outil |
| **P-γ Hybride approuvé** | MA-C1 | LK-1 | VS-1 ou VS-2 | SB-2 | RL-2 ou RL-3 | CX-2 ou CX-5 | saisie d'un secret dans l'application ; code DDL présent dans le binaire ; piège de l'approbation avant connexion (§4.4.2) |

Le rapport de transition (§2.5) observait que P-β retire tout DDL de l'application et réduit P4-6 côté poste à
une garde de version en lecture seule. **Cette observation est devenue la décision** (DP-1, DP-4).

### 5.4 Combinaisons incohérentes

| Combinaison | Pourquoi |
|---|---|
| SB-1 avec MA-B | une montée automatique suppose que l'application migre |
| MA-A1 avec RL-2 ou RL-3 et un rôle applicatif sans DDL | l'application ne peut pas migrer |
| VS-1 strict avec une montée progressive des postes | tout poste non mis à jour est bloqué dès la première migration. **C'est le motif du rejet de l'égalité stricte** (RB-14) |
| LK-3 avec MA-A1 sur base vide | la table de verrou n'existe pas avant la baseline |
| MA-B3 pour la baseline | contraire à K-7 |
| MA-C3 sans classification vérifiable des migrations | réintroduit le risque interdit par O8 |
| **fenêtre N-1 sans discipline « étendre → migrer → contracter »** | la fenêtre autoriserait un poste N-1 à écrire sur un schéma qui a **supprimé** ou **resserré** ce qu'il utilise : exactement le risque qu'O8 interdit. **La fenêtre n'existe que sous la discipline** (§5.2.3.4) |
| **expand et contract dans la même release** | le *contract* de N détruirait ce que N-1 utilise, au moment même où la fenêtre déclare N-1 supporté |
| **deux releases porteuses de schéma dans la même tournée de postes** | la seconde contracterait avant que tous les postes aient atteint la première. La fenêtre vaut **un** cran, pas deux (H15, OI-13) |
| **fenêtre dans le sens C-2** (poste plus récent toléré sur un schéma plus ancien) | le modèle du binaire attend des colonnes qui n'existent pas encore : aucune discipline de rédaction ne peut le rendre sûr (§5.2.3.2) |

---

## 6. Consequences

### 6.1 Certaines, quel que soit le choix

- **Un gestionnaire de préparation serveur est nécessaire** : `SqliteDatabaseManager` est inutilisable sur
  PostgreSQL (P4-5E-A R-E9). C'est le contenu absorbé de P4-5G.
- **Une garde de version doit exister pour les deux chaînes**, hors du `catch` générique (K-13). Côté SQLite,
  elle détecte aussi le rétrogradage d'un poste (U-1), **sans changer** le chemin nominal (K-12).
- **Un affichage de blocage minimal** est nécessaire (K-14), puisque rien n'est affiché aujourd'hui.
- **Le garde-fou P4-3** ([App.axaml.cs:205](../../src/MMV.App/App.axaml.cs#L205)) n'est levé qu'une fois ces
  mécanismes prouvés, **après P4-5F vert** (roadmap).
- **Les preuves serveur** (critères S-1 à S-4) **dépendent de P4-5F** et d'un serveur en CI (ADR-008).
- **La documentation opérateur** (critère P4 n° 13) doit décrire l'installation, la montée de version et la
  reprise (§4.6).
- **Les passages `"AutoMigrate": true`** d'[ARCHITECTURE.md](../../ARCHITECTURE.md) (l. 218, l. 1064)
  deviendront faux ou incomplets. Leur correction est prévue **après** la décision (P4-5E-C9, Q18).

### 6.2 Conséquences des décisions arrêtées

| Décision | Conséquence à porter |
|---|---|
| **DP-1 (MA-B2)** | **nouveau projet `MMV.DatabaseManager` dans `MMV.sln`**, couvert par les tests d'architecture et la CI. Aucun DDL côté poste. Sa distribution est traitée par ADR-APP-DISTRIBUTION-001 DI-5 (même paquet, même version) |
| **DP-2 (LK-1)** | appels SQL bruts de verrou sur une connexion tenue ouverte, confinés à Infrastructure ou à l'outil (K-11, O14). Dépendance PostgreSQL assumée. Preuve à produire sur serveur (S-1), donc **après P4-5F** |
| **DP-3 (VS-1 + fenêtre N-1)** | **aucun changement du modèle EF** : les deux chaînes de migrations restent telles quelles, et la règle de double migration de [CONTRIBUTING.md](../../CONTRIBUTING.md) **n'est pas sollicitée** — la métadonnée de compatibilité vit **hors modèle** et n'est écrite que par l'outil (§5.2.3.5). En revanche, **une règle de rédaction nouvelle** entre dans CONTRIBUTING.md : « étendre → migrer → contracter », et l'interdiction de contracter dans la même release que l'expand. **Une table de métadonnées MMV apparaît en base centrale**, hors des contrôles de dérive — à couvrir par un test qui prouve son **absence** du modèle EF |
| **DP-4 (SB-3)** | **écran de blocage à trois états minimum** (poste obsolète **hors fenêtre**, base à mettre à jour, maintenance en cours), hors du `catch` générique (K-13). C'est la seule UI nouvelle, et elle reste dans l'enveloppe de K-14. **E3a n'a pas d'écran** : un poste dans la fenêtre démarre normalement |
| **DP-8 (journal dédié)** | une **table de journal** en base centrale, hors modèle EF, plus une **trace locale** de l'outil ; écritures **hors transaction** de migration. Une **référence d'opérateur** devient un argument obligatoire au lancement de l'outil (DP-9) |
| **DP-5 (RL-3)** | privilèges par défaut à poser au provisioning ; **trois secrets** à distribuer et protéger (D-13, D-14) ; documentation opérateur plus lourde ; propriété des objets à fixer **dès la première installation réelle** (§4.5.4) |
| **DP-6 (maintenance)** | selon la stratégie retenue en P4-6B : connexion dédiée hors pool (CX-2), `Application Name` imposé (CX-4), ou procédure vérifiée (CX-5) |
| **DP-7 (SemVer)** | [Directory.Build.props](../../Directory.Build.props) gagne une version ; le `1.0.0` écrit en dur de [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) disparaît |
| **DP-9, DP-10** | la procédure de montée de version devient **exécutable telle qu'écrite** au §4.6.2 : maintenance, sauvegarde vérifiée, verrou, migration, vérification, postes |

**Ce que les décisions retirent du périmètre de P4-6B** : ni verrou côté poste, ni DDL côté poste, ni table de
verrou, ni table de version de schéma, ni registre des postes, ni saisie de secret dans `MMV.App`. Le poste se
réduit à une **garde de version en lecture seule** plus un **écran de blocage**.

### 6.3 Risques résiduels après les décisions

| Risque | État |
|---|---|
| ~~P4-6B ne peut pas commencer~~ | **levé** par les dix points de décision. P4-6B est **spécifiable et écrivable**, garde de version comprise |
| ~~Rôles et propriété des objets figés de fait par la première installation de laboratoire~~ | **levé en principe** par DP-5 (RL-3). Reste à **appliquer** au provisioning P4-8 avant toute installation réelle : la propriété se fige à la première (§4.5.4) |
| ~~Politique de compatibilité (Q-3)~~ | **levé** par DP-3 : fenêtre N-1. La tension avec l'ordre « base d'abord » d'ADR-APP-DISTRIBUTION-001 DI-4 **disparaît** — un poste en E3a travaille pendant toute la tournée |
| ~~Journal serveur (Q-14)~~ | **levé** par DP-8 : journal dédié, écritures hors transaction, six champs minimaux |
| **D-B4 / postes connectés** | **partiellement levé** : DP-6 impose l'état de maintenance, mais la garde technique reste à choisir en P4-6B. Jusque-là, c'est une exigence de procédure (RR6) |
| **Discipline de rédaction des migrations** | **risque nouveau, créé par DP-3.** La fenêtre N-1 ne vaut que si « étendre → migrer → contracter » est **effectivement** respecté. Une migration qui supprime ou resserre dans la même release que son expand casse la garantie **sans qu'aucun contrôle actuel ne le voie**. Atténuation : règle écrite dans CONTRIBUTING.md, revue de migration, et un contrôle automatisé à concevoir en P4-6B (H14) |
| **Tournée des postes bornée dans le temps** | **contrainte nouvelle.** Deux releases porteuses de schéma ne peuvent pas se déployer dans la même tournée (H15, OI-13). Un magasin qui laisse un poste en retard sur deux crans le verra bloqué en E3b — un arrêt, pas une corruption |
| **Métadonnée hors modèle EF** | la métadonnée de compatibilité et le journal échappent aux contrôles de dérive d'[ADR-007](adr-prod-db-007-schema-drift-prevention.md), par construction. Atténuation : un test prouve leur **absence** du modèle EF, et l'outil est leur unique écrivain |
| **Disponibilité de l'opérateur** | conséquence assumée de MA-B2 : tant que l'outil n'a pas tourné, les postes porteurs de la nouvelle version sont bloqués (E2). **Atténué** par la fenêtre : les postes restés à la version précédente continuent de travailler. Le modèle opérateur mixte (DP-9) élargit le vivier, mais la **disponibilité effective** relève du contrat de service, hors de cet ADR |

---

## 7. Rejected alternatives

### 7.1 Alternatives rejetées **par cet ADR** (consolidation P4-6A)

| # | Alternative | Rejetée par | Motif |
|---|---|---|---|
| **RB-1** | **MA-A1 / MA-A2** — l'application migre au démarrage, sous verrou ou depuis un poste désigné | DP-1 | exige un identifiant DDL **sur les postes**, en tension frontale avec O10 ; moment de migration non maîtrisé (U-5) ; sauvegarde à lancer depuis un poste (U-3) ; charge de preuve maximale (S-1) |
| **RB-2** | **MA-C1** — mode d'administration dans le même binaire | DP-1 | le code DDL vivrait dans l'application ; saisie d'un secret dans `MMV.App` ; piège de l'approbation avant authentification (§4.4.2) |
| **RB-3** | **MA-C2** — l'application applique la baseline seule sur une base vide | DP-1 | un identifiant DDL sur un poste, même temporaire ; deux chemins d'exécution à prouver |
| **RB-4** | **MA-C3** — classification des migrations en « compatibles » et « incompatibles » | DP-1 | une erreur de classement réintroduit exactement le risque qu'O8 interdit ; hors de portée V1 |
| **RB-5** | **MA-B1** — bundle EF `dotnet ef migrations bundle` | DP-1 (MA-B2 préféré) | passerait par la factory design-time (`MMV_DESIGNTIME_DATABASE_PROVIDER`, chaîne factice, P4-5E-B D-05) ; **Q-8 devient sans objet**. MA-B2 permet en outre les gardes MMV : verrou, contrôle de sauvegarde, journal |
| **RB-6** | **LK-3 / LK-4** — table de verrou applicative, ou verrou sur `__EFMigrationsHistory` | DP-2 | n'existe pas sur une base vide ; DDL hors chaîne (K-7) ; expiration et horloge serveur à gérer ; le serveur offre déjà la garantie |
| **RB-7** | **LK-2** — verrou consultatif de **transaction** comme sérialisation d'une montée de version | DP-2 | EF ouvre **une transaction par migration** : il ne couvre pas une montée en plusieurs migrations |
| **RB-8** | **SB-1** — montée automatique par le poste | DP-4 | suppose que l'application migre : incohérent avec DP-1 (§5.4) |
| **RB-9** | **SB-2** — approbation d'un administrateur **dans** `MMV.App` | DP-4, DP-9 | un rôle applicatif MMV n'est pas un droit PostgreSQL ; la garde s'exécute avant l'authentification |
| **RB-10** | **RL-1** — un rôle propriétaire unique pour tous les postes | DP-5 | DDL sur chaque poste ; retour vers RL-3 impossible sans transfert de propriété sur chaque base installée |
| **RB-11** | **VS-3** — registre des versions d'application alimenté par les postes | DP-3 | ne décide jamais seul de la compatibilité ; écriture à chaque démarrage ; changement de modèle pour un bénéfice de diagnostic |
| **RB-12** | **Migration déclenchée par un minuteur, un démarrage ou une détection de version** | DP-9 | contourne la décision humaine et l'état de maintenance (DP-6) |
| **RB-13** | **Migration sans sauvegarde, ou avec une sauvegarde non vérifiée** | DP-10 | la restauration est le seul retour arrière réaliste (U-7) ; une sauvegarde non vérifiée n'en est pas une |
| **RB-14** | **Égalité stricte comme politique de compatibilité** (VS-1 seul : Appliquées = Connues, sinon blocage) | DP-3 | combinée à l'ordre « base d'abord » (DI-4), elle **arrête le magasin** entre la migration et la mise à jour du dernier poste ; elle contredit DR-7, qui fait de l'écart de version l'état **normal** d'un déploiement. §5.4 la qualifiait déjà d'incohérente avec une montée progressive |
| **RB-15** | **Fenêtre de compatibilité plus large qu'un cran** (N-2 et au-delà) | DP-3 | la discipline « étendre → migrer → contracter » ne garantit la sûreté que sur **un** cran : au-delà, la release N a pu supprimer ce que N-2 utilisait. Une fenêtre large exigerait de ne jamais contracter, donc de ne jamais nettoyer le schéma |
| **RB-16** | **Minimum supporté choisi à la main par le développeur**, déclaré dans une migration ou dans le binaire | DP-3 | c'était l'objection décisive à VS-2 (§4.3) : une erreur de saisie autorise un poste ancien sur un schéma incompatible. Le minimum est **calculé** par l'outil à partir du journal (DP-8) : un constat, pas une déclaration |
| **RB-17** | **Table de version de schéma dans le modèle EF**, tenue à jour par les migrations elles-mêmes | DP-3 | imposerait la règle de double migration, une colonne sans objet côté SQLite, et un oubli possible dans un `Up()`. La métadonnée vit **hors modèle**, écrite par l'outil seul (§5.2.3.5) |
| **RB-18** | **Journal des migrations en base seulement** | DP-8 | serait **muet** sur tout échec survenu avant ou pendant la connexion : serveur injoignable, authentification refusée, sauvegarde manquante |
| **RB-19** | **Journal écrit dans la transaction de la migration** | DP-8 | l'annulation d'une migration en échec effacerait la trace de son échec, laissant l'étape 4 de la reprise (§4.6.3) sans preuve |
| **RB-20** | **Identité de l'opérateur câblée dans le produit** (un seul modèle imposé : magasin, ou éditeur) | DP-9 | QD-14 retient un **modèle mixte selon contrat** ; le même binaire doit servir les deux, et un contrat peut changer en cours de vie |

**Restent délibérément possibles** : LK-5 (verrou natif d'EF Core ≥ 9) si la montée de version d'EF a lieu.
DP-2 retient une **famille** de mécanismes, non un appel précis, pour ne pas fermer cette voie. Sur quoi
s'appuie l'implémentation Npgsql de ce verrou reste **`UNKNOWN`** (§4.2.1, Q-6) : c'est à vérifier, pas à
supposer. MA-B3
(scripts SQL générés) pour les migrations **postérieures** à la baseline, que **Q-7** n'a pas tranchée et que
DP-1 n'interdit pas, dès lors que c'est l'outil qui les exécute.

### 7.2 Alternatives déjà rejetées par des documents acceptés

Rappelées pour ne pas être rouvertes.

| # | Alternative | Rejetée par |
|---|---|---|
| **RA-1** | chaque poste appelle `Migrate()` au démarrage, sans coordination | ADR-005 §5.10 ; roadmap P4-6 (« Interdit ») ; ADR-001 §6 |
| **RA-2** | baseline écrite à la main, ou exécution du script SQL de revue de la baseline | ADR-005 §4 Option 3 ; P4-5E-B D-03.7 |
| **RA-3** | correction manuelle du schéma de production | ADR-007 §5.8 |
| **RA-4** | création du schéma de production par `EnsureCreated()` | ADR-007 §5.5 |
| **RA-5** | repli silencieux sur SQLite quand le serveur n'est pas prêt | conception P4-3 ; `DatabaseProviderResolver` |
| **RA-6** | import de `__EFMigrationsHistory` SQLite dans la base PostgreSQL | ADR-005 §5.8 ; O11 |
| **RA-7** | absence de garde de version, ou tolérance silencieuse d'un schéma plus récent | O8 ; ADR-007 §5.7 ; ADR-001 §6 |
| **RA-8** | rôle de laboratoire (`db_owner` ou équivalent) comme modèle de production | O10 ; ADR-002 l. 963 |
| **RA-9** | verrou ou garde de version dans Domain ou Application | O14 |
| **RA-10** | réutiliser `SqliteDatabaseManager` pour PostgreSQL | P4-5E-A, *New Installation Flow Analysis* (R-E9) : techniquement impossible |

---

## 8. Implementation impact

**Aucun fichier `.cs`, de test, de migration, de projet, de configuration ou de CI n'est modifié par cet ADR.**
Le tableau ci-dessous décrit les zones qu'un lot futur toucherait. Le découpage P4-6A / P4-6B / P4-6C, issu du
rapport de transition, est depuis le 22/09/2026 **inscrit dans [la roadmap P4](P4-multi-poste-roadmap.md)**
(§P4-6) — dans l'arbre de travail, **non commité** (R-1, Q-18).

### 8.1 Zones concernées

| Zone | Nature probable | Lot proposé | Conditionné par |
|---|---|---|---|
| [App.axaml.cs:200-278](../../src/MMV.App/App.axaml.cs#L200-L278) | levée du garde-fou ; garde de version hors `try` ; sortie du `catch` générique ; branchement de l'affichage | P4-6C | **DP-3, DP-4 — arrêtées** |
| **Nouveau projet `MMV.DatabaseManager`** | outil de migration : verrou, contrôle de sauvegarde, `Migrate()`, vérifications, journal | P4-6B | **DP-1 — arrêtée** |
| Infrastructure : gestionnaire de préparation serveur | détection d'état, vérifications post-migration | P4-6B | DP-1, DP-8 |
| Infrastructure : garde de version | comparaison Appliquées / Connues, **en lecture seule**, plus lecture du minimum supporté ; verdicts C-1 à C-5 | P4-6B | **DP-3 — arrêtée** |
| **Métadonnée de compatibilité et journal** | table(s) MMV en base centrale, **hors modèle EF**, écrites par l'outil seul ; trace locale de l'outil | P4-6B | **DP-3, DP-8 — arrêtées** |
| Verrou de migration | appels SQL de verrou consultatif, **dans l'outil** et non dans `MMV.App` | P4-6B | **DP-2 — arrêtée** |
| [DatabaseProviderResolver.cs](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs) | éventuellement `Application Name` et délais | P4-6B / P4-8 | DP-6, D-15 |
| ~~Modèle EF + deux chaînes~~ | **aucun changement de modèle, décision confirmée** : ni table de verrou (DP-2), ni registre des postes, ni table de version (DP-3, RB-17). La métadonnée de compatibilité vit **hors modèle** | P4-6B | DP-2, DP-3 |
| [Directory.Build.props](../../Directory.Build.props), [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) | version de MMV en SemVer, source unique | P4-6B | **DP-7 — arrêtée** |
| Écran de blocage minimal | trois états au minimum | P4-6C | **DP-4 — arrêtée** |
| Provisioning : trois rôles | administrateur, migrateur, applicatif ; privilèges par défaut | P4-8 | **DP-5 — arrêtée** (noms ouverts) |
| Outillage de sauvegarde et de sa vérification | condition d'exécution de la migration | P4-9 | **DP-10 — arrêtée** |
| `.github/workflows/ci.yml` | job PostgreSQL | **P4-5F** | ADR-008 |
| [CONTRIBUTING.md](../../CONTRIBUTING.md) | **règle « étendre → migrer → contracter »**, interdiction de contracter dans la même release que l'expand, règle de transaction | P4-6B | **DP-3 — arrêtée** ; Q-15 |
| [ARCHITECTURE.md](../../ARCHITECTURE.md), documentation opérateur, roadmap | corrections Q18 ; procédures du §4.6 ; découpage de P4-6 | P4-6B / P4-6C ; P4-9 | **acceptation acquise** |

### 8.2 Obligations — **confirmées à l'acceptation du 22/09/2026**

| # | Obligation | Lot proposé | Bloquante |
|---|---|---|---|
| **H1** | Garde de version au démarrage, pour les deux chaînes, hors `catch` générique | P4-6B / P4-6C | **OUI** (O8, S7) |
| **H2** | Sérialisation prouvée : deux acteurs simultanés, **une seule** application du DDL | P4-6B | **OUI** (O8) |
| **H3** | Poste plus ancien que le schéma **et hors fenêtre** (C-4, E3b) : bloqué, sans écriture, avec un message clair | P4-6B / P4-6C | **OUI** (O8) |
| **H4** | Poste face à des migrations en attente : bloqué, **sans migrer** | P4-6B | **OUI** (DP-1, DP-4) |
| **H5** | Rôles PostgreSQL conformes à DP-5 (RL-3), prouvés sur serveur : **le rôle applicatif ne peut pas faire de DDL** | P4-6B / P4-8 | **OUI** (O10) |
| **H6** | Comportement des postes connectés pendant une migration, conforme à DP-6 et testé | P4-6C | **OUI** (roadmap P4-6) |
| **H7** | Version de MMV unique, en SemVer, affichée et journalisée | P4-6B | **OUI** (DP-7) |
| **H8** | Journal des migrations serveur conforme à DP-8 : table dédiée **et** trace locale, écritures **hors transaction**, **six champs minimaux** présents | P4-6B | **OUI** (DP-8) |
| **H9** | Chemin SQLite sans régression ; les 14 migrations restent inchangées | P4-6B / P4-6C | **OUI** (G4, S-5) |
| **H10** | Procédures d'installation, de montée de version et de reprise (§4.6) documentées | P4-6C / P4-9 | **OUI** (critère 13) |
| **H11** | **`MMV.App` ne contient aucun chemin d'exécution de DDL PostgreSQL.** Prouvé par un test d'architecture, pas seulement par revue | P4-6B | **OUI** (DP-1) |
| **H12** | **Aucune migration ne s'exécute sans sauvegarde vérifiée** : l'outil refuse de démarrer sinon | P4-6B / P4-9 | **OUI** (DP-10, O9) |
| **H13** | La migration exige une **action explicite** de l'opérateur : aucun déclenchement au démarrage, sur minuteur ou sur détection de version. Une **référence d'opérateur** est exigée au lancement et journalisée | P4-6B | **OUI** (DP-9) |
| **H14** | **Fenêtre N-1 prouvée par test** : un poste **N-1** démarre et écrit normalement sur un schéma N (E3a) ; un poste **N-2** est bloqué sans écriture (E3b) ; un poste **plus récent** que le schéma est bloqué (E2), **sans fenêtre** | P4-6B | **OUI** (DP-3) |
| **H15** | **Discipline « étendre → migrer → contracter »** inscrite dans [CONTRIBUTING.md](../../CONTRIBUTING.md), avec l'interdiction de contracter dans la release qui étend, et l'interdiction de déployer **deux releases porteuses de schéma dans une même tournée** de postes | P4-6B | **OUI** (DP-3) |
| **H16** | La **métadonnée de compatibilité** et le **journal** sont **absents du modèle EF** — prouvé par test, pas par revue — et écrits par `MMV.DatabaseManager` **seul** | P4-6B | **OUI** (DP-3, DP-8) |
| **H17** | Le **minimum supporté est calculé** par l'outil à partir du journal, **jamais saisi** par un développeur ni codé en dur | P4-6B | **OUI** (DP-3, RB-16) |

### 8.3 Preuves attendues

Critères S-1 à S-7 du rapport de transition. S-1 à S-4 exigent un serveur PostgreSQL en CI : **P4-6B dépend
de P4-5F**. Le test S-1 (« deux postes démarrent en même temps ») touche au multi-processus, qu'ADR-008 §5.11
réserve à P4-10 (Q-13).

---

## 9. Open questions

### 9.1 Questions tranchées (consolidation, puis revue d'acceptation P4-6A)

| # | Question | Tranchée par |
|---|---|---|
| **Q-1** | autorité de migration | **DP-1** — MA-B2, `MMV.DatabaseManager` |
| **Q-4** | RL-1, RL-2 ou RL-3 ? Qui détient l'identifiant migrateur ? | **DP-5** — RL-3 ; l'opérateur le détient au moment de la migration (DP-9), il n'est stocké sur aucun poste. **Les noms des rôles restent ouverts** (P4-8) |
| **Q-5** | numérotation de MMV | **DP-7** — SemVer, source unique. **Emplacement et règle d'incrément restent à P4-6B** |
| **Q-8** | MA-B1 et la factory design-time | **sans objet** : MA-B1 est écarté (RB-5) |
| **Q-10** | « bloqué » s'entend-il après une migration réussie par le poste lui-même ? | **sans objet** : le poste ne migre jamais (DP-1, DP-4) |
| **Q-3** | **AC-1 — politique de compatibilité** : égalité stricte ou fenêtre ? | **DP-3 (revue du 22/09/2026)** — **fenêtre limitée N-1**, asymétrique, sous discipline « étendre → migrer → contracter ». L'égalité stricte est rejetée (RB-14), une fenêtre plus large aussi (RB-15) |
| **Q-14** | **AC-2 — journal des migrations serveur** : où vit-il, que contient-il ? | **DP-8 (revue du 22/09/2026)** — **journal dédié** : table en base centrale hors modèle EF **et** trace locale de l'outil ; écritures hors transaction ; six champs minimaux |
| **Q-2** | **AC-3 — qui installe et met à jour MMV en magasin ?** | **DP-9 (revue du 22/09/2026)** — **modèle mixte selon contrat** : propriétaire du magasin ou support MMV. Rôle migrateur PostgreSQL obligatoire ; aucun utilisateur applicatif ne migre ; identité **jamais câblée** dans le produit (RB-20) |
| **QD-15** | où est déclarée la version minimale supportée, qui la choisit, selon quelle règle ? | **DP-3 §5.2.3.5**, pour le **multi-poste** : déclarée **par le schéma**, **calculée** par l'outil, règle **N-1**. Le volet mono-poste et le volet commercial restent à ADR-APP-DISTRIBUTION-001 |

### 9.2 Questions ouvertes

**Aucune de ces questions ne conditionne l'acceptation.** Toutes relèvent de la **mise en œuvre** : leur
propriétaire est un lot, pas la présente décision.

| # | Question | Propriétaire | Bloque |
|---|---|---|---|
| **Q-6** | Rester sur EF Core 8 avec un verrou LK-1, ou bénéficier du verrou de migration natif d'EF Core ≥ 9 (LK-5) ? DP-2 retient la **famille** « natif PostgreSQL », compatible avec les deux. **Devient décidable dès la montée vers .NET 10** (ADR-APP-DISTRIBUTION-001 **DI-8**), qui emporte EF Core 10 : voir le [plan d'exécution .NET 10](net10-migration-execution-plan.md). **Recommandation du plan : exécuter la montée avant P4-6B, pour que Q-6 se tranche par disponibilité et non par supposition** | architecte | forme exacte du verrou (P4-6B) |
| **Q-7** | Des scripts SQL **générés** peuvent-ils servir d'exécution pour les migrations **postérieures** à la baseline (MA-B3) ? D-03.7 ne vise que la baseline. DP-1 ne l'interdit pas, dès lors que c'est l'outil qui les exécute | architecte | forme interne de l'outil |
| **Q-9** | **DP-6** : l'état de maintenance est-il tenu par une garde technique (CX-2 à CX-4) ou par une procédure vérifiée (CX-5) ? Le **principe** est arrêté ; la **forme** ne l'est pas | architecte | P4-6B, P4-6C |
| **Q-11** | Faut-il une **revérification en cours de session** (poste démarré avant une migration) ? DP-4 tranche le **démarrage** ; DP-6 (état de maintenance) réduit le risque sans le supprimer, puisqu'aucune revérification n'existe aujourd'hui | architecte | P4-6B, lié à Q-9 |
| **Q-12** | **E7** : quel comportement face à une base PostgreSQL qui contient des tables **sans** historique EF ? | architecte | P4-6B |
| **Q-13** | Le test S-1 (concurrence de migration) relève-t-il de P4-6, avec des connexions multiples dans un seul processus, ou de P4-10 (multi-processus, ADR-008 §5.11) ? | architecte | P4-6B |
| **Q-15** | Règle de rédaction : interdire, ou encadrer, les opérations de migration qui ne peuvent pas s'exécuter en transaction (`PG_BEHAVIOR — À CONFIRMER`) ? **Rejoint la règle « étendre → migrer → contracter »** de DP-3, qui entre dans CONTRIBUTING.md par le même lot | architecte | P4-6B |
| **Q-21** | **Nouvelle (revue P4-6A).** Comment **contrôler automatiquement** le respect d'« étendre → migrer → contracter » ? Une revue humaine est le seul garde-fou aujourd'hui, et DP-3 en dépend (§6.3, risque nouveau) | architecte | qualité de P4-6B, pas son démarrage |
| **Q-22** | **Nouvelle (revue P4-6A).** Que fait l'outil face à une base dont le **journal est absent ou incomplet**, alors que `__EFMigrationsHistory` porte plusieurs migrations — cas d'une base migrée avant l'introduction du journal ? Le minimum supporté n'est alors pas calculable | architecte | P4-6B |
| **Q-16** | **C-1** : `EnsureCreated()` du seed de démonstration compte-t-il comme un chemin de production ? Son comportement sous un rôle sans DDL est `UNKNOWN` | architecte | P4-6C |
| **Q-17** | Classement V1 des sept sujets multi-poste sans lot (rapport de transition §3.3) : dans P4-6A, mais **hors de cet ADR** ? | architecte | gouvernance |
| **Q-18** | Gouvernance. **Volet enregistrement : clos.** Le découpage P4-6A/B/C et l'absorption de P4-5G sont inscrits dans [la roadmap P4](P4-multi-poste-roadmap.md) **et enregistrés par le commit documentaire** de la revue d'acceptation (résidu **R-1** clos, constat A-1 de RECON-B-light). **Reste ouvert** : l'arbitrage de l'**inversion P4-4 ↔ P4-5** (constat A-2) | architecte | rien — l'acceptation n'en dépend pas |
| **Q-19** | Faut-il un `Application Name` imposé par MMV dans la chaîne de connexion, pour l'inventaire des sessions (CX-4) et le diagnostic ? | architecte (+ P4-8) | DP-6 |
| **Q-20** | Nom de fichier : les ADR de la série suivent la forme `adr-prod-db-00N-<objet>.md`. Ce document est nommé `ADR-PROD-DB-009.md`, comme demandé. Faut-il le renommer à l'acceptation ? | architecte | aucun |

---

## 10. Références

- [ADR-PROD-DB-001 §6 — impact P4 et production](adr-prod-db-001-multi-poste-database-strategy.md)
- [ADR-PROD-DB-002 §15 — obligations O8, O9, O10, O11, O14](adr-prod-db-002-server-database-provider-selection.md)
- [ADR-PROD-DB-005 — architecture des migrations (§5.4, §5.8, §5.10, G7)](adr-prod-db-005-migration-architecture.md)
- [ADR-PROD-DB-007 — prévention de la dérive (§5.5, §5.7, §5.8, S7)](adr-prod-db-007-schema-drift-prevention.md)
- [ADR-PROD-DB-008 — tests d'intégration PostgreSQL (§5.11)](adr-prod-db-008-postgresql-integration-testing.md)
- [ADR-APP-DISTRIBUTION-001 — installation, distribution et mise à jour](adr-app-distribution-001-installation-and-updates.md)
  (PROPOSED — décisions validées, preuves SD-n en attente) : DI-3.4 version minimale supportée · DI-4 ordre
  « base d'abord » · DI-5 livraison de `MMV.DatabaseManager` · DI-7 retour arrière · DI-9 séparation
  installation/données · **OI-13** une seule release porteuse de schéma par tournée · QD-14, QD-15
- [Plan d'exécution de la montée .NET 10](net10-migration-execution-plan.md) : lot `P4-NET10`, dépendance de Q-6
- [Roadmap P4 — §P4-6 et son découpage A/B/C, §3, §4](P4-multi-poste-roadmap.md)
- [Rapport d'acceptation P4-6A](../implementation/P4-6A-adr-acceptance-report.md)
- [Rapport de consolidation P4-6A](../implementation/P4-6-architecture-consolidation-report.md)
- [Audit P4-5E-A — U-1 … U-8, D-09 … D-18](../implementation/P4-5E-A-postgresql-migration-architecture-audit-report.md)
- [Décisions P4-5E-B — D-03.7, D-05, D-08, *Deferred Decisions*](../implementation/P4-5E-B-architecture-decisions.md)
- [Clôture P4-5D-R — D-B4, RR6](../implementation/P4-5D-R-closure-report.md)
- [P4-1 Lot C — laboratoire Windows et réseau](../implementation/P4-1-windows-network-lot-c-report.md)
- [CONTRIBUTING.md — règle de double migration](../../CONTRIBUTING.md)
- [Rapport de transition P4-6](../implementation/P4-6-transition-audit-report.md)
- [Rapport RECON-B-light](../implementation/P4-RECON-B-light-report.md)

---

## 11. Addendum P4-6B — 4 octobre 2026

> **Addendum autorisé par décision d'architecte du 4 octobre 2026.** Il ferme les questions de mise en œuvre
> dont P4-6B est propriétaire et fixe la **forme** de DP-2 (DP-2.4) et le **calcul** de DP-3.5.3. **Aucune
> décision du §5.2 n'est rouverte.** Preuves : [rapport P4-6B](../implementation/P4-6B-database-lifecycle-report.md)
> — locales sur PostgreSQL 17.10 tant que la CI n'a pas tourné sur le SHA exact.

### 11.1 Forme du verrou (DP-2.4) — plan P4-6B §3.3 amendé

1. **LK-1** : verrou consultatif de **session**, clé constante unique (`0x4D4D564D49475231`, « MMVMIGR1 »),
   déclarée en un seul point ; acquisition par `pg_try_advisory_lock` dans une attente **bornée** ; libération
   explicite par `pg_advisory_unlock` (un verrou de session **survit** au retour de la connexion dans le pool
   Npgsql — mesure M-3).
2. **Session unique** : le verrou est détenu par **la même session PostgreSQL** que celle qui exécute
   `Migrate()`. Une seule connexion, ouverte par l'acquisition du verrou et tenue ouverte jusqu'à sa libération,
   porte toute la séquence : verrou → métadonnée → GRANT → maintenance → journal OPEN → `Migrate()` →
   vérification → métadonnée → journal CLOSE → maintenance `NULL` → libération. Si cette session meurt, la
   migration meurt avec elle ; EF ne peut pas la rouvrir (intercepteur de connexion), l'outil non plus.
   **Il n'existe jamais une session de verrou distincte d'une session de migration.**
3. Le verrou natif d'EF (chez Npgsql : `LOCK TABLE … ACCESS EXCLUSIVE`, `LockReleaseBehavior = Transaction`,
   mesure M-1) reste actif dessous, sans être désactivé ni pris pour référence.
4. `lock_timeout` est posé sur cette session (CX-1).

### 11.2 Calcul de la métadonnée (DP-3.5.3) — plan P4-6B §4.3 amendé

Une **ancre** est un **état physique du schéma vérifié** — pas nécessairement une exécution qui a elle-même
appliqué une migration.

1. Seul un état **vérifié** (exécution réussie : migration, vérification et droits assurés) peut être une ancre.
2. L'état vérifié (Appliquées après) est comparé à celui de la **dernière ancre** : identiques ⇒ aucune
   évolution de compatibilité (une release sans migration ne consomme aucun cran) ; différents ⇒ nouvelle ancre.
3. Chaque migration de l'état est attribuée à la release qui l'a **physiquement** appliquée, d'après le journal
   (y compris une exécution ensuite en échec ou interrompue). `schema_version` = la release qui a produit le
   schéma présent ; `minimum_supported_version` = la release qui a produit le schéma **immédiatement
   précédent**. La version de l'outil qui relance n'est **jamais** prise par défaut.
4. `adopt-compatibility` est une ancre explicite et un point de départ ; aucun littéral de version, aucune
   saisie manuelle du minimum.

### 11.3 Questions fermées

| # | Réponse |
|---|---|
| **Q-6** | **LK-1** retenu, en session unique (§11.1) ; le verrou natif EF reste dessous |
| **Q-7** | **Non en V1** : l'outil appelle `Migrate()` ; K-7 et l'interdit n° 2 de CONTRIBUTING.md restent la règle |
| **Q-9** | **CX-3 minimal + CX-5 + CX-1** : `maintenance_started_at` (signal d'admission à sens unique, jamais un verrou, nettoyé seulement sous verrou), procédure « postes fermés », `lock_timeout` ; CX-2 et CX-4 écartés pour la V1 |
| **Q-11** | **Non en V1** : pas de revérification en cours de session (P4-6C) |
| **Q-12** | **Blocage** du poste en E7 ; l'adoption appartient à l'outil |
| **Q-13** | S-1 prouvé en P4-6B (un processus, deux exécutions concurrentes) ; multi-processus : **P4-10** |
| **Q-15** | **Migrations non transactionnelles interdites en V1** (CONTRIBUTING.md, Interdit n° 10) |
| **Q-21** | **Test d'énumération des contractions + liste d'autorisation** (`ExpandMigrateContractTests`) |
| **Q-22** | `migrate` refuse (code 15) une base migrée sans métadonnée ; `adopt-compatibility` pose `schema_version = minimum = version courante`, journalisé `adopt` ; côté poste, métadonnée absente ⇒ égalité stricte, ligne en initialisation ⇒ bloqué |
| **Q-23** | **CLOSED (04/10/2026)** — P4-8 possède les rôles et leurs noms ; `--app-role` obligatoire pour `migrate` et `adopt-compatibility` ; `GRANT USAGE ON SCHEMA mmv_meta` + `GRANT SELECT ON mmv_meta.schema_compatibility`, idempotents, cités par le serveur ; aucun droit sur `migration_run` ; jamais de succès sans droits assurés |
| **Q-24** | **CLOSED (04/10/2026)** — `RefusingBackupVerification` seule en production ; ordre **P4-6B → P4-9 → P4-6C** |

**Q-20** (renommage du fichier) reste ouverte et ne bloque rien.

---

**ACCEPTED — 22 septembre 2026.** Dix points de décision arrêtés, dix-sept obligations (H1 … H17), vingt
alternatives rejetées (RB-1 … RB-20). **Aucune implémentation** : aucun fichier `.cs`, aucune migration, aucune
CI, aucun paquet. Mise en œuvre par **P4-6B** (préalable technique : **P4-5F**), **P4-6C**, **P4-8** et **P4-9**.
