# P4-12 — Préparation de l'audit final : état des 18 critères de sortie

> **Ce document n'est PAS l'audit final P4-12** et ne prononce **aucun** verdict. Il prépare l'audit : pour chaque
> critère de la [roadmap §4](../architecture/P4-multi-poste-roadmap.md), la preuve existante, son lieu (CI Linux /
> Windows natif) et ce qui manque. État au 05/10/2026, après P4-7 et P4-10.
> **V1 MULTI-POSTE = NOT GO** tant que P4-11 n'est pas exécutée et que l'audit P4-12 n'a pas statué.

| # | Critère | Preuve existante | Lieu | Reste |
|---|---|---|---|---|
| 1 | Provider choisi par ADR | ADR-PROD-DB-002 acceptée (PostgreSQL) | — | — |
| 2 | Base neuve installable | procédure P4-8 (`provision`), P4-9, P4-7 ; tests de bout en bout TLS | CI Linux | **installation Windows native** (P4-11 §1) |
| 3 | Connectable depuis plusieurs postes | multi-processus P4-10 (processus OS distincts) ; `configure-workstation` (P4-8) | CI Linux | **recette réelle sur plusieurs postes Windows** (P4-11 §3–4) |
| 4 | Migrations reproductibles | `SchemaTests.N1_two_freshDatabases_migrated_independently_have_identical_schemas` ; dérive EF ×2 en CI | CI | — |
| 5 | 14 primitives re-prouvées | N6 (`PrimitivesTests`, P4-5F) ; concurrence multi-processus P4-10 | CI Linux | Windows natif (Q-P4-4-1 → P4-11) |
| 6 | Erreurs provider traduites | `PersistenceErrorMapper` (P4-4A1) + P4-10 (issue de validation inconnue, ouverture de transaction) ; tests unitaires et réseau réel | CI | — |
| 7 | Données SQLite migrables ou échec sûr | `import-sqlite` (P4-7) : 17 preuves serveur dont bout en bout | CI Linux | **dry-run sur copie de production réelle (RR4)** + durée réelle (P4-11 §2) |
| 8 | Tests multi-processus verts | `MultiProcessTests` (10 scénarios roadmap) + charge (2) | CI Linux | Windows natif (P4-11 §4) |
| 9 | Perte réseau testée | `NetworkFaultTests` (proxy TCP, 6) | CI Linux | câble débranché en recette (P4-11 §5.1) |
| 10 | Reconnexion testée (redémarrage serveur) | `ServerRestartTests` (redémarrage réel d'un conteneur dédié, 2) | CI Linux | redémarrage du service Windows (P4-11 §5.3) |
| 11 | Sauvegarde testée | P4-9 (28 preuves, vrais `pg_dump`/`pg_restore`) ; sous charge (P4-10) | CI Linux | tâche planifiée Windows, BitLocker, ACL (Q-P4-9-1 → P4-11 §6) |
| 12 | Restauration testée | P4-9 (restauration réelle + intégrité) ; retour arrière d'import (P4-7) | CI Linux | exercice de sinistre en recette (P4-11 §6.4) |
| 13 | Documentation opérateur | procédures P4-7, P4-8, P4-9 ; checklist P4-11 | dépôt | relecture par l'exploitant en recette |
| 14 | Secrets non committés | balayage automatique (05/10/2026) hors tests : seul le mot de passe **éphémère** du conteneur CI (`mmv_it_ephemeral`) et deux lignes de rapport **masquées** ; secrets TLS / restart / sauvegarde générés à chaque exécution | dépôt | revue humaine P4-12 |
| 15 | Build et tests verts | dernière CI verte sur le SHA exact (voir roadmap §6) : unitaires 2457, intégration 232 | CI | — |
| 16 | Aucune vulnérabilité | `dotnet list package --vulnerable --include-transitive` : 0 sur 12 projets (bloquant en CI) | CI | — |
| 17 | Domain/Application provider-neutres | `ProviderNeutralityArchitectureTests` | CI | — |
| 18 | Recette complète multi-postes | — | — | **P4-11 entière** |

## Hors code, à trancher ou à constater avant le verdict

- **ADR-APP-DISTRIBUTION-001 reste PROPOSED** (spikes SD-1/2/3/5, QD-1, QD-10 ouverts) : la recette P4-11 se fait
  avec un paquet `dotnet publish` autonome copié (vérifié constructible le 05/10/2026 : MMV.App 120 Mo, outil 89 Mo),
  **pas** avec un installateur signé. L'audit P4-12 doit dire si la V1 multi-poste exige l'installateur.
- **RR4** (P4-5D-R) : dry-run sur copie de production — couvert par P4-11 §2.2.
- Questions ouvertes non bloquantes recensées : Q-P4-7-1, Q-P4-7-2, Q-P4-8-1, Q-P4-8-2, Q-P4-9-2 (= Q-P4-10-3),
  Q-P4-10-1, Q-P4-10-2, Q-P4-10-4.
- Licence d'évaluation des VM du laboratoire (P4-11 §0.2).
