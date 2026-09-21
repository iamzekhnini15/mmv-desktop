# P4-5D-R Civil Date Migration Preparation

> **Rapport de pré-revue — AUCUN COMMIT, AUCUN PUSH, AUCUNE BRANCHE.**
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de départ **et actuel** :
> **`023048fb2a9cd4cd2e22b173f0157a6f0c57ef60`**.
> Les modifications sont **locales et non validées**. Elles attendent la revue architecte.
>
> Entrées : [ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md) ·
> [P4-5D — rapport final](P4-5D-final-implementation-report.md) ·
> `LegacyCivilDateFormatTests` (P4-5D). **Le dépôt réel prime toujours sur ce document.**

---

## Objective

Lever le blocage identifié comme **la limitation n° 1 de P4-5D** : le passage de `Customer.BirthDate` et
`Prescription.IssueDate` à `DateOnly` rend **illisible toute base antérieure à P4-5D**, sans qu'aucun outil
ne le signale.

Le lot devait répondre à quatre questions. Il y répond, et **chaque réponse est exécutable** :

| Question | Réponse | Preuve |
|---|---|---|
| Comment **détecter** une ancienne base ? | `SqliteCivilDateFormatVerifier` — lecture seule, sous le modèle EF | 4 tests |
| Comment **transformer** les valeurs ? | `CivilDateFormat` — troncature classée, jamais de conversion | 36 tests |
| Comment **tester** la transformation ? | 67 tests neufs, dont une confrontation au lecteur réel | suite verte |
| Comment **déployer** cette reprise ? | intégrée au cycle de vie qui sauvegarde déjà avant mutation | 10 tests |

**Ce lot ne modifie ni le modèle, ni le schéma, ni une migration.** Il corrige des **valeurs**.

---

## Problem Analysis

### Le piège, précisément

`Customer.BirthDate` (`TEXT NULL`) et `Prescription.IssueDate` (`TEXT NOT NULL`) sont déclarées ainsi
**depuis `InitialCreate` (27 janvier 2026)** et le sont **toujours** : P4-5D n'a changé que le type CLR.
Sur SQLite, `DateTime` et `DateOnly` se stockent l'un comme l'autre en `TEXT`.

Conséquence — **vérifiée sur le dépôt, pas supposée** :

```
dotnet ef migrations has-pending-model-changes
→ No changes have been made to the model since the last migration.
```

**Aucune dérive de modèle. Aucune migration générée. Aucune migration générable.** L'outillage EF est
silencieux parce qu'il a raison : le schéma n'a pas bougé. Seul le **format des valeurs** a bougé.

### Pourquoi l'adoption de base historique ne pouvait pas suffire

C'est **la découverte structurante du lot**, et elle a décidé de l'implémentation.

Toutes les réparations de données antérieures du dépôt — R-19 (`FixDateTimeDefaultValues`), P2A-1E
(`AddDocumentSequences`), P3-2B (`AddCustomerArchivingAndProtectHistory`) — sont portées par
`AdoptHistoricalDatabase`, c'est-à-dire par le chemin des bases **sans `__EFMigrationsHistory`**. Ce chemin
fonctionne parce que ces réparations sont attachées à **une migration EF réelle**, exécutée au lieu d'être
baselinée.

Ici, **il n'existe aucune migration à laquelle s'attacher**. Et surtout : une base installée **après
P2A-1A et avant P4-5D** — le cas le plus répandu en exploitation — est déjà `MigrationsManaged`, **n'a
aucune migration en attente**, et **n'emprunte jamais le chemin d'adoption**.

> Une reprise rattachée à l'adoption aurait été **inopérante exactement là où elle est nécessaire**. Elle
> aurait été verte en test et sans effet en production.

La reprise est donc placée dans `PrepareDatabase`, **hors du `switch` d'état**, donc exécutée quel que soit
le chemin. Le test `UneBaseGereeParMigrations_MaisAnterieureAP45D_EstRepriseAuDemarrage` est précisément
celui qui aurait échoué autrement.

---

## Historical Data Formats

### Méthode

Les formats n'ont **pas** été déduits de la documentation : ils ont été **mesurés**, en écrivant des
`DateTime` par `Microsoft.Data.Sqlite` 8.0.27 (la version du dépôt) et en relisant le `TEXT` brut.

### Ce que le pilote écrivait réellement

