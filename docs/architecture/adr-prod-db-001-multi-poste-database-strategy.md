# ADR-PROD-DB-001 — Stratégie base de données production multi-poste (MMV V1)

> **Statut : ACCEPTÉ (cadrage P3-0B — décision d'orientation produit + architecture, AUCUNE implémentation).**
> Cet ADR fixe la **stratégie base de données de production** de MMV V1 et acte que la **production est
> multi-poste**. Il **ne modifie aucun code source, aucun test, aucune migration** et ne change **pas** le
> provider actuel (SQLite reste en place pour dev/test/démo/mono-poste local). Il décide la **trajectoire**.
>
> Références : [roadmap P3](P3-business-rules-roadmap.md), [audit P3-0](../implementation/P3-0-business-audit-report.md),
> [ADR frontières](adr-application-boundaries.md), [adr-candidates §concurrence](adr-candidates.md),
> [adr-stock-concurrency](adr-stock-concurrency.md), [rapport P3-0B](../implementation/P3-0B-multi-poste-db-strategy-report.md).
>
> Date : 8 juillet 2026. Branche : `p3-business-rules`. **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.**

Décision produit **imposée** et non négociable pour V1 :

> **MMV V1 production doit être multi-poste.** Ce n'est **pas** une option future. C'est **indispensable**
> pour un vrai magasin d'optique.

L'implémentation (choix final du SGBD, migration de provider, tokens de concurrence, versioning
schéma/app) est **hors** P3-0B et sera menée dans les étapes ultérieures listées en §7 et §8.

---

## 2. Contexte

### 2.1 Réalité métier

- MMV vise des **magasins d'optique réels**, pas un usage individuel.
- Un magasin a typiquement **plusieurs personnes** (opticien, monteur/technicien, vendeur, gérant) et
  **plusieurs ordinateurs** (comptoir de vente, poste atelier, back-office).
- Ces postes doivent **partager les mêmes données en temps réel** : clients, ordonnances, stock, commandes,
  ventes, fiches atelier et utilisateurs. Une vente au comptoir doit décrémenter un stock que l'atelier voit
  immédiatement ; une fiche atelier générée sur un poste doit être lisible sur un autre.
- Le multi-poste **n'est pas** du multi-tenant : il s'agit d'**un seul magasin, une seule base**, partagée
  par plusieurs postes du **même** magasin. Le SaaS / `Organization` / `Store` / multi-tenant reste **hors
  périmètre** (cf. [roadmap P3 §hors périmètre](P3-business-rules-roadmap.md)).

### 2.2 État actuel du dépôt (audit P3-0B, vérifié)

