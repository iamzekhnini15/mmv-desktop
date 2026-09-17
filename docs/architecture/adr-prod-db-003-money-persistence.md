# ADR-PROD-DB-003 — Persistance monétaire (MMV V1 multi-poste)

> **Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation).**
> Cet ADR tranche **l'obligation O2** d'[ADR-PROD-DB-002 §15](adr-prod-db-002-server-database-provider-selection.md) :
> le **type physique et la précision** des colonnes monétaires sur la base de production PostgreSQL, et le
> sort du mapping SQLite existant. Il **ne modifie aucun fichier `.cs`, aucun test, aucune migration, aucune
> configuration et aucune CI.** L'implémentation appartient à **P4-5C**.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de décision :
> `b49f2ff7c4e3af665a767f508268377be9d19943`.
> Entrées : [audit P4-5A](../implementation/P4-5A-postgresql-schema-audit-report.md) §Money Type Analysis ·
> [ADR-PROD-DB-002 §12.2, §16.3, §15 O2](adr-prod-db-002-server-database-provider-selection.md) ·
> [rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.** Décision d'architecture. Aucune ligne de code n'est écrite par cet ADR.

---

## 2. Contexte

### 2.1 L'état mesuré du dépôt

**14 colonnes monétaires**, réparties en **deux mappings incohérents** (audit P4-5A §8) :

| Mapping actuel | Colonnes | Type physique SQLite |
|---|---|---|
| `HasColumnType("REAL")` explicite | 11 — `Products.PurchasePrice`, `Products.SalePrice`, `Products.RecommendedPrice`, `Sales.TotalAmount`, `Sales.DiscountAmount`, `Sales.FinalAmount`, `Sales.DepositAmount`, `Sales.RemainingAmount`, `SaleItems.UnitPrice`, `SaleItems.TotalPrice`, `OrderItems.UnitPrice` | `REAL` (flottant IEEE 754 **8 octets**) |
| aucun mapping déclaré | 3 — `Supplements.SupplementPrice`, `GlassPricingTiers.PurchasePriceGrid`, `GlassPricingTiers.SalePriceGrid` | `TEXT` (mapping EF par défaut de `decimal` sur SQLite) |

**Aucune** de ces 14 colonnes ne déclare `HasPrecision`. Le type CLR est **`decimal`** partout — le Domain
est donc **déjà correct** ; le défaut est **entièrement dans le mapping**.

Le value object [`Money`](../../src/MMV.Domain/ValueObjects/Money.cs) (`decimal`,
`decimal.Round(…, 2, MidpointRounding.ToEven)`) **n'est pas persisté** : il ne vit que dans les commandes de
la couche Application. **L'intention métier — deux décimales exactes, arrondi bancaire — est donc déjà
écrite dans le code, mais n'est appliquée nulle part au niveau de la base.**

### 2.2 Le fait décisif — `REAL` ne désigne pas le même type des deux côtés

`OFFICIAL_SEMANTICS`. `HasColumnType("REAL")` transmet la chaîne **verbatim** au provider actif :

| Moteur | Ce que `REAL` désigne | Chiffres significatifs |
|---|---|---|
| SQLite | flottant IEEE 754 **8 octets** (équivalent `double`) | ~15 |
| PostgreSQL | `float4`, flottant **4 octets** | **~6** |

Porter le littéral tel quel ne conserverait donc pas le statu quo : il **diviserait par deux une précision
déjà insuffisante**. Un montant tel que `12 345,67` n'est **pas** représentable fidèlement en `float4`.
**C'est une régression, pas un report de dette.**

### 2.3 Deux sites où la base — et non l'application — opère sur un montant

Constat **nouveau**, non relevé par l'audit P4-5A, et **structurant pour cette décision**
(`STATIC_CODE_PROOF`) :

| Site | Opération SQL | Enjeu |
|---|---|---|
| [SaleRepository.cs:118](../../src/MMV.Infrastructure/Repositories/SaleRepository.cs#L118) | `UPDATE … WHERE "SaleId" = @id AND "RemainingAmount" > 0` — **primitive CAS** du règlement de solde | La **comparaison monétaire est évaluée par la base**. C'est elle, et elle seule, qui garantit qu'un solde déjà réglé ne peut pas l'être une seconde fois. |
| [SaleRepository.cs:92](../../src/MMV.Infrastructure/Repositories/SaleRepository.cs#L92) | `SELECT SUM("FinalAmount") …` | **Agrégation côté serveur** sur une colonne monétaire. |

Tous les autres calculs monétaires recensés dans `src/**` sont **en mémoire** sur des collections déjà
chargées (`OrderFormViewModel.TotalAmount`, `DbInitializer`), **jamais** traduits en SQL.

**Conséquence** : le type physique d'une colonne monétaire n'est pas un simple sujet de fidélité de
stockage. Sur ces deux sites, il **détermine la sémantique d'une garantie métier**. Un mapping qui
transformerait `"RemainingAmount" > 0` en **comparaison lexicographique de texte** ferait passer la chaîne
`'0.00'` pour strictement supérieure à `'0'` — donc **autoriserait un second règlement d'un solde déjà
réglé**, sans erreur ni signal. Cette contrainte **élimine toute solution qui basculerait les colonnes
SQLite concernées sur `TEXT`**.

### 2.4 Ce qui est hors de cette décision

**22 colonnes optiques** (`Sphere`, `Cylinder`, `Addition`, `PrismValue`, `Transposed*`, `Od*`/`Og*`) portent
également `HasColumnType("REAL")` mais sont typées **`double`** en CLR. La dioptrie se mesure par pas de
0,25 ; le flottant y est adéquat. [ADR-PROD-DB-002 §15 O2](adr-prod-db-002-server-database-provider-selection.md)
les **exclut explicitement**. Elles relèvent d'[ADR-PROD-DB-006 §4](adr-prod-db-006-index-and-model-portability.md),
**pas d'ici**.

---

## 3. Question de décision

> **Quel type physique et quelle précision les 14 colonnes monétaires doivent-elles porter sur la base de
> production PostgreSQL, et que devient leur mapping SQLite ?**

---

## 4. Options comparées

### Option A — `decimal` + `HasPrecision(12,2)`, mapping unique pour les deux providers

Retrait des 11 littéraux `"REAL"`, ajout de `HasPrecision(12, 2)` sur les 14 colonnes.
⇒ PostgreSQL : `numeric(12,2)`. ⇒ SQLite : mapping `decimal` par défaut, soit **`TEXT`**.

- **Avantages** : un seul mapping ; aucune mention de provider ; incohérence `REAL`/`TEXT` supprimée des
  deux côtés ; exactitude décimale en production.
- **Inconvénients rédhibitoires** : bascule **les 11 colonnes SQLite de `REAL` vers `TEXT`**, donc
  (a) `"RemainingAmount" > 0` devient une **comparaison de chaînes** (§2.3) — perte d'une garantie métier
  sur le provider de dev/test/démo ; (b) `SUM("FinalAmount")` passe par une coercition de texte ;
  (c) impose un **rebuild de table** à toutes les bases SQLite déjà déployées, pour un provider qui **n'est
  pas** celui de la production.

### Option B — Entier de centimes (`long`) + conversion de valeur

- **Avantages** : exact par construction ; identique sur tout moteur ; comparaisons et sommes entières.
- **Inconvénients** : **réécriture large** du Domain, de l'Application et de l'UI (14 propriétés, tous les
  calculs, toutes les liaisons XAML) ; **illisibilité des données en base** pour l'exploitant et pour tout
  outil tiers ; conversion obligatoire à l'import P4-7 ; ampleur **sans rapport** avec le problème posé.
  **Surdimensionnée pour V1.**

### Option C — Précision déclarée, **type physique sélectionné par provider**

`HasPrecision(12, 2)` sur les 14 colonnes **+** conservation du type physique SQLite actuel, par un **point
de sélection unique** (une extension de mapping recevant le nom du provider), sur le modèle **déjà en
production et prouvé** de
[`NotificationRepository.ActiveLowStockInsertSql`](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L179-L201).

⇒ PostgreSQL : `numeric(12,2)`, exact. ⇒ SQLite : **inchangé au caractère près**.

- **Avantages** : exactitude en production ; **aucune régression** sur les deux sites SQL de §2.3 ;
  **aucune migration SQLite**, donc aucun rebuild sur les bases déployées ; couplage confiné à **une**
  fonction d'Infrastructure.
- **Inconvénients** : le modèle EF devient **dépendant du provider** (il l'est déjà pour l'upsert, et le
  deviendra pour l'index filtré — [ADR-PROD-DB-006](adr-prod-db-006-index-and-model-portability.md)) ; la
  **dette monétaire SQLite subsiste** en dev/test/démo/mono-poste ; **la suite de tests SQLite ne peut pas
  prouver l'exactitude monétaire** — seule la suite d'intégration PostgreSQL le peut.

---

## 5. Décision

**Option C est retenue.**

1. **Le type CLR reste `decimal`.** Le Domain n'est pas touché. `Money` reste l'expression de l'intention
   métier (2 décimales, `MidpointRounding.ToEven`).
2. **Les 14 colonnes monétaires déclarent `HasPrecision(12, 2)`.** Sur PostgreSQL, elles sont donc
   **`numeric(12,2)`** : exactes en base 10, contraintes à deux décimales. Douze chiffres couvrent
   9 999 999 999,99 € — sans commune mesure avec le besoin d'un magasin d'optique.
3. **Aucun littéral `"REAL"` n'est transmis à PostgreSQL.** Les 11 `HasColumnType("REAL")` monétaires sont
   retirés du chemin PostgreSQL **sans exception**.
4. **Le mapping physique SQLite reste celui d'aujourd'hui** — 11 `REAL`, 3 `TEXT` — appliqué par un **point
   de sélection unique** par provider, en Infrastructure. **Motif : ne pas transformer une garantie métier
   arbitrée par la base (§2.3) en comparaison de chaînes, et ne pas imposer un rebuild de table aux bases
   déployées.**
5. **La sélection par provider est confinée à une seule fonction d'Infrastructure.** Aucune configuration
   d'entité ne teste le provider pour son propre compte ; aucune mention de provider n'apparaît dans Domain
   ni Application (**O14** préservée, vérifiée par `ProviderNeutralityArchitectureTests`).
6. **La dette monétaire SQLite est enregistrée, pas résolue.** Elle est **explicitement acceptée** pour
   dev/test/démo/mono-poste et **n'est pas** un critère de sortie P4.
7. **L'exactitude monétaire ne peut être prouvée que contre PostgreSQL.** Les tests de fidélité appartiennent
   à la suite d'intégration d'[ADR-PROD-DB-008](adr-prod-db-008-postgresql-integration-testing.md). **Un test
   monétaire vert sur SQLite ne vaut pas preuve d'exactitude** et ne doit jamais être présenté comme tel.
8. **O2 précède O11.** Le schéma PostgreSQL doit porter `numeric(12,2)` **avant** tout import (**P4-7**).
   L'ordre inverse figerait l'altération flottante dans la base de production.

### 5.1 Conversion à l'import — contrainte transmise à P4-7

Les montants déjà écrits en `REAL` sont **altérés à la source** (mesuré : ADR-PROD-DB-002 §12.2). L'import
SQLite → PostgreSQL **doit** :

- convertir explicitement en `numeric(12,2)` avec **arrondi à deux décimales, `MidpointRounding.ToEven`**,
  cohérent avec `Money` ;
- produire une **réconciliation vérifiable** (totaux avant/après, écart par table) ;
- **ne jamais** présenter le résultat comme une restitution fidèle de valeurs qui ne l'étaient plus.

---

## 6. Ce que cet ADR ne dit pas

- Il ne choisit **pas** de type pour les 22 colonnes optiques `double` → [ADR-PROD-DB-006 §4](adr-prod-db-006-index-and-model-portability.md).
- Il ne change **pas** `Product.TechnicalSpecs` (`HasColumnType("TEXT")`, JSON stocké en texte) : `text` est
  un type PostgreSQL valide, ce mapping est **portable tel quel**. Le passage à `jsonb` est **rejeté pour
  V1** — aucun besoin de requête JSON n'est constaté dans le dépôt.
- Il ne traite **pas** l'incohérence de typage CLR de `GlassPricingTiers.PowerMin`/`PowerMax` (`decimal`,
  `HasPrecision(5,2)`, colonnes de **puissance** et non de prix) : signalée, sans conséquence de portage,
  hors périmètre P4.
- Il ne persiste **pas** `Money`. Le sujet reste ouvert, hors P4.
- Il n'écrit **aucune** migration.

---

## 7. Conséquences

### 7.1 Positives

- **Exactitude financière en production** : `numeric(12,2)`, arithmétique décimale exacte, tri et agrégation
  numériques corrects, deux décimales **contraintes par la base**.
- **La régression `float8` → `float4` est évitée** — elle était certaine en portage naïf.
- **Aucune migration SQLite, aucun rebuild de table** sur les bases déployées : le modèle vu par SQLite est
  **inchangé**, donc les 14 migrations historiques restent valides et `has-pending-model-changes` reste vert
  sur la chaîne SQLite. **Le risque R-6 de l'audit P4-5A — casser la chaîne SQLite — disparaît pour le volet
  monétaire.**
- **Les deux sites SQL de §2.3 conservent exactement leur sémantique actuelle sur SQLite** et **gagnent**
  l'exactitude sur PostgreSQL.
- **Prérequis O11 satisfait** : l'import aura une cible exacte.

### 7.2 Négatives — assumées

- **Le modèle EF devient dépendant du provider.** Deux instantanés de modèle deviennent donc **nécessaires**
  — ce qu'[ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md) décide indépendamment. Les deux
  décisions se tiennent ; l'une sans l'autre serait incohérente.
- **La dette monétaire SQLite subsiste** : 11 colonnes en flottant, 3 en texte, en dev/test/démo/mono-poste.
  Un magasin resté mono-poste conserve donc le défaut. **C'est le prix assumé de la non-régression.**
- **La suite SQLite ne prouve rien sur l'exactitude monétaire** — elle ne le prouvait pas davantage
  auparavant, mais la répartition des preuves devient explicite : **la fidélité monétaire n'est verte que
  contre PostgreSQL**.
- **Une divergence de comportement entre providers est officialisée** : un arrondi observable sur SQLite ne
  le sera pas sur PostgreSQL. Tout test monétaire doit donc désigner **le provider qu'il prouve**.

### 7.3 Neutres

- Domain, Application et UI ne changent pas : `decimal` reste `decimal`.
- Les 22 colonnes optiques ne sont pas touchées par cette décision.

---

## 8. Obligations d'implémentation créées

| # | Obligation | Lot | Bloquante |
|---|---|---|---|
| **M1** | Déclarer `HasPrecision(12, 2)` sur les **14** colonnes monétaires | P4-5C | **OUI** |
| **M2** | Retirer les **11** `HasColumnType("REAL")` monétaires du chemin PostgreSQL, via un **point de sélection unique** par provider conservant le type SQLite actuel | P4-5C | **OUI** |
| **M3** | Vérifier que la chaîne SQLite reste **inchangée** : contrôle de dérive vert côté SQLite, **aucune migration SQLite produite par le volet monétaire**, **1569** tests conservés | P4-5C | **OUI** |
| **M4** | Prouver `numeric(12,2)` **physiquement** (`information_schema.columns`) sur la base PostgreSQL | P4-5F | **OUI** |
| **M5** | Prouver la **fidélité monétaire** aller-retour contre PostgreSQL : bornes, deux décimales, arrondi bancaire, égalité **exacte** | P4-5F | **OUI** |
| **M6** | Prouver que `"RemainingAmount" > 0` et `SUM("FinalAmount")` conservent leur sémantique **sur les deux providers** | P4-5F | **OUI** |
| **M7** | Transmettre à P4-7 la règle de conversion et de réconciliation de §5.1 | P4-7 | **OUI** |

---

## 9. Conditions de réexamen

- Si un besoin métier exige plus de deux décimales (devise à trois décimales, prix unitaire fractionné) ⇒
  rouvrir la **précision**, pas le type.
- Si un montant peut dépasser 9 999 999 999,99 ⇒ rouvrir `(12,2)`.
- Si une opération monétaire **traduite en SQL** est ajoutée (comparaison, tri, agrégat, `ExecuteUpdate`
  conditionnel) sur une colonne restée `REAL`/`TEXT` en SQLite ⇒ **réexaminer le point 4** : la
  non-régression cesserait d'être garantie.
- Si SQLite devenait une cible de production monétaire ⇒ la dette de §7.2 deviendrait bloquante.

---

## 10. Références

- [ADR-PROD-DB-002 §12.2, §15 O2, §16.3](adr-prod-db-002-server-database-provider-selection.md)
- [Audit P4-5A §Money Type Analysis, §4, AD-1](../implementation/P4-5A-postgresql-schema-audit-report.md)
- [ADR-PROD-DB-005 — architecture des migrations](adr-prod-db-005-migration-architecture.md)
- [ADR-PROD-DB-006 — index et portabilité du modèle](adr-prod-db-006-index-and-model-portability.md)
- [ADR-PROD-DB-008 — tests d'intégration PostgreSQL](adr-prod-db-008-postgresql-integration-testing.md)
- [Rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md)