| Valeur CLR écrite | `TEXT` stocké |
|---|---|
| `new DateTime(1985,3,15)` (minuit) | `1985-03-15 00:00:00` |
| idem, `Kind = Utc` **ou** `Local` | `1985-03-15 00:00:00` — **identique** |
| avec heure | `1985-03-15 12:30:45` |
| avec millisecondes | `1985-03-15 12:30:45.123` |
| avec ticks | `1985-03-15 12:30:45.1234567` |
| `new DateOnly(1985,3,15)` (modèle actuel) | `1985-03-15` |

**Constat n° 1 — le `Kind` ne laisse aucune trace.** Les trois `Kind` produisent le **même texte**. Une
reprise ne peut donc **pas** savoir si une valeur historique était locale ou UTC — ce qui confirme, par
l'absurde, que **toute conversion de fuseau serait une invention**. La troncature n'est pas seulement
préférable : c'est la **seule** opération justifiable.

**Constat n° 2 — séparateur espace, jamais `T`.** Le pilote n'écrit jamais la forme ISO `T`. Les valeurs
en `T` présentes en base ne peuvent venir que d'un import ou d'une intervention manuelle.

### Formats possibles, et ce que le modèle courant en fait

| Famille | Exemple | Lu par le modèle ? | Classement |
|---|---|---|---|
| Cible | `1985-03-15` | oui | `Canonical` |
| **Historique `DateTime`** | `1985-03-15 00:00:00` | **NON — `FormatException`** | `RepairableLegacyDateTime` |
| Historique fractionné | `1985-03-15 12:30:45.1234567` | **NON** | `RepairableLegacyDateTime` |
| ISO avec `T` | `1985-03-15T12:30:45.123` | **oui**, tronqué en silence | `NormalizableReadable` |
| Espace de tête | `␣1985-03-15` | **oui** | `ReadableLeftAsIs` |
| Jour/mois non complétés | `1985-3-5` | **oui** | `ReadableLeftAsIs` |
| Séparateurs `/` | `1985/03/15` | **oui** | `ReadableLeftAsIs` |
| `NULL` | — | oui (`BirthDate` seul) | `NotApplicable` |
| Texte libre | `abc`, `15/03/1985` | **NON** | `Unrepairable` |
| **Date inexistante** | `2026-02-30` | **NON** | `Unrepairable` |

### Les deux surprises, et ce qu'elles ont changé

**(a) Certaines valeurs non canoniques fonctionnent déjà.** `1985-3-5` et `␣1985-03-15` sont **lues
correctement** aujourd'hui. Une reprise qui aurait supposé « non canonique ⇒ à réparer » les aurait
**cassées**. C'est pourquoi la règle **teste la lisibilité avant toute chose**.

**(b) `date()` de SQLite invente des dates.** Mesuré :

```sql
SELECT date('2026-02-30');  -- → '2026-03-02'
```

Le 30 février devient le 2 mars, **sans erreur, sans trace**. `date()` est donc **proscrite** dans toute la
reprise ; seule `substr(…, 1, 10)` — ou son équivalent CLR — est employée, et **son résultat est validé**
avant écriture. Le test `SqliteRecaleraitSilencieusement_CeQueLaRegleRefuse` fige ce constat.

### Correction apportée à la spécification de P4-5D

`LegacyCivilDateFormatTests.LaReprisePeutSecrireEnSqlPur` (P4-5D) démontrait la faisabilité par :

```sql
UPDATE "Customers" SET "BirthDate" = substr("BirthDate", 1, 10)
WHERE "BirthDate" IS NOT NULL AND length("BirthDate") > 10;
```

**Cette requête est faisable, et elle est dangereuse.** Appliquée à `␣1985-03-15` — onze caractères, lue
parfaitement comme le **15 mars** — elle produit `␣1985-03-1`, qui **ne lève rien** et se lit désormais
comme le **1er mars**.

> Une date de naissance déplacée de quatorze jours, sans erreur, sans trace, sur une donnée de santé.

C'est **pire** qu'une `FormatException` : l'échec bruyant devient une corruption silencieuse. Ce constat —
exécuté dans `LaRepriseNaive_EnSqlDeMasse_ChangeraitSilencieusementUneDate` — est la raison pour laquelle
la reprise est **ligne à ligne et guidée par la classification**, jamais un `UPDATE` de masse.

**Le test de P4-5D n'est pas faux** — sur son corpus, il dit vrai. Il est **incomplet**, et ce lot le
complète. Aucun test existant n'a été modifié ou affaibli.

