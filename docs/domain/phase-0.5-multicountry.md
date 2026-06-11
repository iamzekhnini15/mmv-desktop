# PHASE 0.5D — RÉFÉRENTIEL MÉTIER OPTIQUE MULTI-PAYS CONSOLIDÉ
## Belgique et Maroc — base documentaire pour la Phase 1

> **Version consolidée : 9 juin 2026.** Marchés principaux : **Belgique** et **Maroc**. La France reste une extension future.
>
> Ce document est un référentiel produit et métier destiné à cadrer l'audit architectural. Il ne constitue ni un avis juridique, ni une certification fiscale, ni une autorisation de commercialisation.
>
> **Aucun code, schéma de base, migration ou test n'a été modifié dans cette phase.**

Documents associés :

- [Registre réglementaire](regulatory-source-register.md)
- [Matrice des capacités](country-capability-matrix.md)
- [Backlog par marché](backlog-by-market.md)
- [Glossaire multi-pays](glossary-multicountry.md)
- [Rapport de cohérence Phase 0.5D](phase-0.5d-consistency-report.md)
- [Prompt Phase 1](phase-1-technical-audit-prompt.md)

---

## 1. Statut et règles de lecture

### 1.1 États de fiabilité

- `CONFIRMED` : établi par une source officielle actuelle.
- `CONDITIONAL` : officiel, mais dépend du rôle, de la date, de la transaction ou du contexte.
- `UNCONFIRMED_VALIDATION_REQUIRED` : non établi avec une précision suffisante ; aucune valeur ne doit être codée par défaut.

### 1.2 Catégories à ne pas mélanger

1. obligation légale ;
2. exigence fiscale ;
3. règle de remboursement ou d'éligibilité ;
4. règle de prescription ;
5. pratique professionnelle ;
6. exigence commerciale ;
7. choix stratégique produit ;
8. information non confirmée.

### 1.3 Portée du GO

Le référentiel est **GO pour la Phase 1, qui est un audit technique sans implémentation**. Il reste **NO-GO pour déclarer MMV juridiquement ou fiscalement conforme à la commercialisation** tant que les validation gates du marché concerné ne sont pas levées.

---

## 2. Résumé exécutif

MMV doit devenir un produit optique B2B utilisable en Belgique et au Maroc sans dupliquer le cœur applicatif. Le futur système doit donc distinguer :

1. un **cœur métier optique commun** ;
2. des règles nationales sélectionnables, datées et versionnées ;
3. des paramètres de magasin et d'organisation ;
4. des exigences réglementaires dont certaines restent soumises à validation locale.

Les notions françaises présentes dans l'ancien cadrage — 100 % Santé, classes A/B, SESAM-Vitale, FSE/DRE, NOEMIE, FINESS, RPPS, ADELI, FEC ou HDS — ne doivent pas structurer le cœur commun. Elles correspondent à une extension France éventuelle.

Le besoin multi-pays ne doit pas être repoussé à une phase lointaine : `Organisation`, `Magasin`, pays d'exploitation, devise, langue, fuseau horaire, politique fiscale, numérotation des documents, séparation des données et journal d'audit sont des **fondations**.

La Phase 1 devra vérifier l'état réel du dépôt. Les anciens chemins, symboles et numéros de ligne fournis par Claude sont des pistes de travail, pas des preuves définitives, car l'archive remise pour cette correction ne contenait que les documents Markdown.

---

## 3. Cœur métier optique commun

Le cœur commun ne doit contenir aucune hypothèse propre à l'INAMI, à l'AMO, à Peppol, à l'ICE ou à un régime français.

