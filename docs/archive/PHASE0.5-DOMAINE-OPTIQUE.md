> **Document historique** — remplacé par les référentiels consolidés de `docs/domain` et `docs/architecture`.
> Ne pas utiliser comme source de vérité actuelle.
>
> *(Archivé en P2A-0B.)*

---

> ⛔ **DOCUMENT REQUALIFIÉ — NE PLUS UTILISER COMME RÉFÉRENTIEL MÉTIER GLOBAL**
>
> **Statut : « Étude exploratoire France — non applicable comme référentiel métier global. »**
>
> Ce document a supposé à tort que **la France** était le marché cible principal (déduit de la langue du code, de `SocialSecurityNumber`/`InsuranceName`/« mutuelle »). Cette hypothèse est **incorrecte** : les marchés réellement ciblés sont la **Belgique** et le **Maroc** (France = marché futur / annexe).
>
> Le référentiel métier de référence est désormais : **[docs/domain/phase-0.5-multicountry.md](../domain/phase-0.5-multicountry.md)** (Phase 0.5B).
>
> Réutilisable depuis ce document : uniquement les **workflows optiques génériques** (parcours d'équipement, atelier, stock) et les **constats techniques vérifiés** sur le dépôt. **À écarter : toutes les règles propres à la France** (100 % Santé, classe A/B, FSE/DRE/NOEMIE/SESAM-Vitale, FINESS, RPPS/ADELI, FEC, validités d'ordonnance françaises).
>
> ---

# PHASE 0.5 — ÉTUDE DU DOMAINE OPTIQUE (Vision métier cible) — ⚠️ EXPLORATOIRE FRANCE

> Étude métier réalisée sous 4 angles : **consultant métier optique**, **directeur de magasin**, **expert ERP**, **expert SaaS B2B**.
> Contexte réglementaire de référence : **France** (Sécurité sociale, mutuelles/OCAM, tiers payant, réforme 100 % Santé), cohérent avec le code MMV (français, `SocialSecurityNumber`, `InsuranceName`, `mutuelle`).
> **Aucune proposition de refactoring technique.** Objectif : construire la **vision métier cible** du produit, puis confronter MMV à ce référentiel.
>
> **Date :** 2026-06-09

---

## SOMMAIRE

