# ADR-APP-DISTRIBUTION-001 — Installation, distribution et mise à jour de MMV Desktop

> **Statut : PROPOSED (décisions validées sur le fond par l'architecte, AUCUNE implémentation).**
> Cet ADR fixe **comment MMV est produit, installé, mis à jour, signé et remis en arrière** pour la Release V1.
> Il reprend **uniquement** les décisions validées à l'issue de l'analyse de stratégie d'installation et de mise à
> jour. Les autres recommandations de cette analyse ne sont **ni retenues ni rejetées** : le §7 les liste comme
> non décidées. Cet ADR **ne crée aucun projet, ne référence aucun paquet, et ne modifie ni code, ni test, ni
> configuration, ni CI, ni roadmap.** Velopack n'est pas intégré. L'implémentation appartient au lot
> « P8 — Mise à jour applicative », qui précède « P10 — Release V1 ».
>
> Date : 22 septembre 2026. Branche : `p4-multi-poste`. HEAD de référence :
> `ccbeb3259444d26264e20323058764dd59318e1c` (= `origin/p4-multi-poste`).
> Entrées : [analyse de stratégie d'installation et de mise à jour](../implementation/MMV-installer-update-strategy-analysis.md) ·
> [ADR-PROD-DB-009](ADR-PROD-DB-009.md) (**ACCEPTED**) ·
> [analyse P4-6](../implementation/P4-6-architecture-decision-analysis.md) ·
> [risk-register R-25](risk-register.md) ·
> [roadmap master P2J-10A, P2J-10B](../roadmap/MMV-master-professionalization-roadmap.md) ·
> [ADR-PROD-DB-008 §2.4](adr-prod-db-008-postgresql-integration-testing.md) ·
> [frontières de couches §4.2](adr-application-boundaries.md) · code à HEAD.
> **Le dépôt réel prime toujours sur ce document.**
>
> **Note de numérotation.** Cet ADR ouvre la série **APP-DISTRIBUTION**. La série PROD-DB traite la base de
> données : la distribution en dépend mais n'en relève pas. [adr-candidates.md](adr-candidates.md) numérote déjà
> des « ADR-00N » d'une autre série ; un préfixe distinct évite une ambiguïté de plus.

**Sens de « V1 ».** V1 désigne la **Release V1 commerciale** (« P10 » de la [roadmap P5](P5-product-completion-roadmap.md)), dont la mise
à jour applicative est le lot « P8 ». P4 peut se clore avec des postes mis à jour à la main (HY-7 de l'analyse
P4-6). Cet ADR ne change rien au périmètre de P4.

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `CODE` | constaté par lecture du code à HEAD |
| `DOC` | énoncé d'un document du dépôt |
| `EXTERNE Sn` | fait extérieur au dépôt, vérifié le 22/09/2026 dans la source Sn du §15 de l'analyse |
| `À PROUVER` | à établir par un spike avant acceptation (§10) |

**Identifiants propres à cet ADR** : **DI-n** décisions · **RP-n** points reportés · **ND-n** sujets non décidés ·
**OI-n** obligations d'implémentation. Les spikes **SD-n** et les questions **QD-n** gardent la numérotation de
l'analyse, pour la traçabilité.

---

## 1. Statut

**PROPOSED.**

Les **neuf** décisions du §4 sont **validées par l'architecte sur le fond**. L'ADR reste PROPOSED, car son
acceptation dépend d'éléments qu'une validation de principe ne remplace pas :

| # | Condition | État au 22/09/2026 |
|---|---|---|
| 1 | preuves **SD-1, SD-2, SD-3 et SD-5** (§10) | **NON PRODUITES** — aucun spike n'a été exécuté. **C'est le seul obstacle technique restant.** |
| 2 | réponses aux questions bloquantes **QD-1, QD-10 et QD-13** (§12) | **QD-13 levée** par ADR-PROD-DB-009 DP-1. **QD-1** (entité juridique éditrice) et **QD-10** (nom du `packId`) **restent ouvertes** : ce sont des faits extérieurs au dépôt, qu'aucune revue d'architecture ne produit |
| 3 | **ADR-PROD-DB-009 retient un outil de migration séparé** (DP-1). DI-4 et DI-5 en dépendent (§8.3) | **SATISFAITE** — ADR-PROD-DB-009 est **ACCEPTED** depuis le 22/09/2026, avec **DP-1 = MA-B2**, `MMV.DatabaseManager` |

> **Mise à jour du 22 septembre 2026 — revue d'acceptation P4-6A.** Cet ADR **reste PROPOSED**, et le §1 dit
> pourquoi : ses conditions 1 et 2 ne sont pas satisfaites. Aucune n'est une décision d'architecture — ce sont
> **quatre spikes non exécutés** et **deux faits extérieurs** (entité juridique éditrice, nom du paquet). La
> revue de l'architecte **ne pouvait pas les produire**, et passer cet ADR à ACCEPTED aurait fait dire au dépôt
> une chose fausse. **Les neuf décisions du §4 sont, elles, validées sur le fond** et n'attendent plus rien.
>
> Ce que la revue change ici :
>
> | Élément | Changement |
> |---|---|
> | **DI-3.4** | **complétée** : la règle de la fenêtre et l'emplacement du plancher de version sont désormais fixés pour le multi-poste (ADR-PROD-DB-009 DP-3) |
> | **DI-4** | **complétée** : une seule release porteuse de schéma par tournée de postes (**OI-13**) |
> | **QD-14** | **LEVÉE** — modèle opérateur **mixte selon contrat** |
> | **QD-15** | **LEVÉE pour le multi-poste** ; résidu mono-poste et commercial explicité |
> | **§2.2, §8.3** | l'**égalité stricte** (REC-4c) est **rejetée** par ADR-PROD-DB-009 RB-14 : la tension la plus lourde de l'ensemble **disparaît** |
>
> Voir le [rapport d'acceptation P4-6A](../implementation/P4-6A-adr-acceptance-report.md) et, pour l'historique,
> le [rapport de consolidation](../implementation/P4-6-architecture-consolidation-report.md).

---

## 2. Contexte

### 2.1 État du dépôt

| Constat | Preuve |
|---|---|
| **Aucun installeur, aucune signature, aucun updater, aucune vérification d'intégrité.** Risque **R-25, HIGH** | `DOC` [risk-register R-25](risk-register.md) |
| `MMV.App` : `WinExe`, `net8.0`, Avalonia 11.2.8. SDK épinglé en 8.0.417 | `CODE` [MMV.App.csproj:4-5](../../src/MMV.App/MMV.App.csproj#L4-L5), l. 20 ; [global.json](../../global.json) |
| `Main` est une expression unique qui démarre Avalonia : aucun point d'ancrage pour une installation ou une mise à jour | `CODE` [Program.cs:15-16](../../src/MMV.App/Program.cs#L15-L16) |
| **Aucune version MMV** : pas de `<Version>` dans [Directory.Build.props](../../Directory.Build.props). L'écran « À propos » affiche un `1.0.0` écrit en dur | `CODE` [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) |
| Base SQLite **par utilisateur Windows**, sous `%LocalAppData%\ManageMyVision`, avec ses sauvegardes et son journal de migration | `CODE` [SqliteDatabasePathResolver.cs](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) |
| SQLite : chaque poste sauvegarde sa base **avant** toute mutation, puis la migre, au démarrage | `CODE` [SqliteDatabaseManager.cs:89-95](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L89-L95) |
| Configuration par **variables d'environnement seulement** | `CODE` [DatabaseProviderResolver.cs:33-39](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L33-L39) |
| CI : un job, build **Debug**, `permissions: contents: read`. Aucun `publish`, aucune release | `CODE` [ci.yml](../../.github/workflows/ci.yml) |
| Poste de développement : la seule source NuGet est un cache hors ligne. **Tout paquet nouveau y est non restaurable** | `DOC` [ADR-PROD-DB-008 §2.4](adr-prod-db-008-postgresql-integration-testing.md) |
| **.NET 8 : fin de support le 10 novembre 2026.** .NET 10 (LTS) : support jusqu'au 14 novembre 2028 | `EXTERNE S22` |

### 2.2 Ce que le multi-poste impose

**ADR-PROD-DB-009 est ACCEPTED depuis le 22/09/2026.** Il impose trois règles, dont **une diffère de ce que
l'analyse P4-6 recommandait** :

- un **outil de migration séparé**, `MMV.DatabaseManager` (DP-1 = MA-B2 ; REC-1, REC-6) ;
- une **fenêtre de compatibilité N-1**, sous discipline « étendre → migrer → contracter » (DP-3). **L'égalité
  stricte que recommandait REC-4c est rejetée** (RB-14) ;
- l'ordre **« base d'abord, postes ensuite »** (DI-4 ; REC-10).

Un poste qui suivrait directement le canal de l'éditeur installerait une release munie d'une migration **avant**
que la base soit migrée, et serait bloqué (état **E2**, sur lequel **aucune fenêtre n'existe**). Tous les postes
le seraient en même temps : **le magasin s'arrêterait dès la publication**, jusqu'à l'intervention de
l'opérateur. C'est ce que DI-4 empêche.

**En sens inverse, la fenêtre N-1 fait la différence.** Une fois la base migrée, les postes restés à la version
précédente **continuent de travailler** (état **E3a**) pendant toute la tournée. Sous égalité stricte, ils
auraient tous été bloqués entre la migration et la mise à jour du **dernier** poste.

### 2.3 Enjeux

- **Données de santé** : catégorie particulière du RGPD en Belgique, loi 09-08 au Maroc
  ([audit phase 1 §4.7](phase-1-technical-audit.md)). Aucune donnée patient ne transite par la mise à jour. En
  revanche, le canal de mise à jour **exécute du code sur tous les postes**, et ce code accède à la base. Son
  **intégrité** prime.
- **Clients non techniques** ; postes supposés non gérés par une DSI (hypothèse HD-2 de l'analyse).
- **Runtime embarqué** : les correctifs de sécurité du runtime .NET ne parviennent aux clients que par des
  releases MMV. L'updater est une exigence de sécurité, pas un confort.

---

## 3. Question de décision

> **Comment MMV est-il produit, installé, mis à jour, signé et remis en arrière, en mono-poste SQLite comme en
> multi-poste PostgreSQL, sans que les binaires d'un poste et la base qu'il ouvre ne divergent jamais ?**

---

## 4. Décision

### DI-1 — Production des artefacts

1. MMV est publié par `dotnet publish` en mode **autonome** (*self-contained*), pour **`win-x64`**.
2. Les artefacts distribués sont produits **uniquement par la CI**. Aucun artefact construit sur un poste de
   développement n'est distribué.

*Motifs* : aucun prérequis runtime sur le poste du client ; build reproductible ; le poste de développement ne peut
pas restaurer de paquet nouveau (§2.1).

### DI-2 — Installation

1. **Velopack** est retenu comme **installeur et comme updater** de la V1. Un seul outil couvre les deux rôles.
2. L'installation se fait **par utilisateur Windows**, sans droits administrateur.
3. **Aucun MSI en V1.**

*Motifs* : un opticien installe MMV seul ; aucune mise à jour ne demande de mot de passe administrateur au
comptoir ; sans DSI, un MSI par machine n'apporte que l'élévation.

### DI-3 — Mise à jour

1. La mise à jour est **semi-automatique** : MMV détecte une nouvelle version et la propose. Son **application**
   reste soumise à une décision humaine. Aucune mise à jour n'est imposée en silence.
2. Une mise à jour n'est **jamais appliquée pendant une saisie**.
3. Velopack est placé **derrière une abstraction MMV**. Le code MMV dépend de cette abstraction, pas de Velopack :
   changer d'updater revient à changer une implémentation. Domain et Application n'en dépendent pas, comme leurs
   règles actuelles l'imposent déjà ([frontières de couches §4.2](adr-application-boundaries.md)).
4. **MMV porte une notion de version minimale supportée.** Un poste dont la version applicative est
   **antérieure** à ce minimum est **bloqué**, avec un message clair qui demande une mise à jour. Le blocage
   n'est pas silencieux et ne dégrade pas l'application : il l'arrête proprement, comme le fait la garde de
   version d'ADR-PROD-DB-009 (DP-4).

*Motifs* : ne jamais interrompre le travail ; garder réversible le choix d'un outil jeune (Velopack 1.0 date de
mai 2026, `EXTERNE S1`). Pour DI-3.4 : sans plancher de version, rien ne distingue un poste simplement en retard
d'un poste **trop** en retard pour être servi, et la seule réponse possible resterait l'égalité stricte, qui
bloque tout le magasin dès la première migration (ADR-PROD-DB-009 §5.2, DP-3).

**5. Emplacement et règle du plancher — fixés le 22/09/2026 pour le multi-poste.** Le minimum supporté est
**déclaré par le schéma** de la base centrale, **calculé** par `MMV.DatabaseManager`, selon la règle **N-1** :
la version applicative de la release précédente **qui a modifié le schéma**. Il n'est **jamais saisi par un
développeur** ni codé en dur (ADR-PROD-DB-009 **DP-3 §5.2.3.5**, **RB-16**). **QD-15 est levée pour le
multi-poste.**

**Ce que DI-3.4 ne décide toujours pas :**

- ni **durée de grâce**, ni délai entre publication et blocage ;
- ni **politique commerciale** (qui a droit à quelles versions, à quel prix) ;
- ni **fréquence de mise à jour obligatoire** ;
- ni le **plancher en mono-poste SQLite**, où la question est d'une autre nature : un poste mono-poste migre sa
  propre base au démarrage, donc son binaire et son schéma **ne divergent jamais**. Le blocage de DI-3.4 n'y
  concerne pas le schéma mais le **canal de mise à jour** — servir, ou refuser de servir, un poste trop ancien.
  Cela reste **P8** (résidu de QD-15).

**État du code.** Aucune notion de version minimale n'existe aujourd'hui : `MinimumSupportedVersion`,
`MinimumVersion` et `MinVersion` ne renvoient **aucune occurrence** dans `src/` ni `tests/` (`CODE`, `grep`).
DI-3.4 suppose donc d'abord **DI-5** et **ADR-PROD-DB-009 DP-7** : une version applicative unique, en SemVer,
lisible à l'exécution. Le `1.0.0` écrit en dur de
[SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) ne peut rien fonder.

**Articulation avec la base.** Un poste peut être trop ancien de deux manières indépendantes :

| Source du verdict | Qui décide | Décision |
|---|---|---|
| **Le schéma de la base** : les migrations appliquées ne correspondent pas à celles du binaire | ADR-PROD-DB-009 DP-3, DP-4 | garde de version, blocage sans écriture |
| **La version applicative** : le binaire est antérieur au minimum supporté | **DI-3.4** | blocage avec demande de mise à jour |

Les deux doivent produire un **message distinct** : « ce poste doit être mis à jour » ne se confond pas avec
« la base doit être mise à jour par l'opérateur ». C'est la raison pour laquelle l'écran de blocage compte au
moins trois états (ADR-PROD-DB-009 DP-4).

Savoir si une application « à la fermeture de MMV » vaut décision humaine est la question QD-8.

### DI-4 — Multi-poste : flux local du magasin

1. En multi-poste, les postes **ne contactent jamais le canal de l'éditeur**, ni pour détecter ni pour
   télécharger une mise à jour.
2. Ils suivent un **flux local au magasin**.
3. Une release n'est publiée dans ce flux **qu'après** que `MMV.DatabaseManager` **de la même release** a amené
   la base à exactement ses migrations. Quand la release ne contient aucune migration, cette étape n'applique rien.
4. En V1, cette publication est un **geste de l'opérateur**, encadré par une procédure. Son automatisation est
   reportée (RP-2).
5. **Une seule release porteuse de schéma par tournée de postes.** Une release qui modifie le schéma n'est
   publiée dans le flux local **que si la tournée précédente est terminée**, c'est-à-dire si **tous** les postes
   ont atteint au moins la version de la dernière release porteuse de schéma. Obligation **OI-13**.

En multi-poste, **la version d'un poste est donc dictée par la base, pas par l'éditeur**. L'ordre « base d'abord »
est conservé, et un magasin sans Internet reste servi. En mono-poste SQLite, le poste suit le canal de l'éditeur :
sa base est sauvegardée puis migrée localement au démarrage, par le chemin existant.

*Motif de DI-4.5.* La fenêtre de compatibilité vaut **un cran** (ADR-PROD-DB-009 DP-3). Publier une seconde
release porteuse de schéma avant la fin de la tournée reviendrait à *contracter* — supprimer ce que la version
N-1 utilise — alors que des postes sont encore en N-1. Ils passeraient de **E3a** (servis) à **E3b** (bloqués),
et la fenêtre n'aurait servi à rien. C'est le **coût opérationnel** de la fenêtre : elle borne la tournée dans
le temps. Une release **sans migration** ne consomme aucun cran et n'est donc pas concernée.

### DI-5 — Livraison de `MMV.DatabaseManager`

1. `MMV.DatabaseManager` est livré **dans le même paquet** que `MMV.App`.
2. Les deux exécutables portent **la même version applicative** et embarquent **la même assembly de migrations**.

*Motif* : l'outil qui migre la base connaît, **par construction**, exactement les migrations que connaîtront les
postes. Aucun contrôle de version croisé entre l'outil et la base n'est nécessaire (HY-8 de l'analyse P4-6).

La présence de l'outil sur les postes n'est sans risque que si la frontière est **l'identifiant migrateur**, et non
le binaire, et si cet identifiant n'est stocké sur aucun poste client (REC-5 de l'analyse P4-6, non acceptée ;
§8.3).

### DI-6 — Signature

1. Les artefacts distribués sont signés **Authenticode**.
2. La signature s'exécute **uniquement dans la CI**, jamais sur un poste de développement.
3. La clé privée est protégée par un **HSM ou un service de signature équivalent**. Aucun fichier de clé n'existe
   dans le dépôt, dans un secret de CI ou sur un poste.

*Motifs* : la clé de signature est la dernière barrière contre un binaire malveillant qui porterait l'identité
MMV. Depuis juin 2023, la clé d'un certificat de signature de code doit de toute façon résider dans un HSM
(`EXTERNE S18, S21`).

Le choix du service et du certificat dépend de QD-1 (ND-2).

**Ce que DI-6 n'ajoute pas.** **Aucun contrôle Authenticode supplémentaire n'est implémenté en V1** : MMV ne
vérifie pas lui-même, avant d'appliquer une mise à jour, que chaque fichier du paquet porte la signature de
l'identité MMV attendue. Ce contrôle reste **reporté en spike** — **RP-1**, à prouver par **SD-4** (§10) — et
n'est pas une obligation d'implémentation de P8. DI-6 couvre la **production** de la signature, pas sa
**vérification à l'application**. Le risque résiduel est énoncé au §6, et le contrôle d'écriture du canal
(ND-1) en est la contrepartie obligatoire avant la Release V1.

### DI-7 — Retour arrière

1. L'unité de retour arrière est **le couple (binaires, base)**.
2. Un retour arrière ne ramène **jamais les binaires seuls**. Revenir à des binaires antérieurs n'est admis que si
   la base est ramenée à un état que ces binaires connaissent.
3. Par conséquent, **l'updater ne rétrograde jamais un poste de lui-même** : il n'agit que sur les binaires.

Quand la release fautive ne contenait aucune migration, la base n'a pas changé et le couple reste cohérent. Quand
elle en a appliqué une, le seul moyen existant de ramener la base est la **restauration de la sauvegarde de
pré-migration** : le fichier de sauvegarde du poste en SQLite (`CODE`, §2.1), une sauvegarde serveur en
PostgreSQL (P4-9, à venir). ADR-PROD-DB-009 (U-7) constate que seule la restauration est réaliste.

### DI-8 — Version de .NET

1. MMV passe à **.NET 10 (LTS) avant la Release V1**.
2. Aucune Release V1 n'est publiée sur .NET 8.

*Motif* : en publication autonome (DI-1), le runtime est embarqué dans le paquet. Une Release V1 sur .NET 8, hors
support après le 10 novembre 2026 (`EXTERNE S22`), livrerait un runtime privé de correctifs de sécurité.

La montée de version (SDK, `TargetFramework`, EF Core, Npgsql, compatibilité Avalonia, CI) est un lot distinct.
Cet ADR n'en fixe ni le périmètre ni la date. Elle rejoint Q-A8 de l'analyse P4-6 et Q-6 d'ADR-PROD-DB-009.

### DI-9 — Séparation du dossier d'installation et des données

**Décision obligatoire.** Elle promeut ND-9 en décision : c'est la seule dont un mauvais choix **détruit des
données de santé**.

1. Le **dossier d'installation Velopack ne contient jamais de données métier.** Ni base, ni sauvegarde, ni
   journal de migration, ni fichier de configuration par poste, ni export.
2. La **base SQLite, ses sauvegardes et toute donnée utilisateur vivent dans un emplacement séparé**, distinct
   du dossier d'installation et **hors de son arborescence**.
3. Une **désinstallation ne supprime jamais les données métier.** Désinstaller MMV retire l'application, pas le
   fichier de la patientèle.
4. Le choix du **`packId`** et des **chemins** doit **garantir** cette séparation, et non la rendre seulement
   probable.

**Pourquoi c'est une décision et non une recommandation.** Velopack installe sous `%LocalAppData%\{packId}` et
**remplace entièrement** son dossier `current` à chaque mise à jour (`EXTERNE S2`). Or la base mono-poste vit
aujourd'hui sous `%LOCALAPPDATA%\ManageMyVision\mmv.db`, valeur portée par la constante
`ApplicationFolderName = "ManageMyVision"` (`CODE`
[SqliteDatabasePathResolver.cs:23](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs#L23)).
**Un `packId` valant `ManageMyVision` ferait donc du dossier de données le dossier d'installation.** La
conséquence serait la perte de la base à une mise à jour ou à une désinstallation.

**Ce qui en découle, sans être tranché ici :**

- Le **`packId` ne vaut pas `ManageMyVision`.** Sa valeur exacte, et le libellé de l'entrée « Programmes »,
  restent **QD-10**.
- Le **chemin des données ne change pas** du fait de cet ADR. `SqliteDatabasePathResolver` reste la source
  unique de résolution ; DI-9 lui interdit seulement de pointer sous le dossier d'installation.
- La **vérification appartient à SD-1** (§10) : après désinstallation, le dossier de données doit être
  **intact**. C'est un critère de réussite, pas une observation.
- La configuration par poste, dont la source de mise à jour, vit **hors du dossier d'installation** (ND-10) —
  sans quoi une mise à jour l'écraserait.

*Portée* : DI-9 vaut pour le mono-poste SQLite, où les données sont locales. En multi-poste, les données
métier vivent sur le serveur PostgreSQL, mais la configuration par poste et les journaux locaux relèvent de la
même règle.

---

## 5. Options écartées pour la V1

| Option | Écartée par | Motif |
|---|---|---|
| MSI pour le client (WiX, Advanced Installer, InstallShield) | DI-2 | élévation à chaque mise à jour, aucun updater, aucun gain sans DSI. **Reporté** (RP-3, RP-4) |
| MSIX, Microsoft Store | DI-2, DI-4 | les nouveaux fichiers qu'une application empaquetée crée sous `AppData` sont redirigés, puis **supprimés à la désinstallation** (`EXTERNE S16`) ; or la base mono-poste y vit. L'installation depuis le web passe par un protocole désactivé par défaut (`EXTERNE S17`). Le Store pousse les mises à jour hors de l'ordre « base d'abord » |
| ClickOnce | DI-2 | outillage pensé pour WinForms et WPF ; `ApplicationDeployment` indisponible en .NET moderne (`EXTERNE S10`) |
| Squirrel.Windows | DI-2 | maintenance incertaine : appel à mainteneurs (`EXTERNE S9`) |
| AutoUpdater.NET | DI-2 | WinForms et WPF seulement (`EXTERNE S11`) |
| Updater écrit par MMV | DI-2, DI-3 | réinvention d'un mécanisme de sécurité critique |
| winget | DI-4 | mise à jour à l'initiative de l'utilisateur, hors de l'ordre « base d'abord » |
| Publication dépendante du framework | DI-1 | runtime à installer et à maintenir sur chaque poste ; version réelle non maîtrisée |
| Artefact construit ou signé sur un poste de développement ; jeton de signature USB | DI-1, DI-6 | build non reproductible ; un jeton physique ne se branche pas sur un runner hébergé |
| Postes multi-poste reliés au canal de l'éditeur, **y compris épinglés** sur une version cible (LF-4 de l'analyse) | DI-4 | contredit DI-4.1. Seul un amendement de DI-4 peut le réintroduire (§11) |
| Rétrogradage automatique par l'updater | DI-7 | il ramènerait les binaires seuls |
| Release V1 sur .NET 8 | DI-8 | runtime hors support |
| **`packId` valant `ManageMyVision`**, ou données métier sous le dossier d'installation | **DI-9** | Velopack remplace entièrement son dossier `current` à chaque mise à jour (`EXTERNE S2`) : la base mono-poste serait perdue à la première mise à jour ou à la désinstallation |
| **Désinstallation qui supprime les données métier** | **DI-9** | données de santé ; aucune suppression ne doit être un effet de bord d'une désinstallation |
| **Blocage silencieux, ou dégradation, d'un poste trop ancien** | **DI-3.4** | un poste qui ne peut plus être servi doit le **dire**, pas se comporter étrangement |

---

## 6. Points reportés

| # | Point reporté | Ce qui s'applique en V1 | Risque résiduel accepté | Déclencheur de réexamen |
|---|---|---|---|---|
| **RP-1** | **Contrôle explicite Authenticode avant application** : vérifier que chaque fichier du paquet téléchargé porte la signature de l'identité MMV attendue (SD-4) | Velopack vérifie des sommes de contrôle **lues dans le flux lui-même** (`EXTERNE S4`) : il garantit que le paquet est celui que décrit le flux, pas qu'il vient de l'éditeur. Aucune vérification de l'éditeur n'est documentée sous Windows (`EXTERNE S7`) | un acteur capable d'**écrire dans le canal** peut y publier un paquet cohérent, qui serait appliqué. En multi-poste, la promotion humaine (DI-4) freine la propagation ; en mono-poste, rien ne s'interpose | Velopack fournit la vérification de l'éditeur ; incident sur le canal ; revue de sécurité avant la Release V1 |
| **RP-2** | **Automatisation complète du multi-poste** : promotion vérifiée par l'outil, qui ne publie la release N que si la base contient exactement les migrations de N | procédure de l'opérateur (DI-4.4) | une release publiée avant sa migration bloque les postes qui l'installent (E2). C'est un arrêt, pas une corruption, **à condition** que la garde de version d'ADR-PROD-DB-009 existe (§8.3) | premier incident de procédure ; opticien sans opérateur technique |
| **RP-3** | **MSI**, par utilisateur ou par machine | `Setup.exe` de Velopack | aucun, hors client géré | client géré par une DSI ; poste partagé par plusieurs comptes Windows |
| **RP-4** | **Déploiement par une DSI** (GPO, Intune, AppLocker) | installation par utilisateur | une politique qui bloque les exécutables sous `%LocalAppData%` empêche l'installation | premier client géré |

**Conséquence du report de RP-1.** Tant que RP-1 est reporté, la signature (DI-6) protège l'installation initiale
et identifie les binaires, mais **personne ne la vérifie avant l'application d'une mise à jour**. En V1,
l'authenticité d'une mise à jour repose donc sur le **contrôle d'écriture du canal de l'éditeur**, et en plus, en
multi-poste, sur la promotion humaine. Cet ADR ne décide pas ce contrôle d'écriture (ND-1). **Il doit être décidé
avant la Release V1.**

---

## 7. Ce que cet ADR ne décide pas

Recommandations de l'analyse qui ne figurent pas parmi les décisions validées. Elles ne sont **ni retenues ni
rejetées**. Chacune doit être tranchée avant le lot indiqué.

| # | Sujet | Recommandation de l'analyse, non retenue ici | Dépend de | Bloque |
|---|---|---|---|---|
| ND-1 | hébergement et contrôle d'écriture du canal de l'éditeur ; canaux | stockage objet HTTPS en UE, lecture publique, écriture réservée au job de release par identité fédérée OIDC ; canaux `stable` et `pilote` (RD-5) | QD-2 | Release V1 (§6) |
| ND-2 | service de signature et type de certificat | Azure Artifact Signing si l'entité éditrice est éligible (UE), sinon certificat OV en HSM cloud ; ni EV ni jeton USB (RD-4) | QD-1 | **ACCEPTED** |
| ND-3 | périmètre signé, horodatage, contrôle en CI | tous les PE de MMV, `Setup.exe` et `Update.exe` ; horodatage RFC 3161, les certificats étant limités à 460 jours (`EXTERNE S21`) ; `signtool verify` bloquant ; signature **pendant** l'empaquetage Velopack, sans quoi les sommes de contrôle sont invalidées (`EXTERNE S3`) | SD-3 | P8 |
| ND-4 | forme de la publication | dossier, sans fichier unique ni *trimming* ; ReadyToRun à mesurer | SD-9 | P8 |
| ND-5 | forme du flux local | partage réseau en lecture seule sur le serveur (LF-1) ; repli : installation manuelle sur chaque poste (LF-0) | SD-2, QD-3 | P8 ; procédure P4-8 |
| ND-6 | moment de la vérification et de l'application ; flux injoignable | vérification au démarrage seulement ; application sur action ou à la fermeture ; un flux injoignable n'empêche jamais le démarrage | QD-8 | P8 |
| ND-7 | mécanisme par défaut du retour arrière | correctif en avant : republier l'ancien code sous un numéro supérieur. Rétrogradage réservé à une procédure de support (RD-8) | — | P8 |
| ND-8 | acheminement de `MMV.DatabaseManager` sur le serveur | archive portable de la même release ; aucun installeur serveur en V1 (RD-9) | SD-1 | P4-8 |
| ~~ND-9~~ | ~~séparation du dossier d'installation et du dossier de données~~ | **PROMU EN DÉCISION : voir DI-9.** Le principe (aucune donnée sous le dossier d'installation, désinstallation non destructrice, `packId` ≠ `ManageMyVision`) n'est plus « non décidé ». **Seul le nom exact du `packId` reste ouvert** — QD-10 | — | — |
| ND-10 | configuration par poste, dont la source de mise à jour | fichier par poste, hors du dossier d'installation ; paquet identique pour tous les clients | — | P4-8 (D-13) |
| ND-11 | cadence des releases de correctif du runtime | revue mensuelle des correctifs .NET | QD-9 | P8 |
| ND-12 | plan B | Inno Setup avec NetSparkle (flux signé Ed25519) | — | §11 |
| ND-13 | garde de rétrogradage SQLite avant la première release munie de l'updater | préalable (RD-10, REC-4c) | QD-12 | DI-7 (§8.3) |

---

## 8. Conséquences

### 8.1 Positives

- **R-25 est traité sur le principe** : installeur, signature et updater sont décidés. Son volet « vérification
  d'intégrité » ne l'est qu'en partie tant que RP-1 est reporté. R-25 ne sera clos qu'à la livraison de P8, preuves
  comprises.
- Un opticien installe et met à jour MMV **sans droits administrateur**.
- Les correctifs du runtime deviennent **livrables** (DI-1, DI-3, DI-8).
- En multi-poste, l'ordre « base d'abord » est **conservé par construction** (DI-4), et un magasin sans Internet
  est servi.
- `MMV.DatabaseManager` et les postes connaissent **les mêmes migrations**, sans contrôle croisé (DI-5).
- Le choix de l'updater reste **réversible** : il tient dans une implémentation derrière une abstraction (DI-3).
- **La désinstallation cesse d'être un risque de perte de données** : DI-9 en fait une décision, vérifiée par
  SD-1 et par OI-11, au lieu d'une précaution à ne pas oublier.
- **Un poste trop ancien devient un état nommé**, avec un message et une issue, au lieu d'un comportement
  indéterminé (DI-3.4).
- **Le magasin ne s'arrête plus pendant une tournée de mise à jour.** C'est l'apport de la fenêtre N-1
  (ADR-PROD-DB-009 DP-3) à cet ADR : l'ordre « base d'abord » de DI-4 cesse d'avoir un coût d'indisponibilité.

### 8.2 Négatives, assumées

- **Première dépendance NuGet nouvelle** depuis la contrainte d'[ADR-PROD-DB-008 §2.4](adr-prod-db-008-postgresql-integration-testing.md).
  Tant que le cache hors ligne n'est pas alimenté, Velopack ne se restaure pas sur le poste de développement. Le
  build local ne doit pas casser pour autant (SD-8).
- **Le workflow CI change de nature** : un job de release (publication, empaquetage, signature, publication des
  artefacts) s'ajoute au job de vérification, avec des permissions plus larges que `contents: read`.
- **Coût récurrent de signature**, et délai de validation de l'identité à anticiper (QD-1, QD-9).
- **Une étape manuelle de l'opérateur** à chaque release multi-poste (RP-2).
- **Une tournée de postes bornée dans le temps** (DI-4.5, OI-13) : la fenêtre vaut un cran, donc une release
  porteuse de schéma ne peut pas en suivre une autre avant la fin de la tournée. C'est une contrainte de
  **cadence de release**, nouvelle, et purement procédurale.
- **Une installation par compte Windows.** C'est déjà le cas de la base mono-poste (§2.1) : deux comptes sur un
  même poste ont deux bases (QD-4).
- **Des releases de correctif du runtime à assurer.** Sans elles, DI-1 embarque des failles connues.
- **Un risque d'authenticité résiduel** tant que RP-1 est reporté (§6).
- **Des avertissements SmartScreen probables** sur les premières versions : la réputation se construit avec les
  téléchargements, et un certificat EV ne l'accorde plus d'emblée depuis 2024 (`EXTERNE S18, S24`).
- **Un lot de plus avant la Release V1** : la montée vers .NET 10 (DI-8).

### 8.3 Dépendances envers d'autres décisions

| Décision | Suppose | Source | État au 22/09/2026 |
|---|---|---|---|
| DI-4, DI-5 | un outil de migration séparé, `MMV.DatabaseManager` | ADR-PROD-DB-009 DP-1 (option MA-B2) ; REC-1, REC-6 | **ARRÊTÉE** — DP-1 = MA-B2. ADR-PROD-DB-009 reste PROPOSED |
| DI-5, **DI-3.4** | une version MMV unique, en SemVer, lisible à l'exécution | ADR-PROD-DB-009 DP-7 ; REC-4a | **ARRÊTÉE** — SemVer, source unique. Emplacement et règle d'incrément : P4-6B |
| DI-5 (outil présent sur les postes) | l'identifiant migrateur n'est stocké sur aucun poste client | ADR-PROD-DB-009 DP-5, DP-9 ; REC-5 | **ARRÊTÉE** — RL-3 ; l'identifiant est fourni au moment de la migration |
| DI-4 (filet de sécurité), DI-7 (garantie technique) | une garde de version au démarrage, qui bloque un binaire face à un schéma différent, **y compris en SQLite** (rétrogradage) | ADR-PROD-DB-009 H1, §6.1 | **ARRÊTÉE** — DP-4 = SB-3, blocage sans écriture ; **politique de compatibilité = fenêtre N-1** (DP-3). REC-4c (égalité stricte) est **rejetée** (RB-14) |
| **DI-3.4** | un **plancher de version** déclaré quelque part, et une règle pour le choisir | ADR-PROD-DB-009 DP-3 §5.2.3.5 | **ARRÊTÉE** pour le multi-poste — déclaré par le schéma, **calculé** par l'outil, règle **N-1**. Résidu mono-poste : P8 |
| **DI-4.5 (OI-13)** | une **tournée de postes bornée** : une seule release porteuse de schéma à la fois | ADR-PROD-DB-009 DP-3 §5.2.3.4, H15 | **ARRÊTÉE** — contrainte de procédure, à inscrire dans la documentation opérateur (P4-8, P8) |
| **DI-7** | une **sauvegarde vérifiée** avant toute migration serveur | ADR-PROD-DB-009 DP-10 ; O9 | **ARRÊTÉE** en principe ; outillage P4-9 |
| DI-4 | un écran de blocage E3 capable d'accueillir, en P8, l'action « Mettre à jour ce poste » | lot P4-6C | à venir |
| DI-7 en PostgreSQL | sauvegarde et restauration serveur | P4-9 ; [ADR-PROD-DB-002 O9](adr-prod-db-002-server-database-provider-selection.md) | à venir |
| DI-4 | une source de mise à jour configurable par poste, sans variable d'environnement | P4-8 (D-13) ; ND-10 | à venir |

**Sans garde de version, DI-7 n'est garantie que par procédure.** Aujourd'hui, un binaire plus ancien démarre sur
un schéma plus récent sans le détecter (U-1 d'ADR-PROD-DB-009). ~~Si ADR-PROD-DB-009 retient une autre autorité
de migration que l'outil séparé, DI-4 et DI-5 sont à réexaminer.~~ **Cette réserve est levée** : DP-1 retient
l'outil séparé (MA-B2). Elle redeviendrait active si l'acceptation d'ADR-PROD-DB-009 revenait sur DP-1 (§11).

**~~Une tension subsiste, et elle est la plus lourde de l'ensemble.~~ Cette tension est LEVÉE.** Elle opposait
l'ordre « base d'abord, postes ensuite » (DI-4) à une politique de compatibilité alors indécise. La revue du
22/09/2026 a tranché **Q-3 en faveur de la fenêtre N-1** (ADR-PROD-DB-009 DP-3) : une fois la base migrée, les
postes restés à la version précédente **travaillent normalement** (E3a). **Le magasin ne s'arrête plus pendant
la tournée.**

**Ce que la levée coûte, en échange.** La tension ne disparaît pas sans contrepartie, et la contrepartie est
**opérationnelle** :

| # | Contrepartie | Porté par |
|---|---|---|
| 1 | les migrations doivent suivre **« étendre → migrer → contracter »** — aucune suppression ni resserrement dans la release qui étend | ADR-PROD-DB-009 **H15** ; CONTRIBUTING.md (P4-6B) |
| 2 | **une seule release porteuse de schéma par tournée** : la tournée est désormais **bornée dans le temps** | **DI-4.5**, **OI-13** |
| 3 | un poste laissé en retard de **deux** crans est bloqué (E3b) — un arrêt, pas une corruption | documentation opérateur (P4-8, P8) |

**Aucune de ces contreparties n'est technique** : elles sont de discipline et de procédure. C'est précisément ce
qui les rend fragiles, et pourquoi ADR-PROD-DB-009 les inscrit en obligations prouvées par test (H14 à H17)
plutôt qu'en recommandations.

### 8.4 Relation avec les documents existants

- [ADR-PROD-DB-001 §8](adr-prod-db-001-multi-poste-database-strategy.md) : « Pas d'updater implémenté » est une
  non-décision du lot P3-0B. Cet ADR ne la contredit pas : il décide pour la Release V1.
- Analyse P4-6, HY-7 et REC-10 : P4 peut se clore sans updater. REC-10 prévoyait ce réexamen « si un updater
  apparaît » ; l'ordre « base d'abord » est conservé (DI-4). **REC-4c (égalité stricte) est en revanche écartée**
  par ADR-PROD-DB-009 RB-14.
- [Roadmap master P2J-10A, P2J-10B](../roadmap/MMV-master-professionalization-roadmap.md) : réalisés par le lot P8
  ([roadmap P5](P5-product-completion-roadmap.md)).
- [ARCHITECTURE.md](../../ARCHITECTURE.md), l. 973-993 (`PublishSingleFile` et script Inno Setup), et
  [SAAS_LICENSING.md](../SAAS_LICENSING.md), l. 1080 (« Squirrel.Windows ou Velopack »), deviennent obsolètes ou
  incomplets. Leur correction suivra l'acceptation ; cet ADR ne les modifie pas.

---

## 9. Impact d'implémentation

**Aucun fichier n'est modifié par cet ADR.** Les tableaux ci-dessous décrivent ce qu'un lot futur toucherait.

### 9.1 Zones concernées

| Zone | Nature probable | Lot | Conditionné par |
|---|---|---|---|
| `.github/workflows/` | job de release : publication autonome, empaquetage Velopack, signature, contrôle, publication | P8 | DI-1, DI-6, ND-1, ND-3 |
| publication de `MMV.DatabaseManager` | dans le même dossier que `MMV.App`, avant l'empaquetage | P8 | DI-5 ; ADR-PROD-DB-009 DP-1 |
| `src/MMV.App` | abstraction de mise à jour et son implémentation Velopack | P8 | DI-3 |
| [Program.cs:15-16](../../src/MMV.App/Program.cs#L15-L16) | point d'ancrage exigé par Velopack **en première instruction de `Main`**, avant Avalonia et avant toute base (`EXTERNE S4`) | P8 | DI-2 |
| [Directory.Build.props](../../Directory.Build.props), [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) | version MMV unique, en SemVer | P4-6B | ADR-PROD-DB-009 DP-7 |
| [SqliteDatabasePathResolver.cs:23](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs#L23) | **aucune modification attendue.** Le chemin de données reste ce qu'il est ; c'est le `packId` qui doit s'en écarter | P8 | DI-9, QD-10 |
| garde de version applicative | plancher de version et message dédié | P8, avec P4-6B | DI-3.4, QD-15 |
| [global.json](../../global.json), `TargetFramework`, paquets, CI | .NET 10 | lot à définir, avant P10 | DI-8 |
| écran de blocage E3 | action « Mettre à jour ce poste » | P4-6C (emplacement), P8 (action) | DI-4 |
| documentation opérateur | publication dans le flux local après `migrate` ; retour arrière SQLite et PostgreSQL | P4-8, P4-9, P8 | DI-4, DI-7 |
| [ARCHITECTURE.md](../../ARCHITECTURE.md), [SAAS_LICENSING.md](../SAAS_LICENSING.md) | alignement | après acceptation | §8.4 |

### 9.2 Obligations proposées (à confirmer à l'acceptation)

| # | Obligation | Décision | Lot | Bloquante |
|---|---|---|---|---|
| **OI-1** | Le job de release de la CI est le **seul** producteur des artefacts distribués | DI-1 | P8 | **OUI** |
| **OI-2** | La signature s'exécute dans ce job seulement. La clé reste dans un HSM ou un service équivalent ; aucune clé dans le dépôt, dans un secret ou sur un poste | DI-6 | P8 | **OUI** |
| **OI-3** | Vérification de signature **bloquante** dans le job de release, sur le périmètre fixé par ND-3 | DI-6 | P8 | **OUI** |
| **OI-4** | Velopack n'est référencé que par l'implémentation de l'abstraction et par le point d'ancrage de `Main`. Un test d'architecture le vérifie | DI-3 | P8 | **OUI** |
| **OI-5** | Aucune application de mise à jour sans décision humaine, ni pendant une saisie. Prouvé par test | DI-3 | P8 | **OUI** |
| **OI-6** | En multi-poste, la seule source de mise à jour est le flux local ; le canal de l'éditeur n'est jamais contacté. Prouvé par test | DI-4 | P8 | **OUI** |
| **OI-7** | Procédure opérateur : publication dans le flux local **après** que l'outil de la même release a amené la base à ses migrations | DI-4 | P4-8, P8 | **OUI** |
| **OI-8** | `MMV.App` et `MMV.DatabaseManager` dans le même paquet, à la même version, avec la même assembly de migrations. Vérifié par le job de release | DI-5 | P8 | **OUI** |
| **OI-9** | Procédures de retour arrière SQLite et PostgreSQL documentées, qui ramènent toujours binaires **et** base. L'updater n'expose aucun rétrogradage automatique | DI-7 | P8, P4-9 | **OUI** |
| **OI-10** | La Release V1 est construite sur .NET 10 | DI-8 | avant P10 | **OUI** |
| **OI-11** | **Aucune donnée métier sous le dossier d'installation.** Le `packId` ne vaut pas `ManageMyVision`. La désinstallation laisse le dossier de données **intact** — vérifié par SD-1, puis par un contrôle du job de release | DI-9 | P8 | **OUI** |
| **OI-12** | Un poste antérieur à la **version minimale supportée** est bloqué, avec un message distinct de celui du blocage par le schéma. Prouvé par test | DI-3.4 | P8 (avec P4-6B) | **OUI** |
| **OI-13** | **Une seule release porteuse de schéma par tournée de postes.** La procédure de publication dans le flux local **vérifie** que tous les postes ont atteint la version de la dernière release porteuse de schéma avant d'en publier une nouvelle. Une release sans migration n'est pas concernée | DI-4.5 ; ADR-PROD-DB-009 DP-3, H15 | P4-8 (procédure), P8 (contrôle) | **OUI** |

---

## 10. Preuves attendues avant acceptation

| # | Preuve | Critère de réussite | Couvre | Exigée pour |
|---|---|---|---|---|
| **SD-1** | Velopack, Avalonia 11 et publication autonome, sur Windows 10 et 11 propres : installer, mettre à jour, désinstaller | installation sans UAC ; mise à jour appliquée ; **`%LocalAppData%\ManageMyVision` intact après la désinstallation** ; `MMV.DatabaseManager` présent et exécutable dans le paquet | DI-1, DI-2, DI-5, ND-9 | **ACCEPTED** |
| **SD-2** | flux local sur un partage réseau en lecture seule, poste sans Internet, en groupe de travail | le poste détecte, télécharge et applique la release depuis le flux local ; la procédure de partage tient en une page | DI-4, ND-5 | **ACCEPTED** |
| **SD-3** | job CI → service de signature → empaquetage Velopack | `signtool verify /pa` réussit sur chaque PE, `Setup.exe` et `Update.exe` compris ; le comportement sur les binaires tiers déjà signés est établi ; **aucun secret stocké** | DI-6, ND-3 | **ACCEPTED** |
| **SD-5** | coupure pendant l'application d'une mise à jour ; disque plein | l'application redémarre sur une version complète, ancienne ou nouvelle | DI-2, DI-7 | **ACCEPTED** |
| SD-6 | release avec migration SQLite sur une base peuplée, puis retour arrière | sauvegarde créée, migration appliquée ; retour arrière rejoué par restauration et version précédente | DI-7 | première release munie de l'updater |
| SD-7 | réaction de Defender et de SmartScreen à une première release signée | constat consigné pour la fiche support | DI-6 | idem |
| SD-8 | restauration de Velopack : cache hors ligne alimenté, ou restauration en CI seulement | build local non cassé | DI-2 | idem |
| SD-9 | tailles du paquet complet et d'un delta ; effet de ReadyToRun sur le démarrage | chiffres consignés | ND-4 | idem |
| SD-4 | contrôle de l'éditeur avant application | un paquet modifié, cohérent avec un flux modifié, est refusé | RP-1 | **reportée** |

---

## 11. Conditions de réexamen

- ADR-PROD-DB-009 **revient sur DP-1** par amendement, et retient une autorité de migration autre que l'outil
  séparé ⇒ **DI-4, DI-5**. *(Condition désormais dormante : DP-1 est ACCEPTED.)*
- ADR-PROD-DB-009 **revient sur la fenêtre N-1** (DP-3) ⇒ **DI-3.4, DI-4.5, OI-13**. Un retour à l'égalité
  stricte rétablirait l'arrêt du magasin pendant la tournée (§8.3) ; un élargissement de la fenêtre lèverait
  OI-13 mais interdirait de contracter le schéma.
- La **discipline « étendre → migrer → contracter » s'avère non tenue** en pratique — une release contracte ce
  qu'utilise la précédente ⇒ **DI-4.5** est insuffisante, et la fenêtre doit être reconsidérée, non la
  procédure.
- SD-1, SD-3 ou SD-5 échoue, ou Velopack ne publie aucune version pendant 12 mois ⇒ **DI-2, DI-3**. L'analyse
  propose Inno Setup avec NetSparkle (ND-12) ; l'abstraction de DI-3 limite le coût de la bascule.
- SD-2 échoue, ou le partage s'avère impraticable chez les clients ⇒ **ND-5**. Même si les postes sont tous en
  ligne, le canal de l'éditeur épinglé (LF-4) ne peut revenir que par un **amendement** de DI-4.
- Premier client géré par une DSI, ou poste partagé par plusieurs comptes Windows ⇒ **RP-3, RP-4**.
- Velopack documente une vérification de l'éditeur sous Windows, ou un incident touche le canal ⇒ **RP-1**.
- Premier incident de procédure en multi-poste (release publiée avant sa migration) ⇒ **RP-2**.
- La montée vers .NET 10 prend du retard ⇒ DI-8 ne se négocie pas : **c'est la Release V1 qui recule**. Le
  périmètre, l'ordre et les critères GO / NO-GO de cette montée sont établis par le
  [plan d'exécution .NET 10](net10-migration-execution-plan.md) (lot `P4-NET10`).

---

## 12. Questions ouvertes

| # | Question | Bloque |
|---|---|---|
| **QD-1** | Quelle **entité juridique** publie MMV, et dans quel pays ? UE : Artifact Signing ; Maroc : OV en HSM cloud (`EXTERNE S18, S19`) | ND-2 ; **ACCEPTED** |
| QD-2 | Les binaires peuvent-ils être **téléchargeables publiquement** ? | ND-1 |
| QD-3 | Les postes multi-poste ont-ils accès à **Internet** ? | ND-5 ; §11 |
| QD-4 | Existe-t-il des postes mono-poste partagés par **plusieurs comptes Windows** ? Chaque compte a aujourd'hui sa propre base | RP-3 |
| QD-8 | L'application « à la fermeture » vaut-elle décision humaine au sens de DI-3, ou seule une action explicite compte-t-elle ? | ND-6 |
| QD-9 | Budget récurrent : signature, hébergement du canal | ND-1, ND-2, ND-11 |
| **QD-10** | Nom du paquet (`packId`) et de l'entrée « Programmes », conformes à ND-9 | **ACCEPTED** |
| QD-11 | Confirmer que P4 se clôt sans updater (HY-7) et que l'updater arrive en P8 | gouvernance |
| QD-12 | La garde de rétrogradage SQLite (REC-4c ; ADR-PROD-DB-009 H1) est-elle un préalable de la première release munie de l'updater ? L'analyse le recommande | ND-13 ; DI-7 |
| ~~QD-13~~ | ~~DI-4 et DI-5 valent-elles choix de l'outil séparé (MA-B2) pour DP-1 ?~~ | **LEVÉE** — ADR-PROD-DB-009 **DP-1 = MA-B2** tranche l'autorité de migration dans cet ADR-là ; DI-4 et DI-5 s'y accordent |
| ~~QD-14~~ | ~~Qui peut déclencher une migration de la base centrale, et publier dans le flux local ?~~ | **LEVÉE le 22/09/2026 — option (c), modèle mixte selon contrat.** Le **propriétaire du magasin** ou le **support MMV** peut déclencher une migration, selon le contrat de service. Le **rôle migrateur PostgreSQL reste obligatoire**, et **aucun utilisateur applicatif standard ne migre**. L'identité n'est **jamais câblée** dans le produit (ADR-PROD-DB-009 DP-9, RB-20) : l'identifiant migrateur est **fourni au moment de la migration**, jamais stocké sur un poste, et une **référence d'opérateur** est journalisée (DP-8). Le partage effectif du rôle est une **clause contractuelle**, portée par la procédure P4-8 |
| ~~QD-15~~ | ~~Version minimale supportée (DI-3.4) : où, par qui, selon quelle règle ?~~ | **LEVÉE pour le multi-poste** — déclarée **par le schéma**, **calculée** par `MMV.DatabaseManager`, règle **N-1** (ADR-PROD-DB-009 DP-3 §5.2.3.5). **Résidu ouvert, non bloquant** : le plancher en **mono-poste SQLite**, où le binaire et son schéma ne divergent jamais et où la question porte sur le canal de mise à jour (P8) ; la **durée de grâce**, la **politique commerciale** et la **fréquence obligatoire**, que DI-3.4 n'a jamais prétendu décider |

**Tranchées par les décisions validées** : QD-5 (Velopack comme première dépendance NuGet nouvelle) par DI-2 ;
QD-6 (.NET 10 avant la Release V1) par DI-8 ; QD-7 (contrôle de l'éditeur en V1) par son report en RP-1 ;
**QD-13** par ADR-PROD-DB-009 DP-1.

**Le principe de ND-9 n'est plus une question** : DI-9 le tranche. **QD-10 ne porte plus que sur le nom** du
`packId` et de l'entrée « Programmes ». Qu'il doive **différer de `ManageMyVision`** n'est plus discutable :
DI-9.4 l'impose.

---

## 13. Références

- [Analyse de stratégie d'installation et de mise à jour](../implementation/MMV-installer-update-strategy-analysis.md) :
  §5 à §12 ; sources externes S1 à S24 au §15
- [ADR-PROD-DB-009](ADR-PROD-DB-009.md) (**ACCEPTED**) : **DP-1 (MA-B2)**, **DP-3 (fenêtre N-1)**, DP-4,
  DP-5 (RL-3), DP-7, **DP-8**, **DP-9**, **DP-10**, H1, H14 … H17, U-1, U-7, **RB-14**, Q-6
- [Rapport d'acceptation P4-6A](../implementation/P4-6A-adr-acceptance-report.md)
- [Rapport de consolidation P4-6A](../implementation/P4-6-architecture-consolidation-report.md)
- [Plan d'exécution .NET 10](net10-migration-execution-plan.md) : lot `P4-NET10`, mise en œuvre de DI-8 / OI-10
- [Roadmap P4 — §P4-6, découpage P4-6A / P4-6B / P4-6C](P4-multi-poste-roadmap.md)
- [Analyse P4-6](../implementation/P4-6-architecture-decision-analysis.md) : REC-1, REC-4a,
  REC-4c *(écartée)*, REC-5, REC-6, REC-10, HY-7, HY-8, Q-A8
- [Roadmap P5](P5-product-completion-roadmap.md) : lots P8 et P10
- [ADR-PROD-DB-001 §8 — non-décisions](adr-prod-db-001-multi-poste-database-strategy.md)
- [ADR-PROD-DB-002 §15 — obligation O9](adr-prod-db-002-server-database-provider-selection.md)
- [ADR-PROD-DB-008 §2.4 — contrainte NuGet hors ligne](adr-prod-db-008-postgresql-integration-testing.md)
- [Frontières de couches §4.2](adr-application-boundaries.md)
- [Registre des risques — R-25](risk-register.md)
- [Audit phase 1 §4.7](phase-1-technical-audit.md)
- [Roadmap master — P2J-10A, P2J-10B](../roadmap/MMV-master-professionalization-roadmap.md)
- [ARCHITECTURE.md](../../ARCHITECTURE.md) · [SAAS_LICENSING.md](../SAAS_LICENSING.md)

---

**PROPOSED — revue d'acceptation P4-6A passée le 22 septembre 2026.** Les **neuf décisions du §4 sont validées
sur le fond** et complétées (DI-3.4, DI-4.5). **Treize obligations** (OI-1 … OI-13). Le statut reste PROPOSED
pour **deux raisons, aucune d'ordre architectural** :

1. les preuves **SD-1, SD-2, SD-3 et SD-5** ne sont **pas produites** — aucun spike n'a été exécuté ;
2. **QD-1** (entité juridique éditrice) et **QD-10** (nom du `packId`) sont des **faits extérieurs au dépôt**.

**Ce document ne passera à ACCEPTED que sur ces éléments**, et non sur une décision supplémentaire.
Toute dépendance envers ADR-PROD-DB-009 est désormais **satisfaite** (§8.3).
