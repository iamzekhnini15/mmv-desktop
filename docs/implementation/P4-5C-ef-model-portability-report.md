# P4-5C — EF Model Portability

> **Lot d'implémentation.** Premier lot de P4-5 qui **écrit du code**. P4-5A (audit) et P4-5B (six ADR)
> étaient strictement documentaires.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`.
> SHA de départ : **`9247497d597bf6c8ff630035323ef4f8c0fd69ef`** (`docs(P4-5B): define PostgreSQL architecture decisions`).
> **Le dépôt réel prime toujours sur ce document.**

---

## Objective

Rendre le modèle EF Core unique de MMV capable de servir **SQLite** (développement, test, démonstration,
mono-poste local) **et PostgreSQL** (serveur de production V1 multi-poste), en appliquant les décisions
d'[ADR-PROD-DB-003](../architecture/adr-prod-db-003-money-persistence.md) et
d'[ADR-PROD-DB-006](../architecture/adr-prod-db-006-index-and-model-portability.md) — **sans** casser le
Domain, les tests existants ni les 14 migrations SQLite historiques.

Ce lot **prépare** P4-5E (chaîne de migrations PostgreSQL) ; il **ne crée aucune migration**.

Périmètre exact traité : obligations **M1, M2, M3** (ADR-003 §8) et **X1, X2, X3, X4** (ADR-006 §8).

---

## Starting State

État mesuré sur `9247497`, **avant toute modification** :

| Mesure | Valeur mesurée | Commande |
|---|---|---|
| Build `MMV.sln` | **0 erreur, 0 avertissement** | `dotnet build MMV.sln -c Debug` |
| Tests | **1569** (Domain 710 · Application 620 · App 239), 0 échec, 0 ignoré | `dotnet test MMV.sln` |
| Dérive de modèle EF | **aucune** — *« No changes have been made to the model since the last migration »* | `dotnet ef migrations has-pending-model-changes` |
| Migrations SQLite | **14**, plus `OpticDbContextModelSnapshot.cs` | — |
| `HasColumnType` écrits à la main (hors migrations) | **30** — 29 × `"REAL"`, 1 × `"TEXT"` | `grep` |
| Colonnes monétaires | **14** (`decimal` en CLR) : 11 en `REAL` explicite, 3 sans mapping déclaré (`TEXT` par défaut SQLite) | — |
| `HasPrecision` sur une colonne monétaire | **0** | — |
| Index filtrés | **2** — `idx_workshop_sheets_current_unique` (`"IsCurrent" = 1`, **invalide en PostgreSQL**) et `idx_notifications_active_low_stock_unique` (**portable tel quel**) | — |
| Point de sélection par provider existant | **1**, en repository : `NotificationRepository.ActiveLowStockInsertSql` (P4-1 Lot D) | — |

### Note d'environnement — restauration NuGet

L'audit P4-5A n'avait **pas pu exécuter de test** : la restauration NuGet était indisponible sur ce poste
(son §0.2). La cause a été identifiée ici : **`nuget.org` n'est pas une source NuGet enregistrée** sur la
machine (seules `C:\Program Files\dotnet\library-packs` et *Microsoft Visual Studio Offline Packages* le
sont), et le cache global ne contenait pas les versions demandées. Le réseau, lui, est disponible.

La restauration a donc été faite en passant la source **explicitement sur la ligne de commande** —
`dotnet restore MMV.sln --source https://api.nuget.org/v3/index.json` et
`dotnet tool restore --add-source …` pour `dotnet-ef`. **Aucun fichier de configuration NuGet n'a été créé
ni modifié**, ni dans le dépôt, ni sur la machine. La CI n'est pas concernée : elle restaure depuis ses
propres sources.

**Conséquence** : contrairement à P4-5A, **ce lot a pu exécuter le build, la suite de tests complète, le
contrôle de dérive EF et l'audit de vulnérabilités**. Toutes les valeurs de ce rapport sont **mesurées**,
aucune n'est citée de mémoire.

