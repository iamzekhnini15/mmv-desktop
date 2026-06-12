# ADR candidats — MMV (Phase 1)

> **Décisions différées.** Ces ADR candidats exposent contexte, options, avantages/inconvénients, risques et informations manquantes. **Aucune décision n'est arrêtée en Phase 1, aucune n'est implémentée.** Les valeurs réglementaires restent des gates Phase 0.5D.

Index :
- [ADR-001 — Modularité des politiques nationales](#adr-001)
- [ADR-002 — Multi-organisation / multi-magasin (isolation)](#adr-002)
- [ADR-003 — Persistance cible & stratégie de schéma](#adr-003)
- [ADR-004 — Money / devise / arrondis](#adr-004)
- [ADR-005 — Temps & fuseaux](#adr-005)
- [ADR-006 — Numérotation des documents](#adr-006)
- [ADR-007 — Documents & e-facturation](#adr-007)
- [ADR-008 — Localisation / RTL](#adr-008)
- [ADR-009 — Audit & données de santé](#adr-009)
- [ADR-010 — Stratégie de concurrency token](#adr-010)
- [ADR-011 — Cycle de vie de la configuration fiscale](#adr-011)

---

## <a id="adr-001"></a>ADR-001 — Modularité des politiques nationales

**Contexte.** Le cœur doit appliquer des règles BE et MA datées/versionnées (éligibilité INAMI, fréquence 2/5 ans, changement ≥0,5 D, fiscalité, mentions, validité de prescription…) **sans dupliquer l'application** (Phase 0.5D §3.1). Aujourd'hui : logique dans les VM ([SaleFormViewModel.cs:809-980](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L809-L980)), services de domaine contournés, aucun point d'extension.

**Options.**
1. **Configuration versionnée en base** (tables de règles datées + données de référence).
2. **Stratégies/services de politique** (interfaces `IPolicy` résolues par pays/date via DI).
3. **Modules applicatifs** (assemblies/feature-folders par pays, activés par configuration).
4. **Tables de règles + moteur d'évaluation** (règles déclaratives interprétées).
5. **Combinaison** : couche Application avec use cases neutres + résolution de politiques datées (2) alimentées par configuration versionnée (1).

| Option | Avantages | Inconvénients | Risques |
|---|---|---|---|
| 1 Config versionnée | barèmes/dates sans redéploiement ; auditable | logique conditionnelle limitée | sous-modélisation des cas complexes |
| 2 Stratégies | testable, typé, clair | prolifération de classes ; versionnement par date à gérer | couplage si mal cadré |
| 3 Modules | forte isolation BE/MA | risque de duplication du cœur (anti-objectif 0.5D) | divergence des modules |
| 4 Moteur de règles | très flexible | complexité, débogage difficile | sur-ingénierie |
| 5 Combinaison | sépare orchestration (use case) et variabilité (politique+données) | nécessite d'abord la couche Application (R-05) | dépend de fondations |

**Décision différée.** Recommandation d'orientation : **option 5** (use cases neutres + politiques datées paramétrées par données versionnées), à confirmer après la couche Application.

**Informations manquantes.** Granularité réelle des règles INAMI/AMO ; cadence d'évolution des barèmes ; besoin d'historisation rétroactive.

---

## <a id="adr-002"></a>ADR-002 — Multi-organisation / multi-magasin (isolation)

**Contexte.** Aucun `Organisation`/`Magasin`/tenant n'existe (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`). Cible : plusieurs organisations et magasins, stock par magasin, numérotation par établissement, reporting consolidé, prévention des fuites inter-tenant ; produit desktop évoluant vers SaaS B2B ; contrainte CNDP (MA) sur la localisation/transferts.

**Trois approches comparées (exigé) :**

| Approche | Principe | Avantages | Inconvénients | Adapté à |
|---|---|---|---|---|
| **A. Colonne discriminante partagée** (`OrganizationId`/`StoreId` + filtres globaux EF) | une base, filtrage par tenant | simple à migrer depuis l'existant mono-base ; reporting consolidé facile ; coût d'infra faible | risque de fuite si filtre oublié ; « voisins bruyants » ; isolation logique seulement | démarrage SaaS, faible nb de tenants |
| **B. Schéma par tenant** (même base, schémas distincts) | séparation par schéma | meilleure isolation logique ; sauvegarde ciblée partielle | SQLite ne gère pas les schémas → impose un autre SGBD ; complexité migrations | tenants moyens, SGBD serveur |
| **C. Base par organisation** | une base SQLite/serveur par org | isolation forte ; portabilité ; conforme à un déploiement « par enseigne » ; aligné desktop actuel | reporting cross-org complexe ; multiplication des migrations ; routage de connexion | desktop multi-sites, exigences CNDP fortes |

**Compromis clés.** Le desktop actuel (1 fichier SQLite) est proche de **C** par nature. Pour un futur SaaS B2B, **A** est le plus courant mais déplace le risque vers la rigueur des filtres ; **C** maximise l'isolation (utile RGPD/CNDP) au prix du reporting.

**Décision différée.** Probable trajectoire **C (desktop/déploiement par organisation) → A ou hybride en SaaS**, à trancher selon le modèle commercial et `G-CNDP`. Nécessite d'abord un `StoreId`/`OrganizationId` porté par les entités (fondation R-01).

**Informations manquantes.** Nombre de tenants visé ; SGBD SaaS cible ; exigences exactes de localisation des données (MA).

---

## <a id="adr-003"></a>ADR-003 — Persistance : stratégie de schéma, exécution des migrations & rollback

**Contexte.** Aujourd'hui : `EnsureCreated()` contourne les migrations ([DbInitializer.cs:16](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16)), 2 chemins physiques DB sur 4 sites de configuration, factory design-time ≠ runtime, montants en `REAL`. 7 migrations existent mais ne sont pas appliquées au runtime.

### A. Source de vérité du schéma

Décision d'orientation (différée) : **migrations EF = source de vérité**, suppression de `EnsureCreated`, **chemin DB unique**. Le statu quo (`EnsureCreated`) est écarté car il empêche l'évolution de schéma et la migration de données. Corriger d'abord les types monétaires (ADR-004) pour éviter une migration jetable.

### B. **Déclencheur d'exécution des migrations (ne pas imposer `Database.Migrate()` au démarrage)**

| Option | Avantages | Inconvénients | Adapté à |
|---|---|---|---|
| 1. Migration au **démarrage du desktop** | simple, transparent | risque sur fichier ouvert/2ᵉ instance ; pas de fenêtre de contrôle ; échec = app inutilisable | desktop simple, faible enjeu |
| 2. **Utilitaire de migration dédié** (commande/outil séparé) | contrôle, journal, exécution supervisée | étape opérationnelle explicite | desktop avec support |
| 3. Migration **intégrée à l'installeur / au programme de mise à jour** | s'exécute hors de l'app, sauvegarde possible avant | dépend du packaging (cf. R-25, absent) | desktop distribué |
| 4. Migration **par le pipeline de déploiement** (futur SaaS) | contrôlée, testée en préprod, reversible côté infra | nécessite infra serveur | SaaS |

**Pour le desktop SQLite**, quelle que soit l'option, prévoir : **sauvegarde préalable du fichier**, **détection d'une autre instance**, **gestion du verrou** SQLite, **reprise après interruption**, **vérification du schéma** (version appliquée), **journal de migration**, **restauration en cas d'échec**. **Pour le futur SaaS**, privilégier l'option 4 (migration contrôlée au déploiement, pas au démarrage applicatif).

### C. Stratégie de rollback (corrigée en Phase 1B — `Down()` n'est pas la restauration de données)

Distinguer **cinq mécanismes** qui ne se substituent pas :
1. **Rollback de code** (revenir à une version applicative antérieure) ;
2. **Rollback de schéma** (migration `Down()`) — utile pour le DDL, **mais ne restaure pas les données** transformées/supprimées ;
3. **Restauration des données** (depuis une sauvegarde vérifiée) ;
4. **Migration corrective vers l'avant** (forward-fix) — souvent préférable en production ;
5. **Restauration du fichier SQLite sauvegardé** (desktop).

Pour toute **migration destructrice ou transformation de données** : **sauvegarde vérifiée obligatoire** avant exécution, **contrôle d'intégrité avant/après**, **stratégie de reprise** définie, et **aucune garantie de réversibilité par `Down()` seul**.

**Décision différée.** Orientation : migrations source de vérité (A) ; déclencheur à choisir (B) selon desktop/SaaS ; rollback fondé sur sauvegarde + forward-fix (C), pas sur `Down()`.

**Informations manquantes.** Bases utilisateurs déjà peuplées par `EnsureCreated` à reprendre ? SGBD SaaS cible ? Existence d'un canal de packaging/MAJ (R-25) ?

---

## <a id="adr-004"></a>ADR-004 — Money / devise / arrondis

**Contexte.** `Money` (decimal + devise, arrondi `ToEven`) existe mais **inutilisé** ([Money.cs](../../src/MMV.Domain/ValueObjects/Money.cs)). Les entités utilisent `decimal` brut **sans devise**, stockés en **`REAL`** (flottant). Cible : EUR et MAD sans devise implicite, arrondis explicites, multi-devise par magasin/document.

> **Point clé (Phase 1B) :** ne pas considérer que des « colonnes `decimal`/`numeric` » constituent **automatiquement** une solution correcte sous **SQLite**. SQLite a une affinité de type dynamique : il **ne possède pas** de type `decimal`/`numeric` à précision fixe natif ; une colonne déclarée `decimal`/`numeric` peut être stockée en `REAL` (flottant) ou `TEXT` selon la valeur et le provider. La décision doit **séparer trois plans** : (a) le **value object du domaine**, (b) la **représentation en base**, (c) la **configuration propre au provider**.

**Options de représentation comparées.**
1. **Montant en unités mineures entières (centimes) + code devise** (`long` + ISO-4217).
2. **`decimal` dans le domaine, stocké en `TEXT`** (sérialisation canonique préservant la précision).
3. **`decimal` dans le domaine, converti en `double`** (statu quo `REAL`) — **avec perte de précision documentée**.
4. **Mapping dépendant du provider** : représentation adaptée à SQLite (TEXT ou entier) en desktop, `numeric/decimal` natif pour un futur SGBD serveur.
5. **Valeur décimale normalisée stockée** (échelle fixe) **avec contraintes applicatives** (validation d'échelle/arrondi côté domaine).

| Option | Précision | Sommes/agrégations | Tri & comparaison en base | Migrations | Portabilité SQLite↔PostgreSQL | Complexité | Performance | Auditabilité |
|---|---|---|---|---|---|---|---|---|
| 1 Unités mineures (entier) | exacte | exactes (entiers) | correct (entier) | REAL→entier (×100) à valider | excellente (entier universel) | conversions d'affichage | excellente | très bonne (valeur exacte) |
| 2 decimal→TEXT | exacte si format canonique | **à faire en mémoire** (TEXT ne s'agrège pas numériquement) | **lexicographique, piégeux** | REAL→TEXT | bonne | moyenne | agrégations plus lentes | bonne |
| 3 decimal→double (REAL) | **perte** (arrondis binaires) | imprécises à l'échelle | numérique mais imprécis | aucune (statu quo) | bonne | faible | bonne | **faible** (valeurs non exactes) |
| 4 Provider-dependent | exacte si bien mappé | exactes côté serveur | correct côté serveur | double cible à maintenir | **conçue pour** | élevée | bonne | bonne |
| 5 Décimal normalisé + contraintes | exacte si contraintes respectées | dépend du stockage sous-jacent | dépend du stockage | selon stockage | moyenne | moyenne | moyenne | bonne |

**Décision différée — ne pas trancher l'« owned type EF » avant d'avoir séparé les trois plans.** Orientations plausibles : VO `Money` au domaine (a) ; pour SQLite (b/c), privilégier **unités mineures entières (option 1)** ou **TEXT canonique (option 2)** plutôt que `REAL` ; prévoir l'**option 4** pour le futur SGBD serveur. Le choix du mapping EF (owned type, convertisseur de valeur, colonnes séparées) est **subordonné** à ces décisions et **non arrêté ici**.

**Informations manquantes.** Règles d'arrondi fiscal BE/MA par catégorie (gates `G-TVA-BE`/`G-TVA-MA`) ; SGBD SaaS cible ; gestion du change si documents multi-devises ; besoin de calculs/agrégations en base vs en mémoire.

---

## <a id="adr-005"></a>ADR-005 — Temps & fuseaux

**Contexte.** Mélange `DateTime.Now`/`UtcNow` (23 occurrences `Now`), `Notification.CreatedAt = DateTime.Now`, `HasDefaultValue(DateTime.UtcNow)` figé, pas d'horloge injectable. Cible : Europe/Brussels et Africa/Casablanca, stockage cohérent, date métier locale, horloge testable.

**Options.**
1. **`IClock` injectable + stockage UTC + conversion par fuseau du magasin** (date métier locale calculée).
2. **`DateTimeOffset` partout** (UTC + offset stocké).
3. **Statu quo** — rejeté (non testable, fuseaux non gérés).

| Option | Avantages | Inconvénients | Risques |
|---|---|---|---|
| 1 IClock+UTC | testable ; fuseau par magasin ; aligne 0.5D | refonte des accès temps | oublis résiduels de `DateTime.Now` |
| 2 DateTimeOffset | conserve l'offset d'origine | ne suffit pas pour les règles « date métier locale » | confusion offset/zone |
| 3 Statu quo | nul | bloque tests & fuseaux | bugs de date |

**Décision différée.** Orientation **1** (+ éventuellement `DateTimeOffset` en stockage), fuseau rattaché au `Magasin` (dépend de R-01).

**Informations manquantes.** Règles de « date métier » par marché (clôture de caisse, exercice fiscal).

---

## <a id="adr-006"></a>ADR-006 — Numérotation des documents

**Contexte.** `SaleNumber`/`OrderNumber` générés par `new Random().Next(10000)` ([SaleFormViewModel.cs:835](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L835), [:896](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L896)) avec index UNIQUE → collisions/échecs. Cible : séquences fiables par organisation/magasin/exercice, unicité, audit ; futures factures (mentions légales).

**Options.**
1. **Table de séquences transactionnelle** (compteur par org/magasin/type/exercice, incrément dans la transaction du document).
2. **Séquence SGBD native** (selon provider).
3. **Numérotation applicative avec verrou** (`SELECT … FOR UPDATE`/transaction sérialisable).

| Option | Avantages | Inconvénients | Risques |
|---|---|---|---|
| 1 Table séquences | portable SQLite/serveur ; multi-tenant ; auditable | gérer la contention | points chauds si très concurrent |
| 2 Séquence native | performante | non portable SQLite ; pas de remise à zéro par exercice simple | couplage provider |
| 3 Verrou applicatif | simple | contention/timeouts | blocages |

**Décision différée.** Orientation **1**, intégrée à la transaction du use case de vente/facture (dépend de R-01, R-05, R-03). Format légal des numéros à confirmer (gate `G-FACT-MA`, mentions BE).

**Informations manquantes.** Règles de format/continuité légales BE/MA ; remise à zéro par exercice ; gestion des annulations/avoirs.

---

## <a id="adr-007"></a>ADR-007 — Documents & e-facturation

**Contexte.** Aucune entité Facture, aucune TVA, aucune séparation B2C/B2B, aucun canal Peppol. Phase 0.5D : distinguer **données de facture**, **document lisible (PDF)** et **canal d'échange**; BE B2B structuré obligatoire dans son champ depuis 01/01/2026 ; MA mentions/ICE + e-facture (gate `G-EINV-MA`).

**Options.**
1. **Modèle Facture structuré + générateur de représentation (PDF) + connecteurs découplés** (Peppol/AMO/INAMI hors cœur).
2. **Facture = document PDF uniquement** — rejeté (un PDF ne remplace pas la facture structurée obligatoire).
3. **Intégration native Peppol** vs **interopérabilité via prestataire** (sous-option du connecteur).

| Option | Avantages | Inconvénients | Risques |
|---|---|---|---|
| 1 Modèle + connecteurs | sépare donnée/rendu/canal (aligné 0.5D) ; extensible BE/MA | conception plus lourde | dépend du modèle monétaire & fiscal |
| 2 PDF only | rapide | non conforme B2B BE | non-conformité |
| 3a Peppol natif | contrôle total | coût de conformité EN 16931 | maintenance |
| 3b Via prestataire | délégation conformité | dépendance tierce | contractuel |

**Décision différée.** Orientation **1** ; choix natif/prestataire selon `G-EINV-BE`/`G-EINV-MA` et le périmètre B2B réel. **Aucune obligation MA codée** tant que le texte d'application n'est pas confirmé.

**Informations manquantes.** Périmètre B2B exact de MMV (gate `G-EINV-BE`), mentions exactes (`G-FACT-MA`), calendrier MA (`G-EINV-MA`).

---

## <a id="adr-008"></a>ADR-008 — Localisation / RTL

**Contexte.** 606 littéraux FR codés en dur, 0 `.resx`, 0 `x:Uid`, 0 `FlowDirection`, recherche `ToLower().Contains` non normalisée. Cible : FR/NL/DE (BE) et FR/AR-RTL (MA), formats locaux, documents localisés, recherche latin/arabe ; périmètre de lancement = décision produit (gate `L-1`).

**Options.**
1. **Ressources `.resx` + service de localisation Avalonia + `FlowDirection` piloté par culture**.
2. **Dictionnaires de ressources XAML par langue** (ResourceDictionary).
3. **Catalogue de traductions en base** (pour libellés métier/catalogue) + ressources pour l'UI statique.

| Option | Avantages | Inconvénients | Risques |
|---|---|---|---|
| 1 .resx | standard .NET ; outillage | binding dynamique de culture à concevoir en Avalonia | RTL à valider sur tous les écrans |
| 2 ResourceDictionary | natif Avalonia | gestion de la bascule de langue | duplication |
| 3 i18n en base | libellés métier multilingues (catalogue) | ne couvre pas l'UI statique seul | complexité combinée |

**Décision différée.** Orientation **combinaison 1 + 3** (ressources pour l'UI, base pour le catalogue/documents), RTL piloté par la culture. Le périmètre de langues activées dépend de `L-1` (décision produit).

**Informations manquantes.** Langues du 1er lancement par marché ; validation des traductions NL/AR (réf. glossaire 0.5D non certifié).

---

## <a id="adr-009"></a>ADR-009 — Audit & données de santé

**Contexte.** Aucun journal d'audit, aucune trace d'accès aux ordonnances, authz UI-only, base en clair, cascade supprimant les données de santé ([CustomerConfiguration.cs:61-64](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs#L61-L64)). Cible RGPD (BE, données de santé = catégorie particulière) et loi 09-08/CNDP (MA), avec gate `G-CNDP`.

**Options.**
1. **Journal d'audit applicatif immuable** (append-only) + interception des accès aux données de santé + suppression logique/rétention.
2. **Audit au niveau base** (triggers/CDC) — selon provider.
3. **Combinaison** : audit applicatif (qui/quoi/quand/contexte) + chiffrement au repos + politique de rétention/export.

| Option | Avantages | Inconvénients | Risques |
|---|---|---|---|
| 1 Audit applicatif | portable ; contexte métier riche | discipline transverse | oublis si non centralisé |
| 2 Audit base | exhaustif au niveau données | dépend du provider ; SQLite limité | non portable |
| 3 Combinaison | défense en profondeur ; aligne RGPD/CNDP | effort | coordination |

**Décision différée.** Orientation **3**, branchée sur la couche Application (R-05) et l'isolation tenant (R-01). Le périmètre exact (chiffrement, hébergement, transferts) dépend de `G-CNDP` et du déploiement.

> **Nuance (Phase 1B).** L'absence d'audit/chiffrement est une **lacune de sécurité et d'« accountability » à fort risque** pour des données de santé, **probablement nécessaire** à corriger — mais sa portée exacte relève d'une **analyse de risque et d'une validation juridique locale**, et non d'une conclusion de non-conformité tirée du seul code.

**Informations manquantes.** Rôles juridiques (responsable/sous-traitant), localisation d'hébergement, durées de rétention par juridiction.

---

## <a id="adr-010"></a>ADR-010 — Stratégie de concurrency token

**Contexte.** Aucun jeton de concurrence dans le modèle (`ABSENCE_VÉRIFIÉE` : 0 `IsConcurrencyToken`/`IsRowVersion`), décrément de stock en lecture-modification-écriture ([SaleFormViewModel.cs:938-948](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L938-L948)). **Important : SQLite n'a pas de `rowversion`/`timestamp` auto-géré** comme SQL Server ; on ne peut donc pas « simplement imposer `RowVersion` ». On emploie le terme générique **« concurrency token »** tant que le provider cible n'est pas décidé.

**Options.**
1. **Jeton `Guid` géré par l'application** (régénéré à chaque update, configuré `IsConcurrencyToken`).
2. **Compteur/version entier géré par l'application** (incrémenté à chaque update).
3. **Mise à jour atomique conditionnelle du stock** (`UPDATE … SET qty = qty - n WHERE qty >= n`), sans token de ligne.
4. **Niveau d'isolation transactionnel** adapté (transaction + relecture) pour les sections critiques.
5. **Stratégie propre au futur SGBD serveur** (ex. `xmin` PostgreSQL, `rowversion` SQL Server) activée lorsque le provider est choisi.

| Option | Avantages | Inconvénients | Portabilité |
|---|---|---|---|
| 1 Guid app | portable, simple à mapper | écritures concurrentes détectées au commit | excellente |
| 2 Version entière | portable, lisible/auditable | gestion manuelle de l'incrément | excellente |
| 3 Update conditionnel | évite la perte de stock sans token | spécifique aux compteurs (stock) | bonne |
| 4 Isolation transactionnelle | pas de colonne dédiée | contention/verrous ; sémantique SQLite limitée | moyenne |
| 5 Token natif serveur | optimal côté SGBD serveur | non disponible en SQLite | dépend du provider |

**Critères & tests attendus (tous providers).** Détection effective des écritures concurrentes ; **gestion explicite de `DbUpdateConcurrencyException`** (politique de re-lecture/retry/abandon) ; test de **mise à jour perdue** sur stock ; test d'**update conditionnel** rejetant un décrément sous zéro ; test de numérotation concurrente (lien ADR-006).

**Décision différée.** Orientation plausible : **combiner (2 ou 1) pour les agrégats financiers/documents** et **(3) pour le décrément de stock**, avec gestion de `DbUpdateConcurrencyException`. Le token natif (5) sera réévalué au choix du SGBD SaaS. Rien n'est arrêté ici.

**Informations manquantes.** SGBD SaaS cible ; volume de concurrence réel (multi-postes par magasin).

---

## <a id="adr-011"></a>ADR-011 — Cycle de vie de la configuration fiscale

**Contexte.** Aucune fiscalité modélisée (cf. ADR-007). **Un taux de 0 % est une vraie règle fiscale** (exonération/hors-champ), pas une absence de configuration : il ne faut donc **pas** représenter l'absence de fiscalité par un « taux 0 ». La capacité métier attendue doit distinguer des **états** explicites.

**États conceptuels à modéliser (capacité, pas classe imposée).**
- **`NotConfigured`** — aucune politique fiscale pour (marché, catégorie, date). Conséquence : **aucune facture fiscale définitive ne doit pouvoir être émise** ; seuls **brouillon**, **simulation** ou **tests avec politique fictive** sont autorisés.
- **`Configured`** — politique valide et applicable à la date/au contexte.
- **`InvalidOrExpired`** — politique périmée ou incohérente → **blocage de l'émission définitive**, signalement.

**Options de portage.**
1. État dérivé d'une **table de politiques fiscales datées** (présence/validité d'une règle applicable).
2. **Service de politique fiscale** exposant l'état pour (catégorie, date, marché).
3. Combinaison (table de données + service d'évaluation), cohérente avec ADR-001/ADR-007.

**Conséquences attendues.** L'émission d'un document fiscal **définitif** est conditionnée à l'état `Configured` ; sinon, le système reste en brouillon/simulation. **Aucune valeur de taux (BE 21 %, MA 20/10 %, ni 0 %) n'est codée** : elles proviennent des gates `G-TVA-BE`/`G-TVA-MA`.

**Décision différée.** Orientation **3**. Aucune classe définitive imposée ; capacité à concevoir en Phase 2 après le modèle monétaire (ADR-004) et la couche Application (ADR-001).

**Informations manquantes.** Mapping catégorie↔taux (gates TVA) ; règles d'exonération/hors-champ par marché ; définition exacte du « définitif » vs « brouillon » par juridiction.
