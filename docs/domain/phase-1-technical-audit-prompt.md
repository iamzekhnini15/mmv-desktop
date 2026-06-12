# PROMPT CLAUDE CODE — PHASE 1
## Audit technique et architectural multi-pays de MMV — sans implémentation

Tu dois réaliser la Phase 1 du projet MMV : un audit technique complet du dépôt actuel, évalué à partir du référentiel métier Belgique–Maroc consolidé en Phase 0.5D.

## 1. Documents obligatoires à lire avant toute analyse

Lis intégralement :

- `docs/domain/phase-0.5-multicountry.md`
- `docs/domain/regulatory-source-register.md`
- `docs/domain/country-capability-matrix.md`
- `docs/domain/backlog-by-market.md`
- `docs/domain/glossary-multicountry.md`
- `docs/domain/phase-0.5d-consistency-report.md`

Le rapport historique `phase-0.5c-validation-report.md` ne doit pas primer sur la Phase 0.5D.

## 2. Mission

Évalue si l'architecture, le modèle métier, la persistance, l'interface et les tests actuels de MMV peuvent accueillir :

- le cœur métier commun ;
- Belgique et Maroc sans duplication de l'application ;
- plusieurs organisations et magasins ;
- plusieurs devises, langues, fuseaux horaires et politiques fiscales ;
- des règles nationales datées et versionnées ;
- la protection des données et le cloisonnement requis ;
- l'évolution ultérieure vers un SaaS B2B.

Tu agis comme :

- architecte logiciel principal .NET ;
- expert DDD, Clean Architecture et Vertical Slice ;
- expert Avalonia/MVVM ;
- expert EF Core et SQLite ;
- expert sécurité et SaaS multi-tenant ;
- expert qualité, testabilité et migration progressive.

## 3. Interdictions absolues

Pendant cette phase :

- ne modifie aucun code source ;
- ne déplace aucun fichier ;
- ne crée aucune migration ;
- ne modifie aucune base ;
- ne corrige aucun test ;
- ne réalise aucun refactoring ;
- ne crée aucune branche d'implémentation ;
- ne génère aucun country pack ;
- ne code aucun taux, montant, délai ou règle réglementaire ;
- ne commence pas la Phase 2.

Tu peux uniquement lire, rechercher, cartographier, construire et tester sans modification.

Avant et après l'audit, affiche `git status --short` afin de démontrer qu'aucun fichier applicatif n'a été modifié. Les seuls nouveaux fichiers autorisés sont les rapports Markdown demandés sous `docs/architecture/`.

## 4. Fiabilité des preuves

Ne fais confiance à aucun ancien numéro de ligne ou chemin sans le revérifier dans le dépôt actuel.

Pour chaque constat, fournis :

- projet ;
- chemin exact ;
- classe, méthode, propriété ou configuration ;
- plage de lignes actuelle ;
- extrait très court ou description précise ;
- commande de recherche utilisée si pertinent ;
- niveau de confiance.

Quand une absence est affirmée, précise :

- les dossiers inspectés ;
- les termes recherchés ;
- la commande utilisée ;
- les limites de la recherche.

Utilise :

- `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` lorsque la recherche est complète ;
- `NON_VÉRIFIÉ_DANS_LE_DÉPÔT` lorsqu'elle ne l'est pas.

N'invente jamais une classe, un fichier ou un comportement.

## 5. Étape A — Cartographie factuelle du dépôt

Documente :

1. solution et projets ;
2. dépendances entre projets ;
3. packages NuGet et versions ;
4. points d'entrée ;
5. composition root et injection de dépendances ;
6. entités, value objects, agrégats, enums ;
7. services de domaine et d'application ;
8. repositories, Unit of Work, DbContext et configurations EF ;
9. migrations et stratégie d'initialisation SQLite ;
10. ViewModels, vues, navigation et commandes ;
11. validations ;
12. génération de documents ;
13. authentification, autorisation et permissions ;
14. tests unitaires, intégration et UI ;
15. scripts de build, CI et packaging ;
16. documentation technique existante.

Produis une carte de dépendances réelle. Signale toute inversion de dépendance non respectée.

## 6. Étape B — Vérification de l'état du build

Sans modifier le dépôt :