---

## ADR Applied

| ADR | Obligations traitées ici | Obligations **non** traitées (et leur lot) |
|---|---|---|
| [ADR-PROD-DB-003](../architecture/adr-prod-db-003-money-persistence.md) — persistance monétaire | **M1**, **M2**, **M3** | M4, M5, M6 → **P4-5F** · M7 → **P4-7** |
| [ADR-PROD-DB-006](../architecture/adr-prod-db-006-index-and-model-portability.md) — index et portabilité | **X1**, **X2**, **X3**, **X4** | X5, X6 → **P4-5F** |
| [ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md) — stratégie temporelle | **aucune** — reconnaissance seule (voir *DateTime Preparation*) | T1 … T6 → **P4-5D** · T7 → P4-5F · T8 → P4-7 · T9 → P4-8/P4-9 |
| [ADR-PROD-DB-005](../architecture/adr-prod-db-005-migration-architecture.md) — architecture des migrations | **aucune** | G1 … G4 → **P4-5E** |
| [ADR-PROD-DB-007](../architecture/adr-prod-db-007-schema-drift-prevention.md) — dérive de schéma | **aucune** | → P4-5E / P4-5F |
| [ADR-PROD-DB-008](../architecture/adr-prod-db-008-postgresql-integration-testing.md) — tests d'intégration | **aucune** | → **P4-5F** |

---

## Files Changed

**16 fichiers** : **10 modifiés** et **6 ajoutés** — 3 de production, 2 de test, 1 de documentation
(le présent rapport). Deux répertoires sont créés : `Data/Portability/` et `tests/…/Data/Portability/`.

### Ajoutés — production

| Fichier | Rôle |
|---|---|
| [`src/MMV.Infrastructure/Data/Portability/ModelPortability.cs`](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs) | **Point de sélection unique** par provider (X2) : précision monétaire, type physique monétaire, filtre d'index partiel. **Lève** sur provider inconnu. |
| [`src/MMV.Infrastructure/Data/Portability/LegacySqliteMoneyStoreType.cs`](../../src/MMV.Infrastructure/Data/Portability/LegacySqliteMoneyStoreType.cs) | Énumération du **constat du dépôt** : type physique qu'une colonne monétaire porte *aujourd'hui* sur SQLite (`Real` / `ProviderDefault`). |
| [`src/MMV.Infrastructure/Data/Portability/MoneyMappingExtensions.cs`](../../src/MMV.Infrastructure/Data/Portability/MoneyMappingExtensions.cs) | Écriture fluide `HasMoneyMapping(...)`. **Ne décide rien** : applique la précision et délègue le type physique au point de sélection. |

### Ajoutés — tests

| Fichier | Tests |
|---|---|
| [`tests/MMV.Domain.Tests/Data/Portability/ModelPortabilityTests.cs`](../../tests/MMV.Domain.Tests/Data/Portability/ModelPortabilityTests.cs) | **12** — la décision prise par le point de sélection |
| [`tests/MMV.Domain.Tests/Data/Portability/EfModelPortabilityTests.cs`](../../tests/MMV.Domain.Tests/Data/Portability/EfModelPortabilityTests.cs) | **99** — le modèle EF **réellement construit**, pour chacun des deux providers |

### Modifiés

