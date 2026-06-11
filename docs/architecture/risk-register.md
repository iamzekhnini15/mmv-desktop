# Registre des risques — MMV (Phase 1)

> Sévérités : `BLOCKER` (empêche une évolution sûre / compromet intégrité ou sécurité), `CRITICAL` (risque majeur produit/données), `HIGH` (dette structurante), `MEDIUM` (amélioration importante), `LOW` (qualité/confort).
> Effort relatif : `S` < `M` < `L` < `XL`. Probabilité/Impact : Faible / Moyen / Élevé.
> Toutes les valeurs réglementaires restent des **gates** Phase 0.5D et ne sont jamais codées.

## 1. Tableau des risques

| ID | Sévérité | Domaine | Constat | Preuve | Impact BE | Impact MA | Probabilité | Effort | Dépendances | Recommandation |
|---|---|---|---|---|---|---|---|---|---|---|
| R-01 | BLOCKER *(cible multi-pays/SaaS)* | Multi-tenant | Aucun concept Organisation/Magasin/Pays | recherche globale `OrganizationId/StoreId/TenantId` → 0 dans `src/` | Pas de multi-site ni pays d'exploitation | Idem + cloisonnement CNDP | Élevée | XL | — | Introduire `Organisation`/`Magasin`/`Pays` (ADR-002). *Nuance : non bloquant pour un déploiement réellement mono-organisation ; fondation indispensable pour multi-org/SaaS* |
| R-02 | BLOCKER | Monétaire | Montants stockés en `REAL` (flottant) et **sans devise** | [SaleConfiguration.cs:28-44](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L28-L44), [ProductConfiguration.cs:32-40](../../src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs#L32-L40) | Erreurs d'arrondi sur factures EUR | MAD impossible + erreurs | Élevée | L | R-05 | Représentation décimale **fiable selon provider** + devise explicite (ADR-004) ; **ne pas présumer que des colonnes `decimal` suffisent sous SQLite** |
| R-03 | CRITICAL | Numérotation | `SaleNumber`/`OrderNumber` via `new Random().Next(10000)` face à index UNIQUE | [SaleFormViewModel.cs:835](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L835), [:896](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L896) ; snapshot UNIQUE l.316/630 | Collision → vente refusée ; audit impossible | Idem | Élevée | M | R-01 | Service de séquence transactionnel par org/magasin/exercice (ADR-006) |
| R-04 | BLOCKER | Persistance | `EnsureCreated()` contourne les 7 migrations ; **2 chemins physiques DB** sur 4 sites de config | [DbInitializer.cs:16](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16) ; §V4 dependency-map | Évolution de schéma & migration de données impossibles | Idem | Élevée | M | — | Stratégie de migration **contrôlée** + chemin DB unique (ADR-003) ; **le déclencheur (démarrage/outil/installeur/pipeline) reste à décider, pas imposé** |
| R-05 | CRITICAL | Architecture | Logique métier dans les VM, services de domaine contournés | [SaleFormViewModel.cs:809-980](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L809-L980) ; double DI | Règles BE non testables/centralisables | Règles MA idem | Élevée | L | — | Couche Application/use cases ; interdire l'accès repo depuis les VM (ADR-001) |
| R-06 | CRITICAL | Fiscalité/Facture | Aucune abstraction TVA ni entité Facture (calcul total−remise sans taxe) | [SaleService.cs:63-84](../../src/MMV.Domain/Services/SaleService.cs#L63-L84) ; recherche TVA → 0 | Facture BE incomplète (TVA, B2C/B2B, Peppol) | Facture MA incomplète (ICE, mentions) | Élevée | L | R-02, R-05 | Modèle Facture + moteur fiscal paramétrable daté (ADR-007) ; valeurs = gates |
| R-07 | CRITICAL | i18n/RTL | 606 littéraux FR codés en dur, 0 `.resx`, 0 `FlowDirection` | recherche `.axaml` ; 0 ressource | NL/DE impossibles | AR/RTL impossible | Élevée | L | — | Socle i18n + RTL dès la fondation (ADR-008) |
| R-08 | CRITICAL | Sécurité/accountability | Aucun journal d'audit ni trace d'accès aux données de santé | recherche `audit/journal` → 6 résultats, **tous faux positifs** (« Journalières ») | Lacune d'accountability ; **mesure probablement nécessaire (données de santé RGPD)** — à valider juridiquement | Idem (loi 09-08/CNDP) — à valider | Élevée | L | R-01 | Journal d'audit immuable + accès tracé (ADR-009). *Lacune de sécurité, pas conclusion juridique* |
| R-09 | HIGH | Concurrence | Aucun **concurrency token** (SQLite n'a pas de `rowversion` auto comme SQL Server) ; stock en lecture-modif-écriture | snapshot (0 `IsConcurrencyToken`/`IsRowVersion`) ; [SaleFormViewModel.cs:938-948](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L938-L948) | Mises à jour perdues (stock/finances) | Idem | Moyenne | M | R-04 | Concurrency token applicatif (Guid ou compteur entier) + mise à jour atomique conditionnelle + **gestion `DbUpdateConcurrencyException`** (ADR-010) |
| R-10 | CRITICAL | Tests/CI | Suite rouge sur checkout propre (5 échecs) ; aucune CI | §2.3 audit ; pas de `.github/` | Régressions non détectées | Idem | Élevée | S | — | Réparer tests, ajouter CI build+test obligatoire |
| R-11 | HIGH | Domaine | Entités anémiques + value objects morts | `Money`/`Address`/`Email`/`PhoneNumber` jamais instanciés | Invariants non garantis | Idem | Élevée | L | R-05 | Encapsuler invariants ; brancher les VO ou les retirer |
| R-12 | HIGH | Intégrité/Audit | Suppression en cascade `Customer → Prescriptions` (santé) et `SetNull` sur `Sales` | [CustomerConfiguration.cs:61-69](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs#L61-L69) | Perte de données de santé/financières | Idem | Moyenne | S | R-08 | Suppression logique + rétention ; pas de cascade sur données légales |
| R-13 | HIGH | Sécurité/Authz | Autorisation purement UI, non appliquée au domaine | [PermissionService.cs](../../src/MMV.App/Services/PermissionService.cs) (projet `MMV.App`) | Contrôles contournables en SaaS | Idem + cloisonnement | Moyenne | M | R-01, R-05 | Autorisation au niveau use case + filtrage tenant |
| R-14 | HIGH | Règles nationales | Aucun point d'extension pour règles par pays/date | recherche → 0 | INAMI (éligibilité/périodicité/≥0,5D) non logeable | AMO/régimes datés non logeables | Élevée | L | R-01, R-05 | Mécanisme de politiques datées/versionnées (ADR-001) |
| R-15 | HIGH | Temps | Mélange `DateTime.Now`/`UtcNow`, pas d'horloge injectable | 23 `DateTime.Now` (dont [Notification.cs:48](../../src/MMV.Domain/Entities/Notification.cs#L48)) | Fuseau Europe/Brussels non géré | Africa/Casablanca non géré | Moyenne | M | R-05 | `IClock` injectable, stockage UTC, date métier locale (ADR-005) |
| R-16 | MEDIUM | Données | Seed de démo (50 clients/100 produits/60 ventes) exécuté en production | [App.axaml.cs:158](../../src/MMV.App/App.axaml.cs#L158), [DbInitializer.cs](../../src/MMV.Infrastructure/Data/DbInitializer.cs) | Données factices en base réelle | Idem | Élevée | S | R-04 | Séparer seed minimal prod / jeu de démo conditionnel |
| R-17 | MEDIUM | Sécurité | Mot de passe admin par défaut « admin » seedé (hash partagé) | [DbInitializer.cs:93-101](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L93-L101) | Compte par défaut faible | Idem | Élevée | S | R-16 | Forcer changement au 1er login ; pas de hash partagé |
| R-18 | MEDIUM | Sécurité | `System.Random` (non cryptographique) pour générer des mots de passe | [UserValidator.cs:89](../../src/MMV.Domain/Validators/UserValidator.cs#L89) | Mots de passe prévisibles | Idem | Faible | S | — | `RandomNumberGenerator` |
| R-19 | MEDIUM | Persistance | `HasDefaultValue(DateTime.UtcNow)` = constante figée au build | [SaleConfiguration.cs:26](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L26), [CustomerConfiguration.cs:48-52](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs#L48-L52) | Horodatage erroné | Idem | Moyenne | S | R-04 | `getutcdate`/valeur applicative |
| R-20 | MEDIUM | Perf/Échelle | « Charger toute la table puis filtrer en mémoire » | [BaseRepository.cs:42-47](../../src/MMV.Infrastructure/Repositories/BaseRepository.cs#L42-L47), [SaleFormViewModel.cs:413-519](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L413-L519) | Lenteur multi-magasin | Idem | Moyenne | M | R-01 | Pagination + requêtes filtrées serveur |
| R-21 | LOW | Qualité | Fichiers/vues obsolètes et dupliqués | `ProductDetailView.old.axaml.cs`, `CustomerRepositoryTests.cs.old`, vues `Users` en double | Bruit, confusion | Idem | Élevée | S | — | Nettoyage |
| R-22 | MEDIUM | Build | Pas de `global.json`, versions de test hétérogènes | §2.1 ; §6 dependency-map | Builds non reproductibles | Idem | Moyenne | S | R-10 | `global.json` + `Directory.Packages.props` |
| R-23 | HIGH | Domaine | `UnitOfWork.RollbackAsync` ne fait pas de rollback (dispose) ; transactions inutilisées dans la vente | [UnitOfWork.cs:72-75](../../src/MMV.Infrastructure/Repositories/UnitOfWork.cs#L72-L75) ; §R-05 | Incohérence vente/commande/stock sur panne | Idem | Moyenne | M | R-05 | Transactions explicites par use case |
| R-24 | HIGH | Dépendances | Packages **transitifs** vulnérables **High** | commande `dotnet list ... --vulnerable` (cf. audit §2.5) : `System.Text.Json 8.0.0`, `Microsoft.Extensions.Caching.Memory 8.0.0`, `Tmds.DBus.Protocol 0.20.0`, + tests `System.Net.Http 4.3.0`/`System.Text.RegularExpressions 4.3.0` | Surface d'attaque (sérialisation/cache) | Idem | Moyenne | S | R-10 | Mettre à jour les versions cadres (EF/Avalonia/SDK test) ; intégrer le scan au CI |
| R-25 | HIGH | Livraison | Aucun packaging/installeur, aucune signature, aucun mécanisme de mise à jour ni vérification d'intégrité | `app.manifest`+`WinExe` présents ; 4 occurrences packaging **uniquement en doc** (`ARCHITECTURE.md`/`SPRINTS.md`/`docs/SAAS_LICENSING.md`) | Distribution non maîtrisée | Idem | Moyenne | L | — | Définir packaging signé + mise à jour vérifiée (déploiement) |
| R-26 | MEDIUM | Sécurité/Auth | Pas de limitation des tentatives de connexion ; pas de changement obligatoire du mot de passe initial | `ABSENCE_VÉRIFIÉE` (0 `lockout/throttle` dans `src/`) ; [AuthenticationService.cs:25-47](../../src/MMV.Infrastructure/Services/AuthenticationService.cs#L25-L47) | Brute-force facilité ; compte par défaut persistant | Idem | Moyenne | S | R-17 | Verrouillage/temporisation + rotation forcée du mot de passe initial |

## 2. Dix risques les plus importants (ordre proposé de traitement)

1. **R-10** — Suite de tests rouge + pas de CI *(prérequis qualité, effort S)*.
2. **R-04** — `EnsureCreated`/migrations/chemins DB *(débloque toute évolution de schéma)*.
3. **R-02** — Modèle monétaire (decimal + devise) *(prérequis fiscalité & MAD)*.
4. **R-01** — Fondations Organisation/Magasin/Pays *(prérequis multi-pays & isolation)*.
5. **R-05** — Logique métier hors UI + couche Application *(prérequis règles nationales testables)*.
6. **R-03** — Numérotation transactionnelle *(intégrité documentaire/audit)*.
7. **R-08** — Audit & traçabilité des données de santé *(RGPD/CNDP)*.
8. **R-07** — Socle i18n + RTL *(NL/DE/AR)*.
9. **R-06** — Modèle Facture + moteur fiscal paramétrable *(valeurs = gates)*.
10. **R-09 / R-23** — Concurrence optimiste + transactions correctes *(intégrité financière/stock)*.

## 3. <a id="threat-model"></a>Mini threat model

### Actifs
- Données de santé (ordonnances OD/OG) — catégorie particulière RGPD / sensible loi 09-08.
- Données personnelles & sociales (`SocialSecurityNumber`, `InsuranceName`, coordonnées).
- Données financières (ventes, montants, paiements).
- Secrets : hash de mots de passe.
- Intégrité de la numérotation documentaire (ventes/commandes/futures factures).

### Acteurs
- Utilisateurs légitimes par rôle (Admin / Optician / Technician).
- Futur contexte SaaS multi-organisation : tenants distincts.
- Attaquant local (accès au poste / fichier SQLite).
- Sous-traitant/hébergeur (transferts MA art. 43-44 ; RGPD BE).

### Frontières de confiance
- UI ↔ services/persistance (aujourd'hui poreuse : VM accède aux repos).
- Application ↔ fichier SQLite local (aucun chiffrement au repos).
- **Distinction par modèle de déploiement (Phase 1B)** : pour un **mono-poste / mono-organisation**, la frontière inter-tenant **n'est pas applicable** (un seul locataire) ; pour un **déploiement multi-organisation** et le **futur SaaS**, cette frontière **devient indispensable et est aujourd'hui inexistante**.

### Menaces principales et contrôles attendus
| Menace | État actuel | Contrôle attendu |
|---|---|---|
| Accès non autorisé aux données de santé | authz UI-only, pas d'audit | authz au use case + journal d'accès (R-08, R-13) |
| Fuite inter-tenant | aucun cloisonnement | **(N/A en mono-organisation ; requis dès le multi-org/SaaS)** isolation par org/magasin + filtrage requêtes (R-01) |
| Vol du fichier SQLite | base en clair | chiffrement au repos + politique de sauvegarde |
| Élévation via appel direct service/repo | non contrôlé | autorisation centralisée (R-13) |
| Corruption d'intégrité (numéros, totaux, stock) | `Random`, pas de transaction, pas de concurrency token | séquences transactionnelles + transactions + **concurrency token applicatif** + gestion `DbUpdateConcurrencyException` (R-03, R-09, R-23) |
| Brute-force / compte par défaut | pas de limitation des tentatives ; admin par défaut | verrouillage/temporisation + rotation forcée (R-17, R-26) |
| Dépendances vulnérables | transitifs High (cf. §2.5) | mise à jour cadres + scan CI (R-24) |
| Distribution non maîtrisée | pas de packaging/signature/MAJ | packaging signé + MAJ vérifiée (R-25) |
| Compte par défaut | admin/admin seedé | rotation forcée, pas de hash partagé (R-17) |
| Transfert international (MA) | hébergement non cadré | cartographie des flux, gate `G-CNDP` |

> Le threat model est volontairement orienté « préparation Phase 2 » : il identifie les contrôles à concevoir, sans en implémenter aucun en Phase 1.