- détecte le SDK .NET attendu ;
- exécute `dotnet restore` ;
- exécute `dotnet build` dans la configuration pertinente ;
- exécute `dotnet test` ;
- relève warnings, erreurs, tests ignorés et couverture disponible ;
- indique les commandes exactes et leur résultat.

Ne masque aucune erreur. Une dépendance externe manquante doit être décrite, pas contournée par une modification du code.

## 7. Étape C — Audit architectural

### 7.1 Frontières et dépendances

Analyse :

- direction des références entre Domain, Application, Infrastructure et UI ;
- dépendances du domaine envers EF, Avalonia ou des détails techniques ;
- emplacement des interfaces ;
- composition root ;
- duplication et couplage ;
- services présents mais non utilisés ;
- logique métier exécutée directement dans les ViewModels.

### 7.2 Modèle de domaine

Évalue :

- invariants ;
- mutabilité ;
- encapsulation ;
- agrégats et transactions ;
- client / porteur / payeur ;
- ordonnance et correction ;
- vente, facture, paiement et commande ;
- stock et atelier ;
- SAV, réparation et garantie ;
- Money, devise, arrondis et précision ;
- identifiants et numérotation ;
- temps, date locale, UTC et fuseau ;
- règles nationales et règles datées.

Ne propose pas encore les nouvelles classes définitives. Identifie les capacités manquantes et compare plusieurs options.

### 7.3 Persistance et intégrité

Analyse :

- frontières transactionnelles ;
- Unit of Work ;
- concurrence ;
- séquences de documents ;
- contraintes uniques ;
- index ;
- cascade et suppression ;
- historisation ;
- migrations ;
- données de seed ;
- stratégie SQLite locale versus SaaS/multi-magasin ;
- sauvegarde, restauration et évolution de schéma.

### 7.4 Multi-organisation et multi-magasin

Vérifie la capacité actuelle à gérer :

- organisation ;
- magasin ;
- affectation utilisateur ;
- isolation des requêtes ;
- stock par magasin ;
- numérotation par établissement ;
- reporting consolidé ;
- transferts ;
- prévention des fuites inter-tenant.

Compare au moins trois approches possibles d'isolation et explique leurs compromis, sans en implémenter une.

### 7.5 Internationalisation et localisation

Analyse :

- chaînes codées en dur ;
- ressources ;
- culture ;
- formats de dates, nombres, téléphones et adresses ;
- devises ;
- arrondis ;
- fuseaux horaires ;
- RTL Avalonia ;
- recherche en alphabet latin et arabe ;
- documents localisés ;
- stockage des libellés catalogue.

### 7.6 Fiscalité, facturation et politiques nationales

Évalue la capacité à ajouter des règles par pays et par date sans duplication.

Ne présuppose pas que la solution sera un plugin ou une interface spécifique. Compare notamment :

- configuration versionnée ;
- stratégies ou services de politique ;
- modules applicatifs ;
- tables de règles ;
- combinaison de ces approches.

Évalue séparément :

- taxe par catégorie et date ;
- mentions de document ;
- numérotation ;
- facture B2C ;
- facture B2B structurée ;
- représentation PDF ;
- connecteurs externes ;
- barèmes de remboursement ;
- validité de prescription ;
- périodicité de prise en charge.

Toutes les gates de Phase 0.5D restent des paramètres. N'utilise aucune valeur non confirmée.

### 7.7 Sécurité et protection des données

Analyse :

- authentification ;
- hachage et stockage des secrets ;
- autorisation par rôle et permission ;
- moindre privilège ;
- isolation par organisation/magasin ;
- journal des accès aux données de santé ;
- chiffrement au repos et en transit ;
- export et suppression ;
- rétention ;
- sauvegardes ;
- logs et données sensibles ;
- dépendances vulnérables ;
- menaces liées au desktop local et au futur SaaS.

Produis un mini threat model : actifs, acteurs, frontières de confiance, menaces principales et contrôles attendus.

### 7.8 Performance et résilience

Analyse :

- requêtes EF et chargements inutiles ;
- pagination ;
- N+1 ;
- indexes ;
- synchronisation éventuelle ;
- opérations longues sur le thread UI ;
- gestion d'erreurs ;
- retries et idempotence pour futurs connecteurs ;
- volume multi-magasin ;
- démarrage et mémoire de l'application.

