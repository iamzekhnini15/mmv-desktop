# P4-5D-R Closure Report

> **Clôture documentaire — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 21 septembre 2026. Branche : `p4-multi-poste`.
> HEAD : **`45f67a270d926996f8cd5cd3f7b53baada030c39`** (`feat(P4-5D-R): repair legacy civil date formats at startup`),
> **inchangé** pendant la clôture.
> Nature : **documentaire seule**. Aucun fichier de code, de test, de migration EF, d'ADR, de CI ni aucun
> artefact PostgreSQL n'est modifié.
> Statut : **`P4-5D-R = COMPLETED`**, en attente de validation architecte.
>
> Sources : [rapport final d'implémentation](P4-5D-R-final-implementation-report.md) (référence détaillée du lot) ·
> [roadmap P4](../architecture/P4-multi-poste-roadmap.md) · [ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md).
> **Le dépôt réel prime sur ce document.**

---

## Objective

**Objectif du lot.** Préparer la migration sûre des valeurs de dates civiles SQLite historiques avant le
déploiement PostgreSQL.

P4-5D a fait passer `Customer.BirthDate` et `Prescription.IssueDate` de `DateTime` à `DateOnly`. La colonne
SQLite reste `TEXT`, mais le format attendu passe de `yyyy-MM-dd HH:mm:ss[.fffffff]` à `yyyy-MM-dd`. Toute base
antérieure à P4-5D lève donc `FormatException` à la lecture d'un client ou d'une ordonnance. **EF ne détecte
aucune dérive** : `has-pending-model-changes` reste vert et aucune migration n'est générable (ADR-PROD-DB-004 §7.2).

**Objectif de cette clôture.** Enregistrer l'achèvement du lot et aligner la roadmap P4 sur l'état réel du
dépôt pour `P4-5D-R`, puis signaler les autres écarts de la roadmap sans les corriger.

---

## Scope Completed

Périmètre : `Customers.BirthDate` (nullable) et `Prescriptions.IssueDate` (NOT NULL). Les 19 colonnes
d'instants sont hors périmètre.

| Livrable | État | Preuve dans le dépôt |
|---|---|---|
| Classification des formats de dates civiles historiques | **FAIT** | `CivilDateFormat.Classify` : six états (`NotApplicable`, `Canonical`, `NormalizableReadable`, `ReadableLeftAsIs`, `RepairableLegacyDateTime`, `Unrepairable`), fidélité au lecteur réel `Microsoft.Data.Sqlite` vérifiée par test |
| Stratégie de reprise sûre | **FAIT** | `SqliteCivilDateRepairService` : troncature `substring(0, 10)`, jamais de conversion de fuseau ; une transaction ; idempotente ; `dryRun` ; sauvegarde préalable ; contrôle après reprise |
| Protection contre les valeurs invalides | **FAIT** | une seule valeur `Unrepairable` ⇒ démarrage refusé, **aucune écriture**, `DatabaseMigrationException` (Q2) ; grammaire stricte (D-B1) et bornes horaires (Q-C2) ; date calendaire impossible refusée, jamais recalée |
| Protection des données sensibles au journal | **FAIT** | aucune valeur brute au journal ni dans les messages d'exception (D-B3) : table, colonne, clé primaire et empreinte `sha256:` seulement |
| Validation par tests automatisés | **FAIT** | **+157 tests** (118 + 27 + 12) ; **1 915 / 1 915** au vert |

**Invariants respectés** : aucune migration EF, aucun changement de schéma ni de modèle, `DateOnly` conservé,
aucun fichier PostgreSQL, aucun test antérieur au lot modifié.

---

## Implementation Summary

### Code de production

| Fichier | État | Rôle |
|---|---|---|
| `src/MMV.Infrastructure/Data/Time/CivilDateFormat.cs` | nouveau | **La règle** : fonction pure, sans base |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateFormatVerifier.cs` | nouveau | **Le diagnostic** : lecture seule du `TEXT` brut en ADO, sous le modèle EF |
| `src/MMV.Infrastructure/Data/Time/SqliteCivilDateRepairService.cs` | nouveau | **La reprise** : transactionnelle, idempotente, simulable |
| `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` | modifié | branchement `RepairCivilDateFormats` dans `PrepareDatabase` |

Le diagnostic, la reprise et le gestionnaire de base appellent tous `CivilDateFormat.Classify`. **Il n'existe
qu'une seule règle.**

### Point d'insertion

La reprise s'exécute **hors du `switch` d'état** de `PrepareDatabase`, donc sur tous les chemins : installation
neuve, base gérée par migrations, base historique adoptée. Une base installée après P2A-1A et avant P4-5D, le cas
le plus répandu en exploitation, est ainsi couverte.

```
PrepareDatabase
  ├─ BACKUP du fichier                          ← avant toute mutation
  ├─ détection d'état → fresh | migrations | adoption
  └─ RepairCivilDateFormats                     ← P4-5D-R
       ├─ diagnostic (dryRun)
       ├─ ≥ 1 Unrepairable → CIVILDATE REFUSED, rien n'est écrit
       ├─ rien à reprendre → CIVILDATE ok
       ├─ reprise en une transaction → CIVILDATE detected / repaired
       └─ contrôle après reprise → CIVILDATE FAILURE si une valeur reste illisible
```

### Règle de reprise

| Valeur stockée | État | Résultat |
|---|---|---|
| `1985-03-15 00:00:00`, `1985-03-15T12:30:45+01:00` | `RepairableLegacyDateTime` | `1985-03-15` (jour écrit, jamais converti) |
| `1985-03-15T12:30`, `1985-03-15T12:30:45.12345678` | `NormalizableReadable` | `1985-03-15` |
| `␣1985-03-15`, `1985-3-5` | `ReadableLeftAsIs` | inchangée (Q3 → P4-7) |
| `1985-03-15 24:00:00`, `2026-02-30 00:00:00`, `abc`, `""` | `Unrepairable` | **refus du démarrage** |

### Documentation du lot

Sept documents dans `docs/implementation/` : pré-revues V1 et V2, spécification Phase B, addendum Phase B,
pré-revues C et C2, rapport final. Le commit `45f67a2` compte **15 fichiers, +5 360 / −1**.

---

## Architecture Decisions

Toutes les décisions du lot sont tranchées. **Aucune ADR n'est modifiée** : le lot applique
[ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md) (§2.4 : une date civile n'a pas
d'heure ; §7.2 : changement de format sans dérive de schéma).

| # | Statut | Décision |
|---|---|---|
| Q1 | VALIDÉ | Reprise automatique au démarrage, avec sauvegarde, transaction, journal et `dryRun` |
| Q2 | VALIDÉ | Une valeur non réparable **bloque le démarrage** : aucune modification, erreur explicite, sauvegarde conservée |
| Q3 | REPORTÉ → P4-7 | Les valeurs `ReadableLeftAsIs` ne sont pas modifiées |
| Q7 | REPORTÉ → P7 | Commande support `dryRun` : atteignable par code seulement |
| D-B1 | VALIDÉ | Troncature réservée à la **forme exacte** d'un `DateTime` historique |
| D-B2 | CONSERVÉ | `""` sur `BirthDate` reste `Unrepairable` |
| D-B3 | VALIDÉ | Aucune valeur brute au journal ni dans les messages |
| D-B4 | → P4-6 | Sauvegarde non atomique en multi-poste |
| Q-C1 | VALIDÉ | Une valeur déjà lisible est normalisée si la troncature est neutre |
| Q-C2 | VALIDÉ | Heures bornées : `HH` 00–23, `mm` et `ss` 00–59 |
| Q-C3 | ACCEPTÉ TEMPORAIREMENT | SHA-256 non salé ; durcissement (HMAC) prévu en P7 |
| Q-C4 | VALIDÉ | `BirthDate` et `IssueDate` suivent la même politique |
| Q-C5 | VALIDÉ | Spécification Phase B alignée par l'addendum (prédicat SQL `L` remplacé par `L'`) |
| Q-C6 | VALIDÉ | `1985-03-15T12:30:45.12345678` reste `NormalizableReadable` → `1985-03-15` |

---

## Tests Validation

### Contrôles exécutés pendant cette clôture (21/09/2026, HEAD `45f67a2`)

| Contrôle | Attendu | Obtenu |
|---|---|---|
| `git status --short` | aucun fichier de code modifié | **conforme** : seuls des fichiers `docs/` apparaissent |
| `dotnet build MMV.sln -c Debug` | 0 erreur, 0 avertissement | **0 erreur, 0 avertissement** |
| `dotnet test MMV.sln -c Debug` | 1 915 / 1 915 | **1 915 / 1 915**, 0 échec, 0 ignoré |

| Projet | P4-5D (avant lot) | **P4-5D-R** | Échecs | Ignorés |
|---|---|---|---|---|
| `MMV.Domain.Tests` | 872 | **1 029** | 0 | 0 |
| `MMV.Application.Tests` | 628 | **628** | 0 | 0 |
| `MMV.App.Tests` | 258 | **258** | 0 | 0 |
| **Total** | 1 758 | **1 915** | **0** | **0** |

### Contrôles cités, non ré-exécutés

- `dotnet ef migrations has-pending-model-changes` : `No changes have been made to the model since the last
  migration.` (rapport final, contrôle de clôture du 21/09).
- Pouvoir discriminant : avec l'ancienne grammaire remise en place, **17 échecs**, exactement les tests de refus
  (pré-revue C2 §4.4).

### CI

**Non enregistrée.** `45f67a2` n'est pas poussé : `origin/p4-multi-poste` pointe sur `2976401`, et la branche
locale a **6 commits d'avance** (`b49f2ff` … `45f67a2`). Aucune CI n'a donc tourné sur ce SHA exact. Voir
Remaining Risks.

---

## Commit Reference

| Élément | Valeur |
|---|---|
| SHA | `45f67a270d926996f8cd5cd3f7b53baada030c39` |
| Message | `feat(P4-5D-R): repair legacy civil date formats at startup` |
| Parent | `023048fb2a9cd4cd2e22b173f0157a6f0c57ef60` (P4-5D) |
| Date | 21 septembre 2026 |
| Volume | 15 fichiers, +5 360 / −1 |
| Poussé | **non** |
| CI | **non enregistrée** |

**Cette clôture ne crée aucun commit.** Les deux fichiers qu'elle touche sont en attente de validation :

```
 M docs/architecture/P4-multi-poste-roadmap.md      (statut P4-5D-R, 3 emplacements)
?? docs/implementation/P4-5D-R-closure-report.md    (ce rapport)
```

Modifications de la roadmap, **limitées aux entrées de `P4-5D-R`** :

1. **Tableau du découpage de P4-5 (§2)** : cellule d'état `NOT STARTED — BLOQUE LE DÉPLOIEMENT SUR BASE
   EXISTANTE` → `COMPLETED — 45f67a2`, avec rappel de RR4.
2. **Bloc d'état courant (§6)** : ligne `P4-5D-R CIVIL DATE REPRISE` → `COMPLETED — 45f67a2 — RR4 …`.
3. **Section `P4-5D-R Civil Date Migration Preparation`** (fin de document) : statut
   `READY FOR ARCHITECT REVIEW` → `COMPLETED`, avec le commit, l'objectif, les livrables, la validation, la CI et RR4.

Les points 1 et 2 vont au-delà de la seule section de fin. Ils sont inclus parce que le rapport final signalait
cette contradiction interne à `P4-5D-R` (**RR2**) : la laisser aurait fait coexister `NOT STARTED` et `COMPLETED`
pour le même lot. **Aucune autre phase n'est touchée.** Ces deux points se retirent sans effet sur le reste si
l'architecte le demande.

Fichiers non suivis présents avant la clôture, non touchés : `docs/implementation/P4-5D-final-implementation-report.md`
et `docs/architecture/P5-product-completion-roadmap.md`.

---

## Remaining Risks

### Risques du lot

| # | Risque | Portée | Statut |
|---|---|---|---|
| **RR4** | **`dryRun` jamais exécuté sur une copie de base réelle.** Le volume réel de valeurs `Unrepairable` est inconnu. | **préalable obligatoire au déploiement** | **OUVERT** |
| RC1 / RR3 | Une seule valeur non conforme bloque le démarrage : surface de refus élargie par D-B1 puis Q-C2 | exploitation | assumé (Q2) ; mesuré par RR4 |
| RC5 | Aucun test automatique ne garde le prédicat SQL support `L'` (T-B1) | outil support | atténué par l'addendum |
| RC2 | Empreinte SHA-256 non salée : pseudonymisation faible | journal | accepté temporairement (Q-C3) → P7 |
| RC3 | `RawValue` accessible en mémoire : un futur appelant pourrait la journaliser | code | faible, commenté |
| RC7 | Décalage horaire non borné (`+99:99` accepté), jamais appliqué | règle | assumé, figé par test |
| RR5 | `ReadableLeftAsIs` reste en place : base non garantie intégralement canonique | export PostgreSQL | → P4-7 (Q3) |
| RR6 | Sauvegarde non atomique si un autre poste écrit pendant le premier démarrage | multi-poste | → P4-6 (Q5, D-B4) |
| — | Deux transactions au démarrage : un refus laisse le schéma migré mais les dates non reprises ; revenir à l'état exact exige la sauvegarde | exploitation | documenté ; T-B7 à réaliser |
| RR7 | Diagnostic complet des deux tables à chaque démarrage | performance | à mesurer sur base centrale |
| — | Tests de spécification non réalisés : T-B1 (sur `L'`), T-B4 à T-B8 | couverture | prochain lot de tests |
| **CI** | `45f67a2` et les cinq commits qui le précèdent ne sont pas poussés : aucune CI sur le SHA exact | enregistrement | **OUVERT** |

**RR4 ne bloque ni cette clôture ni P4-5E. Il bloque le déploiement sur toute installation en service.**

### Vérification de cohérence de la roadmap P4 (partie 2)

**RR2 est levé pour `P4-5D-R` seul.** Les autres écarts sont constatés ci-dessous, **sans correction** :
aucune instruction ne couvre ces sections.

| Phase | Attendu | Roadmap, tableau §2 | Roadmap, §6 | Constat |
|---|---|---|---|---|
| P4-5A | COMPLETED | `COMPLETE — DECISIONS REQUIRED` (l. 598) | `COMPLETE — DOCUMENTAIRE, AUCUN CODE` (l. 812) | **cohérent** sur le fond ; le libellé `DECISIONS REQUIRED` est dépassé, puisque P4-5B a pris ces décisions |
| P4-5B | COMPLETED | `COMPLETE — ADRs ACCEPTED` (l. 599) | `COMPLETE — 6 ADR ACCEPTED` (l. 813) | **cohérent** |
| P4-5C | COMPLETED | **`NOT STARTED`** (l. 600) | **`P4-5C … P4-5G = NOT STARTED`** (l. 817) | **INCOHÉRENT** : commit `b5c2073`, [rapport](P4-5C-ef-model-portability-report.md) présent |
| P4-5D | COMPLETED | **`NOT STARTED`** (l. 601) | **idem** (l. 817) | **INCOHÉRENT** : commit `023048f` ; son rapport final existe mais **n'est pas commité** |
| P4-5D-R | COMPLETED | `COMPLETED` (l. 602) | `COMPLETED` (l. 818) | **corrigé par cette clôture** |
| P4-5E | NEXT | `NOT STARTED` (l. 603) | `NOT STARTED` (l. 817) | **ÉCART** : `NOT STARTED` est exact, mais le statut `NEXT` n'apparaît nulle part ; ses dépendances P4-5C et P4-5D sont satisfaites |

Autres mentions périmées, relevées sans correction :

| Emplacement | Texte actuel | Écart |
|---|---|---|
| Bandeau d'introduction (l. 16–21) | « `P4-5` est ouverte avec deux sous-lots strictement documentaires […] Aucune ligne de code n'a encore été écrite pour le schéma serveur » | P4-5C, P4-5D et P4-5D-R ont écrit du code |
| Titre de P4-5 (l. 573) | `IN PROGRESS` (P4-5A et P4-5B livrés) | cinq sous-lots livrés, pas deux |
| §6, ligne P4-5 (l. 770) | « Restant : P4-5C … P4-5G, **aucune ligne implémentée** » | P4-5C, P4-5D et P4-5D-R sont implémentés |
| §6, lignes O2, O3, O4, O7 (l. 815–816) et ligne P4-4 (l. 769) | `DECIDED — NOT IMPLEMENTED` ; « Restant : O2, O3, O4 » | P4-5C (ADR 003, 006) et P4-5D (ADR 004) implémentent les décisions correspondantes, mais leurs rapports ne se rattachent pas explicitement aux numéros O. **À réévaluer par l'architecte, non affirmé ici** |
| Note de clôture P4-3 (l. 850–851) | « **P4-5 n'est pas commencée** » | énoncé au présent, aujourd'hui faux |
| Règle du découpage (l. 593–594) | chaque sous-lot « se termine par **commit + CI verte sur le SHA exact** » | aucun commit de `c9f37bc` (P4-5A) à `45f67a2` n'est poussé : la règle n'est remplie pour **aucun** sous-lot de P4-5 |

Recommandation : un lot documentaire séparé de réconciliation de P4-5 (Q6 du rapport final), sur le modèle de
P4-4C.

---

## Next Phase

**P4-5E — Chaîne de migrations PostgreSQL**, + factory design-time sélective + **double contrôle de dérive en CI**
([ADR-PROD-DB-005](../architecture/adr-prod-db-005-migration-architecture.md),
[ADR-PROD-DB-007](../architecture/adr-prod-db-007-schema-drift-prevention.md)). Dépendances selon la roadmap :
P4-5C et P4-5D, **toutes deux satisfaites en code**.

Préalables recommandés, dans l'ordre, **à arbitrer par l'architecte** :

1. **Push de `p4-multi-poste` et CI verte sur `45f67a2`**, pour satisfaire la règle « commit + CI verte sur le SHA
   exact » des six commits en attente.
2. **Enregistrement du rapport final P4-5D**, aujourd'hui non suivi.
3. **Réconciliation documentaire de P4-5** : P4-5C et P4-5D à `COMPLETED`, P4-5E à `NEXT`, obligations O2, O3, O4
   et O7, mentions périmées listées plus haut.

Point d'attention : P4-5E sera la **première modification de `.github/workflows/ci.yml` depuis P4-0** (roadmap,
note P4-5B).

