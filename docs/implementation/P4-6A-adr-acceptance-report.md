# Rapport d'acceptation P4-6A — clôture de la revue d'architecture

> **Nature : rapport de décision, strictement documentaire.**
> **AUCUN CODE MODIFIÉ.** Aucun fichier `.cs`, aucun `.csproj`, aucune migration EF, aucune CI, aucun paquet
> NuGet. Aucun test exécuté — cette phase n'avait pas à en exécuter.
>
> Date : 22 septembre 2026. Branche : `p4-multi-poste`. HEAD de référence :
> `ccbeb3259444d26264e20323058764dd59318e1c` (= `origin/p4-multi-poste`).
> Objet : enregistrer les **trois décisions de la revue de l'architecte**, en tirer toutes les conséquences dans
> les documents, et porter les ADR au statut que leur cohérence autorise.
> Remplace, comme état courant, le [rapport de consolidation P4-6A](P4-6-architecture-consolidation-report.md),
> qui reste au dépôt comme **historique**.
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Résultat en une page

| Document | Avant | Après |
|---|---|---|
| [ADR-PROD-DB-009](../architecture/ADR-PROD-DB-009.md) | PROPOSED — 8 décisions sur 10, trois conditions d'acceptation ouvertes | **ACCEPTED** — **10 décisions sur 10**, AC-1 / AC-2 / AC-3 satisfaites |
| [ADR-APP-DISTRIBUTION-001](../architecture/adr-app-distribution-001-installation-and-updates.md) | PROPOSED | **PROPOSED** — décisions validées et complétées ; **voir §5, c'est le point à lire** |
| [Roadmap P4](../architecture/P4-multi-poste-roadmap.md) | P4-6B bloquée par Q-3 | **P4-6B débloquée côté décisions** ; lot **`P4-NET10`** inscrit |
| [Roadmap P5](../architecture/P5-product-completion-roadmap.md) | — | prérequis .NET 10 de P10 rattaché à `P4-NET10` |
| [Plan .NET 10](../architecture/net10-migration-execution-plan.md) | inexistant | **créé** — Phase 2 |

**Ce qui change matériellement pour la suite du projet :** P4-6B n'a plus **aucune question d'architecture
ouverte**. Son seul préalable est **P4-5F vert** — un serveur PostgreSQL en CI. L'acceptation ne remplace pas
cette dépendance et ne la contourne pas.

---

## 2. Les trois décisions de la revue

### 2.1 Q-3 — fenêtre de compatibilité N-1 *(condition AC-1)*

**Décision : la compatibilité n'est pas une égalité stricte, mais une fenêtre d'un seul cran.** Inscrite en
[ADR-PROD-DB-009 DP-3](../architecture/ADR-PROD-DB-009.md), qui passe de *partiellement arrêtée* à **arrêtée**.

**C'est la décision la plus lourde de la revue**, et celle dont les conséquences se propagent le plus loin. Elle
mérite d'être comprise avant d'être appliquée.

#### Ce qu'elle dit

| # | Situation | Version du poste | Verdict |
|---|---|---|---|
| **C-1** | Appliquées = Connues | — | démarrage normal (**E1**) |
| **C-2** | le poste connaît des migrations **non appliquées** | — | **blocage strict** (**E2**) — « la base doit être mise à jour » |
| **C-3** | la base est **en avance** | **≥** minimum supporté | **démarrage normal** (**E3a**) |
| **C-4** | la base est **en avance** | **<** minimum supporté | **blocage** (**E3b**) — « ce poste doit être mis à jour » |
| **C-5** | chaînes **incomparables** | — | **blocage inconditionnel** (**E3c**) |

**E3a est le seul état nouveau**, et il porte tout le bénéfice : un poste resté à la version précédente
**travaille normalement** pendant la tournée de mise à jour. Il n'est ni dégradé, ni en lecture seule.

#### Ce qu'elle a fallu décider pour être cohérente

La décision de l'architecte énonçait la règle. Trois points en découlaient **mécaniquement**, et sont inscrits
dans l'ADR :

