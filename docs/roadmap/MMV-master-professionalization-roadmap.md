# MMV — Feuille de route maîtresse de professionnalisation

**Projet :** ManageMyVision  
**Produit actuel :** application desktop .NET 8 / Avalonia / EF Core / SQLite  
**Marchés principaux :** Belgique et Maroc  
**Ambition :** logiciel professionnel pour magasins d’optique, puis évolution SaaS B2B éventuelle.

> Cette feuille de route organise le travail d’ingénierie. Elle ne garantit pas l’absence absolue de défauts et ne remplace pas une validation juridique, fiscale, médicale ou métier locale.

---

## 1. Définition du résultat attendu

Un logiciel professionnel n’est pas un logiciel « sans aucune erreur ». C’est un logiciel pour lequel :

- les défauts connus sont traités ou explicitement acceptés ;
- les nouvelles régressions sont détectées par les tests et la CI ;
- les données peuvent être sauvegardées, migrées et restaurées ;
- les opérations critiques sont cohérentes et transactionnelles ;
- les droits et accès sont contrôlés ;
- les règles nationales sont versionnées et sourcées ;
- les versions sont installables, observables et supportables ;
- un pilote réel confirme les parcours métier.

Trois validations indépendantes sont nécessaires :

1. **Engineering Ready** : architecture, données, tests, sécurité technique et exploitation.
2. **Market Ready** : règles Belgique ou Maroc confirmées et correctement implémentées.
3. **Commercial Ready** : pilote magasin, support, documentation, contrats et procédure de mise en production.

Aucune de ces validations ne doit être confondue avec les deux autres.

---

## 2. Règles d’exécution obligatoires

1. Une seule étape atomique par session Claude Code.
2. L’étape à exécuter est fournie explicitement par son identifiant.
3. Claude ne choisit jamais seul la prochaine étape.
4. Aucune étape ne démarre sans rapport `GO` de ses prérequis.
5. Une étape se termine par un arrêt obligatoire, même si elle réussit.
6. Les documents ne remplacent jamais la vérification du dépôt réel.
7. Les changements hors périmètre sont interdits.
8. Les décisions structurantes sont documentées par ADR.
9. Toute migration de données est testée sur une copie réaliste.
10. Une information réglementaire non confirmée reste une validation gate.
11. Aucun taux fiscal fictif, aucune devise implicite et aucune règle française ne servent de valeur par défaut.
12. Un commit ou un push n’est autorisé que sur instruction explicite.
13. Le développement SaaS ne doit pas déstabiliser la version desktop.
14. Une phase peut être révisée si les preuves du dépôt invalident la roadmap.

---

## 3. État actuel de référence

| Identifiant | Étape | État attendu au 9 juin 2026 |
|---|---|---|
| `P0` | Cartographie du dépôt | Terminée |
| `P0.5` | Référentiel métier Belgique/Maroc | Terminée |
| `P1` | Audit technique | Terminée |
| `P1B` | Validation et correction de l’audit | Terminée |
| `P2A-0` | Tests, SDK, dépendances et CI locale | Réalisée localement |
| `P2A-0B` | Clôture Git et validation CI distante | **Prochaine étape** |
| Étapes suivantes | Implémentation progressive | Non commencées |

Le statut réel doit être confirmé dans le dépôt et dans les rapports avant toute action.

---

# PROGRAMME 1 — STABILISER LE SOCLE EXISTANT

## `P2A-0B` — Clôture Git et validation CI distante

### But

Rendre l’Étape 0 reproductible et réellement intégrée au dépôt.

### Contenu

- suivre les référentiels Markdown utiles ;
- exclure les ZIP et artefacts temporaires ;
- durcir le scan NuGet de la CI ;
- vérifier le comportement de `global.json`;
- préparer un commit propre ;
- pousser uniquement si explicitement autorisé ;
- confirmer le premier workflow GitHub Actions.

### Sortie attendue