**RR4 suit le déploiement, pas P4-5E** : le `dryRun` sur copie de base réelle doit être exécuté et ses
`Unrepairable` corrigés avant le premier démarrage d'une version post-P4-5D sur toute installation en service.

---

## Closure Status

```
P4-5D-R IMPLEMENTATION      = COMPLETED — 45f67a2
P4-5D-R TESTS               = 1915 / 1915 PASS
P4-5D-R BUILD               = 0 ERROR — 0 WARNING
P4-5D-R EF MIGRATION        = NONE (by design)
P4-5D-R CI                  = NOT RECORDED — COMMIT NOT PUSHED
P4-5D-R RR4                 = OPEN — BLOCKS DEPLOYMENT ON EXISTING INSTALLATIONS
P4-5D-R ROADMAP STATUS      = RECONCILED
P4-5 ROADMAP (OTHER PHASES) = INCONSISTENT — SEPARATE RECONCILIATION REQUIRED
CLOSURE COMMIT              = NOT CREATED
ARCHITECT REVIEW            = REQUIRED
```

**`P4-5D-R` est clos sur le plan fonctionnel et documentaire.** Le code est commité, les tests passent, la
roadmap porte le bon statut pour ce lot. Il reste deux conditions extérieures au lot : la CI sur le SHA exact, et
RR4 avant tout déploiement.