1. [Panorama des logiciels d'optique modernes](#1-panorama-des-logiciels-doptique-modernes)
2. [Acteurs & profils d'un magasin d'optique](#2-acteurs--profils-dun-magasin-doptique)
3. [Référentiel métier par domaine (1 à 19)](#3-référentiel-métier-par-domaine)
4. [Confrontation MMV ↔ référentiel](#4-confrontation-mmv--référentiel)
5. [Backlog métier priorisé (P0 → P3)](#5-backlog-métier-priorisé)
6. [Synthèse : vision cible](#6-synthèse--vision-cible)

---

## 1. Panorama des logiciels d'optique modernes

Le marché français de l'édition logicielle optique est mûr et dominé par quelques acteurs. Comprendre leur périmètre = comprendre le « standard attendu » par un opticien.

| Éditeur / Logiciel | Positionnement | Forces caractéristiques |
|---|---|---|
| **Optimum (CIDpro)** | Leader historique desktop | Gestion complète magasin + atelier + tiers payant intégré |
| **MyEasyOptic** | SaaS/web, très répandu | 100 % web, tiers payant, multi-magasins, dématérialisation |
| **Winoptics** | Desktop/réseau | Gestion magasin + EDI fournisseurs |
| **Irisoptic / Octantis / Codir / Orcanta-like** | Divers | CRM, encaissement, statistiques |
| **Acuitis / Krys / Optic 2000 (outils intégrés enseignes)** | Réseaux/franchises | Centrale d'achat, catalogue groupe, reporting consolidé |
| **Plateformes tiers payant** : Almerys, Viamedis, SP Santé, Tp.net, Sévéane, Itelis, Kalixia, Carte Blanche | Connecteurs OCAM | Calcul des droits, DRE, gestion des rejets |

**Standards de fait** qu'un logiciel doit aujourd'hui couvrir pour être vendable :
- **Devis normalisé optique** + double offre **100 % Santé (classe A) / classe B** (obligation légale).
- **Tiers payant** AMO + AMC avec télétransmission (Carte Vitale, FSE/DRE, NOEMIE, SCOR).
- **Mesures techniques** (écart pupillaire, hauteur de montage) et **traçabilité du dispositif médical**.
- **Atelier** (suivi montage/taillage), **SAV / garanties**, **relances**.
- **EDI fournisseurs verriers** (Essilor, Zeiss, Hoya, BBGR…) pour commande/traçabilité.
- **Statistiques & pilotage** (CA, marge, panier moyen, taux de transformation), **multi-magasins**.
- **Conformité RGPD données de santé** (et **hébergement HDS** pour le SaaS).

---

## 2. Acteurs & profils d'un magasin d'optique

| Profil | Rôle métier | Attentes logicielles principales |
|---|---|---|
| **Gérant / Directeur** | Pilotage, marge, achats, RH, conformité | Tableaux de bord CA/marge, stats, multi-magasins, comptabilité/export, gestion des droits |
| **Opticien diplômé (BTS OL)** | Réfraction/examen de vue, adaptation d'ordonnance, vente technique, prise de mesures, conseil | Fiche client/patient, ordonnance, mesures, devis normalisé, commande verres, suivi |
| **Technicien atelier / monteur** | Montage, taillage/meulage des verres sur monture, contrôle qualité, réparations | File d'attente atelier, fiche de fabrication, statuts, SAV, stock atelier |
| **Vendeur / conseiller** | Accueil, vente montures/accessoires/solaires, prise de RDV | Caisse rapide, catalogue, stock, fidélité, encaissement |
| **Administratif / secrétaire tiers payant** | Télétransmission, suivi des remboursements, relances, rejets, facturation | Tiers payant, gestion AMO/AMC, rapprochement NOEMIE, relances impayés, exports compta |

> Conséquence : le logiciel doit être **multi-rôles avec permissions fines** (MMV a déjà 3 rôles : Admin/Opticien/Technicien — il manque au moins **Vendeur** et **Administratif**).

---

## 3. Référentiel métier par domaine

> Pour chaque domaine : **Description · Acteurs · Workflow · Données · Règles métier · Contraintes légales · Fonctionnalités attendues.**

### 3.1 Gestion Client (fiche commerciale)

- **Description :** identité commerciale de l'acheteur (peut différer du porteur/patient : ex. un parent achète pour son enfant).
- **Acteurs :** vendeur, opticien, administratif.
- **Workflow :** création/recherche → coordonnées → historique d'achats → préférences/segmentation → relances marketing.
- **Données :** civilité, nom/prénom, contacts, adresse, consentements (RGPD/marketing), n° client, segment, date de création.
- **Règles métier :** un client ≠ un porteur ; un client peut avoir plusieurs porteurs (foyer) ; déduplication.
- **Contraintes légales :** RGPD (base légale, consentement marketing, droit à l'effacement, durée de conservation).
- **Fonctionnalités attendues :** recherche multi-critères, fusion de doublons, historique 360°, gestion des consentements.

### 3.2 Gestion Patient / Porteur (fiche santé)

- **Description :** la personne **portant** l'équipement ; porte des **données de santé** (ordonnances, examens, paramètres visuels).
- **Acteurs :** opticien (principal).
- **Workflow :** rattachement au client/foyer → historique visuel → ordonnances → équipements → suivi dans le temps.
- **Données :** date de naissance (détermine validité ordonnance et droits), n° de sécurité sociale, régime, médecin traitant/ophtalmo, antécédents visuels.
- **Règles métier :** distinction **client (payeur)** / **patient (porteur)** ; un foyer = N patients ; mineur ⇒ règles spécifiques.
- **Contraintes légales :** **données de santé = données sensibles RGPD** ; conservation et confidentialité renforcées ; **hébergement HDS** si SaaS.
- **Fonctionnalités attendues :** séparation client/patient, historique médical, gestion du foyer, traçabilité des accès.

### 3.3 Gestion des Ordonnances

- **Description :** prescription médicale (ophtalmologiste) autorisant la délivrance d'équipement correcteur.
- **Acteurs :** ophtalmologiste (émetteur), opticien (lecteur/adaptateur).
- **Workflow :** saisie/scan ordonnance → contrôle de validité → adaptation éventuelle par l'opticien → archivage → liaison à l'équipement vendu.
- **Données :** date d'émission, prescripteur (nom + **RPPS/ADELI**), OD/OG (sphère, cylindre, axe, addition, prisme/base), écart, mention « renouvellement / non adaptation ».
- **Règles métier — validité (France) :**
  - **Verres correcteurs :** < 16 ans → **1 an** ; 16–42 ans → **5 ans** ; > 42 ans → **3 ans**.
  - **Lentilles :** < 16 ans → **1 an** ; ≥ 16 ans → **3 ans**.
  - **Adaptation par l'opticien** possible dans la durée de validité (sauf mention contraire / opposition du prescripteur).
- **Contraintes légales :** archivage de l'ordonnance, traçabilité, mentions obligatoires, droit de prescription/adaptation encadré (décret 2016).
- **Fonctionnalités attendues :** **calcul automatique de la date de validité** selon âge et type, alerte d'expiration, scan/PDF, gestion des renouvellements, blocage si ordonnance invalide.

### 3.4 Examens visuels / Réfraction (mesure de vue)

- **Description :** mesure de la réfraction réalisée par l'opticien (dans le cadre légal de l'adaptation/renouvellement), prise de mesures techniques.
- **Acteurs :** opticien diplômé.
- **Workflow :** anamnèse → réfraction subjective/objective → comparaison à l'ordonnance → mesures de centrage (écart pupillaire, hauteur) → préconisation.
- **Données :** acuités, réfraction mesurée OD/OG, **écart pupillaire (PD)**, hauteur de montage, distance verre-œil, angle pantoscopique, galbe.
- **Règles métier :** l'opticien adapte mais ne « prescrit » pas hors cadre ; cohérence mesure/ordonnance.
- **Contraintes légales :** acte d'adaptation tracé ; données de santé.
- **Fonctionnalités attendues :** fiche de réfraction, **paramètres de centrage obligatoires pour la commande de verres**, historique des mesures, connexion éventuelle aux instruments (réfracteur).

### 3.5 Vente de lunettes (équipement optique)

- **Description :** vente de l'équipement monture + verres correcteurs (cœur de métier, ~70-80 % du CA).
- **Acteurs :** opticien (technique), vendeur (montures), administratif (TP).
- **Workflow :** sélection monture → choix verres + traitements → **mesures** → **devis normalisé (100 % Santé / libre)** → calcul prise en charge AMO+AMC → **commande verres fournisseur** → réception → **montage atelier** → contrôle → **livraison + ajustage** → facture + encaissement reste à charge.
- **Données :** monture (réf, taille, coloris), verres (type, indice, traitements, fournisseur), paramètres de centrage, prix détaillé, prise en charge, reste à charge, acompte.
- **Règles métier :** verre = **sur-mesure** (souvent non stocké, commandé) ; prix verre dépend de la **puissance** (grille tarifaire) et des **traitements** ; un équipement = 1 monture + 2 verres.
- **Contraintes légales :** **devis normalisé obligatoire** avec **offre 100 % Santé (classe A, RAC 0)** + offre libre (classe B) ; **TVA 20 %** ; traçabilité DM (marquage CE).
- **Fonctionnalités attendues :** configurateur d'équipement, application des traitements au prix, devis normalisé conforme, gestion acompte/solde, lien vente↔commande↔atelier.

### 3.6 Vente de lentilles de contact

- **Description :** vente d'équipement de contact (renouvellement fréquent, logique d'abonnement possible).
- **Acteurs :** opticien (adaptation), vendeur (renouvellement).
- **Workflow :** ordonnance lentilles → adaptation/essai → choix produit (journalière/mensuelle…) → vente → **renouvellement récurrent**.
- **Données :** marque/modèle, rayon (BC), diamètre, puissance, type, durée de port, quantité/boîte.
- **Règles métier :** validité ordonnance lentilles spécifique (cf. 3.3) ; produit **stocké et consommable** (contrairement aux verres) ; ventes répétitives.
- **Contraintes légales :** prise en charge AMO/AMC selon contrat ; DM.
- **Fonctionnalités attendues :** catalogue lentilles paramétré, **renouvellement/réassort rapide**, abonnement, suivi de consommation, rappels.

### 3.7 Vente d'accessoires & solaires

- **Description :** ventes additionnelles (étuis, produits d'entretien, cordons, solaires non correctrices).
- **Acteurs :** vendeur.
- **Workflow :** vente comptoir rapide → encaissement immédiat → sortie de stock.
- **Données :** produit, prix, quantité, TVA.
- **Règles métier :** **pas d'ordonnance**, **stock décrémenté immédiatement**, vente « caisse ».
- **Contraintes légales :** TVA standard, ticket/facture.
- **Fonctionnalités attendues :** **caisse rapide (POS)**, code-barres, promotions, ventes liées.

### 3.8 Commandes fournisseurs

- **Description :** approvisionnement (montures de réassort en gros, **verres à l'unité sur-mesure**, lentilles, accessoires).
- **Acteurs :** opticien, gérant (achats), administratif.
- **Workflow :** besoin (vente verre / seuil stock) → bon de commande fournisseur → **transmission EDI** → accusé → réception (BL) → contrôle → entrée stock / affectation à la vente → facture fournisseur.
- **Données :** fournisseur, référence, quantité, prix d'achat, délai, n° commande, lien vers vente (pour verres), statut.
- **Règles métier :** verre commandé **rattaché à une vente client** ; réassort déclenché par seuil ; rapprochement BL/facture.
- **Contraintes légales :** traçabilité DM, facture fournisseur (compta).
- **Fonctionnalités attendues :** **EDI verriers** (Essilor/Zeiss/Hoya/BBGR), commandes auto sur seuil, suivi livraison, rapprochement, gestion des reliquats.

### 3.9 Fabrication des verres (commande verrier)

- **Description :** le verre correcteur est **fabriqué sur-mesure** par le verrier selon ordonnance + mesures + monture.
- **Acteurs :** opticien (commande), verrier (fabrication), technicien (réception/montage).
- **Workflow :** transmission des paramètres (puissances, traitements, **forme de la monture / traçage**) → fabrication verrier → expédition → réception → contrôle conformité.
- **Données :** OD/OG complets, traitements, indice, diamètre, **données de forme/traçage**, délai, n° de commande verrier.
- **Règles métier :** paramètres de fabrication = ordonnance + mesures de centrage ; contrôle à réception (puissance, axe).
- **Contraintes légales :** DM sur-mesure, conformité, matériovigilance.
- **Fonctionnalités attendues :** fiche de fabrication, EDI verrier bidirectionnel, suivi délais, contrôle de conformité à réception.

### 3.10 Atelier / Montage

- **Description :** taillage/meulage des verres et montage sur la monture en magasin (ou sous-traité).
- **Acteurs :** technicien atelier.
- **Workflow :** réception verres → **file d'attente atelier** → taillage/montage → **contrôle qualité** (centrage, serrage) → prêt → notification client.
- **Données :** statut (à monter / en cours / contrôle / prêt), opérateur, temps, défauts.
- **Règles métier :** transitions de statut ordonnées ; contrôle qualité obligatoire avant livraison.
- **Contraintes légales :** conformité de l'équipement délivré.
- **Fonctionnalités attendues :** **vue atelier / Kanban**, fiche de fabrication imprimable, suivi des temps, gestion des priorités/urgences.

### 3.11 SAV (Service Après-Vente)

- **Description :** prise en charge post-livraison (inadaptation, gêne, défaut, casse).
- **Acteurs :** opticien, technicien, administratif.
- **Workflow :** réclamation → diagnostic → décision (ajustage / refabrication / échange / geste commercial) → suivi → clôture.
- **Données :** équipement concerné, motif, date, décision, coût, responsable, statut.
- **Règles métier :** **période d'adaptation** (souvent 1–3 mois) ; lien à la garantie ; impact stock/refabrication.
- **Contraintes légales :** garantie légale de conformité (2 ans), obligation de conseil.
- **Fonctionnalités attendues :** ticket SAV lié à la vente, motifs typés, suivi, indicateurs de SAV.

### 3.12 Réparations

- **Description :** réparation d'équipement (changement de branche, plaquette, soudure, re-montage).
- **Acteurs :** technicien.
- **Workflow :** dépôt → devis réparation → accord → réparation → restitution → facturation éventuelle.
- **Données :** équipement, pièces, main d'œuvre, prix, garantie applicable, délai.
- **Règles métier :** sous garantie (gratuit) vs hors garantie (facturé) ; stock de pièces.
- **Contraintes légales :** facture, garantie.
- **Fonctionnalités attendues :** fiche réparation, devis, gestion pièces, distinction garantie/hors garantie.

### 3.13 Garanties

- **Description :** garanties légales et commerciales (casse, adaptation, satisfaction).
- **Acteurs :** opticien, administratif.
- **Workflow :** activation à la vente → suivi de la période → application lors d'un SAV/réparation.
- **Données :** type de garantie, durée, date début/fin, conditions, équipement couvert.
- **Règles métier :** garantie casse souvent 1–2 ans ; adaptation 1–3 mois ; garantie légale 2 ans.
- **Contraintes légales :** **garantie légale de conformité (2 ans)** obligatoire et distincte des garanties commerciales.
- **Fonctionnalités attendues :** suivi automatique des garanties par équipement, alertes d'expiration, application au SAV.

### 3.14 Remboursements (financiers)

- **Description :** avoirs, remboursements client, annulations de vente.
- **Acteurs :** gérant (validation), administratif, vendeur.
- **Workflow :** demande → validation (rôle) → avoir/remboursement → impact caisse + stock + compta.
- **Données :** vente d'origine, montant, motif, mode, validateur, date.
- **Règles métier :** **droit spécial** (validation gérant) ; ré-incrémentation du stock ; régularisation TP.
- **Contraintes légales :** traçabilité comptable, avoir nominatif.
- **Fonctionnalités attendues :** avoirs, remboursement partiel/total, workflow d'autorisation, écritures associées.

### 3.15 Mutuelles / OCAM (complémentaires santé)

- **Description :** organismes complémentaires couvrant le reste après Sécurité sociale.
- **Acteurs :** administratif, opticien.
- **Workflow :** identification mutuelle/contrat → **calcul des droits** (plafonds, grilles, réseau) → application au devis → demande de prise en charge → remboursement.
- **Données :** organisme, n° de contrat/adhérent, **garanties optiques** (plafond monture, forfait verres par classe/correction), réseau de soins, période de droits.
- **Règles métier :** plafonds par poste (monture plafonnée à **100 €** en classe B) ; **renouvellement limité (1 équipement / 2 ans** adulte, sauf exceptions) ; grille **100 % Santé** ; réseaux (tarifs négociés).
- **Contraintes légales :** réforme 100 % Santé, contrats responsables, périodicité de renouvellement.
- **Fonctionnalités attendues :** base mutuelles/contrats, **calcul automatique AMC**, gestion réseaux, contrôle de périodicité.

### 3.16 Tiers payant (AMO + AMC)

- **Description :** dispense d'avance de frais : le magasin se fait payer directement par la Sécu (AMO) et la mutuelle (AMC).
- **Acteurs :** administratif (principal), opticien.
- **Workflow :** lecture **Carte Vitale** → vérification droits AMO → **FSE/DRE** → transmission AMC (plateforme) → suivi **NOEMIE** → rapprochement paiements → **gestion des rejets**.
- **Données :** n° SS, régime AMO, organisme AMC, montants AMO/AMC, n° de lot/FSE, statut télétransmission, rejets.
- **Règles métier :** part AMO (faible en optique depuis réformes) + part AMC ; le **reste à charge** = total − AMO − AMC ; gestion fine des rejets/relances.
- **Contraintes légales :** **SESAM-Vitale**, **SCOR** (numérisation justificatifs), **DRE**, agréments plateformes, conventionnement.
- **Fonctionnalités attendues :** **télétransmission AMO/AMC**, connecteurs plateformes (Almerys, Viamedis, SP Santé…), tableau de bord des rejets, relances, rapprochement bancaire.

### 3.17 Comptabilité

- **Description :** tenue/export comptable, TVA, encaissements, journal de caisse.
- **Acteurs :** gérant, administratif, expert-comptable (externe).
- **Workflow :** encaissements → journal → **export comptable** → TVA → rapprochement TP → clôture.
- **Données :** factures, avoirs, modes de paiement, TVA, écritures, journaux.
- **Règles métier :** **TVA 20 %** ; ventilation par mode de paiement ; rapprochement TP.
- **Contraintes légales :** **logiciel de caisse certifié anti-fraude (loi Finances 2018)** : inaltérabilité, sécurisation, conservation, archivage ; facturation conforme ; **facturation électronique B2B (2026-2027)**.
- **Fonctionnalités attendues :** journal de caisse certifié, export FEC/compta, états TVA, clôtures Z, rapprochement.

### 3.18 Statistiques & Pilotage

- **Description :** indicateurs de performance commerciale et opérationnelle.
- **Acteurs :** gérant/directeur.
- **Workflow :** collecte continue → tableaux de bord → analyse → décisions (achats, marges, équipe).
- **Données :** CA, marge, **panier moyen**, **taux de transformation** (devis→vente), mix produits, performance vendeur, délais atelier, taux SAV, stock dormant.
- **Règles métier :** marge = (PV − PA) ; suivi par magasin/vendeur/période.
- **Contraintes légales :** —
- **Fonctionnalités attendues :** dashboards, comparatifs périodes, **multi-magasins consolidé**, export, alertes.

### 3.19 Inventaire / Stock

- **Description :** gestion du stock magasin (montures, accessoires, lentilles) — les verres étant majoritairement sur-commande.
- **Acteurs :** gérant, vendeur, technicien.
- **Workflow :** entrées (réception) → sorties (ventes) → ajustements → **inventaire physique** → écarts → valorisation.
- **Données :** produit, quantité, seuil d'alerte, emplacement, valorisation (PMP), n° de série/lot.
- **Règles métier :** seuil de réassort ; **distinction stocké (monture/accessoire) vs commandé (verre)** ; inventaire tournant.
- **Contraintes légales :** valorisation comptable du stock, traçabilité DM.
- **Fonctionnalités attendues :** mouvements tracés, alertes seuils, inventaire physique assisté, valorisation, multi-emplacements.

### 3.20 (Transverse) Multi-magasins

- **Description :** réseau de points de vente sous une même enseigne/entité.
- **Acteurs :** directeur réseau, gérants locaux.
- **Workflow :** catalogue/tarifs centralisés → stock par magasin → **transferts inter-magasins** → reporting consolidé.
- **Données :** entité magasin (FINESS, adresse), affectation utilisateurs, stock par site, ventes par site.
- **Règles métier :** **cloisonnement des données par magasin** + vue consolidée gérant ; transferts ; droits par site.
- **Contraintes légales :** FINESS par site, comptabilité par établissement.
- **Fonctionnalités attendues :** entité magasin, filtrage par site, transferts, reporting consolidé — **prérequis du futur SaaS multi-tenant**.

---

## 4. Confrontation MMV ↔ référentiel

Légende : ✅ Présent · 🟡 Incomplet · ❌ Absent · ⚠️ Incorrect / à risque

| # | Domaine | État MMV | Ce qui existe | Ce qui manque / incomplet / incorrect |
|---|---|---|---|---|
| 3.1 | Client | 🟡 | CRUD client, historique ventes, notes | ⚠️ pas de séparation client/patient ; pas de gestion **consentements RGPD** ; pas de foyer ; pas de déduplication |
| 3.2 | Patient/Porteur | ❌ | (confondu avec Client) | Notion de **porteur** absente ; foyer absent ; données santé non isolées |
| 3.3 | Ordonnances | 🟡 | OD/OG complet, médecin, historique, date émission | ❌ **calcul de validité** (âge/type) ; ❌ alerte expiration ; ❌ scan/PDF ; ❌ RPPS prescripteur ; ❌ blocage si invalide |
| 3.4 | Examens / Réfraction | ❌ | — | ❌ fiche réfraction ; ❌ **écart pupillaire / hauteur de montage** (mesures de centrage indispensables à la commande verres) |
| 3.5 | Vente lunettes | 🟡 | Vente monture+verres, split OD/OG, suggestion verres, acompte/reste, 2 modes (comptoir/fabrication) | ❌ **devis normalisé 100 % Santé / classe B** ; ❌ prise en charge AMO/AMC ; ❌ mesures de centrage ; ⚠️ **traitements/suppléments non appliqués au prix** ; ⚠️ n° de vente par `Random` |
| 3.6 | Vente lentilles | 🟡 | Lentilles catalogue (LensDetail), vendables | ❌ **renouvellement/abonnement** ; ❌ adaptation/essai ; ⚠️ même flux que verre (split OD/OG inadapté) |
| 3.7 | Accessoires / solaires | ✅ | Vente comptoir, décrément stock, catégorie SOLAIRE/accessoires | 🟡 pas de **POS/code-barres** dédié ; pas de promotions |
| 3.8 | Commandes fournisseurs | 🟡 | Order = commande verres liée à la vente, statuts, Kanban | ❌ **EDI verriers** ; ❌ réassort auto sur seuil ; ❌ rapprochement BL/facture ; ⚠️ `SupplierId` optionnel non exploité ; seed Orders vide (TODO) |
| 3.9 | Fabrication verres | 🟡 | Paramètres OD/OG portés par OrderItem, fiche de fabrication (vue) | ❌ **données de forme/traçage** ; ❌ EDI verrier ; ❌ contrôle conformité à réception |
| 3.10 | Atelier / Montage | 🟡 | Statuts atelier (New→…→Delivered), Kanban, FabricationSheet | ⚠️ transitions validées dans `OrderService` **non utilisé** (VM écrit en direct) ; ❌ temps/opérateur ; ❌ contrôle qualité tracé |
| 3.11 | SAV | ❌ | — | Aucun ticket SAV, aucun motif, aucun suivi |
| 3.12 | Réparations | ❌ | — | Aucune fiche réparation, pièces, devis |
| 3.13 | Garanties | ❌ | — | Aucune notion de garantie (légale ni commerciale) |
| 3.14 | Remboursements | 🟡 | `PermissionService.CanRefund()` (Admin) | ❌ pas d'**avoir** ni workflow de remboursement réel ; pas d'impact stock/compta |
| 3.15 | Mutuelles / OCAM | ⚠️ | `Customer.InsuranceName` (string libre) | ❌ base mutuelles/contrats ; ❌ **garanties/plafonds** ; ❌ calcul AMC ; ❌ périodicité renouvellement ; ❌ réseaux |
| 3.16 | Tiers payant | ❌ | `SocialSecurityNumber` stocké | ❌ **télétransmission AMO/AMC** ; ❌ FSE/DRE/SCOR/NOEMIE ; ❌ gestion rejets ; ❌ connecteurs plateformes |
| 3.17 | Comptabilité | ❌ | Modes de paiement, montants | ❌ **caisse certifiée anti-fraude** ; ❌ export FEC/compta ; ❌ états TVA ; ❌ clôture Z |
| 3.18 | Statistiques | 🟡 | Dashboard, vue Reports | ❌ KPIs métier (panier moyen, **taux de transformation**, marge réelle, taux SAV) ; ❌ consolidation multi-magasins |
| 3.19 | Inventaire / Stock | ✅ | Mouvements In/Out/Adjustment, seuils, alertes, notifications stock bas | 🟡 ❌ **inventaire physique** assisté ; ❌ valorisation (PMP) ; ❌ multi-emplacements ; verres exclus du stock (OK métier) |
| 3.20 | Multi-magasins | ❌ | — | ❌ aucune entité magasin ; ❌ cloisonnement ; **prérequis SaaS absent** |
| — | Profils/Rôles | 🟡 | Admin / Opticien / Technicien + permissions | ❌ rôles **Vendeur** et **Administratif (TP)** manquants |

**Synthèse de la confrontation :**
- **Solide :** stock, vente comptoir/accessoires, structure ordonnance optique, workflow atelier (façade), permissions de base.
- **Le plus gros manque vendabilité France :** **devis normalisé 100 % Santé**, **tiers payant/mutuelles**, **mesures de centrage**, **validité d'ordonnance**, **caisse certifiée**.
- **Absent total :** SAV, réparations, garanties, examens/réfraction, comptabilité, multi-magasins, séparation patient.
- **Incorrect/à risque :** mutuelle en texte libre, n° vente aléatoire, traitements non facturés, services métier non câblés.

---

## 5. Backlog métier priorisé

> **P0** = indispensable pour **vendre le produit** (utilisable + légalement conforme en magasin France)
> **P1** = important (compétitivité, opérations complètes)
> **P2** = confort (différenciation, productivité)
> **P3** = futur SaaS (scalabilité, réseau)

### P0 — Indispensable pour vendre le produit

| ID | Besoin métier | Domaine | Pourquoi P0 |
|----|---------------|---------|-------------|
| P0-1 | **Devis normalisé optique** avec double offre **100 % Santé (classe A) / classe B** | 3.5 | Obligation légale ; pas de vente lunettes conforme sans lui |
| P0-2 | **Calcul de validité d'ordonnance** (âge + type) + alerte expiration + blocage si invalide | 3.3 | Sécurité juridique de la délivrance |
| P0-3 | **Mesures de centrage** (écart pupillaire, hauteur) obligatoires avant commande verre | 3.4/3.5 | Sans elles, pas de commande verrier réelle |
| P0-4 | **Base mutuelles/contrats** + **calcul de prise en charge AMO/AMC** + reste à charge | 3.15/3.16 | Cœur du parcours d'achat français |
| P0-5 | **Facturation conforme + caisse inaltérable** (loi anti-fraude) + TVA 20 % | 3.17 | Obligation légale logiciel de caisse |
| P0-6 | **Numérotation fiable** ventes/commandes/factures (séquence, pas de `Random`) | 3.5/3.17 | Intégrité légale et comptable |
| P0-7 | **Application des traitements/suppléments au prix** du verre | 3.5 | Prix faux aujourd'hui = bloquant |
| P0-8 | **Séparation Client (payeur) / Patient (porteur)** + données santé isolées | 3.1/3.2 | Base RGPD santé + foyer |
| P0-9 | **Conformité RGPD données de santé** (consentements, durées, accès) | 3.2 | Légal |

### P1 — Important

| ID | Besoin métier | Domaine |
|----|---------------|---------|
| P1-1 | **Télétransmission tiers payant** AMO/AMC (SESAM-Vitale, FSE/DRE, SCOR, NOEMIE) + gestion des rejets | 3.16 |
| P1-2 | **SAV** (tickets, motifs, suivi) + **Garanties** (légale 2 ans + commerciales) | 3.11/3.13 |
| P1-3 | **Réparations** (fiche, pièces, devis, garantie) | 3.12 |
| P1-4 | **Atelier réel** : transitions contrôlées (réhabiliter `OrderService`), contrôle qualité, temps/opérateur | 3.10 |
| P1-5 | **Commandes fournisseurs complètes** : EDI verriers, réassort sur seuil, rapprochement BL/facture | 3.8/3.9 |
| P1-6 | **Remboursements/avoirs** avec workflow d'autorisation + impacts stock/compta | 3.14 |
| P1-7 | **Statistiques métier** : panier moyen, taux de transformation, marge réelle, taux SAV | 3.18 |
| P1-8 | **Renouvellement/abonnement lentilles** + réassort rapide | 3.6 |
| P1-9 | **Rôles Vendeur & Administratif** + permissions associées | 2 |
| P1-10 | **Périodicité de renouvellement** équipement (contrôle 100 % Santé / 2 ans) | 3.15 |

### P2 — Confort

| ID | Besoin métier | Domaine |
|----|---------------|---------|
| P2-1 | **Examen de vue / réfraction** intégré + historique mesures (+ connexion instruments) | 3.4 |
| P2-2 | **Prise de rendez-vous** (examens, livraisons, SAV) | transverse |
| P2-3 | **POS rapide** (code-barres, promotions, ventes liées) | 3.7 |
| P2-4 | **Scan/archivage ordonnance** (PDF) + signature électronique devis | 3.3 |
| P2-5 | **CRM marketing / fidélité** (segmentation, relances, anniversaires renouvellement) | 3.1 |
| P2-6 | **Inventaire physique assisté** + valorisation (PMP) + multi-emplacements | 3.19 |
| P2-7 | **Export comptable FEC** + rapprochement bancaire TP | 3.17 |

### P3 — Futur SaaS

| ID | Besoin métier | Domaine |
|----|---------------|---------|
| P3-1 | **Entité Magasin** + cloisonnement des données par site (multi-établissement) | 3.20 |
| P3-2 | **Multi-tenant** (isolation par enseigne/organisation) | 3.20 |
| P3-3 | **Hébergement HDS** (données de santé) | 3.2 |
| P3-4 | **Reporting consolidé réseau** + transferts inter-magasins | 3.18/3.20 |
| P3-5 | **Catalogue & tarifs centralisés** (centrale d'achat / franchise) | 3.8 |
| P3-6 | **API ouverte** (connecteurs plateformes TP, verriers, instruments) | transverse |
| P3-7 | **Facturation électronique B2B** (réforme 2026-2027) | 3.17 |

---

## 6. Synthèse — Vision cible

MMV couvre aujourd'hui un **socle « gestion de magasin » générique** (clients, catalogue, stock, vente, atelier en façade) mais lui manquent les **piliers spécifiques de l'optique française** qui conditionnent sa **vendabilité réelle** :

1. **Conformité réglementaire de la vente** : devis normalisé 100 % Santé, validité d'ordonnance, caisse certifiée.
2. **Chaîne de remboursement** : mutuelles structurées, calcul AMO/AMC, tiers payant.
3. **Réalité technique optique** : mesures de centrage, traitements facturés, fabrication/commande verrier.
4. **Cycle de vie de l'équipement** : SAV, garanties, réparations.

La **vision cible** se lit en 4 paliers :
- **Palier 1 (P0) — « Vendable en France »** : un opticien peut réaliser une vente conforme de A à Z (ordonnance valide → devis normalisé → mutuelle → facture certifiée).
- **Palier 2 (P1) — « Magasin complet »** : tiers payant télétransmis, SAV/garanties/réparations, atelier et achats industrialisés, pilotage.
- **Palier 3 (P2) — « Différenciant »** : réfraction, RDV, CRM/fidélité, POS avancé, dématérialisation.
- **Palier 4 (P3) — « SaaS B2B réseau »** : multi-magasins, multi-tenant, HDS, consolidation, API.

> Cette vision métier servira de **référentiel cible** pour l'audit technique de la Phase 1 : chaque écart P0/P1 deviendra un critère d'évaluation de l'architecture (capacité du modèle actuel à accueillir devis normalisé, tiers payant, multi-magasins, etc.).

---

*Fin de la Phase 0.5 — Étude du domaine optique. Aucun refactoring proposé. Prochaine étape possible : Phase 1 (audit technique) évalué à l'aune de ce référentiel métier.*
