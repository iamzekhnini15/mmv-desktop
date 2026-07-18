# P3 — Feuille de route : **règles métier** MMV

> Document de cadrage produit à l'ouverture de **P3** (issue de **P2D**, mergé sur `main`). Aucune étape
> ci-dessous n'est implémentée ici : ce document fixe l'objectif, l'ordre des étapes, les règles métier
> **candidates** par domaine et les critères de sortie. **Le dépôt réel prime toujours sur ce document.**
>
> **Changement de nature vs P1/P2.** P1/P2 étaient du **nettoyage technique** (couches, use cases, lectures).
> **P3 est métier** : MMV doit devenir un logiciel réellement *utile à un opticien*. On introduit ici des
> **règles métier** (validations, transitions, garde-fous de suppression, cohérence des flux), pas seulement
> du déplacement de code.
>
> **Hors périmètre absolu P3 (inchangé depuis P2) :** SaaS, `Organization`, `Store`, multi-tenant,
> `Subscription`, `Plan`, `Payment` avancé / en ligne, `Invoice`, `Quote`, TVA (Belgique/Maroc/France),
> facturation légale, signature électronique, cloud, email automatique. P3 traite les **règles métier de
> l'atelier d'optique**, pas l'évolution commerciale/fiscale.
>
> **Cadre production V1 (décision P3-0B, [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md)) :**
> MMV V1 production est **multi-poste** (plusieurs postes d'un même magasin partagent **une base centrale**).
> Le multi-poste **n'est pas** du multi-tenant (toujours hors périmètre) : c'est **un magasin, une base**,
> partagée par plusieurs ordinateurs. **Toute règle métier P3 doit rester compatible multi-poste.** SQLite
> local reste utile pour dev/test/démo/mono-poste mais **ne doit pas guider** les décisions de production
> multi-poste ; SQLite sur dossier réseau est **non supporté**.

Base de départ (vérifiée au 2026-07-07, cf. [rapport P3-0](../implementation/P3-0-business-audit-report.md)) :
branche `p3-business-rules`, build vert, **585 tests**, 0 vulnérabilité, `has-pending-model-changes = false`,
`MMV.Application` pure (Domain + `DI.Abstractions`).

---

## 1. Principes directeurs P3

1. **Une règle métier a un seul propriétaire de couche** (rappel [ADR frontières §9](adr-application-boundaries.md)) :
   invariants durs → **Domain** ; préconditions de cas d'usage → **Application** ; ergonomie de saisie → **UI**.
   Ne jamais dupliquer une règle sur deux couches.
2. **Iso-comportement sauf décision explicite.** Chaque règle nouvelle est documentée, testée, et son
   introduction est un **choix conscient** (pas un effet de bord). On ne durcit pas une saisie optique
   facultative sans justification métier.
3. **Ordonnance / prescription source = vérité intangible.** Aucune règle P3 ne réécrit une ordonnance
   d'examen. Les transformations (ex. transposition) sont des **vues** dérivées, jamais des mutations.
4. **Sécurité des écritures conservée.** Tout décrément de stock passe par `IStockMutationService` ; toute
   écriture multi-étapes passe par `ITransactionRunner`. P3 **corrige** les endroits qui dérogent, il n'en
   crée pas de nouveaux.
5. **Suppression = dernier recours.** Préférer l'archivage/désactivation dès qu'un historique métier existe.
6. **`MMV.Application` reste pure** ; aucune régression d'architecture P2 (garde-fous `AppUiPersistenceGuardrailTests`).
7. **MMV V1 production est multi-poste** ([ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md)).
   Toute règle métier P3 doit rester **compatible avec une base centrale partagée par plusieurs postes** :
   pas d'hypothèse mono-utilisateur, pas de cache local trompeur, écritures atomiques et concurrence prévue.
   SQLite local reste utile pour dev/test/démo mais **ne doit pas guider** les décisions production multi-poste.

---

## 2. Ordre des étapes P3

| Étape | Titre | Nature |
|---|---|---|
| **P3-0** | Audit métier initial | audit + roadmap |
| **P3-0B** | ADR multi-poste / stratégie DB production | ADR + roadmap (ce cycle) |
| **P3-1** | Standardisation des validations / `Result` | socle transverse |
| **P3-2** | Clients | domaine |
| **P3-3** | Ordonnances | domaine |
| **P3-4** | Produits | domaine |
| **P3-5** | Stock / mouvements **multi-poste safe** | domaine |
| **P3-6** | Commandes **multi-poste safe** | domaine |
| **P3-6B** | Fiche atelier de montage / technicien | domaine (nouveau flux) |
| **P3-7** | Ventes **multi-poste safe** | domaine |
| **P3-8** | Notifications métier | domaine |
| **P3-9** | Fournisseurs | domaine |
| **P3-10** | Utilisateurs locaux | domaine |
| **P3-11** | Scénarios métier de recette | recette bout-en-bout |
| **P3-12** | Audit final P3 | audit |

> Une étape = un domaine = un commit + un rapport + une CI verte (protocole P2). Ordre indicatif : les
> dépendances réelles observées dans le code peuvent justifier un réordonnancement documenté (ex. P3-1 doit
> précéder les domaines car il pose le socle de validation ; P3-6B suit P3-6 et précède P3-7 car la fiche
> atelier dérive de la commande et alimente le suivi de vente).

En parallèle, une **règle optique transverse** (transposition sphère/cylindre/axe) est cadrée en §5 et
détaillée dans [les notes atelier & transposition](../domain/P3-workshop-sheet-and-transposition-notes.md).
Elle n'est **pas** une étape autonome : elle est consommée par P3-3 (validation) et P3-6B (affichage atelier).

---

## 3. Détail des étapes

### P3-0B — ADR multi-poste / stratégie DB production

- **Objectif** : acter, avant toute règle métier, que **MMV V1 production est multi-poste** et fixer la
  **stratégie base de données de production**. Décision produit **imposée** (indispensable à un vrai magasin
  d'optique). Livrable : [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md).
- **Décision** : production multi-poste ⇒ **base client/serveur obligatoire** (candidat principal
  **PostgreSQL**, secondaire **SQL Server Express**, choix final différé à un spike). **SQLite local**
  autorisé pour dev/test/démo/mono-poste ; **SQLite réseau non supporté** ; cloud/API/SaaS reportés.
- **Périmètre** : ADR + mise à jour de cette roadmap + rapport P3-0B. **Aucun code / test / migration.**
- **Impact sur P3** : P3-5 (stock), P3-6 (commandes), P3-6B (fiche atelier persistée), P3-7 (ventes) sont
  **critiques multi-poste** ; toute règle P3 doit rester compatible base centrale partagée (cf. §1.7).
- **Sortie** : ADR acceptée, roadmap alignée, aucune modification de code ; provider inchangé (SQLite).

### P3-1 — Standardisation des validations / `Result`

- **Objectif** : poser le socle de validation **côté Application** (aujourd'hui absente : `MMV.Application`
  ne contient **aucun** validateur ; la validation vit dans 4 `AbstractValidator` Domain + dans l'UI). Décider
  et documenter la convention `Result` vs exceptions typées (cf. [ADR frontières §8](adr-application-boundaries.md)).
- **Périmètre** : convention de validation de commande (FluentValidation côté Application **ou** gardes
  explicites) ; harmonisation des `*Result` ; contrat « introuvable » homogène (déjà amorcé : `*Found = false`).
- **Hors périmètre** : réécrire les use cases existants ; introduire un `Result<T>` monade complet si non nécessaire.
- **Règles candidates** : chaque `*Command` d'écriture a une validation de précondition explicite et testée ;
  message d'erreur métier normalisé.
- **Fichiers/domaines** : `src/MMV.Application/Common/`, `UseCases/**` (commandes), tests Application.
- **Tests attendus** : validateurs de commande (cas valides/invalides), non-régression des use cases existants.
- **Risques** : sur-validation dupliquant le Domain ; churn massif. Mitigation : socle + 1 domaine pilote.
- **Sortie** : convention documentée + appliquée au domaine pilote ; build/tests verts.
- **Réalisé (cf. [rapport P3-1](../implementation/P3-1-validation-result-report.md)) :**
  - **Choix validation retenu** : réutiliser les **validateurs FluentValidation du Domain** depuis Application
    via `Common/CommandValidation` (règle propriétaire du Domain, pas de duplication) ; **aucune** dépendance
    NuGet ajoutée (FluentValidation transite déjà du Domain, Apache-2.0) ; **pas** de framework maison ni de
    `*CommandValidator` parallèle.
  - **Choix `Result` retenu** : conserver le drapeau `*Found` homogène + ajouter `ValidationErrors`/`IsValid`
    neutres (`Common/ValidationError`) sur les `*Result` d'écriture ; refus de règle **dure** = exception
    typée Domain (ADR §8) ; **pas** d'`Ardalis.Result`, **pas** de monade `Result<T>`.
  - **Domaine pilote appliqué** : **Clients** (`CreateCustomerUseCase`, `UpdateCustomerUseCase`) —
    validation de commande **avant** écriture, aucune persistance si invalide. Base : **592 tests** verts,
    `MMV.Application` pure.

### P3-2 — Clients

- **Objectif** : fiabiliser création/édition/suppression et recherche client.
- **Règles candidates** : `FirstName`/`LastName` requis (déjà) ; email valide si renseigné (déjà) ;
  **garde-fou de suppression** si historique (ventes/ordonnances) → **archiver** plutôt que supprimer ;
  politique de doublon *souple* (ne pas bloquer un téléphone partagé en famille — alerte, pas blocage).
- **Hors périmètre** : fusion de doublons, RGPD/export, sécurité sociale comme identifiant.
- **Fichiers** : `Customer`, `CustomerValidator`, `Create/Update/DeleteCustomerUseCase`, VM clients.
- **Tests** : suppression bloquée/adoucie avec historique ; validation ; recherche.
- **Risques** *(formulation corrigée en P3-2B — le dépôt réel prime)* : `DeleteCustomerUseCase` s'appuyait sur les
  **cascades EF**, avec deux effets **distincts** (et non « effacer l'historique de ventes ») :
  les **ordonnances** étaient **cascade-supprimées** (`Cascade` → perte de données médicales) ; les **ventes**
  étaient **conservées mais détachées** de leur client (`SetNull` → historique commercial anonymisé). Le risque de
  perte pure portait donc d'abord sur les **ordonnances**. **Point dur tranché en P3-2B** : les deux clés
  étrangères passent en `Restrict` (la base refuse la suppression d'un client porteur d'historique) et l'archivage
  devient l'alternative non destructive.
- **Sortie** : suppression sûre (archivage ou refus documenté) ; tests verts.
- **État** : **P3-2 terminé** — **P3-2A** (audit), **P3-2B** (métier + persistance) et **P3-2C** (UI) livrés.
  - [rapport P3-2A](../implementation/P3-2A-customer-domain-audit-report.md) — audit.
  - [rapport P3-2B](../implementation/P3-2B-customer-archiving-and-deletion-report.md) — `IsArchived`,
    suppression conditionnelle, clés étrangères `Restrict`, `SetCustomerArchivedUseCase`.
  - [rapport P3-2C](../implementation/P3-2C-customer-ui-archiving-report.md) — UI : confirmation explicite avant
    suppression physique, refus métier affiché en clair (proposant l'archivage), boutons **Archiver / Réactiver**
    (état **absolu**, jamais un basculement), filtre **« Afficher les clients archivés »**
    (`ListCustomersQuery.IncludeArchived`, filtré **en base**), et **rechargement depuis la source après toute
    mutation** (la collection locale n'est jamais la vérité — multi-poste, ADR-PROD-DB-001).
- **Risque résiduel reporté — partiellement levé** : l'archivage n'empêchait **ni** la création d'une ordonnance
  **ni** celle d'une vente pour un client archivé (aucun garde-fou **à l'écriture**). **P3-3B a posé le garde-fou du
  côté des ordonnances** (chargement du client, refus par `BusinessRuleException` si `IsArchived`). Le refus reste
  **exigé** de **P3-7** (ventes).

### P3-3 — Ordonnances

- **Objectif** : compléter les **règles optiques manquantes** (aujourd'hui `PrescriptionValidator` ne valide
  que des **plages** par œil, sans **cohérence croisée**).
- **Règles candidates** (à valider métier, ne pas durcir aveuglément) :
  - si `Cylinder` non nul ⇒ `Axis` **obligatoire** (0–180) ; `Axis` sans cylindre = incohérent ;
  - si `PrismValue` renseigné ⇒ `PrismBase` obligatoire, et réciproquement ;
  - `Addition` cohérente avec un usage progressif/près (au niveau **verre**, pas forcément ordonnance brute) ;
  - conserver la tolérance sur les champs optiques **facultatifs** (ne pas bloquer une ordonnance partielle).
- **Hors périmètre** : implémenter la transposition (cadrée §5, consommée en affichage atelier P3-6B).
- **Fichiers** : `Prescription`, `PrescriptionValidator`, `Create/Update/DeletePrescriptionUseCase`.
- **Tests** : chaque règle croisée (cyl↔axe, prisme↔base) ; ordonnance partielle acceptée.
- **Risques** : casser des données historiques partielles. Mitigation : règles à la **saisie**, pas rétroactives.
- **Exigence OBLIGATOIRE héritée de P3-2B** : `CreatePrescriptionUseCase` **ne vérifie pas** que le client visé est
  actif. P3-2B exclut les clients archivés des listes et des sélecteurs, mais **ne pose aucun garde-fou à
  l'écriture** : une ordonnance peut donc encore être créée pour un client archivé (identifiant fourni
  directement, écran resté ouvert, sélection obsolète). **P3-3 doit implémenter le refus au moment de l'écriture**
  (charger le client, refuser si `IsArchived`, selon la convention P3-1 : refus dur = exception typée). Cf.
  [rapport P3-2B §10](../implementation/P3-2B-customer-archiving-and-deletion-report.md).
- **Sortie** : cohérence croisée validée ; ordonnance source restant intangible.
- **État** : **P3-3 TERMINÉ localement** (sous réserve de CI distante) — **P3-3A** (audit), **P3-3B** (métier +
  validation) et **P3-3C** (UI) livrés.
  - [rapport P3-3A](../implementation/P3-3A-prescription-domain-audit-report.md) — audit. **Découverte
    structurante** : `PrescriptionValidator` était **du code mort** (appelé par aucun use case) — toute la validation
    optique reposait sur l'UI seule.
  - [rapport P3-3B](../implementation/P3-3B-prescription-validation-and-archived-customer-report.md) — validateur
    **réellement câblé** (Create **et** Update via `CommandValidation`), règles croisées cylindre ⇄ axe et
    prisme ⇄ base (OD/OG), rejet des valeurs `NaN`/infinies, normalisation `Axis 0 → 180` (fonction Domain **pure**,
    hors validateur), **refus de création pour un client archivé** (`BusinessRuleException`, message constant),
    **client introuvable** traité explicitement, et **suppression de `PrescriptionService`** (second chemin
    d'écriture mort qui contournait tous les garde-fous). **Aucune migration, aucune UI.**
  - [rapport P3-3C](../implementation/P3-3C-prescription-ui-validation-report.md) — UI : **`PrismBase` en `ComboBox`**
    sur les 4 valeurs réelles de l'enum (+ « Aucune » = `null`), watermark erroné « H/V/In/Out » **supprimé** (`H` et
    `V` n'existent pas) ; **affichage des `ValidationErrors`** (ordre conservé, dédupliquées, sans détail technique) ;
    **capture spécifique de la `BusinessRuleException`** « client archivé » (message métier exact + modal, `catch`
    générique séparé) ; **`CustomerFound` / `PrescriptionFound` = false** traités explicitement ; **confirmation avant
    suppression physique** (le `// TODO` est levé) ; **`DoctorName` aligné** (facultatif, l'obligation UI était une
    règle sans propriétaire métier) ; **9 validations locales supprimées** (le Domain est propriétaire des règles
    depuis P3-3B — plus de seconde source de vérité) ; **rechargement depuis la source après toute mutation**
    (multi-poste). **Aucune modification Domain/Application/Infrastructure, aucune migration, aucun package.**
- **Réserve honnête sur la « source intangible »** : l'immutabilité **forte** de `Prescription` n'est **toujours pas**
  implémentée (ni versionnement, ni verrouillage après usage, ni FK `SaleItem.PrescriptionId` — le lien passé est
  **irrécupérable**). Les **documents** historiques restent protégés par la **copie par valeur** dans
  `SaleItem`/`OrderItem`, pas par une règle. La suppression **physique** d'une ordonnance existe toujours : elle est
  désormais **confirmée** (P3-3C) mais reste **irréversible**. Dette **documentée**, **non résolue**.
- **Toujours ouvert après P3-3** (rien de tout ceci n'est traité par P3-3C) :
  - **transposition** sphère/cylindre/axe ⇒ **reportée à P3-6B** (cf. §5) ;
  - **concurrence multi-poste** (aucun token : deux postes corrigeant des yeux différents d'une même ordonnance ⇒ la
    première correction est **écrasée en silence**) et **immutabilité forte** ⇒ chantiers **transverses**,
    ADR-PROD-DB-001 — **non résolus** ;
  - **prisme perdu** au pré-remplissage des commandes (`OrderFormViewModel.AutoFillFromPrescription()`) ⇒ **P3-6** ;
  - **refus de vente** pour un client archivé ⇒ **P3-7**.

> **Décision — périmètre backend uniquement à partir de P3-4.** À partir de P3-4, les travaux P3 en cours sont
> backend uniquement : Domain, Application, Infrastructure, persistance, migrations éventuelles, tests et
> documentation. Les changements UI sont reportés au redesign global piloté via Claude Design. Cette décision
> ne modifie pas rétroactivement P3-2C et P3-3C, réellement exécutées avant elle.

### P3-4 — Produits

- **Objectif** : fiabiliser catalogue et cohérence catégorie/détails.
- **Règles candidates** : prix ≥ 0 & stock ≥ 0 (déjà) ; **unicité `Reference`** (documentée « unique » mais
  **non vérifiée** : `ProductValidator` exige seulement non-vide) ; cohérence `Category` ↔ détail associé
  (`GlassDetail`/`LensDetail`/`AccessoryDetail`) ; suppression produit **utilisé** en vente/commande →
  désactivation (`IsActive` existe déjà) plutôt que suppression dure.
- **Hors périmètre** : modèle de prix verres/suppléments avancé, `Money` VO.
- **Fichiers** : `Product`, `ProductValidator`, `Create/Update/DeleteProductUseCase`.
- **Tests** : unicité référence ; cohérence catégorie/détail ; suppression adoucie si historique.
- **Risques** : `DeleteProductUseCase` = cascade EF (peut affecter `SaleItem`/`OrderItem`).
- **Sortie** : catalogue cohérent ; suppression sûre.

#### État (P3-4A — audit terminé)

- **P3-4A audit terminé** — rapport : [`docs/implementation/P3-4A-product-domain-audit-report.md`](../implementation/P3-4A-product-domain-audit-report.md). Verdict : `P3-4A = GO AUDIT`. **P3-4 reste en cours** (aucune règle implémentée).
- Constats factuels (état actuel du code, non corrigé) :
  - **Suppression physique destructive** : `StockMovement` supprimé en **cascade** ; `SaleItem.ProductId` et `OrderItem.ProductId` mis à **null** ; `DeleteProductUseCase` ne vérifie aucun usage.
  - `IsActive` **existe** mais **aucun use case de désactivation** n'existe et `UpdateProductUseCase` ne le mappe pas.
  - **Cohérence catégorie/détails absente** (détails obsolètes conservés lors d'un changement de catégorie).
  - **`ProductValidator` non branché** (unicité `Reference` garantie seulement par l'index unique DB, sans normalisation ni vérification applicative).
- Reports explicites : **écriture directe du stock → P3-5** ; **intégrité fournisseur détaillée → P3-9**.
- Découpage candidat pour la suite (noms non officiels, à valider) : règles métier Produits + migration FK `Restrict` → UI Produits → doc/CI. Détail dans le rapport P3-4A.

#### État (P3-4B — implémenté et validé localement)

- **P3-4A terminé** (audit) et **P3-4B implémenté et validé localement** — rapport :
  [`docs/implementation/P3-4B-product-business-rules-report.md`](../implementation/P3-4B-product-business-rules-report.md).
  Verdict : `P3-4B = GO LOCAL`.
- Livré et prouvé par tests sur bases jetables : normalisation `Reference`/`NormalizedReference`
  (`Trim().ToUpperInvariant()`) + index unique normalisé ; validation Create/Update branchée ; cohérence
  catégorie/détails ; suppression protégée (`BusinessRuleException`) + FK `SaleItem`/`OrderItem`/`StockMovement`
  → **`Restrict`** ; `SetProductActiveUseCase` (idempotent) ; filtrage des sélecteurs vente/commande/stock ;
  migration `AddProductNormalizedReferenceAndProtectHistory` avec **backfill « exact ou échec sûr »** (ASCII
  reproduit exactement `ToUpperInvariant` ; toute référence non-ASCII avorte la migration sans perte ni
  modification, migration non enregistrée).
- **799 tests** verts localement (Domain 297 / Application 263 / App 239), 0 vulnérabilité, aucune migration en
  attente, frontière `Application → Domain` préservée.
- **P3-4 backend terminé** ; CI verte du commit P3-4. L'**UI Produits** est reportée au **redesign global**
  (cf. décision de périmètre ci-dessus) : **P3-4C** n'a pas été créée.
- Reports **inchangés** : **écriture directe du stock → P3-5** ; **règles/intégrité fournisseur → P3-9**.

### P3-5 — Stock / mouvements

- **Objectif** : **unifier la politique de décrément** et la traçabilité.
- **Règles candidates** : tout décrément passe par `IStockMutationService` (jamais négatif) — **corriger**
  `AdvanceOrderStatusUseCase` qui décrémente en **direct** (`Product.StockQuantity -= …`, peut rendre négatif) ;
  éviter le **double décrément** (vente comptoir + fabrication) ; chaque mouvement reste **tracé** ;
  ajustement d'inventaire contrôlé (déjà en valeur absolue).
- **Hors périmètre** : réservation de stock, multi-emplacements.
- **Fichiers** : `StockMovement`, `CreateStockMovementUseCase`, `AdvanceOrderStatusUseCase`, `IStockMutationService`.
- **Tests** (vrai SQLite) : décrément conditionnel ; refus de négatif ; non double-décrément.
- **Risques** : régression du flux fabrication existant. Mitigation : tests avant/après.
- **Sortie** : politique de stock **unique et sûre** sur tous les flux.

#### État (P3-5 — audit terminé, implémentation backend validée localement)

- **P3-5A audit terminé** — rapport : [`docs/implementation/P3-5-stock-and-movements-audit-report.md`](../implementation/P3-5-stock-and-movements-audit-report.md).
- **P3-5 terminé côté backend** — rapport :
  [`docs/implementation/P3-5-stock-and-movements-implementation-report.md`](../implementation/P3-5-stock-and-movements-implementation-report.md).
  CI du commit confirmée verte (SHA `3e7a1ab3a5f7936555b1126c488c748d0dcc0096`, run
  [`29623464755`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29623464755), conclusion `success`).
  Verdict : **`P3-5-CI = GO`**.
- **Contenu** : `IStockMutationService` étendu (incrément atomique + ajustement concurrent-safe) ; convention de
  signe unique (`In` +q / `Out` −q / `Adjustment` delta) ; `AdvanceOrderStatusUseCase` corrigé (décrément sûr,
  prise de statut atomique conditionnelle idempotente, transaction unique) ; `UpdateProductUseCase` ne réécrit plus
  le stock ; non-verres décrémentés à la vente fabrication.
- **819 tests** au moment du commit P3-5 (799 + 20), 0 échec, 0 ignoré.
- **Aucune UI** modifiée. **Aucune migration** (schéma inchangé).
- **Reports maintenus** : matrice complète des statuts Commande → **P3-6** ; validations monétaires / client
  archivé en vente → **P3-7**. Autres reports (PerformedByUserId, liens `SaleId`/`OrderId`, `CHECK` DB, provider
  serveur) détaillés dans le rapport d'implémentation §18.

### P3-6 — Commandes

- **Objectif** : formaliser le **workflow de statut** (aujourd'hui `AdvanceOrderStatusUseCase` accepte
  **n'importe quelle** transition).
- **Règles candidates** : transitions autorisées `New → ToFabricate → InProgress → QualityCheck → Ready →
  Delivered` (interdire les sauts arrière / sauts d'étape non prévus) ; suppression commande selon statut
  (interdire si `Delivered`, p. ex.) ; clarifier le **double rôle** actuel d'`Order` (commande fournisseur
  verres *et* ordre de fabrication atelier — cf. incohérence P3-0 §7).
- **Hors périmètre** : contrôle qualité complet (préparé, finalisé avec P3-6B) ; gestion fournisseur avancée.
- **Fichiers** : `Order`, `OrderStatus`, `AdvanceOrderStatusUseCase`, `Create/Update/DeleteOrderUseCase`.
- **Tests** : matrice de transitions (autorisées/refusées) ; suppression selon statut.
- **Risques** : le workflow réel mélange fournisseur et atelier. Mitigation : décider le sens avant de coder.
- **Sortie** : machine à états explicite et testée.

#### État (P3-6 — audit terminé, implémentation backend validée localement)

- **P3-6 audit terminé** — rapport :
  [`docs/implementation/P3-6-orders-workflow-audit-report.md`](../implementation/P3-6-orders-workflow-audit-report.md).
- **P3-6 implémentation backend validée localement** — rapport :
  [`docs/implementation/P3-6-orders-workflow-implementation-report.md`](../implementation/P3-6-orders-workflow-implementation-report.md).
  - **Matrice linéaire stricte** unique dans le Domain (`OrderStatusPolicy`) :
    `New → ToFabricate → InProgress → QualityCheck → Ready → Delivered` ; sauts, retours arrière, même statut et
    sortie de `Delivered` refusés (`InvalidOrderStatusTransitionException`). La seconde matrice morte
    (`OrderService`) a été **supprimée** (source de vérité unique). La prise atomique multi-poste P3-5 est
    **conservée** (légalité et concurrence = deux gardes complémentaires).
  - **Suppression** limitée au statut `New` ; **modification** bloquée à partir de `InProgress`
    (`BusinessRuleException`).
  - **869 tests** au total (0 échec, 0 ignoré) ; **aucune migration** ; **aucune UI** modifiée.
  - Verdict : `P3-6 = GO LOCAL` (subordonné au vert CI du commit ; **non** marqué définitivement terminé).
- **Reports maintenus** : fiche atelier persistée / QC bloquant / mesures montage → **P3-6B** ; réconciliation
  `SaleStatus` ↔ `OrderStatus` et validations monétaires vente → **P3-7** ; règles générales de notifications
  → **P3-8** ; statut `Cancelled`/archivage, `SaleId` de la création manuelle, pertes optiques du DTO manuel et
  séparation formelle fournisseur/atelier → **redesign métier futur** (non ouverts en P3-6).

### P3-6B — Fiche atelier de montage / technicien

> **Placement volontaire** : *après* P3-6 (la fiche dérive d'une commande) et *avant* P3-7 (elle alimente le
> suivi de vente/fabrication). Détail complet : [notes atelier & transposition](../domain/P3-workshop-sheet-and-transposition-notes.md).

- **Constat** : une **fiche de fabrication existe déjà** mais uniquement en UI éphémère
  (`FabricationSheetViewModel`, vue imprimable construite à la volée depuis `OrderDetailsDto`) — **non
  persistée, non versionnée, sans contrôle qualité, sans transposition, sans mesures porteur/cotes de montage**.
  P3-6B **formalise** ce concept, il ne part pas de zéro.
- **Objectif futur** : générer depuis une commande une **fiche de travaux technicien** (bon d'atelier) :
  imprimable/exportable, **versionnée**, **obsolète** si les données techniques changent, incluant un
  **contrôle qualité** atelier. Ce n'est **ni une facture ni un devis**.
- **Hors périmètre** : prix/acompte/reste à payer sur la fiche (sauf besoin explicite) ; facturation.
- **Blocs de données** : identification commande, client minimal, monture, verres OD/OG, prescription
  originale, **notation atelier si transposition appliquée**, mesures porteur, cotes de montage, calculs de
  décentrement, instructions, alertes, contrôle qualité, version.
- **Règles candidates** : la fiche ne modifie **jamais** l'ordonnance ; peut afficher une correction
  **transposée** pour labo/technicien ; distingue mesures d'examen / mesures porteur / cotes de montage ;
  une fiche validée/imprimée ne se modifie pas (nouvelle **version**) ; QC requis avant `Ready`/`Delivered` ;
  minimisation des données client.
- **Cible technique probable** : entité `WorkshopSheet` versionnée + `GenerateWorkshopSheetUseCase` consommant
  un `OpticalPrescriptionTranspositionService` Domain pur (cf. §5).
- **Tests** : génération, versionnement/obsolescence, QC bloquant, minimisation données.
- **Sortie** : fiche atelier persistée, versionnée, avec QC ; ordonnance source intacte.

### P3-7 — Ventes

- **Objectif** : fiabiliser `RegisterSaleUseCase` (aujourd'hui **sans validation de commande** : il fait
  confiance aux montants fournis).
- **Règles candidates** : `Quantity > 0` par ligne ; `UnitPrice`/`FinalAmount` ≥ 0 ; `Deposit ≥ 0` et
  `Deposit ≤ FinalAmount` ; `RemainingAmount = FinalAmount − Deposit` **recalculé** (ne pas faire confiance) ;
  `FinalAmount = TotalAmount − DiscountAmount` cohérent ; vente atomique avec le stock (déjà via
  `ITransactionRunner`) ; **pas de double décrément** (cf. P3-5) ; clarifier la **double machine à états**
  `SaleStatus` vs `OrderStatus` (aujourd'hui `SaleStatus` n'est jamais avancée après création — cf. P3-0 §7).
- **Hors périmètre** : `Money` VO, TVA, paiements multiples/échéanciers, facturation.
- **Fichiers** : `Sale`, `SaleItem`, `SaleStatus`, `RegisterSaleUseCase`, `SaleFormViewModel`.
- **Tests** (vrai SQLite) : recalculs monétaires ; garde acompte ≤ total ; atomicité vente/stock.
- **Risques** : la VM validait certaines choses ; ne pas régresser. Mitigation : porter la validation en Application.
- **Exigence OBLIGATOIRE héritée de P3-2B** : `RegisterSaleUseCase` **ne vérifie pas** que le client rattaché est
  actif. P3-2B exclut les clients archivés du sélecteur, mais **ne pose aucun garde-fou à l'écriture** : une vente
  peut donc encore être enregistrée pour un client archivé. **P3-7 doit implémenter le refus au moment de
  l'écriture**, dans la transaction, quand `CustomerId` est renseigné et que le client est archivé (`CustomerId`
  reste **nullable** : une vente au comptoir sans client demeure valide). Cf.
  [rapport P3-2B §10](../implementation/P3-2B-customer-archiving-and-deletion-report.md).
- **Sortie** : vente cohérente, atomique, aux montants fiables.

### P3-8 — Notifications métier

- **Objectif** : fiabiliser les notifications stock bas / commande.
- **Règles candidates** : ne pas régénérer une notif « stock bas » **doublonnée** (anti-doublon existe mais
  seulement sur *non lues*) ; **corriger le N+1** (`GenerateLowStockNotificationsUseCase` recharge **toutes**
  les notifications à chaque produit dans la boucle) ; envisager la **résolution** d'une notif quand le stock
  repasse au-dessus du seuil ; « marquer lu » ne supprime pas l'historique.
- **Hors périmètre** : email/push, notifications temps réel.
- **Fichiers** : `Notification`, `GenerateLowStockNotificationsUseCase`, `MarkAllNotificationsReadUseCase`.
- **Tests** : anti-doublon ; pas de N+1 ; résolution au réapprovisionnement (si retenu).
- **Sortie** : notifications non doublonnées, requête efficace.

### P3-9 — Fournisseurs

- **Objectif** : fiabiliser fournisseur et lien produits.
- **Règles candidates** : **interdire la suppression** d'un fournisseur ayant des produits liés
  (`Product.SupplierId` est **non nullable** → suppression dure = risque d'échec FK ou d'orphelins) ou
  archiver ; email valide si renseigné (**absent** : `Supplier` n'a **aucun** validateur, contrairement à `Customer`).
- **Hors périmètre** : commandes fournisseur réelles, catalogue fournisseur.
- **Fichiers** : `Supplier`, (nouveau) `SupplierValidator`, `DeleteSupplierUseCase`.
- **Tests** : suppression refusée avec produits liés ; validation email.
- **Sortie** : intégrité fournisseur ↔ produits garantie.

### P3-10 — Utilisateurs locaux

- **Objectif** : durcir la sécurité locale minimale.
- **Règles candidates** : politique mot de passe déjà présente (`UserValidator.ValidatePasswordPolicy`) —
  garantir qu'elle est **appliquée** partout ; **désactivation** (`SetUserActiveUseCase` existe) plutôt que
  suppression si historique ; pas de compte admin *seed* en production (vérifier `DbInitializer`).
- **Hors périmètre** : SSO, rôles fins, audit trail complet.
- **Fichiers** : `User`, `UserValidator`, `Create/Update/SetUserActiveUseCase`, `DbInitializer`.
- **Tests** : mot de passe faible refusé ; désactivation vs suppression ; seed prod sûr.
- **Sortie** : sécurité locale cohérente.

### P3-11 — Scénarios métier de recette

- **Objectif** : recette **bout-en-bout** de parcours opticien (client → ordonnance → vente verres →
  commande → fabrication → fiche atelier → QC → livraison → encaissement solde).
- **Périmètre** : tests de scénario haut niveau, non-régression métier.
- **Sortie** : parcours nominal + parcours d'erreur couverts.

### P3-12 — Audit final P3

- **Objectif** : audit de clôture (mêmes contrôles que P3-0 + revue des règles introduites), verdict GO merge.

---

## 4. Points durs transverses identifiés en P3-0

Ces sujets traversent plusieurs étapes ; à trancher explicitement quand l'étape concernée les rencontre.

1. **Suppression vs archivage** (P3-2/4/9/10) : aucune entité n'a de garde-fou de suppression ; tout repose
   sur la cascade EF. Décider une politique d'archivage cohérente.
2. **Politique de stock unique** (P3-5/6/7) : `IStockMutationService` est contourné par le flux fabrication.
3. **Double machine à états** (P3-6/7) : `SaleStatus` (dead) vs `OrderStatus` (actif) ; à réconcilier.
4. **Double rôle d'`Order`** (P3-6/6B) : commande fournisseur *et* ordre de fabrication atelier.
5. **Cohérence horloge** (transverse) : mélange `DateTime.Now` / `DateTime.UtcNow` ; une **horloge injectable**
   serait le bon réceptacle (cf. ADR-005 évoqué en P2), mais reste **hors P3** sauf décision explicite.
6. **Validation Application absente** (P3-1) : socle à poser avant les domaines.
7. **Contrainte multi-poste** (P3-5/6/6B/7 — [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md)) :
   production V1 sur base centrale partagée. Les écritures sensibles (stock, commandes, ventes) doivent rester
   atomiques et robustes à la concurrence de plusieurs postes ; le décrément direct de stock
   (`AdvanceOrderStatusUseCase`) est **doublement** à corriger en multi-poste.

---

## 5. Règle optique — Transposition sphère/cylindre/axe (cadrage)

> Détail et exemples : [notes atelier & transposition](../domain/P3-workshop-sheet-and-transposition-notes.md).
> **Ne pas implémenter en P3-0.** Consommée par P3-3 (cohérence) et P3-6B (affichage atelier).

- L'**ordonnance originale est toujours conservée** ; la transposition est une **vue** dérivée
  (technicien/labo), **jamais** un remplacement de la prescription source.
- **Formule** : `sphère' = sphère + cylindre` ; `cylindre' = −cylindre` ; `axe' = axe + 90` ;
  si `axe' > 180` alors `axe' = axe' − 180` ; normaliser `0 → 180` selon la convention retenue.
- **Exemple** : `+2.00 (−1.00) axe 180` ⇔ `+1.00 (+1.00) axe 90`.
- **Cible technique** : `OpticalPrescriptionTranspositionService` (Domain **pur**, sans état, testé
  exhaustivement), utilisé par `GenerateWorkshopSheetUseCase` (P3-6B).
- **Points de vigilance code** : `Sphere`/`Cylinder`/`Axis` existent sur `Prescription`, `SaleItem`,
  `OrderItem` ; l'axe est déjà borné 0–180 mais la cohérence *cylindre nul ⇒ axe absent* n'est pas validée.

---

## 6. Critères d'entrée / sortie P3

**Entrée** (vérifiés P3-0) : P2D mergé sur `main`, working tree propre, build/tests/sécurité/EF verts,
`MMV.Application` pure, garde-fous d'architecture à zéro.

**Sortie P3** (cible P3-12) : règles métier par domaine introduites, testées et documentées ; suppression
sûre partout ; politique de stock unique ; workflow commande explicite ; fiche atelier versionnée avec QC ;
ventes aux montants fiables ; `MMV.Application` toujours pure ; build/tests verts (> total P3-0) ; 0
vulnérabilité ; `has-pending-model-changes = false` (ou migrations P3 assumées et documentées).
