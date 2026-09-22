# Rapport de consolidation architecturale P4-6A

> ## ⚠ Document historique — état intermédiaire du 22 septembre 2026
>
> **Ce rapport décrit l'état du dossier AVANT la revue d'acceptation de l'architecte**, tenue le **même jour**.
> Il reste au dépôt parce qu'il est la trace du raisonnement qui a mené aux décisions, et parce que les deux ADR
> y renvoient. **Il n'est pas l'état courant.** Pour l'état courant, lire
> [le rapport d'acceptation P4-6A](P4-6A-adr-acceptance-report.md).
>
> **Ce que la revue a changé par rapport à ce rapport :**
>
> | Ce rapport écrit | État réel après la revue |
> |---|---|
> | « les deux ADR restent `PROPOSED` » | **ADR-PROD-DB-009 = ACCEPTED.** ADR-APP-DISTRIBUTION-001 reste PROPOSED, mais pour ses **spikes** et **deux faits extérieurs**, non pour une décision manquante |
> | **Q-3** bloquante, non tranchée (§4.1) | **tranchée : fenêtre de compatibilité N-1**, sous discipline « étendre → migrer → contracter ». L'égalité stricte est **rejetée** (RB-14) |
> | **Q-14** ouverte — « seul point resté entièrement ouvert » | **tranchée : journal dédié**, table en base + trace locale, six champs minimaux (DP-8) |
> | **Q-2 / QD-14** ouverte | **tranchée : modèle opérateur mixte selon contrat** (DP-9) |
> | « **treize** alternatives rejetées, RB-1 à RB-13 » | **vingt**, RB-1 à RB-20 |
> | « obligations H1 à H13 » | **H1 à H17** |
> | roadmap modifiée « dans l'arbre de travail, non commitée » ; résidu **R-1** ouvert | **enregistrée par commit** ; **R-1 clos** |
> | plusieurs documents cités « **non suivis** » | **tous commités** par la revue d'acceptation |
>
> ---
>
> **Nature : rapport de décision, strictement documentaire.**
> **AUCUN CODE MODIFIÉ.** Aucun fichier `.cs`, aucun `.csproj`, aucune migration EF, aucune CI, aucun paquet
> NuGet. Aucun commit, aucun push.
>
> Date : 22 septembre 2026. Branche : `p4-multi-poste`. HEAD de référence :
> `ccbeb3259444d26264e20323058764dd59318e1c` (= `origin/p4-multi-poste`).
> Objet : fermer les décisions d'architecture nécessaires avant **(1)** la migration vers .NET 10 et
> **(2)** le démarrage de **P4-6B**.
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Ce qui a été fait

Trois fichiers modifiés, un créé. Tous dans `docs/`.

| Fichier | Nature | Effet |
|---|---|---|
| [ADR-PROD-DB-009.md](../architecture/ADR-PROD-DB-009.md) | **modifié** | huit points de décision tranchés, deux créés, deux laissés ouverts |
| [adr-app-distribution-001-installation-and-updates.md](../architecture/adr-app-distribution-001-installation-and-updates.md) | **modifié** | DI-3 complétée, **DI-9** créée, QD-14 précisée, **QD-15** créée, QD-13 levée |
| [P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) | **modifié** | découpage **P4-6A / P4-6B / P4-6C** inscrit, avec ses états |
| `P4-6-architecture-consolidation-report.md` | **créé** | ce rapport |

**Les deux ADR restent `PROPOSED`.** Les décisions sont arrêtées sur le fond ; l'acceptation formelle appartient
à la revue de l'architecte. Aucun statut n'a été passé à `ACCEPTED` par cette consolidation.

---

## 2. Une divergence de numérotation, et comment elle a été traitée

**À signaler en priorité.** La demande de consolidation énumérait huit décisions pour ADR-PROD-DB-009, dans un
ordre qui **ne coïncide pas** avec les identifiants `DP-n` que le document portait déjà. La demande insérait un
sujet nouveau — le **déclenchement** — en deuxième position, ce qui décalait tous les suivants.

**Renuméroter était exclu.** Les identifiants `DP-n` sont cités par le §4.6 d'ADR-PROD-DB-009 (déroulés
opérationnels, étapes marquées `[DP-n]`), par son §8, et par ADR-APP-DISTRIBUTION-001 (DP-1, DP-5, DP-7). Les
décaler aurait silencieusement changé le sens de renvois existants.