| Fichier | Changement |
|---|---|
| [`Data/OpticDbContext.cs`](../../src/MMV.Infrastructure/Data/OpticDbContext.cs) | **Point de lecture unique** du provider dans `OnModelCreating` ; `ModelPortability` passé aux **7** configurations concernées |
| [`Configurations/ProductConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs) | 3 colonnes monétaires ; justification écrite du `"TEXT"` conservé |
| [`Configurations/SaleConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs) | 5 colonnes monétaires |
| [`Configurations/SaleItemConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/SaleItemConfiguration.cs) | 2 colonnes monétaires |
| [`Configurations/OrderItemConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/OrderItemConfiguration.cs) | 1 colonne monétaire + **4** littéraux optiques retirés |
| [`Configurations/SupplementConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/SupplementConfiguration.cs) | 1 colonne monétaire (défaut provider) |
| [`Configurations/GlassPricingTierConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/GlassPricingTierConfiguration.cs) | 2 colonnes monétaires (défaut provider) ; `PowerMin`/`PowerMax` **inchangées** |
| [`Configurations/WorkshopSheetConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs) | **Filtre d'index sélectionné par provider** (X1) |
| [`Configurations/PrescriptionConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/PrescriptionConfiguration.cs) | **8** littéraux optiques retirés |
| [`Configurations/WorkshopSheetItemConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetItemConfiguration.cs) | **6** littéraux optiques retirés |

### Non touchés — vérifié

- `src/MMV.Infrastructure/Migrations/**` — **les 14 migrations et le snapshot sont bit-à-bit inchangés**
  (`git status` vide sur ce répertoire).
- `.github/workflows/**` — **aucune modification**. Les deux modifications annoncées du workflow
  appartiennent à P4-5E et P4-5F.
- `docs/architecture/adr-*` — **aucun ADR modifié**.
- `MMV.Domain`, `MMV.Application`, `MMV.App` — **aucune ligne modifiée**. Aucun `.csproj` modifié, aucun
  paquet ajouté.

---

## Money Mapping Implementation

### Ce qui est appliqué

**Les 14 colonnes monétaires** déclarent désormais `HasPrecision(12, 2)` **sur les deux providers** (M1), et
leur type physique est décidé au **point de sélection unique** (M2) :

| Provider | Type physique obtenu | Vérification |
|---|---|---|
| **PostgreSQL** | **`numeric(12,2)`** pour les 14 — exact en base 10, deux décimales contraintes par la base | mesuré sur le modèle construit |
| **SQLite** | **`REAL`** pour les 11 historiquement `REAL`, **`TEXT`** pour les 3 sans mapping déclaré — **identique à avant** | mesuré sur le modèle construit |

Le site d'appel déclare le **constat** (`LegacySqliteMoneyStoreType.Real` ou `.ProviderDefault`) ; la
**décision** d'appliquer ou non ce type appartient à `ModelPortability.MoneyStoreType`, et à lui seul.
Aucune configuration d'entité ne teste le provider.

```csharp
builder.Property(s => s.RemainingAmount)
    .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real);
```

### Pourquoi SQLite n'est pas aligné sur PostgreSQL

Aligner SQLite sur `numeric(12,2)` aurait basculé les 11 colonnes de `REAL` vers `TEXT`. ADR-003 §2.3 le
refuse pour une raison qui n'est **pas** esthétique : deux sites SQL font opérer **la base elle-même** sur
une colonne monétaire —
[`SaleRepository.cs:118`](../../src/MMV.Infrastructure/Repositories/SaleRepository.cs#L118)
(`UPDATE … WHERE "RemainingAmount" > 0`, primitive **CAS** du règlement de solde) et
[`SaleRepository.cs:92`](../../src/MMV.Infrastructure/Repositories/SaleRepository.cs#L92)
(`SELECT SUM("FinalAmount")`). En `TEXT`, `"RemainingAmount" > 0` deviendrait une **comparaison
lexicographique**, où `'0.00' > '0'` — soit **l'autorisation silencieuse d'un second règlement d'un solde
déjà réglé**. S'y ajoute le rebuild de table qu'imposerait le changement à toutes les bases SQLite déjà
déployées.

**La dette monétaire SQLite est donc enregistrée et assumée, pas résolue** (ADR-003 §5.6, §7.2).

### Le fait mesuré qui rend M1 et M3 compatibles

M1 (« déclarer `HasPrecision(12,2)` sur les 14 ») et M3 (« la chaîne SQLite reste inchangée, aucune
migration produite ») auraient pu se contredire : la précision **est** écrite dans le snapshot de modèle.
Le risque a été **mesuré avant d'écrire le lot**, par une sonde isolée sur deux colonnes (une `REAL`, une
`TEXT` par défaut), puis rebuild et `has-pending-model-changes` :

> **Résultat : aucune dérive.** Sur SQLite, le type de stockage d'un `decimal` ne dépend pas de la
> précision, et celui des colonnes `REAL` reste imposé explicitement. Le différentiel de migrations
> raisonne sur le **type de stockage**, inchangé des deux côtés.

**Aucune décision d'architecture nouvelle n'a donc été nécessaire** : M1 est appliquée littéralement, aux
14 colonnes, sur les deux providers, et M3 reste satisfaite.

### Hors périmètre, confirmé

`GlassPricingTiers.PowerMin` / `PowerMax` (`decimal`, `HasPrecision(5,2)`) sont des colonnes de
**puissance**, pas de prix : **inchangées** (ADR-003 §6).

---

## Index Provider Strategy

### X1 — le filtre est sélectionné par provider

```csharp
builder.HasIndex(w => w.OrderId)
    .IsUnique()
    .HasFilter(_portability.CurrentWorkshopSheetIndexFilter)
    .HasDatabaseName("idx_workshop_sheets_current_unique");
```

| Provider | Filtre | Raison |
|---|---|---|
| SQLite | `"IsCurrent" = 1` | pas de type booléen : `IsCurrent` y est un `INTEGER` 0/1. **Inchangé au caractère près.** |
| PostgreSQL | `"IsCurrent"` | colonne `boolean` ; PostgreSQL **refuse** la comparaison booléen ↔ entier. `"IsCurrent" = 1` y ferait **échouer la création de l'index, donc du schéma entier**. |
| autre | — | **lève** (`NotSupportedException`) |

La garantie — *au plus une version courante par commande* — reste **arbitrée par la base** des deux côtés.
Elle n'est ni déplacée vers l'applicatif, ni affaiblie : seule son **écriture** change.

`idx_notifications_active_low_stock_unique` est **délibérément intact** (ADR-006 §5.4) : comparaisons de
texte et tests `IS NULL` uniquement, portables et immuables sur les deux moteurs. Un test verrouille cette
identité entre providers.

### X2 — un seul point de lecture du provider

Le nom du provider est lu **une seule fois**, dans `OpticDbContext.OnModelCreating`, puis **passé** aux
7 configurations qui en ont besoin :

```csharp
var portability = ModelPortability.For(Database.ProviderName);
…
modelBuilder.ApplyConfiguration(new SaleConfiguration(portability));
```

Un **test de structure** échoue si une configuration d'entité se remet à lire `ProviderName` pour son
propre compte : la dispersion du couplage est précisément ce que l'ADR interdit.

### Échec explicite plutôt que forme devinée

`ModelPortability.For` **lève** sur provider inconnu — y compris SQL Server, `null` et une casse
différente. Motif, tiré d'ADR-006 §7.2 : **un index partiel mal filtré ne casse aucun test fonctionnel**.
Il ne se voit qu'en concurrence, et trop tard.

---

## HasColumnType Audit

Les **30** littéraux écrits à la main ont été classés **un par un**. Aucun n'a été retiré automatiquement.

| Classe | Nombre | Type CLR | Décision | Effet PostgreSQL | Effet SQLite |
|---|---|---|---|---|---|
| **Monétaires** `"REAL"` | **11** | `decimal` | **Retirés du chemin PostgreSQL**, reconduits sur SQLite par le point de sélection | `numeric(12,2)` | `REAL` — **inchangé** |
| **Optiques** `"REAL"` | **18** | `double` | **Retirés** (X3) | `double precision` (float8) | `REAL` — **inchangé** |
| `Product.TechnicalSpecs` `"TEXT"` | **1** | `string` | **CONSERVÉ** (ADR-006 §5.6) | `TEXT` — type valide | `TEXT` — inchangé |

**Reste après ce lot : 1 littéral**, celui de `TechnicalSpecs`, avec sa justification écrite dans le code.

### Justification du retrait des 18 littéraux optiques

`"REAL"` est transmis **verbatim** au provider actif, et **ne désigne pas le même type des deux côtés** :
8 octets sur SQLite, **4 octets** (`float4`, ~6 chiffres significatifs) sur PostgreSQL. Conserver le
littéral aurait donc **dégradé** ces mesures en production, sans contrepartie. Le mapping par défaut d'un
`double` redonne exactement `REAL` sur SQLite et `double precision` sur PostgreSQL : **aucune perte, aucun
littéral de moteur**. Le type CLR reste `double`, adéquat pour une dioptrie (pas de 0,25).

**Mesuré** : le retrait des 18 littéraux ne produit **aucune** dérive de modèle SQLite (sonde isolée sur
les 6 littéraux de `WorkshopSheetItem`, puis vérification finale sur les 18).

### Justification du maintien du littéral `"TEXT"`

`Product.TechnicalSpecs` stocke du JSON en texte. `text` est un type **valide sur les deux moteurs** : ce mapping
est portable tel quel, et le modifier serait un risque gratuit. `jsonb` est **rejeté pour la V1** — aucun
besoin de requête JSON n'est constaté dans le dépôt.

---

## DateTime Preparation

**Aucune implémentation `DateTime` n'a été faite.** Conformément au mandat du lot, cette section est une
**reconnaissance** et prépare le terrain de **P4-5D**. `IClock` n'est pas créé, aucun `DateTime.Now` n'est
remplacé, aucun convertisseur n'est posé, aucun use case n'est touché.

### Constat central — aucun mapping EF n'est à corriger ici

`STATIC_CODE_PROOF`, mesuré sur le dépôt courant :

- **21 colonnes** `DateTime` / `DateTime?` sont persistées (8 nullables).
- **Aucune** ne porte de `HasColumnType` écrit à la main. Le `"TEXT"` visible dans le snapshot SQLite est
  le **mapping par défaut du provider**, pas une déclaration du code.

**Le code de mapping temporel est donc déjà provider-neutre.** Aucune modification minimale n'a été
nécessaire pour empêcher un mauvais mapping EF : il n'y avait rien à empêcher. Ce qui change entre
providers est le **comportement** (Npgsql exige `Kind = Utc`), pas la déclaration — et le comportement
appartient à P4-5D.

### Mesures transmises à P4-5D

| Mesure | Valeur mesurée sur `9247497` | Écart avec ADR-004 §2.2 |
|---|---|---|
| `DateTime.UtcNow` | **61** | conforme |
| `DateTime.Now` | **17 sites d'appel réels** (18 occurrences textuelles, dont **1 dans un commentaire** : [`DocumentSequenceConfiguration.cs:22`](../../src/MMV.Infrastructure/Data/Configurations/DocumentSequenceConfiguration.cs#L22)) | conforme — l'écart n'est qu'apparent |
| `DateTime.Today` | **0** | conforme |
| `DateTimeOffset` | **8 occurrences sur 7 lignes, dans 2 ViewModels** | ADR-004 annonce « 6 (UI uniquement) ». **Le périmètre UI est confirmé** ; le comptage exact est celui ci-contre. |

Répartition des **17** `DateTime.Now`, par couche : Domain **1** ([`Notification.cs:50`](../../src/MMV.Domain/Entities/Notification.cs#L50)) ·
Application **9** (`RegisterSale` 5, `GenerateLowStockNotifications` 2, `AdvanceOrderStatus` 1,
`SettleOrderBalance` 1) · Infrastructure **3** ([`OrderRepository.cs:167`](../../src/MMV.Infrastructure/Repositories/OrderRepository.cs#L167),
`DbSeeder` 2) · UI **3**.

> **Précision utile pour P4-5D** : ADR-004 §2.2 annonçait « Application (7) » et « RegisterSale (4
> valeurs) ». La mesure donne **9** sites Application, dont **5** dans `RegisterSaleUseCase`. La différence
> tient au comptage (valeurs métier vs sites d'appel), pas à un changement du dépôt — **aucun site n'a été
> ajouté ni retiré par P4-5C**.

### Les 8 `DateTimeOffset` portent exactement les deux dates civiles d'ADR-004 §5.7

Elles sont toutes dans [`CustomerFormViewModel`](../../src/MMV.App/ViewModels/CustomerFormViewModel.cs) et
[`PrescriptionFormViewModel`](../../src/MMV.App/ViewModels/PrescriptionFormViewModel.cs), et soutiennent
`Customer.BirthDate` et `Prescription.IssueDate` — **précisément les deux propriétés qu'ADR-004 fait passer
à `DateOnly`**. Les deux chantiers (T5 et T6) portent donc sur **les mêmes fichiers** et gagneront à être
menés ensemble en P4-5D.

**Rappel du point le plus dangereux de P4-5D**, inchangé : le passage à `DateOnly` ne change **pas** le type
de colonne SQLite (`TEXT`), donc **EF ne générera aucune migration** alors que le **format** des valeurs
change. Une **migration de données écrite à la main** est obligatoire.

---

## Tests Added

**+111 tests** (1569 → **1680**), tous verts. Aucun test existant n'a été modifié ni supprimé.

| Fichier | Nombre | Objet |
|---|---|---|
| `ModelPortabilityTests` | **12** | filtre par provider ; provider inconnu ⇒ **lève** (5 cas : `null`, vide, SQL Server, InMemory, casse différente) ; précision (12,2) ; reconduction des types SQLite ; **le littéral `REAL` n'atteint jamais PostgreSQL** |
| `EfModelPortabilityTests` | **99** | le **modèle EF réellement construit**, pour les **deux** providers |

Détail du second fichier : précision (12,2) sur les 14 colonnes × 2 providers (**28**) · `numeric(12,2)` sur
PostgreSQL (**14**) · `REAL` conservé sur SQLite (**11**) · `TEXT` conservé sur SQLite (**3**) · les 18
colonnes optiques en `REAL` sur SQLite et `double precision` sur PostgreSQL (**36**) · filtre d'index par
provider (**2**) · filtre `Notifications` identique entre providers (**1**) · `TechnicalSpecs` en `TEXT` des
deux côtés (**1**) · **aucun** littéral `REAL` dans le modèle PostgreSQL (**1**) · **coexistence des deux
modèles dans le même processus** (**1**) · point de lecture unique du provider (**1**).

### Deux propriétés notables de ces tests

**Aucun serveur n'est requis.** La construction d'un modèle EF n'ouvre aucune connexion : le modèle
PostgreSQL est donc entièrement vérifiable sur un poste de développement **et en CI**, sans instance
PostgreSQL. C'est ce qui rend ce lot prouvable dès maintenant.

**Le risque de cache de modèle est verrouillé par mesure.** EF met le modèle en cache. Si ce cache était
partagé entre providers, le **premier** contexte construit dans le processus imposerait son filtre d'index
et son type monétaire au second — un schéma PostgreSQL pourrait naître avec la forme SQLite, **sans qu'aucun
test fonctionnel ne casse**. Le test `BothProviderModels_CoexistInTheSameProcess_WithoutContaminatingEachOther`
construit les deux modèles dans le **même processus**, dans cet ordre, et vérifie que chacun garde sa forme.
**Il passe** : EF isole bien les deux modèles. Ce n'est donc plus une hypothèse, c'est une mesure — et elle
reste vérifiée à chaque exécution de la suite.

### Ce que ces tests ne prouvent pas — à ne jamais présenter autrement

Ils prouvent ce que le modèle **déclare**, jamais ce que la base **fait**. Restent dues, en **P4-5F** :

- **M4** — `numeric(12,2)` vérifié **physiquement** dans `information_schema.columns` ;
- **M5** — fidélité monétaire aller-retour contre PostgreSQL (bornes, deux décimales, arrondi bancaire,
  égalité **exacte**) ;
- **M6** — sémantique de `"RemainingAmount" > 0` et de `SUM("FinalAmount")` **sur les deux providers** ;
- **X5** — les deux index filtrés vérifiés dans `pg_index` ;
- **X6** — comportement de recherche `lower()` verrouillé par provider.

**Un test monétaire vert sur SQLite ne vaut pas preuve d'exactitude monétaire** (ADR-003 §5.7).

---

## Risks

| # | Risque | Statut |
|---|---|---|
| **R-1** | Rompre la chaîne SQLite (migration parasite, rebuild de table) | **Écarté, mesuré** : `has-pending-model-changes` vert, `git status` vide sur `Migrations/`, 14 migrations et snapshot bit-à-bit inchangés |
| **R-2** | Régression fonctionnelle sur SQLite | **Écarté, mesuré** : les 1569 tests préexistants passent **sans modification** |
| **R-3** | Cache de modèle EF partagé entre providers | **Écarté, mesuré** et **verrouillé par test** (voir *Tests Added*) |
| **R-4** | Filtre d'index faux en production, silencieux | **Réduit, non clos** : la forme est testée sur le modèle ; sa **présence physique** dans `pg_index` reste due (**X5**, P4-5F). Un index partiel absent ou mal filtré ne casse aucun test fonctionnel — il ne se voit qu'en concurrence |
| **R-5** | Dette monétaire SQLite subsistante (11 flottants, 3 textes) | **Acceptée explicitement** par ADR-003 §5.6 ; **non** critère de sortie P4. Un magasin resté mono-poste conserve le défaut |
| **R-6** | Montants déjà altérés dans les bases `REAL` existantes | **Hors périmètre** : la conversion et la réconciliation à l'import appartiennent à **P4-7** (M7). Ce lot ne touche aucune donnée |
| **R-7** | Un troisième provider ferait lever le point de sélection | **Voulu** — échec explicite plutôt que forme devinée (ADR-006 §9) |
| **R-8** | L'exactitude monétaire n'est **pas** prouvée par ce lot | **Assumé et déclaré** : elle ne peut l'être que contre PostgreSQL (P4-5F) |

---

## Remaining Work

### Immédiatement après ce lot

**P4-5D — stratégie temporelle** (T1 … T6), dont la reconnaissance ci-dessus fournit les mesures.
**Ce lot n'ouvre pas P4-5D** : validation de l'architecte attendue.

### Le reste de P4-5

| Lot | Contenu | État |
|---|---|---|
| **P4-5D** | `IClock`, UTC partout, convertisseur validant, test d'architecture, `DateOnly` + migration de données écrite à la main | **NOT STARTED** |
| **P4-5E** | Chaîne de migrations PostgreSQL + factory design-time sélective + double contrôle de dérive en CI | **NOT STARTED** |
| **P4-5F** | Tests d'intégration PostgreSQL (corpus N1 … N8) — **porte les preuves M4, M5, M6, X5, X6** | **NOT STARTED** |
| **P4-5G** | Levée du garde-fou de démarrage ([`App.axaml.cs:198`](../../src/MMV.App/App.axaml.cs#L198)) + chaîne de préparation serveur | **NOT STARTED** |

### Ce que P4-5C ne débloque pas

**`POSTGRESQL CLEAN START` reste `BLOCKED`.** Des trois causes cumulatives recensées par P4-5B, **une
seule** est levée ici :

| Cause | État après P4-5C |
|---|---|
| Modèle EF non portable | **LEVÉE** pour le volet monétaire et le volet index |
| Absence de chaîne de migrations serveur | **INCHANGÉE** — P4-5E |
| Garde-fou de démarrage (volontaire, P4-3) | **INCHANGÉ** — P4-5G |

S'y ajoute le volet `DateTime` (P4-5D), qui reste **bloquant pour l'écriture** vers PostgreSQL par le chemin
EF.

**La V1 multi-poste n'est pas `GO`.**

---

## Proposition soumise à l'architecte — non décidée par ce lot

Une seule question a émergé pendant l'implémentation et **n'est tranchée par aucun des six ADR**. Elle est
posée ici, **sans être choisie**.

### P-1 — Faut-il une garde explicite sur le cache de modèle EF ?

**Fait mesuré** : EF Core 8 isole déjà les modèles des deux providers dans un même processus — chaque
provider obtient son propre fournisseur de services interne, donc son propre cache de modèle. Le test
ajouté par ce lot le vérifie et le verrouille.

**Question ouverte** : ce comportement est un **détail d'implémentation d'EF**, pas un contrat public. Une
garde explicite existe — un `IModelCacheKeyFactory` incluant le nom du provider dans la clé — qui rendrait
l'isolation **indépendante** de ce détail.

| Option | Pour | Contre |
|---|---|---|
| **A — statu quo** *(état livré)* | aucun code superflu ; comportement vérifié par test à chaque exécution | repose sur un détail d'implémentation d'EF ; une régression du framework ne serait vue qu'au test |
| **B — `IModelCacheKeyFactory` explicite** | isolation garantie par contrat MMV | code supplémentaire pour un risque aujourd'hui non observé ; sort du périmètre des ADR |

**Ce lot livre A**, l'option **sans code nouveau**, parce que le risque est mesuré comme non réalisé et que
choisir B serait une décision d'architecture que P4-5C n'a pas mandat de prendre. **Si l'architecte retient
B, elle appartient naturellement à P4-5E**, avec la chaîne de migrations.

---

## Conclusion

**P4-5C est implémentée et mesurée.** Le modèle EF de MMV sert désormais **les deux providers** :

- **14 colonnes monétaires** en `numeric(12,2)` sur PostgreSQL, **type SQLite reconduit à l'identique** —
  la primitive CAS du règlement de solde conserve exactement sa sémantique sur les deux moteurs ;
- **l'index unique partiel** des fiches atelier est enfin **créable sur PostgreSQL**, sa garantie restant
  arbitrée par la base des deux côtés ;
- **29 littéraux de type sur 30 sont retirés** ; le seul conservé porte sa justification écrite ;
- **la sélection par provider est confinée à un point unique**, et **lève** sur provider inconnu.

**La chaîne SQLite est intacte** : 14 migrations et snapshot inchangés, contrôle de dérive vert, **1569
tests préexistants passants sans modification**, aucune migration produite.

**Obligations closes** : **M1**, **M2**, **M3**, **X1**, **X2**, **X3**, **X4**.

**Ce que ce lot ne prouve pas, et ne doit pas être présenté comme prouvé** : l'exactitude monétaire réelle,
la présence physique des index, la compatibilité applicative complète de MMV sur PostgreSQL. Ces preuves
appartiennent à **P4-5F**, et aucune d'elles ne peut être obtenue sans serveur.

**Aucune migration n'a été créée. Aucun ADR n'a été modifié. La CI n'a pas été touchée. P4-5D n'est pas
ouverte.**

---

## Vérification finale — valeurs mesurées

| Contrôle | Commande | Résultat |
|---|---|---|
| Build | `dotnet build MMV.sln -c Debug` | **0 erreur, 0 avertissement** |
| Tests | `dotnet test MMV.sln` | **1680** — Domain **821** · Application **620** · App **239** — 0 échec, 0 ignoré |
| Dérive de modèle EF | `dotnet ef migrations has-pending-model-changes` | *« No changes have been made to the model since the last migration »* |
| Migrations | `git status --short src/MMV.Infrastructure/Migrations/` | **vide** |
| Vulnérabilités | `dotnet list package --vulnerable --include-transitive` | **aucune**, sur les 7 projets |
| Littéraux de type restants | `grep HasColumnType` hors migrations | **1**, justifié |