**(a) La fenêtre est asymétrique.** Elle tolère un poste **plus ancien** sur un schéma **plus récent** (C-3),
jamais l'inverse (C-2). Un binaire plus récent attend des colonnes qui n'existent pas encore : **aucune
discipline de rédaction ne peut rendre C-2 sûr.** L'ordre « base d'abord » de DI-4 existe précisément pour que
C-2 ne se produise pas.

**(b) « N-1 » se compte en releases porteuses de schéma**, non en numéros de version. Une release sans migration
**ne consomme aucun cran** : trois correctifs successifs sans DDL laissent la fenêtre intacte. Sur une base
neuve, le minimum vaut N — rien de plus ancien n'a jamais existé.

**(c) Le minimum supporté doit être porté par le schéma, et calculé — non déclaré.** C'est le point le plus
délicat, et il est traité au §3.

#### Ce qu'elle coûte

| # | Contrepartie | Nature |
|---|---|---|
| 1 | discipline **« étendre → migrer → contracter »** obligatoire : aucune suppression ni resserrement dans la release qui étend | règle de rédaction — CONTRIBUTING.md, obligation **H15** |
| 2 | **une seule release porteuse de schéma par tournée de postes** — la tournée est désormais **bornée dans le temps** | procédure — **DI-4.5**, **OI-13** |
| 3 | un poste laissé en retard de **deux** crans est bloqué (E3b) | arrêt, jamais corruption |

**Aucune de ces contreparties n'est technique.** Elles sont de discipline et de procédure — c'est ce qui les rend
fragiles. L'ADR les inscrit donc en **obligations prouvées par test** (H14 à H17), pas en recommandations.

#### Ce qu'elle rapporte

**La tension la plus lourde de tout le dossier disparaît.** Sous égalité stricte, l'ordre « base d'abord » de
DI-4 arrêtait le magasin **entre** la migration de la base et la mise à jour du **dernier** poste. Cette tension
était énoncée dans les deux ADR et dans la roadmap ; elle est **levée**, et les trois documents le disent
désormais.

### 2.2 Q-14 — journal de migration dédié *(condition AC-2)*

**Décision : « les deux », avec une division de rôles explicite.** Inscrite en **DP-8**, qui était le seul point
de décision resté **entièrement** ouvert.

| Support | Rôle |
|---|---|
| **table dédiée de la base centrale** | journal **autoritatif** ; hors modèle EF ; propriété du rôle migrateur ; écrite par `MMV.DatabaseManager` seul |
| **trace locale de l'outil** | seule capable d'enregistrer ce qui **n'a jamais atteint la base** : serveur injoignable, authentification refusée, sauvegarde absente ou non vérifiée |

**Un point de conception a dû être ajouté pour que la décision tienne : les écritures du journal sont hors de la
transaction de la migration** — une entrée à l'ouverture, une entrée de résultat à la fin. Sans cette séparation,
l'annulation de la transaction d'une migration en échec **effacerait la trace de son propre échec**, et l'étape 4
de la reprise après échec resterait sans preuve : exactement le manque que Q-14 devait combler.

**Champs minimaux**, conformes à la demande de la revue (version, date UTC, opérateur, résultat, référence
sauvegarde), précisés pour être implémentables :

| Champ | Précision apportée |
|---|---|
| version applicative | SemVer de l'outil (DP-7) ; **c'est la source du calcul du minimum supporté** |
| migrations appliquées | identifiants EF, état **avant → après** |
| date et heure | **UTC, horloge du serveur** (`now()`), à l'ouverture **et** à la clôture — [ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md) ; l'horloge d'un poste n'est pas fiable |
| opérateur | rôle PostgreSQL effectif (`current_user`) **et** référence d'opérateur fournie au lancement |
| résultat | succès ou échec, avec la cause |
| référence de sauvegarde | identifiant de la sauvegarde **vérifiée** (DP-10) |

Le **rôle applicatif ne lit pas le journal** : le journal est un instrument d'exploitation, pas une donnée
applicative.

### 2.3 QD-14 / Q-2 — modèle opérateur mixte *(condition AC-3)*

