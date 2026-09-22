  # P4-6 — Analyse des décisions d'architecture multi-poste (V1)

> **Note d'enregistrement (22 septembre 2026).** Ce rapport est **enregistré au dépôt** par le commit documentaire
> de la [revue d'acceptation P4-6A](P4-6A-adr-acceptance-report.md). Les mentions « non commité », « aucun commit »
> ou « non suivi » ci-dessous décrivent l'état **au moment de sa rédaction** : elles restent exactes pour le lot
> qu'elles décrivent, qui n'a produit **aucun commit de code**.

  > **Statut : ANALYSE DE DÉCISION — AUCUNE IMPLÉMENTATION.** Aucun fichier `.cs`, test, migration, instantané EF,
  > configuration, workflow CI, ADR ni roadmap n'est modifié. Ce rapport est le seul fichier créé. Il n'est pas
  > commité.
  >
  > Date : 22 septembre 2026 · Branche : `p4-multi-poste` · HEAD : **`ccbeb3259444d26264e20323058764dd59318e1c`**
  > (= `origin/p4-multi-poste`) · Destinataire : architecte principal.
  >
  > **Rôle de ce document.** `docs/architecture/ADR-PROD-DB-009.md` (statut PROPOSED, **non suivi**) recense les
  > options de P4-6 sans en retenir aucune. Ce rapport **recommande une option pour chaque décision**, avec sa
  > justification, afin que l'architecte puisse statuer sur l'ADR. Il ne l'accepte pas et ne la modifie pas.
  >
  > **Méthode.** Lecture du code à HEAD (`src/`, `tests/`, `.github/workflows/ci.yml`, `Directory.Build.props`,
  > `.csproj`), des ADR PROD-DB 001 à 008, de la roadmap P4, des rapports P4-0, P4-1 (spike et Lot C), P4-5E-A,
  > P4-5E-B et P4-5D-R, et de quatre documents **non suivis**, cités sans lien : ADR-PROD-DB-009, rapport de
  > transition P4-6, rapport RECON-B-light, roadmap P5. Commandes : `git`, `grep`, `sed`, `cat`. **Aucun build ni
  > test relancé** : aucun fichier compilé n'a changé depuis la vérification finale P4-5 (1953 / 1953 verts, `DOC`).
  >
  > **Le dépôt réel prime toujours sur ce document.**

  **Légende des preuves**

  | Étiquette | Sens |
  |---|---|
  | `CODE` | constaté par lecture du code à HEAD |
  | `TEST` | constaté dans un test existant |
  | `DOC` | énoncé d'un document du dépôt (ADR, roadmap, rapport) |
  | `EF_BEHAVIOR — À CONFIRMER` | comportement documenté d'EF Core, de Npgsql ou du SDK .NET, **non exécuté ici** |
  | `PG_BEHAVIOR — À CONFIRMER` | comportement documenté de PostgreSQL, **non exécuté ici** |
  | `EXTERNE` | fait extérieur au dépôt, non vérifié depuis le dépôt |
  | `HYPOTHÈSE` | supposition de ce rapport, listée au §2 |
  | `UNKNOWN` | absent du code et de la documentation : rien n'est inventé |

  **Identifiants propres à ce rapport** (choisis pour ne pas entrer en collision avec D-xx, DP-x, Hx, Q-x, K-x et
  U-x des documents existants) : **REC-n** recommandations · **HY-n** hypothèses · **P-n** préconditions de
  l'outil · **E1 … E8** états de démarrage d'un poste · **S-n** preuves attendues (S-1 à S-7 reprennent le rapport de
  transition) · **RS-n** risques résiduels · **Q-An** questions à l'architecte.

  **Noms proposés.** Les noms de rôles (`mmv_owner`, `mmv_app`), de commandes (`status`, `backup`, `migrate`) et
  d'états sont des **propositions** de ce rapport. Aucun n'existe dans le dépôt. Le nom `MMV.DatabaseManager` vient
  du mandat.

  ---

  ## 0. Résumé exécutif

  **L'architecture recommandée, en une phrase.** Aucun poste ne modifie jamais le schéma PostgreSQL. Un outil
  console dédié, `MMV.DatabaseManager`, lancé sur la machine serveur par l'administrateur technique sous un rôle
  migrateur, est le **seul** acteur qui exécute du DDL. Chaque poste se contente d'une garde de version **en lecture
  seule** et d'un verrou de présence.

  | # | Sujet | Recommandation V1 |
  |---|---|---|
  | **REC-1** | Autorité de migration | **Outil séparé `MMV.DatabaseManager`**. Aucune migration PostgreSQL depuis `MMV.App`. SQLite mono-poste inchangé |
  | **REC-2** | Qui déclenche | Le détenteur de l'identifiant migrateur, **depuis la machine serveur**, via l'outil, préconditions satisfaites. Un compte MMV `Admin` ne donne aucun droit de migration |
  | **REC-3** | Verrouillage | **Un verrou consultatif PostgreSQL de session, une clé, deux modes** : exclusif pour l'outil, partagé pour chaque poste. Pas de table de verrou |
  | **REC-4** | Versions et compatibilité | Version MMV unique (SemVer) dans `Directory.Build.props`. Version de schéma = migrations inscrites dans `__EFMigrationsHistory`. Compatibilité = **égalité stricte** entre migrations appliquées et migrations connues du binaire, pour les deux chaînes |
  | **REC-5** | Poste administrateur | Pas de « poste désigné » dans MMV. Le poste administrateur est **la machine serveur**, désignée par PostgreSQL (`pg_hba.conf`) et par la possession de l'identifiant migrateur, qui n'est stocké sur aucun poste client |
  | **REC-6** | `MMV.DatabaseManager` | **Nouveau projet console .NET 8** dans `MMV.sln`, sans UI ni paquet nouveau. Coquille mince : la logique vit dans `MMV.Infrastructure`, partagée avec `MMV.App`. V1 : `status`, `backup`, `migrate` |
  | **REC-7** | Rôles PostgreSQL | Superutilisateur d'installation · `mmv_owner`, propriétaire et migrateur · `mmv_app`, DML seul, **lecture seule sur l'historique EF** |
  | **REC-8** | Sauvegarde avant migration | `pg_dump -F c` **obligatoire et intégré** à `migrate`, après le verrou et avant le premier DDL. Archive vérifiée. Échec de sauvegarde = aucune migration |
  | **REC-9** | Postes secondaires | L'outil **refuse** de migrer tant qu'un poste tient le verrou partagé. Un poste qui démarre pendant une migration affiche « maintenance en cours » et n'écrit rien. La règle « tous postes fermés » (D-B4) devient une **garde technique** |
  | **REC-10** | Mise à jour | **Semi-automatique, avec une validation humaine unique** : l'opérateur décide et lance une commande ; l'outil enchaîne contrôles, sauvegarde, migration et vérification. Les postes sont ensuite mis à jour à la main (aucun updater en V1) |

  **Pourquoi cette combinaison**

  1. **Aucun droit DDL sur les postes.** U-2 (migrations concurrentes) et U-4 (DDL au runtime) disparaissent par
    construction. O10 (moindre privilège) est satisfaite sans exception.
  2. **Une seule primitive serveur couvre trois besoins** : exclure un second migrateur, détecter les postes ouverts,
    bloquer les démarrages pendant une maintenance.
  3. **Aucun changement du modèle EF.** P4-6 ne génère donc aucune migration, sur aucune des deux chaînes, et
    n'introduit aucune seconde source de vérité.
  4. **Aucun paquet NuGet nouveau**, contrainte réelle du poste de développement (ADR-008 §2.4).
  5. **Chaque garantie se prouve avec deux sessions PostgreSQL dans un seul processus**, donc dans le projet
    d'intégration de P4-5F.

  **Ce qu'elle coûte** : un livrable de plus ; un opérateur technique pour toute release qui contient une
  migration ; les postes indisponibles entre la migration et leur mise à jour ; une connexion serveur de plus par
  poste.

  **Hypothèse déterminante : HY-2** (une personne technique installe et met à jour la base). **Aucun document ne
  l'établit.** Si elle est fausse, REC-1 doit être réexaminée (§4.1).

  ```
  P4-6 ARCHITECTURE ANALYSIS   = COMPLETE — RECOMMENDATIONS ONLY — NOTHING IMPLEMENTED
  MIGRATION AUTHORITY          = MMV.DatabaseManager (console) — NO DDL FROM MMV.App ON POSTGRESQL
  LOCK                         = PG ADVISORY SESSION LOCK — ONE KEY — EXCLUSIVE (TOOL) / SHARED (POSTES)
  VERSION GUARD                = APPLIED == KNOWN (STRICT, ON MIGRATIONS) — BOTH CHAINS — READ-ONLY ON POSTES
  ROLES                        = SUPERUSER (INSTALL) · mmv_owner (MIGRATOR) · mmv_app (DML, HISTORY READ-ONLY)
  BACKUP BEFORE MIGRATION      = MANDATORY pg_dump -F c INSIDE migrate — VERIFIED — NO BYPASS
  EF MODEL / MIGRATIONS        = UNCHANGED BY P4-6
  NEW NUGET PACKAGES           = NONE
  KEY HYPOTHESIS               = HY-2 (TECHNICAL OPERATOR) — UNDOCUMENTED — PRODUCT CONFIRMATION REQUIRED
  ```

  ---

  ## 1. Périmètre

  **Dans le périmètre** : les dix sujets du mandat, soit l'obligation **O8** d'ADR-002 et les décisions reportées
  D-10, D-11, D-16, D-17, D-18, avec U-1, U-2, U-4, U-5 et D-B4 / RR6.

  **Hors périmètre, avec la contrainte que P4-6 leur transmet** (§6.3) :

  - provisioning de la base, stockage des secrets, TLS, politique d'échec au démarrage : **P4-8** (D-09, D-12 à
    D-15) ;
  - sauvegarde planifiée, rétention, restauration outillée : **P4-9** (O9) ;
  - import SQLite vers PostgreSQL : **P4-7** (O11) ;
  - tests multi-processus et de résilience : **P4-10** (O13) ;
  - les sept sujets multi-poste sans lot attribué (rapport de transition §3.3 : *lost update*, invalidation de
    session, autorisation dans Application, fraîcheur, audit métier, niveau d'isolation, recoupement P5).

  Quand une recommandation touche un de ces lots, ce rapport fixe **ce que P4-6 exige**, pas la mise en œuvre.

  ---

  ## 2. Hypothèses de travail

  Chaque recommandation qui en dépend le signale.

  | # | Hypothèse | Fondement dans le dépôt | Conséquence si elle est fausse |
  |---|---|---|---|
  | **HY-1** | Un magasin compte **quelques postes** (comptoir, atelier, back-office) | ADR-001 §2.1 : « typiquement ». Aucun chiffre | au-delà de quelques dizaines de postes : revoir le coût d'une connexion de présence par poste et la mise à jour synchronisée |
  | **HY-2** | L'installation du serveur et les mises à jour de la base sont faites par **une personne technique** : technicien, prestataire ou gérant formé | **aucun document** (DR-6 d'ADR-009 : `UNKNOWN`). Indice : installer PostgreSQL exige un service Windows, une règle de pare-feu (dont le correctif de profil `Any`), `pg_hba.conf` et des rôles (Lot C §17.2-17.3, ADR-002 §17). C'est déjà un travail technique | si un opticien non technique doit migrer seul : une interface graphique au-dessus des **mêmes** services Infrastructure (§4.1, réexamen) |
  | **HY-3** | Le serveur PostgreSQL tourne sur une machine Windows du magasin, **avec `pg_dump` et `pg_restore`** de la même version majeure | Lot C : PostgreSQL 17.10 natif, `pg_dump` et `pg_restore` 17.10 utilisés (§17.2, §17.7). Contenu de l'installeur de production : `EXTERNE` | la sauvegarde intégrée exige un chemin `pg_dump` configurable. Sans outil disponible, `migrate` refuse |
  | **HY-4** | Une **fenêtre de maintenance** de quelques minutes, tous postes fermés, est acceptable pour une release qui contient une migration | ADR-001 §6 (postes mis à jour un par un, migrations sérialisées) ; D-B4 impose déjà « tous postes fermés ». **Non validé auprès d'utilisateurs** | si l'arrêt est inacceptable : fenêtre de compatibilité VS-2 et discipline « étendre puis contracter » (§4.4, réexamen) |
  | **HY-5** | La base est assez petite pour un `pg_dump` complet avant chaque migration | volumétrie réelle `NOT_PROVED` (ADR-002 §18) | fenêtre plus longue, ou sauvegarde récente vérifiée acceptée (P4-9) |
  | **HY-6** | Les releases qui contiennent une migration sont **peu fréquentes** | aucune mesure | le coût de la mise à jour synchronisée augmente avec leur fréquence |
  | **HY-7** | **Aucun updater** en V1 : les binaires sont mis à jour à la main sur chaque poste | ADR-001 §8 (« Pas d'updater ») ; phase-1 audit, l. 253 (ni installeur ni mécanisme de mise à jour) ; roadmap P5 (non suivie) : « P8 — Mise à jour applicative » après P5 | un updater permettrait d'orchestrer « base, puis postes ». L'ordre recommandé reste valable |
  | **HY-8** | `MMV.App` et `MMV.DatabaseManager` sortent de **la même release**, avec la même version et la même assembly de migrations | proposition de ce rapport | sinon, un contrôle de version croisé entre l'outil et la base devient nécessaire |

  ---

  ## 3. Socle factuel commun (état du code à HEAD)

  Les sections du §4 renvoient à ce tableau pour ne pas le répéter.

  | # | Constat | Preuve |
  |---|---|---|
  | F-1 | Tout démarrage PostgreSQL est bloqué par un garde-fou placé **avant** le `try` | `CODE` [App.axaml.cs:205-214](../../src/MMV.App/App.axaml.cs#L205-L214) |
  | F-2 | Ce garde-fou lève dans `ConfigureServices`, appelé **avant toute fenêtre** : aucun message n'est affiché | `CODE` [App.axaml.cs:41-46](../../src/MMV.App/App.axaml.cs#L41-L46) |
  | F-3 | `DatabaseMigrationException` est relancée sans affichage ; **toute autre erreur est avalée** et l'application continue | `CODE` [App.axaml.cs:267-278](../../src/MMV.App/App.axaml.cs#L267-L278) |
  | F-4 | La branche PostgreSQL de `Configure` appelle `UseNpgsql` avec l'assembly de migrations dédiée. **Aucune** autre option (ni `Application Name`, ni délai) | `CODE` [DatabaseProviderResolver.cs:123-136](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L123-L136) |
  | F-5 | L'application ne lit **qu'une** chaîne de connexion : `MMV_DATABASE_CONNECTION_STRING` | `CODE` [DatabaseProviderResolver.cs:39](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L39) |
  | F-6 | La chaîne PostgreSQL contient **une** migration, `20260922001219_InitialPostgreSqlBaseline`. Un test le verrouille | `CODE` [Migrations/](../../src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/) ; `TEST` [MigrationChainsTests.cs:143-150](../../tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs#L143-L150) |
  | F-7 | L'assembly de migrations PostgreSQL ne peut contenir **que** migrations, instantané et factory déléguante | `TEST` [MigrationChainsTests.cs:212-224](../../tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs#L212-L224) |
  | F-8 | `MMV.App` référence l'assembly de migrations PostgreSQL pour que la DLL soit déployée | `CODE` [MMV.App.csproj:51](../../src/MMV.App/MMV.App.csproj#L51) |
  | F-9 | Cycle SQLite : sauvegarde par copie de fichier, détection par `sqlite_master`, puis `Migrate()` **par poste** | `CODE` [SqliteDatabaseManager.cs:89-95](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L89-L95), l. 189-218, l. 1158, l. 316, 334, 596 |
  | F-10 | La vérification ne contrôle que les migrations **en attente**. Une migration appliquée mais inconnue du binaire n'est jamais détectée (U-1) | `CODE` [SqliteDatabaseManager.cs:331](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L331), l. 770 |
  | F-11 | Journal de migration : fichier local à côté du fichier SQLite, horodatage UTC | `CODE` [App.axaml.cs:222-224](../../src/MMV.App/App.axaml.cs#L222-L224) ; [MigrationJournal.cs:59](../../src/MMV.Infrastructure/Data/MigrationJournal.cs#L59) |
  | F-12 | Le seed tourne sur chaque poste, à chaque démarrage, et **écrit** dans `Users` | `CODE` [DatabaseSeeder.cs:123](../../src/MMV.Infrastructure/Configuration/DatabaseSeeder.cs#L123), l. 151, 168, 186 |
  | F-13 | Le seed de démonstration appelle `EnsureCreated()`. Il n'est actif qu'en Development / Demonstration avec `MMV_ENABLE_DEMO_SEED` (C-1) | `CODE` [DbInitializer.cs:19](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L19) ; [SeedOptions.cs:38-40](../../src/MMV.Infrastructure/Configuration/SeedOptions.cs#L38-L40) |
  | F-14 | **Aucun verrou** : 0 occurrence de `advisory`, `pg_try`, `pg_locks`, `pg_stat_activity` ou `MigrationLock` dans `src/` et `tests/` | `CODE` (`grep`) |
  | F-15 | **Aucune version MMV** : [Directory.Build.props](../../Directory.Build.props) ne contient que l'audit NuGet ; aucun `.csproj` ne définit `<Version>`. L'écran « À propos » affiche `1.0.0` **en dur** | `CODE` [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) |
  | F-16 | **Aucune identité de poste** : 0 occurrence de `MachineName`, d'identifiant de poste ou d'`Application Name` | `CODE` (`grep`) |
  | F-17 | **Aucun polling** : le seul minuteur du code est l'inactivité de session | `CODE` [SessionService.cs:62](../../src/MMV.App/Services/SessionService.cs#L62) |
  | F-18 | Rôles MMV `Admin`, `Optician`, `Technician`, vérifiés **dans `MMV.App` seulement**, après connexion | `CODE` [UserRole.cs](../../src/MMV.Domain/Enums/UserRole.cs), `PermissionService` |
  | F-19 | EF Core **8.0.27**, Npgsql EF **8.0.11**, `dotnet-ef` **8.0.27**, SDK **8.0.417**. EF Core 8 n'a pas de verrou de migration natif | `CODE` `.csproj`, [global.json](../../global.json), [.config/dotnet-tools.json](../../.config/dotnet-tools.json) ; `DOC` P4-5E-A U-2, `EF_BEHAVIOR — À CONFIRMER` |
  | F-20 | CI : un seul job Windows (build, tests, audit, deux contrôles de dérive). **Aucun service PostgreSQL**, aucun test d'intégration serveur | `CODE` [ci.yml](../../.github/workflows/ci.yml) |
  | F-21 | Laboratoire : `mmv_spike` (`LOGIN`, `CREATEDB`, propriétaire de sa base) ; `mmv_app` a créé une table à distance, donc avait des droits DDL. ADR-002 : « raccourci jetable » | `DOC` [spike P4-1, l. 956-958](P4-1-provider-comparison-spike-report.md) ; [Lot C §17.4-17.5](P4-1-windows-network-lot-c-report.md) ; [ADR-002 §21](../architecture/adr-prod-db-002-server-database-provider-selection.md) |
  | F-22 | Poste de développement : seule source NuGet = cache hors ligne. Tout paquet nouveau est **non restaurable** localement | `DOC` [ADR-008 §2.4](../architecture/adr-prod-db-008-postgresql-integration-testing.md) |
  | F-23 | Aucun installeur, aucune signature, aucun mécanisme de mise à jour dans le build | `DOC` [phase-1 audit, l. 253](../architecture/phase-1-technical-audit.md) ; [ADR-001 §8](../architecture/adr-prod-db-001-multi-poste-database-strategy.md) |

  ---

  ## 4. Décisions

  ### 4.1 Autorité de migration PostgreSQL

  **État actuel.** Sur SQLite, chaque poste migre sa base au démarrage (F-9). Sur PostgreSQL, rien ne s'exécute :
  la chaîne est désignée (F-4), puis le démarrage est bloqué (F-1). Le `Migrate()` non coordonné par poste est
  **interdit** (ADR-005 §5.10, roadmap §P4-6, ADR-001 §6). EF Core 8 n'offre aucun verrou (F-19).

  **Options**

  | # | Option | Principe |
  |---|---|---|
  | **A1** | Migration automatique, premier poste sous verrou | chaque poste peut migrer ; le premier qui obtient le verrou le fait |
  | **A2** | Migration automatique, poste désigné | un poste marqué par configuration migre ; les autres vérifient |
  | **B1** | Bundle EF | `dotnet ef migrations bundle` produit un exécutable autonome (`EF_BEHAVIOR — À CONFIRMER`) |
  | **B2** | **Outil MMV dédié, `MMV.DatabaseManager`** | console MMV qui appelle `Migrate()` entouré des gardes MMV |
  | **B3** | Script SQL idempotent | `dotnet ef migrations script --idempotent`, exécuté par `psql` |
  | **C1** | Mode administration dans `MMV.App` | même binaire ; migration sur action explicite et identifiant saisi |
  | **C2** | Hybride | l'application applique la baseline sur base vide ; un outil fait les montées de version |

  **Analyse**

  | Critère | A1 | A2 | B1 | **B2** | B3 | C1 | C2 |
  |---|---|---|---|---|---|---|---|
  | DDL depuis un poste client | oui | oui (un poste) | non | **non** | non | oui (mode admin) | oui (installation) |
  | Identifiant DDL sur les postes | tous, en permanence | un, en permanence | aucun | **aucun** | aucun | saisi à la demande | un, temporairement |
  | Moment de migration choisi | non : le premier poste mis à jour qui démarre | partiel | oui | **oui** | oui | oui | oui (montées) |
  | Sauvegarde serveur intégrable | depuis un poste (`pg_dump` sur chaque poste) | depuis le poste désigné | hors outil | **dans l'outil, sur le serveur** | hors outil | dans l'application | hors outil |
  | Gardes MMV (verrou, présence, vérification, journal) | dans l'application | dans l'application | **aucune** | **toutes** | aucune | dans l'application | partielles |
  | Contexte construit comme en production | oui | oui | **non** : factory design-time, chaîne factice, variable `MMV_DESIGNTIME_*` (D-05) | **oui** (`DatabaseProviderResolver.Configure`) | — | oui | oui |
  | Conforme à K-7 (exécution par EF, jamais par script) | oui | oui | oui | **oui** | **non** pour la baseline (D-03.7) ; non tranché ensuite (Q-7) | oui | oui |
  | Charge de preuve de la concurrence | forte | moyenne | faible | **faible** | faible | moyenne | moyenne |
  | Artefact nouveau | non | non | bundle | **projet** | script | non | outil |
  | Qui voit l'échec | un utilisateur au comptoir | l'utilisateur du poste désigné | l'opérateur | **l'opérateur** | l'opérateur | l'opérateur | les deux |

  **Risques principaux des options écartées**

  - **A1, A2** : chaque poste (ou le poste désigné) détient en permanence un identifiant capable de modifier le
    schéma, en tension directe avec O10. La migration part quand le premier poste mis à jour démarre, peut-être en
    pleine journée, pendant que les autres travaillent (U-5). La sauvegarde serveur devrait partir d'un poste, ce
    qui suppose `pg_dump` et des droits sur chaque poste. A2 : si le poste désigné est éteint, tous restent bloqués.
  - **B1** : le bundle ne porte **aucune** garde MMV (verrou, présence, sauvegarde, vérification, journal). Il crée
    le contexte par la factory design-time, dont la branche PostgreSQL repose sur une chaîne factice volontaire
    (D-05.4). L'utiliser en exploitation mélangerait design-time et production (Q-8 d'ADR-009).
  - **B3** : `psql` requis, aucune garde, et la baseline est exclue par K-7.
  - **C1** : le code DDL et la saisie d'un secret de migration vivent dans le binaire des postes. Il faut une UI
    dédiée et un verrou. Piège relevé par ADR-009 §4.4.2 : une approbation par un utilisateur MMV `Admin` lirait
    `Users` sur un schéma non encore vérifié, et un rôle MMV n'est pas un droit PostgreSQL.
  - **C2** : deux chemins à tester ; un identifiant DDL sur un poste au moins pendant l'installation.

  **REC-1 — B2 : outil MMV dédié. Aucune migration PostgreSQL depuis `MMV.App`.**

  1. C'est la seule option où **aucun poste ne détient de droit DDL**. U-2 et U-4 sont résolus par construction.
  2. Le moment de la migration est **choisi par un humain**, pendant une fenêtre annoncée (ADR-001 §6).
  3. La sauvegarde s'intègre **là où `pg_dump` existe** : la machine serveur (HY-3).
  4. L'outil exécute les migrations par l'API EF standard. K-7 et K-8 sont respectés : aucune ligne de DDL écrite à
    la main.
  5. Le code côté poste se réduit à une garde **en lecture seule**, la partie la plus simple à prouver (S-2, S-4).
  6. Le coût est maîtrisé : un projet console, sans UI ni paquet nouveau (§4.6).

  - **SQLite** : inchangé. Le `Migrate()` par poste reste légitime pour une base locale (K-12 d'ADR-009).
  - **Installation (D-11)** : la même commande `migrate`, sur une base vide, applique la baseline. Aucun chemin
    spécifique (C2 écartée).
  - **Évolution ouverte** : une interface graphique d'administration pourra plus tard appeler **les mêmes**
    services Infrastructure. C1 n'est pas fermée, elle est reportée, sans refonte à prévoir.

  **Réexamen si** : HY-2 est infirmée ; un updater applicatif apparaît ; le périmètre devient multi-magasins (hors
  P4, roadmap §3).

  **Preuves attendues** : S-4 (poste face à des migrations en attente : bloqué, sans migrer) ; S-9 (le rôle
  applicatif ne peut pas faire de DDL) ; test statique : `MMV.App` n'appelle `Migrate()` que sur la branche SQLite.

  ---

  ### 4.2 Qui peut déclencher une migration

  **État actuel.** Aujourd'hui, **tout processus MMV** migre sa base SQLite au démarrage (F-9). Les rôles MMV ne
  sont vérifiés que par l'UI, **après** la connexion, alors que la préparation de base a lieu **avant** (F-2, F-18).
  Côté PostgreSQL, seuls des rôles de laboratoire existent, avec droits DDL (F-21).

  **Options**

  | # | Déclencheur | Ce qui le garantit |
  |---|---|---|
  | T1 | tout poste, au démarrage | rien, sinon un verrou |
  | T2 | un poste désigné par configuration | un indicateur local, falsifiable |
  | T3 | un utilisateur MMV de rôle `Admin` | un rôle applicatif lu dans `Users`, sur un schéma non vérifié. Aucun lien avec les droits PostgreSQL |
  | T4 | le détenteur de l'identifiant migrateur, via l'outil | les droits PostgreSQL |
  | **T5** | **T4, depuis la machine serveur seulement** | les droits PostgreSQL et `pg_hba.conf` |

  T1 et T2 sont écartés avec A1 et A2 (§4.1). T3 confond deux systèmes de droits. T4 est correct mais laisse le
  rôle migrateur utilisable depuis n'importe quel poste du réseau.

  **REC-2 — T5.** Une migration est déclenchée par une personne qui réunit **quatre** conditions :

  1. elle exécute `MMV.DatabaseManager migrate` ;
  2. elle s'authentifie sous le rôle migrateur (§4.7) ;
  3. elle le fait depuis la machine serveur : `pg_hba.conf` n'accepte le rôle migrateur qu'en boucle locale
    (`PG_BEHAVIOR — À CONFIRMER`, mise en place en P4-8) ;
  4. les préconditions de l'outil sont toutes satisfaites :

  | # | Précondition | Sinon |
  |---|---|---|
  | **P-1** | le fournisseur est PostgreSQL | refus : l'outil ne traite jamais SQLite |
  | **P-2** | le verrou exclusif est obtenu **immédiatement** (§4.3) | refus. L'outil lit `pg_locks` et dit qui détient le verrou : un autre migrateur, ou des postes ouverts (P-3) |
  | **P-3** | aucun poste ne tient le verrou partagé | refus, avec la liste des postes ouverts (§4.9) |
  | **P-4** | migrations appliquées ⊆ migrations connues de l'outil | refus : l'outil est plus ancien que la base |
  | **P-5** | la base est reconnue : vide (E4) ou gérée par la chaîne PostgreSQL | refus pour une base qui a des tables sans historique EF (E7) |
  | **P-6** | sauvegarde réussie et vérifiée, sauf base vide (§4.8) | refus |
  | **P-7** | confirmation explicite après affichage du plan : migrations de départ et d'arrivée, liste, fichier de sauvegarde | abandon, sans effet |

  **Règles**

  - Un compte MMV `Admin` **ne donne aucun droit de migration**. Les rôles MMV sont des droits applicatifs vérifiés
    par l'UI ; ce ne sont pas des droits PostgreSQL.
  - Organisationnellement, la personne est **l'administrateur technique** du magasin (HY-2). La documentation
    opérateur (critère P4 n° 13) doit nommer ce rôle.
  - Aucun poste client ne peut déclencher une migration, même mal configuré : il ne détient pas l'identifiant
    (§4.7).

  ---

  ### 4.3 Mécanisme de verrouillage des migrations

  **État actuel.** Aucun verrou (F-14). EF Core 8 n'en fournit pas ; EF Core 9 en introduit un, dont
  l'implémentation Npgsql est `UNKNOWN` ici (P4-5E-A U-2). Npgsql applique chaque migration dans sa propre
  transaction (U-6, `EF_BEHAVIOR — À CONFIRMER`) : un verrou de transaction ne couvrirait qu'une migration.

  **Options**

  | # | Mécanisme |
  |---|---|
  | **L1** | verrou consultatif de session : `pg_try_advisory_lock`, et sa forme partagée `pg_try_advisory_lock_shared` |
  | L2 | verrou consultatif de transaction : `pg_advisory_xact_lock` |
  | L3 | table de verrou applicative : une ligne avec détenteur, horodatage, expiration |
  | L4 | combinaison L1 + L3 : L1 pour l'exclusion, L3 pour la traçabilité |
  | L5 | verrou natif d'EF Core ≥ 9 |

  L2 est écarté : il libère le verrou à la fin de **chaque** migration. L5 exige une montée d'EF Core et de Npgsql,
  hors du périmètre de P4-6, et ne fournit pas le mode partagé dont §4.9 a besoin.

  **Analyse** (`PG_BEHAVIOR — À CONFIRMER` pour toute la colonne L1)

  | Critère | **L1 consultatif de session** | L3 table de verrou | L4 combinaison |
  |---|---|---|---|
  | Exclusion garantie par | le serveur | une acquisition atomique (`UPDATE` conditionnel) | le serveur (L1) |
  | Plantage du détenteur | libéré à la fin de la session serveur | **verrou orphelin** jusqu'à expiration ou intervention | L1 libéré ; ligne L3 à nettoyer |
  | Base vide, avant la baseline | fonctionne | la table n'existe pas : DDL hors chaîne, ou installation non protégée | L3 inopérant |
  | Dépendance à une horloge | aucune | expiration fondée sur l'heure serveur | oui |
  | Effet sur le modèle EF | aucun | table dans le modèle (double migration, dérive) ou hors modèle (invisible des contrôles) | idem L3 |
  | Mode partagé, pour la présence des postes | **natif** | à construire : compteur, battement de cœur, expiration | L1 |
  | Observabilité | `pg_locks`, `pg_stat_activity` (droits : §4.7) | une requête | la meilleure |
  | Provider | PostgreSQL seul, confiné à Infrastructure (O14) | portable | PostgreSQL |
  | Coût V1 | quelques appels SQL | schéma, expiration, nettoyage, tests | la somme des deux |

  **REC-3 — L1 seul : un verrou consultatif de session, une clé, deux modes.**

  | Acteur | Mode | Rôle du verrou | Tenu pendant |
  |---|---|---|---|
  | `MMV.DatabaseManager migrate` | **exclusif** | exclut un second migrateur ; échoue si un poste est ouvert | toute l'exécution : contrôles, sauvegarde, migration, vérification |
  | `MMV.DatabaseManager backup` | **partagé** | échoue si une migration est en cours ; ne gêne pas les postes | la durée du `pg_dump` |
  | `MMV.App` (PostgreSQL) | **partagé** | signale la présence du poste ; échoue si une maintenance est en cours | toute la vie du processus |

  | # | Règle |
  |---|---|
  | R3.1 | La clé est un entier 64 bits constant, propre à MMV, défini **une seule fois** dans Infrastructure et verrouillé par un test. Sa valeur est choisie en P4-6B ; ce rapport n'en invente pas |
  | R3.2 | Toujours la forme `try` : ni l'outil ni un poste n'attendent en silence. Un refus produit un message |
  | R3.3 | Le verrou est tenu sur une **connexion dédiée, hors pool**, ouverte pour toute la durée. Motif : une connexion rendue au pool est réinitialisée, ce qui libérerait le verrou, ou le laisserait survivre dans le pool (`EF_BEHAVIOR — À CONFIRMER`). Les migrations s'exécutent sur la connexion d'EF |
  | R3.4 | Libération explicite en fin d'exécution ; automatique à la fermeture de la session |
  | R3.5 | Code PostgreSQL confiné à `MMV.Infrastructure` (O14). Aucune abstraction dans Domain ni dans Application |
  | R3.6 | **Défense en profondeur.** Si la connexion dédiée tombait en pleine migration, le verrou serait libéré. Un poste qui démarrerait alors verrait des migrations en attente ou inconnues, et la garde de version le bloquerait (§4.4). Le verrou n'est pas la seule barrière |

  **Pas de table de verrou (L3, L4).** Elle coûterait une table sur deux chaînes, un risque d'orphelin, une
  dépendance à l'horloge et une impossibilité sur base vide, pour un gain d'observabilité que `pg_locks` et
  l'`Application Name` fournissent déjà (§4.9).

  **Indépendant de la version d'EF.** Une montée vers EF ≥ 9 (Q-6 d'ADR-009) ne remplacerait pas le mode partagé.
  Le verrou natif pourrait coexister avec celui-ci. REC-3 ne dépend donc pas de cette décision.

  **Preuve attendue : S-1.** Deux sessions, un seul verrou exclusif obtenu. Un verrou consultatif appartient à une
  **session**, pas à un processus (`PG_BEHAVIOR — À CONFIRMER`). Deux connexions dans un même processus prouvent donc
  la sémantique du verrou, dans le projet d'intégration, en P4-6B. Le test à deux exécutables relève de P4-10. Cela
  répond à Q-13 d'ADR-009.

  ---

  ### 4.4 Détection des versions et compatibilité poste / base

  **État actuel.** Aucune version MMV ; « À propos » affiche `1.0.0` en dur (F-15). La version de schéma n'existe
  qu'implicitement : identifiants de migrations compilés, face aux lignes de `__EFMigrationsHistory`, dont la colonne
  `ProductVersion` est la version **d'EF**, pas celle de MMV (P4-5E-A). Seules les migrations en attente sont
  contrôlées (F-10).

  #### (a) Version de l'application

  | Option | Limite |
  |---|---|
  | **`<Version>` unique dans `Directory.Build.props`** | incrément manuel à chaque release |
  | version par projet | versions divergentes entre `MMV.App` et l'outil |
  | version calculée depuis Git (MinVer, GitVersion, etc.) | **paquet NuGet nouveau**, non restaurable hors ligne (F-22) |
  | version injectée par la CI (`-p:Version=…`) | suppose un pipeline de release, qui n'existe pas (F-23) |

  **REC-4a** — `<Version>` **unique** dans [Directory.Build.props](../../Directory.Build.props), au format SemVer
  `MAJEUR.MINEUR.CORRECTIF`, incrémentée à la main à chaque release. Tous les assemblys la portent, outil compris
  (HY-8). Elle est lue à l'exécution par l'attribut de version d'assembly généré par le SDK
  (`EF_BEHAVIOR — À CONFIRMER`). Usages : écran « À propos » (fin du `1.0.0` en dur), écrans de blocage, journal de
  l'outil, `Application Name` (§4.9). **Convention proposée** : une release qui ajoute une migration incrémente au
  moins MINEUR.

  #### (b) Version du schéma

  | Option | Principe | Limite |
  |---|---|---|
  | **VS-1** | historique EF seul | aucun intervalle de compatibilité possible |
  | VS-2 | table de version MMV : version de schéma et **version minimale d'application** | seconde source de vérité ; table sur deux chaînes ; la version minimale est un jugement du développeur |
  | VS-3 | registre des postes | ne décide rien seul ; écritures à chaque démarrage ; table sur deux chaînes |

  **REC-4b** — **VS-1.** La version du schéma est **l'ensemble** des `MigrationId` inscrits dans
  `__EFMigrationsHistory`. Aucune table nouvelle.

  #### (c) Politique de compatibilité

  **REC-4c** — **Égalité stricte, sur les migrations et non sur la version MMV.** Soit **A** les migrations appliquées
  (historique) et **C** les migrations connues du binaire (assembly déployée).

  | État | Condition | Poste | Sens du message |
  |---|---|---|---|
  | **E1** | A = C | démarre | — |
  | **E2** | A ⊂ C : base en retard | bloqué | « La base doit être mise à jour par l'administrateur technique, avec MMV.DatabaseManager \<version du poste\>. » |
  | **E3** | C ⊂ A : poste en retard | bloqué | « Ce poste (version \<X\>) est plus ancien que la base. Mettez-le à jour. » |
  | **E4** | historique absent, aucune table | bloqué | « Base non initialisée. » |
  | **E5** | verrou exclusif détenu (§4.3) | bloqué, bouton Réessayer | « Maintenance de la base en cours. » |
  | **E6** | serveur injoignable, ou lecture impossible | bloqué, bouton Réessayer | « Serveur indisponible. » Politique complète : D-15, P4-8 |
  | **E7** | tables présentes sans historique EF | bloqué | « Base non reconnue. » Aucune adoption côté PostgreSQL : l'historique y est neuf (ADR-005 §5.8). Répond à Q-12 d'ADR-009 |
  | **E8** | A et C incomparables | bloqué | « Installation incohérente. » |

  - **Conséquence utile** : deux postes en 1.4.0 et 1.4.1 sans migration de différence restent compatibles. **Seule
    une release porteuse de migration** impose de mettre à jour tous les postes.
  - **Limite assumée de VS-1** : en E3, le poste connaît sa propre version, pas celle qui a migré la base. L'action
    demandée reste univoque : mettre à jour ce poste.
  - **SQLite** : la même comparaison est ajoutée **avant** `Migrate()`. En E3 ou E8 : `DatabaseMigrationException`,
    base et sauvegarde conservées. Le chemin nominal ne change pas ; seul le rétrogradage d'un poste est désormais
    détecté (U-1, ADR-007 §5.7).
  - **Fonction pure** : la comparaison (A, C) → état est indépendante du provider et testable sans serveur.

  **Pourquoi pas VS-2.** Une fenêtre de compatibilité repose sur une « version minimale » fixée par le développeur.
  Une erreur de sa part laisse un poste ancien écrire sur un schéma incompatible, soit exactement le risque
  qu'O8 interdit. Elle ajoute une table sur deux chaînes. Elle n'apporte rien tant qu'une mise à jour progressive
  n'est pas exigée, ce qui est le cas sous HY-4 et HY-7.

  **Réexamen si** : un updater apparaît, ou l'arrêt de maintenance devient inacceptable. Il faudrait alors VS-2 avec
  « étendre puis contracter », sous forme d'une migration postérieure à la baseline (exception d'ADR-005 §5.4).

  **Preuves attendues** : S-2 (E3 : bloqué, sans écriture) ; S-4 (E2) ; S-10 (outil plus ancien que la base :
  refus) ; tests unitaires de la fonction pure pour les deux chaînes ; test de rétrogradage SQLite.

  ---

  ### 4.5 Poste administrateur : identification, stockage, changement

  **État actuel.** Aucune identité de poste dans le code (F-16). Tous les postes sont configurés de la même façon,
  par deux variables d'environnement (P4-5E-A, *Connection Configuration Review*). Le « poste désigné » est cité
  comme option autorisée (ADR-001 §6, roadmap §P4-6) mais n'est défini nulle part. Le secret bootstrap suit le même
  canal, sans poste désigné pour le porter (P4-5E-A, constat S-6).

  **Options**

  | # | Modèle | Identification | Stockage | Changement | Limite |
  |---|---|---|---|---|---|
  | PA-1 | aucun poste administrateur : tout poste migre | — | identifiant DDL sur tous les postes | — | A1, écartée au §4.1 |
  | PA-2 | poste désigné par configuration locale | un indicateur sur le poste | identifiant DDL stocké sur ce poste | reconfigurer deux postes | indicateur falsifiable ; identifiant permanent ; poste éteint = blocage |
  | PA-3 | poste désigné enregistré en base | nom de machine comparé à une table | une table, plus l'identifiant DDL sur ce poste | mise à jour de la table | table sur deux chaînes ; nom de machine non authentifié ; limites de PA-2 |
  | **PA-4** | **aucun poste désigné dans MMV : l'autorité est l'identifiant migrateur, utilisable seulement depuis la machine serveur** | par PostgreSQL : authentification du rôle migrateur et règle `pg_hba.conf` en boucle locale | sur aucun poste client ; saisi à l'exécution sur le serveur | rotation du mot de passe, ou modification de `pg_hba.conf` | exige un accès à la machine serveur, sur place ou par prise en main à distance |

  **REC-5 — PA-4. Le poste administrateur est la machine qui héberge PostgreSQL.**

  - **Identification.** Elle est faite par PostgreSQL, pas par MMV. Un nom de machine ou un indicateur de
    configuration ne sont pas des frontières de sécurité ; l'authentification d'un rôle en est une. MMV ne stocke
    aucune identité de poste administrateur.
  - **Stockage.** L'identifiant migrateur n'est **jamais** placé : dans `MMV_DATABASE_CONNECTION_STRING` ; sur un
    poste client ; dans un argument de ligne de commande, visible dans la liste des processus ; dans le dépôt. Par
    défaut, l'outil le demande à l'exécution, sans écho. Un stockage protégé sur le serveur (fichier `pgpass` sous ACL
    NTFS du compte de l'opérateur, ou gestionnaire d'identifiants Windows) relève de **D-13, P4-8**. P4-6 ne fixe
    que les interdits ci-dessus.
  - **Changement.**

  | Événement | Action | Poste client touché ? |
  |---|---|---|
  | changement d'administrateur technique | rotation du mot de passe migrateur par le superutilisateur | non |
  | remplacement de la machine serveur | restauration (P4-9), `pg_hba.conf` sur la nouvelle machine, nouvelle adresse du serveur sur les postes (P4-8) | adresse seulement |
  | intervention depuis une autre machine | règle `pg_hba.conf` temporaire, retirée après usage, selon une procédure documentée | non |
  | petit magasin où le serveur est aussi un poste de travail | aucun cas particulier : `MMV.App` y tourne sous le rôle applicatif, l'outil sous le rôle migrateur | non |

  - **Écart assumé** : si la machine serveur est inaccessible, aucune migration n'est possible. C'est voulu : sans
    accès au serveur, on ne peut pas non plus sauvegarder.
  - **Proposition pour P4-8 (D-12), non décidée ici** : créer le premier administrateur MMV par une commande de
    l'outil, plutôt que par `MMV_BOOTSTRAP_ADMIN_PASSWORD` posé sur un poste. Le secret quitterait les postes, et la
    course entre deux postes au premier démarrage disparaîtrait (P4-5E-A, constat (b)).

  ---

  ### 4.6 `MMV.DatabaseManager`

  **État actuel.** `MMV.sln` compte 8 projets (5 dans `src`, 3 dans `tests`), aucun exécutable console. L'assembly de
  migrations ne peut rien accueillir d'autre (F-7). `ApplicationArchitectureTests` interdit à Application les
  assemblys préfixées `MMV.Infrastructure` ([l. 35](../../tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs#L35)) :
  un projet nommé `MMV.DatabaseManager` **n'est pas couvert** par ce préfixe. `MMV.App` référence l'assembly de
  migrations pour la déployer (F-8) : l'outil devra faire de même. Aucun paquet nouveau n'est restaurable (F-22). Les
  composants SQLite ne servent pas sur PostgreSQL (P4-5E-A R-E9) ; les principes de `VerifyAfterPreparation`, si.

  #### (a) Nouveau projet ?

  | Option | Verdict |
  |---|---|
  | commande cachée dans `MMV.App` (`--migrate`) | écartée : application `WinExe` Avalonia, et code DDL dans le binaire des postes (C1) |
  | logique dans l'assembly de migrations | **interdite** par un test (F-7) |
  | **exécutable séparé, nouveau projet** | **retenu** |

  #### (b) Type de projet

  **REC-6** — `src/MMV.DatabaseManager`, application console :

  - `OutputType=Exe`, `net8.0`, `Nullable`, `ImplicitUsings`, `LangVersion 12`, comme les autres projets ;
  - **aucune** UI, aucun hôte générique, **aucun paquet nouveau** : trois commandes, arguments analysés à la main,
    sortie texte en français, codes de sortie distincts ;
  - références : `MMV.Infrastructure` (contexte, résolveur, services partagés) et
    `MMV.Infrastructure.PostgreSQL.Migrations` (déploiement de la DLL, comme F-8). **Jamais** `MMV.App`,
    `MMV.Application` ni Avalonia ;
  - inscrit dans `MMV.sln` : il est construit et testé par la CI existante, sans modifier le workflow au-delà de
    P4-5F.

  #### (c) Où vit le code

  | Emplacement | Contenu (noms indicatifs) | Utilisé par |
  |---|---|---|
  | `MMV.Infrastructure`, partie neutre | comparaison (A, C) → E1 … E8, fonction pure | `MMV.App` (deux chaînes), outil |
  | `MMV.Infrastructure`, partie PostgreSQL | verrou consultatif exclusif et partagé ; inspection de l'état de la base (historique, tables) ; vérification post-migration par les catalogues (portage des invariants de `VerifyAfterPreparation`, agrégats lisibles) ; appel de `pg_dump` et contrôle d'archive ; contrôle des privilèges du rôle applicatif ; orchestration de `migrate` | outil ; `MMV.App` pour le verrou partagé et l'inspection |
  | `MMV.DatabaseManager` | arguments, saisie du secret, affichage, confirmation, journal fichier, codes de sortie | — |
  | `MMV.App` | appel de la garde hors du `try` générique, verrou de présence, écrans de blocage | — |

  Motif : **une seule implémentation de la règle de compatibilité** pour les deux acteurs. Les services se testent
  dans le projet d'intégration P4-5F sans lancer l'exécutable.

  #### (d) Responsabilités V1

  | Commande | Effet | Écrit ? | Verrou |
  |---|---|---|---|
  | `status` | versions (outil, serveur PostgreSQL), migrations appliquées, en attente et inconnues, état E1 … E8, postes ouverts, détenteur éventuel du verrou | non | aucun |
  | `backup` | `pg_dump -F c`, contrôle de l'archive, journal | un fichier | partagé |
  | `migrate` | P-1 … P-7, sauvegarde, `Migrate()`, vérification post-migration, contrôle des privilèges (§4.7), journal. Sur base vide : applique la baseline (installation, D-11). En E1 : « rien à faire », sans sauvegarde | oui | exclusif |

  Les codes de sortie distinguent au minimum : succès, refus de précondition, échec de sauvegarde, échec de
  migration, échec de vérification. Valeurs fixées en P4-6B.

  #### (e) Hors responsabilités V1

  SQLite (jamais) ; création de base et de rôles (P4-8, D-09) ; restauration (P4-9) ; import SQLite (P4-7) ; premier
  administrateur (P4-8, D-12). **Principe proposé** : si ces opérations deviennent des commandes, elles rejoignent
  **cet outil unique** plutôt qu'un nouvel exécutable. Chaque lot le décide.

  #### (f) Configuration

  - Le contexte est construit par `DatabaseProviderResolver.Configure` avec des options runtime, **jamais** par
    `OpticDbContextFactory` (D-05).
  - Hôte, base et rôle : arguments ou variables non secrètes. Mot de passe : saisi (§4.5). Les noms de variables sont
    fixés en P4-6B. `MMV_DESIGNTIME_*` et `MMV_DATABASE_CONNECTION_STRING` sont exclus.

  #### (g) Journal (DP-8 d'ADR-009)

  Fichier local sur la machine qui exécute l'outil, à côté des sauvegardes, horodaté en UTC (réutilisation de
  `MigrationJournal`, F-11). **Pas de table de journal en base**, qui serait un changement de modèle. Contenu : version
  de l'outil et du serveur, migrations de départ et d'arrivée, sauvegarde (chemin, taille, empreinte), durée,
  résultat, postes détectés. Jamais de secret.

  #### (h) Tests

  Unitaires (arguments, fonction de compatibilité, messages) ; architecture (l'outil ne référence ni `MMV.App`, ni
  `MMV.Application`, ni Avalonia ; Domain et Application ne référencent pas l'outil) ; intégration (§7).

  ---

  ### 4.7 Rôles PostgreSQL nécessaires

  **État actuel.** L'application ne connaît qu'une chaîne de connexion (F-5). Seuls des rôles de laboratoire
  existent, avec DDL (F-21). D-08 place tout dans `public`. Depuis PostgreSQL 15, `CREATE` sur `public` n'est plus
  accordé à tous ; un schéma portant le nom d'un rôle capterait les tables par le `search_path`
  (`PG_BEHAVIOR — À CONFIRMER`). Le seed écrit dans `Users` à chaque démarrage (F-12) : le rôle applicatif a besoin de
  DML, pas de DDL.

  **Options** : RL-1 (un rôle propriétaire pour tout), RL-2 (migrateur + applicatif), RL-3 (administrateur +
  migrateur + applicatif). RL-1 est incompatible avec REC-1 : le poste détiendrait le DDL. RL-2 et RL-3 diffèrent peu
  en pratique : l'installeur crée de toute façon un superutilisateur (`EXTERNE`). RL-3 rend ce troisième rôle
  explicite et interdit son usage par MMV.

  **REC-7 — trois rôles, dont deux pour MMV** (noms proposés ; privilèges `PG_BEHAVIOR — À CONFIRMER` en P4-5F)

  | Rôle | Attributs | Droits | Utilisé par | Jamais utilisé par |
  |---|---|---|---|---|
  | superutilisateur d'installation (nom fixé par l'installeur, `EXTERNE`) | `SUPERUSER` | tout | le technicien : provisioning (P4-8), restauration (P4-9), rotation des mots de passe, fin d'une session bloquée | `MMV.App`, `migrate` |
  | **`mmv_owner`** | `LOGIN`, `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE` | propriétaire de la base et de tous les objets de `public`. Lecture des statistiques de sessions pour lister les postes (rôle prédéfini `pg_read_all_stats`, `À CONFIRMER`) | `MMV.DatabaseManager`, en boucle locale | les postes clients |
  | **`mmv_app`** | `LOGIN`, sans autre attribut | `CONNECT` ; `USAGE` sur `public` ; `SELECT`, `INSERT`, `UPDATE`, `DELETE` sur les tables métier ; **`SELECT` seul** sur `__EFMigrationsHistory` ; fonctions de verrou consultatif (accordées à tous par défaut, `À CONFIRMER`) ; droits sur les séquences d'identité : `UNKNOWN`, à établir en P4-5F | chaque poste | DDL, écriture de l'historique EF, `CREATEDB` |

  | # | Règle |
  |---|---|
  | R7.1 | La base appartient à `mmv_owner`. Depuis PostgreSQL 15, le schéma `public` appartient au propriétaire de la base, qui peut donc y créer des objets (`PG_BEHAVIOR — À CONFIRMER`). Les objets créés par `migrate` appartiennent à `mmv_owner` |
  | R7.2 | Le provisioning pose des **privilèges par défaut** (`ALTER DEFAULT PRIVILEGES FOR ROLE mmv_owner …`) pour que chaque nouvelle table soit accessible à `mmv_app` **sans retoucher une migration** (ADR-005 §5.4 n'autorise de retouche que pour une migration de données) |
  | R7.3 | **L'historique EF est en lecture seule pour `mmv_app`.** Sinon un poste pourrait falsifier la version de schéma que lit la garde. Les privilèges par défaut l'incluraient : l'outil **vérifie** donc après chaque migration que `mmv_app` n'a que `SELECT` sur l'historique et le DML sur chaque table métier, et **corrige** tout écart de façon idempotente et journalisée. À confirmer par l'architecte (Q-A5) |
  | R7.4 | La base est créée par le provisioning, jamais par Npgsql au démarrage (D-09, option (a)). Aucun rôle MMV n'a `CREATEDB` |
  | R7.5 | Aucun schéma ne porte le nom d'un rôle MMV (D-08) |
  | R7.6 | La sauvegarde avant migration passe par `pg_dump` sous `mmv_owner`, propriétaire de tout. Un rôle de sauvegarde planifiée, en lecture seule, relève de P4-9 |
  | R7.7 | La propriété des objets se fige à la première installation réelle (ADR-009 §4.5.4). Ce modèle doit donc être décidé **avant** toute installation, y compris en laboratoire applicatif |

  **Preuve attendue : S-9**, sur un vrai serveur : sous `mmv_app`, un DDL est refusé, une écriture dans l'historique
  est refusée, le DML métier réussit.

  ---

  ### 4.8 Sauvegarde avant migration

  **État actuel.** SQLite : copie du fichier et de ses annexes avant toute mutation (F-9). PostgreSQL : rien (U-3).
  `pg_dump -F c` et `pg_restore` ont été validés **à la main** en laboratoire (Lot C §17.7). Une restauration exige
  `dropdb --force`, `createdb` et une purge du pool, sinon l'erreur `57P01` survient (ADR-002 §17). La sauvegarde
  planifiée et la restauration outillée appartiennent à P4-9 (O9). D-B4 / RR6 : une sauvegarde prise pendant qu'un
  autre poste écrit n'est pas atomique ; la règle actuelle est une procédure, « tous postes fermés ».

  **Options**

  | # | Option | Limite |
  |---|---|---|
  | BK-1 | procédure manuelle, hors outil | repose sur la discipline : rien n'empêche de migrer sans sauvegarde |
  | **BK-2** | **sauvegarde intégrée et obligatoire dans `migrate`** | exige `pg_dump` sur la machine qui exécute l'outil (HY-3) |
  | BK-3 | s'appuyer sur la dernière sauvegarde planifiée | les écritures postérieures sont perdues en cas de restauration ; P4-9 n'existe pas encore |
  | BK-4 | instantané de machine virtuelle ou de disque | dépend de l'infrastructure du magasin (`UNKNOWN`) |
  | BK-5 | aucune, confiance dans la transaction par migration | ne protège pas d'une migration réussie mais fausse. Seule la restauration est un vrai retour arrière (U-7) |

  **REC-8 — BK-2.** Ordre imposé dans `migrate` :

  1. verrou exclusif (P-2) ;
  2. aucun poste présent (P-3). À partir d'ici, aucun poste MMV ne peut plus écrire : **RR6 et D-B4 sont fermés
    techniquement**, et non plus par procédure ;
  3. `pg_dump -F c` vers le dossier de sauvegarde ;
  4. contrôle de l'archive : `pg_restore --list` réussit et liste l'historique EF et les tables métier
    (`PG_BEHAVIOR — À CONFIRMER`) ;
  5. journal : chemin, taille, empreinte SHA-256, durée, migration de départ ;
  6. seulement ensuite, `Migrate()`.

  | # | Règle |
  |---|---|
  | R8.1 | Échec à l'étape 3 ou 4 : aucune migration, code de sortie dédié |
  | R8.2 | Base vide (installation) : aucune sauvegarde, puisqu'il n'y a rien à perdre. Journalisé |
  | R8.3 | **Aucune option de contournement en V1** |
  | R8.4 | `pg_dump` : chemin configurable ; version majeure supérieure ou égale à celle du serveur (`PG_BEHAVIOR — À CONFIRMER`) ; mot de passe transmis au processus enfant par son environnement ou par `pgpass`, jamais en argument |
  | R8.5 | Emplacement : dossier local du serveur ; nom horodaté en UTC, avec la migration de départ. Pas de purge automatique en V1 (HY-6). Copie hors machine et rétention : P4-9 |
  | R8.6 | Restauration : procédure documentée en P4-9. L'outil n'en a pas en V1 ; `status` permet de vérifier l'état après restauration |
  | R8.7 | Cohérence : `pg_dump` produit un instantané cohérent sans bloquer les autres opérations (ADR-002 §17, source officielle S14) |

  **Preuve attendue : S-8.** `migrate` sans `pg_dump` disponible : refus, schéma inchangé. `migrate` nominal :
  l'archive existe et `pg_restore --list` réussit. L'exercice complet de restauration relève de P4-9.

  ---

  ### 4.9 Postes secondaires pendant une migration

  **État actuel.** Aucune revérification après le démarrage. Aucune garde PostgreSQL au démarrage, hormis le
  garde-fou P4-3 (F-1). Aucun polling (F-17) : un poste ouvert mais inactif peut n'avoir **aucune** connexion
  ouverte, car le pool Npgsql ferme les connexions inactives (`EF_BEHAVIOR — À CONFIRMER`). Il est alors invisible
  dans `pg_stat_activity`. Aucun `Application Name` n'est imposé (F-4). Un `ALTER TABLE` prend un verrou exclusif et
  bloque les requêtes suivantes sur la table (U-5, `PG_BEHAVIOR — À CONFIRMER`).

  **Options** (numérotation d'ADR-009 §4.2.4)

  | # | Option | Verdict |
  |---|---|---|
  | CX-1 | délai d'attente de verrou (`lock_timeout`) côté migrateur | **retenu en défense** ; ne protège pas le poste |
  | **CX-2** | verrou partagé tenu par chaque poste pendant toute sa session | **retenu** |
  | CX-3 | indicateur de maintenance en base | écarté : table sur deux chaînes, drapeau orphelin, polling nécessaire |
  | CX-4 | inventaire par `pg_stat_activity` et `Application Name` | **insuffisant seul** : un poste inactif sans connexion est invisible. Retenu pour **l'affichage** |
  | CX-5 | procédure « tous postes fermés » | invérifiable seule |

  **REC-9 — CX-2, avec CX-1 en défense et CX-4 pour l'affichage.**

  | Phase | Poste | Outil |
  |---|---|---|
  | **Avant** | tient le verrou partagé depuis son démarrage | la tentative de verrou exclusif échoue : refus, avec la liste des postes (adresse, `Application Name`, heure de connexion) |
  | **Pendant** | un poste qui démarre : la tentative de verrou partagé échoue. Écran « maintenance en cours », avec Réessayer et Quitter. Aucune écriture, aucun seed | migre. `lock_timeout` sur sa session : un verrou de table inattendu fait échouer la migration proprement au lieu d'attendre |
  | **Après** | poste mis à jour : E1. Poste non mis à jour : E3, bloqué | libère le verrou |
  | **Poste resté ouvert** | impossible par construction : l'outil a refusé | — |

  | # | Règle |
  |---|---|
  | R9.1 | La connexion de présence est ouverte **avant** la garde de version et tenue jusqu'à la fin du processus, hors pool (R3.3) |
  | R9.2 | `DatabaseProviderResolver.Configure` impose un `Application Name` : produit, version, nom de machine. Sa longueur est bornée par le serveur (`PG_BEHAVIOR — À CONFIRMER`). Répond à Q-19 d'ADR-009 |
  | R9.3 | **Perte de la connexion de présence en cours de session** (coupure réseau) : le poste la rétablit, reprend le verrou partagé et **rejoue la garde de version**. En cas d'échec (maintenance en cours, schéma changé, serveur injoignable) : écran bloquant, redémarrage requis. C'est la seule revérification en cours de session (Q-11 d'ADR-009). Tests : P4-10 |
  | R9.4 | **Poste arrêté brutalement** (coupure de courant) : sa session reste ouverte côté serveur jusqu'à sa détection par les keepalive TCP. Délai `UNKNOWN` pour la configuration cible. L'outil affiche le détenteur ; l'opérateur attend, ou met fin à la session avec le superutilisateur, selon une procédure documentée. Le réglage des keepalive du serveur relève de P4-8 |
  | R9.5 | Contrainte pour P4-8 : ne pas activer d'expiration des sessions inactives côté serveur (`idle_session_timeout`, `PG_BEHAVIOR — À CONFIRMER`). Elle couperait les connexions de présence |
  | R9.6 | Coût : une connexion par poste ouvert. Négligeable sous HY-1 ; la limite de connexions du serveur cible est `UNKNOWN` (P4-8) |
  | R9.7 | **Migrations non transactionnelles interdites en V1** (Q-15 d'ADR-009). Tous les postes étant fermés, aucune opération sans verrou n'est utile. Règle à inscrire dans [CONTRIBUTING.md](../../CONTRIBUTING.md) |

  **Preuves attendues** : S-3 (un poste tient le verrou partagé : `migrate` refuse ; un poste démarre pendant le
  verrou exclusif : E5, sans écriture) ; S-11 (perte de présence : garde rejouée).

  ---

  ### 4.10 Stratégie de mise à jour

  **État actuel.** Aucun installeur ni updater (F-23). Sur SQLite, la montée de schéma est automatique au premier
  lancement de la nouvelle version, sauvegarde comprise (P4-5E-A, *Database Upgrade Analysis*). ADR-001 §6 : les
  binaires sont mis à jour sur chaque poste ; la base centrale est migrée de façon sérialisée. La roadmap P5 (non
  suivie) place « P8 — Mise à jour applicative » après P5.

  **Options**

  | # | Stratégie | Base | Postes |
  |---|---|---|---|
  | MU-1 | automatique | le premier poste mis à jour migre | updater |
  | **MU-2** | **semi-automatique, validation humaine unique** | l'opérateur lance `migrate` ; l'outil enchaîne contrôles, sauvegarde, migration et vérification | mise à jour manuelle ; chaque poste se vérifie au démarrage |
  | MU-3 | validation humaine à chaque étape | `pg_dump`, script SQL, contrôles, tous à la main | manuelle |

  MU-1 suppose A1 (écartée au §4.1) et un updater absent (HY-7). MU-3 multiplie les occasions d'erreur humaine et
  exécute du SQL de script, contraire à K-7 pour la baseline.

  **REC-10 — MU-2.** Une décision humaine, une commande ; tout le reste est automatisé et vérifié.

  **Release avec migration**

  1. annoncer la maintenance, fermer les postes ;
  2. sur le serveur : `MMV.DatabaseManager status`, qui affiche le plan ;
  3. `MMV.DatabaseManager migrate` : confirmation, sauvegarde, migration, vérification ;
  4. mettre à jour MMV sur chaque poste. Un poste non mis à jour est bloqué (E3), avec un message clair ;
  5. démarrer les postes : E1.

  **Release sans migration** : aucune maintenance. Les postes sont mis à jour un par un, dans n'importe quel ordre.
  L'ensemble des migrations ne change pas, donc tous restent en E1 ; `status` le confirme.

  **Retour arrière** : restaurer la sauvegarde de l'étape 3 (P4-9) et réinstaller la version précédente sur tous les
  postes. Jamais de `Down()` (U-7).

  **Installation** : provisioning (P4-8) → `migrate` sur base vide → premier administrateur (D-12, P4-8) →
  configuration des postes (D-13, P4-8) → démarrage des postes.

  **SQLite mono-poste** : automatique, inchangé.

  **Réexamen si** un updater apparaît (P5, « P8 ») : la distribution des binaires pourra être automatisée. La base
  reste migrée **en premier**, par l'outil.

  ---

  ## 5. Architecture cible — synthèse

  ### 5.1 Vue d'ensemble

  ```
  ┌─────────────────────────── Machine serveur du magasin ───────────────────────────┐
  │  PostgreSQL ── base MMV (schéma public), propriétaire mmv_owner                   │
  │                                                                                   │
  │  MMV.DatabaseManager.exe (console) — rôle mmv_owner, boucle locale (pg_hba)       │
  │     status  : lecture seule                                                       │
  │     backup  : verrou K PARTAGÉ   → pg_dump -F c → dossier de sauvegardes local    │
  │     migrate : verrou K EXCLUSIF  → P-1…P-7 → pg_dump → Migrate() → vérification   │
  │               → contrôle des privilèges de mmv_app → journal fichier local        │
  └───────────────────────────────────────▲───────────────────────────────────────────┘
                                          │ TCP 5432, réseau local
  ┌──────────────── Postes (MMV.App.exe) — rôle mmv_app, DML seul ────────────────────┐
  │  1. connexion de présence hors pool → verrou K PARTAGÉ           (sinon E5 / E6)  │
  │  2. garde de version, lecture seule : A = C ?                    (sinon E2…E8)    │
  │  3. agrégats lisibles → seed (DML) → écran de connexion                           │
  │  Hors du try générique. Chaque état bloquant a un écran minimal.                  │
  └───────────────────────────────────────────────────────────────────────────────────┘
  ```

  ### 5.2 Démarrage d'un poste PostgreSQL

  1. Résolution de la configuration : inchangée ([App.axaml.cs:120](../../src/MMV.App/App.axaml.cs#L120)).
  2. Ouverture de la connexion de présence. Échec : **E6**.
  3. Verrou partagé. Refus : **E5**.
  4. Inspection de la base : historique EF présent ? autres tables ? Donne **E4** ou **E7**.
  5. Comparaison A / C : **E1**, **E2**, **E3** ou **E8**.
  6. En E1 seulement : lecture des agrégats principaux, puis seed, puis écran de connexion.

  Les étapes 2 à 5 sont **en lecture seule**. Elles s'exécutent **hors** du `catch (Exception)` générique (K-13). Comme
  `ConfigureServices` s'exécute avant toute fenêtre (F-2), P4-6C doit restructurer le démarrage : un résultat de garde
  typé ouvre une fenêtre de blocage au lieu de lever une exception sans message.

  ### 5.3 `migrate`

  1. résolution des paramètres, saisie du secret ;
  2. P-1, puis tentative de verrou exclusif (P-2, P-3). En cas d'échec, lecture de `pg_locks` pour nommer le
    détenteur ;
  3. inspection et comparaison : E1 → « rien à faire », fin ; E3, E7 ou E8 → refus (P-4, P-5) ; E2 ou E4 → suite ;
  4. affichage du plan, confirmation (P-7) ;
  5. sauvegarde et contrôle d'archive (P-6), sauf E4 ;
  6. `lock_timeout` sur la session de migration, puis `Migrate()` ;
  7. vérification : aucune migration en attente, A = C, invariants physiques par les catalogues, agrégats lisibles ;
  8. contrôle et correction des privilèges de `mmv_app` (R7.3) ;
  9. journal, libération du verrou, code de sortie.

  ### 5.4 Ce qui ne change pas

  | Élément | Statut |
  |---|---|
  | Chaîne SQLite (14 migrations) et cycle de vie SQLite | inchangés, sauf le contrôle de rétrogradage ajouté avant `Migrate()` (REC-4c) |
  | Modèle EF, deux instantanés, contrôles de dérive en CI | inchangés : **P4-6 ne génère aucune migration** |
  | Domain et Application | intacts (O14) |
  | Paquets NuGet | aucun nouveau |

  ---

  ## 6. Impact d'implémentation par lot

  Découpage repris du rapport de transition (non suivi). Il n'est **pas encore enregistré** dans la roadmap commitée
  (RECON-B-light, résidu R-1).

  ### 6.1 P4-6B — mécanisme, garde-fou toujours actif (après P4-5F vert)

  | Zone | Changement | Recommandation |
  |---|---|---|
  | [Directory.Build.props](../../Directory.Build.props) | `<Version>` SemVer unique | REC-4a |
  | `MMV.Infrastructure`, partie neutre | fonction de compatibilité (A, C) → E1 … E8 | REC-4c |
  | `MMV.Infrastructure`, partie PostgreSQL | verrou consultatif ; inspection ; vérification par catalogues ; `pg_dump` et contrôle d'archive ; contrôle des privilèges ; orchestration de `migrate` | REC-3, REC-7, REC-8 |
  | [DatabaseProviderResolver.cs](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs) | `Application Name` sur la branche PostgreSQL, et mise à jour de `DatabaseProviderResolverTests` | R9.2 |
  | [SqliteDatabaseManager.cs](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) | contrôle A ⊆ C avant `Migrate()`, avec tests | REC-4c |
  | `src/MMV.DatabaseManager` (nouveau) + `MMV.sln` | console, trois commandes | REC-6 |
  | Tests | unitaires, architecture, intégration (dans le projet P4-5F) | §7 |
  | [CONTRIBUTING.md](../../CONTRIBUTING.md) | migrations non transactionnelles interdites ; convention de version ; commandes de l'outil | R9.7, REC-4a |

  ### 6.2 P4-6C — activation (absorbe P4-5G)

  | Zone | Changement |
  |---|---|
  | [App.axaml.cs:200-278](../../src/MMV.App/App.axaml.cs#L200-L278) | levée du garde-fou P4-3 pour PostgreSQL ; garde de version hors du `try` ; connexion de présence pour la vie du processus ; fenêtre de blocage au lieu d'une exception sans message ; comportement E6 aligné sur D-15 |
  | Seed de démonstration | interdit sur PostgreSQL : il appelle `EnsureCreated()` (F-13, K-9). Répond à C-1 et Q-16 d'ADR-009, sous réserve de Q-A6 |
  | Écran de blocage minimal | états E2 … E8, avec Réessayer pour E5 et E6. UI minimale autorisée par la roadmap §3 |
  | [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) | version lue dans l'assembly |
  | [ServerStartupGuardTests.cs](../../tests/MMV.App.Tests/Architecture/ServerStartupGuardTests.cs) | mise à jour **délibérée**, comme son commentaire l'exige |
  | Documentation opérateur | installation, release avec et sans migration, retour arrière (avec P4-9) |
  | [ARCHITECTURE.md](../../ARCHITECTURE.md), l. 218 et 1064 | passages `"AutoMigrate": true` à corriger (Q18 de P4-5E-C9) |
  | Laboratoire `MMV-SRV` / `MMV-CLI` | preuve manuelle : installation, migration, poste bloqué, poste en maintenance |

  ### 6.3 Contraintes transmises aux autres lots

  | Lot | Ce que P4-6 exige |
  |---|---|
  | **P4-5F** | le projet d'intégration accueille S-1 … S-12. Les rôles sont globaux au serveur (`PG_BEHAVIOR — À CONFIRMER`) : le fixture doit les nommer par exécution ou les nettoyer. Le service CI fournit un superutilisateur de test (ADR-008 §5.3) |
  | **P4-8** | créer `mmv_owner` et `mmv_app` selon REC-7 ; privilèges par défaut (R7.2) ; `pg_hba.conf` : `mmv_owner` en boucle locale seulement ; keepalive TCP réglés ; pas d'expiration des sessions inactives (R9.5) ; stockage de la chaîne `mmv_app` sur les postes (D-13) ; E6 cohérent avec D-15 ; `pg_dump` et `pg_restore` présents sur le serveur ; version PostgreSQL alignée sur la CI (Q8 d'ADR-008) |
  | **P4-9** | restauration depuis les archives de pré-migration ; rétention et copie hors machine ; sauvegarde planifiée compatible avec le verrou partagé |
  | **P4-7** | l'import vise une base migrée par l'outil ; il peut devenir une commande de l'outil (REC-6 (e)) |
  | **P4-10** | verrou prouvé avec deux exécutables ; perte réseau pendant la présence (R9.3) ; session orpheline (R9.4) |

  ---

  ## 7. Preuves attendues

  | # | Preuve | Où | Lot |
  |---|---|---|---|
  | S-1 | deux sessions tentent le verrou exclusif : une seule l'obtient ; **une seule** application du DDL | intégration PostgreSQL | P4-6B |
  | S-2 | poste plus ancien que la base (E3) : bloqué, **aucune écriture**, message clair | intégration + démarrage | P4-6B / P4-6C |
  | S-3 | poste ouvert pendant une tentative de migration : `migrate` refuse. Poste qui démarre pendant une migration : E5, aucune écriture | intégration | P4-6C |
  | S-4 | poste face à des migrations en attente (E2) : bloqué, **ne migre pas** | intégration | P4-6B |
  | S-5 | chemin SQLite sans régression ; les 14 migrations inchangées ; rétrogradage détecté | suite existante + nouveau test | P4-6B |
  | S-6 | aucune garde avalée par le `catch` générique | test statique de la composition | P4-6C |
  | S-7 | Domain et Application provider-neutres | `ProviderNeutralityArchitectureTests` | continu |
  | **S-8** | `migrate` sans `pg_dump` : refus, schéma inchangé. `migrate` nominal : archive présente et lisible | intégration | P4-6B |
  | **S-9** | sous `mmv_app` : DDL refusé, écriture de l'historique refusée, DML métier accepté | intégration | P4-6B |
  | **S-10** | outil plus ancien que la base (P-4) : refus | intégration | P4-6B |
  | **S-11** | perte de la connexion de présence : garde rejouée ; blocage si le schéma a changé | intégration, puis multi-processus | P4-6C / P4-10 |
  | **S-12** | base vide migrée deux fois par l'outil : schéma identique (N1 par le chemin de production) | intégration | P4-6B |

  ---

  ## 8. Risques résiduels

  | # | Risque | Effet | Traitement | Propriétaire |
  |---|---|---|---|---|
  | RS-1 | HY-2 fausse : aucun opérateur technique | release avec migration inapplicable par le magasin seul | réexamen de REC-1 : interface graphique au-dessus des mêmes services | produit + architecte |
  | RS-2 | tous les postes bloqués entre la migration et leur mise à jour | arrêt du magasin pendant la maintenance | fenêtre annoncée ; releases sans migration non concernées ; VS-2 en réexamen | exploitation |
  | RS-3 | session orpheline d'un poste éteint brutalement | `migrate` refuse à tort | affichage du détenteur ; procédure de fin de session ; keepalive réglés | P4-8, documentation |
  | RS-4 | intervalle entre une coupure réseau et le rétablissement de la présence | un poste non protégé pendant quelques instants | R9.3 ; garde de version en seconde barrière (R3.6) | P4-6C, P4-10 |
  | RS-5 | `pg_dump` absent ou d'une version incompatible | migration impossible (échec sûr) | chemin configurable, contrôle de version | P4-8 |
  | RS-6 | **.NET 8 et EF Core 8 en fin de support le 10 novembre 2026** (`EXTERNE`, non vérifié dans le dépôt, cité par ADR-009 Q-6) | une V1 commerciale sur un runtime sans correctifs de sécurité | hors P4-6. L'architecture recommandée ne dépend pas de la version d'EF | architecte, **décision à ouvrir** (Q-A8) |
  | RS-7 | privilèges par défaut absents ou mal posés | une nouvelle table inaccessible à `mmv_app` | contrôle et correction par l'outil (R7.3) | P4-6B, P4-8 |
  | RS-8 | une session hors MMV (`psql`) tient un verrou de table | migration en attente | `lock_timeout` : échec propre | P4-6B |
  | RS-9 | contournement humain : DDL lancé avec l'identifiant propriétaire en dehors de l'outil | gardes ignorées | identifiant limité au serveur ; K-8 inscrit dans la documentation d'exploitation (ADR-007 §5.8) | P4-8, documentation |
  | RS-10 | un artefact de plus à livrer, sans installeur | erreur de distribution | même release, même version (HY-8) ; `status` affiche les deux versions | P4-8 |

  ---

  ## 9. Comportements à confirmer avant ou pendant P4-6B

  | # | Comportement | Étiquette | Si faux | Où le prouver |
  |---|---|---|---|---|
  | V-1 | sémantique des verrous consultatifs de session : exclusif contre partagé, formes `try` non bloquantes, portée base de données | `PG_BEHAVIOR` | REC-3 à revoir | intégration P4-6B |
  | V-2 | la fin de session (y compris la fermeture de socket par un client planté) libère les verrous consultatifs | `PG_BEHAVIOR` | verrous orphelins | intégration P4-6B, P4-10 |
  | V-3 | la réinitialisation d'une connexion rendue au pool Npgsql libère les verrous consultatifs ; une connexion hors pool les conserve | `EF_BEHAVIOR` | R3.3 à adapter | intégration P4-6B |
  | V-4 | le pool Npgsql ferme les connexions inactives, ce qui rend un poste inactif invisible | `EF_BEHAVIOR` | CX-4 redeviendrait suffisant, CX-2 resterait correct | lecture de la documentation, puis test |
  | V-5 | Npgsql applique chaque migration dans sa transaction ; un échec laisse la base à la dernière migration réussie | `EF_BEHAVIOR`, `PG_BEHAVIOR` | règle de reprise à revoir | P4-5F |
  | V-6 | `GetAppliedMigrations()` sur une base sans historique renvoie une liste vide, sans erreur | `EF_BEHAVIOR` | inspection E4 à adapter | P4-6B |
  | V-7 | `SELECT` sur l'historique suffit à la garde ; les fonctions de verrou consultatif sont exécutables par tout rôle | `PG_BEHAVIOR` | droits de `mmv_app` à compléter | P4-6B |
  | V-8 | un rôle membre de `pg_read_all_stats` voit l'adresse et l'`Application Name` des sessions d'un autre rôle | `PG_BEHAVIOR` | liste des postes moins précise ; refus inchangé | P4-6B |
  | V-9 | `pg_restore --list` suffit à contrôler une archive `-F c` ; `pg_dump` doit être d'une version majeure ≥ serveur | `PG_BEHAVIOR` | contrôle d'archive à renforcer | P4-6B |
  | V-10 | droits nécessaires sur les séquences d'identité pour un `INSERT` sous `mmv_app` | `UNKNOWN` | privilèges à compléter | P4-5F |
  | V-11 | les privilèges par défaut couvrent les tables créées par `Migrate()`, historique compris | `PG_BEHAVIOR` | R7.3 à adapter | P4-6B |
  | V-12 | `pg_hba.conf` peut restreindre un rôle à la boucle locale | `PG_BEHAVIOR` | REC-5 perd sa garde technique : procédure seule | P4-8 |
  | V-13 | un `lock_timeout` posé sur la session s'applique aux commandes de migration d'EF | `EF_BEHAVIOR`, `PG_BEHAVIOR` | CX-1 inopérant ; S-3 inchangé | P4-6B |
  | V-14 | longueur maximale de l'`Application Name` | `PG_BEHAVIOR` | nom tronqué | P4-6B |
  | V-15 | l'installeur Windows de production fournit `pg_dump`, `pg_restore` et un superutilisateur | `EXTERNE` | HY-3 à revoir | P4-8 |
  | V-16 | le SDK génère l'attribut de version d'assembly à partir de `<Version>` | `EF_BEHAVIOR` (SDK) | lecture de version à adapter | P4-6B |

  ---

  ## 10. Questions ouvertes pour l'architecte

  Seules restent les questions que ce rapport ne peut pas trancher.

  | # | Question | Bloque |
  |---|---|---|
  | **Q-A1** | **HY-2** : qui installe et met à jour la base en magasin ? La réponse conditionne REC-1 | acceptation d'ADR-009 |
  | **Q-A2** | **HY-4** : un arrêt de quelques minutes pour une release avec migration est-il acceptable ? Autrement dit, la mise à jour progressive des postes est-elle **hors** exigence V1 ? | REC-4c |
  | Q-A3 | Adopter la convention « une migration implique au moins un incrément MINEUR » ? | REC-4a |
  | Q-A4 | Valider les noms proposés (rôles, commandes) ; la valeur de la clé de verrou sera fixée en P4-6B | P4-6B |
  | **Q-A5** | La correction des privilèges de `mmv_app` après migration relève-t-elle de l'outil (recommandé) ou du seul provisioning ? | R7.3 |
  | Q-A6 | Interdire le seed de démonstration sur PostgreSQL (recommandé) ? | P4-6C |
  | Q-A7 | Principe d'**un outil d'administration unique** pour les futures opérations (restauration, import, premier administrateur, provisioning) ? | P4-7, P4-8, P4-9 |
  | **Q-A8** | Ouvrir, hors P4-6, la décision sur la fin de support de .NET 8 / EF Core 8 (RS-6) ? | V1 commerciale |
  | Q-A9 | Gouvernance : enregistrer le découpage P4-6A / B / C dans la roadmap (R-1) et statuer sur ADR-009 avec ces choix | P4-6B |

  ---

  ## 11. Correspondance avec ADR-PROD-DB-009

  ### 11.1 Points de décision

  | DP (ADR-009 §5.2) | Option recommandée | Ce rapport |
  |---|---|---|
  | **DP-1** autorité (D-11, D-18) | **MA-B2** | REC-1 |
  | **DP-2** verrou | **LK-1**, avec un mode partagé pour les postes | REC-3 |
  | **DP-3** version du schéma (D-16) | **VS-1**, égalité stricte | REC-4b, REC-4c |
  | **DP-4** comportement au démarrage | **SB-3**, plus les états E4, E5, E7, E8 ; revérification seulement à la perte de présence | REC-4c, R9.3 |
  | **DP-5** rôles (D-10) | **RL-3** | REC-7 |
  | **DP-6** postes connectés, D-B4 | **CX-2**, plus CX-1 en défense et CX-4 pour l'affichage. D-B4 devient une garde technique | REC-9 |
  | **DP-7** numérotation (D-17) | `<Version>` SemVer dans `Directory.Build.props` | REC-4a |
  | **DP-8** journal | fichier local sur la machine de l'outil | REC-6 (g) |

  La combinaison recommandée correspond au profil **P-β** d'ADR-009 §5.3, avec deux écarts : **RL-3** et **CX-2**, là
  où P-β citait « CX-4 ou CX-5 ». Motif de l'écart : CX-4 ne voit pas un poste inactif, et CX-5 n'est pas vérifiable
  (§4.9).

  ### 11.2 Questions d'ADR-009 traitées

  | Q (ADR-009 §9) | Réponse proposée |
  |---|---|
  | Q-1, Q-4 | REC-1, REC-5, REC-7 |
  | Q-2 | non tranchée : HY-2, Q-A1 |
  | Q-3 | la mise à jour progressive n'est pas requise en V1, sous HY-4 (Q-A2) |
  | Q-5 | REC-4a. Le lien version MMV ↔ schéma n'est pas matérialisé (VS-1) |
  | Q-6 | rester sur EF Core 8 **pour P4-6** : REC-3 ne dépend pas de la version d'EF. La fin de support est un sujet séparé (Q-A8) |
  | Q-7 | aucun script SQL comme source d'exécution en V1 |
  | Q-8 | bundle EF écarté (§4.1) |
  | Q-9 | CX-2 : garde technique |
  | Q-10 | sans objet : aucun poste ne migre |
  | Q-11 | revérification seulement à la perte de présence (R9.3) |
  | Q-12 | E7 : blocage, sans adoption |
  | Q-13 | sémantique du verrou prouvée en P4-6B, avec deux sessions dans un processus ; deux exécutables en P4-10 |
  | Q-14 | REC-6 (g) |
  | Q-15 | opérations non transactionnelles interdites en V1 (R9.7) |
  | Q-16 | seed de démonstration interdit sur PostgreSQL (Q-A6) |
  | Q-17, Q-18 | hors de ce rapport (gouvernance, Q-A9) |
  | Q-19 | oui : `Application Name` imposé (R9.2) |
  | Q-20 | non traité ici |

  ---

  ## 12. Conclusion

  ```
  P4-6 ARCHITECTURE DECISION ANALYSIS = COMPLETE — RECOMMENDATIONS FOR ARCHITECT REVIEW
  HEAD                                = ccbeb3259444d26264e20323058764dd59318e1c (= origin/p4-multi-poste)
  REC-1  MIGRATION AUTHORITY          = MMV.DatabaseManager — NO POSTGRESQL DDL FROM MMV.App
  REC-2  TRIGGER                      = MIGRATOR CREDENTIAL + SERVER MACHINE (pg_hba) + PRECONDITIONS P-1…P-7
  REC-3  LOCK                         = PG ADVISORY SESSION LOCK — ONE KEY — EXCLUSIVE / SHARED — NO LOCK TABLE
  REC-4  VERSIONS                     = SEMVER IN Directory.Build.props — SCHEMA = EF HISTORY — STRICT A == C
  REC-5  ADMIN WORKSTATION            = THE DATABASE SERVER MACHINE — IDENTIFIED BY POSTGRESQL, NOT BY MMV
  REC-6  MMV.DatabaseManager          = NEW .NET 8 CONSOLE PROJECT — THIN SHELL — status · backup · migrate
  REC-7  ROLES                        = SUPERUSER (INSTALL) · mmv_owner (MIGRATOR) · mmv_app (DML, HISTORY READ-ONLY)
  REC-8  BACKUP                       = MANDATORY pg_dump -F c INSIDE migrate — VERIFIED — NO BYPASS
  REC-9  SECONDARY POSTES             = SHARED PRESENCE LOCK — MIGRATION REFUSED WHILE A POSTE IS OPEN — D-B4 TECHNICAL
  REC-10 UPDATE STRATEGY              = SEMI-AUTOMATIC — ONE HUMAN VALIDATION — DATABASE FIRST, THEN POSTES
  EF MODEL / MIGRATIONS               = UNCHANGED BY P4-6
  KEY HYPOTHESIS                      = HY-2 — UNDOCUMENTED — PRODUCT CONFIRMATION REQUIRED (Q-A1)
  PREREQUISITES                       = P4-5F GREEN (FOR P4-6B) · P4-8 ROLES / D-15 (FOR P4-6C)
  CODE / TESTS / CI / ADR / ROADMAP   = UNCHANGED
  COMMIT / PUSH                       = NONE — THIS REPORT IS UNCOMMITTED
  V1 MULTI-POSTE                      = NOT GO
  ```

  READY FOR ARCHITECT REVIEW