---

## Migration Strategy

### Détection

Une base nécessite une reprise ⟺ `CivilDateFormatAudit.RequiresRepair`, c'est-à-dire : **au moins une
valeur à réécrire**. Le diagnostic lit le `TEXT` **brut en ADO**, sous le modèle EF — obligatoire, puisque
interroger `context.Customers` sur une base ancienne **lève avant tout constat**. Même idiome que
`SqliteDateTimeDefaultVerifier` (P2A-1R19-R2).

Il est **en lecture seule**, ne lève jamais, et ignore table ou colonne absente : **jamais de fausse
alerte**.

### Transformation

**Troncature aux dix premiers caractères, validée avant écriture.**

```
1985-03-15 00:00:00        →  1985-03-15
1985-03-15 12:30:45.1234567→  1985-03-15
1985-03-15 23:59:59        →  1985-03-15      (jamais le 16)
1985-03-15 00:00:00        →  1985-03-15      (jamais le 14)
```

- **Perte d'heure volontaire** — une date de naissance n'a pas d'heure (ADR §2.4).
- **Aucune conversion de fuseau** — le `Kind` historique étant indétectable (voir *Historical Data
  Formats*), toute conversion serait une invention.
- **Déterministe** — fonction pure de la chaîne d'entrée, sans horloge, sans culture, sans fuseau.

### La règle, en cinq états

L'**ordre** des tests **est** la garantie de sûreté :

```
valeur NULL                                             → NotApplicable        (ne rien faire)
déjà « yyyy-MM-dd »                                     → Canonical            (ne rien faire)
LISIBLE aujourd'hui ?
   └ troncature redonne EXACTEMENT la même date         → NormalizableReadable (réécrire — neutre)
   └ sinon                                              → ReadableLeftAsIs     (ne rien faire, signaler)
ILLISIBLE :
   └ les 10 premiers caractères font une date valide    → RepairableLegacy…    (réécrire — troncature)
   └ sinon                                              → Unrepairable         (ne rien faire, SIGNALER)
```

Une valeur déjà lisible n'est réécrite que si la réécriture est **démontrablement neutre** : même date
avant et après. C'est ce qui protège `␣1985-03-15` et `1985-3-5`.

### Fidélité au lecteur réel

`CivilDateFormat.IsReadableByCurrentModel` doit se comporter **exactement** comme le lecteur
`Microsoft.Data.Sqlite`. Un détecteur qui divergerait signalerait des lignes saines ou en manquerait de
cassées — il serait **pire qu'absent**.

`LaClassification_EstFidele_AuLecteurReel` confronte les deux sur **22 formes**, et exige le même **verdict**
*et* la même **date produite**. Une montée de version du pilote qui changerait la lecture échouerait **ici**,
en test, plutôt qu'en production sur la base d'un client.

---

## Implementation

### Choix de mécanisme

Le lot autorisait quatre formes et demandait de **ne pas surdimensionner**. Retenu : **un service de
maintenance dans l'Infrastructure, branché sur le cycle de vie existant** — 720 lignes de production,
aucun projet nouveau, aucun paquet, aucune dépendance.

| Option | Verdict |
|---|---|
| Script SQL documenté seul | **rejeté** — exécution manuelle non garantie, et la forme SQL naïve **corrompt** (voir plus haut) |
| Migration EF | **impossible** — le schéma est identique ; EF ne peut rien générer. Également interdit par le lot |
| Outil de migration séparé | **rejeté** — un exécutable de plus à déployer et versionner pour une correction qui doit être **certaine** |
| **Service + branchement sur `PrepareDatabase`** | **RETENU** — la sauvegarde y est déjà prise avant mutation, le journal y est déjà écrit, le refus explicite y est déjà l'idiome |

### Fichiers

| Fichier | Lignes | Rôle |
|---|---|---|
| `src/…/Data/Time/CivilDateFormat.cs` | 194 | **La règle** — fonction pure, sans base de données, donc prouvable seule |
| `src/…/Data/Time/SqliteCivilDateFormatVerifier.cs` | 256 | **Le diagnostic** — lecture seule, compte et nomme |
| `src/…/Data/Time/SqliteCivilDateRepairService.cs` | 270 | **La reprise** — transactionnelle, idempotente, simulable |
| `src/…/Data/SqliteDatabaseManager.cs` | **+89 / −1** | branchement + politique de refus |

