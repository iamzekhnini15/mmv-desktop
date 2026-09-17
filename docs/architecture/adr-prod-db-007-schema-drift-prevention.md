# ADR-PROD-DB-007 — Prévention de la dérive de schéma (deux chaînes, une seule vérité)

> **Statut : ACCEPTÉ (lot P4-5B — décision d'architecture, AUCUNE implémentation).**
> Cet ADR fixe **comment MMV empêche que le modèle EF et la base PostgreSQL de production cessent d'être
> synchronisés**, une fois les deux chaînes de migrations en place
> ([ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md)). Il **ne modifie aucun fichier `.cs`, aucun
> test, aucune migration, aucune configuration et aucune CI.** L'implémentation appartient à **P4-5E**
> (contrôles) et **P4-5F** (vérification physique).
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`. SHA de décision :
> `b49f2ff7c4e3af665a767f508268377be9d19943`.
> Entrées : [audit P4-5A §20, AD-5, R-5](../implementation/P4-5A-postgresql-schema-audit-report.md) ·
> [ci.yml](../../.github/workflows/ci.yml) · [P2A-1R19](../implementation/P2A-1R19-report.md).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.**

---

## 2. Contexte

### 2.1 La garde actuelle, et son angle mort

La CI exécute aujourd'hui
[`dotnet ef migrations has-pending-model-changes`](../../.github/workflows/ci.yml) **une seule fois**, **sans
configuration serveur** — donc **contre SQLite**, puisque
[`OpticDbContextFactory`](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs) est volontairement figée
sur SQLite. Cette garde est **efficace** : elle a été mise en place pour un incident réel de dérive
(défauts SQL figés, [P2A-1R19](../implementation/P2A-1R19-report.md)) et elle protège la chaîne SQLite depuis.

**Avec deux chaînes, elle ne couvrira plus que la moitié du problème.** Un développeur pourrait modifier le
modèle, générer la migration SQLite, oublier la migration PostgreSQL — et la CI resterait **verte**. La
dérive du schéma de **production** deviendrait **invisible**. C'est le risque R-5 de l'audit P4-5A.

### 2.2 Trois formes de dérive, distinctes

| Forme | Description | Ce qui la détecte |
|---|---|---|
| **D-A — dérive modèle ↔ migrations** | le modèle EF a changé, une chaîne n'a pas suivi | `has-pending-model-changes`, **par chaîne** |
| **D-B — dérive migrations ↔ schéma réel** | la migration s'applique, mais l'objet créé n'est pas celui attendu (index partiel au mauvais prédicat, type de colonne inattendu) | **lecture des catalogues** de la base, après migration |
| **D-C — dérive base de production ↔ migrations** | quelqu'un modifie la base centrale à la main | garde de version au démarrage (**P4-6 / O8**) + procédure d'exploitation |

**Aucun** de ces trois contrôles ne remplace les deux autres. La CI actuelle ne couvre que D-A, et pour une
seule chaîne.

### 2.3 Les tests actuels ne prouvent rien sur les migrations

**114 sites** `EnsureCreated()` dans la suite : ils construisent le schéma **directement depuis le modèle**,
**sans passer par les migrations**. C'est légitime et rapide pour des tests unitaires — mais cela signifie
que **la quasi-totalité de la suite reste verte même si les migrations sont fausses**. Seules quelques suites
d'acceptation appliquent `Migrate()` réellement
([AcceptanceScenarioBase.cs:92](../../tests/MMV.Application.Tests/Acceptance/AcceptanceScenarioBase.cs#L92)).

**Conséquence directe** : sur PostgreSQL, un test d'intégration qui utiliserait `EnsureCreated()` validerait
**un schéma que personne ne déploie**. Ce serait un faux vert de la pire espèce.

---

## 3. Question de décision

> **Quels contrôles garantissent qu'à tout instant le modèle EF, les deux chaînes de migrations et le schéma
> PostgreSQL réellement créé disent la même chose — et que leur divergence échoue bruyamment ?**

---

## 4. Options comparées

| Option | Description | Verdict |
|---|---|---|
| **V1 — deux exécutions du contrôle de dérive, une par chaîne** | factory design-time sélective ; **aucun serveur requis** — la génération et la comparaison de migrations ne se connectent pas | **RETENUE** |
| **V2 — statu quo, une seule exécution SQLite** | ne change rien à la CI | **REJETÉE** — la dérive PostgreSQL devient **indétectable** ; revient à ne pas avoir de garde sur la base de production |
| **V3 — contrôle PostgreSQL déplacé dans un job d'intégration avec serveur** | fusionné avec les tests serveur | **REJETÉE** — lie inutilement un contrôle **statique** à la disponibilité d'un serveur ; en cas d'indisponibilité, la garde disparaît au moment où on en a le plus besoin |
| **V4 — comparaison de schéma générée (script SQL diffé)** | produire le DDL et le comparer à une référence | **REJETÉE pour V1** — redondant avec V1 + vérification physique, et coûteux à maintenir |

---

## 5. Décision

1. **Le contrôle de dérive est exécuté une fois par chaîne**, et **les deux sont bloquants** en CI :
   - chaîne **SQLite** — comportement actuel, **inchangé** ;
   - chaîne **PostgreSQL** — même commande, provider sélectionné par variable d'environnement.
   **Aucun serveur n'est requis** : ni la génération, ni la comparaison de modèle ne se connectent à une base.
2. **Un seul chemin de génération de migrations** : la factory design-time sélective d'
   [ADR-PROD-DB-005 §5.5](adr-prod-db-005-migration-architecture.md), avec **SQLite par défaut**. Toute
   migration générée autrement est **non conforme**.
3. **Vérification physique du schéma PostgreSQL** (forme D-B), sur une base **créée par migrations** :
   colonnes et types (dont `numeric(12,2)` — [ADR-PROD-DB-003](adr-prod-db-003-money-persistence.md)), index
   uniques, **les deux index filtrés avec leur prédicat** ([ADR-PROD-DB-006](adr-prod-db-006-index-and-model-portability.md)),
   clés étrangères et comportements de suppression. Lecture des **catalogues** (`information_schema`,
   `pg_index`), **jamais** déduction depuis le modèle.
4. **Reproductibilité prouvée** : une base neuve migrée **deux fois** donne un schéma **identique** —
   critère de sortie P4 n° 4.
5. **`EnsureCreated()` est interdit** dans tout chemin de production **et** dans tout test d'intégration
   PostgreSQL : les bases d'intégration sont **créées par migrations**, sans exception. Les tests unitaires
   SQLite existants conservent `EnsureCreated()` — **mais il est acté qu'ils ne prouvent rien sur les
   migrations**, et ils ne doivent jamais être cités comme tels.
6. **Règle d'entretien rendue vérifiable** : tout changement du modèle produit, **dans le même commit**, la
   migration correspondante **sur chacune des deux chaînes**, ou la **preuve** que l'autre chaîne est
   inchangée (cas prévu et légitime : une construction **volontairement** dépendante du provider —
   ADR-PROD-DB-003 §5.4, ADR-PROD-DB-006 §5.1). Le double contrôle de §5.1 **fait échouer la CI** dans le cas
   contraire.
7. **La dérive de la base de production (D-C) est traitée au démarrage** : un client dont les migrations ne
   sont pas toutes appliquées, ou dont le schéma est plus récent que l'application, **doit être bloqué
   proprement**. Cette garde appartient à **P4-6 (O8)** et n'est **pas** livrée par P4-5.
8. **Le schéma de production ne se modifie jamais à la main.** Toute correction passe par une migration.
   Règle à inscrire dans la documentation d'exploitation (**P4-8/P4-9**).

---

## 6. Ce que cet ADR ne dit pas

- Il ne décide **pas** de la forme exacte du workflow CI (jobs, matrice, ordre) : détail d'implémentation de
  P4-5E, contraint seulement par « **les deux contrôles sont bloquants** ».
- Il ne fournit **pas** la garde de version applicative (**P4-6 / O8**).
- Il ne réécrit **pas** les 114 `EnsureCreated()` existants : hors périmètre, et sans valeur ajoutée pour la
  base serveur.

---

## 7. Conséquences

### 7.1 Positives

- **La garantie acquise sur SQLite est restaurée sur PostgreSQL** : la chaîne de production redevient
  surveillée automatiquement.
- **La modification de CI est minimale** : une seconde exécution d'une commande déjà présente, **sans**
  serveur, **sans** dépendance nouvelle.
- **Les trois formes de dérive sont nommées et affectées** — aucune ne reste implicite.
- **Le faux vert le plus probable — un test d'intégration sur `EnsureCreated()` — est interdit par écrit**,
  avant d'avoir été écrit.

### 7.2 Négatives — assumées

- **La CI s'allonge** : un restore d'outillage et deux exécutions au lieu d'une.
- **La discipline de double migration a un coût réel** à chaque changement de modèle, et elle sera parfois
  ressentie comme une friction. **C'est le prix d'une base de production qui ne dérive pas.**
- **La vérification physique (D-B) exige un serveur**, donc dépend d'
  [ADR-PROD-DB-008](adr-prod-db-008-postgresql-integration-testing.md). Tant que ce serveur n'existe pas en
  CI, **D-B n'est prouvée que localement** — et doit être présentée comme telle.
- **`.github/workflows/ci.yml` sera modifié.** C'est la **première modification du workflow depuis P4-0**, et
  elle doit être faite consciemment, sans toucher au reste du fichier.

### 7.3 Neutres

- Le contrôle SQLite existant garde exactement son comportement actuel.
- Aucun test existant n'est supprimé ni réécrit.

---

## 8. Obligations d'implémentation créées

| # | Obligation | Lot | Bloquante |
|---|---|---|---|
| **S1** | Ajouter le **second** contrôle de dérive (chaîne PostgreSQL) en CI, **bloquant**, sans serveur | P4-5E | **OUI** |
| **S2** | N'altérer aucune autre partie de `ci.yml` | P4-5E | **OUI** |
| **S3** | Vérifier **physiquement** le schéma PostgreSQL créé par migrations (colonnes, types, index filtrés, FK) | P4-5F | **OUI** |
| **S4** | Prouver la **reproductibilité** : base neuve migrée deux fois, schéma identique | P4-5F | **OUI** |
| **S5** | Interdire `EnsureCreated()` dans les tests d'intégration PostgreSQL et dans tout chemin de production | P4-5F | **OUI** |
| **S6** | Documenter la règle de double migration dans la documentation de contribution | P4-5E | **OUI** |
| **S7** | Transmettre la garde de version au démarrage à P4-6 (O8) | P4-6 | **OUI** |

---

## 9. Conditions de réexamen

- Si la génération d'une migration PostgreSQL venait à **exiger** une connexion serveur ⇒ réexaminer §5.1
  (le contrôle deviendrait dépendant d'un service, ce que V3 rejetait).
- Si le double contrôle produisait des faux positifs répétés ⇒ **diagnostiquer la cause**, jamais désactiver
  le contrôle : P2A-1R19 a montré que le bruit de dérive avait une **cause réelle**.
- Si un troisième provider apparaissait ⇒ un troisième contrôle, même forme.

---

## 10. Références

- [Audit P4-5A §20, AD-5, R-5](../implementation/P4-5A-postgresql-schema-audit-report.md)
- [P2A-1R19 — dérive de modèle, cause réelle et correctif](../implementation/P2A-1R19-report.md)
- [ADR-PROD-DB-005 — architecture des migrations](adr-prod-db-005-migration-architecture.md)
- [ADR-PROD-DB-006 — index et portabilité du modèle](adr-prod-db-006-index-and-model-portability.md)
- [ADR-PROD-DB-008 — tests d'intégration PostgreSQL](adr-prod-db-008-postgresql-integration-testing.md)
- [Rapport P4-5B](../implementation/P4-5B-postgresql-architecture-decisions-report.md)
