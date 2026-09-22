# P4-5D Final Implementation Report

> **Lot clôturé.** Validation architecte obtenue, vérification finale exécutée, commit créé.
>
> Date : 18 septembre 2026. Branche : `p4-multi-poste`.
> `HEAD` avant : **`b5c2073`** — `HEAD` après : **`023048f`**.
> Aucun push n'a été effectué, aucune branche n'a été créée.

---

## Objective

Appliquer la stratégie temporelle définie par [ADR-PROD-DB-004](../architecture/adr-prod-db-004-datetime-strategy.md)
à l'ensemble du dépôt, en préparation du passage en production sur PostgreSQL :

1. **Tout instant est en UTC.** Plus aucune lecture de `DateTime.Now` sur un chemin persisté.
2. **Tout instant est injecté**, jamais lu depuis le code métier : `IClock` en Domain, `SystemClock` en Infrastructure.
3. **Les dates civiles sont des dates**, pas des instants : `Customer.BirthDate` et `Prescription.IssueDate`
   passent de `DateTime` à `DateOnly` — une date de naissance n'a ni heure ni fuseau.
4. **La garantie est vérifiée à la lecture comme à l'écriture** : `UtcDateTimeConverter` normalise en
   persistance et rejette (`NonUtcDateTimeException`) ce qui ne serait pas UTC.
5. **La règle est exécutable**, pas seulement écrite : tests d'architecture interdisant l'horloge ambiante.

---

## Architect Review Summary

La revue architecturale a porté sur le rapport de pré-revue de phase A et a émis **trois corrections**,
toutes appliquées en phase A2 et intégrées à ce commit.

| # | Correction demandée | Nature | État |
|---|---|---|---|
| **C1** | `Notification.CreatedAt` ne doit plus lire l'horloge système — le défaut de propriété `= DateTime.UtcNow` constituait un précédent architectural, même avec tous les chemins applicatifs corrects | Code + tests | **appliquée** |
| **C2** | Documenter explicitement les exceptions d'architecture, avec portée autorisée **et** interdits | Documentation | **appliquée** |
| **C3** | Tracer la reprise des dates civiles dans la roadmap P4, à l'endroit où l'ordonnancement se décide | Documentation | **appliquée** |

