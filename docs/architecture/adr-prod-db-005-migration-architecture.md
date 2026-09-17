# ADR-PROD-DB-005 — Architecture des migrations PostgreSQL (deux chaînes, un seul modèle)

> **Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation).**
> Cet ADR tranche **l'obligation O7** d'[ADR-PROD-DB-002 §15](adr-prod-db-002-server-database-provider-selection.md) :
> comment MMV entretient une **chaîne de migrations PostgreSQL** sans toucher aux **14 migrations SQLite
> historiques**. Il **ne crée aucune migration, aucun projet, aucun `DbContext`, et ne modifie aucun fichier
> `.cs`, `.csproj`, de configuration ou de CI.** L'implémentation appartient à **P4-5E**.
>
> **C'est la décision la plus structurante du lot P4-5** : le découpage de P4-5 en dépend.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de décision :
> `b49f2ff7c4e3af665a767f508268377be9d19943`.
> Entrées : [audit P4-5A §Migration Analysis, AD-3](../implementation/P4-5A-postgresql-schema-audit-report.md) ·
> [ADR-PROD-DB-002 §15 O7](adr-prod-db-002-server-database-provider-selection.md) ·
> [roadmap P4 §P4-5](P4-multi-poste-roadmap.md).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.**

---

## 2. Contexte

### 2.1 Les 14 migrations SQLite ne sont pas portables

`STATIC_CODE_PROOF` (audit P4-5A §19) :

| Construction | Où | Pourquoi bloquante |
|---|---|---|
| `.Annotation("Sqlite:Autoincrement", true)` | `InitialCreate`, 10+ tables | annotation de provider, inopérante hors SQLite |
| `GLOB '*[^ -~]*'` | `AddProductNormalizedReferenceAndProtectHistory` | **`GLOB` n'existe pas** en PostgreSQL |
| `GLOB` + `lower(trim(…))` | `AddNormalizedUsernameAndSecureLocalUsers` | `GLOB` absent ; `lower()` ne se comporte pas pareil |
| tables `CHECK (col = '')` pour **avorter** | 2 migrations | astuce d'arrêt SQLite |
| `filter: "\"IsCurrent\" = 1"` | `AddWorkshopSheets` | **invalide** en PostgreSQL ([ADR-PROD-DB-006](adr-prod-db-006-index-and-model-portability.md)) |
| ~90 `AlterColumn` (rebuilds de table) | 8 migrations | SQLite reconstruit la table, PostgreSQL fait un vrai `ALTER` — chemins **incomparables** |

### 2.2 Ces migrations ne doivent pas être modifiées

**Interdit par [ADR-PROD-DB-002 §15 O7](adr-prod-db-002-server-database-provider-selection.md) et par le
critère de sortie P4 n° 7.** Les modifier romprait le hachage d'historique des bases SQLite **déjà
déployées** et provoquerait re-migrations ou échecs **sur des données réelles**.

### 2.3 La contrainte technique centrale — un seul instantané par assembly de migrations

EF Core entretient **un** `ModelSnapshot` par couple (`DbContext`, assembly de migrations). Deux chaînes de
migrations pour un même modèle exigent donc **deux emplacements d'instantané** — il n'y a pas d'autre
lecture possible.

Or **le modèle EF de MMV devient volontairement dépendant du provider** par deux décisions distinctes et
déjà prises : le **type physique monétaire** ([ADR-PROD-DB-003 §5.4](adr-prod-db-003-money-persistence.md))
et le **filtre d'index booléen** ([ADR-PROD-DB-006 §5](adr-prod-db-006-index-and-model-portability.md)). Ce
n'est donc pas seulement l'historique qui diffère : **l'instantané lui-même diffère**. La séparation des
instantanés n'est pas un contournement d'outillage, c'est la **conséquence directe** de ces deux décisions.

### 2.4 Deux pièges déjà identifiés

- **La factory design-time est figée sur SQLite, volontairement**
  ([OpticDbContextFactory.cs:20-24](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L20-L24)) : elle
  ignore délibérément `MMV_DATABASE_PROVIDER`, et son commentaire renvoie explicitement ce déverrouillage à
  P4-5. Générer une migration PostgreSQL **exige** de le lever, **sans casser la CI**.
- **Le contrôle de dérive de la CI tourne sans configuration serveur**, donc **contre SQLite**
  ([ci.yml](../../.github/workflows/ci.yml)). Avec deux chaînes, une seule serait vérifiée →
  [ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md).

### 2.5 L'historique SQLite n'a aucune valeur sur une base neuve

Les 14 migrations décrivent l'évolution d'une base **existante**. Une base PostgreSQL neuve n'a **aucun passé
à rattraper** : une **baseline propre** est plus courte, plus lisible et plus vérifiable.