**Choix retenu** : conserver les identifiants du dépôt, et donner des identifiants **neufs** aux deux sujets
réellement nouveaux — **DP-9** (déclenchement) et **DP-10** (sauvegarde). La correspondance est inscrite dans
l'ADR lui-même, au §5.2.

| Intitulé de la demande | Identifiant retenu |
|---|---|
| 1 — Qui réalise les migrations | **DP-1** |
| 2 — Déclenchement | **DP-9** *(nouveau)* |
| 3 — Verrou migration | **DP-2** |
| 4 — Version schéma | **DP-3** + **DP-7** |
| 5 — Compatibilité postes | **DP-4** |
| 6 — Sauvegarde | **DP-10** *(nouveau)* |
| 7 — Utilisateurs connectés | **DP-6** |
| 8 — Rôles PostgreSQL | **DP-5** |

De même, **QD-14 existait déjà** dans ADR-APP-DISTRIBUTION-001, et portait exactement le sujet demandé — qui
est l'opérateur qui migre la base. Elle a été **précisée** avec les trois options contractuelles, plutôt que
dupliquée sous un numéro neuf. La question créée porte donc le numéro **QD-15**, et traite la version minimale
supportée.

---

## 3. Décisions prises

### 3.1 ADR-PROD-DB-009 — cycle de vie de la base centrale

| # | Décision | Contenu |
|---|---|---|
| **DP-1** | **Autorité de migration : MA-B2** | `MMV.DatabaseManager`, outil **séparé**, est seul responsable des migrations PostgreSQL. **`MMV.App` ne migre jamais**, ni automatiquement ni sur action. Le chemin SQLite reste inchangé |
| **DP-9** | **Déclenchement** *(nouveau)* | action **explicite** d'un opérateur autorisé. « Autorisé » au sens PostgreSQL : il détient le rôle migrateur. **Aucun rôle applicatif MMV nouveau** |
| **DP-2** | **Verrou** | mécanisme **natif PostgreSQL** (famille `pg_advisory_lock`). **Aucune table de verrou applicative.** Forme exacte arrêtée en P4-6B |
| **DP-3** | **Version du schéma** *(partielle)* | version applicative en **SemVer** ; état du schéma par les **migrations EF** (`__EFMigrationsHistory`) ; poste incompatible **bloqué**. **La politique de compatibilité reste ouverte** |
| **DP-4** | **Compatibilité des postes : SB-3** | un poste incompatible **n'écrit rien**, **affiche un message explicite**, **demande une mise à jour**, et **ne migre jamais** |
| **DP-10** | **Sauvegarde** *(nouveau)* | **obligatoire et vérifiée** avant toute migration. Sans elle, la migration ne démarre pas. Durcit K-5, qui n'énonçait qu'un principe |
| **DP-6** | **Utilisateurs connectés** *(principe)* | un changement de schéma impose un **état de maintenance**. Stratégie exacte en P4-6B |
| **DP-5** | **Rôles PostgreSQL : RL-3** | trois rôles — **administrateur d'installation**, **migrateur**, **applicatif**. Le rôle applicatif n'a **aucun DDL**. **Noms définitifs ouverts** (P4-8) |
| **DP-7** | **Numérotation MMV** | version **unique**, **SemVer**, source unique, affichée et journalisée. Le `1.0.0` écrit en dur disparaît. Emplacement et règle d'incrément en P4-6B |