Le découpage n'est pas décoratif : la règle est séparée du stockage **pour que les 36 tests qui la prouvent
n'aient besoin d'aucune base**.

### Point d'insertion

```
PrepareDatabase
  ├─ sauvegarde du fichier          ← déjà là, AVANT toute mutation
  ├─ détection d'état
  ├─ fresh install | migrations | adoption   ← schéma
  └─ RepairCivilDateFormats                  ← P4-5D-R, HORS du switch : tous les chemins
```

Placée **après** le schéma et **avant** le seeding (`App.axaml.cs`), donc avant toute lecture EF de
`Customers` ou `Prescriptions`. La reprise de l'ancien fichier `mmv-optic.db`
(`LegacyDatabaseRecoveryService`) passe par le même `PrepareDatabase` : elle en bénéficie sans modification.

### PostgreSQL : hors périmètre, et structurellement hors d'atteinte

`SqliteDatabaseManager` n'est atteint **que** lorsque SQLite est le fournisseur retenu (garde-fou de
démarrage P4-3, `App.axaml.cs`). Sur PostgreSQL, `DateOnly` mappe vers un vrai type `date` : le problème de
format **n'existe pas**. **Aucun fichier PostgreSQL n'est touché.**

---

## Safety Measures

| Exigence | Mise en œuvre | Preuve |
|---|---|---|
| **Sauvegarde obligatoire** | `PrepareDatabase` sauvegarde le fichier **avant toute mutation** — garantie préexistante, non réécrite | `LaPreparation_SauvegardeLaBase_AvantDeLaReprendre` vérifie que la sauvegarde porte **encore le format historique** |
| **Validation avant écriture** | diagnostic complet **d'abord** ; toute valeur de remplacement est validée canonique avant d'être écrite | `ToutFormatIllisible_EstSoitReparable_SoitSignale` |
| **Rollback** | `SqliteDatabaseManager.Restore` ramène l'état d'origine | `LaSauvegarde_PermetDeRevenirEnArriere` — **exécuté**, pas seulement décrit |
| **Atomicité** | une transaction unique : tout ou rien | transaction explicite dans le service |
| **Aucune reprise partielle** | une valeur non réparable ⇒ **refus en bloc**, avant toute écriture | `UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier` |
| **Ne jamais deviner** | valeur non réparable laissée **intacte** et **nommée** (table, colonne, clé primaire, valeur brute) | `UneValeurIncomprehensible_EstSignaleeNommement_EtLaisseeIntacte` |
| **Simulation** | `dryRun` produit le compte exact sans écrire | `UneSimulation_NEcritRien_MaisRendLeMemeCompte` |
| **Idempotence** | rejouable sans effet ni risque | 3 exécutations successives vérifiées |
| **Traçabilité** | `CIVILDATE ok` / `detected` / `repaired` / `REFUSED` au journal de migration | `LaReprise_EstJournalisee_AvantEtApres` |
| **Contrôle d'après-coup** | si une valeur reste illisible après reprise, on **échoue** plutôt que d'affirmer le succès | garde dans `RepairCivilDateFormats` |
| **Périmètre borné** | les **19 colonnes d'instants ne sont pas touchées** | `LaReprise_NeToucheAucuneColonneDInstant` |

---

## Tests Added

**67 tests neufs**, tous verts. **Aucun test existant modifié, supprimé, ignoré ou affaibli.**

| Fichier | Tests | Objet |
|---|---|---|
| `CivilDateFormatTests` | **36** | la règle, sans base de données |
| `SqliteCivilDateRepairTests` | **21** | la reprise sur une vraie base, jusqu'à la relecture EF |
| `CivilDateRepairPreparationTests` | **10** | le cycle de vie : fichiers réels, sauvegarde, refus, journal |

Couverture demandée par le lot :

| Exigence | Couverture |
|---|---|
| `1985-03-15 00:00:00` → `1985-03-15` | ✔ règle + base + relecture EF |
| `1985-03-15 12:30:45` → `1985-03-15` | ✔ |
| `1985-03-15T12:30:45.123` → `1985-03-15` | ✔ — **voir Q3** : valeur déjà lue `1985-03-15`, remise en forme neutre |
| `abc` et formats incorrects | ✔ `abc`, `15/03/1985`, `1985/03/15`, chaîne vide, blancs, `2026-02-30`, `2025-02-29` |
| `null` | ✔ jamais touché, jamais « corrigé » |
| dates anciennes | ✔ `1900-01-01`, `1899-12-31`, `0001-01-01` (`DateTime.MinValue`) |
| dates futures | ✔ `2099-06-01`, `9999-12-31` (`DateTime.MaxValue`) |
| **idempotence** | ✔ règle (point fixe), base (3 passages), démarrage (2 lancements) |