- **Provider unique : SQLite.** La production enregistre le `DbContext` via `options.UseSqlite(...)`
  ([App.axaml.cs:119-120](../../src/MMV.App/App.axaml.cs#L119-L120)). L'inscription infrastructure alternative
  (inerte) fait de même ([DependencyInjection.cs:37](../../src/MMV.Infrastructure/DependencyInjection.cs#L37)).
  Les tests utilisent également SQLite (fichier ou `:memory:`). **Aucun** provider PostgreSQL / SQL Server
  n'existe dans le code (`Npgsql` / `UseSqlServer` : 0 occurrence dans `src`/`tests` — seulement dans des
  documents SaaS/roadmap/archive).
- **Chemin de base = local, par machine.** Résolu par
  [`SqliteDatabasePathResolver`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) :
  variable `MMV_DATABASE_PATH`, sinon configuration `OpticDatabase`, sinon défaut
  `%LOCALAPPDATA%\ManageMyVision\mmv.db`. **Chaque poste possède donc, par défaut, son propre fichier local
  et isolé** — il n'existe aujourd'hui **aucun** partage de base entre postes.
- **Migrations appliquées au démarrage, par poste.**
  [`SqliteDatabaseManager.PrepareDatabase`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs)
  détecte l'état de la base et applique `context.Database.Migrate()` (sauvegarde préalable, adoption des bases
  historiques, journal). C'est robuste **pour une base locale mono-poste**, mais conçu pour un **seul
  processus** appliquant les migrations sur **sa** base.
- **Transactions** : toute écriture multi-étapes passe par `ITransactionRunner` /
  [`EfTransactionRunner`](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs).
- **Stock atomique** :
  [`EfStockMutationService`](../../src/MMV.Infrastructure/Persistence/EfStockMutationService.cs) réalise un
  **UPDATE conditionnel atomique** (`SET StockQuantity = StockQuantity - @q WHERE StockQuantity >= @q` via
  `ExecuteUpdateAsync`). Le pattern est **correct au niveau SQL** (la base décide en une instruction) et donc
  déjà **favorable** au multi-poste, **mais** son commentaire assume explicitement l'hypothèse SQLite
  « écrivain unique, écritures sérialisées ». La numérotation
  ([`EfNumberSequenceService`](../../src/MMV.Infrastructure/Persistence/EfNumberSequenceService.cs)) suit le
  même principe d'incrément atomique conditionnel.
- **Aucune stratégie de concurrence au niveau ligne** : `0` occurrence de `RowVersion` / `IsRowVersion` /
  `IsConcurrencyToken` dans le modèle. L'enum `PersistenceException.Concurrency` existe mais **aucun jeton**
  n'est configuré. Le sujet est déjà cadré comme candidat dans [adr-candidates §concurrence](adr-candidates.md)
  (SQLite n'a pas de `rowversion` natif ; options : jeton `Guid`/entier applicatif, update conditionnel,
  stratégie propre au futur SGBD serveur).

### 2.3 Le problème

SQLite est un moteur **embarqué, mono-fichier, écrivain unique**. Il a été **très utile** en dev/test/solo :
zéro installation, base locale rapide, cycle de vie maîtrisé (sauvegarde/adoption/migrations).

Mais **SQLite partagé sur un dossier réseau ne doit pas être la base officielle de production multi-poste** :
le verrouillage de fichiers SQLite sur SMB/CIFS/NFS est **notoirement peu fiable** (verrous consultatifs mal
honorés → **corruption de base**, « database is locked », écritures perdues). SQLite lui-même **déconseille**
l'usage sur systèmes de fichiers réseau. Un magasin d'optique ne peut pas risquer la corruption de son stock,
de ses ventes ou de ses ordonnances.

Il faut donc, pour la **production multi-poste**, une base **client/serveur** conçue pour les accès
concurrents réseau.

---

## 3. Décision

1. **MMV V1 production doit supporter le multi-poste.** C'est une exigence produit, pas une option future.
2. **SQLite local reste autorisé** pour **dev / test / démo / mono-poste local**. On ne le retire pas ; il
   reste le moteur par défaut pour ces usages (cycle de vie actuel conservé).
3. **SQLite sur dossier réseau n'est PAS supporté officiellement** comme base de production multi-poste. Ce
   n'est pas une configuration recommandée ni testée ; elle sera explicitement **déconseillée**.
4. **Production multi-poste ⇒ base client/serveur obligatoire** (serveur DB local au magasin, postes clients).
5. **Candidat principal : PostgreSQL** (local au magasin).
6. **Candidat secondaire : SQL Server Express** (local au magasin).
7. Le **choix final PostgreSQL vs SQL Server Express** est **différé** et sera tranché par un **spike
   technique** dédié (provider EF Core, migrations provider-specific, installation serveur, backup/restore,
   tests multi-connexion). **Non tranché en P3-0B.**
8. **Cloud / API centrale / SaaS** sont **reportés** (hors V1 production initiale).

**Conséquence transverse pour P3** : toute règle métier P3 doit être conçue **compatible multi-poste / base
centrale partagée** (cf. §5). SQLite local reste utile pour dev/test/démo mais **ne doit pas guider les
décisions de production multi-poste**.

---

## 4. Options comparées

### Option A — SQLite local, un fichier par poste

- **Description** : configuration actuelle. Chaque poste a sa base `%LOCALAPPDATA%\...\mmv.db`.
- **Avantages** : zéro installation ; rapide ; cycle de vie maîtrisé (sauvegarde/adoption/migrations) ;
  parfait pour dev, tests automatisés, démo, poste unique.
- **Inconvénients** : **données non partagées** entre postes — chaque poste a sa propre vérité, divergente.
- **Risques** : incohérence totale du stock/ventes en magasin réel multi-poste.
- **Compatibilité V1 production multi-poste** : **aucune** (données isolées).
- **Verdict** : **OK dev / test / démo / mono-poste local. PAS production multi-poste.**

### Option B — SQLite partagé sur dossier réseau

- **Description** : un unique fichier `mmv.db` sur un partage réseau (SMB/NAS), pointé par tous les postes.
- **Avantages** : partage apparent des données sans installer de serveur DB.
- **Inconvénients** : verrouillage de fichiers SQLite **non fiable** sur réseau ; hypothèse « écrivain
  unique » violée par plusieurs postes ; « database is locked », contention forte.
- **Risques** : **corruption de la base**, écritures perdues, indisponibilité — **inacceptable** pour un
  magasin (stock, ventes, ordonnances).
- **Compatibilité V1 production multi-poste** : **non supporté** (anti-pattern reconnu, déconseillé par SQLite).
- **Verdict** : **NON SUPPORTÉ officiellement. Interdit comme base de production.**

### Option C — PostgreSQL local au magasin (client/serveur)

- **Description** : serveur PostgreSQL installé sur un poste/serveur du magasin ; les postes s'y connectent
  en TCP. Base centrale unique partagée.
- **Avantages** : conçu pour les **accès concurrents réseau** ; transactions ACID robustes multi-clients ;
  MVCC (bonne concurrence lecture/écriture) ; tokens de concurrence natifs (`xmin`) ; **open source, gratuit,
  sans licence** ; excellent support EF Core (`Npgsql`) ; backup/restore matures (`pg_dump`).
- **Inconvénients** : installation/administration d'un serveur local ; sauvegarde à organiser ; migration de
  provider et migrations provider-specific à préparer.
- **Risques** : montée en compétence exploitation ; différences SQL SQLite↔PostgreSQL à couvrir par tests.
- **Compatibilité V1 production multi-poste** : **excellente**.
- **Verdict** : **CANDIDAT PRINCIPAL.**

### Option D — SQL Server Express local au magasin (client/serveur)

- **Description** : SQL Server Express installé localement au magasin ; postes clients connectés.
- **Avantages** : conçu client/serveur ; concurrence robuste ; `rowversion` natif ; très bon support EF Core
  (`Microsoft.EntityFrameworkCore.SqlServer`) ; outillage Windows familier.
- **Inconvénients** : **limites d'édition Express** (taille de base ~10 Go, RAM/CPU plafonnés) ; écosystème
  plus lié à Windows ; conditions de licence à vérifier selon l'usage.
- **Risques** : plafond Express atteignable à long terme ; dépendance plus forte à l'écosystème Microsoft.
- **Compatibilité V1 production multi-poste** : **bonne**.
- **Verdict** : **CANDIDAT SECONDAIRE.**

### Option E — Cloud / API centrale / SaaS

- **Description** : base centrale hébergée + API ; postes clients via réseau/Internet.
- **Avantages** : accès multi-sites, sauvegardes centralisées, base des évolutions SaaS futures.
- **Inconvénients** : nécessite Internet fiable ; refonte majeure (API, auth, sécurité, latence, RGPD,
  hébergement) ; coût récurrent ; dépendance connectivité pour un magasin.
- **Risques** : indisponibilité si coupure Internet ; complexité et périmètre très supérieurs à V1.
- **Compatibilité V1 production multi-poste** : possible mais **surdimensionné** pour un magasin unique.
- **Verdict** : **Intéressant plus tard. HORS V1 production initiale.**

**Synthèse** : A = OK hors production multi-poste ; B = non supporté ; **C = candidat principal** ;
D = candidat secondaire ; E = ultérieur.

---

## 5. Impact P3

> **Principe** : **toutes les règles métier P3 doivent être compatibles multi-poste** (base centrale partagée
> par plusieurs postes). Aucune étape P3 ne doit introduire d'hypothèse mono-utilisateur / mono-poste.

Règles transverses à respecter dès maintenant, **même si le provider reste SQLite en dev** :

- **Pas d'hypothèse mono-utilisateur** : plusieurs postes peuvent lire/écrire la même donnée en même temps.
- **Transactions robustes** : toute écriture multi-étapes reste sous `ITransactionRunner` (jamais d'écriture
  composée hors transaction).
- **Stock atomique obligatoire** : tout décrément passe par `IStockMutationService` (UPDATE conditionnel) ;
  **jamais** de décrément direct en lecture-modification-écriture (le défaut connu
  `AdvanceOrderStatusUseCase` : `Product.StockQuantity -= …`, cf. [audit P3-0 §7](../implementation/P3-0-business-audit-report.md)
  est **doublement** dangereux en multi-poste et doit être corrigé en P3-5).
- **Pas de cache local trompeur** : ne pas décider une règle métier sur une valeur lue puis supposée stable ;
  en multi-poste, la donnée peut avoir changé sur un autre poste entre lecture et écriture.
- **Conflits concurrents à prévoir** : la stratégie de jeton de concurrence
  ([adr-candidates §concurrence](adr-candidates.md)) devra être activée avec le SGBD serveur.
- **Fiches atelier centralisées / versionnées** : la fiche doit être **persistée** (pas seulement une vue UI
  éphémère) pour être lisible/partagée entre le comptoir et l'atelier, et **versionnée**.
- **Workflow commande compatible plusieurs postes** : les transitions de statut doivent rester correctes si
  deux postes agissent sur la même commande.
- **Vente atomique avec rollback** : la vente + décrément stock + numérotation restent atomiques (déjà via
  `ITransactionRunner`) ; en cas d'échec (stock épuisé par un autre poste), rollback complet.

**Impacts spécifiques par étape :**

| Étape | Impact multi-poste |
|---|---|
| **P3-5 — Stock / mouvements** | **Critique.** Unifier le décrément atomique ; supprimer le décrément direct ; refuser le négatif ; pas de double décrément. Cœur de la sûreté multi-poste. |
| **P3-6 — Commandes** | **Critique.** Machine à états explicite robuste à des transitions concurrentes depuis plusieurs postes. |
| **P3-6B — Fiche atelier** | **Doit être persistée/centralisée** et versionnée (pas une vue UI locale) pour partage comptoir↔atelier. |
| **P3-7 — Ventes** | **Critique.** Vente atomique, montants recalculés côté serveur, pas de double décrément, rollback sur conflit stock. |

---

## 6. Impact P4 / production

- **Updates applicatives** : rester nécessaires **sur chaque poste** (chaque poste exécute le binaire MMV).
- **Migrations DB** : en production multi-poste, elles concernent **une base centrale** — pas N bases locales.
  Le mécanisme actuel `PrepareDatabase → Database.Migrate()` **par poste** n'est **plus** adapté tel quel :
  plusieurs postes ne doivent pas appliquer une migration concurremment.
- **Backup obligatoire avant migration** : conserver le principe (sauvegarde avant mutation) mais l'adapter au
  SGBD serveur (`pg_dump` / sauvegarde SQL Server) sur la base centrale.
- **Un seul poste/processus applique une migration à la fois** : sérialiser l'application des migrations
  (verrou / poste désigné / phase de maintenance) pour éviter les migrations concurrentes.
- **Compatibilité version app ↔ version schéma DB** : au démarrage, chaque poste doit **vérifier** que sa
  version applicative est compatible avec le schéma de la base centrale. Un **client trop ancien** face à un
  schéma **trop récent** doit être **bloqué** (message clair, pas d'écriture hasardeuse) plutôt que de
  corrompre les données.

---

## 7. Impact infrastructure future

Travaux probables (hors P3-0B, à planifier) :

- **Configuration du provider DB** : sélection SQLite (dev/solo) vs SGBD serveur (production) par
  configuration/environnement.
- **Support PostgreSQL ou SQL Server** dans `MMV.Infrastructure` (packages provider, `UseNpgsql`/`UseSqlServer`).
- **Migrations provider-specific** si nécessaire (types, valeurs par défaut, colonnes de concurrence).
- **Installation d'un serveur local au magasin** (procédure, service, port, pare-feu).
- **Backup / restore central** (planification, rétention, test de restauration).
- **Tests multi-poste / multi-connexion** (concurrence réelle, contention, conflits).
- **Gestion des conflits de concurrence** (activation d'un jeton — cf. [adr-candidates](adr-candidates.md) /
  [adr-stock-concurrency](adr-stock-concurrency.md)).
- **Stratégie de licence par boutique / poste** (plus tard, hors V1).

---

## 8. Non-décisions (explicitement hors P3-0B)

- **Pas** de choix final PostgreSQL vs SQL Server Express (différé à un spike).
- **Pas** de migration DB en P3-0B.
- **Pas** de changement de provider en P3-0B (SQLite reste en place).
- **Pas** de SaaS.
- **Pas** de cloud obligatoire.
- **Pas** de multi-tenant / `Organization` / `Store`.
- **Pas** de licence implémentée.
- **Pas** d'updater implémenté.
- **Pas** de jeton de concurrence implémenté (cadré, non activé).
- **Pas** de vérification version app ↔ schéma implémentée (décidée comme exigence, non codée).

---

## 9. Conséquences

- **Positives** : la trajectoire production est **cadrée et explicite** ; les décisions P3 seront prises en
  connaissance de la contrainte multi-poste, évitant des règles métier à refaire ; l'anti-pattern « SQLite
  réseau » est **officiellement écarté** avant qu'il ne s'installe par facilité.
- **Coûts / dette** : un futur travail d'infrastructure (provider serveur, migrations, installation, backup,
  versioning, concurrence) est acté. Il est **assumé** et séquencé hors P3.
- **Réversibilité** : SQLite restant en dev/test/démo/solo, aucune régression immédiate ; le choix
  PostgreSQL vs SQL Server Express reste ouvert jusqu'au spike.
