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
- **Risques** : `DeleteCustomerUseCase` s'appuie aujourd'hui sur la **cascade EF** → suppression d'un client
  peut effacer son historique de ventes. **Point dur à trancher en P3-2.**
- **Sortie** : suppression sûre (archivage ou refus documenté) ; tests verts.

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
- **Sortie** : cohérence croisée validée ; ordonnance source restant intangible.

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