Tests qui vont au-delà de la demande, et qui portent le raisonnement du lot :

- `LaClassification_EstFidele_AuLecteurReel` — 22 formes confrontées au **vrai** lecteur.
- `LaRepriseNaive_EnSqlDeMasse_ChangeraitSilencieusementUneDate` — la contre-preuve du `UPDATE` global.
- `SqliteRecaleraitSilencieusement_CeQueLaRegleRefuse` — `date('2026-02-30')` = `2026-03-02`, exécuté.
- `UneBaseGereeParMigrations_MaisAnterieureAP45D_EstRepriseAuDemarrage` — le cas que l'adoption manquait.
- `UneBaseVolumineuse_EstReprise_IntegralementEtSansMelange` — 28 lignes, chacune sa propre date.
- `LaReprise_NeToucheAucuneColonneDInstant` — non-régression de P4-5D.

### Résultats

```
dotnet build MMV.sln -c Debug   → 0 erreur, 0 avertissement
dotnet test  MMV.sln -c Debug   → 1 825 / 1 825
```

| Projet | Avant (P4-5D) | Après | Écart |
|---|---|---|---|
| `MMV.Domain.Tests` | 872 | **939** | **+67** |
| `MMV.Application.Tests` | 628 | 628 | — |
| `MMV.App.Tests` | 258 | 258 | — |
| **Total** | **1 758** | **1 825** | **+67** |

Aucun test ignoré.

---

## Risks

### R1 — La reprise s'exécute automatiquement au démarrage *(le risque principal — voir Q1)*

Une base ancienne est **modifiée sans décision humaine explicite**, sur des données de santé. Atténué par :
sauvegarde préalable garantie, transaction, idempotence, journal, refus en bloc sur valeur douteuse,
rollback prouvé. Le précédent du dépôt (réparation R-19 exécutée automatiquement) va dans ce sens, mais
**il portait sur des `DEFAULT` de schéma, pas sur des valeurs métier**. La différence mérite un arbitrage.

### R2 — Un refus bloque le démarrage de l'application *(voir Q2)*

Une seule date de naissance illisible empêche le lancement. C'est délibéré — une reprise partielle serait
pire — mais **c'est sévère** : la ligne fautive ferait de toute façon échouer sa propre lecture, sans
forcément bloquer le reste. Arbitrage d'exploitation.

### R3 — Valeurs lisibles non canoniques laissées en place *(voir Q3)*

`␣1985-03-15`, `1985-3-5`, `1985/03/15` restent telles quelles : elles **fonctionnent**, et les normaliser
changerait leur valeur. La base n'est donc pas garantie 100 % canonique après reprise. Sans effet sur SQLite ;
**à revoir avant l'export P4-7 vers PostgreSQL**, où le typage est strict.

### R4 — Concurrence multi-poste

`PrepareDatabase` s'exécute **par poste** (limite déjà inscrite en P4-6). Deux postes démarrant simultanément
sur une base partagée exécuteraient la reprise en parallèle. La transaction et l'idempotence rendent
l'opération **sûre**, mais la sérialisation relève de **P4-6** et n'est pas traitée ici.

### R5 — Coût de démarrage

Le diagnostic lit toutes les lignes de `Customers` et `Prescriptions` à chaque lancement. À l'échelle d'un
magasin d'optique, négligeable ; sur une base centrale multi-poste très volumineuse, à mesurer. Un
court-circuit par sondage est possible s'il devient nécessaire — non fait, faute de besoin constaté.

### R6 — Valeurs `Unrepairable` en base réelle : inconnues

Aucune base de production n'a été inspectée (aucune n'est accessible depuis ce poste). Le volume réel de
valeurs non réparables est donc **inconnu**, et il conditionne R2. **La simulation `dryRun` est faite pour
cela** : elle doit être exécutée sur une copie de base réelle avant tout déploiement.

---

## Deployment Procedure Proposal

Aucune de ces étapes n'est engagée : ce sont des **propositions** soumises à la revue.