**Décision : option (c), modèle mixte selon contrat.** Le **propriétaire du magasin** ou le **support MMV** peut
déclencher une migration. Le **rôle migrateur PostgreSQL reste obligatoire**. **Aucun utilisateur applicatif
standard ne migre.** Inscrite en **DP-9.3** et en **QD-14**, désormais **levée**.

**Ce que le modèle mixte impose au produit**, et qui est la vraie conséquence : puisque les deux modèles doivent
être servis par le **même** binaire, **l'identité de l'opérateur n'est jamais câblée dans MMV** — ni dans le code,
ni dans la configuration livrée (alternative **RB-20**, rejetée). Trois conséquences :

1. l'identifiant migrateur est **fourni au moment de la migration**, jamais stocké sur un poste ;
2. l'outil exige au lancement une **référence d'opérateur**, journalisée (DP-8) — seule trace qui, après coup,
   distingue une migration faite par le magasin d'une migration faite par le support ;
3. le partage effectif du rôle migrateur est une **clause contractuelle**, portée par la procédure P4-8, qui doit
   rester compatible avec les deux modèles **et** avec un contrat qui change en cours de vie.

---

## 3. Le point qui a demandé un arbitrage de conception

**La décision « fenêtre N-1 » ne se suffisait pas à elle-même.** Elle exige que le poste sache si les migrations
qu'il ne connaît pas valent **un** cran ou **cinq** — et **rien dans `__EFMigrationsHistory` ne le dit**. Une
métadonnée était donc nécessaire, ce que l'ADR appelait VS-2 et **écartait** jusque-là, pour une raison précise :

> « La version minimale est choisie par le développeur : **une erreur de sa part autorise un poste ancien sur un
> schéma incompatible**, soit exactement le risque que O8 interdit. »
> — ADR-PROD-DB-009 §4.3, VS-2

**Retenir la fenêtre sans traiter cette objection aurait introduit le risque même qu'O8 interdit.** La solution
inscrite (**DP-3 §5.2.3.5**) la retire :

| Ce que VS-2 supposait | Ce qui est retenu |
|---|---|
| une table **dans le modèle EF** | une métadonnée **hors modèle EF** — ni règle de double migration, ni angle mort de dérive, ni table sans objet côté SQLite. Le précédent est `__EFMigrationsHistory` lui-même |
| tenue à jour **par les migrations** | écrite par **`MMV.DatabaseManager` seul**, dans la même opération verrouillée — rien à oublier dans un `Up()` |
| minimum **déclaré** par le développeur | minimum **calculé** par l'outil à partir du journal (DP-8) : la version de la dernière exécution qui a modifié le schéma. **Un constat, pas une déclaration** |

**Conséquence heureuse** : la conséquence « aucun changement de modèle EF », que l'ADR croyait devoir inverser si
la fenêtre était retenue, **tient**. Les deux chaînes de migrations restent inchangées et la règle de double
migration n'est pas sollicitée.

**Conséquence à porter** : la métadonnée et le journal **échappent aux contrôles de dérive** d'ADR-PROD-DB-007,
par construction. Atténuation inscrite : un test prouve leur **absence** du modèle EF (**H16**), et l'outil est
leur unique écrivain.

**Effet de bord favorable : QD-15 est tranchée pour le multi-poste** — le minimum est déclaré *par le schéma*,
calculé *par l'outil*, selon la règle *N-1*. Restent ouverts, sans bloquer : le plancher en **mono-poste** (où le
binaire et son schéma ne divergent jamais, la question portant sur le canal de mise à jour) et le volet
commercial, que DI-3.4 n'a jamais prétendu décider.

---

## 4. Ce qui a changé, document par document

### 4.1 ADR-PROD-DB-009 → **ACCEPTED**

