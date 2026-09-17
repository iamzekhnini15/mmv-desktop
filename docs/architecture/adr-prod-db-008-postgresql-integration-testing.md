# ADR-PROD-DB-008 — Stratégie de tests d'intégration PostgreSQL

> **Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation).**
> Cet ADR décide **comment MMV prouve son comportement contre un vrai serveur PostgreSQL** : où vivent ces
> tests, qui fournit le serveur, quand ils s'exécutent, et ce qui est interdit. Il **ne crée aucun projet,
> aucun test, et ne modifie aucun fichier `.cs`, `.csproj`, de solution ou de CI.** L'implémentation
> appartient à **P4-5F**.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de décision :
> `b49f2ff7c4e3af665a767f508268377be9d19943`.
> Entrées : [audit P4-5A §Test Coverage Analysis, AD-6, R-7](../implementation/P4-5A-postgresql-schema-audit-report.md) ·
> [ADR-PROD-DB-002 §6.2, §15 O12](adr-prod-db-002-server-database-provider-selection.md) ·
> [roadmap P4 §4 critères 4, 5, 6](P4-multi-poste-roadmap.md).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.**

---

## 2. Contexte

### 2.1 L'état mesuré

- **143 fichiers de test** dans `MMV.sln`, baseline **1569** tests. **78 fichiers** câblent `UseSqlite`,
  **114 sites** appellent `EnsureCreated()`.
- **5 fichiers** mentionnent PostgreSQL ou Npgsql — `DatabaseProviderResolverTests`,
  `PersistenceErrorMapperPostgreSqlTests`, `ProviderNeutralityArchitectureTests`,
  `LowStockCreationProviderPortabilityTests`, `UnitOfWorkTests` — et **aucun ne se connecte à un serveur** :
  ce sont des tests de résolution, de classification, d'architecture et de dialecte. **Ils ont leur valeur ;
  ils ne prouvent rien du serveur.**
- **Zéro test d'intégration serveur dans `MMV.sln`.** Le harness `spikes/P4.ProviderComparison` (E1→E21) est
  **hors solution** : la CI ne l'exécute pas et ne l'exécutera pas sans modification explicite du workflow
  (ADR-PROD-DB-002 §6.2).
- Tout le corpus de concurrence est **même processus, multi-connexion SQLite**.

### 2.2 Pourquoi le compromis actuel cesse d'être tenable

Garder le harness hors solution a été **le bon choix** jusqu'ici : il a permis de mesurer deux providers sans
toucher au workflow. Mais à partir de P4-5, **des critères de sortie P4 dépendent de preuves serveur** —
critère 4 (migrations reproductibles), 5 (primitives re-prouvées), 6 (erreurs traduites), et les obligations
M4/M5 ([ADR-PROD-DB-003](adr-prod-db-003-money-persistence.md)), T7 ([ADR-PROD-DB-004](adr-prod-db-004-datetime-strategy.md)),
S3/S4 ([ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md)).

**Sans serveur en CI, toutes ces preuves restent locales** — exactement la situation que l'ADR-PROD-DB-002 et
la roadmap signalent comme à **ne jamais confondre** avec une preuve reproduite à distance.

### 2.3 Le risque à conjurer en premier : le faux vert

Un test d'intégration qui **s'ignore** faute de serveur et qu'on lit comme un succès est **pire que l'absence
de test** : il produit de la confiance sans preuve. C'est le risque R-7 de l'audit P4-5A, et toute solution
retenue doit le rendre **structurellement impossible**.

### 2.4 Une contrainte de terrain, constatée

L'audit P4-5A n'a pu exécuter **aucun** test : la seule source NuGet enregistrée sur le poste de travail est
un cache hors ligne, et `nuget.org` est inatteignable (§0.2). **Toute solution exigeant un paquet NuGet
nouveau est donc, aujourd'hui, non exécutable localement.** Ce n'est pas un argument d'architecture, mais
c'est un fait opérationnel qui pèse sur le choix de l'outillage.

---

## 3. Question de décision

> **Où vivent les tests d'intégration PostgreSQL, qui leur fournit un serveur, quand s'exécutent-ils, et
> comment garantit-on qu'un test non exécuté ne passe jamais pour un test réussi ?**

---

## 4. Options comparées

### 4.1 Emplacement

| Option | Description | Verdict |
|---|---|---|
| **E1 — projet d'intégration dédié dans `MMV.sln`** | nouveau projet de test, séparé de la suite unitaire | **RETENUE** |
| **E2 — promouvoir le harness du spike** | l'intégrer à la solution | **REJETÉE** — il est écrit pour **comparer des providers**, porte encore SQL Server et un `AdaptedOpticDbContext` **divergent** du modèle de production : il validerait autre chose que MMV |
| **E3 — ajouter les tests serveur aux projets existants** | mélange unitaire/intégration | **REJETÉE** — rendrait la suite des **1569** tests dépendante d'un serveur, donc non exécutable hors CI |