---

## 3. Question de décision

> **Comment MMV entretient-il une chaîne de migrations PostgreSQL et une chaîne SQLite pour un seul modèle
> métier, sans toucher à l'historique SQLite et sans perdre la détection de dérive ?**

---

## 4. Options comparées

### Option 1 — **Deux assemblys de migrations, un seul `DbContext`, un seul modèle**

La chaîne SQLite reste dans `MMV.Infrastructure`. Une **seconde assembly de migrations** porte la baseline
PostgreSQL **et son propre instantané**. Le choix se fait à la configuration EF
(`MigrationsAssembly(...)`), au même endroit que le choix du provider.

- **Avantages** : **un seul** `OpticDbContext`, **un seul** jeu de configurations d'entités, **un seul**
  modèle métier ; les 14 migrations SQLite sont **physiquement intouchées** ; forme **supportée par EF Core**
  pour le multi-provider ; la détection de dérive reste possible **sur les deux chaînes** ; la sélection vit
  là où la sélection de provider vit déjà (P4-3).
- **Inconvénients** : un projet supplémentaire ; la factory design-time doit devenir sélective ; la CI doit
  exécuter **deux** contrôles de dérive.

### Option 2 — **Deux `DbContext` de migrations** dérivés d'une configuration commune

- **Avantages** : pas de projet supplémentaire.
- **Inconvénients** : **deux types de contexte** pour une base unique, alors que **tous** les repositories,
  `UnitOfWork`, `EfTransactionRunner` et la composition DI dépendent d'`OpticDbContext` ; risque permanent
  qu'un contexte reçoive une configuration que l'autre n'a pas ; complexité d'outillage (`--context` partout) ;
  **aucun gain** sur la contrainte réelle de §2.3. **Rejetée.**

### Option 3 — **Baseline PostgreSQL en script SQL écrit à la main**, hors migrations EF

