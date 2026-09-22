# P5 — Product Completion & UI Implementation Roadmap

## Document Information

**Project:** MMV — ManageMyVision
**Phase:** P5
**Objective:** Align complete product design with existing implementation and deliver a fully usable V1 Beta application.

---

# 1. Vision de la phase P5

La phase P5 a pour objectif de transformer MMV d'une application techniquement fonctionnelle en une application professionnelle utilisable par des opticiens pour des tests terrain.

P4 traite la capacité technique du logiciel à fonctionner en environnement multi-poste.

P5 traite :

* l'implémentation complète de l'interface utilisateur ;
* l'alignement entre le design validé et le code existant ;
* les fonctionnalités métier manquantes nécessaires à la V1 ;
* la préparation des premiers tests utilisateurs professionnels.

---

# 2. Objectif de sortie P5

À la fin de P5, MMV doit permettre à un opticien de réaliser un cycle métier complet :

```
Connexion
    ↓
Dashboard / Navigation
    ↓
Création client
    ↓
Gestion rendez-vous
    ↓
Création ordonnance
    ↓
Création vente
    ↓
Commande / Atelier
    ↓
Gestion stock
    ↓
Historique client
```

L'objectif n'est pas encore la sortie commerciale officielle.

Le résultat attendu est :

```
MMV V1 Beta — Professional Testing Ready
```

---

# 3. Entrées nécessaires

P5 utilisera plusieurs sources :

## Code existant

Analyse :

* Domain
* Application
* Infrastructure
* Tests existants
* Architecture actuelle

## Claude Design

Sources :

* Design System
* Login Handoff
* Shell Handoff
* Lot 1
* Lot 2
* Lot 3
* Lot 4
* Lot 5
* Lot 6

## Documentation projet

Sources :

* ADR
* Roadmaps
* rapports d'implémentation
* décisions architecturales

---

# 4. P5-0 — Design vs Code Audit

## Objectif

Comparer intégralement :

```
Claude Design
       +
Code actuel
       +
Besoin métier V1
```

avant toute implémentation.

---

## Livrables

Créer une matrice complète :

| Module       | Design disponible | Backend existant | UI existante | Action |
| ------------ | ----------------- | ---------------- | ------------ | ------ |
| Login        |                   |                  |              |        |
| Shell        |                   |                  |              |        |
| Clients      |                   |                  |              |        |
| Rendez-vous  |                   |                  |              |        |
| Ordonnances  |                   |                  |              |        |
| Ventes       |                   |                  |              |        |
| Atelier      |                   |                  |              |        |
| Stock        |                   |                  |              |        |
| Fournisseurs |                   |                  |              |        |

---

## Classification

Chaque élément sera classé :

### A — Existe déjà

Action :

Implémentation UI / intégration.

### B — Design existe mais fonctionnalité absente

Action :

Création backend + UI.

### C — Fonctionnalité existe mais design absent

Action :

Validation de nécessité V1.

### D — Hors périmètre V1

Action :

Reporter.

---

# 5. P5-1 — Application Foundation UI

## Objectif

Implémenter la fondation commune.

Périmètre :

* Login ;
* gestion session ;
* Shell principal ;
* navigation ;
* sidebar ;
* thème ;
* composants communs ;
* gestion erreurs globales.

---

# 6. P5-2 — Module Clients

Basé sur le design Lot 2.

Fonctionnalités :

* liste clients ;
* recherche ;
* filtres ;
* création ;
* modification ;
* archivage ;
* réactivation ;
* fiche client ;
* historique.

---

# 7. P5-3 — Module Rendez-vous

Fonctionnalités :

* calendrier ;
* vue jour ;
* vue semaine ;
* création rendez-vous ;
* modification ;
* annulation ;
* association client ;
* gestion conflits.

---

# 8. P5-4 — Module Ordonnances

Basé sur le design Lot 3.

Fonctionnalités :

* création ordonnance ;
* modification ;
* consultation ;
* historique ;
* liaison client ;
* liaison vente.

---

# 9. P5-5 — Module Ventes & Paiements