**Étape 0 — Mesurer avant de décider.** Sur une **copie** d'une base réelle, exécuter la simulation
(`Repair(context, dryRun: true)`) et relever : lignes à réécrire, lignes non réparables, valeurs fautives
nommément. **C'est cette mesure qui tranche R2/R6**, pas une hypothèse.

**Étape 1 — Arbitrer les valeurs non réparables.** S'il y en a, les corriger à la main sur la base réelle
(le rapport les nomme : table, colonne, clé primaire, valeur), ou décider de les mettre à `NULL` pour
`BirthDate`. **Décision métier, jamais automatique.**

**Étape 2 — Déployer.** La reprise s'exécute au premier lancement de la version P4-5D+ : sauvegarde
automatique, reprise transactionnelle, journal. Aucune action opérateur.

**Étape 3 — Vérifier.** Contrôler `migration-journal.log` : une ligne `CIVILDATE repaired: …` indique le
nombre exact de valeurs reprises. Ouvrir une fiche client et une ordonnance.

**Étape 4 — En cas de problème.** Restaurer la sauvegarde horodatée du dossier `backups/`
(`SqliteDatabaseManager.Restore`), et revenir à la version précédente de l'application.

**Ordonnancement.** P4-5D-R **précède tout déploiement** de P4-5D sur une installation en service. Il ne
bloque ni P4-5E ni P4-5F, qui ne concernent que PostgreSQL.

---

## Architect Review Questions

**Q1 — Reprise automatique au démarrage, ou action de maintenance explicite ?**
Retenu : automatique, dans `PrepareDatabase` (sauvegarde déjà prise, journal, refus explicite, rollback
prouvé). Motif : une base non reprise est **inutilisable**, et faire dépendre la correction d'un geste
manuel garantit qu'elle sera oubliée quelque part. Mais cela **modifie des données de santé sans décision
humaine**, là où le précédent R-19 ne touchait que des `DEFAULT` de schéma. **Confirmez-vous l'automatisme,
ou exigez-vous un déclenchement explicite ?**

**Q2 — Le refus doit-il bloquer le démarrage ?**
Retenu : une valeur `Unrepairable` ⇒ refus en bloc, **rien n'est modifié**, la base et sa sauvegarde sont
conservées. Alternative : reprendre ce qui est reprenable, signaler le reste, laisser l'application démarrer
(la ligne fautive échouera à sa propre lecture). Le refus total est cohérent avec l'idiome `ADOPT REFUSED`,
mais il est **plus sévère que le mal qu'il prévient**. **Quel comportement retenez-vous ?**

**Q3 — Faut-il normaliser les valeurs lisibles non canoniques ?**
Retenu : oui **si et seulement si** la réécriture est démontrablement neutre (`1985-03-15T12:30:45.123` →
`1985-03-15`) ; non sinon (`␣1985-03-15`, `1985-3-5`, `1985/03/15` restent intactes). Conséquence : la base
n'est **pas** garantie intégralement canonique. **Est-ce acceptable jusqu'à P4-7, ou faut-il traiter ces
valeurs — et alors comment, puisque les réécrire changerait leur valeur ?**

**Q4 — La règle de reprise du `Kind` (ADR décision 8) est-elle close ou reportée ?**
La mesure de ce lot établit que le `Kind` historique est **indétectable** : les trois `Kind` produisent le
même `TEXT`. La décision 8 prévoit d'interpréter les instants importés comme heure locale puis de convertir.
**Cela reste entier pour les 19 colonnes d'instants en P4-7** ; ce lot ne traite que les deux dates civiles.
**Confirmez-vous ce partage ?**

**Q5 — Concurrence multi-poste : rattachement à P4-6 ?**
La reprise est transactionnelle et idempotente, donc sûre en concurrence, mais elle hérite de la limite
connue de `PrepareDatabase` (exécution par poste). **Se rattache-t-elle à la sérialisation prévue en P4-6,
ou exige-t-elle un traitement propre ?**

**Q6 — Roadmap : incohérence d'état constatée, non corrigée.**
[P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) porte encore **P4-5C** et **P4-5D** en
`NOT STARTED`, alors que les deux sont commités (`b5c2073`, `023048f`). Je **n'ai pas** modifié la roadmap :
la mise à jour des états et le rang de `P4-5D-R` relèvent de votre arbitrage. **Souhaitez-vous que cette
mise à jour soit faite, et dans quel lot ?**