### 7.9 Tests et qualité

Évalue :

- couverture des règles métier ;
- tests de persistance ;
- tests des ViewModels ;
- tests des workflows critiques ;
- tests de sécurité ;
- tests de localisation ;
- tests de migration ;
- testabilité des dépendances temps, identifiants et fichiers ;
- qualité des assertions ;
- données de test.

## 8. Étape D — Confrontation avec le référentiel métier

Pour chaque capacité du fichier `country-capability-matrix.md`, attribue :

- `PRESENT_REUSABLE`
- `PRESENT_COUPLED`
- `PARTIAL`
- `ABSENT`
- `INCORRECT_ASSUMPTION`
- `NOT_VERIFIED`

Ajoute :

- preuve dépôt ;
- impact Belgique ;
- impact Maroc ;
- risque ;
- dette ;
- options de remédiation ;
- prérequis ;
- priorité.

Ne reprends pas mécaniquement l'ancienne matrice : reconstruis-la à partir du code actuel.

## 9. Sévérité des constats

Utilise :

- `BLOCKER` : empêche une évolution sûre ou compromet l'intégrité/sécurité ;
- `CRITICAL` : risque majeur pour le produit ou les données ;
- `HIGH` : dette structurante à traiter tôt ;
- `MEDIUM` : amélioration importante mais non bloquante ;
- `LOW` : qualité ou confort.

Pour chaque constat, indique :

- probabilité ;
- impact ;
- portée ;
- preuve ;
- recommandation ;
- dépendances ;
- effort relatif `S/M/L/XL` ;
- ordre proposé.

## 10. Livrables à créer

Crée uniquement les fichiers suivants :

### `docs/architecture/phase-1-technical-audit.md`

- résumé exécutif ;
- état du build et des tests ;
- cartographie ;
- audit par thème ;
- confrontation métier ;
- principaux constats ;
- conclusion.

### `docs/architecture/dependency-map.md`

- projets ;
- références ;
- flux de dépendances ;
- diagrammes Mermaid ;
- violations observées.

### `docs/architecture/risk-register.md`

| ID | Sévérité | Domaine | Constat | Preuve | Impact BE | Impact MA | Probabilité | Effort | Dépendances | Recommandation |

### `docs/architecture/adr-candidates.md`

ADR candidats, sans décision définitive, pour :

- modularité des politiques nationales ;
- multi-tenant/multi-magasin ;
- persistance cible ;
- Money/devise/arrondis ;
- temps et fuseaux ;
- numérotation ;
- documents et e-facturation ;
- localisation/RTL ;
- audit et données de santé.

Pour chaque ADR : contexte, options, avantages, inconvénients, risques, décision différée et informations manquantes.

### `docs/architecture/migration-roadmap.md`

Une feuille de route progressive, sans code, avec :

- prérequis ;
- ordre des changements ;
- dépendances ;
- stratégie de compatibilité et migration de données ;
- stratégie de tests ;
- critères d'arrêt/rollback ;
- proposition du premier vertical slice.

## 11. Critères d'acceptation

La Phase 1 n'est terminée que si :

- aucun code n'a été modifié ;
- le build et les tests ont été exécutés ou l'impossibilité est prouvée ;
- chaque constat majeur possède une preuve actuelle ;
- les absences sont qualifiées correctement ;
- les risques Belgique et Maroc sont distingués ;
- aucune règle nationale non confirmée n'est codifiée ;
- plusieurs options d'architecture sont comparées ;
- la roadmap respecte les dépendances ;
- les cinq livrables existent ;
- `git status --short` final est affiché ;
- une décision `GO` ou `NO-GO` pour la Phase 2 est motivée.

## 12. Conclusion attendue

Termine par :

1. les dix risques les plus importants ;
2. les hypothèses du dépôt invalidées ;
3. les éléments réellement réutilisables ;
4. les décisions architecturales à prendre ;
5. les gates métier qui restent externes ;
6. le premier vertical slice recommandé ;
7. le verdict `GO` ou `NO-GO` pour commencer l'implémentation.

Ne commence aucune implémentation.