- référentiels suivis par Git ;
- pipeline distant vert ou mention explicite `VALIDATION DISTANTE REQUISE`;
- rapport `docs/implementation/P2A-0B-report.md`;
- aucun code métier modifié.

### Dépendances

- `P2A-0` avec verdict local positif.

---

## `P2A-1A` — Cycle de vie SQLite et chemin de base unique

### But

Définir une stratégie sûre pour une installation vierge et les bases historiques.

### Contenu

- recenser tous les chemins et points de configuration ;
- définir un chemin unique et configurable par environnement ;
- détecter les bases créées par `EnsureCreated`;
- choisir le mécanisme d’adoption des migrations ;
- sauvegarder avant mutation ;
- journaliser les migrations ;
- gérer le verrou mono-instance ;
- prévoir reprise et restauration.

### Interdictions

- aucune migration destructive sans preuve ;
- aucune suppression prématurée de `EnsureCreated`;
- aucune modification du modèle monétaire ;
- aucune refonte de la couche Application.

### Sortie attendue

- ADR accepté ;
- installation vierge testée ;
- copie de base historique migrée ;
- restauration testée ;
- rapport `P2A-1A`.

---

## `P2A-1B` — Migration des données historiques

### But

Rendre les bases existantes compatibles avec le nouveau cycle de migration.

### Contenu

- outil de diagnostic de schéma ;
- baselining ou migration d’adoption ;
- contrôle des tables, clés et comptes ;
- traitement des données inattendues ;
- rapport avant/après ;
- procédure de reprise.

### Sortie attendue

- scénarios de migration automatisés ;
- absence de perte démontrée ;
- sauvegarde restaurable ;
- rapport `P2A-1B`.

---

## `P2A-1C` — Transactions, idempotence et erreurs de persistance

### But

Empêcher les écritures partielles et doubles opérations.

### Cas prioritaires

- vente et lignes ;
- paiement/acompte ;
- mouvements de stock ;
- commandes et statuts ;
- remboursements ;
- réception fournisseur.

### Sortie attendue

- frontières transactionnelles documentées ;
- tests de rollback ;
- protection contre double clic/double soumission ;
- erreurs utilisateur maîtrisées ;
- rapport `P2A-1C`.

---

## `P2A-1D` — Concurrence et intégrité du stock

### But

Protéger le stock et les écritures concurrentes.

### Contenu

- concurrency token compatible SQLite ;
- mises à jour conditionnelles ;
- traitement de `DbUpdateConcurrencyException`;
- invariant de quantité ;
- stratégie de retry limitée et documentée ;
- tests avec deux contextes concurrents.

### Sortie attendue

- aucun stock négatif non autorisé ;
- conflit visible et récupérable ;
- rapport `P2A-1D`.

---

## `P2A-1E` — Numérotation fiable

### But

Remplacer `Random`, timestamps fragiles et calculs non transactionnels.

### Documents

- devis ;
- ventes ;
- factures ;
- avoirs ;
- commandes ;
- SAV et réparations.

### Sortie attendue

- unicité transactionnelle ;
- format configurable ;
- séquence par périmètre retenu ;
- tests concurrents ;
- rapport `P2A-1E`.

---

## `P2A-1F` — Environnements, configuration et seeds

### But

Séparer clairement Development, Test, Demo et Production.

### Contenu

- validation de configuration au démarrage ;
- suppression du seed démo en production ;
- compte bootstrap sécurisé ;
- secrets hors dépôt ;
- paramètres typés ;
- logs de démarrage non sensibles.

### Sortie attendue

- production sans données démo ;
- configuration invalide bloquée ;
- tests d’environnement ;
- rapport `P2A-1F`.

---

# PROGRAMME 2 — CORRIGER L’ARCHITECTURE APPLICATIVE

## `P2B-2A` — ADR et frontières de couches

### But

Valider les responsabilités de Domain, Application, Infrastructure et UI.