| Capacité | Définition neutre | Point d'attention multi-pays |
|---|---|---|
| Organisation | Entité exploitante du SaaS | Isolation des données et paramètres |
| Magasin | Point de vente rattaché à une organisation | Pays, devise, langue, fuseau, fiscalité |
| Client | Acheteur ou interlocuteur commercial | Peut différer du porteur et du payeur |
| Porteur / patient | Personne utilisant l'équipement | Données de santé et historique visuel |
| Payeur | Personne ou organisme réglant tout ou partie | Client, tiers ou organisme |
| Prescription | Document médical et prescripteur | Validité et identifiants nationaux variables |
| Correction OD/OG | Sphère, cylindre, axe, addition, prisme/base | Neutre et historisée |
| Mesures de centrage | Écart pupillaire, hauteur et mesures techniques | Requises selon équipement et fabrication |
| Catalogue | Montures, verres, lentilles, traitements, accessoires | Prix, taxes et langues variables |
| Devis | Proposition commerciale | Mentions et durée de validité configurables |
| Vente | Transaction commerciale | Magasin, devise et règles nationales |
| Facture / avoir | Pièce comptable structurée en données | Rendu humain et échange machine séparés |
| Paiement | Acompte, solde, remboursement | Devise et modes de paiement |
| Organisme de prise en charge | Tiers pouvant rembourser ou payer | Modèle générique, politiques nationales |
| Commande fournisseur | Approvisionnement standard ou sur mesure | Connecteurs et formats variables |
| Atelier | Montage, contrôle qualité et livraison | Workflow commun, documents localisés |
| Stock | Entrées, sorties, réservations, inventaires | Par magasin et emplacement |
| SAV / réparation / garantie | Cycle de vie après livraison | Durées et droits paramétrables |
| Reporting | KPIs commerciaux et opérationnels | Par organisation, magasin, pays et devise |
| Identité et accès | Utilisateurs, rôles, permissions | Cloisonnement et moindre privilège |
| Audit et conservation | Historique des actions et accès | Durées et exigences par juridiction |

### 3.1 Concepts nationaux comme politiques, pas comme cœur

MMV doit pouvoir sélectionner, configurer, versionner et appliquer des règles nationales sans dupliquer le cœur applicatif. Le choix technique — services de politique, stratégies, modules, configuration versionnée ou autre — reste ouvert et sera évalué en Phase 1.

Les notions suivantes sont des **concepts métier candidats**, pas des interfaces ou classes déjà décidées :

- règle de validité de prescription ;
- règle d'éligibilité au remboursement ;
- règle de périodicité de prise en charge ;
- règle de changement de correction ;
- politique fiscale ;
- mentions de facture ;
- politique de numérotation ;
- politique de conservation ;
- politique de langue et de document.

---

## 4. Belgique

### 4.1 Remboursement et délivrance

Les règles INAMI confirmées dans cette étude portent sur **l'intervention financière**, et non sur une durée générale de validité de l'ordonnance.

La page officielle consultée le 9 juin 2026 indique notamment :

- moins de 18 ans : intervention à partir du verre plan ;
- 18 à 64 ans : intervention à partir de ±6,00 dioptries ;
- à partir de 65 ans : ±6,00 dioptries pour les unifocaux et ±4,25 dioptries pour les bifocaux ou progressifs ;
- nouvelle intervention après deux ans pour les moins de 18 ans et après cinq ans à partir de 18 ans ;
- nouveau droit possible en cas de différence d'au moins 0,5 dioptrie dans la sphère, le cylindre ou le prisme.

Ces règles doivent être datées et versionnées. La nomenclature et les tarifs INAMI 2026 constituent les références opérationnelles, pas des constantes dispersées dans le code.

Le parcours de remboursement décrit par l'INAMI comprend une prescription, un opticien agréé et une attestation de délivrance transmise à la mutualité.

### 4.2 Prescription

La validité juridique générale de la prescription optique n'a pas été confirmée par une source officielle suffisamment précise dans cette étude. MMV ne doit donc pas déduire une durée de validité à partir des périodes de remboursement.

Statut : `UNCONFIRMED_VALIDATION_REQUIRED` — gate `G-PRESC-BE`.

### 4.3 Facturation

Depuis le 1er janvier 2026, la facturation électronique structurée s'applique aux opérations entrant dans le champ B2B entre entreprises belges assujetties à la TVA. Les ventes B2C ordinaires ne sont pas incluses dans ce champ.

MMV doit distinguer :

1. facture ou ticket B2C ;
2. facture B2B structurée ;
3. réception des factures fournisseurs structurées ;
4. canal d'échange Peppol ;
5. format Peppol-BIS ou format alternatif conforme autorisé ;
6. représentation PDF ou imprimée destinée à la lecture humaine.

Un PDF n'est pas, à lui seul, une facture électronique structurée lorsqu'une telle facture est obligatoire.

### 4.4 Fiscalité

Le taux normal belge de 21 % est confirmé. Le taux réellement applicable à chaque catégorie optique — monture, verre, lentille, prestation, réparation ou accessoire — n'est pas établi dans ce référentiel.

Aucune catégorie ne doit recevoir un taux optique par défaut avant levée de `G-TVA-BE`.

### 4.5 Données et langues

Le RGPD s'applique, notamment son régime renforcé pour les données de santé. Le produit doit prévoir contrôle d'accès, audit, minimisation, rétention, droits des personnes et encadrement des sous-traitants.