### 4.2 Fourniture du serveur

| Option | Description | Verdict |
|---|---|---|
| **P1 — chaîne de connexion par variable d'environnement** ; CI fournit le serveur par un **service conteneurisé** du workflow ; en local, installation native ou conteneur au choix du développeur | un seul contrat : la variable | **RETENUE** |
| **P2 — Testcontainers** (le test démarre lui-même un conteneur) | isolation parfaite, zéro configuration | **REJETÉE comme mécanisme obligatoire** — impose **Docker sur chaque poste de développement** et un **paquet NuGet nouveau**, aujourd'hui non restaurable (§2.4) ; ajoute une dépendance d'exécution à la CI. **Non éliminée** : elle reste adoptable plus tard **derrière le même contrat de variable**, sans rien changer aux tests |
| **P3 — serveur partagé, permanent** | une base de test commune | **REJETÉE** — état partagé entre exécutions concurrentes, nettoyage fragile, secret durable à gérer |

---

## 5. Décision

1. **Un projet de test d'intégration dédié, inscrit dans `MMV.sln`**, séparé des projets unitaires. Il cible
   **PostgreSQL uniquement** — SQLite est déjà couvert par la suite existante.
2. **Le serveur est désigné par une variable d'environnement de test**, distincte de la variable de
   production. **Un seul contrat**, quelle que soit la provenance du serveur : conteneur de CI, installation
   Windows native (celle du Lot C P4-1), ou conteneur local.
3. **La CI fournit réellement le serveur**, par un **service conteneurisé PostgreSQL** déclaré dans le
   workflow, dans un **job distinct** du job unitaire actuel. Les identifiants de ce service sont des
   **identifiants de test éphémères, non secrets**, définis dans le workflow ; **aucune chaîne de connexion
   de production n'est jamais committée** (rappel O10).
4. **Interdiction du faux vert — deux verrous cumulatifs :**
   - en **local**, sans variable, les tests **s'ignorent explicitement**, avec un message disant **ce qui n'a
     pas été prouvé et comment fournir un serveur** ;
   - en **CI**, le job d'intégration **échoue** si un test d'intégration est ignoré, ou si le nombre de tests
     exécutés est nul. **Un `skip` en CI est un échec, jamais un succès.**
5. **Le schéma de test est créé par migrations**, jamais par `EnsureCreated()`
   ([ADR-PROD-DB-007 §5.5](adr-prod-db-007-schema-drift-prevention.md)). **Les migrations sont l'objet même
   du test** : les contourner validerait un schéma que personne ne déploie.
6. **Isolation** : chaque exécution travaille sur une **base créée puis supprimée**, jamais sur une base
   persistante. Aucun test ne dépend de l'ordre d'exécution ni d'un état laissé par un autre.
7. **La baseline de tests reste lisible** : le compte **1569** désigne la suite unitaire. Les tests
   d'intégration sont comptés **séparément** et ne doivent **jamais** être fusionnés dans ce chiffre — sans
   quoi une baseline « en hausse » masquerait un serveur absent.