### Décisions attendues

- dépendances autorisées ;
- services applicatifs ou CQRS léger ;
- validation ;
- transactions ;
- mapping ;
- événements éventuels ;
- erreurs typées.

### Sortie attendue

- ADR acceptés/rejetés ;
- graphe de dépendances cible ;
- aucune implémentation massive ;
- rapport `P2B-2A`.

---

## `P2B-2B` — Création de la couche Application

### But

Fournir une couche de cas d’utilisation indépendante d’Avalonia.

### Contenu

- commandes/requêtes ;
- résultats ;
- interfaces de persistance ;
- horloge injectable ;
- gestion des erreurs ;
- tests applicatifs.

### Sortie attendue

- Domain sans dépendance EF/UI ;
- Application sans dépendance Avalonia ;
- rapport `P2B-2B`.

---

## `P2B-2C` — Premier vertical slice

### Slice

**Enregistrement transactionnel d’un brouillon de vente magasin.**

### Contraintes

- pas de facture fiscale définitive si fiscalité absente ;
- pas de taux `0` utilisé comme absence de configuration ;
- pas de règle Belgique/Maroc inventée ;
- UI limitée à l’interaction et à l’état.

### Sortie attendue

- slice vertical testé ;
- rollback ;
- résultat métier explicite ;
- rapport `P2B-2C`.

---

## `P2B-2D` à `P2B-2I` — Migration progressive des parcours

Exécuter un parcours par étape :

| ID | Parcours |
|---|---|
| `P2B-2D` | Vente |
| `P2B-2E` | Commandes |
| `P2B-2F` | Stock |
| `P2B-2G` | Client |
| `P2B-2H` | Ordonnances |
| `P2B-2I` | Atelier |

### Règle

Aucun big bang. Chaque parcours doit rester utilisable après migration.

---

# PROGRAMME 3 — FONDATIONS PRODUIT MULTI-PAYS

## `P2C-3A` — Organisation et Magasin

- organisation ;
- magasin ;
- utilisateur affecté ;
- magasin courant ;
- données opérationnelles rattachées ;
- tests anti-fuite inter-magasin.

## `P2C-3B` — Pays, langue, devise et fuseau horaire

- pays d’exploitation ;
- codes ISO ;
- locale utilisateur ;
- devise ;
- fuseau ;
- adresses et téléphones configurables ;
- stockage UTC et affichage local.

## `P2C-3C` — Modèle monétaire

- value object domaine ;
- devise obligatoire ;
- politique d’arrondi ;
- stockage SQLite sûr ;
- mapping futur SGBD serveur ;
- migration des montants existants.

## `P2C-3D` — Configuration et versionnement des règles nationales

- règles datées ;
- source ;
- état `NotConfigured`, `Configured`, `InvalidOrExpired`;
- capacités par pays ;
- absence de duplication du cœur.

## `P2C-3E` — Rôles et permissions

- administrateur ;
- gérant ;
- opticien ;
- vendeur ;
- technicien ;
- administratif ;
- permissions par action et magasin.

## `P2C-3F` — Audit métier minimal

- acteur ;
- date ;
- magasin ;
- cible ;
- opération ;
- résultat ;
- corrélation ;
- événements sensibles prioritaires.

Chaque étape possède son propre rapport et son propre verdict.

---

# PROGRAMME 4 — RECONSTRUIRE LE DOMAINE OPTIQUE

## `P2D-4A` — Client, Porteur, Payeur et Foyer

Séparer les identités commerciales, médicales et financières, avec migration des clients existants.

## `P2D-4B` — Prescription et ordonnance

Distinguer validité de prescription, renouvellement et éligibilité au remboursement.

## `P2D-4C` — Examen visuel et mesures

Réfraction, acuité, écart pupillaire, hauteurs et paramètres de centrage.

## `P2D-4D` — Catalogue, produits, verres et traitements