**C1 en détail** : `CreatedAt` devient `public required DateTime CreatedAt { get; set; }`. La garantie
passe du runtime au **compilateur** — un chemin de création qui omettrait l'horodatage ne compile pas.
`required` a été retenu contre un constructeur (qui aurait fait de `Notification` la seule entité à
constructeur du Domain) et contre une validation FluentValidation (qui n'aurait échoué qu'à l'exécution).
À la première compilation après le changement, le compilateur a signalé **exactement 4 erreurs `CS9035`,
toutes dans `tests/`, aucune dans `src/`** : la preuve, par le compilateur, qu'aucun chemin de production
ne dépendait du défaut supprimé.

Détail complet des corrections : [P4-5D-temporal-strategy-pre-review-v2.md](P4-5D-temporal-strategy-pre-review-v2.md).
Rapport de phase A : [P4-5D-temporal-strategy-pre-review.md](P4-5D-temporal-strategy-pre-review.md).

---

## Final Changes

**86 fichiers** dans le commit : **68 modifiés**, **18 ajoutés**.

| Zone | Fichiers | Contenu |
|---|---|---|
| `src/MMV.Domain` | 7 | `IClock`, entités `Customer` / `Prescription` (`DateOnly`) / `Notification` (`required`), interfaces de repositories, `PrescriptionValidator` |
| `src/MMV.Application` | 10 | Use cases horodatés via `IClock` injectée, commandes et DTO en `DateOnly` |
| `src/MMV.Infrastructure` | 9 | `SystemClock`, `UtcDateTimeConverter`, `NonUtcDateTimeException`, `OpticDbContext`, repositories, seeders |
| `src/MMV.App` | 7 | `DatePickerCivilDate`, ViewModels et composition root |
| `tests/` | 50 | 3 projets — tests temporels neufs + adaptation des fixtures existantes |
| `docs/` | 3 | 2 rapports de pré-revue, roadmap P4 (étape `P4-5D-R`) |

**Hors périmètre, vérifié avant commit** : aucune migration EF, aucun ADR, aucune CI, aucun `.csproj`,
`.sln` ou paquet n'a été touché.

---

## Temporal Architecture Implemented

### Les quatre pièces

| Composant | Rôle |
|---|---|
| `IClock` (Domain) | L'abstraction de l'instant. Le code métier la **reçoit**, il ne lit pas l'horloge. |
| `SystemClock` (Infrastructure) | L'unique implémentation de production. Enregistrée en `Singleton`. |
| `UtcDateTimeConverter` (Infrastructure) | Convertisseur EF appliqué à **toute** colonne `DateTime` : normalise en UTC à l'écriture, restitue `Kind = Utc` à la lecture. |
| `NonUtcDateTimeException` | Ce qui n'est pas convertible en UTC **échoue bruyamment**, au lieu d'être persisté silencieusement faux. |

### Instant contre date civile

La distinction structurante du lot : **un instant n'est pas une date**.

- **Instant** (`CreatedAt`, `OrderDate`, `SaleDate`, …) : `DateTime` en UTC, fourni par `IClock`.
- **Date civile** (`Customer.BirthDate`, `Prescription.IssueDate`) : `DateOnly`. Une date de naissance ne
  subit **aucune** conversion de fuseau — c'est précisément la classe de bug que le type élimine.
  `DatePickerCivilDate` fait le pont côté UI, où Avalonia n'expose qu'un `DateTimeOffset`.

### Repositories : l'instant est un paramètre

Un repository qui a besoin d'un instant le **reçoit en paramètre de méthode**
(`GetOverdueOrdersAsync(DateTime asOfUtc, …)`) ; il ne le lit pas. Règle posée comme réutilisable pour
les futurs repositories.

### La règle est exécutable

`AmbientTimeArchitectureTests` interdit l'horloge ambiante par test, et non par convention orale.
`LegacyCivilDateFormatTests` (6 tests) constitue la **spécification exécutable** de la reprise de données
à venir : elle établit que la transformation est une **troncature**, jamais une conversion de fuseau, et
qu'elle est donc exprimable en SQL pur.

### Exceptions d'architecture, bornées

Trois écarts assumés, chacun avec sa portée autorisée **et** ses interdits : `IClock` optionnel dans les
ViewModels Avalonia (construites à la main, hors conteneur) ; `SystemClock.Instance` en accès statique
dans les trois seuls contextes sans injection disponible (composition root, code framework UI, seeders
statiques) ; `FixedClock` dupliqué entre projets de test. **Le Domain ne figure dans aucune colonne
« autorisé »** — après C1, plus une seule entité ne lit l'horloge.

---

## Tests Executed

```bash
git diff --stat ; git diff --name-only
git status --short src/MMV.Infrastructure/Migrations/
dotnet build MMV.sln -c Debug
dotnet test  MMV.sln -c Debug
dotnet ef migrations has-pending-model-changes \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure
```

### Build

| Mesure | Valeur |
|---|---|
| Erreurs | **0** |
| Avertissements | **0** |

### Suite complète — 1 758 tests

| Projet | Total | Réussis | Échecs | Ignorés |
|---|---|---|---|---|
| `MMV.Domain.Tests` | 872 | 872 | **0** | **0** |
| `MMV.Application.Tests` | 628 | 628 | **0** | **0** |
| `MMV.App.Tests` | 258 | 258 | **0** | **0** |
| **Total** | **1 758** | **1 758** | **0** | **0** |

Aucun test ignoré, aucune assertion affaiblie.

---

## Migration Status

> `No changes have been made to the model since the last migration.`

| Contrôle | Résultat |
|---|---|
| Dérive de modèle EF | **aucune** |
| Migrations créées, modifiées ou supprimées | **0** — `git status --short src/MMV.Infrastructure/Migrations/` renvoie vide |
| Migrations dans le commit | **0** — vérifié sur `HEAD` après commit |
| Snapshot de modèle | **inchangé** |

`required` sur `Notification.CreatedAt` ne modifie pas le modèle relationnel : la colonne était déjà
déclarée `IsRequired()` en configuration EF, et son type est inchangé.

**Point d'attention majeur** — l'absence de dérive **ne signifie pas** l'absence de reprise de données à
faire. Voir *Known Limitations*.

---

## Git Information

| Élément | Valeur |
|---|---|
| Branche | `p4-multi-poste` |
| SHA avant | `b5c2073e73eab100deb2843829f1a687a7c9a31d` |
| **SHA après** | **`023048fb2a9cd4cd2e22b173f0157a6f0c57ef60`** |
| Commits créés | **1** — aucun squash, aucun commit antérieur modifié |
| Fichiers dans le commit | 86 (68 modifiés, 18 ajoutés) — +3 774 / −222 |
| État de l'arbre après commit | **propre** |
| Push | **NON** |
| Branche créée | **NON** |

Message de commit :

```
feat(P4-5D): implement temporal strategy and UTC enforcement

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Known Limitations

### 1. Déploiement bloqué sur toute base existante — `P4-5D-R`

**C'est la limitation qui prime sur toutes les autres.** Le passage à `DateOnly` change le **format** des
valeurs `TEXT` SQLite (`yyyy-MM-dd HH:mm:ss[.fffffff]` → `yyyy-MM-dd`) **sans qu'EF ne génère de
migration** : `has-pending-model-changes` reste vert, comme constaté plus haut. C'est le piège signalé
par l'ADR §7.2.

Conséquence : une base antérieure à P4-5D lève **`FormatException`** à la lecture d'un client ou d'une
ordonnance. L'étape **`P4-5D-R` — Civil Date Data Migration Preparation** est tracée en roadmap, statut
`NOT STARTED`, dépendance P4-5D. Son **ordonnancement reste à trancher** par l'architecte.

### 2. Questions d'architecture ouvertes

| # | Question | Statut |
|---|---|---|
| Q1 | `IClock` optionnel en UI — à rendre obligatoire malgré le coût de 25 sites ? | Documenté et borné ; optionnalité **conservée** |
| Q2 | `DbInitializer` statique — à transformer en classe d'instance ? | Ouverte |
| Q3 | Instant en paramètre de méthode pour les repositories — à valider comme principe général ? | Inscrit en exception bornée ; à confirmer |
| Q5 | `DateTimeOffset.UtcNow` à ajouter aux interdits ; 57 `DateTime.UtcNow` non routés vers `IClock` | Ouverte |
| Q6 | Test d'architecture ignorant les commentaires — analyseur Roslyn en P4-5F ? | Ouverte |
| Q7 | `FixedClock` dupliqué — projet `MMV.TestSupport` ? | Documenté et borné, non modifié |

*(Q4 est devenue `P4-5D-R` ci-dessus ; Q8 est résolue par C1.)*

### 3. Deux décisions de convention à arbitrer

1. **Étendre `required` aux autres entités datées ?** `Order`, `Sale`, `StockMovement`… portent encore
   `= DateTime.UtcNow` comme défaut de propriété. La **valeur est correcte** ; c'est le **mécanisme** que
   C1 vient d'écarter. Cohérent à faire, mais c'est un lot à part entière. **Lot dédié, ou dette acceptée ?**
2. **`required` devient-il la convention du Domain** pour les données obligatoires à la construction ?
   `Notification.CreatedAt` en est la première utilisation du dépôt. Si oui, cela mérite d'être écrit
   ailleurs que dans un rapport de lot.

---

## Next Recommended Task

**`P4-5D-R` — Civil Date Data Migration Preparation**, avant toute autre étape de P4-5.

La raison est d'ordonnancement, pas de préférence : `P4-5D-R` **bloque le déploiement sur toute base
existante**, et ce blocage ne se résorbera pas tout seul — aucun outil ne le signalera, puisque la dérive
de modèle EF est vierge. Tant qu'il tient, P4-5D est complet **en code** mais non déployable **en l'état**
sur une installation en service.

Le travail est cadré et peu coûteux : `LegacyCivilDateFormatTests` en fournit déjà la spécification
exécutable, et la transformation est une troncature exprimable en SQL pur. Restent à produire le script de
reprise, sa vérification sur une copie de base réelle, et sa place dans la séquence de déploiement.