- **Avantages** : contrôle total du DDL.
- **Inconvénients** : supprime `has-pending-model-changes` sur la chaîne serveur, donc **la seule garde
  automatique contre la dérive** ; rend le schéma de production **non reproductible par l'outillage** ;
  contredit le critère de sortie n° 4 (« base neuve migrée deux fois à l'identique »). **Rejetée.**

---

## 5. Décision

**Option 1 est retenue.**

1. **Un seul modèle métier, un seul `DbContext`.** `OpticDbContext` et les configurations d'entités restent
   la **source unique** de vérité du modèle. Aucun second contexte n'est créé.
2. **Deux assemblys de migrations :**
   - **SQLite** — les **14 migrations existantes** et leur instantané restent **exactement où ils sont**,
     dans `MMV.Infrastructure`, **sans la moindre modification**.
   - **PostgreSQL** — une **assembly de migrations dédiée**, portant une **baseline propre** et **son propre
     instantané**.
3. **La sélection de l'assembly de migrations se fait au même endroit que la sélection du provider**, dans
   [`DatabaseProviderResolver`](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs) :
   un seul point de décision, déjà établi et testé par P4-3.
4. **La baseline PostgreSQL est générée par l'outillage EF, jamais écrite à la main.** Toute retouche
   manuelle d'une migration générée doit être **exceptionnelle, commentée et justifiée** — le cas prévu et
   légitime étant une **migration de données** (par exemple celle exigée par
   [ADR-PROD-DB-004 §7.2](adr-prod-db-004-datetime-strategy.md)).
5. **La factory design-time devient sélective par provider**, avec **SQLite par défaut** : sans variable
   d'environnement, le comportement actuel — et donc la CI existante — est **inchangé au caractère près**.
   Le déverrouillage annoncé dans son propre commentaire est ainsi honoré, sans régression.
6. **Aucune connexion à un serveur n'est requise pour générer une migration PostgreSQL.** La chaîne de
   connexion design-time provient d'une variable d'environnement, **jamais du dépôt**, et peut désigner un
   serveur inexistant : la génération ne se connecte pas.
7. **Règle d'entretien, non négociable :** à partir de P4-5E, **tout changement du modèle produit, dans le
   même commit, la migration correspondante sur chacune des deux chaînes** — ou, si le changement ne concerne
   qu'un provider (§2.3), la **preuve explicite** que l'autre chaîne est inchangée. Le contrôle de dérive
   double d'[ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md) rend cette règle **vérifiable
   automatiquement**.
8. **`__EFMigrationsHistory` de la base PostgreSQL est neuf** et n'est **jamais** alimenté depuis SQLite
   (rappel d'O11, exécuté en **P4-7**).
9. **L'assembly de migrations PostgreSQL appartient à l'anneau Infrastructure.** Domain et Application ne la
   référencent pas et restent **provider-neutres** (**O14**, verrouillée par
   `ProviderNeutralityArchitectureTests`).
10. **L'application des migrations en multi-poste n'est pas décidée ici** : `Migrate()` par poste est
    **inadapté** à une base centrale. Ce sujet reste **entier** et appartient à **P4-6** (obligation O8).

---

## 6. Ce que cet ADR ne dit pas

- Il ne décide **pas** du nom des projets, des dossiers ni des migrations : ce sont des détails
  d'implémentation de P4-5E, contraints seulement par §5.
- Il ne décide **pas** qui applique les migrations en production, ni quand, ni comment les postes sont
  sérialisés → **P4-6 / O8**.
- Il ne traite **pas** l'import des données → **P4-7 / O11**.
- Il ne crée **aucune** migration et **ne génère aucune** baseline.

---

## 7. Conséquences

### 7.1 Positives

- **Les 14 migrations SQLite sont protégées structurellement**, pas seulement par consigne : elles vivent
  dans une autre assembly que la chaîne serveur.
- **La baseline PostgreSQL est propre** : pas de rebuilds de table, pas de `GLOB`, pas de tables d'arrêt —
  le schéma serveur est lisible en un seul fichier.
- **La détection de dérive reste possible sur les deux chaînes**, sans serveur.
- **Un seul modèle métier** : aucune duplication des configurations d'entités, donc aucune divergence
  silencieuse entre deux définitions du même agrégat.
- **Le gel design-time de P4-3 est levé exactement comme son commentaire le prévoyait**, sans rien casser.

### 7.2 Négatives — assumées

- **Un projet supplémentaire** à construire, référencer et maintenir.
- **Deux instantanés à faire vivre** : le modèle avance à deux endroits. Sans le double contrôle de dérive,
  une chaîne pourrait prendre du retard **en silence** — c'est le risque R-5 de l'audit P4-5A, et il est
  **transféré** à [ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md), qui existe pour cela.
- **La CI s'allonge** de deux contrôles et d'un restore supplémentaire.
- **Le diagnostic devient légèrement plus subtil** : un développeur doit savoir **quelle chaîne** il
  regarde. À documenter dans la procédure opérateur.

### 7.3 Neutres

- Le code applicatif (repositories, use cases, UI) ne voit **aucune** différence : le contexte reste unique.
- La composition DI existante est préservée.

---

## 8. Obligations d'implémentation créées

| # | Obligation | Lot | Bloquante |
|---|---|---|---|
| **G1** | Créer l'assembly de migrations PostgreSQL (baseline + instantané propre) | P4-5E | **OUI** |
| **G2** | Sélectionner l'assembly de migrations **dans `DatabaseProviderResolver`**, avec SQLite par défaut | P4-5E | **OUI** |
| **G3** | Rendre `OpticDbContextFactory` sélective par provider, **défaut SQLite inchangé** | P4-5E | **OUI** |
| **G4** | Vérifier que les **14** migrations SQLite et leur instantané sont **inchangés** (`git diff` vide sur `src/MMV.Infrastructure/Migrations/`) | P4-5E | **OUI** |
| **G5** | Prouver la création d'une base PostgreSQL neuve par migrations, **deux fois à l'identique** (critère de sortie n° 4) | P4-5F | **OUI** |
| **G6** | Documenter la règle d'entretien §5.7 dans la documentation de contribution | P4-5E | **OUI** |
| **G7** | Transmettre à P4-6 la question de l'**application** des migrations en multi-poste (O8) | P4-6 | **OUI** |

---

## 9. Conditions de réexamen

- Si EF Core offrait un instantané multi-provider natif ⇒ réexaminer §5.2.
- Si le modèle cessait d'être dépendant du provider (les deux décisions de §2.3 étant révoquées) ⇒ la
  séparation resterait justifiée par l'historique seul, **mais** l'argument serait plus faible : à réexaminer.
- Si un troisième provider devait être supporté (SQL Server Express, **non éliminé** —
  [ADR-PROD-DB-002 §19.2](adr-prod-db-002-server-database-provider-selection.md)) ⇒ le motif se généralise
  à une troisième assembly, **sans** changer la décision.

---

## 10. Références

- [ADR-PROD-DB-002 §15 O7, §19.2](adr-prod-db-002-server-database-provider-selection.md)
- [Audit P4-5A §Migration Analysis, AD-3](../implementation/P4-5A-postgresql-schema-audit-report.md)
- [ADR-PROD-DB-003 — persistance monétaire](adr-prod-db-003-money-persistence.md)
- [ADR-PROD-DB-006 — index et portabilité du modèle](adr-prod-db-006-index-and-model-portability.md)
- [ADR-PROD-DB-007 — prévention de la dérive de schéma](adr-prod-db-007-schema-drift-prevention.md)
- [Rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md)