Catégories optiques, grilles de prix, suppléments, fournisseurs et historique tarifaire.

## `P2D-4E` — Devis et offre commerciale

Version, expiration, options, acceptation et conversion en vente.

## `P2D-4F` — Vente, paiements, facture et avoir

Acomptes, paiements multiples, reste dû, annulation et remboursement autorisé.

## `P2D-4G` — Fournisseurs et commandes

Commande, réception, reliquat, BL, rapprochement et affectation.

## `P2D-4H` — Atelier et contrôle qualité

File d’attente, transitions, opérateur, fabrication, non-conformité et livraison.

## `P2D-4I` — Stock et inventaire

Mouvements traçables, inventaire, emplacements et transferts.

## `P2D-4J` — SAV, réparations et garanties

Ticket, diagnostic, couverture, réparation, coût, décision et clôture.

---

# PROGRAMME 5 — ADAPTATIONS NATIONALES

## Gate préalable `G-MARKET`

Avant toute implémentation nationale, décider explicitement :

- premier pays pilote ;
- périmètre fonctionnel du pilote ;
- langues du lancement ;
- fiscalité validée ;
- prescription validée ;
- remboursement validé ;
- documents obligatoires ;
- expert local responsable de validation.

Sans ce gate, les étapes nationales restent `NO-GO`.

---

## `P2E-5A` — Belgique

- INAMI/RIZIV ;
- mutualités ;
- conventionnement ;
- éligibilité et périodicité ;
- attestations ;
- facture B2C ;
- facture électronique structurée B2B ;
- Peppol ;
- EUR ;
- langues selon périmètre décidé.

## `P2E-5B` — Maroc

- AMO ;
- ANAM ;
- CNSS ;
- transition CNOPS ;
- dossier de remboursement ;
- assurances complémentaires ;
- MAD ;
- documents ;
- CNDP ;
- français/arabe/RTL selon périmètre décidé.

## `P2E-5C` — Registre des règles et tests réglementaires

- source officielle ;
- date d’effet ;
- version ;
- expiration ;
- statut ;
- tests ;
- journal de changement.

---

# PROGRAMME 6 — UX, ACCESSIBILITÉ ET LOCALISATION

## `P2F-6A` — Système de localisation

Ressources, formats, documents, erreurs et contenu dynamique.

## `P2F-6B` — Arabe et RTL

`FlowDirection`, composants, tableaux, icônes, recherche et documents bilingues si décidés.

## `P2F-6C` — Accessibilité et ergonomie

Clavier, focus, contrastes, lecteurs d’écran, validation accessible et tests utilisateurs.

---

# PROGRAMME 7 — SÉCURITÉ ET PROTECTION DES DONNÉES

## `P2H-8A` — Threat modeling

Actifs, menaces, surfaces d’attaque, mesures et risques résiduels.

## `P2H-8B` — Authentification et secrets

Mots de passe, limitation des tentatives, sessions, rotation, stockage sécurisé et MFA pour le SaaS.

## `P2H-8C` — Données personnelles et de santé

Minimisation, conservation, droits, export, audit d’accès, transferts et rôles juridiques.

## `P2H-8D` — Supply chain et sécurité applicative

Audit NuGet, SAST, secret scanning, SBOM, signature et tests de sécurité.

---

# PROGRAMME 8 — EXPLOITATION DESKTOP

## `P2J-10A` — Packaging et installation

Installation, désinstallation, permissions, emplacement des données et signature.

## `P2J-10B` — Mises à jour sécurisées

Signature, intégrité, canaux, compatibilité de schéma et rollback applicatif.

## `P2J-10C` — Sauvegarde et restauration

Automatisation, chiffrement, rétention et restauration réellement testée.

## `P2J-10D` — Observabilité et support

Logs structurés, diagnostics, métriques, export support et runbooks.

---

# PROGRAMME 9 — REPORTING ET QUALITÉ DES DONNÉES

## `P2I-9A` — Indicateurs métier

