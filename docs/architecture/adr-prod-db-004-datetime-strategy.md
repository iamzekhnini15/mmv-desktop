# ADR-PROD-DB-004 — Stratégie temporelle : instants UTC, dates civiles, horloge injectable

> **Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation).**
> Cet ADR tranche **l'obligation O4** d'[ADR-PROD-DB-002 §15](adr-prod-db-002-server-database-provider-selection.md)
> et **clôt l'orientation différée** d'[ADR-005 — Temps & fuseaux](adr-candidates.md#adr-005). Il **ne modifie
> aucun fichier `.cs`, aucun test, aucune migration, aucune configuration et aucune CI.** L'implémentation
> appartient à **P4-5D**.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de décision :
> `b49f2ff7c4e3af665a767f508268377be9d19943`.
> Entrées : [audit P4-5A §DateTime Analysis](../implementation/P4-5A-postgresql-schema-audit-report.md) ·
> [ADR-PROD-DB-002 §16.1](adr-prod-db-002-server-database-provider-selection.md) ·
> [ADR-005](adr-candidates.md#adr-005) · [P2A-1R19](../implementation/P2A-1R19-report.md).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.** Cet ADR **ferme** ADR-005 (« décision différée », orientation 1) pour le périmètre MMV V1 :
l'orientation y devient une décision, et les points qu'ADR-005 laissait ouverts — dates civiles, mécanisme
de défense, reprise des données existantes — sont tranchés ici.

---

## 2. Contexte

### 2.1 Le blocage technique, mesuré

`EXECUTED_SPIKE` + `OFFICIAL_SEMANTICS`. Npgsql ≥ 6 mappe `DateTime` → `timestamp with time zone` et **exige
`Kind = Utc`** : *« trying to send a non-UTC DateTime as timestamptz will throw an exception »*
(ADR-PROD-DB-002 §16.1).

| Chemin | Comportement PostgreSQL avec `Kind = Local` |
|---|---|
| **EF** (`SaveChanges`) | **refus** — `DbUpdateException` |
| **SQL brut** | l'heure locale est **stockée comme si elle était UTC** — **2 h d'écart silencieuses** en heure d'été française |

**En l'état, MMV ne peut écrire aucune date vers PostgreSQL par le chemin EF.** Ce n'est pas une
préférence de style : c'est un blocage de démarrage.

### 2.2 L'état mesuré du dépôt (`STATIC_CODE_PROOF`, hors migrations)

| Source | Occurrences | `Kind` produit |
|---|---|---|
| `DateTime.UtcNow` | **61** | `Utc` — compatible |
| `DateTime.Now` | **17** | **`Local`** — refusé par EF sur PostgreSQL |
| `DateTime.Today` | 0 | — |
| `DateTimeOffset` | 6 (UI uniquement) | conversion à auditer |

**21 colonnes** `DateTime`/`DateTime?` sont persistées. **Aucune** ne porte de `HasColumnType` écrit à la
main : le code de mapping est **déjà neutre**, seul le comportement du provider change.

Les 17 sites `DateTime.Now`, nommément :

- **Défaut d'entité** — [Notification.cs:50](../../src/MMV.Domain/Entities/Notification.cs#L50) :
  `CreatedAt = DateTime.Now`. **Le plus exposé** : toute notification créée sans horodatage explicite naît
  en `Local`. Les neuf autres entités datées utilisent `UtcNow`.
- **Application (7)** : `GenerateLowStockNotificationsUseCase` (2), `AdvanceOrderStatusUseCase` (1),
  `SettleOrderBalanceUseCase` (1), `RegisterSaleUseCase` (4 valeurs : `SaleDate`, `EstimatedDelivery`,
  `OrderDate`, `CreatedAt`).
- **Infrastructure (3)** : [OrderRepository.cs:167](../../src/MMV.Infrastructure/Repositories/OrderRepository.cs#L167)
  — **comparaison `EstimatedDelivery < DateTime.Now` traduite en SQL**, donc un `Local` comparé à une colonne
  `timestamptz` — et `DbSeeder` (2).
- **UI (3)** : `OrderDetailViewModel`, `OrderFormViewModel`, `CustomersView.axaml.cs`.

### 2.3 Le risque propre au multi-poste

C'est le risque **structurellement nouveau** de P4. Avec `DateTime.Now`, l'horodatage dépend du **fuseau et
de l'horloge de chaque poste**. Deux postes réglés différemment produisent un **ordre chronologique faux
dans une base commune** : ordre des mouvements de stock, chronologie des alertes `LowStock`, séquence des
transitions de commande, traçabilité d'ordonnance — **donnée de santé horodatée**.

**Le stockage UTC ne corrige pas une horloge fausse**, mais il supprime la **moitié « fuseau »** du problème
et rend la dérive résiduelle mesurable.

### 2.4 Les dates civiles ne sont pas des instants

`Customer.BirthDate` et `Prescription.IssueDate` sont des **dates civiles**, mappées `DateTime`. Les traiter
en instants UTC peut **décaler le jour** : une naissance saisie à 00:00 locale devient la veille à 22:00 UTC.
Sur une date de naissance et une date d'ordonnance, ce décalage est **fonctionnellement faux**, pas seulement
inélégant.

### 2.5 Ce qui est déjà acquis

- [**P2A-1R19**](../implementation/P2A-1R19-report.md) a **supprimé les défauts SQL figés**
  `HasDefaultValue(DateTime.UtcNow)` : l'horodatage est **géré par l'application**, avec la mention explicite
  d'un « futur `IClock` ». Le terrain est donc **déjà préparé**.
- [ADR-005](adr-candidates.md#adr-005) avait **déjà rejeté** le statu quo `DateTime.Now` et orienté vers
  **horloge injectable + stockage UTC**. **La remédiation exigée par PostgreSQL est un travail déjà prévu,
  pas une dette nouvelle** (ADR-PROD-DB-002 §16.1).
- **Aucune abstraction d'horloge n'existe à ce jour** : `IClock` n'apparaît que dans des commentaires
  d'intention ([DocumentSequence.cs:39](../../src/MMV.Domain/Entities/DocumentSequence.cs#L39),
  [SaleConfiguration.cs:25](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L25)).

---

## 3. Question de décision

> **Sous quelle forme MMV stocke-t-il le temps dans une base centrale partagée par plusieurs postes, comment
> l'affiche-t-il, comment l'empêche-t-on de régresser, et que fait-on des dates civiles ?**

---

## 4. Options comparées

### 4.1 Stockage des instants

| Option | Description | Verdict |
|---|---|---|
| **S1 — UTC partout, colonne `timestamptz`** | mapping Npgsql par défaut ; toute valeur persistée porte `Kind = Utc` | **RETENUE** |
| **S2 — `timestamp without time zone`** | PostgreSQL accepterait `Local`/`Unspecified` sans lever | **REJETÉE** — traite le symptôme, **conserve intacte la faille multi-poste** de §2.3 : un instant sans fuseau reste ininterprétable entre deux postes |
| **S3 — `DateTimeOffset` partout** | UTC + décalage d'origine conservé | **REJETÉE pour V1** — refonte de 21 colonnes, de tout le Domain et de l'UI ; le décalage d'origine n'a **aucun usage métier constaté** ; ADR-005 la jugeait déjà insuffisante pour la « date métier locale » |

### 4.2 Mécanisme de défense

| Option | Description | Verdict |
|---|---|---|
| **D1 — convertisseur de valeur silencieux** (`ToUniversalTime()` à l'écriture) | corrige tout seul | **REJETÉE** — **masque** l'erreur : un `DateTime.Now` oublié serait converti au lieu d'être révélé, et la conversion d'un `Unspecified` est une **supposition** |
| **D2 — aucun convertisseur, discipline + test d'architecture** | interdit `DateTime.Now` dans `src/**` | **partiellement retenue** — nécessaire mais **insuffisante** : elle ne dit rien du `Kind` **relu**, qui diverge entre providers |
| **D3 — convertisseur *validant*** : à l'écriture, **lève** si `Kind != Utc` ; à la lecture, `SpecifyKind(Utc)` | échoue bruyamment côté écriture, normalise côté lecture | **RETENUE**, **avec** D2 |

**Pourquoi D3 plutôt que D1.** Un convertisseur validant ne « corrige » rien : il **refuse**. Il transforme
un bogue de production silencieux en échec immédiat et localisé, y compris **sur SQLite** — donc **détectable
par la suite de tests existante**, sans serveur. La branche lecture supprime en outre la divergence de `Kind`
entre providers (SQLite rend `Unspecified`, PostgreSQL rend `Utc`), qui ferait autrement changer le
comportement de tout code comparant ou formatant une date **sans que rien n'échoue**.

### 4.3 Dates civiles (`BirthDate`, `IssueDate`)

| Option | Description | Verdict |
|---|---|---|
| **C1 — `DateOnly` en CLR** ⇒ `date` PostgreSQL, `TEXT` `yyyy-MM-dd` SQLite | le type exprime enfin ce que la donnée **est** | **RETENUE** |
| **C2 — `DateTime` à minuit UTC par convention** | aucun changement de type | **REJETÉE** — la convention n'est **portée par rien** ; le premier `ToLocalTime()` d'affichage **décale le jour**, sur une date de naissance et une date d'ordonnance |
| **C3 — colonne `date` sans changer le CLR** | mapping provider | **REJETÉE** — divergence modèle ↔ CLR, non exprimable proprement sur SQLite |

---

## 5. Décision

1. **Invariant unique — tout instant persisté est UTC.** Les 21 colonnes d'instants sont des
   `timestamp with time zone` sur PostgreSQL (mapping Npgsql par défaut, **aucun littéral de type**).
2. **`IClock` injectable**, interface en **Domain** (`MMV.Domain.Interfaces`, neutre), implémentation
   système en **Infrastructure**. `DateTime.UtcNow` n'est plus appelé que **dans l'implémentation**.
   Cela **ferme** l'intention déjà inscrite en commentaire depuis P2A-1R19.
3. **Les 17 `DateTime.Now` disparaissent de `src/**`.** Priorité : (a) le défaut d'entité
   [Notification.cs:50](../../src/MMV.Domain/Entities/Notification.cs#L50) ; (b) la comparaison traduite en
   SQL [OrderRepository.cs:167](../../src/MMV.Infrastructure/Repositories/OrderRepository.cs#L167) ;
   (c) les usages Application ; (d) les usages UI.
4. **Convertisseur de valeur *validant*** appliqué à **toutes** les propriétés d'instants du modèle :
   écriture ⇒ **lève** si `Kind != Utc` ; lecture ⇒ `SpecifyKind(Utc)`. Il s'applique **aux deux providers**,
   de sorte que la suite SQLite détecte la faute **sans serveur**.
5. **Test d'architecture bloquant** : `DateTime.Now`, `DateTime.Today` et `DateTimeOffset.Now` sont
   **interdits** dans `src/**`, à l'exception d'une **liste blanche explicite et justifiée** limitée à la
   **frontière d'affichage** UI. L'échec survient **au test**, pas en production.
6. **Affichage** : conversion UTC → heure locale **au moment de l'affichage seulement**. Aucune valeur
   convertie en local ne repart vers la persistance. Les six conversions `DateTimeOffset` de l'UI —
   dont [CustomerFormViewModel.cs:250](../../src/MMV.App/ViewModels/CustomerFormViewModel.cs#L250) et
   [PrescriptionFormViewModel.cs:414](../../src/MMV.App/ViewModels/PrescriptionFormViewModel.cs#L414) —
   sont **auditées une à une** en P4-5D.
7. **Dates civiles** : `Customer.BirthDate` et `Prescription.IssueDate` passent à **`DateOnly?` / `DateOnly`**
   ⇒ `date` sur PostgreSQL, `TEXT` `yyyy-MM-dd` sur SQLite. **Elles ne sont ni converties en UTC, ni
   soumises au convertisseur d'instants.**
8. **Reprise des données existantes** : le `Kind` des instants déjà stockés en SQLite est **inconnu et
   hétérogène**. La règle de reprise est fixée ici et **exécutée en P4-7** : toute valeur importée est
   **interprétée comme heure locale du magasin d'origine** puis convertie en UTC, **la règle appliquée est
   consignée dans le rapport d'import**, et **aucune interprétation n'est faite en silence**. Le passage des
   dates civiles au format `yyyy-MM-dd` fait partie de la même reprise.
9. **L'horloge des postes est une exigence d'exploitation.** Le stockage UTC **ne corrige pas** une horloge
   fausse. La **synchronisation horaire (NTP) de tous les postes** devient une exigence documentée de
   **P4-8**, et sa dérive un point de supervision de **P4-9**.

### 5.1 Horodatage côté serveur — envisagé, non retenu pour V1

Faire produire les horodatages critiques par la base (`now()` côté serveur) supprimerait **entièrement** la
dérive d'horloge entre postes. C'est **rejeté pour V1** : cela réintroduirait des valeurs générées par la
base (que P2A-1R19 a précisément retirées), imposerait une relecture après écriture sur chaque insertion, et
ne fonctionnerait pas de la même manière sur SQLite. **C'est le remède désigné si la dérive mesurée en P4-10
ou en exploitation se révèle nuisible** — pas une option ouverte aujourd'hui.

---

## 6. Ce que cet ADR ne dit pas

- Il ne choisit **pas** de fuseau de magasin configurable ni de règle de « date métier locale » (clôture de
  caisse, exercice) : ADR-005 les évoquait, **aucun besoin n'est constaté** dans le périmètre V1 multi-poste.
  L'affichage utilise le fuseau **du poste**.
- Il ne convertit **pas** les colonnes d'instants en `DateTimeOffset`.
- Il n'écrit **aucune** migration et **ne réalise pas** l'import P4-7.

---

## 7. Conséquences

### 7.1 Positives

- **Le blocage d'écriture PostgreSQL est levé** : c'est une condition de démarrage de P4-5G.
- **La chronologie d'une base partagée redevient interprétable** : plus de dépendance au fuseau du poste.
- **La divergence de `Kind` à la relecture disparaît** : les deux providers rendent `Utc`.
- **Une faute future échoue au test**, sur SQLite, sans serveur — par le convertisseur validant **et** par le
  test d'architecture.
- **Les dates civiles cessent de pouvoir changer de jour** — sur une date de naissance et une date
  d'ordonnance, c'est une correction **métier**, pas cosmétique.
- **L'horloge injectable rend le temps testable** : les scénarios datés cessent de dépendre de l'heure réelle.

### 7.2 Négatives — assumées

- **Périmètre réel non trivial** : 17 sites de génération, 21 colonnes, six conversions UI, une comparaison
  traduite en SQL, une interface et son implémentation, un test d'architecture. **P4-5D est un lot à part
  entière.**
- **Le passage à `DateOnly` exige une reprise de données explicite et écrite à la main.** Le type de colonne
  SQLite reste `TEXT` : **EF ne détectera aucun changement de schéma** et ne générera donc **aucune**
  migration, alors que le **format** des valeurs change (`yyyy-MM-dd HH:mm:ss` → `yyyy-MM-dd`). Une migration
  **de données**, écrite à la main et prouvée par test, est **obligatoire** ; l'oublier corromprait la
  lecture des dates de naissance et d'ordonnance sur les bases existantes. **C'est le point le plus
  dangereux de cet ADR.**
- **Le convertisseur validant peut faire échouer des écritures qui « passaient »** — c'est l'effet recherché,
  mais il peut révéler des sites non recensés. À traiter **en P4-5D**, pas en production.
- **La dérive d'horloge entre postes n'est pas résolue** — seule la moitié « fuseau » l'est. Risque résiduel
  explicitement reporté à P4-8 / P4-9 (décision 9) et à §5.1.

### 7.3 Neutres

- Aucune colonne d'instant ne change de type physique sur SQLite ; le convertisseur ne modifie pas le schéma.
- `Money` et le mapping monétaire ne sont pas concernés ([ADR-PROD-DB-003](adr-prod-db-003-money-persistence.md)).

---

## 8. Obligations d'implémentation créées

| # | Obligation | Lot | Bloquante |
|---|---|---|---|
| **T1** | Introduire `IClock` (Domain, neutre) + implémentation système (Infrastructure) + injection | P4-5D | **OUI** |
| **T2** | Éliminer les **17** `DateTime.Now` de `src/**`, en commençant par `Notification.CreatedAt` et `OrderRepository:167` | P4-5D | **OUI** |
| **T3** | Appliquer le **convertisseur validant** à toutes les propriétés d'instants du modèle | P4-5D | **OUI** |
| **T4** | Ajouter le **test d'architecture** interdisant `DateTime.Now` / `DateTime.Today` / `DateTimeOffset.Now` dans `src/**`, liste blanche UI explicite | P4-5D | **OUI** |
| **T5** | Convertir `BirthDate` et `IssueDate` en `DateOnly` + **migration de données SQLite écrite à la main**, prouvée par test de non-destruction | P4-5D | **OUI** |
| **T6** | Auditer les **six** conversions `DateTimeOffset` de l'UI ; formater en local **à l'affichage seulement** | P4-5D | **OUI** |
| **T7** | Prouver la fidélité `DateTime` **contre PostgreSQL, avec assertion** : écriture UTC, relecture, `Kind`, ordre chronologique — le constat P4-1 était **observationnel** | P4-5F | **OUI** |
| **T8** | Transmettre à P4-7 la règle de reprise du `Kind` (décision 8) et le changement de format des dates civiles | P4-7 | **OUI** |
| **T9** | Inscrire l'exigence **NTP** dans la documentation d'exploitation ; superviser la dérive | P4-8 / P4-9 | **OUI** |

---

## 9. Conditions de réexamen

- Si un besoin de **fuseau par magasin** apparaît (multi-site, exercice fiscal local) ⇒ rouvrir §6.
- Si la **dérive d'horloge** mesurée en P4-10 ou en exploitation fausse une chronologie métier ⇒ appliquer
  §5.1 (horodatage côté serveur) sur les colonnes concernées.
- Si le convertisseur validant se révèle intenable sur un chemin légitime (import de masse, données
  héritées) ⇒ **n'en exempter qu'un chemin nommé et documenté**, jamais le modèle entier.

---

## 10. Références

- [ADR-005 — Temps & fuseaux](adr-candidates.md#adr-005) *(orientation différée, close par le présent ADR)*
- [ADR-PROD-DB-002 §16.1](adr-prod-db-002-server-database-provider-selection.md)
- [Audit P4-5A §DateTime Analysis, AD-2](../implementation/P4-5A-postgresql-schema-audit-report.md)
- [P2A-1R19 — suppression des défauts SQL de date](../implementation/P2A-1R19-report.md)
- [ADR-PROD-DB-008 — tests d'intégration PostgreSQL](adr-prod-db-008-postgresql-integration-testing.md)
- [Rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md)
