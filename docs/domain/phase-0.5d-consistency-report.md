# PHASE 0.5D — RAPPORT DE COHÉRENCE ET DE CLÔTURE

> **Date : 9 juin 2026.** Contrôle documentaire réalisé sur les six fichiers Markdown transmis. L'archive ne contenait pas le dépôt source MMV ; les preuves de code doivent être revérifiées en Phase 1.

## 1. Verdict

- **GO : Phase 1 — audit technique sans modification de code.**
- **NO-GO : déclaration de conformité ou commercialisation par pays avant levée des gates concernées.**

Ce verdict ne constitue pas un avis juridique.

---

## 2. Contradictions corrigées

| Sujet | Ancien problème | Correction Phase 0.5D |
|---|---|---|
| Loi 54.23 | Chronologie incohérente | BO n° 7478 du 29/01/2026 ; promulgation 22/01/2026 ; entrée en vigueur calculée au 01/02/2027 selon art. 19 |
| Prescription BE | Périodicité INAMI assimilée à une validité | Séparation stricte prescription / éligibilité / périodicité / changement de correction |
| Remboursement MA | Montants et périodes approximatifs utilisés dans le P0 | Retirés ; gate `G-R-MA`, aucun défaut |
| Peppol | Présenté comme facture imprimée ou obligation générale | B2B structuré séparé du B2C, du PDF lisible et de la réception fournisseur |
| Tarifs INAMI | Version 2026 considérée indisponible | PDF officiel applicable au 01/01/2026 intégré au registre |
| TVA | Taux normal assimilé au taux des catégories optiques | Deux niveaux distincts ; gates `G-TVA-BE` et `G-TVA-MA` |
| CNDP | Article 23 présenté comme base d'autorisation | Articles 21/22 pour les régimes, 23/24 pour sécurité, 43/44 pour transferts |
| Transferts MA | Formulation ambiguë concernant l'UE | Tout hébergement hors Maroc est traité comme transfert international à analyser |
| Langues | Multilinguisme présenté comme obligation générale | Socle de localisation = fondation ; langues activées = décision produit/contextuelle |
| Country packs | Solution technique presque imposée | Besoin métier conservé, architecture laissée ouverte à la Phase 1 |
| Rapport 0.5C | `GO` trop affirmatif malgré des restes | Requalifié comme historique ; décision transférée au présent rapport |

---

## 3. Sources officielles consolidées

| Pays | Sujet | Source |
|---|---|---|
| Belgique | Portée B2B de l'e-facturation au 01/01/2026 | Portail officiel `efacture.belgium.be` |
| Belgique | Facture structurée, Peppol-BIS, alternative EN 16931 et PDF | Portail officiel `efacture.belgium.be` |
| Belgique | Éligibilité, périodicité et changement de correction | INAMI/RIZIV |
| Belgique | Tarifs opticiens applicables au 01/01/2026 | PDF INAMI `tarif_opticiens_20260101.pdf` |
| Belgique | Taux normal de TVA | Portail officiel Belgium.be / SPF Finances |
| Maroc | Données sensibles, sécurité et transferts | Loi 09-08 publiée par la CNDP |
| Maroc | Transition CNOPS/CNSS | Loi 54.23, BO n° 7478 du 29/01/2026 |
| Maroc | Taux prévus par le CGI 2026 | DGI / Ministère des Finances, CGI 2026 |

Le détail, les états de fiabilité et les impacts figurent dans [regulatory-source-register.md](regulatory-source-register.md).

---

## 4. Validation gates synchronisées

Les identifiants de référence sont :

- `G-TVA-BE`
- `G-PRESC-BE`
- `G-EINV-BE`
- `G-TVA-MA`
- `G-R-MA`
- `G-CNDP`
- `G-FACT-MA`
- `G-EINV-MA`
- `G-PRESC-MA`
- `L-1`

L'ancienne gate relative à la récupération des tarifs INAMI 2026 est supprimée : le document officiel est désormais intégré.

---

## 5. Contrôles textuels

Une recherche globale a été prévue sur les formulations obsolètes suivantes :

- ancienne date de la loi 54.23 ;
- ancienne périodicité marocaine présentée comme certaine ;
- confusion entre changement de correction et validité ;
- Peppol décrit comme document imprimé ;
- indication que les tarifs INAMI 2026 seraient indisponibles ;
- article 23 présenté comme fondement d'autorisation ;
- ancienne formulation ambiguë laissant croire que l’Union européenne n’était pas un transfert international ;
- anciens seuils belges non conformes à la page INAMI actuelle.

Les seules occurrences éventuellement conservées dans ce rapport sont des descriptions historiques explicites d'erreurs corrigées. Elles ne constituent pas des règles applicables.

---

## 6. Limites de la vérification

1. L'archive reçue contenait uniquement des documents Markdown, pas le dépôt source MMV.
2. Les chemins, classes, propriétés et lignes rapportés par les versions précédentes ne sont donc pas certifiés dans cette correction.
3. La Phase 1 doit rechercher chaque symbole dans le dépôt actuel et produire sa propre preuve.
4. Les documents officiels ne remplacent pas une validation par un fiscaliste, un juriste ou l'autorité compétente pour le cas concret du produit.
5. Les traductions arabes et néerlandaises restent soumises à validation native et métier.

---

## 7. Fichiers consolidés

- `phase-0.5-multicountry.md`
- `regulatory-source-register.md`
- `country-capability-matrix.md`
- `backlog-by-market.md`
- `glossary-multicountry.md`
- `phase-0.5c-validation-report.md` — historique
- `phase-0.5d-consistency-report.md` — présent rapport
- `phase-1-technical-audit-prompt.md` — instruction de démarrage de Phase 1

Aucun fichier applicatif n'a été modifié.

---

## 8. Critères du GO Phase 1

Le GO est accordé parce que :

- aucune valeur marocaine non confirmée n'est une constante par défaut ;
- prescription et remboursement sont séparés ;
- le résumé et le backlog utilisent les mêmes gates ;
- Peppol est correctement séparé du PDF et du B2C ;
- les tarifs INAMI 2026 sont référencés ;
- les statuts fiscaux sont cohérents ;
- les choix d'architecture nationale restent ouverts ;
- l'incertitude restante est paramétrable et n'empêche pas l'audit.

La Phase 1 reste strictement un audit. Toute mise en œuvre attendra la validation de ses livrables.