8. **Corpus obligatoire** — chacun est un **critère de sortie**, pas un test de confort :

   | # | Test | Prouve | Obligation |
   |---|---|---|---|
   | **N1** | création d'une base neuve par migrations, **deux fois à l'identique** | critère 4 | ADR-005 G5, ADR-007 S4 |
   | **N2** | **vérification physique** des colonnes, types, index uniques, **deux index filtrés et leur prédicat**, FK et comportements de suppression | schéma réel | ADR-006 X5, ADR-007 S3 |
   | **N3** | **fidélité monétaire** : bornes, deux décimales, arrondi bancaire, égalité **exacte** ; `"RemainingAmount" > 0` et `SUM("FinalAmount")` | O2 | ADR-003 M4/M5/M6 |
   | **N4** | **fidélité `DateTime`** : écriture UTC, relecture, `Kind`, ordre chronologique — **avec assertion** | O4 | ADR-004 T7 |
   | **N5** | **CRUD complet** de chaque entité via les **repositories de production** | compatibilité applicative | critère 3 |
   | **N6** | **re-preuve des 14 primitives** sur PostgreSQL via leurs implémentations de production | **O12** | critère 5 |
   | **N7** | **rollback et `25P02`** : erreur en transaction puis commande suivante ; re-vérification au runtime des `catch` intra-transactionnels | O6 (aujourd'hui **statique**) | critère 6 |
   | **N8** | divergence `lower()` verrouillée par test, **sur chaque provider** | comportement de recherche | ADR-006 X6 |

9. **Séparation stricte unitaire / intégration.** La suite unitaire reste **rapide, sans serveur, exécutable
   hors ligne**. Aucun test unitaire n'acquiert de dépendance serveur.
10. **Le harness `spikes/P4.ProviderComparison` reste où il est**, hors solution, **comme trace** du spike
    P4-1. Il n'est ni promu, ni supprimé, ni maintenu.
11. **La concurrence multi-processus n'est pas dans ce périmètre** : elle appartient à **P4-10** (O13), avec
    perte réseau, reconnexion, timeout et interblocage.

---

## 6. Ce que cet ADR ne dit pas

- Il ne fixe **pas** la version majeure de PostgreSQL utilisée en CI : elle doit être **alignée sur la
  version d'exploitation cible** décidée en P4-8, et cet alignement est une obligation de P4-8, pas de P4-5F.
- Il ne décide **pas** de la forme du workflow (noms de jobs, matrice, cache).
- Il ne couvre **pas** les tests de résilience et de concurrence multi-processus (**P4-10**).
- Il n'écrit **aucun** test.

---

## 7. Conséquences

### 7.1 Positives

- **Les preuves serveur cessent d'être locales** : elles sont reproduites à distance, sur le SHA exact, comme
  toute la discipline P4 l'exige.
- **Le faux vert devient structurellement impossible** en CI (§5.4).
- **P4-4 est débloquée** : N6 est la re-preuve des 14 primitives (O12), aujourd'hui `BLOCKED_BY_P4_5_SCHEMA`.
- **O6 passe du statique au runtime** par N7 — l'audit P4-5A l'avait jugée « satisfaite par construction »,
  ce qui est un constat de lecture, pas une preuve d'exécution.
- **Aucune dépendance NuGet nouvelle n'est imposée** : la contrainte de terrain de §2.4 est respectée.

### 7.2 Négatives — assumées

- **`.github/workflows/**` est modifié** pour y ajouter un job avec service. Avec
  [ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md), ce sont les **premières modifications
  substantielles du workflow depuis P4-0** — à décider consciemment, et à faire **sans toucher** au reste.
- **La CI s'allonge** d'un job complet, démarrage de serveur compris.
- **Le développeur qui n'a pas de serveur ne voit pas ces tests s'exécuter.** Le message d'ignorance doit
  donc être **explicite et actionnable** — c'est une exigence, pas un détail de confort.
- **Une classe de tests devient dépendante d'un service externe**, donc sujette à des échecs d'infrastructure
  distincts des échecs de code. À diagnostiquer comme tels, **sans jamais rendre le job non bloquant**.

### 7.3 Neutres

- La suite des **1569** tests garde son comportement, sa durée et son indépendance.
- Le harness du spike reste inchangé.

---

## 8. Obligations d'implémentation créées

| # | Obligation | Lot | Bloquante |
|---|---|---|---|
| **Q1** | Créer le projet d'intégration PostgreSQL et l'inscrire dans `MMV.sln` | P4-5F | **OUI** |
| **Q2** | Contrat de connexion par variable d'environnement de test, distincte de la production | P4-5F | **OUI** |
| **Q3** | Job CI avec service PostgreSQL, **bloquant**, identifiants de test non secrets | P4-5F | **OUI** |
| **Q4** | Garde anti-faux-vert : **un test ignoré en CI est un échec** | P4-5F | **OUI** |
| **Q5** | Schéma créé **par migrations** ; base créée puis supprimée à chaque exécution | P4-5F | **OUI** |
| **Q6** | Livrer **N1 … N8** | P4-5F | **OUI** |
| **Q7** | Ne pas fusionner le compte des tests d'intégration avec la baseline unitaire | P4-5F | **OUI** |
| **Q8** | Aligner la version PostgreSQL de CI sur la version d'exploitation cible | P4-8 | **OUI** |

---

## 9. Conditions de réexamen

- Si Docker devenait disponible et acquis sur tous les postes, et la restauration NuGet possible ⇒
  **Testcontainers redevient candidate** (P2), derrière le **même** contrat de variable, sans réécrire les
  tests.
- Si la durée du job d'intégration devenait pénalisante ⇒ **paralléliser ou segmenter**, jamais rendre le job
  non bloquant ni le déplacer hors de la CI.
- Si un test d'intégration se révélait instable ⇒ **en traiter la cause** ; le marquer « connu instable » est
  une forme de faux vert et reste interdit.

---

## 10. Références

- [Audit P4-5A §Test Coverage Analysis, §22, §23, AD-6, R-7](../implementation/P4-5A-postgresql-schema-audit-report.md)
- [ADR-PROD-DB-002 §6.2, §15 O12, §16.5](adr-prod-db-002-server-database-provider-selection.md)
- [ADR-PROD-DB-003](adr-prod-db-003-money-persistence.md) · [ADR-PROD-DB-004](adr-prod-db-004-datetime-strategy.md) ·
  [ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md) · [ADR-PROD-DB-006](adr-prod-db-006-index-and-model-portability.md) ·
  [ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md)
- [Rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md)