| Zone | Changement |
|---|---|
| En-tête, §1 | statut **ACCEPTED** ; AC-1 / AC-2 / AC-3 **satisfaites** ; ce que l'acceptation autorise, et ce qu'elle ne fait pas |
| §4 | **conservé tel quel**, avec un bandeau de lecture : c'est le registre des options, non l'énoncé des décisions |
| §4.6 | **déroulés opérationnels mis à jour** — ce sont des procédures à suivre, non des options. Les conditionnels « si VS-2 » sont résolus ; l'étape 7 de la montée de version dit désormais que les postes N-1 continuent de travailler |
| §5.2 | **DP-3** réécrite (fenêtre, verdicts C-1 … C-5, définition de N-1, discipline, emplacement de la métadonnée) · **DP-4** révisée (E3a / E3b / E3c) · **DP-8** arrêtée · **DP-9** complétée (modèle mixte) · **DP-5** complétée (droits sur la métadonnée) |
| §5.4 | **quatre combinaisons incohérentes** ajoutées, dont « expand et contract dans la même release » |
| §6 | conséquences et risques réécrits : deux risques **levés**, **trois nouveaux** nommés (discipline de rédaction, tournée bornée, métadonnée hors modèle) |
| §7.1 | **RB-14 … RB-20** ajoutées — de treize à **vingt** alternatives rejetées |
| §8.2 | **H14 … H17** ajoutées — de treize à **dix-sept** obligations |
| §9 | Q-3, Q-14, Q-2 et QD-15 passent en **tranchées** ; **Q-21** et **Q-22** créées ; Q-18 clos sur son volet enregistrement |

### 4.2 ADR-APP-DISTRIBUTION-001 → reste **PROPOSED**, complétée

| Zone | Changement |
|---|---|
| §1 | conditions d'acceptation réévaluées ; **condition 3 satisfaite** (ADR-PROD-DB-009 ACCEPTED) |
| §2.2 | **REC-4c (égalité stricte) est rejetée** ; le bénéfice de la fenêtre est énoncé |
| **DI-3.4** | **complétée** d'un point 5 : emplacement et règle du plancher fixés pour le multi-poste. QD-15 levée |
| **DI-4** | **complétée** d'un point 5 : **une seule release porteuse de schéma par tournée** |
| §8.1, §8.2 | un bénéfice et un coût nouveaux |
| §8.3 | tableau de dépendances mis à jour ; **la tension la plus lourde est marquée LEVÉE**, avec ses trois contreparties |
| §9.2 | **OI-13** ajoutée — de douze à **treize** obligations |
| §11 | conditions de réexamen : DP-1 devient dormante ; **deux conditions nouvelles** sur la fenêtre et sur la discipline |
| §12 | **QD-14 levée**, **QD-15 levée** pour le multi-poste |

### 4.3 Roadmap P4

- **P4-6A** : `COMPLETE`, avec le statut réel de chaque ADR — et non un statut unique qui masquerait la
  différence ;
- **P4-6B** : `NEXT`, **plus aucune question d'architecture ouverte** ; contenu enrichi (métadonnée, journal,
  règle de rédaction) ; dépendance ramenée à **P4-5F vert** ;
- **`P4-NET10`** : lot **transverse** inscrit, `READY — NOT STARTED`, sans dépendance dans P4 ;
- bloc d'état : les trois questions passent en `ANSWERED`, `P4-6A BLOCKING QUESTIONS = NONE` ;
- le découpage P4-6A/B/C est **enregistré par commit** : résidu **R-1** clos.

### 4.4 Autres

- **Roadmap P5** : le prérequis .NET 10 de P10 est rattaché à `P4-NET10` ; P8 hérite des treize obligations ;
- **Rapport de consolidation** : bandeau « document historique », avec le tableau de ce que la revue a changé ;
- **Annotations « non suivi »** : supprimées des documents concernés — ce commit les enregistre tous, et les
  liens rompus deviennent des liens valides.

---

## 5. Le point à lire : pourquoi ADR-APP-DISTRIBUTION-001 reste PROPOSED

**La demande était de porter les ADR à ACCEPTED « uniquement après cohérence complète ».** La vérification de
cohérence donne deux réponses différentes, et il faut le dire clairement.

**ADR-PROD-DB-009 → ACCEPTED.** Ses trois conditions d'acceptation, qu'il énonçait lui-même, sont exactement les
trois questions que la revue a tranchées. Aucune décision ne manque.

**ADR-APP-DISTRIBUTION-001 → PROPOSED.** Son §1 subordonne son acceptation à deux choses qu'**aucune revue
d'architecture ne peut produire** :

