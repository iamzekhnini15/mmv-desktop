# P4-8 — Provisioning, secrets et sécurité de connexion — rapport d'implémentation

> Date : 4 octobre 2026. Branche `p4-8`, issue de `p4-multi-poste` = `59a6ac9` (P4-6B CLOSED, intégré par
> avance rapide de `ccbeb32`). Décisions : [ADR-PROD-DB-010](../architecture/adr-prod-db-010-provisioning-and-connection-security.md).
> Procédure : [opérateur](../operations/P4-8-provisioning-procedure.md).

## 1. Livré

| Zone | Élément | Décision |
|---|---|---|
| `MMV.Infrastructure/Configuration` | `PostgreSqlConnectionSecurity` — VerifyFull + `Require Auth=ScramSHA256`, défauts relevés, affaiblissement refusé | D-14 |
| | `PostgreSqlConnectionSettings`, `WorkstationDatabaseSettingsFile`, `DpapiCurrentUserSecretProtector` | D-13 |
| | `DatabaseProviderResolver.Resolve()` / `ResolveWorkstation` — fichier de poste + environnement durci, ambiguïté refusée ; `Resolve(env)` inchangé | D-13, D-14 |
| | `PostgreSqlConnectivityProbe`, `DatabaseUnavailableException` — une tentative bornée, cause classée, identité privilégiée refusée | D-15, DP-5 |
| `MMV.App` | vérification de disponibilité avant le garde-fou P4-3, hors `try` ; garde-fou **inchangé** (P4-6C) | D-15 |
| `MMV.DatabaseManager` | verbes `provision`, `rotate-role-password`, `bootstrap-admin`, `configure-workstation` ; secrets sur l'entrée standard ; chaîne du migrateur durcie (D-14) ; codes 16, 17, 18 | D-09, D-12, D-13, D-14 |
| | `ScramSha256Verifier` (vérificateur calculé par l'outil), `ServerSecurityAudit` (`ssl`, `password_encryption`, `pg_hba`) | D-14 |
| Dépendance | `System.Security.Cryptography.ProtectedData` 10.0.12 (Microsoft), dans `MMV.Infrastructure` | D-13 |
| CI | job d'intégration : `Tls/start-tls-servers.sh` (PostgreSQL 17.10 TLS valide + expiré, autorité étrangère, clés générées à chaque exécution) | D-14 |

**Aucun nom de rôle** n'existe dans le code de production : les trois noms sont des paramètres du provisioning.

## 2. Comportement mesuré (PostgreSQL 17.10, Npgsql 10.0.3)

| Cas | Forme | Classe |
|---|---|---|
| autorité étrangère, nom d'hôte différent, magasin système, certificat expiré | `NpgsqlException` ← `AuthenticationException` | `TlsValidationFailed` |
| serveur sans TLS en VerifyFull | `NpgsqlException` « No SSL enabled connection », sans cause | `TlsUnavailable` — **aucun repli** |
| secret faux **et** rôle inexistant | `28P01` (indiscernables, anti-énumération de PostgreSQL) | `AuthenticationFailed` |
| base inexistante | `3D000` | `DatabaseNotFound` |
| port fermé / hôte inconnu | `TimeoutException` / `SocketException` | `ServerUnreachable` |
| serveur demandant MD5 | refus client « authentication method is not allowed » | `AuthenticationMethodRefused` |

Constats : `Trust Server Certificate` est inopérant dans Npgsql 10 (seul `SSL Mode` compte) ; `Require Auth` est
sensible à la casse ; une session déjà ouverte n'est pas réauthentifiée après rotation.

## 3. Tests

| Suite | Avant | Après | Nouveaux |
|---|---|---|---|
| Unitaires (Windows) | 2148 | **2289** | +141 : politique de connexion, DPAPI réel, fichier de poste (corruption, altération, troncature, format), résolution, classification, SCRAM (vecteur connu Python), audit `pg_hba`, options, point d'entrée, test statique App |
| Intégration PostgreSQL (Linux) | 104 | **147** | +43 : provisioning, permissions (applicatif, sauvegarde, migrateur), idempotence, refus avant écriture, noms hostiles, rotation, premier administrateur (one-shot, concurrence ×4, authentification sous le rôle applicatif), configuration de poste, TLS/SCRAM |

- Local : unitaires 2289/2289 ; intégration 147/147 trois exécutions consécutives ; 0 avertissement, 0 erreur.
- **Ajustements de tests existants (assertions inchangées)** :
  1. `Status_through_the_real_entry_point…` (P4-6B) s'exécute sur le serveur **TLS** : le point d'entrée impose
     désormais VerifyFull ; un test jumeau prouve le **refus** (code 20) sur le serveur sans TLS.
  2. `LifecycleDatabase.DisposeAsync` vide **ses** pools au lieu de `ClearAllPools()`, qui fermait la session où
     **M3** garde volontairement un verrou consultatif : échec intermittent de M3 reproduit localement, corrigé.
- Aucun test supprimé, ignoré ni affaibli.

## 4. Base de données

Aucun changement de modèle EF ; `has-pending-model-changes` vert sur SQLite et PostgreSQL ; **14** migrations
SQLite, **1** PostgreSQL, instantanés inchangés. Le provisioning crée l'historique EF **vide** par le script d'EF
(`IHistoryRepository.GetCreateIfNotExistsScript`) pour le réduire à la lecture seule du rôle applicatif ;
conséquence : une base provisionnée non migrée est vue **E2** par la garde P4-6B (bloquante, comme E4).

## 5. Sécurité

- `dotnet list package --vulnerable --include-transitive` : **0** sur les 11 projets.
- Secrets : jamais en argument, variable de production, journal, message ni exception (tests dédiés) ; vérificateur
  SCRAM seul envoyé au serveur ; `ToString` des demandes et paramètres sans secret.
- Identifiants : cités par le serveur (`format('%I')`), paramètres liés, test de noms hostiles réels.
- `BackupSeamTests` (P4-6B) inchangés et verts : toujours une seule variable lue, une seule vérification de sauvegarde.

## 6. Questions ouvertes

**Q-P4-8-1** remplacement forcé du secret initial (changement de modèle EF requis) · **Q-P4-8-2** chiffrement du
stockage serveur (infrastructure) · **Q-P4-8-3** preuve Windows native (O12). Détail : ADR-PROD-DB-010 §6.
