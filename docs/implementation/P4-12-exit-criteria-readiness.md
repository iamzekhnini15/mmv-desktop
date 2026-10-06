# P4-12 — Audit final P4 : état des 18 critères de sortie

> **Pré-audit de clôture — verdict : `P4 = NOT CLOSED` · `V1 MULTI-POSTE = NOT GO`.**
> État au 06/10/2026, code audité : `p4-12-supplier-race` = `da311dd` (deux correctifs de tests instables, ci-dessous).
> **P4-11 est MISE EN ATTENTE (`DEFERRED / BLOCKED`)** par les contraintes du laboratoire Windows (horloge NTP des
> VM, licences d'évaluation). Aucune preuve Windows native ni multi-postes réelle n'existe dans ce document : les
> preuves « CI Linux » ne valent **pas** preuves Windows natives.

## 1. Constat de l'audit du 06/10/2026 et correction

**Constat.** La CI de `p4-multi-poste` sur `8a505ce` était **rouge** (run `37304770469`, job « PostgreSQL integration
(P4-5F) », 231/232) alors que le même SHA était vert sur `p4-10` (run `37303936998`) : test instable.
`MultiProcessTests.Supplier_deletion_racing_a_product_creation_never_leaves_an_orphan` a obtenu
`error:ConstraintViolation/created`, issue que son oracle n'acceptait pas.

**Cause (mesurée).** Sous PostgreSQL `READ COMMITTED`, `DELETE … WHERE NOT EXISTS (produits)` évalue la condition sur
l'instantané de l'instruction : un produit inséré par l'autre poste mais **pas encore validé** n'y est pas visible.
Le `DELETE` attend le verrou que l'insertion tient sur la ligne fournisseur (FK), puis, l'insertion validée, la FK
`Restrict` le rejette (23503). Intégrité intacte (rien supprimé, aucun orphelin) ; `DeleteSupplierUseCase` convertissait
déjà cette violation en refus métier stable — mais le test appelait le dépôt directement, et le code qualifiait ce
chemin de « normalement inatteignable ».

**Décision (aucune décision d'architecture nouvelle).** Conversion conservée au niveau du **cas d'usage** : le dépôt
ne peut pas absorber l'exception, PostgreSQL ayant déjà annulé la transaction englobante. Contrat du dépôt documenté :
deux formes du même refus (`false`, ou violation de contrainte classée `ConstraintViolation`).

**Correctif `7df0d01`** (aucun changement de comportement de production, aucune migration, aucun snapshot) :

- le scénario `delete-supplier` du poste passe par le **cas d'usage de production** (cas d'usage + `EfTransactionRunner`
  + dépôt) : issues `deleted` / `absent` / `refused` ;
- nouveau test **déterministe** `Supplier_deletion_waiting_on_an_uncommitted_product_is_refused_by_the_fk_net` :
  insertion tenue ouverte, `DELETE` observé bloqué sur son verrou (`pg_stat_activity`), puis validation ⇒
  `refused/created` exigé. **Contrôle négatif** : filet FK du cas d'usage désactivé, le test reproduit exactement
  l'échec CI (`error:ConstraintViolation/created`) ;
- les deux tests vérifient d'abord les invariants : fournisseur présent ⇔ produit conservé ; aucun produit orphelin
  dans toute la base ; FK `Products → Suppliers` toujours `Restrict` et validée ;
- tests unitaires du filet : `ConstraintViolation` ⇒ même refus métier ; autres catégories propagées telles quelles ;
- commentaires corrigés (`ISupplierRepository`, `SupplierRepository`, `DeleteSupplierUseCase`, rapport P4-10).

**Preuves locales de `7df0d01`.** Unitaires **2461/2461**, intégration PostgreSQL **233/233** (0 ignoré), tests
fournisseur **15/15** exécutions répétées, dérive EF SQLite et PostgreSQL nulle, 0 paquet vulnérable,
`git diff --check` propre.

### Second défaut révélé par la CI de `7df0d01`

La CI **`37459291267`** sur `7df0d01` est **rouge** pour une autre raison, sans lien avec le correctif : un test P4-7
(`SqliteImportTests.Nonexistent_local_time_rejects_the_source_before_any_server_write`) voit sa source refusée car
« `mmv-source.db-wal` présent », avant d'atteindre la condition testée.

**Cause (prouvée).** `SqliteSourceBuilder.Populate` créait une commande par table sans la libérer. Si le GC collecte une
telle commande avant la finalisation de ses instructions préparées, la fermeture de la connexion laisse un descripteur
« zombie » (`sqlite3_close_v2`) : SQLite saute le point de contrôle final et le journal `-wal` subsiste. Un
`GC.Collect()` avant la fermeture le reproduit à coup sûr (13/15 tests d'import refusés) ; commandes libérées, 17/17
passent sous le même GC forcé. Une première hypothèse (connexion poolée d'EF après `Migrate()`) a été **réfutée** par
la garde ajoutée : source froide après `Migrate()`, journal présent seulement après `Populate`.

**Correctif `da311dd`** (aucun changement de production) : commandes de `Populate` libérées ; garde « source froide »
(aucun `-journal` / `-wal` non vide) à la fin de chaque écriture du builder, qui échoue au bon endroit avec la vraie
cause. Local : unitaires 2461/2461, intégration **233/233 sur 4 exécutions complètes consécutives**, import 10/10.

CI sur le SHA exact `da311dd` : **run `37464003636`** (voir roadmap §6).

**Observation locale, hors CI.** Sous Docker Desktop (VM WSL2), l'horloge des conteneurs retarde de ~0,1–0,3 s sur
l'hôte Windows ; deux tests P4-9 (`BackupRestoreTests`) y ont échoué une fois sur 7 exécutions (« preuve de vérification
antérieure à la sauvegarde »), puis 0 sur les 4 suivantes. La CI Linux (horloge partagée) n'est pas concernée. Ce
comportement relève de **Q-P4-10-4** (dérive d'horloge) et sera à constater en recette P4-11 si l'outil est lancé
depuis une autre machine que le serveur ; aucun correctif n'est fait sans décision.

## 2. Les 18 critères

| # | Critère | Statut | Preuve existante | Lieu | Reste |
|---|---|---|---|---|---|
| 1 | Provider choisi par ADR | **PASS** | ADR-PROD-DB-002 acceptée (PostgreSQL) | — | — |
| 2 | Base neuve installable | **PARTIAL** | procédures P4-8 (`provision`), P4-9, P4-7 ; tests de bout en bout TLS | CI Linux | **installation Windows native** (P4-11 §1) |
| 3 | Connectable depuis plusieurs postes | **NOT STARTED** | multi-processus P4-10 (processus OS distincts) ; `configure-workstation` (P4-8) | CI Linux | **recette réelle sur plusieurs postes Windows** (P4-11 §3–4) |
| 4 | Migrations reproductibles | **PASS** | `SchemaTests.N1_two_freshDatabases_migrated_independently_have_identical_schemas` ; dérive EF ×2 en CI | CI | — |
| 5 | 14 primitives re-prouvées | **PASS (CI Linux)** | N6 (`PrimitivesTests`, P4-5F) ; concurrence multi-processus P4-10 ; contrat réel de `TryDeleteIfUnusedAsync` sous PostgreSQL documenté et prouvé (§1) | CI Linux | Windows natif (Q-P4-4-1 → P4-11) |
| 6 | Erreurs provider traduites | **PASS** | `PersistenceErrorMapper` (P4-4A1) + P4-10 + filet FK fournisseur (§1) | CI | — |
| 7 | Données SQLite migrables ou échec sûr | **PARTIAL** | `import-sqlite` (P4-7) : 17 preuves serveur dont bout en bout | CI Linux | **dry-run sur copie de production réelle (RR4)** + durée réelle (P4-11 §2) |
| 8 | Tests multi-processus verts | **PASS (CI Linux)** — réévalué | `MultiProcessTests` (10 scénarios roadmap + entrelacement fournisseur forcé) + charge (2) ; FAIL du 06/10 corrigé par `7df0d01` | CI Linux | Windows natif (P4-11 §4) |
| 9 | Perte réseau testée | **PASS (CI Linux)** | `NetworkFaultTests` (proxy TCP, 6) | CI Linux | câble débranché en recette (P4-11 §5.1) |
| 10 | Reconnexion testée (redémarrage serveur) | **PASS (CI Linux)** | `ServerRestartTests` (redémarrage réel d'un conteneur dédié, 2) | CI Linux | redémarrage du service Windows (P4-11 §5.3) |
| 11 | Sauvegarde testée | **PASS (CI Linux)** | P4-9 (28 preuves, vrais `pg_dump`/`pg_restore`) ; sous charge (P4-10) | CI Linux | tâche planifiée Windows, BitLocker, ACL (Q-P4-9-1 → P4-11 §6) |
| 12 | Restauration testée | **PASS (CI Linux)** | P4-9 (restauration réelle + intégrité) ; retour arrière d'import (P4-7) | CI Linux | exercice de sinistre en recette (P4-11 §6.4) |
| 13 | Documentation opérateur | **PARTIAL** | procédures P4-7, P4-8, P4-9 ; checklist P4-11 | dépôt | relecture par l'exploitant en recette |
| 14 | Secrets non committés | **PASS** | revue de l'audit du 06/10/2026 (arbre + historique par motifs) : seuls le mot de passe **éphémère** CI (`mmv_it_ephemeral`), des littéraux de test factices et les hash d'admin par défaut neutralisés par `DatabaseSeeder` | dépôt | — |
| 15 | Build et tests verts | **PASS** — réévalué | CI verte sur le SHA exact `da311dd` (run `37464003636`) : unitaires 2461, intégration 233, 0 ignoré | CI | fast-forward de `p4-multi-poste` |
| 16 | Aucune vulnérabilité | **PASS** | `dotnet list package --vulnerable --include-transitive` : 0 sur 12 projets (06/10/2026, bloquant en CI) | CI | — |
| 17 | Domain/Application provider-neutres | **PASS** | `ProviderNeutralityArchitectureTests` | CI | — |
| 18 | Recette complète multi-postes | **BLOCKED** | — | — | **P4-11 entière** (mise en attente) |

## 3. Ce qui reste bloqué uniquement par P4-11

Tout ce qui suit exige le laboratoire Windows et un opérateur ; **aucun** travail de code connu ne le débloque :

- critère **3** et critère **18** (aucune preuve existante) ;
- volet Windows natif des critères **2, 5, 7, 8, 9, 10, 11, 12** (O12, Q-P4-4-1, Q-P4-8-3, Q-P4-9-1) ;
- **RR4** (dry-run sur copie de production réelle) et volumétrie réelle de l'import (critère 7) ;
- relecture des procédures par l'exploitant (critère 13).

Le 06/10/2026, seuls des prérequis de laboratoire ont été préparés hors dépôt (VM, NTP, PostgreSQL 17 sur MMV-SRV,
volume de sauvegarde) ; **aucune** étape applicative de la checklist n'a été jouée et le second client n'existe pas.

## 4. Hors code, à trancher ou à constater avant le verdict

- **ADR-APP-DISTRIBUTION-001 reste PROPOSED** (spikes SD-1/2/3/5, QD-1, QD-10 ouverts) : la recette P4-11 se fait
  avec un paquet `dotnet publish` autonome copié, **pas** avec un installateur signé. L'audit doit dire si la V1
  multi-poste exige l'installateur.
- Questions ouvertes non bloquantes à accepter ou reporter explicitement : Q-P4-7-1, Q-P4-7-2, Q-P4-8-1, Q-P4-8-2,
  Q-P4-9-2 (= Q-P4-10-3), Q-P4-10-1, Q-P4-10-2, Q-P4-10-4.
- Licence d'évaluation des VM du laboratoire (P4-11 §0.2).
