# MMV — Stratégie d'installation et de mise à jour (extension P4-6)

> **Note d'enregistrement (22 septembre 2026).** Ce rapport est **enregistré au dépôt** par le commit documentaire
> de la [revue d'acceptation P4-6A](P4-6A-adr-acceptance-report.md). Les mentions « non commité », « aucun commit »
> ou « non suivi » ci-dessous décrivent l'état **au moment de sa rédaction** : elles restent exactes pour le lot
> qu'elles décrivent, qui n'a produit **aucun commit de code**.

> **Statut : ANALYSE D'ARCHITECTURE — AUCUNE IMPLÉMENTATION.** Aucun fichier `.cs`, test, projet, configuration,
> workflow CI, ADR ni roadmap n'est modifié. Ce rapport est le seul fichier créé. Il n'est pas commité.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · HEAD : **`ccbeb3259444d26264e20323058764dd59318e1c`**
> (= `origin/p4-multi-poste`) · Destinataire : architecte principal.
>
> **Rôle de ce document.** L'analyse P4-6 (`P4-6-architecture-decision-analysis.md`, non suivie) fixe **qui migre
> la base et quand** ; elle suppose qu'aucun updater n'existe (HY-7) et renvoie la distribution à plus tard. Ce
> rapport traite **comment MMV est installé, distribué, mis à jour, signé et remis en arrière**, en restant
> compatible avec les recommandations REC-1 à REC-10 de P4-6. Il recommande ; il ne décide pas.
>
> **Méthode.** Lecture du code à HEAD (`src/MMV.App`, `src/MMV.Infrastructure`, `.github/workflows/ci.yml`,
> `Directory.Build.props`, `global.json`), des ADR PROD-DB, de l'analyse P4-6, du registre des risques, des roadmaps
> P4, P5 et « master », de `docs/SAAS_LICENSING.md`. **Vérification en ligne le 22 septembre 2026** de l'état de
> maintenance, des licences et des règles de signature (sources au §15). **Aucun build, aucun test, aucun spike
> exécuté.**
>
> **Le dépôt réel prime toujours sur ce document. Les sources officielles priment sur les faits `EXTERNE`.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `CODE` | constaté par lecture du code à HEAD |
| `DOC` | énoncé d'un document du dépôt (ADR, roadmap, rapport) |
| `EXTERNE Sn` | fait extérieur au dépôt, **vérifié le 22/09/2026** dans la source Sn (§15) |
| `EXTERNE — NV` | fait extérieur connu, **non vérifié** pour ce rapport |
| `À PROUVER` | à établir par un spike (§12) avant toute acceptation |
| `HYPOTHÈSE` | supposition de ce rapport, listée au §2 |
| `UNKNOWN` | absent du dépôt et des sources consultées : rien n'est inventé |

**Identifiants propres à ce rapport** (choisis pour ne pas entrer en collision avec REC-n, HY-n, F-n, E1…E8, S-n,
RS-n et Q-An de l'analyse P4-6) : **RD-n** recommandations · **HD-n** hypothèses · **FD-n** faits · **EX-n**
exigences · **LF-n** options de flux multi-poste · **DU-n** règles données/désinstallation · **RB-n** niveaux de
retour arrière · **SD-n** spikes · **RKD-n** risques · **DT-n** dette · **QD-n** questions.

**Sens de « V1 » dans ce rapport.** V1 = la **Release V1 commerciale** (« P10 » de la roadmap P5), dont la mise à
jour applicative est le lot « P8 ». P4 peut se clore avec des postes mis à jour à la main (HY-7 de P4-6). V2 = la
première évolution après la Release V1.

---

## 0. Résumé exécutif

**L'architecture recommandée, en une phrase.** Un paquet unique, construit et signé **uniquement par la CI** avec
**Velopack**, installe MMV **par utilisateur, sans droits administrateur**, et le met à jour de façon
**semi-automatique** ; en multi-poste, les postes **ne suivent jamais l'éditeur directement** mais un **flux local
sur le serveur du magasin**, alimenté par l'opérateur **après** `MMV.DatabaseManager migrate`.

| # | Sujet | Recommandation V1 |
|---|---|---|
| **RD-1** | Format de livraison | `dotnet publish` **autonome** (self-contained) `win-x64`, en dossier : ni fichier unique, ni trimming. Produit **par la CI seulement** |
| **RD-2** | Installateur | **Velopack** : `Setup.exe` par utilisateur, sans UAC. **Pas de MSI en V1** |
| **RD-3** | Mise à jour | **Velopack `UpdateManager`, semi-automatique** : vérification au démarrage, téléchargement en arrière-plan, application sur décision de l'utilisateur ou à la fermeture ; jamais pendant une saisie |
| **RD-4** | Signature | **Authenticode** sur tous les binaires MMV et Velopack et sur l'installeur, horodatage RFC 3161, dans la CI. **Azure Artifact Signing** si l'entité éditrice est éligible, sinon **certificat OV en HSM cloud** |
| **RD-5** | Canal éditeur | **stockage objet HTTPS en UE**, lecture publique, écriture réservée au job de release (identité fédérée OIDC, aucun secret longue durée). Canaux `stable` et `pilote` |
| **RD-6** | Multi-poste | **flux local** (partage en lecture seule sur le serveur du magasin), **promu par l'opérateur après la migration**. Les postes multi-poste n'interrogent **jamais** le canal éditeur |
| **RD-7** | Intégrité | HTTPS + sommes de contrôle Velopack + **contrôle de l'éditeur Authenticode avant application** (à prouver, SD-4) + écriture du canal sous approbation humaine |
| **RD-8** | Retour arrière | l'unité de retour est **(binaires, base)**. Par défaut : correctif sous une version supérieure. Si une migration a eu lieu : sauvegarde de pré-migration **et** version précédente |
| **RD-9** | Serveur multi-poste | **pas d'installeur serveur MMV en V1** : PostgreSQL par l'installeur EDB (procédure P4-8), `MMV.DatabaseManager` par l'**archive portable de la même release** |
| **RD-10** | Préalables | `<Version>` unique (REC-4a) · garde de rétrogradage SQLite (REC-4c) · configuration par fichier (P4-8) · **.NET 10 avant la Release V1** |

**Pourquoi cette combinaison**

1. **Un seul outil** couvre installeur, updater, deltas et intégration de la signature. C'est la moindre quantité
   de code et d'outillage pour une petite équipe.
2. **Par utilisateur, sans UAC** : un opticien installe seul, et les mises à jour ne demandent jamais de mot de
   passe administrateur au comptoir.
3. **En multi-poste, la version d'un poste est dictée par la base, pas par l'éditeur.** Avec l'égalité stricte de
   REC-4c, un updater branché sur l'éditeur bloquerait **tous** les postes (E2) dès la publication d'une release
   qui contient une migration, jusqu'à l'intervention de l'opérateur. Le flux local conserve l'ordre « base
   d'abord » de REC-10.
4. **Pour un logiciel qui manipule des données de santé, le canal de mise à jour est la surface d'attaque la plus
   rentable** : il exécute du code sur tous les postes. D'où : CI seule, signature en HSM, écriture OIDC, contrôle
   de l'éditeur.
5. **Runtime embarqué et fin de support de .NET 8 (10 novembre 2026)** : les correctifs de sécurité du runtime ne
   parviennent aux clients **que par des releases MMV**. L'updater est une exigence de sécurité, pas un confort.

**Ce qu'elle coûte** : une dépendance NuGet nouvelle (la première depuis la contrainte d'ADR-008 §2.4) ; environ
120 USD par an de signature (Artifact Signing) ou 150 à 300 USD (OV) ; un stockage objet ; une étape manuelle de
l'opérateur à chaque release multi-poste ; une discipline de publication mensuelle des correctifs du runtime ; neuf
spikes avant acceptation.

```
INSTALLER & UPDATE ANALYSIS = COMPLETE — RECOMMENDATIONS ONLY — NOTHING IMPLEMENTED
V1 PACKAGING                = SELF-CONTAINED win-x64 FOLDER — RELEASE BUILT BY CI ONLY
V1 INSTALLER + UPDATER      = VELOPACK — PER-USER, NO UAC — SEMI-AUTOMATIC
V1 SIGNING                  = AUTHENTICODE ALL BINARIES + INSTALLER — ARTIFACT SIGNING OR OV CLOUD HSM
V1 VENDOR CHANNEL           = EU OBJECT STORAGE, HTTPS — WRITE BY CI RELEASE JOB (OIDC) ONLY
V1 MULTI-POSTE              = STORE-LOCAL FEED PROMOTED AFTER migrate — POSTES NEVER READ VENDOR FEED
V1 ROLLBACK                 = ROLL-FORWARD — BACKUP RESTORE + PREVIOUS VERSION IF A MIGRATION RAN
PLAN B                      = INNO SETUP + NETSPARKLE (Ed25519)
EXCLUDED FOR V1             = MSI · MSIX · CLICKONCE · SQUIRREL.WINDOWS · STORE · CUSTOM UPDATER
PROPOSED ADR                = ADR-APP-DISTRIBUTION-001 — PROPOSED
```

---

## 1. Périmètre

**Dans le périmètre** : les sept sujets du mandat (installation initiale, distribution, détection, téléchargement,
installation automatique ou semi-automatique, retour arrière, signature), pour le client `MMV.App` en mono-poste
SQLite et en multi-poste PostgreSQL, et pour la livraison de `MMV.DatabaseManager` (REC-6 de P4-6).

**Hors périmètre** (la contrainte transmise est indiquée au §10.4) :

- installation et configuration de PostgreSQL, rôles, secrets, `pg_hba.conf` : **P4-8** ;
- sauvegarde planifiée et restauration outillée : **P4-9** ;
- import SQLite vers PostgreSQL : **P4-7** ;
- licences et abonnement : brouillon `docs/SAAS_LICENSING.md`, non adopté (ADR-001 §8 : « Pas de licence
  implémentée ») ;
- la montée de version vers .NET 10 elle-même (Q-A8 de P4-6) : ce rapport en établit seulement la dépendance.

---

## 2. Hypothèses de travail

| # | Hypothèse | Fondement | Conséquence si elle est fausse |
|---|---|---|---|
| **HD-1** | Cible **Windows 10 et 11 x64** uniquement | `CODE` [app.manifest](../../src/MMV.App/app.manifest) (`supportedOS` Windows 10/11), `OutputType=WinExe` ; laboratoire P4 sous Windows | Avalonia est multiplateforme ; Velopack aussi (`EXTERNE S1`). Le choix reste valable, la signature macOS s'ajouterait |
| **HD-2** | Les postes des magasins ne sont **pas gérés par une DSI** (pas de GPO, d'Intune ni d'AppLocker), souvent une session Windows partagée par poste | `UNKNOWN`, cohérent avec la cible « petits opticiens » | MSI par machine et déploiement par GPO/Intune (V2-2) |
| **HD-3** | Un poste mono-poste accède à Internet au moins par intermittence ; un poste multi-poste **peut ne pas** y accéder | `UNKNOWN` | si tous les postes sont en ligne, l'option LF-4 (§9.2) devient praticable |
| **HD-4** | Les binaires peuvent être **téléchargeables publiquement** : l'usage sera contrôlé par une licence, pas par le secret du téléchargement | `DOC` `SAAS_LICENSING.md` (brouillon : clé de licence, `/updates/check`) | téléchargement authentifié (V2-5), source Velopack personnalisée |
| **HD-5** | L'opérateur technique de P4-6 (HY-2) fait aussi les releases multi-poste | `DOC` P4-6 HY-2, REC-10 | si l'opticien est seul : automatiser la promotion (V2-1) avant la Release V1 |
| **HD-6** | Une release toutes les quelques semaines, plus des correctifs de sécurité du runtime | aucune mesure | au-delà, les deltas (RD-3) et l'automatisation V2 deviennent prioritaires |
| **HD-7** | L'entité juridique qui publie MMV n'est **pas connue** : Belgique (UE) ou Maroc | `DOC` marchés BE et MA (audit phase 1) ; entité `UNKNOWN` | conditionne l'option de signature (§8.2, QD-1) |

---

## 3. Socle factuel

| # | Constat | Preuve |
|---|---|---|
| FD-1 | Application **Avalonia 11.2.8**, `net8.0`, `WinExe`, FluentAvalonia, CommunityToolkit.Mvvm | `CODE` [MMV.App.csproj](../../src/MMV.App/MMV.App.csproj) |
| FD-2 | `Main` est une expression unique qui démarre Avalonia. Aucun point d'ancrage d'installation | `CODE` [Program.cs:15-16](../../src/MMV.App/Program.cs#L15-L16) |
| FD-3 | **Aucune version MMV** : pas de `<Version>` ; « À propos » affiche `1.0.0` en dur | `CODE` [SettingsView.axaml:43](../../src/MMV.App/Views/SettingsView.axaml#L43) ; `DOC` P4-6 F-15 |
| FD-4 | **Aucun installeur, aucune signature, aucun updater, aucune vérification d'intégrité.** Risque **R-25, HIGH** | `DOC` [risk-register R-25](../architecture/risk-register.md) ; [audit phase 1 §4.7](../architecture/phase-1-technical-audit.md) |
| FD-5 | Base SQLite **par utilisateur Windows** : `%LocalAppData%\ManageMyVision\mmv.db`, surchargeable par `MMV_DATABASE_PATH`. Sauvegardes et journal de migration à côté du fichier | `CODE` [SqliteDatabasePathResolver.cs:17-23](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs#L17-L23), l. 86-90 ; P4-6 F-9, F-11 |
| FD-6 | Configuration **uniquement par variables d'environnement** (`MMV_DATABASE_PROVIDER`, `MMV_DATABASE_CONNECTION_STRING`, `MMV_DATABASE_PATH`) ; aucun `appsettings.json` | `CODE` [DatabaseProviderResolver.cs:33-39](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L33-L39) ; `DOC` roadmap P4 §P4-8 |
| FD-7 | SQLite : chaque poste sauvegarde puis migre sa base **au démarrage**. PostgreSQL (recommandé, non accepté) : migration **par l'outil seul**, **égalité stricte** migrations appliquées = migrations connues, **base d'abord puis postes** | `CODE` P4-6 F-9 ; `DOC` P4-6 REC-1, REC-4c, REC-10 |
| FD-8 | Aujourd'hui, une migration appliquée mais inconnue du binaire **n'est pas détectée** : un binaire plus ancien démarre sur un schéma plus récent | `CODE` P4-6 F-10 |
| FD-9 | CI : un job `windows-latest`, build **Debug**, tests, audit NuGet, contrôles de dérive. `permissions: contents: read`. Aucun `publish`, aucune release | `CODE` [ci.yml:18-19](../../.github/workflows/ci.yml#L18-L19), [l. 46](../../.github/workflows/ci.yml#L46) |
| FD-10 | Poste de développement : seule source NuGet = cache hors ligne. **Tout paquet nouveau est non restaurable localement** | `DOC` [ADR-008 §2.4](../architecture/adr-prod-db-008-postgresql-integration-testing.md) |
| FD-11 | **.NET 8 et .NET 9 : fin de support le 10 novembre 2026.** .NET 10 (LTS) : support jusqu'au 14 novembre 2028. Dernier correctif .NET 8 : 8.0.31 (8 septembre 2026). SDK du dépôt : 8.0.417 | `EXTERNE S22` ; `CODE` [global.json](../../global.json) |
| FD-12 | Marchés **Belgique et Maroc** ; données de santé (catégorie particulière RGPD côté BE, loi 09-08 côté MA) | `DOC` [audit phase 1 §4.7](../architecture/phase-1-technical-audit.md) |
| FD-13 | PostgreSQL s'installe sous Windows par l'**installeur hébergé par EDB** ; sa chaîne d'approvisionnement est un risque identifié (R7 d'ADR-002) | `DOC` [ADR-002](../architecture/adr-prod-db-002-server-database-provider-selection.md) l. 809, 941, 1059 |
| FD-14 | Planification existante : « P2J-10A Packaging et installation », « P2J-10B Mises à jour sécurisées » ; « P8 — Mise à jour applicative » avant « P10 — Release V1 » | `DOC` [roadmap master](../roadmap/MMV-master-professionalization-roadmap.md) ; roadmap P5 §15 (non suivie) |
| FD-15 | Pistes déjà écrites, **jamais décidées** : `PublishSingleFile` + script Inno Setup (ARCHITECTURE.md, l. 974-993) ; « Squirrel.Windows ou Velopack » (SAAS_LICENSING.md, l. 1080) | `DOC` |
| FD-16 | Analyse P4-6 : `MMV.DatabaseManager` est un **nouvel exécutable console** livré **dans la même release et à la même version** que `MMV.App` (HY-8) | `DOC` P4-6 REC-6, HY-8 |

---

## 4. Exigences dérivées

| # | Exigence | Origine |
|---|---|---|
| **EX-1** | Un opticien non technique installe MMV mono-poste **seul**, sans prérequis à installer | mandat §5 ; HD-2 |
| **EX-2** | Une mise à jour ne demande **pas** de droits administrateur au comptoir | HD-2 ; support limité |
| **EX-3** | Tous les exécutables et l'installeur sont **signés** (SmartScreen, antivirus, intégrité) | R-25 ; P2J-10A/B |
| **EX-4** | L'**authenticité** d'une mise à jour est vérifiable avant son application | données de santé (FD-12) ; P2J-10B |
| **EX-5** | Une mise à jour **n'interrompt jamais** une saisie : l'utilisateur ou l'opérateur choisit le moment | fiabilité ; P4-6 REC-10 |
| **EX-6** | En multi-poste, un poste n'installe **jamais** une version dont les migrations diffèrent de celles de la base ; un poste en retard peut **rattraper** la base | P4-6 REC-4c, REC-10 |
| **EX-7** | Ni une mise à jour ni une désinstallation ne **suppriment** les données (base, sauvegardes, journal) | FD-5 |
| **EX-8** | Un retour arrière **cohérent** binaires et base est défini | mandat §1.6 ; P4-6 U-7 (jamais de `Down()`) |
| **EX-9** | Les correctifs de sécurité du runtime .NET parviennent aux clients | FD-11 |
| **EX-10** | Les artefacts de release sont **reproductibles** et produits par la CI, jamais par un poste de développement | FD-9, FD-10 |
| **EX-11** | Le mécanisme tolère un magasin **sans Internet** ou avec un Internet instable | HD-3 |
| **EX-12** | Aucun composant existant n'est redéveloppé ; coût récurrent faible | mandat §5 |
| **EX-13** | Le support sait quelle version tourne sur chaque poste | P4-6 REC-4a, R9.2 |

---

## 5. Installateurs Windows

**Remarque préalable sur .NET 8 et Avalonia.** Un installateur classique copie le dossier produit par
`dotnet publish` ; il est **neutre** vis-à-vis du framework d'interface. Avalonia n'exige que la présence de ses
bibliothèques natives dans ce dossier. La vraie question est le **prérequis runtime** : en publication autonome
(RD-1), il n'y en a aucun ; en publication dépendante du framework, l'installeur doit amorcer le runtime .NET. Les
seuls outils sensibles au framework d'interface sont ClickOnce (outillage centré WinForms/WPF) et MSIX (projet
d'empaquetage dédié).

### 5.1 Comparaison

| Solution | Maturité, maintenance | Usage entreprise | Format | Silencieux | Mise à niveau | Retour arrière | Licence, coût |
|---|---|---|---|---|---|---|---|
| **WiX Toolset** (v6, v7) | très mature ; actif ; projets SDK .NET, paquets NuGet | standard de fait du MSI (`EXTERNE — NV`) | **MSI**, bundles EXE (Burn) | `msiexec /qn` | *major upgrade*, correctifs MSP | **transactionnel pendant l'installation** (natif MSI) ; aucun après | source libre ; binaires sous **Open Source Maintenance Fee** depuis v6 (5 avril 2025). v7 : acceptation explicite du CLUF, **redevance au-delà de 10 000 USD/an** de revenus tirés des projets qui l'utilisent, par parrainage GitHub (`EXTERNE S12`) |
| **Inno Setup** (6.x) | très mature (depuis 1997), actif | très répandu (`EXTERNE — NV`) | **EXE** | `/VERYSILENT /SUPPRESSMSGBOXES` (`EXTERNE — NV`) | même `AppId`, en place | annule ses changements si l'installation échoue (`EXTERNE — NV`) | licence libre permettant l'usage commercial ; **depuis 6.5.0, achat « demandé »** aux utilisateurs commerciaux (> 5 000 USD de revenus), non exigé par la licence ; perpétuelle, 2 ans de mises à jour ; prix non relevé (`EXTERNE S13`) |
| **Advanced Installer** | mature, commercial (Caphyon) | répandu (`EXTERNE — NV`) | MSI, EXE, MSIX | oui | oui ; **updater intégré** (`EXTERNE — NV`) | celui du MSI | abonnement annuel ; ordre de grandeur **~390 USD (Professional), ~1 370 USD (Enterprise)** chez un revendeur (`EXTERNE S15`, à confirmer). Projet au format propriétaire |
| **NSIS** (3.11, 8 mars 2025) | très mature, maintenance lente | répandu (`EXTERNE — NV`) | EXE | `/S` (`EXTERNE — NV`) | à scripter | **à scripter** | libre et gratuit (`EXTERNE S14`) |
| **MSIX** | mature, Microsoft | entreprise et Store | paquet MSIX | oui (PowerShell, `EXTERNE — NV`) | natif, différentiel par blocs (`EXTERNE S16`) | désinstallation propre | signature **obligatoire** ; nouveaux fichiers d'`AppData` **redirigés et supprimés à la désinstallation** pour une application virtualisée (`EXTERNE S16`) ; protocole `ms-appinstaller` **désactivé par défaut depuis le 28/12/2023** (`EXTERNE S17`) |
| **Velopack** (`Setup.exe`) | **1.0 le 26 mai 2026**, 1.2.x en juin–septembre 2026 ; MIT (`EXTERNE S1`) | récent (`EXTERNE — NV`) | EXE ; **MSI optionnel** généré par WiX 5 (`--msi`) (`EXTERNE S2`) | `À PROUVER` pour `Setup.exe` ; `msiexec` pour le MSI | **c'est un updater** (§6) | voir §6 et §9.7 | gratuit |
| **InstallShield** | mature, commercial (Revenera) | grands comptes | MSI, EXE | oui | oui | MSI | coûteux (`EXTERNE — NV`) : écarté d'emblée |

### 5.2 Lecture par solution

- **WiX** : la référence MSI, gratuite pour le code source, avec redevance pour une entreprise qui dépasse
  10 000 USD de revenus. Point fort : transactions, GPO, Intune, déploiement par une DSI. Points faibles pour MMV : 
  courbe d'apprentissage élevée ; **aucun updater** ; un MSI par machine impose l'élévation à **chaque** mise à
  jour, en contradiction avec EX-2. Pertinent pour un **installeur serveur** V2 (bundle Burn qui enchaîne PostgreSQL)
  ou pour une clientèle gérée.
- **Inno Setup** : l'installeur EXE le plus simple à maintenir, installation par utilisateur possible. Pas
  d'updater : il faut l'associer à NetSparkle ou équivalent. Le prix d'une licence commerciale est faible face au
  temps de développement (non relevé ici). **Base du plan B** (§11.5).
- **Advanced Installer** : le plus complet (MSI, MSIX, updater, prérequis, signature cloud), au prix d'un
  abonnement et d'un format de projet propriétaire. Défendable pour une équipe qui préfère une interface graphique
  et un support commercial ; **surdimensionné pour le client V1**, plausible pour l'installeur serveur V2.
- **NSIS** : gratuit et léger, mais tout est à scripter, retour arrière compris. N'apporte rien face à Inno Setup.
- **MSIX** : **écarté pour V1.** La redirection d'`AppData` menace directement la base mono-poste
  (`%LocalAppData%\ManageMyVision\mmv.db`, FD-5) : créée par une application empaquetée, elle serait placée dans un
  emplacement privé au paquet et **supprimée à la désinstallation** (`EXTERNE S16`). L'installation depuis le web
  passe par un protocole désactivé par défaut (`EXTERNE S17`). Réexamen possible avec le Store (§7).
- **Velopack** : installeur **et** updater, par utilisateur, sans élévation, avec MSI optionnel par utilisateur
  ou par machine (`EXTERNE S2`). Seul candidat qui satisfait EX-1 et EX-2 avec **un seul outil**.

### 5.3 Verdict installateur

**Velopack pour le client V1 (RD-2).** Aucun installateur MSI en V1 : il n'apporte rien sans DSI (HD-2) et
impose l'élévation. **WiX ou Advanced Installer** restent candidats pour un installeur serveur V2 (V2-3). **Inno
Setup** est le repli (plan B).

---

## 6. Frameworks de mise à jour

### 6.1 Comparaison

| Solution | Maintenance | .NET moderne | Plateformes | Signature, intégrité | Delta | Retour arrière | Complexité |
|---|---|---|---|---|---|---|---|
| **Squirrel.Windows** | README : **appel à mainteneurs** (#1470) ; dernière version ancienne (2.0.1, 2020, `EXTERNE — NV`) | outillage .NET Framework (`EXTERNE — NV`) | Windows | signature des binaires ; pas de vérification de l'éditeur | oui | anciens dossiers de version (`EXTERNE — NV`) | moyenne |
| **Velopack** | **actif** : 1.0 (mai 2026), 1.2.158 (21/09/2026) (`EXTERNE S1`) | `net8.0`, `net9.0`, `net10.0`, netstandard2.0 (`EXTERNE S1`) | Windows, macOS, Linux | signature **intégrée à l'empaquetage** (`signtool`, modèle, Artifact Signing) ; **sommes de contrôle** vérifiées (`ChecksumFailedException`) ; aucune vérification de l'éditeur documentée sous Windows (`EXTERNE S3, S4, S7`) | **oui**, repli automatique sur le paquet complet (`EXTERNE S4`) | **aucun automatique** : l'application automatique n'avance que vers une version supérieure ; rétrogradage sur appel explicite (`EXTERNE S4`) | faible |
| **NetSparkle** | **actif** : 3.1.0 (5 mai 2026) ; MIT (`EXTERNE S8`) | `net6.0`, netstandard2.0 : utilisable en .NET 8 (`EXTERNE S8`) | Windows, macOS, Linux | **flux et fichiers signés Ed25519**, indépendamment de TLS (`EXTERNE S8`) | non mentionné | celui de l'installeur lancé | moyenne : il **télécharge et lance un installeur** à fournir (Inno, MSI) |
| **ClickOnce** | maintenu dans Visual Studio (`EXTERNE — NV`) | .NET 5+ via l'outil *Publish* ; API `ApplicationDeployment` absente (variables d'environnement depuis .NET 7) ; pas de mise à jour en cours d'exécution (`EXTERNE S10`) | Windows | manifestes signés | non | version précédente conservée (`EXTERNE — NV`) | faible, mais **Avalonia non documenté** (`EXTERNE — NV`) |
| **MSIX + App Installer** | Microsoft | oui | Windows | signature obligatoire | par blocs (`EXTERNE S16`) | non | moyenne ; limites du §5.2 |
| **AutoUpdater.NET** | actif (`EXTERNE — NV`) | WinForms et WPF **seulement** | Windows | somme de contrôle (`EXTERNE — NV`) | non | non | faible, **incompatible Avalonia** |
| **Updater d'Advanced Installer** | commercial | oui | Windows | selon configuration | `EXTERNE — NV` | celui du MSI | liée à l'outil |
| **Updater maison** | à nous | — | — | à concevoir | à concevoir | à concevoir | **élevée**, contraire à EX-12 |

### 6.2 Lecture

- **Squirrel.Windows** est **écarté** : maintenance incertaine ; Velopack en reprend les concepts avec un outillage
  moderne.
- **ClickOnce** est **écarté** : pensé pour WinForms/WPF, API absente en .NET moderne, emplacement d'installation
  opaque, aucune personnalisation, ce qui compte en multi-poste.
- **AutoUpdater.NET** est **écarté** : pas d'interface Avalonia.
- **Updater maison** : **écarté**. C'est exactement la réinvention que le mandat interdit, et un mécanisme de
  sécurité critique (EX-4) qu'une petite équipe ne doit pas écrire.
- **NetSparkle** a le **meilleur modèle d'authenticité** (signature Ed25519 du flux) et une interface Avalonia
  prête. En contrepartie, il faut **deux outils** (updater et installeur), les téléchargements sont complets, et un
  installeur par machine redemande l'élévation. **Plan B.**
- **Velopack** couvre installeur, updater, deltas, canaux et sources locales ou HTTP. Sa faiblesse est
  l'authenticité : il vérifie des **sommes de contrôle lues dans le flux lui-même**. Un attaquant qui écrit dans le
  flux peut donc publier un paquet cohérent. RD-7 compense cette faiblesse (§8.4).

### 6.3 Constat structurant : le retour arrière d'un framework ne suffit pas à MMV

Tout framework ne sait revenir qu'en arrière **sur les binaires**. Or une release MMV peut contenir une migration.
Revenir aux anciens binaires sans restaurer la base produit, aujourd'hui, un ancien binaire qui tourne **sans le
savoir** sur un schéma plus récent (FD-8). Le critère « rollback » ne départage donc pas les frameworks : **le
retour arrière de MMV est un processus (binaires, base)**, défini au §9.7. Le framework est choisi pour la
**fiabilité de l'installation et de la mise à jour**.

---

## 7. Canaux de distribution et hébergement

### 7.1 Ce que « données sensibles » implique ici

1. **Le canal transporte du code, pas des données.** Aucune donnée patient ne transite par la mise à jour. Il faut
   pouvoir le **prouver** : aucune donnée métier dans les requêtes, aucune télémétrie en V1.
2. **Le risque principal est l'intégrité.** Une mise à jour compromise s'exécute sous le compte de l'utilisateur,
   qui a accès à la base SQLite locale ou à la chaîne de connexion `mmv_app`. Autrement dit : **toutes les données de
   santé de tous les clients**. Le contrôle d'**écriture** du canal prime sur la confidentialité des binaires.
3. **Les métadonnées existent.** Chaque vérification révèle l'adresse IP du magasin et sa version. Hébergement en
   **UE**, journaux d'accès à rétention courte, aucun identifiant client en V1. À inscrire dans P2H-8C.
4. **Un magasin hors ligne** doit rester fonctionnel : l'échec d'une vérification n'empêche jamais le démarrage.

### 7.2 Comparaison

| Canal | Statut | Accès client | Contrôle de l'écriture | Adéquation MMV | Verdict |
|---|---|---|---|---|---|
| **Visual Studio App Center** | **retiré le 31 mars 2025** ; seuls l'analytique et le diagnostic restent prolongés (`EXTERNE S23`) | — | — | aucune | **écarté** : n'existe plus |
| **GitHub Releases** | actif | dépôt public : **binaires publics** ; dépôt privé : **jeton à embarquer dans le client** (`EXTERNE S5`) ; 60 requêtes/heure/IP sans jeton (`EXTERNE S5`) | droits GitHub, jetons | source Velopack native. Un jeton distribué à chaque client est un secret divulgué ; un dépôt public dédié aux releases fonctionne | **acceptable** pour un dépôt public de releases ; **pas** avec un jeton embarqué |
| **Azure DevOps Artifacts** | actif | flux de paquets pour développeurs, **authentifiés** (NuGet, npm, Universal Packages) (`EXTERNE — NV`) | Entra ID | conçu pour les pipelines, pas pour des postes clients ; un PAT sur chaque poste | **écarté** comme canal client ; inutile comme stockage interne (les artefacts de la CI suffisent) |
| **Stockage objet en UE** (Azure Blob Storage, compatible S3), avec CDN éventuel | actif | HTTPS, lecture publique (HD-4) ; `SimpleWebSource` de Velopack (`EXTERNE S5`) | **identité fédérée OIDC du job de release**, sans secret stocké (`EXTERNE — NV`) | région UE, coût marginal, journaux d'accès maîtrisés | **retenu (RD-5)** |
| **Velopack Flow** | **bêta** ; déploiements progressifs, liens statiques, API (`EXTERNE S6`) | `VelopackFlowSource` (`EXTERNE S5`) | compte Flow | intéressant pour le déploiement progressif ; bêta, tarif non publié, hébergement tiers | **V2 à évaluer** (V2-4) |
| **Microsoft Store** | actif ; signature gratuite pour MSIX (`EXTERNE S18`) | Store | Partner Center | mises à jour poussées par le Store : **incompatible avec « base d'abord »** en multi-poste | **reporté** |
| **winget** | actif (`EXTERNE — NV`) | `winget upgrade` à l'initiative de l'utilisateur | manifestes publics | aucun contrôle de l'ordre base/postes | **reporté** |
| **Flux local du magasin** (partage sur le serveur) | — | `SimpleFileSource` : « apps that update from a network/USB share » (`EXTERNE S5`) | ACL NTFS : lecture seule pour les postes, écriture pour l'opérateur | **seul canal compatible avec REC-10** en multi-poste ; postes sans Internet | **retenu pour le multi-poste (RD-6)** |

---

## 8. Signature

### 8.1 Règles externes applicables en 2026

| Règle | Conséquence pour MMV | Preuve |
|---|---|---|
| Depuis **juin 2023**, la clé privée d'un certificat de signature de code doit être en **HSM** (FIPS 140-2 niveau 2 ou plus) | plus de fichier `.pfx` dans un dépôt ou un secret CI : jeton physique, HSM cloud ou service de signature | `EXTERNE S18, S21` |
| Tout certificat émis depuis le **1er mars 2026** a une validité maximale de **460 jours** (ballot CSC-31) | renouvellement annuel ; l'**horodatage RFC 3161** est indispensable pour que les signatures survivent au certificat | `EXTERNE S21` |
| **Depuis 2024, un certificat EV ne supprime plus d'emblée l'avertissement SmartScreen** | payer l'EV pour SmartScreen n'est plus justifié ; la réputation se construit avec les téléchargements, quel que soit le certificat | `EXTERNE S18, S24` |
| **Azure Artifact Signing** (ex-Trusted Signing) : **environ 9,99 USD/mois** ; organisations des **États-Unis, du Canada, de l'UE et du Royaume-Uni** ; particuliers des États-Unis et du Canada seulement ; pas d'EV ; HSM FIPS 140-3 niveau 3 ; abonnement Azure payant exigé | **éligible si l'entité éditrice est belge (UE)**, **non éligible si elle est marocaine** | `EXTERNE S18, S19` |
| Exigence d'ancienneté de trois ans pour une organisation : **informations contradictoires** (une réponse Microsoft d'août 2026 indique « no minimum org age restrictions ») | à confirmer au moment de la validation d'identité | `EXTERNE S20` |
| Certificat **OV** d'une autorité de certification : **150 à 300 USD/an**, clé en jeton ou en HSM cloud | seule voie hors des pays éligibles | `EXTERNE S18` |
| Velopack doit signer **pendant** l'empaquetage : `Update.exe` et `Setup.exe` sont signés à des étapes distinctes ; signer après coup invaliderait les sommes de contrôle du paquet. Signature « profonde » par défaut, désactivable (`--signDisableDeep`) | la signature s'intègre à `vpk pack`, pas à une étape ultérieure | `EXTERNE S3` |

### 8.2 Options

| Option | Coût indicatif | Intégration CI | Éligibilité | Verdict |
|---|---|---|---|---|
| **Azure Artifact Signing** | ~120 USD/an | GitHub Actions, SignTool, intégration Velopack (`EXTERNE S3, S19`) | organisation UE (HD-7) | **retenu si l'entité est éligible** |
| **OV en HSM cloud** (autorité reconnue) | 150 à 300 USD/an + service HSM éventuel | par `signTemplate` vers l'outil de l'autorité (`EXTERNE S3`) | mondiale | **retenu sinon** |
| OV sur jeton USB | 150 à 300 USD/an | **impossible sur un runner hébergé** : signature sur un poste | mondiale | écarté : casse EX-10 |
| EV | 400 USD/an et plus | idem OV | mondiale | écarté : aucun gain SmartScreen (`EXTERNE S18`) |
| Auto-signé | 0 | — | — | écarté : bloqué chez les clients |

### 8.3 Périmètre signé

- **Signés par MMV** : `MMV.App.exe`, `MMV.DatabaseManager.exe`, toutes les `MMV.*.dll` (dont
  `MMV.Infrastructure.PostgreSQL.Migrations.dll`), `Setup.exe` et `Update.exe` de Velopack, le MSI s'il existe
  (V2).
- **Binaires tiers** : conservent la signature de leur éditeur quand elle existe. Le comportement de la signature
  profonde de `vpk` vis-à-vis d'un binaire **déjà signé** est `À PROUVER` (SD-3).
- **Où** : **uniquement** dans le job de release de la CI, jamais sur un poste de développement (EX-10, FD-10).
  Horodatage systématique.
- **Contrôle** : `signtool verify /pa` sur chaque fichier PE du dossier publié, en échec bloquant du job.

### 8.4 Authenticité des mises à jour (EX-4)

Velopack garantit que le paquet téléchargé est **celui que décrit le flux** (sommes de contrôle, `EXTERNE S4`),
pas qu'il vient **de l'éditeur**. RD-7 empile quatre barrières :

| # | Barrière | Protège contre |
|---|---|---|
| 1 | HTTPS de bout en bout | interception réseau |
| 2 | Écriture du canal réservée au job de release (OIDC), environnement `release` sous approbation humaine, tags protégés | vol d'un secret de publication, publication hors processus |
| 3 | **Contrôle de l'éditeur avant application** : chaque fichier PE du paquet téléchargé porte une signature Authenticode valide, et les fichiers `MMV.*` sont signés par **l'identité MMV attendue**. Sinon, pas d'application, paquet supprimé, journalisé | écriture illégitime dans le canal **sans** la clé de signature (clé en HSM, §8.1) |
| 4 | Multi-poste : promotion **humaine** dans le flux local (RD-6) | propagation automatique d'une release compromise aux postes |

La barrière 3 est du **code MMV** : environ une vérification `WinVerifyTrust` et une comparaison de sujet, placées
entre `DownloadUpdatesAsync` et `ApplyUpdates…`. Son coût réel (format et emplacement du paquet, paquet
reconstruit depuis un delta) est `À PROUVER` (SD-4). Si elle est trop coûteuse en V1, elle passe en V2 et le risque
résiduel est accepté explicitement (QD-7). NetSparkle offre l'équivalent nativement par Ed25519 (`EXTERNE S8`).
C'est un critère de bascule vers le plan B.

---

## 9. Contraintes propres à MMV

### 9.1 Couplage mise à jour ↔ migration

| Cas | Mono-poste SQLite | Multi-poste PostgreSQL |
|---|---|---|
| Release **sans** migration | le poste se met à jour quand l'utilisateur accepte ; la base n'est pas touchée | tout poste peut se mettre à jour à tout moment : A = C reste vrai (REC-4c) |
| Release **avec** migration | au premier démarrage : sauvegarde du fichier puis `Migrate()` (FD-7). Automatique et sauvegardé | la base doit être migrée **d'abord**, par l'outil. Un poste mis à jour **avant** la base est bloqué (E2). Un poste **non** mis à jour **après** la base est bloqué (E3) |
| **Conséquence pour l'updater** | le canal éditeur peut être suivi **directement** | le canal éditeur **ne doit pas** être suivi directement : chaque poste passerait en E2 dès la publication, **tout le magasin serait arrêté** jusqu'à l'intervention de l'opérateur |

**Règle qui en découle (RD-6).** En multi-poste, **la version cible d'un poste est celle de la base**, et c'est
l'opérateur qui la fixe en migrant. Les postes suivent un flux qu'il alimente **après** `migrate`. L'ordre « base
d'abord » de REC-10 est conservé.

### 9.2 Options de flux pour le multi-poste

| # | Option | Principe | Avantages | Inconvénients |
|---|---|---|---|---|
| LF-0 | Pas d'updater sur les postes (HY-7 de P4-6) | `Setup.exe` relancé à la main sur chaque poste | aucun mécanisme | un passage par poste ; un oubli laisse un poste bloqué en E3 |
| **LF-1** | **Flux local sur partage du serveur** | l'opérateur copie les fichiers de la release dans un partage en lecture seule, **après** `migrate` ; les postes utilisent `SimpleFileSource` | natif Velopack (`EXTERNE S5`) ; postes **sans Internet** ; ordre garanti par la procédure ; même mécanisme qu'en mono-poste | partage à configurer en groupe de travail (comptes, droits), `À PROUVER` (SD-2) ; étape manuelle |
| LF-2 | Flux HTTP local | serveur web sur la machine serveur | source native (`SimpleWebSource`) | un service web de plus à installer, sécuriser et maintenir ; TLS local difficile |
| LF-3 | Flux transporté par PostgreSQL | paquets stockés en base, source Velopack personnalisée | canal déjà authentifié (`mmv_app`) | **table nouvelle sur deux chaînes**, base alourdie, code sur mesure : contraire à la sobriété de P4-6 (aucun changement de modèle) |
| LF-4 | Canal éditeur **épinglé** sur une version cible | l'outil publie la version cible au moment de `migrate` ; chaque poste installe **exactement** cette version depuis le canal éditeur | pas de partage ; automatisable | Internet sur chaque poste (HD-3) ; source personnalisée ; emplacement de la version cible à définir sans changer le modèle |

**Recommandation : LF-1 en V1**, sous réserve de SD-2, avec **repli LF-0** si le partage s'avère impraticable chez
les clients. **V2-1** automatise LF-1 : une commande `MMV.DatabaseManager publish-update` copie la release
**seulement si** la base contient exactement les migrations que connaît l'outil. L'outil de la release N publie la
release N seulement si la base est à N : l'ordre devient une **garde technique**, comme D-B4 dans P4-6. **LF-4**
reste l'alternative V2 si HD-3 se confirme (postes en ligne) et que le partage pose problème.

**Comportement côté poste (V1, P8).** Au démarrage en multi-poste, le poste interroge **uniquement** le flux local :

- **E3** (poste en retard sur la base) : l'écran de blocage de P4-6C propose « Mettre à jour ce poste ». C'est la
  seule issue ; elle est immédiate.
- **E1** avec une version plus récente dans le flux : notification, application sur décision (RD-3).
- **Flux injoignable** : aucun effet sur le démarrage ; journalisé.
- **Filet de sécurité** : si l'opérateur copie une release **avant** de migrer, les postes qui l'installent
  passent en **E2**, bloqués sans écrire. L'erreur de procédure provoque un arrêt, **jamais une corruption**.

### 9.3 Données et désinstallation

| # | Règle | Motif |
|---|---|---|
| **DU-1** | Le dossier d'installation Velopack, `%LocalAppData%\{packId}` (`EXTERNE S2`), **ne doit jamais** être le dossier de données `%LocalAppData%\ManageMyVision` (FD-5). Le `packId` **ne doit pas** valoir `ManageMyVision` | le dossier d'installation est remplacé ou supprimé par l'updater ; la base mono-poste y serait perdue. Effet exact de la désinstallation : `À PROUVER` (SD-1) |
| DU-2 | Aucune donnée sous le dossier d'installation : base, sauvegardes, journal, configuration, journaux | le dossier `current` est **entièrement remplacé** à chaque mise à jour (`EXTERNE S2`) |
| DU-3 | La désinstallation **conserve** les données ; leur effacement est une procédure distincte et documentée (RGPD, P2H-8C) | EX-7 |
| DU-4 | MSIX exclu tant que la base vit sous `AppData` | redirection et suppression (§5.2) |
| DU-5 | Installation **par utilisateur Windows**. **Donnée existante, non introduite ici** : la base mono-poste est **déjà** par utilisateur (FD-5). Deux comptes Windows sur un même poste mono-poste ont donc deux bases | QD-4. Une installation par machine ne corrigerait pas ce point |

### 9.4 Configuration

La configuration actuelle passe **entièrement** par des variables d'environnement (FD-6). Un installeur par
utilisateur ne peut pas proprement poser une chaîne de connexion avec mot de passe dans l'environnement, et il ne
le doit pas. **Contrainte transmise à P4-8 (D-13)** : une configuration **par poste, dans un fichier**, hors du
dossier d'installation, contenant au minimum le mode (mono ou multi), l'adresse du serveur, et **la source de mise à
jour** (canal éditeur en mono-poste, chemin du flux local en multi-poste). Le paquet est **identique pour tous les
clients** : il ne porte aucune configuration.

### 9.5 Runtime

| Critère | **Autonome (RD-1)** | Dépendant du framework |
|---|---|---|
| Prérequis sur le poste | aucun | runtime .NET à installer et maintenir |
| Correctifs de sécurité du runtime | **livrés par MMV** (release de correctif) | Microsoft Update si le client l'a activé (`EXTERNE — NV`) ; version réelle non maîtrisée |
| Taille | plusieurs dizaines de Mo, compensée par les deltas (`EXTERNE S4`) | faible |
| Reproductibilité | totale | dépend du poste |

- **Pas de fichier unique** (piste de FD-15) : il extrairait les bibliothèques natives d'Avalonia et de SQLite vers
  un dossier temporaire à l'exécution, ce qui attire les antivirus et n'apporte rien à un installeur. **Pas de
  trimming** : EF Core et Avalonia reposent sur la réflexion (`EXTERNE — NV`). ReadyToRun : à mesurer (SD-9).
- **.NET 8 est hors support le 10 novembre 2026** (FD-11). Une Release V1 postérieure embarquerait un runtime sans
  correctifs. **Le passage à .NET 10 (LTS, novembre 2028) est un préalable de la Release V1** (RD-10), déjà ouvert
  par P4-6 (Q-A8). Velopack cible `net10.0` (`EXTERNE S1`).
- **Conséquence opérationnelle** : chaque correctif de sécurité du runtime qui touche MMV impose une release de
  correctif. Les correctifs .NET sont mensuels (`EXTERNE S22`, dates de correctifs).

### 9.6 Livraison de `MMV.DatabaseManager`

| Option | Garantie de même version (HY-8 de P4-6) | Remarque |
|---|---|---|
| **Dans le même paquet que `MMV.App`**, sans raccourci | **par construction** : mêmes fichiers, même version | présent sur les postes mais **inutilisable sans l'identifiant migrateur**, lui-même restreint à la boucle locale du serveur (REC-5 : la frontière est l'identifiant, pas le binaire) |
| Paquet distinct, même pipeline | par le pipeline | deux paquets, deux flux à maintenir |

**Recommandation : même paquet.** Sur le serveur, l'opérateur utilise l'**archive portable** de la même release,
extraite dans un dossier versionné. Aucune installation n'est nécessaire, et `migrate` s'exécute avec exactement
les migrations que les postes connaîtront. Existence et contenu de l'archive portable : `À PROUVER` (SD-1).

### 9.7 Retour arrière

| # | Situation | Mécanisme V1 | Qui |
|---|---|---|---|
| RB-1 | Échec pendant l'installation initiale | Velopack : `À PROUVER` (SD-5). MSI (V2) : transactionnel | — |
| RB-2 | Échec pendant l'application d'une mise à jour (coupure, disque plein) | le dossier `current` est remplacé (`EXTERNE S2`) ; l'état après un échec est `À PROUVER` (SD-5) | — |
| RB-3 | Release défectueuse **sans** migration | **correctif en avant** : republier l'ancien code sous un numéro supérieur (1.4.2 = code de 1.4.0). Aucun rétrogradage automatique (`EXTERNE S4`) | éditeur |
| RB-4 | Release défectueuse **avec** migration, **SQLite** | restaurer la **sauvegarde de pré-migration** (FD-5), puis réinstaller la version précédente. **Préalable : garde de rétrogradage SQLite (REC-4c)**, faute de quoi l'ancien binaire tournerait sur le nouveau schéma sans le voir (FD-8) | support, procédure documentée |
| RB-5 | Release défectueuse **avec** migration, **PostgreSQL** | REC-10 : `pg_restore` de l'archive de pré-migration (P4-9), puis version précédente sur tous les postes (flux local ou `Setup.exe`). Jamais de `Down()` | opérateur |

**Principe (RD-8)** : le retour arrière par défaut est **en avant**. Le rétrogradage est une **procédure de
support** couplée à la restauration de la base, jamais une fonction automatique de l'updater. Le rétrogradage
assisté depuis le flux local est une évolution V2 (V2-6).

---

## 10. Architecture proposée

### 10.1 Vue d'ensemble

```
ÉDITEUR — CI GitHub Actions (job de release, déclenché par un tag vX.Y.Z = <Version>)
  1. SHA identique à une CI verte ; build Release ; tests
  2. dotnet publish -c Release -r win-x64 --self-contained   (MMV.App + MMV.DatabaseManager)
  3. environnement « release » : approbation humaine
  4. vpk pack + signature Authenticode (OIDC → Artifact Signing, ou HSM cloud)
       → Setup.exe · archive portable · paquet complet + delta · fichier de flux (canal)
  5. signtool verify sur chaque PE ; publication sur le stockage objet UE (écriture : identité du job seule)
                                   │ HTTPS, lecture publique
            ┌──────────────────────┴───────────────────────┐
            ▼                                              ▼
MAGASIN MONO-POSTE (SQLite)                    MAGASIN MULTI-POSTE (PostgreSQL)
  MMV.App, installé par utilisateur              Serveur : PostgreSQL (installeur EDB, P4-8)
  démarrage → vérifie le canal éditeur             opérateur : 1. récupère la release N (archive portable + flux)
  → télécharge en arrière-plan                                 2. MMV.DatabaseManager status / migrate (sauvegarde)
  → contrôle de l'éditeur (RD-7)                               3. copie la release N dans le flux local
  → « Installer maintenant » / « À la fermeture »                 \\SERVEUR\MMV-MAJ (postes : lecture seule)
  → redémarrage : sauvegarde + migration SQLite  Postes : MMV.App → flux local UNIQUEMENT
                                                   E3 → « Mettre à jour ce poste » · E1 → notification
```

### 10.2 Composants

| Composant | Responsabilité | Outil | Lot |
|---|---|---|---|
| Pipeline de release | version, build, tests, publication autonome, signature, empaquetage, publication | GitHub Actions, `vpk`, service de signature | P8 |
| Canal éditeur | héberger les releases `stable` et `pilote` ; conserver **toutes** les versions publiées (RB-3 à RB-5, rattrapage E3) | stockage objet UE | P8 |
| Installeur client | installation par utilisateur, raccourcis, désinstallation qui conserve les données | Velopack `Setup.exe` | P8 |
| Service de mise à jour client | vérifier, télécharger, contrôler l'éditeur, proposer, appliquer | Velopack `UpdateManager` derrière une **interface MMV** (adaptateur dans `MMV.App`) | P8 |
| Point d'ancrage de démarrage | `VelopackApp.Build().Run()` **en première instruction** de `Main` (`EXTERNE S4`), avant Avalonia et avant toute base | `Program.cs` (FD-2) | P8 |
| Flux local | releases promues par l'opérateur | partage en lecture seule | P8, procédure P4-8 |
| `MMV.DatabaseManager` | migration (P4-6) ; V2 : `publish-update` | console .NET (REC-6) | P4-6B, V2 |

**Placement de l'adaptateur.** Le service de mise à jour vit dans `MMV.App`, comme les autres services
d'interface. Domain et Application n'en savent rien. Aucune référence Velopack hors de `MMV.App` et de
`Program.cs` : un changement d'updater (plan B) ne touche qu'un adaptateur.

### 10.3 Flux

| Flux | Déroulement |
|---|---|
| **Installation mono-poste** | lien de téléchargement → `Setup.exe` signé (avertissement SmartScreen possible sur les premières versions, RKD-6) → installation par utilisateur → premier lancement : création de la base SQLite (inchangé) |
| **Installation multi-poste** | provisioning (P4-8) → archive portable de la release N sur le serveur → `migrate` sur base vide (baseline, REC-1) → copie de la release N dans le flux local → sur chaque poste : `Setup.exe` depuis le partage, configuration (§9.4) → E1 |
| **Release sans migration** | mono : notification, application sur décision. Multi : copie dans le flux local **sans maintenance** ; les postes se mettent à jour à leur rythme et restent en E1 |
| **Release avec migration** | mono : idem, la migration SQLite suit au redémarrage. Multi : fenêtre annoncée → postes fermés → `migrate` → copie dans le flux local → postes démarrés en **E3** → « Mettre à jour ce poste » → E1 |
| **Correctif de sécurité du runtime** | release de correctif (`PATCH`), sans migration : flux « sans migration » |
| **Retour arrière** | §9.7 |

### 10.4 Contraintes transmises aux autres lots

| Lot | Ce que cette analyse exige |
|---|---|
| **P4-6B** | `<Version>` SemVer unique (REC-4a), lisible à l'exécution et reprise par `vpk` ; garde de rétrogradage SQLite (REC-4c) **avant** la première release munie d'un updater |
| **P4-6C** | l'écran de blocage E3 doit pouvoir accueillir, en P8, l'action « Mettre à jour ce poste » ; il ne doit dépendre ni du conteneur de services complet ni de la base |
| **P4-8** | configuration par fichier par poste, avec la source de mise à jour (§9.4) ; procédure de création du partage du flux local et de ses droits ; vérification de signature et de provenance de l'installeur EDB (FD-13) |
| **P4-9** | la restauration de l'archive de pré-migration fait partie du retour arrière RB-5 |
| **P2H-8C / P2H-8D** | rétention des journaux d'accès du canal ; aucune télémétrie V1 ; SBOM et provenance en V2 |

---

## 11. Décision recommandée

### 11.1 V1 (lot P8, avant la Release V1)

| # | Décision | Justification principale |
|---|---|---|
| RD-1 | publication autonome `win-x64` en dossier, par la CI | EX-1, EX-9, EX-10 ; §9.5 |
| RD-2 | installeur Velopack par utilisateur ; pas de MSI | EX-1, EX-2 ; §5.3 |
| RD-3 | mise à jour Velopack semi-automatique ; vérification **au démarrage** seulement (le code n'a aucun minuteur hors session, P4-6 F-17) ; jamais pendant une saisie | EX-5, EX-11 |
| RD-4 | Authenticode partout, horodaté, en CI ; Artifact Signing ou OV HSM cloud selon QD-1 | EX-3 ; §8 |
| RD-5 | canal éditeur sur stockage objet UE, écriture OIDC, canaux `stable` et `pilote` (tests terrain P9) | EX-4 ; §7 |
| RD-6 | multi-poste : flux local LF-1 promu après `migrate` ; repli LF-0 | EX-6 ; §9.1, §9.2 |
| RD-7 | quatre barrières d'authenticité, dont le contrôle de l'éditeur (SD-4, QD-7) | EX-4 ; §8.4 |
| RD-8 | retour arrière en avant ; restauration + version précédente si migration | EX-8 ; §9.7 |
| RD-9 | pas d'installeur serveur ; outil par l'archive portable du même paquet | EX-12 ; §9.6 |
| RD-10 | préalables : version, garde SQLite, configuration fichier, .NET 10 | §10.4, §9.5 |

**Avantages**

- Un outil libre (MIT), gratuit, sans format propriétaire ; le canal est un ensemble de **fichiers statiques** :
  aucun serveur applicatif à exploiter.
- Aucune élévation pour installer ni pour mettre à jour ; deltas.
- Signature intégrée à l'empaquetage ; la CI produit tout, le poste de développement rien.
- Même paquet pour tous les clients, en mono-poste et en multi-poste. Le passage de l'un à l'autre (P4-7) ne change
  que la configuration.
- Cohérent avec P4-6 : base d'abord, égalité stricte, opérateur unique, aucun changement de modèle EF.
- Choix **réversible** : quitter Velopack coûte un adaptateur et une dernière mise à jour.

### 11.2 V2

| # | Évolution | Déclencheur |
|---|---|---|
| V2-1 | `MMV.DatabaseManager publish-update` : promotion vérifiée (base = migrations de la release) et récupération vérifiée depuis le canal éditeur | premier incident de procédure, ou HD-5 infirmée |
| V2-2 | MSI par machine (`vpk --msi`, `EXTERNE S2`) | client géré par une DSI, poste multi-comptes, AppLocker |
| V2-3 | installeur serveur « MMV Serveur » (bundle WiX Burn ou Advanced Installer) : PostgreSQL, provisioning P4-8, outil, partage, sauvegarde planifiée P4-9 | volume d'installations multi-poste |
| V2-4 | déploiement progressif : anneaux par canal, ou Velopack Flow sorti de bêta (`EXTERNE S6`) | base installée significative |
| V2-5 | téléchargements liés à la licence | serveur de licences (brouillon SAAS_LICENSING), HD-4 infirmée |
| V2-6 | rétrogradage assisté depuis le flux local (appel explicite, `EXTERNE S4`) | premier RB-5 réel |
| V2-7 | vérification périodique en session | sessions de plusieurs jours |
| V2-8 | SBOM et attestation de provenance des releases | P2H-8D |
| V2-9 | télémétrie de version facultative pour le support | besoin du support, validation RGPD |
| V2-10 | LF-4 (canal éditeur épinglé) si les postes sont en ligne et que le partage est un frein | SD-2 difficile, HD-3 confirmée |

### 11.3 Reportés sans échéance

MSIX et Microsoft Store (redirection d'`AppData`, mises à jour hors de l'ordre « base d'abord ») · winget ·
ClickOnce · Squirrel.Windows · macOS et Linux · mise à jour silencieuse **forcée** · updater maison · InstallShield ·
Azure DevOps Artifacts comme canal client · correctifs MSI (MSP).

### 11.4 Risques

| # | Risque | Effet | Gravité | Traitement |
|---|---|---|---|---|
| RKD-1 | écriture illégitime dans le canal éditeur | code malveillant sur tous les postes, accès aux données de santé | **critique** | OIDC sans secret longue durée ; écriture réservée au job ; approbation ; journal d'accès ; contrôle de l'éditeur (RD-7) ; promotion humaine en multi-poste |
| RKD-2 | compromission de l'identité de signature | binaire malveillant signé MMV | **critique** | clé en HSM ; signature depuis la CI seulement ; historique de signature (`EXTERNE S19`) ; procédure de révocation |
| RKD-3 | données supprimées à la désinstallation | perte de la base mono-poste | **critique** | DU-1 à DU-4 ; test SD-1 |
| RKD-4 | postes multi-poste mis à jour avant la base | magasin arrêté (E2) | élevé | RD-6 ; V2-1 |
| RKD-5 | rétrogradage SQLite sans garde | ancien binaire sur schéma récent (FD-8) | élevé | REC-4c avant la première release à updater ; pas de rétrogradage automatique |
| RKD-6 | avertissements SmartScreen ou antivirus sur les premières versions | abandons d'installation, appels au support | moyen | signature, identité constante pour cumuler la réputation (`EXTERNE S18`) ; fiche support ; soumission à Microsoft si nécessaire (`EXTERNE S19`) |
| RKD-7 | pérennité de Velopack (1.0 en mai 2026, petite équipe : `EXTERNE — NV`) | updater sans évolution | moyen | adaptateur ; canal en fichiers statiques ; plan B |
| RKD-8 | Release V1 sur .NET 8 | runtime sans correctifs après le 10/11/2026 | élevé | .NET 10 avant la Release V1 (QD-6) |
| RKD-9 | identité de signature indisponible ou tardive | release bloquée | moyen | lancer la validation d'identité tôt ; OV en HSM cloud en repli |
| RKD-10 | partage réseau difficile en groupe de travail | flux local inopérant | moyen | SD-2 ; repli LF-0 ; V2-10 |
| RKD-11 | politiques bloquant les exécutables sous `%LocalAppData%` | installation impossible chez un client géré | faible (HD-2) | V2-2 |
| RKD-12 | correctifs du runtime non suivis | failles connues embarquées | élevé | revue mensuelle des correctifs .NET ; version du SDK contrôlée par `global.json` |
| RKD-13 | taille des paquets, bande passante | mises à jour lentes | faible | deltas ; flux local ; mesure SD-9 |
| RKD-14 | poste de développement hors ligne (FD-10) | Velopack non restaurable localement | faible | release en CI uniquement ; alimenter le cache local |

### 11.5 Dette technique assumée et plan B

| # | Dette | Remboursement |
|---|---|---|
| DT-1 | promotion manuelle vers le flux local | V2-1 |
| DT-2 | pas de MSI pour les clients gérés | V2-2 |
| DT-3 | contrôle de l'éditeur écrit par MMV (s'il est retenu) | à maintenir ; supprimé si Velopack le fournit |
| DT-4 | une installation par compte Windows | V2-2 si besoin |
| DT-5 | ni déploiement progressif ni visibilité des versions installées | V2-4, V2-9 |
| DT-6 | partage configuré à la main | V2-3 |

**Plan B : Inno Setup (licence commerciale) + NetSparkle (interface Avalonia, flux signé Ed25519).** Critères de
bascule, un seul suffit :

1. SD-1, SD-3 ou SD-5 en échec ;
2. le contrôle de l'éditeur (SD-4) est jugé indispensable en V1 et irréalisable proprement avec Velopack ;
3. aucune version de Velopack pendant 12 mois (seuil proposé).

Coût du plan B : deux outils, des téléchargements complets, une élévation à chaque mise à jour en installation par
machine, et le flux multi-poste à reconstruire au-dessus de NetSparkle.

---

## 12. Preuves attendues avant acceptation

| # | Preuve | Critère de réussite |
|---|---|---|
| **SD-1** | Velopack + Avalonia 11 + publication autonome, sur Windows 10 et 11 propres : installer, mettre à jour, désinstaller | installation sans UAC ; mise à jour appliquée ; **`%LocalAppData%\ManageMyVision` intact après désinstallation** ; archive portable produite et exécutable |
| **SD-2** | flux local sur partage UNC en lecture seule, poste sans Internet, en groupe de travail | le poste détecte, télécharge et applique la release ; la procédure de partage tient en une page |
| **SD-3** | job GitHub Actions : OIDC → service de signature → `vpk pack` | `signtool verify /pa` réussit sur **chaque** PE, `Update.exe` et `Setup.exe` compris ; comportement sur les binaires tiers déjà signés établi ; aucun secret stocké |
| **SD-4** | contrôle de l'éditeur avant application | un paquet modifié mais cohérent avec un flux modifié est **refusé** ; coût en lignes et en maintenance estimé |
| **SD-5** | coupure pendant l'application d'une mise à jour, disque plein | l'application redémarre sur une version complète, ancienne ou nouvelle |
| **SD-6** | release avec migration SQLite sur une base peuplée | sauvegarde créée, migration appliquée ; RB-4 rejoué avec la garde REC-4c |
| **SD-7** | réaction de Defender et de SmartScreen à une première release signée | constat documenté pour la fiche support |
| **SD-8** | restauration de Velopack : cache hors ligne alimenté, ou CI seule | build local non cassé (FD-10) |
| **SD-9** | tailles : paquet complet, delta typique ; effet de ReadyToRun sur le démarrage | chiffres consignés, décision ReadyToRun |

---

## 13. Impact ADR

### 13.1 Identifiant proposé

**ADR-APP-DISTRIBUTION-001**, plutôt qu'ADR-PROD-DB-010 :

- la série **PROD-DB** traite la base de données ; la distribution n'en relève pas, même si elle en dépend ;
- `adr-candidates.md` contient déjà un « ADR-010 » (concurrency token) d'une autre série : un « 010 » de plus
  ajouterait une ambiguïté, déjà signalée par la note de numérotation d'ADR-PROD-DB-009 ;
- une série **APP-DISTRIBUTION** accueillera la suite (installeur serveur, licences liées au téléchargement).

Fichier proposé, **non créé** : `docs/architecture/adr-app-distribution-001-installation-and-updates.md`.

### 13.2 Projet d'ADR

> **ADR-APP-DISTRIBUTION-001 — Installation, distribution et mise à jour de MMV Desktop**
>
> **Statut proposé : PROPOSED.** Passage à ACCEPTED après SD-1, SD-2, SD-3 et SD-5 réussis, et réponses à QD-1
> (entité de signature) et QD-6 (.NET 10).
>
> **Contexte.** Aucun installeur, aucune signature, aucun updater (R-25, HIGH). Clients non techniques, données de
> santé, deux modes de base de données. En multi-poste, P4-6 impose « base d'abord » et l'égalité stricte des
> migrations. .NET 8 est hors support le 10 novembre 2026.
>
> **Décision.**
> 1. Publication autonome `win-x64` en dossier, produite par la CI seule.
> 2. Velopack comme installeur (par utilisateur) et comme updater (semi-automatique, jamais pendant une saisie).
> 3. Signature Authenticode, horodatée, de tous les binaires MMV et Velopack et de l'installeur, pendant
>    l'empaquetage, par Azure Artifact Signing ou par un certificat OV en HSM cloud.
> 4. Canal éditeur sur stockage objet en UE, écriture réservée au job de release par identité fédérée.
> 5. En multi-poste, les postes suivent **un flux local promu par l'opérateur après la migration**, jamais le
>    canal éditeur.
> 6. Contrôle de l'éditeur avant application d'une mise à jour, sous réserve de SD-4.
> 7. Retour arrière en avant ; restauration de la base et version précédente quand une migration a eu lieu.
> 8. `MMV.DatabaseManager` livré dans le même paquet ; archive portable sur le serveur.
> 9. Le dossier d'installation n'est jamais le dossier de données ; la désinstallation conserve les données.
>
> **Options écartées.** MSI/WiX et Advanced Installer pour le client V1 (élévation, pas d'updater, coût) ; MSIX
> (redirection et suppression d'`AppData`) ; ClickOnce (inadapté à Avalonia et à .NET moderne) ;
> Squirrel.Windows (maintenance) ; AutoUpdater.NET (pas d'Avalonia) ; updater maison (réinvention, sécurité) ;
> App Center (retiré) ; Azure DevOps Artifacts (canal de développeurs) ; GitHub Releases privé (jeton dans le
> client) ; Store et winget (mises à jour hors de l'ordre « base d'abord »).
>
> **Conséquences positives.** R-25 traité ; installation autonome par l'opticien ; mises à jour sans élévation ;
> correctifs du runtime livrables ; ordre de P4-6 préservé ; un seul paquet pour tous les clients.
>
> **Conséquences négatives.** Dépendance nouvelle (Velopack) ; coût de signature récurrent ; étape manuelle de
> l'opérateur en multi-poste (V1) ; une installation par compte Windows ; releases de correctif du runtime à
> assurer ; code MMV de contrôle de l'éditeur si SD-4 le retient.
>
> **Relations.** Précise le réexamen prévu par ADR-PROD-DB-009 et l'analyse P4-6 (HY-7, REC-10 : « si un updater
> apparaît, la base reste migrée en premier »). Traite R-25. Réalise P2J-10A et P2J-10B (roadmap master) dans le lot
> P8 (roadmap P5). Transmet des contraintes à P4-6B, P4-6C, P4-8 et P4-9 (§10.4).
>
> **Réexamen si** : clientèle gérée par une DSI (V2-2) ; postes tous en ligne et partage impraticable (V2-10) ;
> critère de bascule du plan B atteint ; Velopack Flow ou un équivalent devient nécessaire au déploiement progressif.

### 13.3 Effet sur ADR-PROD-DB-009 et l'analyse P4-6

**Aucune contradiction.** HY-7 (« aucun updater ») décrit l'état **pendant P4**, et P4 peut se clore ainsi. Cet ADR
décrit ce que P8 livre **avant la Release V1**. REC-10 avait prévu ce réexamen, et l'ordre « base d'abord » est non
seulement conservé mais **outillé** (RD-6, V2-1). Deux recommandations de P4-6 deviennent des **préalables** de
cet ADR : REC-4a (version) et REC-4c (garde de rétrogradage SQLite).

---

## 14. Questions ouvertes pour l'architecte

| # | Question | Bloque |
|---|---|---|
| **QD-1** | Quelle **entité juridique** publie MMV, et dans quel pays ? UE : Artifact Signing ; Maroc : OV en HSM cloud | RD-4 ; ADR ACCEPTED |
| QD-2 | Les binaires peuvent-ils être **téléchargeables publiquement** (HD-4) ? | RD-5 |
| **QD-3** | Les postes des magasins ont-ils accès à **Internet** (HD-3) ? | LF-1 ou LF-4 ; mono-poste hors ligne |
| QD-4 | Existe-t-il des postes mono-poste **à plusieurs comptes Windows** ? Aujourd'hui, chaque compte a sa propre base (FD-5) | DU-5 ; V2-2 |
| **QD-5** | Accepter **Velopack** comme première dépendance NuGet nouvelle depuis ADR-008 §2.4 ? | RD-2, RD-3 |
| **QD-6** | Passer à **.NET 10 avant la Release V1** ? Rejoint Q-A8 de P4-6 | RD-10 ; ADR ACCEPTED |
| QD-7 | Contrôle de l'éditeur avant application (SD-4) : **V1 obligatoire**, ou V2 avec risque accepté ? | RD-7 ; critère 2 du plan B |
| QD-8 | Politique d'application par défaut : **à la fermeture**, ou **uniquement sur action explicite** ? | RD-3 |
| QD-9 | Budget récurrent : signature (~120 USD/an ou 150 à 300 USD/an), stockage, licence Inno Setup en plan B | RD-4, RD-5 |
| QD-10 | Nom du paquet (`packId`) et de l'entrée « Programmes » : règle DU-1 à respecter | SD-1 |
| QD-11 | Confirmer que **P4 se clôt avec HY-7** (postes mis à jour à la main) et que l'updater arrive en **P8** | gouvernance |

---

## 15. Sources externes (consultées le 22 septembre 2026)

| # | Source | Ce qu'elle établit |
|---|---|---|
| **S1** | NuGet, *Velopack* — <https://www.nuget.org/packages/Velopack> | 1.0.1 (26/05/2026), 1.2.0 (03/06/2026), 1.2.158 (21/09/2026) ; `net8.0`/`net9.0`/`net10.0`, netstandard2.0 ; MIT |
| **S2** | Velopack, *Installer* — <https://docs.velopack.io/packaging/installer> | installation dans `%LocalAppData%\{packId}` sans élévation ; MSI par WiX 5 (`--msi`), `PerUser` / `PerMachine` / `Either` ; dossier `current` entièrement remplacé |
| **S3** | Velopack, *Code Signing* — <https://docs.velopack.io/packaging/signing> | `--signParams`, `signTemplate`, Artifact Signing ; signature pendant l'empaquetage ; signature profonde, `--signDisableDeep` |
| **S4** | Velopack, *Integrating overview* — <https://docs.velopack.io/integrating/overview> | `VelopackApp.Build().Run()` en tête de `Main` ; `CheckForUpdatesAsync`, `DownloadUpdatesAsync`, `ApplyUpdatesAndRestart`, `WaitExitThenApplyUpdates` ; deltas et repli ; sommes de contrôle ; rétrogradage explicite seulement |
| **S5** | Velopack, *Update Sources* — <https://docs.velopack.io/integrating/update-sources> | `SimpleWebSource` (web, S3, Azure Blob), `SimpleFileSource` (répertoire, partage réseau), GitHub avec jeton, Flow ; 60 requêtes/heure/IP sans jeton |
| **S6** | Velopack, *Flow* — <https://docs.velopack.io/distributing/flow> | service hébergé en bêta ; déploiements progressifs |
| **S7** | Velopack, issue #975 — <https://github.com/velopack/velopack/issues/975> | la vérification de la signature d'une nouvelle version est discutée pour macOS ; rien de documenté pour Windows |
| **S8** | NuGet, *NetSparkleUpdater.SparkleUpdater* — <https://www.nuget.org/packages/NetSparkleUpdater.SparkleUpdater> ; *NetSparkleUpdater.UI.Avalonia* — <https://www.nuget.org/packages/NetSparkleUpdater.UI.Avalonia/> | 3.1.0 (05/05/2026) ; MIT ; signatures Ed25519 ; interface Avalonia |
| **S9** | GitHub, *Squirrel.Windows* — <https://github.com/Squirrel/Squirrel.Windows> | appel à mainteneurs (#1470) |
| **S10** | Microsoft Learn, *ClickOnce for .NET on Windows* — <https://learn.microsoft.com/en-us/visualstudio/deployment/clickonce-deployment-dotnet> | outil *Publish* ; `ApplicationDeployment` indisponible ; variables d'environnement depuis .NET 7 |
| **S11** | GitHub, *AutoUpdater.NET* — <https://github.com/ravibpatel/AutoUpdater.NET> | WinForms et WPF |
| **S12** | FireGiant, *Open Source Maintenance Fee* — <https://docs.firegiant.com/wix/osmf/> ; discussion #9239 — <https://github.com/orgs/wixtoolset/discussions/9239> | OSMF depuis WiX v6 (05/04/2025) ; v7 : CLUF explicite, seuil de 10 000 USD/an (annoncé le 05/02/2026) |
| **S13** | JR Software, *Inno Setup Commercial Licenses* — <https://jrsoftware.org/isorder.php> | achat demandé aux utilisateurs commerciaux depuis 6.5.0, non exigé par la licence ; perpétuelle, 2 ans de mises à jour |
| **S14** | SourceForge, *NSIS 3.11 released* — <https://sourceforge.net/p/nsis/news/2025/03/nsis-311-released/> | NSIS 3.11 le 08/03/2025 |
| **S15** | ComponentSource, *Advanced Installer* — <https://www.componentsource.com/product/advanced-installer-professional/prices> | prix revendeur indicatifs (Professional, Enterprise) |
| **S16** | Microsoft Learn, *Understanding how packaged desktop apps run on Windows* — <https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes> | redirection des nouveaux fichiers d'`AppData`, suppression à la désinstallation ; mises à jour par blocs |
| **S17** | MSRC, *Microsoft addresses App Installer abuse* — <https://www.microsoft.com/en-us/msrc/blog/2023/12/microsoft-addresses-app-installer-abuse> | `ms-appinstaller` désactivé par défaut (28/12/2023) |
| **S18** | Microsoft Learn, *Code signing options for Windows app developers* — <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options> | Artifact Signing ~9,99 USD/mois, pays éligibles ; OV 150–300 USD/an ; EV sans effet SmartScreen depuis 2024 ; HSM depuis juin 2023 |
| **S19** | Microsoft Learn, *Artifact Signing FAQ* — <https://learn.microsoft.com/en-us/azure/artifact-signing/faq> | pas d'EV ; FIPS 140-3 niveau 3 ; abonnement payant ; réputation progressive ; historique de signature |
| **S20** | Microsoft Q&A, question 5977141 — <https://learn.microsoft.com/en-us/answers/questions/5977141/azure-artifact-signing-trusted-signing-is-a-us-llc> | réponse Microsoft (17/08/2026) : pas d'âge minimal d'organisation ; information contradictoire en circulation |
| **S21** | DigiCert, *Understanding the new code-signing certificate validity change* — <https://www.digicert.com/blog/understanding-the-new-code-signing-certificate-validity-change> | 460 jours pour les certificats émis à partir du 01/03/2026 (CSC-31) |
| **S22** | Microsoft, *.NET support policy* — <https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core> | .NET 8 et 9 : fin le 10/11/2026 ; .NET 10 : 14/11/2028 ; 8.0.31 le 08/09/2026 |
| **S23** | Microsoft Learn, *App Center Retirement* — <https://learn.microsoft.com/en-us/appcenter/retirement> | retrait le 31/03/2025 |
| **S24** | Microsoft Learn, *SmartScreen reputation* — <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation> | réputation construite par l'historique de téléchargement |

---

## 16. Conclusion

```
MMV INSTALLER & UPDATE STRATEGY ANALYSIS = COMPLETE — RECOMMENDATIONS FOR ARCHITECT REVIEW
HEAD                                     = ccbeb3259444d26264e20323058764dd59318e1c (= origin/p4-multi-poste)
RD-1  PACKAGING                          = dotnet publish SELF-CONTAINED win-x64 — FOLDER — CI ONLY
RD-2  INSTALLER                          = VELOPACK Setup.exe — PER-USER — NO UAC — NO MSI IN V1
RD-3  UPDATES                            = VELOPACK UpdateManager — SEMI-AUTOMATIC — NEVER DURING INPUT
RD-4  SIGNING                            = AUTHENTICODE + RFC 3161 — ARTIFACT SIGNING (EU ENTITY) OR OV CLOUD HSM
RD-5  VENDOR CHANNEL                     = EU OBJECT STORAGE — HTTPS — WRITE BY CI RELEASE JOB (OIDC)
RD-6  MULTI-POSTE                        = STORE-LOCAL FEED PROMOTED AFTER migrate — FALLBACK: MANUAL SETUP
RD-7  AUTHENTICITY                       = HTTPS + CHECKSUMS + PUBLISHER CHECK (SD-4) + HUMAN-GATED WRITES
RD-8  ROLLBACK                           = ROLL-FORWARD — (BINARIES, DATABASE) AS ONE UNIT — NEVER Down()
RD-9  SERVER                             = NO MMV SERVER INSTALLER V1 — DatabaseManager FROM SAME PACKAGE
RD-10 PREREQUISITES                      = <Version> · SQLITE DOWNGRADE GUARD · FILE CONFIG (P4-8) · .NET 10
PLAN B                                   = INNO SETUP + NETSPARKLE (Ed25519)
PROPOSED ADR                             = ADR-APP-DISTRIBUTION-001 — PROPOSED
SPIKES BEFORE ACCEPTANCE                 = SD-1 · SD-2 · SD-3 · SD-5 (+ SD-4 PER QD-7)
KEY QUESTIONS                            = QD-1 SIGNING ENTITY · QD-3 INTERNET ON POSTES · QD-5 NEW NUGET · QD-6 .NET 10
CODE / TESTS / CI / ADR / ROADMAP        = UNCHANGED
COMMIT / PUSH                            = NONE — THIS REPORT IS UNCOMMITTED
```

READY FOR ARCHITECT REVIEW
