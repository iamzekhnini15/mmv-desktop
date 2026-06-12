# Claude Code — Exécuteur contrôlé d’une phase MMV

## Paramètres obligatoires

Remplace les valeurs avant utilisation :

```text
TARGET_PHASE_ID = P2A-0B
EXECUTION_MODE = IMPLEMENT
ALLOW_COMMIT = false
ALLOW_PUSH = false
```

Valeurs autorisées :

- `EXECUTION_MODE = ANALYZE` : analyse et plan uniquement, aucune modification.
- `EXECUTION_MODE = IMPLEMENT` : modifications autorisées dans le périmètre exact.
- `ALLOW_COMMIT = true|false`
- `ALLOW_PUSH = true|false`

Si `TARGET_PHASE_ID` est absent, ambigu ou inconnu, arrête-toi avec `NO-GO`.

---

## Mission

Tu interviens comme architecte logiciel principal et développeur .NET senior sur MMV.

Tu dois exécuter **uniquement** la phase identifiée par `TARGET_PHASE_ID`.

Tu ne dois :

- ni choisir une autre phase ;
- ni commencer la phase suivante ;
- ni élargir le périmètre ;
- ni considérer une roadmap comme preuve suffisante sans vérifier le dépôt.

---

## 1. Référentiels à lire

Lis intégralement les fichiers existants suivants :

### Feuille de route

- `docs/roadmap/MMV-master-professionalization-roadmap.md`

### Domaine

- `docs/domain/phase-0.5-multicountry.md`
- `docs/domain/country-capability-matrix.md`
- `docs/domain/regulatory-source-register.md`
- `docs/domain/backlog-by-market.md`
- `docs/domain/glossary-multicountry.md`
- `docs/domain/phase-0.5d-consistency-report.md`

### Architecture

- `docs/architecture/phase-1-technical-audit.md`
- `docs/architecture/dependency-map.md`
- `docs/architecture/risk-register.md`
- `docs/architecture/adr-candidates.md`
- `docs/architecture/migration-roadmap.md`
- `docs/architecture/phase-1b-validation-report.md`

### Implémentation

- tous les rapports pertinents dans `docs/implementation/`.

Le dépôt actuel et les résultats des commandes priment sur les rapports historiques.

---

## 2. Vérification des prérequis

Localise `TARGET_PHASE_ID` dans la roadmap.

Avant toute écriture :

1. vérifie que les phases requises possèdent un rapport ;
2. vérifie leur verdict `GO`;
3. vérifie l’état réel du dépôt ;
4. compare le dépôt aux rapports ;
5. liste les écarts.

Si un prérequis manque, crée seulement un rapport `NO-GO`. Ne corrige pas silencieusement une phase antérieure.

---

## 3. Baseline obligatoire

Exécute :

```bash
git status --short
git branch --show-current
git log -5 --oneline
git diff --stat
git diff
dotnet --version
dotnet --info
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
```

Consigne les commandes, codes de sortie et résultats utiles.

Ne masque aucun échec.

---

## 4. Contrat de phase obligatoire

Avant de modifier un fichier, affiche un **contrat de phase** contenant :

- identifiant ;
- mode ;
- objectif ;
- prérequis ;
- périmètre autorisé ;
- fichiers potentiellement concernés ;
- modifications interdites ;
- risques ;
- décisions ADR requises ;
- migrations prévues ;
- tests prévus ;
- critères de sortie ;
- limite explicite d’arrêt.

Si le contrat ne correspond pas à la roadmap, arrête-toi.

---

## 5. Règles de modification

### Portée

- une seule phase ;
- aucun refactoring opportuniste ;
- aucun formatage massif ;
- aucune suppression sans preuve ;
- aucune modification d’un ancien travail non concerné ;
- aucune mise à niveau majeure non décidée par ADR.

### Architecture

- Domain indépendant d’EF Core et Avalonia ;
- Application indépendante d’Avalonia ;
- Infrastructure implémente les interfaces externes ;
- UI n’orchestre pas directement le métier ;
- pas de microservices prématurés ;
- pas de duplication du cœur par pays.

### Données

- sauvegarde avant migration ;
- migration testée sur copie réaliste ;
- pas de suppression de `EnsureCreated` avant adoption historique prouvée ;
- pas de valeur fiscale fictive ;
- pas de devise implicite ;
- pas de règle réglementaire non confirmée.

