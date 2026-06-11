# Backlog métier consolidé par marché — MMV

> **Version Phase 0.5D.** Le P0 contient des capacités nécessaires au produit ou des obligations confirmées. Les règles non confirmées sont des **validation gates**, pas des valeurs par défaut.

## A — Fondations communes, avant tout développement national profond

| ID | Capacité | Résultat attendu |
|---|---|---|
| A-01 | Organisation / enseigne | Racine de propriété et d'isolation des données. |
| A-02 | Magasin / établissement | Rattachement à une organisation, un pays, un fuseau et une configuration. |
| A-03 | Pays d'exploitation et période d'effet | Les règles nationales sont choisies et versionnées par contexte. |
| A-04 | Multi-devise | EUR et MAD sans devise implicite globale ; arrondis explicites. |
| A-05 | Localisation et RTL | Ressources traduisibles, formats locaux, documents localisés et support RTL. |
| A-06 | Temps et fuseaux | Stockage cohérent, date métier locale, horloge injectable/testable. |
| A-07 | Fiscalité configurable | Taux, exonérations, catégories et dates d'effet, sans taux optique présumé. |
| A-08 | Documents et numérotation | Types de documents, séquences par organisation/magasin/exercice, unicité et audit. |
| A-09 | Client / porteur / payeur | Personnes et responsabilités distinctes ; foyer et mineurs possibles. |
| A-10 | Politiques nationales extensibles | Capacité à appliquer des règles par pays/date sans dupliquer le cœur. La forme technique sera décidée en Phase 1. |
| A-11 | Sécurité, audit et protection des données | Permissions fines, journal d'accès, minimisation, rétention et export des preuves. |
| A-12 | Isolation organisation/magasin | Toutes les données opérationnelles sont correctement cloisonnées. |
| A-13 | Organismes, régimes et droits | Modèle neutre pour mutualité, AMO, complémentaire, bénéficiaire et périodes de droits. |
| A-14 | Barèmes versionnés | Import/configuration de barèmes datés, avec provenance et statut de validation. |

## B — Lancement Belgique

### P0 Belgique

| ID | Capacité |
|---|---|
| BE-P0-01 | Vente et facture/ticket B2C en EUR avec moteur fiscal configurable et numérotation fiable. |
| BE-P0-02 | Mutualité et régime structurés, distincts du client et du porteur. |
| BE-P0-03 | Prescription, délivrance et attestation de délivrance liées et traçables. |
| BE-P0-04 | Identifiants et statuts de l'opticien : agrément, inscription et conventionnement avec périodes d'effet. |
| BE-P0-05 | Règles INAMI confirmées : éligibilité, fréquence 2/5 ans et changement ≥0,5 D, versionnées et séparées de la validité de prescription. |
| BE-P0-06 | Barèmes INAMI 2026 importables/configurables, jamais codés dans le cœur. |
| BE-P0-07 | Mesures de centrage et traitements correctement inclus dans le prix et la commande. |
| BE-P0-08 | Périmètre linguistique de lancement décidé ; socle multilingue déjà opérationnel. |
| BE-P0-09 | Pour la facturation B2B et les achats : intégration native ou interopérabilité documentée permettant d'émettre/recevoir les factures structurées obligatoires. |

### P1 Belgique

SAV, réparations, garanties, atelier complet, commandes fournisseurs/verriers, rapprochement des factures fournisseurs, tableaux de bord métier, rôles Vendeur/Administratif, gestion avancée des conventions et compléments des mutualités.

### P2 Belgique

Expérience NL complète, éventuel DE selon marché, CRM/fidélité, rendez-vous, réfraction, automatisations documentaires et intégrations d'instruments.

## C — Lancement Maroc

### P0 Maroc

| ID | Capacité |
|---|---|
| MA-P0-01 | Vente et documents en MAD, numérotation fiable, moteur fiscal configurable sans taux optique par défaut. |
| MA-P0-02 | Modèles de facture paramétrables intégrant l'ICE lorsqu'il est applicable et les autres mentions selon profil validé. |
| MA-P0-03 | Organismes et régimes AMO structurés avec périodes d'effet, sans figer CNOPS/CNSS hors calendrier légal. |
| MA-P0-04 | Dossier de remboursement générique : pièces configurables, statut, dates, organisme et historique ; aucune liste de pièces ni montant supposés. |
| MA-P0-05 | Mesures de centrage et traitements correctement inclus dans le prix et la commande. |
| MA-P0-06 | Protection des données by design : rôles juridiques, sécurité, sous-traitance, localisation et transferts documentés. |
| MA-P0-07 | Socle FR/AR-RTL opérationnel ; périmètre exact du lancement décidé. |

### P1 Maroc

Paramétrage des règles AMO après validation officielle, SAV/réparations/garanties, atelier complet, commandes fournisseurs, complémentaires privées, statistiques et rôles spécialisés.

### P2 Maroc

Recherche latin/arabe, documents bilingues avancés, CRM, rendez-vous, réfraction, POS avancé et facturation électronique lorsque son périmètre officiel est confirmé.

## D — France future

Devis normalisé et 100 % Santé, AMO/AMC et télétransmission, identifiants français, règles de prescription françaises, caisse/fiscalité françaises, HDS et facturation électronique française. Aucun de ces éléments ne doit contaminer le cœur Belgique/Maroc.

## Validation gates avant commercialisation

| ID | Sujet | Autorité / validation | Décision temporaire | Bloque la Phase 1 ? | Bloque la commercialisation ? |
|---|---|---|---|---|---|
| G-TVA-BE | TVA par catégorie optique belge | SPF Finances / fiscaliste belge | Catégories et taux configurables, aucun mapping par défaut | Non | Oui pour une facturation réelle non validée |
| G-PRESC-BE | Validité juridique d'une prescription belge | Autorité belge / expert métier | Aucune durée par défaut | Non | Selon parcours retenu |
| G-EINV-BE | Périmètre B2B exact couvert par MMV | Analyse du modèle commercial et des flux | Interopérabilité structurée prévue ; activation selon périmètre | Non | Oui si MMV émet/reçoit des factures B2B dans le champ |
| G-TVA-MA | TVA par catégorie optique marocaine | DGI / fiscaliste marocain | Aucun mapping par défaut | Non | Oui |
| G-FACT-MA | Mentions exactes par type de facture et contribuable | DGI / expert-comptable | Modèle paramétrable | Non | Oui |
| G-R-MA | Montants, périodicités, éligibilité et pièces du remboursement optique | CNSS / ANAM / régime concerné | Aucun montant, délai ou pièce par défaut | Non | Oui pour le module de remboursement |
| G-PRESC-MA | Validité et exigences de prescription | Ministère / expert local | Aucune durée par défaut | Non | Selon parcours retenu |
| G-CNDP | Formalités et transferts selon les rôles juridiques | CNDP / juriste local | Privacy by design, aucun engagement d'hébergement non validé | Non | Oui pour déploiement réel |
| G-EINV-MA | E-facture : texte d'application, calendrier et périmètre | DGI | Extensibilité uniquement | Non | Quand le texte devient applicable |
| L-1 | Langues du premier lancement Belgique/Maroc | Décision produit + validation documentaire | Socle i18n/RTL, activation explicite par marché | Non | Non, si périmètre explicite |

## Séquencement recommandé

1. Auditer les fondations A en Phase 1.
2. Décider l'architecture et la stratégie de migration sans implémenter de règles non confirmées.
3. Lever les gates en parallèle avec des experts locaux.
4. Implémenter par incréments verticaux, Belgique et Maroc partageant le même cœur.