CA, marge, panier, transformation, atelier, stock dormant et SAV.

## `P2I-9B` — Exports comptables

Journaux, paiements, taxes, avoirs et rapprochement selon marché.

## `P2I-9C` — Qualité des données

Doublons, valeurs manquantes, cohérence et correction contrôlée.

---

# PROGRAMME 10 — VALIDATION PRODUIT ET LANCEMENT

## `P2K-11A` — Tests de bout en bout

Parcours client, ordonnance, devis, vente, paiement, commande, atelier, livraison, SAV et inventaire.

## `P2K-11B` — Performance et robustesse

Volumétrie, recherches, démarrage, concurrence, panne, interruption et reprise.

## `P2K-11C` — Pilote magasin

Pilote dans le premier marché choisi, incidents, retours et critères d’acceptation.

## `P2L-12A` — Documentation

Guides utilisateur, administrateur, installation, sécurité et restauration.

## `P2L-12B` — Support et exploitation

SLA, incidents, priorités, escalade et communication.

## `P2L-12C` — Go-Live contrôlé

Version, conformité, support, monitoring, sauvegarde et décision formelle.

---

# PROGRAMME 11 — ÉVOLUTION SAAS, APRÈS VALIDATION DESKTOP

Ce programme est optionnel tant que le desktop professionnel n’est pas validé.

## `P2G-7A` — Abstraction du stockage et SGBD serveur

Mappings provider-specific, PostgreSQL ou SGBD retenu, migrations et tests contractuels.

## `P2G-7B` — Multi-tenant

Isolation, provisioning, quotas, administration, sauvegardes et tests anti-fuite.

## `P2G-7C` — API et intégrations

Contrats versionnés, idempotence, retries, sécurité et journal des échanges.

## `P2G-7D` — Exploitation SaaS

Déploiement, observabilité, haute disponibilité, reprise et support.

---

## 4. Gates générales

### Gate technique

- restore réussi ;
- build réussi ;
- tests verts ;
- CI verte ;
- aucune vulnérabilité High/Critical non traitée ;
- diff contrôlé ;
- rapport complet.

### Gate données

- sauvegarde ;
- migration sur copie ;
- contrôle avant/après ;
- restauration ;
- aucune perte non acceptée.

### Gate réglementaire

- source officielle ;
- date d’effet ;
- règle versionnée ;
- validation locale si nécessaire ;
- aucune valeur non confirmée en production.

### Gate sécurité

- permissions testées ;
- secrets absents ;
- logs contrôlés ;
- audit dépendances ;
- risques résiduels documentés.

### Gate produit

- parcours nominal et erreurs ;
- accessibilité ;
- performance ;
- pilote utilisateur ;
- documentation ;
- procédure de support.

---

## 5. Format obligatoire de chaque rapport

Chaque étape crée :

```text
docs/implementation/<phase-id>-report.md
```

Le rapport contient :

1. identifiant et objectif ;
2. prérequis ;
3. état Git initial ;
4. baseline ;
5. plan ;
6. décisions et ADR ;
7. fichiers modifiés ;
8. migrations ;
9. tests ;
10. commandes et résultats ;
11. sécurité et vulnérabilités ;
12. risques résiduels ;
13. éléments différés ;
14. état Git final ;
15. verdict `GO` ou `NO-GO`;
16. prochaine étape candidate, non exécutée.

---

## 6. Protocole Git

Avant :

```bash
git status --short
git branch --show-current
git log -5 --oneline
git diff --stat
git diff
```

Après :

```bash
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
git status --short
git diff --stat
git diff
```

Aucun commit ou push sans autorisation explicite.

---

## 7. Ordre immédiat

1. `P2A-0B`
2. revue humaine du rapport et du pipeline
3. `P2A-1A`
4. revue de la migration sur copies
5. `P2A-1B`

Claude ne doit jamais lancer automatiquement l’étape suivante.