| # | Condition posée par le document | État réel |
|---|---|---|
| 1 | preuves **SD-1** (installer / mettre à jour / désinstaller sur Windows propre), **SD-2** (flux local sur partage réseau), **SD-3** (signature en CI), **SD-5** (coupure pendant une mise à jour) | **aucun spike exécuté.** Ce sont des mesures, pas des décisions |
| 2 | **QD-1** — quelle entité juridique publie MMV, dans quel pays ? · **QD-10** — nom du `packId` | **faits extérieurs au dépôt.** QD-1 conditionne le type de certificat de signature ; QD-10 conditionne une décision dont une erreur **détruit des données de santé** (DI-9) |

**Le passer à ACCEPTED aurait fait dire au dépôt une chose fausse** — et l'aurait fait précisément sur le
document dont DI-9 rappelle qu'il est « la seule décision dont un mauvais choix détruit des données de santé ».
Les **neuf décisions de son §4 sont validées sur le fond** et n'attendent plus rien ; c'est son statut de
document qui attend des preuves.

**Ce que cela ne bloque pas.** P4-6B ne dépend pas de l'acceptation de cet ADR. Le lot `P4-NET10` non plus.
Seul **P8** en dépend, et P8 n'est pas dans P4.

---

## 6. Ce que P4-6B sait désormais

**Acquis, et non rediscutable sans amendement :** un outil séparé porte les migrations (DP-1) ; il porte le
verrou, le contrôle de sauvegarde, la métadonnée de compatibilité et le journal (DP-2, DP-10, DP-3, DP-8) ; le
poste se réduit à une **garde de version en lecture seule** plus un **écran de blocage à trois messages**
(DP-4) ; la compatibilité est une **fenêtre N-1** sous discipline « étendre → migrer → contracter » (DP-3) ; le
minimum supporté est **calculé**, jamais saisi (H17) ; trois rôles PostgreSQL (DP-5) ; une version unique en
SemVer (DP-7) ; la migration exige une **action explicite** d'un opérateur dont l'identité n'est pas câblée
(DP-9) ; **aucun changement du modèle EF**.

**Ce qui reste à P4-6B, et qui est de la mise en œuvre :** forme exacte du verrou (clé, portée, comportement du
pool Npgsql) · stratégie de maintenance parmi CX-1 … CX-5 (Q-9) · emplacement de la version applicative
(Q-5 résiduel) · noms et types des objets de métadonnée et de journal · revérification en cours de session
(Q-11) · comportement face à E7 (Q-12) · contrôle automatisé de la discipline de rédaction (**Q-21**) · base dont
le journal est absent ou incomplet (**Q-22**).

**Ce qui le bloque encore, et qui n'est pas une décision : P4-5F vert.** Les critères S-1 à S-4 exigent un
serveur PostgreSQL en CI.

---

## 7. Deux questions créées par la revue

| # | Question | Pourquoi elle naît maintenant |
|---|---|---|
| **Q-21** | Comment **contrôler automatiquement** le respect d'« étendre → migrer → contracter » ? | La fenêtre N-1 en dépend entièrement, et une **revue humaine est aujourd'hui le seul garde-fou**. Une migration qui contracte trop tôt casse la garantie **sans qu'aucun contrôle actuel ne le voie** |
| **Q-22** | Que fait l'outil face à une base dont le **journal est absent ou incomplet**, alors que l'historique EF porte plusieurs migrations ? | Le minimum supporté est **calculé à partir du journal** : sans journal, il n'est pas calculable. Cas d'une base migrée avant l'introduction du journal |

**Aucune des deux ne bloque le démarrage de P4-6B.** Q-21 pèse sur sa qualité, Q-22 sur sa robustesse.

---

## 8. Vérification — aucun code modifié

| Contrôle | Résultat |
|---|---|
| Fichiers `.cs` modifiés | **aucun** |
| Fichiers `.csproj` modifiés | **aucun** |
| Migrations EF modifiées ou créées | **aucune** |
| Fichiers de CI (`.github/`) modifiés | **aucun** |
| Paquets NuGet ajoutés, retirés ou mis à jour | **aucun** |
| [global.json](../../global.json), [Directory.Build.props](../../Directory.Build.props) | **non modifiés** |
| Tests exécutés | **aucun** — cette phase n'avait pas à en exécuter |