Basé sur le design Lot 4.

Fonctionnalités :

* parcours client vers vente ;
* sélection produits ;
* panier ;
* paiement ;
* acompte ;
* solde ;
* historique ventes.

---

# 10. P5-6 — Module Commandes & Atelier

Basé sur le design Lot 5.

Fonctionnalités :

* commandes ;
* suivi statuts ;
* atelier ;
* tâches ;
* contrôle qualité ;
* historique fabrication.

---

# 11. P5-7 — Catalogue, Stock & Fournisseurs

Basé sur le design Lot 6.

Fonctionnalités :

## Catalogue

* produits ;
* catégories ;
* prix ;
* fournisseurs.

## Stock

* quantité ;
* mouvements ;
* inventaire.

## Fournisseurs

* gestion fournisseurs ;
* association produits.

---

# 12. P5-8 — Intégration métier complète

Objectif :

Valider les workflows complets.

Scénarios :

## Nouveau client

```
Client
 ↓
Ordonnance
 ↓
Vente
```

## Vente complète

```
Client
 ↓
Produit
 ↓
Paiement
 ↓
Commande
 ↓
Retrait
```

## Gestion atelier

```
Commande
 ↓
Production
 ↓
Contrôle qualité
 ↓
Livraison
```

---

# 13. P5-9 — UX & Stabilisation

Objectif :

Préparer les tests professionnels.

Travail :

* cohérence écrans ;
* messages erreurs ;
* chargements ;
* états vides ;
* raccourcis clavier ;
* performances UI.

---

# 14. Critères de sortie P5

P5 sera considéré terminé lorsque :

## Interface

* tous les écrans V1 sont implémentés ;
* navigation complète ;
* design aligné avec Claude Design.

## Fonctionnel

Un opticien peut :

* créer un client ;
* gérer une ordonnance ;
* réaliser une vente ;
* suivre une commande ;
* gérer stock et fournisseurs.

## Technique

* tests verts ;
* aucune régression P4 ;
* architecture respectée.

---

# 15. Phases après P5

P5 ne représente pas la sortie commerciale finale.

Après P5 :

```
P6 — Sécurité & Gestion utilisateurs

P7 — Production Readiness
     Backup
     Restore
     Logs
     Support

P8 — Mise à jour applicative

P9 — Tests terrain professionnels

P10 — Release V1
```

> **Prérequis technique de P10, traité hors de cette roadmap.**
> [ADR-APP-DISTRIBUTION-001](adr-app-distribution-001-installation-and-updates.md) **DI-8** et son obligation
> **OI-10** imposent que **la Release V1 soit construite sur .NET 10 (LTS)** : aucune Release V1 sur .NET 8, dont
> le support s'achève le **10 novembre 2026**. En publication autonome (DI-1), le runtime est **embarqué** dans le
> paquet livré au client — un runtime hors support serait livré avec ses failles connues.
>
> Ce travail est porté par le lot transverse **`P4-NET10`**, inscrit dans la
> [roadmap P4](P4-multi-poste-roadmap.md) et détaillé par le
> [plan d'exécution .NET 10](net10-migration-execution-plan.md). Il est **sans dépendance** : il n'attend ni P5,
> ni P8. **P8 et P10 en héritent** — ils ne le refont pas.
>
> **P8 — Mise à jour applicative** met par ailleurs en œuvre les neuf décisions d'ADR-APP-DISTRIBUTION-001
> (Velopack, installation par utilisateur, signature Authenticode depuis la CI, flux local en multi-poste, retour
> arrière du couple binaires + base, séparation installation / données) et ses **treize obligations** OI-1 … OI-13.

---

# 16. Principes architecturaux

Pendant P5 :

* ne jamais sacrifier l'architecture pour accélérer l'UI ;
* privilégier les composants réutilisables ;
* conserver MVVM ;
* respecter les ADR existantes ;
* documenter chaque décision importante.

---

# Statut

```
P5 Roadmap:
DEFINED

Implementation:
NOT STARTED

Dependency:
P4 Multi-poste completion
```