**Treize alternatives sont explicitement rejetées** (§7.1 de l'ADR, `RB-1` à `RB-13`), dont MA-A1, MA-A2, MA-C1,
MA-C2, MA-C3, MA-B1, LK-2, LK-3, LK-4, SB-1, SB-2, RL-1 et VS-3. Le §4 de l'ADR est **conservé tel quel** :
il reste la trace de l'instruction, et les options écartées y restent lisibles.

### 3.2 ADR-APP-DISTRIBUTION-001 — distribution et mise à jour

**Conservées sans changement** : DI-1 (self-contained `win-x64`, build CI seulement, Velopack), DI-2
(installation per-user, pas de MSI en V1), DI-4 (flux magasin local, jamais le canal éditeur, publication après
migration validée), DI-5 (outil séparé livré avec MMV, même version), DI-6 (signature depuis la CI, clé
protégée), DI-7 (retour arrière = binaires **et** base), DI-8 (.NET 10 avant la Release V1).

**Ajouté :**

| # | Décision | Contenu |
|---|---|---|
| **DI-3.4** | **Version minimale supportée** | un poste antérieur au minimum est **bloqué**, avec un message clair demandant une mise à jour. **Ne décide pas** : durée de grâce, politique commerciale, fréquence obligatoire, emplacement de déclaration → **QD-15** |
| **DI-9** | **Séparation installation / données** | le dossier d'installation Velopack **ne contient jamais** de données métier ; base, sauvegardes et données utilisateur vivent **ailleurs** ; une **désinstallation ne supprime jamais** les données ; le `packId` et les chemins doivent **garantir** cette séparation |

**Précisé :** DI-6 dit désormais explicitement qu'**aucun contrôle Authenticode supplémentaire n'est implémenté
en V1** — il reste reporté en **RP-1**, à prouver par le spike **SD-4**.

**Pourquoi DI-9 est une décision et non une recommandation.** Velopack remplace entièrement son dossier
`current` à chaque mise à jour, sous `%LocalAppData%\{packId}`. Or la base mono-poste vit sous
`%LOCALAPPDATA%\ManageMyVision\mmv.db`, valeur portée par la constante `ApplicationFolderName = "ManageMyVision"`
([SqliteDatabasePathResolver.cs:23](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs#L23),
vérifié à HEAD). **Un `packId` valant `ManageMyVision` détruirait la base à la première mise à jour.** C'est le
seul point de cette consolidation dont une erreur fait perdre des données de santé.

**Deux obligations nouvelles** : **OI-11** (aucune donnée sous le dossier d'installation ; désinstallation non
destructrice, vérifiée par SD-1) et **OI-12** (blocage par version minimale, message distinct, prouvé par test).

---

## 4. Décisions encore ouvertes

### 4.1 Bloquantes

| # | Question | Pourquoi elle bloque | Propriétaire |
|---|---|---|---|
| **Q-3** | **Politique de compatibilité** : égalité stricte (`Appliquées = Connues`), ou **fenêtre** ancrée sur une version minimale supportée ? | **La garde de version de P4-6B en est l'implémentation directe.** Elle ne peut pas être écrite avant | architecte + produit |

**C'est la question la plus lourde de la consolidation, et elle mérite d'être comprise avant d'être tranchée.**
ADR-APP-DISTRIBUTION-001 DI-4 impose l'ordre « base d'abord, postes ensuite ». Sous **égalité stricte**, dès que
la base est migrée, **tous** les postes non encore mis à jour passent en état E3 — donc bloqués, sans écriture.
Le magasin s'arrête **entre** la migration et la mise à jour du **dernier** poste. Le §5.4 d'ADR-PROD-DB-009
qualifiait déjà cette combinaison d'incohérente avec une montée progressive (DR-7).

**DI-3.4 crée l'instrument** qui permet d'y échapper — un plancher de version, donc une fenêtre — **mais ne
choisit pas** de l'utiliser. La fenêtre a son propre coût : une seconde source de vérité (VS-2), un changement
de modèle EF sur deux chaînes, la discipline « étendre puis contracter », et le fait que le minimum soit choisi
par le développeur — une erreur de sa part autorise un poste ancien sur un schéma incompatible, soit exactement
ce que l'obligation O8 interdit.

**Aucune des deux voies n'est gratuite.** C'est un arbitrage produit autant qu'architectural : il revient à
décider si une interruption de service à chaque migration est acceptable en magasin.

### 4.2 Ouvertes, non bloquantes pour le démarrage de P4-6B

| # | Question | Bloque |
|---|---|---|
| **Q-14** | **DP-8** — où vit le journal des migrations serveur, et que contient-il ? Seul point de décision resté **entièrement** ouvert | clôture de P4-6B |
| **Q-2 / QD-14** | **identité** de l'opérateur autorisé : propriétaire du magasin, support MMV, ou modèle mixte selon contrat. **La V1 ne doit imposer aucun de ces modèles** — d'où l'identifiant migrateur fourni au moment de la migration, jamais stocké sur un poste | procédure P4-8 |
| **QD-15** | où la **version minimale supportée** est déclarée, qui la choisit, selon quelle règle | P8, lié à Q-3 |
| **Q-9** | **DP-6** — garde technique (CX-2 à CX-4) ou procédure vérifiée (CX-5) ? Le principe est arrêté, la forme ne l'est pas | P4-6B, P4-6C |
| **Q-6** | EF Core 8 avec verrou LK-1, ou montée vers le verrou natif d'EF ≥ 9 ? Lié à **DI-8** (.NET 10) | forme du verrou |
| **Q-11** | revérification en cours de session | P4-6B |
| **Q-12** | **E7** — base PostgreSQL avec des tables **sans** historique EF | P4-6B |
| **QD-1**, **QD-10** | entité juridique éditrice ; nom du `packId` | acceptation d'ADR-APP-DISTRIBUTION-001 |

### 4.3 Levées par cette consolidation

**Q-1** (autorité de migration) par DP-1 · **Q-4** (rôles) par DP-5 · **Q-5** (numérotation) par DP-7 ·
**Q-8** (bundle EF et factory design-time) devenue sans objet, MA-B1 étant écarté · **Q-10** (« bloqué » après
une migration par le poste lui-même) devenue sans objet, le poste ne migrant jamais · **QD-13** (DI-4 et DI-5
valent-elles choix de MA-B2 ?) par DP-1 · **le principe de ND-9** par DI-9.

---

## 5. Impact sur la roadmap

Le découpage **P4-6A / P4-6B / P4-6C** venait du rapport de transition P4-6 et du rapport RECON-B-light, **tous
deux non suivis**. Il est désormais **inscrit** dans [la roadmap P4](../architecture/P4-multi-poste-roadmap.md),
§P4-6, ce que demandaient le résidu **R-1** et la question **Q-18**.

| Sous-lot | État inscrit | Condition |
|---|---|---|
| **P4-6A** — décisions d'architecture | **COMPLETE — PENDING ADR ACCEPTANCE** | les deux ADR restent PROPOSED jusqu'à la revue |
| **P4-6B** — cycle de vie de la base en multi-poste | **NEXT** | **P4-6A accepté** **et** **P4-5F vert** |
| **P4-6C** — levée du garde-fou (contenu absorbé de P4-5G) | **FUTURE** | **après P4-6B validé** |

**Non modifiés, conformément à la demande** : **P4-5F** reste `NEXT` ; **P4-5G** reste `ABSORBÉ PAR P4-6C` ;
P4-0 à P4-5E, P4-7 à P4-12 sont inchangés dans leur contenu.

**Une ligne du bloc d'état a changé de forme** : `P4-6 … P4-12 = NOT STARTED` devenait faux, P4-6A étant
complète. Elle est remplacée par les états P4-6A / P4-6B / P4-6C, suivis de `P4-7 … P4-12 = NOT STARTED`.

**Deux dépendances à ne pas confondre.** L'acceptation des ADR débloque la **spécification** de P4-6B ; elle ne
remplace pas **P4-5F**, qui reste le préalable **technique** — sans serveur PostgreSQL en CI, le verrou et la
garde ne peuvent pas être prouvés (critères S-1 à S-4).

---

## 6. Effet sur les deux objectifs de la consolidation

### 6.1 Migration vers .NET 10

**DI-8 est inchangée** : .NET 10 (LTS) avant la Release V1, aucune Release V1 sur .NET 8. La consolidation
**n'ouvre ni ne ferme** ce lot, dont ni le périmètre ni la date ne sont fixés par un ADR.

Deux liens sont désormais explicites : **Q-6** (rester sur EF Core 8 avec un verrou LK-1, ou monter de version
pour le verrou natif d'EF ≥ 9) rejoint DI-8, puisque la montée de runtime emporte celle d'EF Core et de Npgsql ;
et **DP-2** a été formulée comme une **famille** de mécanismes — natif PostgreSQL — précisément pour rester
compatible avec les deux issues de Q-6.

**Aucune décision de cette consolidation n'interdit ni ne présuppose la montée vers .NET 10.**

### 6.2 Démarrage de P4-6B

**Ce que P4-6B sait désormais** : qu'il construit un outil séparé (DP-1) ; que cet outil porte le verrou, le
contrôle de sauvegarde et le journal (DP-2, DP-10) ; que le poste se réduit à une **garde de version en lecture
seule** et à un **écran de blocage** (DP-4) ; qu'il n'y a **ni DDL, ni verrou, ni secret migrateur, ni table
nouvelle** côté poste ; que les rôles sont au nombre de trois (DP-5) ; que la version est unique et en SemVer
(DP-7).

**Ce qu'il ne sait pas encore** : la politique de compatibilité (**Q-3**), qui est la garde elle-même.

**Conclusion** : P4-6B est **spécifiable** et son périmètre est **considérablement réduit** par DP-1. Son
**écriture** reste conditionnée à Q-3 pour le volet garde de version, et à P4-5F pour toute preuve serveur.

---

## 7. Vérification — aucun code modifié

`git status --short`, après consolidation :

```
 M docs/architecture/ADR-PROD-DB-009.md              (fichier non suivi à HEAD — voir note)
 M docs/architecture/P4-multi-poste-roadmap.md
 M docs/architecture/adr-app-distribution-001-installation-and-updates.md
?? docs/implementation/P4-6-architecture-consolidation-report.md
```

> **Note.** ADR-PROD-DB-009 et ADR-APP-DISTRIBUTION-001 étaient **déjà non suivis** (`??`) avant cette
> consolidation, ayant été créés sans commit. Ils restent donc `??` dans `git status`, et non ` M`. Seule la
> roadmap était suivie, et apparaît en ` M`.

**Contrôles :**

| Contrôle | Résultat |
|---|---|
| Fichiers `.cs` modifiés | **aucun** |
| Fichiers `.csproj` modifiés | **aucun** |
| Migrations EF modifiées | **aucune** |
| Fichiers de CI (`.github/`) modifiés | **aucun** |
| Paquets NuGet ajoutés, retirés ou mis à jour | **aucun** |
| Commit créé | **aucun** |
| Push effectué | **aucun** |
| Tests exécutés | **aucun** — cette consolidation n'avait pas à en exécuter |

**La baseline de tests reste celle de P4-4C : 1569.** Elle est **citée**, jamais revendiquée par ce lot.

---

## 8. Ce que l'architecte doit trancher pour clore P4-6A

Par ordre de poids :

1. **Q-3 — politique de compatibilité.** Bloquante pour la garde de version de P4-6B. Arbitrage produit autant
   qu'architectural : une interruption de service à chaque migration est-elle acceptable en magasin ?
2. **QD-14 / Q-2 — identité de l'opérateur autorisé.** Trois modèles possibles ; la V1 ne doit en imposer aucun.
3. **Q-14 — journal des migrations serveur.** Bloque la clôture de P4-6B, pas son démarrage.
4. **QD-1, QD-10** — entité juridique éditrice, nom du `packId`. Bloquent l'acceptation
   d'ADR-APP-DISTRIBUTION-001.
5. **Acceptation formelle des deux ADR**, puis **commit** de la roadmap, qui seul clôt le résidu R-1.

---

## 9. Références

- [ADR-PROD-DB-009 — migrations et compatibilité de version en multi-poste](../architecture/ADR-PROD-DB-009.md)
  (PROPOSED — décisions arrêtées)
- [ADR-APP-DISTRIBUTION-001 — installation, distribution et mise à jour](../architecture/adr-app-distribution-001-installation-and-updates.md)
  (PROPOSED)
- [Roadmap P4 — §P4-6 et son découpage](../architecture/P4-multi-poste-roadmap.md)
- [ADR-PROD-DB-002 §15 — obligations O8, O9, O10, O11, O14](../architecture/adr-prod-db-002-server-database-provider-selection.md)
- [ADR-PROD-DB-005 — architecture des migrations](../architecture/adr-prod-db-005-migration-architecture.md)
- [ADR-PROD-DB-007 — prévention de la dérive](../architecture/adr-prod-db-007-schema-drift-prevention.md)
- [ADR-PROD-DB-008 — tests d'intégration PostgreSQL](../architecture/adr-prod-db-008-postgresql-integration-testing.md)
- Rapport de transition P4-6 (`P4-6-transition-audit-report.md`, **non suivi**)
- Rapport RECON-B-light (`P4-RECON-B-light-report.md`, **non suivi**)
- Analyse P4-6 (`P4-6-architecture-decision-analysis.md`, **non suivie**)
- Analyse de stratégie d'installation et de mise à jour
  (`MMV-installer-update-strategy-analysis.md`, **non suivie**)

---

READY FOR ARCHITECT REVIEW