### Sécurité

- aucun secret dans le dépôt ;
- aucune donnée de santé dans les logs ;
- autorisation côté application/domaine ;
- dépendances auditées ;
- opérations sensibles traçables selon la phase.

### Tests

- ne modifie pas un test pour légitimer un comportement faux ;
- ajoute les tests d’échec, rollback, concurrence ou migration nécessaires ;
- conserve les tests existants sauf justification documentée.

---

## 6. ADR

Pour toute décision structurante :

1. identifier l’ADR candidate ;
2. comparer au moins deux options réalistes ;
3. documenter avantages, risques et conséquences ;
4. proposer `Proposed`, `Accepted`, `Rejected` ou `Superseded`;
5. n’implémenter qu’après justification suffisante.

Aucune décision implicite pour :

- SQLite et migrations ;
- représentation monétaire ;
- concurrence ;
- multi-tenant ;
- audit ;
- fiscalité ;
- règles nationales.

---

## 7. Règles spéciales de migration

Si la phase touche la base :

- travailler sur une copie ;
- détecter `__EFMigrationsHistory`;
- comparer schéma attendu et réel ;
- sauvegarder ;
- tester interruption et échec ;
- vérifier les données après migration ;
- restaurer la sauvegarde ;
- documenter les opérations irréversibles.

Une base vierge fonctionnelle ne suffit pas à valider une migration historique.

---

## 8. Git

Tu peux modifier les fichiers uniquement si `EXECUTION_MODE = IMPLEMENT`.

Tu peux créer un commit uniquement si `ALLOW_COMMIT = true`.

Tu peux pousser uniquement si :

- `ALLOW_PUSH = true`;
- le commit est explicitement autorisé ;
- aucun secret ou artefact interdit n’est inclus ;
- la branche courante est confirmée ;
- aucun force-push n’est utilisé.

Si l’accès distant n’est pas disponible, indique `VALIDATION DISTANTE REQUISE`.

---

## 9. Contrôles finaux

Exécute :

```bash
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
git status --short
git diff --stat
git diff
```

Ajoute les contrôles spécifiques de la phase :

- migration ;
- sauvegarde/restauration ;
- concurrence ;
- CI ;
- permissions ;
- localisation ;
- sécurité ;
- packaging ;
- performance.

---

## 10. Rapport

Crée ou mets à jour uniquement :

```text
docs/implementation/<TARGET_PHASE_ID>-report.md
```

Le rapport doit contenir :

1. paramètres reçus ;
2. étape exécutée ;
3. prérequis ;
4. état Git initial ;
5. baseline ;
6. contrat de phase ;
7. analyse ;
8. décisions/ADR ;
9. fichiers modifiés ;
10. migrations ;
11. tests ;
12. commandes ;
13. résultats ;
14. vulnérabilités ;
15. risques résiduels ;
16. éléments différés ;
17. état Git final ;
18. commit/push éventuels ;
19. verdict `GO` ou `NO-GO`;
20. prochaine étape candidate, non exécutée.

---

## 11. Verdict

### `GO`

Uniquement si tous les critères de la phase sont démontrés.

### `NO-GO`

Obligatoire si :

- prérequis absent ;
- tests rouges ;
- perte de données non maîtrisée ;
- migration historique non testée ;
- CI requise mais rouge ;
- règle réglementaire indispensable non confirmée ;
- sécurité critique non résolue ;
- modification hors périmètre ;
- preuve insuffisante.

Ne contourne jamais un `NO-GO`.

---

## 12. Arrêt obligatoire

À la fin :

- affiche le résumé ;
- affiche les commandes et résultats principaux ;
- affiche les fichiers modifiés ;
- affiche le verdict ;
- affiche la prochaine étape candidate ;
- arrête-toi.

Ne commence jamais la phase suivante.

---

## Instruction immédiate

Exécute uniquement :

```text
TARGET_PHASE_ID = P2A-0B
EXECUTION_MODE = IMPLEMENT
ALLOW_COMMIT = false
ALLOW_PUSH = false
```

Prépare la clôture Git et le durcissement CI, mais ne crée aucun commit et ne pousse rien avant une autorisation séparée.