Le socle de localisation est obligatoire. En revanche, l'activation simultanée de FR, NL et DE pour toute l'interface n'est pas présentée ici comme une obligation générale. Il faut distinguer langue du magasin, de l'utilisateur, des documents, des relations de travail et de l'offre commerciale.

---

## 5. Maroc

### 5.1 AMO, CNSS et transition CNOPS

La loi 54.23 a été promulguée le 22 janvier 2026 et publiée au Bulletin officiel n° 7478 du 29 janvier 2026. Son article 19 prévoit une entrée en vigueur après douze mois calculés à partir du premier jour du mois suivant la publication, soit le **1er février 2027** selon ce calcul.

MMV ne doit donc pas figer un seul gestionnaire sans période d'effet. Les organismes, régimes et règles de prise en charge doivent être datés et configurables.

Les montants, périodicités et pièces exactes du remboursement optique marocain ne sont pas confirmés dans ce référentiel par une source officielle suffisamment précise. Aucun montant approximatif ne doit être chargé par défaut.

Statut : `UNCONFIRMED_VALIDATION_REQUIRED` — gate `G-R-MA`.

### 5.2 Données personnelles et données de santé

La loi 09-08 impose une analyse selon la finalité, le responsable de traitement, le sous-traitant et le contexte professionnel :

- article 21 : régime d'autorisation des données sensibles, sous réserve des cas prévus ;
- article 22 : régime particulier de déclaration pour certains traitements de santé répondant à ses conditions ;
- articles 23 et 24 : sécurité, confidentialité et sous-traitance ;
- articles 43 et 44 : transferts vers un État étranger.

Tout hébergement hors du Maroc constitue un transfert international à analyser, y compris vers un État de l'Union européenne.

La formalité CNDP applicable ne peut pas être décidée uniquement à partir du type de donnée. Elle dépend également des rôles juridiques du magasin, de l'enseigne, de l'éditeur SaaS, de l'hébergeur et des professionnels.

Statut : `CONDITIONAL` avec validation juridique locale — gate `G-CNDP`.

### 5.3 Facturation et fiscalité

Le CGI 2026 confirme notamment des taux de TVA de 20 % et 10 %. Il ne confirme pas, dans cette étude, le rattachement de chaque catégorie optique à un taux précis.

Le système doit donc gérer une fiscalité paramétrable par nature d'opération et date. Aucune valeur ne doit être assignée automatiquement à tous les produits optiques avant levée de `G-TVA-MA`.

Les mentions de facture dépendent du profil du vendeur, du client, de l'opération et du contexte B2B/B2C. L'ICE est une donnée importante, mais la liste complète des mentions applicables doit être validée avant commercialisation — gate `G-FACT-MA`.

Le calendrier et le périmètre d'une éventuelle obligation marocaine de facturation électronique ne sont pas établis ici par un texte officiel suffisamment précis — gate `G-EINV-MA`.

### 5.4 Langues et interface

Le produit doit être techniquement localisable en français et en arabe, avec support réel du RTL, des documents bilingues et de la recherche sur les noms en alphabet latin et arabe. Le périmètre exact du premier lancement est une décision produit, pas une obligation juridique affirmée par ce référentiel.

---

## 6. Comparaison synthétique

| Capacité | Belgique | Maroc | Conséquence produit |
|---|---|---|---|
| Devise | EUR | MAD | Devise portée par magasin/document |
| Prise en charge | INAMI/RIZIV et mutualités | AMO, CNSS/CNOPS selon date, complémentaires | Modèle générique + politiques datées |
| Document de remboursement | Attestation de délivrance | Dossier et pièces à valider | Génération de documents par contexte |
| Facturation structurée | B2B belge dans le champ depuis 01/01/2026 | Calendrier non confirmé | Connecteurs découplés du cœur |
| Données de santé | RGPD et droit belge | Loi 09-08 et CNDP | Politiques de conformité par déploiement |
| Langues | FR/NL/DE selon stratégie et contexte | FR/AR selon stratégie et contexte | Localisation et RTL dès les fondations |
| Fiscalité optique | À valider par catégorie | À valider par catégorie | Aucun taux métier universel |
| Prescription | Gate de validation | Gate de validation | Ne jamais confondre avec remboursement |

---

## 7. Confrontation avec MMV

La matrice détaillée figure dans [country-capability-matrix.md](country-capability-matrix.md).

À ce stade documentaire, les principaux risques à vérifier dans le dépôt sont :

