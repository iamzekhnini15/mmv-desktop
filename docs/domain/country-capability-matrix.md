# Matrice de capacités multi-pays — MMV

> **Version Phase 0.5D.** Les références au dépôt ci-dessous proviennent de l'audit documentaire précédent. Le code source n'était pas inclus dans l'archive reçue pour cette correction : la Phase 1 doit **revérifier chaque chemin, symbole et numéro de ligne** avant de s'en servir comme preuve définitive.

Statuts : ✅ réutilisable ; 🟡 incomplet ; ⚠️ hypothèse incorrecte ; ❌ absent ; ❓ à revérifier.

## 1. Observations du dépôt à revérifier en Phase 1

| Capacité | Observation antérieure | Preuve rapportée | Statut | Risque Belgique | Risque Maroc | Priorité Phase 1 |
|---|---|---|---|---|---|---|
| Devise | `Money` aurait une valeur par défaut EUR | `MMV.Domain/ValueObjects/Money.cs` | ⚠️ | Faible à court terme | Bloquant pour MAD | Critique |
| Identifiant national | `Customer.SocialSecurityNumber` unique | `Customer.cs` | ⚠️ | RRN mal typé | CIN/CNSS confondus | Critique |
| Organisme de couverture | `Customer.InsuranceName` texte libre | `Customer.cs` | 🟡 | Mutualité non structurée | AMO/régime non structuré | Critique |
| Adresse | Pas de pays explicite | `Customer` / configuration EF | 🟡 | Validation locale limitée | Validation locale limitée | Haute |
| Fiscalité | Aucune abstraction TVA identifiée | Recherche antérieure | ❓ | Facture incomplète | Facture incomplète | Critique |
| Facture | Aucune entité/document structuré identifié | Recherche antérieure | ❓ | B2C/B2B non séparés | Mentions non configurables | Critique |
| Numérotation | `Random` dans le flux de vente/commande | `SaleFormViewModel.cs` | ⚠️ | Risque collision/audit | Risque collision/audit | Critique |
| Temps | `DateTime.Now` et `UtcNow` mélangés | Plusieurs fichiers | ⚠️ | Fuseau Bruxelles | Fuseau Casablanca et changements réglementaires | Haute |
| Localisation | Textes français codés en dur | Vues Avalonia | ❌ | NL/DE impossibles | AR/RTL impossible | Critique |
| Organisation / magasin / pays | Concepts non trouvés dans le domaine | Recherche antérieure | ❌ | Pas de multi-site | Pas de multi-site | Critique |
| Isolation des données | Aucun identifiant tenant/magasin identifié | DbContext / entités | ❓ | Risque de mélange | Risque de mélange + CNDP | Critique |
| Logique métier | Enregistrement de vente/stock dans le ViewModel | `SaleFormViewModel.ExecuteSave` | ⚠️ | Règles pays difficiles à tester | Idem | Critique |
| Services de domaine | Services présents mais non câblés dans l'UI | Recherche antérieure | 🟡 | Règles contournées | Règles contournées | Haute |
| Prescription OD/OG | Modèle optique déjà riche | `Prescription.cs` | ✅ | Réutilisable | Réutilisable | Vérifier |
| Stock | Mouvements In/Out/Adjustment | `StockMovement.cs` | ✅/🟡 | Réutilisable | Réutilisable | Vérifier |

## 2. Matrice cible

| Capacité | Cœur commun | Belgique | Maroc | MMV actuel présumé | Priorité |
|---|---|---|---|---|---|
| Organisation, magasin, pays | Oui | BE | MA | ❌ | Fondation A |
| Devise et arrondis | Oui | EUR | MAD | ⚠️ EUR implicite | Fondation A |
| Localisation et RTL | Oui | FR/NL, DE selon marché | FR/AR-RTL selon marché | ❌ | Fondation A |
| Temps et règles datées | Oui | Europe/Brussels | Africa/Casablanca | ⚠️ | Fondation A |
| Client, porteur, payeur | Oui | Oui | Oui | ❌/🟡 | Fondation A |
| Organisme, régime, droits | Oui | Mutualité/INAMI | AMO/CNSS/CNOPS selon période | 🟡 texte libre | Fondation A/P0 |
| Prescription | Oui | Exigences BE | Exigences MA | ✅ partiel | P0 + gates |
| Validité de prescription | Politique datée | Gate BE | Gate MA | ❌ | Gate, non bloquant audit |
| Éligibilité remboursement | Politique datée | INAMI versionné | Gate MA | ❌ | P0 BE / gate MA |
| Fréquence remboursement | Politique datée | 2/5 ans, versionnée | Gate MA | ❌ | P0 BE / gate MA |
| Changement de correction | Politique datée | ≥0,5 D | Gate MA | ❌ | P0 BE |
| Barèmes | Données versionnées | PDF INAMI 2026 et suivants | TNR/régime à valider | ❌ | P0 BE / gate MA |
| Mesures de centrage | Oui | Oui | Oui | ❌ présumé | P0 |
| Prix et traitements | Oui | Oui | Oui | ⚠️ | P0 |
| Facture B2C | Modèle commun + profil national | TVA/mentions BE | ICE/mentions MA | ❌ présumé | P0 |
| Facture B2B structurée | Extension interopérable | Obligatoire dans son champ | Gate future | ❌ | P0 BE via natif ou intégration |
| Documents remboursement | Modèles configurables | Attestation de délivrance | Gate pièces MA | ❌ | P0/gate |
| Atelier, livraison, SAV | Oui | Oui | Oui | 🟡/❌ | P1 |
| Sécurité et audit | Oui | RGPD/APD | Loi 09-08/CNDP | ❌ présumé | Fondation A |
| Transferts de données | Politique déploiement | RGPD | Art. 43–44 | ❓ | Gate déploiement |
| Multi-magasins / multi-tenant | Oui | Oui | Oui | ❌ | Fondation A |

## 3. Questions que la Phase 1 doit trancher

1. Où se situe aujourd'hui la logique métier réelle : domaine, services, ViewModels ou persistence ?
2. Les dépendances entre projets respectent-elles une direction maîtrisée ?
3. Quel mécanisme permettrait des politiques nationales datées et testables sans dupliquer l'application ?
4. La stratégie multi-tenant doit-elle être base partagée, base par organisation ou autre, au regard du produit desktop/SaaS ?
5. Comment migrer les données mono-pays existantes sans inventer le pays, la devise ou l'identifiant ?
6. Quels éléments peuvent être refactorés incrémentalement sans casser les workflows actuels ?
7. Quelles intégrations externes doivent rester hors du cœur : Peppol, INAMI, verriers, AMO, comptabilité ?
