# P4-6C — Levée du garde-fou de démarrage serveur : rapport de clôture

> **Verdict : `P4-6C = COMPLETE — CLOSED`** (5 octobre 2026) — donc **`P4-6 = COMPLETE — CLOSED`**. Branche
> `p4-6c`, base `3757edb` (clôture P4-9). Aucune décision nouvelle : DP-1, DP-4 (ADR-PROD-DB-009), D-12.3, D-15
> (ADR-PROD-DB-010) ; addendum ADR-009 §13.

## 1. Commit et CI

| Commit | CI | Résultat sur le SHA exact |
|---|---|---|
| `0d2d3e3` | `37290396173` | **success** — Windows : unitaires **2384** (DatabaseManager 288 · App 268 · Application 628 · Domain 1200), aucune vulnérabilité High/Critical, dérive EF SQLite + PostgreSQL verte ; Linux : PostgreSQL 17.10 IT **195/195**, 0 ignoré, garde P4-9 28 |

Écart avec P4-9 : +11 unitaires, +10 IT.

## 2. Livré

| Élément | Effet |
|---|---|
| `Infrastructure/Data/ServerStartupCheck` | disponibilité (une tentative bornée, identité non privilégiée) puis garde de compatibilité en lecture seule ; `null` ⇒ démarrage, sinon `ServerStartupBlock` (titre, constat, action). Erreur serveur ⇒ blocage affiché, jamais avalée |
| `App.axaml.cs` | garde-fou P4-3 retiré ; branche PostgreSQL hors de tout `try`, terminée avant le cycle de vie SQLite et le seed |
| `DatabaseBlockedView` / `DatabaseBlockedViewModel` | écran minimal : constat, action, **Quitter** seulement |
| `ServerSchemaVerification` (outil, étape 9) | invariants physiques : `Notifications.ResolvedAt` + index unique filtré (colonnes et prédicat), `Users.NormalizedUsername` + index unique non filtré, agrégats lisibles |

| État | Écran |
|---|---|
| E1, E3a | démarrage normal |
| E2 | « Mise à jour de la base requise » |
| E3b | « Mise à jour du poste requise » |
| E3c | « Version incompatible » |
| E4 | « Base non installée » |
| E5 | « Maintenance en cours » / « Installation de la base inachevée » |
| E7 | « Base non reconnue » |
| E6 | « Base centrale indisponible » / « Configuration du poste refusée » |

## 3. Preuves

- **Serveur réel** (`Startup/ServerStartupTests`, rôle applicatif d'une base provisionnée) : E2 (provisionnée non
  migrée, rien écrit), E1 (aucun seed), E5 (marqueur), E3b puis E3a (fenêtre N-1), serveur refusé (secret jamais
  restitué, aucune nouvelle tentative), identité privilégiée refusée ; invariants : schéma de production valide,
  index de login supprimé ou non unique, prédicat affaibli, colonne `ResolvedAt` supprimée ⇒ vérification en échec.
- **Unitaires** : un écran distinct par état, trois messages DP-4 distincts, installation inachevée ≠ maintenance ;
  ViewModel « Quitter » seul ; preuve statique de l'ordre de démarrage (contrôle hors `try`, aucun
  `SqliteDatabaseManager`, `PrepareDatabase`, `Seed`, `Migrate(`, `EnsureCreated(` sur la branche serveur).
- **Mutations vues en échec puis retirées** : E5 servi (3 unitaires + 1 IT rouges) ; contrôle du prédicat supprimé
  (1 IT rouge).

## 4. Schéma, sécurité

Aucune modification du modèle EF, aucune migration ; le poste n'écrit rien au démarrage (lecture seule prouvée),
ne migre jamais, ne sème rien. Aucun secret dans les messages (IT). Aucun paquet ajouté.

## 5. Hors périmètre / suites

- Preuve Windows native de l'écran (O12) : recette P4-11.
- DI-3.4 (plancher de version applicative) : ADR-APP-DISTRIBUTION-001 encore **PROPOSED**.
- Résilience, reconnexion : **P4-10** (D-15). Import SQLite → serveur : **P4-7** (dépend de P4-5, non clos).