- devise EUR codée en dur ;
- identifiant social unique et non typé ;
- assurance ou mutuelle en texte libre ;
- absence d'organisation, magasin, pays, langue et politique fiscale ;
- numérotation aléatoire ou non transactionnelle ;
- textes UI en français et absence de localisation structurée ;
- absence de séparation client, porteur et payeur ;
- logique métier éventuellement écrite directement dans les ViewModels ;
- services métier éventuellement présents mais contournés ;
- absence de journal d'audit et de séparation des données par organisation/magasin ;
- absence de modèle explicite de facture, remboursement, SAV et garantie.

**La Phase 1 doit confirmer chaque observation à partir du dépôt réel.** Toute observation non retrouvée doit être retirée ou reclassée.

---

## 8. Backlog de référence

Le détail priorisé se trouve dans [backlog-by-market.md](backlog-by-market.md).

### 8.1 Fondations communes

- organisation et magasin ;
- pays d'exploitation, devise, langue et fuseau horaire ;
- client, porteur et payeur séparés ;
- types de documents et numérotation fiable ;
- politiques fiscales et nationales versionnées ;
- isolation par organisation et magasin ;
- permissions, audit et conservation ;
- localisation, formats et RTL ;
- gestion temporelle des règles et barèmes.

### 8.2 Lancement Belgique

Le P0 belge couvre le parcours magasin et B2C, la mutualité, l'attestation de délivrance, l'agrément/conventionnement, les grilles INAMI versionnées et la protection des données. La facturation structurée B2B et Peppol sont prioritaires dès lors que MMV émet ou reçoit des factures dans ce périmètre ; leur classement opérationnel dépend du périmètre commercial choisi.

### 8.3 Lancement Maroc

Le P0 marocain couvre le parcours magasin, la facture paramétrable, l'AMO comme capacité configurable, les documents de remboursement, les rôles CNDP, MAD et le socle FR/AR-RTL. Les valeurs de remboursement, la fiscalité optique et les formalités exactes restent bloquées par leurs gates.

### 8.4 France future

La France est une extension future séparée. Ses mécanismes propres ne doivent pas être anticipés dans le cœur au-delà de la capacité générale à ajouter des politiques nationales.

---

## 9. Validation gates avant commercialisation

| Gate | Sujet | Marché | Autorité ou validation attendue | Bloque Phase 1 ? | Bloque commercialisation ? |
|---|---|---|---|---|---|
| `G-TVA-BE` | TVA par catégorie optique | BE | SPF Finances / fiscaliste local | Non | Oui pour facturation réelle |
| `G-PRESC-BE` | Validité de prescription | BE | INAMI/SPF Santé/expert local | Non | Oui pour blocages automatiques |
| `G-EINV-BE` | Périmètre exact des flux B2B de MMV | BE | Analyse du modèle commercial | Non | Selon clientèle B2B |
| `G-TVA-MA` | TVA par catégorie optique | MA | DGI / fiscaliste local | Non | Oui |
| `G-R-MA` | Remboursement optique, montants, périodicité, pièces | MA | CNSS/ANAM/TNR | Non | Oui pour calcul automatisé |
| `G-CNDP` | Formalités selon les rôles et l'hébergement | MA | CNDP / juriste | Non | Oui |
| `G-FACT-MA` | Mentions exactes de facture | MA | DGI / CGI / expert local | Non | Oui |
| `G-EINV-MA` | Calendrier et périmètre e-facture | MA | DGI / texte officiel | Non | Selon entrée en vigueur |
| `G-PRESC-MA` | Validité et conditions de prescription | MA | autorité sanitaire/expert local | Non | Oui pour blocages automatiques |
| `L-1` | Langues du premier lancement | BE/MA | décision produit | Non | Non, mais engage le périmètre |

---

## 10. Décision de clôture

### GO pour la Phase 1

La Phase 1 peut commencer parce que :

- les règles confirmées et non confirmées sont séparées ;
- les valeurs incertaines sont isolées en validation gates ;
- aucune valeur nationale non confirmée n'est nécessaire pour auditer l'architecture ;
- le cœur commun et les différences Belgique/Maroc sont identifiés ;
- le mécanisme technique de règles nationales reste volontairement ouvert.

### Limite du GO

Ce GO autorise uniquement :

- l'inspection du dépôt ;
- le build et les tests ;
- l'audit du modèle, des dépendances, de la persistance, de la sécurité et de la capacité multi-pays ;
- la production de recommandations et d'ADR candidats.

Il n'autorise pas à déclarer MMV conforme, à coder des montants non confirmés, ni à commencer un refactoring avant validation du rapport de Phase 1.
