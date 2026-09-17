# ADR-PROD-DB-006 — Index filtrés et portabilité du modèle EF

> **Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation).**
> Cet ADR tranche **l'obligation O3** d'[ADR-PROD-DB-002 §15](adr-prod-db-002-server-database-provider-selection.md)
> — le filtre d'index booléen invalide en PostgreSQL — et fixe la **règle générale de portabilité du modèle
> EF** (littéraux de type, filtres, SQL traduit). Il **ne modifie aucun fichier `.cs`, aucun test, aucune
> migration, aucune configuration et aucune CI.** L'implémentation appartient à **P4-5C**.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de décision :
> `b49f2ff7c4e3af665a767f508268377be9d19943`.
> Entrées : [audit P4-5A §PostgreSQL Index Analysis, AD-4](../implementation/P4-5A-postgresql-schema-audit-report.md) ·
> [ADR-PROD-DB-002 §12.3, §15 O3, §16.2](adr-prod-db-002-server-database-provider-selection.md).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.**

---

## 2. Contexte

### 2.1 Seize index, **un seul** réellement invalide

L'audit P4-5A a recensé **16 déclarations d'index**, dont **deux filtrés**. Contrairement à ce qu'une lecture
rapide suggérait, **un seul** pose problème.

| Index filtré | Filtre | PostgreSQL |
|---|---|---|
| [`idx_workshop_sheets_current_unique`](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs#L62) | `"IsCurrent" = 1` | **❌ INVALIDE** |
| [`idx_notifications_active_low_stock_unique`](../../src/MMV.Infrastructure/Data/Configurations/NotificationConfiguration.cs#L64) | `"Type" = 'LowStock' AND "EntityType" = 'Product' AND "EntityId" IS NOT NULL AND "ResolvedAt" IS NULL` | **✔ VALIDE tel quel** |

Les 14 autres index ne contiennent **aucun** littéral spécifique à un moteur.

### 2.2 Pourquoi `"IsCurrent" = 1` échoue

`STATIC_CODE_PROOF` + `OFFICIAL_SEMANTICS`.

- **SQLite n'a pas de type booléen** : `IsCurrent` y est physiquement un `INTEGER` (0/1), et `= 1` est une
  comparaison valide.
- **PostgreSQL crée une colonne `boolean`** : `"IsCurrent" = 1` compare un `boolean` à un `integer`, ce que
  PostgreSQL **refuse** — il n'existe aucune conversion implicite booléen ↔ entier. **La création de l'index
  échoue, donc la création du schéma échoue.**
- La forme PostgreSQL correcte est `WHERE "IsCurrent"`.

Le filtre est déclaré **à deux endroits** : la configuration EF
([WorkshopSheetConfiguration.cs:62](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs#L62))
et la migration SQLite `AddWorkshopSheets` — cette dernière étant **intouchable**
([ADR-PROD-DB-005 §2.2](adr-prod-db-005-migration-architecture.md)).

L'adaptation existe **côté spike uniquement** (`AdaptedOpticDbContext`, ADR-PROD-DB-002 §16.2) : **les
mappings et migrations de production ne l'ont pas.**

### 2.3 Ce que cet index garantit — et pourquoi il ne peut pas simplement disparaître

Il garantit qu'il existe **au plus une version courante par commande**. C'est le filet structurel
anti-ambiguïté de la bascule « ancienne version à `false` / nouvelle à `true` », arbitrée par la base en
concurrence. **Sa perte serait une perte de garantie métier, pas une perte d'index.** Il doit être recréé
**et prouvé**, jamais supposé.

### 2.4 Le filtre `Notifications` est portable — et cela réduit le périmètre

Il ne contient que des comparaisons de texte et des tests `IS NULL` / `IS NOT NULL` : tous valides et
**immuables** en PostgreSQL, condition requise pour un index partiel. Le comportement des `NULL` dans un
index unique (distincts par défaut) est **identique** sur les deux moteurs. **Le périmètre d'O3 se réduit
donc à un seul index.**

### 2.5 Un motif de dialecte provider existe déjà, en production, et il est prouvé

[`NotificationRepository.ActiveLowStockInsertSql`](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L179-L201)
sélectionne le SQL sur `Database.ProviderName` : SQLite/PostgreSQL → `ON CONFLICT DO NOTHING`, SQL Server →
`INSERT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))`, **provider inconnu → lève**. Cette forme exacte a
été mesurée par le spike P4-1 (E5). **Le motif n'est donc pas à inventer : il est à réutiliser.**

### 2.6 Autres divergences recensées — traitées ici pour clore le sujet

| Sujet | SQLite | PostgreSQL | Portée |
|---|---|---|---|
| **30 `HasColumnType` écrits à la main** | 29 × `"REAL"`, 1 × `"TEXT"` | chaîne transmise **verbatim** au provider actif | 11 monétaires → [ADR-PROD-DB-003](adr-prod-db-003-money-persistence.md) ; **18 optiques** et 1 `TEXT` → **ici** |
| `lower()` | ne replie que l'ASCII A–Z | **sensible à la collation**, replie accentués et Unicode | la recherche remontera **plus** de résultats sur PostgreSQL : [CustomerRepository.cs:38-42](../../src/MMV.Infrastructure/Repositories/CustomerRepository.cs#L38-L42), [ProductRepository.cs:124-127](../../src/MMV.Infrastructure/Repositories/ProductRepository.cs#L124-L127) |
| `LIKE` | insensible à la casse en ASCII | **sensible à la casse** | neutralisé : les deux côtés sont déjà passés en minuscules |
| Identifiants | non sensibles à la casse | `"PascalCase"` **exige les guillemets** | EF les cite toujours ; impact **opérationnel** sur les requêtes manuelles |

---

## 3. Question de décision

> **Comment MMV déclare-t-il un index filtré, et plus généralement une construction dépendante du moteur,
> dans un modèle EF unique servant deux providers ?**

---

## 4. Options comparées

### 4.1 Le filtre `"IsCurrent" = 1`

| Option | Description | Verdict |
|---|---|---|
| **I1 — filtre sélectionné par provider dans la configuration EF** | `"IsCurrent" = 1` pour SQLite, `"IsCurrent"` pour PostgreSQL | **RETENUE** — motif **déjà en production et prouvé** (§2.5) ; SQLite **inchangé au caractère près** |
| **I2 — filtre retiré du modèle, écrit seulement dans les migrations** | le modèle ignore le filtre | **REJETÉE** — crée une **dérive permanente** modèle ↔ base et casse `has-pending-model-changes`, donc la garde d'[ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md) |
| **I3 — supprimer le filtre, garantir en applicatif** | contrôle `check-then-act` | **REJETÉE** — perte de garantie structurelle, contraire au principe P3 « **la base décide** » ; en concurrence multi-poste, un contrôle applicatif ne garantit rien |
| **I4 — rendre l'index unique non filtré** | unicité sur `(OrderId)` | **REJETÉE** — interdirait toute version non courante, c'est-à-dire tout historique de fiches |

### 4.2 Les littéraux de type restants

| Option | Description | Verdict |
|---|---|---|
| **L1 — retirer `HasColumnType("REAL")` des colonnes optiques** | mapping par défaut : `double precision` (PostgreSQL), `REAL` (SQLite) | **RETENUE** — `double` → `float8` **ne perd rien**, et le littéral disparaît |
| **L2 — conserver le littéral** | statu quo | **REJETÉE** — `REAL` désigne **4 octets** en PostgreSQL : perte de précision gratuite sur une mesure optique |

---

## 5. Décision

1. **Le filtre d'index est sélectionné par provider**, dans la configuration EF, via le **même motif** que
   `ActiveLowStockInsertSql` : forme SQLite `"IsCurrent" = 1`, forme PostgreSQL `"IsCurrent"`, et **un
   provider inconnu lève** — jamais de filtre deviné, jamais d'index créé sans filtre par défaut.
2. **Le nom du provider est fourni au modèle par un point unique**, à la construction du modèle
   (`OnModelCreating`), et transmis aux configurations qui en ont besoin. **Aucune configuration d'entité ne
   va chercher le provider pour son propre compte.**
3. **La garantie métier reste celle de la base.** L'index unique filtré « une seule version courante par
   commande » est **conservé sur les deux providers**. Aucune des deux formes n'est un contrôle applicatif.
4. **Le filtre `Notifications` n'est pas touché** : il est portable tel quel. Le modifier serait un risque
   gratuit.
5. **Les 18 `HasColumnType("REAL")` optiques restants** — après traitement des 11 monétaires par
   [ADR-PROD-DB-003](adr-prod-db-003-money-persistence.md) — **sont retirés** : le mapping par défaut donne
   `double precision` sur PostgreSQL et `REAL` sur SQLite, sans perte et sans littéral de provider. Les
   colonnes de puissance `decimal` conservent leur `HasPrecision(5,2)`.
6. **`Product.TechnicalSpecs` conserve `HasColumnType("TEXT")`** : `text` est un type PostgreSQL valide.
   **`jsonb` est rejeté pour V1** — aucun besoin de requête JSON n'est constaté dans le dépôt.
7. **Règle générale de portabilité du modèle**, applicable à partir de P4-5C :
   - toute construction dépendante du moteur (filtre d'index, type physique, SQL brut) est soit
     **provider-neutre**, soit **explicitement sélectionnée par provider en un point unique d'Infrastructure**,
     avec **échec explicite** sur provider inconnu ;
   - **aucun littéral de type propre à un moteur** ne subsiste sans justification écrite ;
   - **aucune mention de provider** dans Domain ni Application (**O14**).
8. **Un index filtré doit être vérifié physiquement**, dans `pg_index` / `information_schema`, jamais déduit
   du modèle. **Un index absent ne fait échouer aucun test fonctionnel : il ne se voit qu'en concurrence, et
   trop tard.**
9. **La divergence de `lower()` est actée, pas subie** : sur PostgreSQL la recherche client et produit
   remontera **davantage** de résultats (accents, Unicode). Le comportement est jugé **souhaitable** pour un
   magasin d'optique, et il est **verrouillé par test sur chaque provider** (P4-5F). **Le comportement
   attendu est désormais celui de PostgreSQL** ; celui de SQLite est documenté comme une limitation du
   provider de développement.
10. **`ToLower()` vs `ToLowerInvariant()`** : signalé — `searchTerm.ToLower()` est **sensible à la culture**.
    **Hors périmètre P4-5**, à traiter comme une correction propre, non mélangée au portage.

---

## 6. Ce que cet ADR ne dit pas

- Il ne modifie **aucune** migration SQLite : la forme `"IsCurrent" = 1` de `AddWorkshopSheets` **reste**
  telle quelle ([ADR-PROD-DB-005 §2.2](adr-prod-db-005-migration-architecture.md)).
- Il ne crée **aucun** index nouveau et n'optimise aucune requête : la performance PostgreSQL n'est pas le
  sujet de P4-5.
- Il ne décide **pas** des collations PostgreSQL (`ICU`, `citext`) : aucun besoin n'est établi, et l'ajouter
  maintenant introduirait une divergence non mesurée.

---

## 7. Conséquences

### 7.1 Positives

- **Le blocage B3 de la création du schéma disparaît** pour le volet index : c'est une condition de P4-5E.
- **La garantie « une seule version courante » est préservée sur les deux moteurs**, sans rien déplacer vers
  l'applicatif.
- **SQLite est inchangé au caractère près** : ni migration, ni rebuild, ni changement de comportement.
- **Le motif de sélection par provider devient explicite et unique**, au lieu d'être un cas particulier
  logé dans un repository.
- **Le périmètre réel est petit** : un filtre, 18 littéraux, aucune autre réécriture d'index.

### 7.2 Négatives — assumées

- **Le modèle EF devient dépendant du provider**, ce qui **exige** deux instantanés
  ([ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md)). Les deux décisions sont solidaires.
- **Le risque de filtre faux est silencieux** : un index partiel mal filtré ne casse rien tant qu'aucune
  course ne se produit. D'où §5.8 — **vérification physique obligatoire**.
- **La recherche ne rendra pas les mêmes résultats selon le provider** : accepté, documenté, verrouillé par
  test.

### 7.3 Neutres

- Les 14 index non filtrés sont portables sans modification.
- Les noms d'index sont déjà globalement distincts : aucun conflit d'espace de noms par schéma.

---

## 8. Obligations d'implémentation créées

| # | Obligation | Lot | Bloquante |
|---|---|---|---|
| **X1** | Sélectionner le filtre `idx_workshop_sheets_current_unique` par provider, échec explicite sur provider inconnu | P4-5C | **OUI** |
| **X2** | Fournir le nom du provider au modèle en **un point unique** ; aucune configuration ne l'interroge seule | P4-5C | **OUI** |
| **X3** | Retirer les **18** `HasColumnType("REAL")` optiques | P4-5C | **OUI** |
| **X4** | Vérifier que la chaîne SQLite reste inchangée (contrôle de dérive vert, 14 migrations intactes, **1569** tests) | P4-5C | **OUI** |
| **X5** | Vérifier **physiquement** les deux index filtrés sur PostgreSQL (`pg_index`), ainsi que les uniques, les FK et leurs comportements de suppression | P4-5F | **OUI** |
| **X6** | Verrouiller par test le comportement de recherche `lower()` **sur chaque provider** | P4-5F | moyenne |

---

## 9. Conditions de réexamen

- Si une garantie métier nouvelle exigeait un index partiel dont le prédicat **n'est pas immuable** en
  PostgreSQL ⇒ l'index partiel est impossible : revenir à une contrainte ou à un déclencheur, **jamais** à un
  contrôle applicatif.
- Si la recherche insensible aux accents devenait un besoin explicite ⇒ rouvrir §6 (collation `ICU`,
  `unaccent`, `citext`), avec mesure.
- Si un troisième provider était introduit ⇒ le `switch` de §5.1 doit être **complété**, et son cas par
  défaut continue de lever.

---

## 10. Références

- [ADR-PROD-DB-002 §12.3, §15 O3, §16.2](adr-prod-db-002-server-database-provider-selection.md)
- [Audit P4-5A §PostgreSQL Index Analysis, §16, §17, AD-4](../implementation/P4-5A-postgresql-schema-audit-report.md)
- [ADR-PROD-DB-003 — persistance monétaire](adr-prod-db-003-money-persistence.md)
- [ADR-PROD-DB-005 — architecture des migrations](adr-prod-db-005-migration-architecture.md)
- [ADR-PROD-DB-007 — prévention de la dérive de schéma](adr-prod-db-007-schema-drift-prevention.md)
- [Rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md)