**Q7 — Le service doit-il être exposé au support ?**
`SqliteCivilDateRepairService` est instancié en interne, comme les autres vérificateurs — il n'est pas
injecté ni exposé en ligne de commande. Le mode `dryRun` n'est donc atteignable que par du code.
**Faut-il une commande de maintenance pour l'exploitation (P4-8/P4-9), ou l'usage interne suffit-il ?**

---

## Architect Decisions

> **Décisions rendues par le Lead Software Architect** en revue de ce rapport (18 septembre 2026).
> Elles portent sur **Q1, Q2, Q3 et Q7**. **Q4, Q5 et Q6 ne sont pas tranchées** à ce jour et restent
> ouvertes (voir *Architect Review Questions*).

### Q1 — Réparation automatique au démarrage

**Décision : VALIDÉ.**

La réparation automatique au démarrage est acceptée.

**Conditions :**

- **backup obligatoire** avant toute modification ;
- **transaction obligatoire** ;
- **journalisation** ;
- **possibilité `dryRun`** pour le support.

*État dans l'implémentation :* les quatre conditions sont **déjà satisfaites** — sauvegarde prise par
`PrepareDatabase` avant toute mutation, transaction unique dans `SqliteCivilDateRepairService`, traces
`CIVILDATE ok` / `detected` / `repaired` / `REFUSED` au journal de migration, et paramètre
`Repair(context, dryRun: true)`. **Aucune modification de code n'est requise par cette décision.**

### Q2 — Valeurs non réparables

**Décision : VALIDÉ.**

Une valeur non réparable **bloque le démarrage**.

**Raison :** une donnée métier invalide est préférable à une corruption silencieuse.

**Comportement :**

- **aucune modification appliquée** ;
- **erreur explicite** ;
- **backup conservé**.

*État dans l'implémentation :* comportement **déjà en place** — refus en bloc avant toute écriture, valeur
fautive nommée (table, colonne, clé primaire, valeur brute), sauvegarde conservée
(`UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier`). **Aucune modification de code n'est
requise par cette décision.**

### Q3 — Valeurs lisibles mais non canoniques

**Décision : REPORTÉ.**

Les valeurs lisibles mais non canoniques **ne sont pas modifiées dans ce lot**.

La canonicalisation sera traitée **avant l'export PostgreSQL**.

**Référence future :** **P4-7 — PostgreSQL Export Validation**.

*Conséquence assumée :* la base **n'est pas garantie intégralement canonique** après reprise. Sans effet sur
SQLite, où ces valeurs sont lues correctement. Voir **R3**.

### Q7 — Mode support `dryRun`

**Décision : REPORTÉ.**

Le **mode support complet** sera traité dans une phase **Production Readiness**.

**Référence future :** **P7 — Production Readiness**.

*État dans l'implémentation :* le `dryRun` reste **atteignable par code uniquement** ; aucune commande de
maintenance n'est exposée, et **aucune ne sera ajoutée dans ce lot**.

### Questions non tranchées

| Question | Objet | État |
|---|---|---|
| **Q4** | Partage de la règle `Kind` (ADR décision 8) entre ce lot et P4-7 | **OUVERTE** |
| **Q5** | Rattachement de la concurrence multi-poste à P4-6 | **OUVERTE** |
| **Q6** | Réconciliation des états de roadmap (P4-5C, P4-5D portés `NOT STARTED`) | **OUVERTE** — la réconciliation complète est explicitement différée à un lot séparé |

---

## État final

| Contrôle | Résultat |
|---|---|
| Migrations EF créées / modifiées / supprimées | **0** — `git status --short src/MMV.Infrastructure/Migrations/` vide |
| Dérive de modèle EF | **aucune** — `No changes have been made to the model` |
| ADR modifiées | **0** — `git status --short docs/architecture/` vide |
| Fichiers PostgreSQL modifiés | **0** |
| `DateOnly` conservé, aucun retour à `DateTime` | **oui** |
| `.csproj` / `.sln` / paquets | **inchangés** |
| Build | **0 erreur, 0 avertissement** |
| Tests | **1 825 / 1 825** |
| Fichiers modifiés | **1** (`SqliteDatabaseManager.cs`, +89 / −1) |
| Fichiers ajoutés | **7** (3 sources, 3 tests, ce rapport) |
| **Commit** | **NON** |
| **Push** | **NON** |
| **Branche créée** | **NON** |