**La baseline de tests reste celle de P4-4C : 1569.** Elle est **citée**, jamais revendiquée par ce lot.

**Périmètre du commit documentaire** : `docs/` uniquement. Il enregistre les deux ADR, les deux roadmaps, le plan
.NET 10 et les rapports d'appui qui vivaient dans l'arbre de travail sans être suivis — ce qui rend valides les
liens que les ADR portent vers eux.

---

## 9. Ce qui reste à l'architecte

Par ordre de poids, et **aucun de ces points ne bloque P4-6B** :

1. **Arbitrer la séquence de `P4-NET10`.** Le [plan d'exécution](../architecture/net10-migration-execution-plan.md)
   recommande de l'exécuter **avant** P4-5F et P4-6B : la montée tranche **Q-6** par disponibilité — EF Core ≥ 9
   apporte un verrou de migration natif, que DP-2 autorise explicitement — et évite de migrer ensuite les projets
   que P4-5F et P4-6B vont créer. C'est une recommandation, pas une dépendance.
2. **Décider si la source NuGet du poste de développement est ouverte.** C'est le seul obstacle matériel à
   `P4-NET10`, et c'est une décision, pas une manipulation : voir le plan, critères GO / NO-GO.
3. **QD-1 et QD-10** — entité juridique éditrice, nom du `packId`. Ils bloquent l'acceptation
   d'ADR-APP-DISTRIBUTION-001, donc P8, pas P4.
4. **Exécuter SD-1, SD-2, SD-3, SD-5.** Quatre spikes, seul chemin vers l'acceptation de cet ADR.
5. **Inversion P4-4 ↔ P4-5** (constat A-2, Q-18) — la roadmap la constate depuis P4-5B et ne la tranche pas.
6. **Q-21** — comment contrôler la discipline de rédaction des migrations autrement que par revue humaine.

---

## 10. Références

- [ADR-PROD-DB-009](../architecture/ADR-PROD-DB-009.md) — **ACCEPTED** : DP-1 … DP-10, H1 … H17, RB-1 … RB-20
- [ADR-APP-DISTRIBUTION-001](../architecture/adr-app-distribution-001-installation-and-updates.md) — PROPOSED :
  DI-1 … DI-9, OI-1 … OI-13
- [Plan d'exécution .NET 10](../architecture/net10-migration-execution-plan.md) — lot `P4-NET10`
- [Roadmap P4 — §P4-6, §P4-NET10, §6](../architecture/P4-multi-poste-roadmap.md) ·
  [Roadmap P5 — §15](../architecture/P5-product-completion-roadmap.md)
- [Rapport de consolidation P4-6A](P4-6-architecture-consolidation-report.md) — **historique**
- [ADR-PROD-DB-002 §15 — O8, O9, O10, O11, O14](../architecture/adr-prod-db-002-server-database-provider-selection.md) ·
  [ADR-PROD-DB-004 — stratégie temporelle](../architecture/adr-prod-db-004-datetime-strategy.md) ·
  [ADR-PROD-DB-005 — migrations](../architecture/adr-prod-db-005-migration-architecture.md) ·
  [ADR-PROD-DB-007 — dérive](../architecture/adr-prod-db-007-schema-drift-prevention.md) ·
  [ADR-PROD-DB-008 — tests d'intégration](../architecture/adr-prod-db-008-postgresql-integration-testing.md)
- [Rapport de transition P4-6](P4-6-transition-audit-report.md) ·
  [Rapport RECON-B-light](P4-RECON-B-light-report.md) ·
  [Analyse P4-6](P4-6-architecture-decision-analysis.md) ·
  [Analyse installeur et mise à jour](MMV-installer-update-strategy-analysis.md)

---

**P4-6A — CLOSE.** ADR-PROD-DB-009 **ACCEPTED**. ADR-APP-DISTRIBUTION-001 **PROPOSED**, décisions validées,
preuves en attente. **P4-6B est spécifiable et écrivable ; son seul préalable est P4-5F.**
