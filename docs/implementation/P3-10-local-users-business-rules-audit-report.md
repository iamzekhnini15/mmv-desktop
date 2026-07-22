# P3-10 — Audit backend des utilisateurs locaux et de la sécurité minimale

> **MODE = AUDIT_AND_WRITE_REPORT_ONLY.** Aucun code, test, migration ni UI n'a été modifié.
> Ce document est le seul livrable. Verdict en §28.

---

## 1. Paramètres

| Clé | Valeur attendue | Valeur observée | OK |
|---|---|---|---:|
| REPOSITORY | iamzekhnini15/mmv-desktop | (local) | ✅ |
| BRANCH | p3-business-rules | p3-business-rules | ✅ |
| PHASE | P3-10 | P3-10 | ✅ |
| SCOPE | Backend uniquement | Backend uniquement | ✅ |
| EXPECTED_HEAD | 057df0f3fad91f344a6333f3a52f32b6d1552ba1 | 057df0f3fad91f344a6333f3a52f32b6d1552ba1 | ✅ |
| EXPECTED_CI_RUN | 29831430629 | 29831430629 | ✅ |
| EXPECTED_TESTS | 1308 | 1308 | ✅ |

---

## 2. État Git et CI

```
git branch --show-current      → p3-business-rules
git rev-parse HEAD             → 057df0f3fad91f344a6333f3a52f32b6d1552ba1
git rev-parse origin/...       → 057df0f3fad91f344a6333f3a52f32b6d1552ba1
git status --short             → seuls design-handoff/, design/, docs/ui/ non suivis
git diff --check              → (rien)
```

- Branche = `p3-business-rules` ✅
- HEAD local = distant = `057df0f…` ✅
- Aucun fichier **suivi** modifié ✅
- Seuls dossiers non suivis autorisés présents ✅

CI `gh run view 29831430629` :

| Champ | Valeur |
|---|---|
| databaseId | 29831430629 |
| headSha | 057df0f3fad91f344a6333f3a52f32b6d1552ba1 |
| headBranch | p3-business-rules |
| event | **push** ✅ |
| status | **completed** ✅ |
| conclusion | **success** ✅ |

**Gate Git/CI : PASS.**

---

## 3. Baseline locale

| Contrôle | Attendu | Observé | OK |
|---|---|---|---:|
| `dotnet build` | 0 erreur / 0 avert. | 0 erreur / 0 avert. | ✅ |
| Domain | 537 | 537 | ✅ |
| Application | 532 | 532 | ✅ |
| App | 239 | 239 | ✅ |
| **Total** | **1308** | **1308** | ✅ |
| Échecs | 0 | 0 | ✅ |
| Ignorés | 0 | 0 | ✅ |
| Vulnérabilités (`list package --vulnerable --include-transitive`) | aucune | aucune | ✅ |
| Migrations en attente (`has-pending-model-changes`) | aucune | *No changes…* | ✅ |
| Références `MMV.Application` | `MMV.Domain` seul | `MMV.Domain` seul (+ pkg `Microsoft.Extensions.DependencyInjection.Abstractions`) | ✅ |

**Gate baseline : PASS.**

---

## 4. Documents lus

Documents d'architecture / implémentation consultés (le **code réel prime**) :

- `docs/architecture/P3-business-rules-roadmap.md` — §P3-10 (l. 552-560) : politique déjà présente à **appliquer partout**, **désactivation** plutôt que suppression, **seed prod sûr**. Reports « auteur du QC → P3-10 » (l. 382) et « PerformedByUserId » (l. 283).
- `docs/architecture/adr-environments-seeding.md` — gouvernance du seeding P2A-1F.
- `docs/implementation/P3-1-validation-result-report.md` — convention P3-1 (refus dur = exception typée / résultat structuré).
- `docs/implementation/P3-6B-workshop-sheet-*`, `P3-7-sales-*`, `P3-9-suppliers-*` — style des rapports et patrons (validator + normalizer + suppression conditionnelle atomique + refus DB `Restrict`).
- `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md`, `adr-application-boundaries.md`, `adr-transaction-idempotency.md`, `adr-sqlite-lifecycle.md` — frontières, idempotence, cycle SQLite.

---

## 5. Cartographie complète

| Élément | Chemin exact | Couche | Responsabilité actuelle |
|---|---|---|---|
| `User` (entité) | `src/MMV.Domain/Entities/User.cs` | Domain | État du compte ; setters publics, aucun invariant |
| `UserRole` | `src/MMV.Domain/Enums/UserRole.cs` | Domain | Enum `Admin, Optician, Technician` |
| `UserValidator` | `src/MMV.Domain/Validators/UserValidator.cs` | Domain | Validation champs + `ValidatePasswordPolicy` (statique) + `GetPasswordStrength` + `GenerateStrongPassword` |
| `IUserRepository` | `src/MMV.Domain/Interfaces/Repositories/IUserRepository.cs` | Domain | `GetByUsername`, `GetActiveUsers`, `GetByRole` |
| `IAuthenticationService` | `src/MMV.Domain/Services/IAuthenticationService.cs` | Domain (contrat) | `Authenticate`, `ValidatePassword`, `HashPassword`, `UpdateLastLogin`, `ChangePassword` |
| `CreateUserUseCase` (+Command/Result/I) | `src/MMV.Application/UseCases/Users/CreateUser/*` | Application | Création + hachage + garde unicité |
| `UpdateUserUseCase` (+Command/Result/I) | `src/MMV.Application/UseCases/Users/UpdateUser/*` | Application | Modification profil/rôle/actif + rehash conditionnel |
| `SetUserActiveUseCase` (+Command/Result/I) | `src/MMV.Application/UseCases/Users/SetUserActive/*` | Application | Activation/désactivation (valeur absolue) |
| `ListUsersUseCase` (+Query/Dto/I) | `src/MMV.Application/UseCases/Users/ListUsers/*` | Application | Lecture projetée vers DTO plat |
| `AuthenticationService` | `src/MMV.Infrastructure/Services/AuthenticationService.cs` | Infrastructure | BCrypt WF=11 ; Authenticate/Verify/Hash/ChangePassword |
| `UserRepository` | `src/MMV.Infrastructure/Repositories/UserRepository.cs` | Infrastructure | Requêtes EF (AsNoTracking via base) |
| `BaseRepository` | `src/MMV.Infrastructure/Repositories/BaseRepository.cs` | Infrastructure | `GetByIdAsync` (FindAsync, **tracké**) ; `GetAllAsync`/`GetQueryable` **AsNoTracking** |
| `UserConfiguration` | `src/MMV.Infrastructure/Data/Configurations/UserConfiguration.cs` | Infrastructure | Colonnes, index unique `idx_users_username_unique`, FK Sales/StockMovements `SetNull` |
| `DatabaseSeeder` | `src/MMV.Infrastructure/Configuration/DatabaseSeeder.cs` | Infrastructure | Politique seed prod/dev + neutralisation/sécurisation admin faible |
| `SeedOptions` / `SeedOptionsResolver` / `ApplicationEnvironment` / `SeedResult` / `SeedConfigurationException` | `src/MMV.Infrastructure/Configuration/*` | Infrastructure | Résolution env + secret bootstrap (env var `MMV_*`), validé contre la politique |
| `DbInitializer` | `src/MMV.Infrastructure/Data/DbInitializer.cs` | Infrastructure | Jeu de démonstration (dont 4 comptes mot de passe « admin ») |
| Migration `InitialCreate` | `src/MMV.Infrastructure/Migrations/20260127184542_InitialCreate.cs` | Infrastructure | Seed SQL de `admin` (UserId=1, hash faible connu) + index unique username |
| `LoginViewModel` | `src/MMV.App/ViewModels/LoginViewModel.cs` | UI (lecture) | Appelle `AuthenticateAsync` |
| `UserFormViewModel` | `src/MMV.App/ViewModels/UserFormViewModel.cs` | UI (lecture) | **Applique** `ValidatePasswordPolicy` (l.443) avant create/update |
| `UserProfileViewModel` | `src/MMV.App/ViewModels/UserProfileViewModel.cs` | UI (lecture) | **Applique** la politique (l.276) puis `ChangePasswordAsync` |
| `UsersListViewModel` | `src/MMV.App/ViewModels/UsersListViewModel.cs` | UI (lecture) | Liste + toggle actif (via use case) |
| `SessionService` / `ISessionService` | `src/MMV.App/Services/SessionService.cs` | UI (lecture) | Session en mémoire, `IsAdmin`, timeout inactivité 30 min |

**Références historiques (FK vers `Users`) :**

| Table/entité | Colonne | Nullable | DeleteBehavior | Fichier |
|---|---|---:|---|---|
| `StockMovement` | `PerformedByUserId` | **oui** (`long?`) | `SetNull` | `Entities/StockMovement.cs:46`, `Configurations/StockMovementConfiguration.cs:44` |
| `Sale` | `StaffId` | **oui** (`long?`) | `SetNull` | `Entities/Sale.cs:29`, `Configurations/SaleConfiguration.cs:73` |

Aucune autre colonne d'auteur (`CheckedByUserId`, `QualityCheckedBy`, `CreatedByUserId`, `UpdatedByUserId`, `ApprovedBy`, `DeletedBy`, `CurrentUserId`) n'existe. Les fiches atelier (P3-6B) **ne portent pas** d'auteur QC → report explicite « auteur du QC → P3-10 » (roadmap l.382), non implémenté à ce jour.

---

## 6. Modèle réel de `User`

| Champ | Type | Nullable | Longueur | Défaut | Index | Mutable | Usage réel |
|---|---|---:|---:|---|---:|---:|---|
| `UserId` | `long` | non | — | auto (PK) | PK | set public | Identité |
| `Username` | `string` | non | 50 | `""` | **unique** `idx_users_username_unique` | oui | Identifiant de connexion |
| `PasswordHash` | `string` | non | — (illimité en SQLite) | `""` | — | oui | Hash BCrypt (sel+coût inclus) |
| `FirstName` | `string` | non | 100 | `""` | — | oui | Profil |
| `LastName` | `string` | non | 100 | `""` | — | oui | Profil |
| `Role` | `UserRole` | non | — (stocké `TEXT`) | `Admin` (0) | — | oui | Autorisation décorative |
| `IsActive` | `bool` | non | — | `true` (défaut SQL) | — | oui | Activation |
| `LastLogin` | `DateTime?` | oui | — | `null` | — | oui | Dernière connexion |
| `CreatedAt` | `DateTime` | non | — | `DateTime.UtcNow` (app) | — | oui | Création |

Réponses :

1. **Identifiant de connexion** : `Username`.
2. **Obligatoire** : oui (`NotEmpty`, `IsRequired`, min 3 / max 50, regex `^[a-zA-Z0-9._-]+$`, sans espaces).
3. **Normalisé** : partiellement — `Trim()` en Application/Auth ; **pas** de normalisation de casse.
4. **Unique en Application** : oui mais **sensible à la casse** (`u.Username == username`).
5. **Unique en base** : oui, index unique — mais collation **BINARY** (sensible à la casse).
6. **Casse significative** : **oui** (`admin` ≠ `Admin` ≠ `ADMIN`).
7. **Espaces périphériques** : retirés par `Trim()` sur les chemins Create/Update/Authenticate.
8. **Mot de passe en clair champ de l'entité** : **non** (seul `PasswordHash`).
9. **Champs hash/sel** : `PasswordHash` uniquement ; **pas** de colonne sel (le sel + coût sont encodés dans la chaîne BCrypt `$2a$11$…`).
10. **Rôle** : enum stocké en **chaîne** (`HasConversion<string>()`).
11. **`IsActive`** : oui, `NOT NULL`, défaut `true`.
12. **Création / dernière connexion** : `CreatedAt` (oui), `LastLogin` (oui, nullable).
13. **Compteur d'échecs / verrouillage** : **non**.
14. **Token de concurrence** : **non** (pas de `RowVersion`/`xmin`).
15. **Setters publics** : **oui** (tous).
16. **Auto-état invalide** : oui — l'entité n'a aucun invariant (POCO à setters publics).
17. **Valeur vide/trop longue persistable** : le validateur FluentValidation **n'est pas invoqué** par les use cases ; les longueurs `HasMaxLength` ne sont **pas** contraignantes sous SQLite (voir §20). Donc oui, une valeur hors politique peut être persistée par un appel Application direct.
18. **Clé métier stable ≠ PK** : `Username` (candidat), mais mutable et sensible à la casse.

---

## 7. Inventaire exhaustif des écritures

| Chemin | Déclencheur | Champs écrits | Validation | Hachage | Transaction | Concurrence sûre |
|---|---|---|---|---|---|---|
| `CreateUserUseCase.ExecuteAsync` | Application (UI create) | Username, First/Last, Role, IsActive, PasswordHash, CreatedAt | garde unicité seule ; **pas** de politique MDP ni FluentValidation | oui (`HashPassword`) | mono-écriture | **non** (TOCTOU unicité ; exception DB non traduite) |
| `UpdateUserUseCase.ExecuteAsync` | Application (UI edit) | Username, First/Last, Role, IsActive, (PasswordHash si fourni) | garde unicité si nom change ; **pas** de politique MDP | conditionnel | mono-écriture | **non** (lost update, pas de CAS/token) |
| `SetUserActiveUseCase.ExecuteAsync` | Application (UI toggle) | IsActive | trouvé/introuvable | — | mono-écriture | **non** (pas de CAS) |
| `AuthenticationService.ChangePasswordAsync` | UI profil | PasswordHash | vérifie ancien MDP ; **pas** de politique | oui | mono-écriture | **non** (lost update) |
| `AuthenticationService.UpdateLastLoginAsync` | login réussi | LastLogin | — | — | mono-écriture | best-effort |
| `DatabaseSeeder.SecureBootstrap` | démarrage | Username/PasswordHash/IsActive/(nouvel admin) | secret validé en amont (resolver) | oui (WF11) | `SaveChanges` | démarrage mono-poste |
| `DbInitializer.Initialize` (démo) | Dev/Demo explicite | 4 comptes (hash « admin ») | — | hash figé en dur | `SaveChanges` | idempotent (garde `Customers.Any()` + `existingUsernames`) |
| Migration `InitialCreate` (`InsertData`) | 1re migration | admin UserId=1 (hash faible connu) | — | hash figé | migration | une fois |
| UI directe | — | **aucune** : les VM passent par les use cases / le service | — | — | — | — |
| Code mort | `DbSeeder.SeedAsync` (`Seeders/DbSeeder.cs`) | **n'écrit aucun User** | — | — | — | — |

Distinction : **runtime** = Create/Update/SetActive/ChangePassword/UpdateLastLogin ; **seed** = DatabaseSeeder/DbInitializer/migration ; **migration** = InitialCreate ; **UI directe** = néant ; **code mort** = `DbSeeder` (n'insère pas d'utilisateur) ; **test only** = fabriques de test.

> **Constat central.** Aucun chemin **runtime d'écriture de mot de passe** (Create, Update, ChangePassword) n'applique `ValidatePasswordPolicy`. La politique n'est appliquée que par l'UI (`UserFormViewModel`, `UserProfileViewModel`) et par le resolver de seed. **La politique est contournable dès qu'on entre par la couche Application.**

---

## 8. Politique de mot de passe

Contenu réel (`UserValidator.ValidatePasswordPolicy`) :

- longueur minimale : **8** ; maximale : **aucune** ;
- **1 majuscule**, **1 minuscule**, **1 chiffre** requis ;
- caractère spécial : **non requis** ; espaces : non interdits explicitement (blanc pur rejeté par `IsNullOrWhiteSpace`) ;
- Unicode : `char.IsUpper/IsLower/IsDigit` (donc Unicode partiellement toléré) ;
- répétitions / mot de passe commun / inclusion du nom d'utilisateur : **non vérifiés** ;
- messages : stables (français, constants).

| Chemin d'écriture | Politique appliquée ? | Avant hachage ? | Aucune écriture si invalide ? |
|---|---:|---:|---:|
| `CreateUserUseCase` | **non** | — | non (écrit un MDP faible) |
| `UpdateUserUseCase` (MDP fourni) | **non** | — | non |
| `AuthenticationService.ChangePasswordAsync` | **non** | — | non |
| `DatabaseSeeder` (secret bootstrap) | **oui** (via resolver) | oui | oui (lève `SeedConfigurationException`) |
| UI `UserFormViewModel` (l.443) | oui | oui | oui (bloque le submit) |
| UI `UserProfileViewModel` (l.276) | oui | oui | oui |
| Seed démo / migration | non (hash figés) | — | — |

Réponses :

1. Définie dans **Domain** (`UserValidator`, statique pure). 2. Une seule implémentation (pas de divergence). 3. Validation Domain pure (statique). 4-7. **Non** appliquée à Create/Update/ChangePassword. 8. Au seed : **oui** pour le secret bootstrap, **non** pour les hash figés. 9. **Oui**, un chemin (les 3 use cases/service) peut écrire un hash sans passer par la politique. 10. **Non**, un MDP vide/blanc ne remplace pas le hash : Create/ChangePassword rejettent le blanc (`HashPassword` lève, `ChangePassword` garde) ; Update **ignore** un MDP blanc (= « conserver »). 11. L'UI n'a **pas** de politique divergente (elle réutilise la même méthode Domain). 12. Messages stables. 13. Chaîne très longue : **oui**, coût BCrypt non borné (pas de max length) → risque DoS mineur. 14. Vérifie le **clair** (avant hachage). 15. **Aucune migration nécessaire** pour appliquer la politique (validation applicative pure).

---

## 9. Hachage et vérification

`AuthenticationService` + `BCrypt.Net-Next` :

- algorithme : **BCrypt** ; work factor **11** (`BcryptWorkFactor`) ;
- sel : généré par la bibliothèque (RNG crypto), **unique par hash**, encodé dans la chaîne ;
- coût stocké : oui (`$2a$11$…`) ; longueur/format : chaîne BCrypt standard ;
- comparaison : `BCrypt.Verify` (comparaison interne constante côté lib) ; `SaltParseException` → `false` ;
- migration de hash / mise à niveau au login : **absente** ;
- compatibilité historique : hash faibles connus catalogués (`KnownDefaultAdminPasswordHashes`).

Réponses : 1. Jamais de clair persisté. 2. Sel unique par utilisateur (par hash). 3. RNG crypto (lib). 4. Déterministe pour un même sel donné (propriété BCrypt). 5. Résistance temporelle : selon l'API BCrypt (comparaison interne) — non re-vérifiée manuellement. 6. Format évolutif (préfixe/coût). 7. Coût stocké. 8. **Pas** de mise à niveau auto au login. 9. Anciens comptes = même format `$2a$11$`. 10. Seed/bootstrap passent par BCrypt WF11 (identique). 11. Logs : `LoginViewModel` peut afficher `ex.Message` (technique) mais **jamais** le MDP ; `Debug.WriteLine` du seed est non sensible. 12. Le clair vit uniquement dans la `Command`/VM (courte durée, VM effacée après submit — `UserFormViewModel` l.259-276). 13. Aucun résultat/DTO ne retourne hash ni sel (voir §21). 14. Les tests vérifient les propriétés (hash ≠ clair, verify true/false) **sans** supposer la sécurité de l'algorithme.

---

## 10. Création d'utilisateur (`CreateUserUseCase`)

1. Command contient le **clair** (`Password`). 2. Champs : Username, First/Last, Role, IsActive, Password. 3. Trim sur Username/First/Last. 4. `IsActive` **fourni par l'appelant** (pas imposé). 5. Rôle **fourni par l'appelant**. 6. **N'importe quel appelant** peut créer un `Admin` (pas d'acteur/autorisation en Application). 7. Politique MDP **non** appelée avant hachage. 8. Unicité vérifiée avant écriture (`GetByUsername`). 9. Contrainte unique en base : oui (`idx_users_username_unique`, sensible casse). 10. Course entre deux créations : **non** correctement traduite (TOCTOU ; la 2ᵉ lève `DbUpdateException` brute). 11. Exception provider **peut remonter** non traduite. 12. Résultat : partiellement P3-1 (`UsernameTaken` structuré) mais **pas** de résultat pour MDP faible (jamais rejeté) ni pour course DB. 13. Hash créé une seule fois. 14. MDP invalide → aucune garde ⇒ **entité persistée** avec MDP faible. 15. Rôles inconnus : la Command est typée `UserRole` (un cast hors-plage resterait théoriquement possible, non validé).

---

## 11. Modification d'utilisateur (`UpdateUserUseCase`)

1. Tous les champs profil (Username, First/Last, Role, IsActive) écrasés **inconditionnellement**. 2. Hash existant **préservé** si `Password` blanc (garde `IsNullOrWhiteSpace`). 3. MDP vide ⇒ **« conserver »**. 4. Politique **non** appliquée quand MDP fourni. 5. Rôle changeable **librement**. 6. Un utilisateur **peut se promouvoir Admin** (pas d'acteur). 7. **Aucune** protection « dernier administrateur ». 8. Un utilisateur **peut se désactiver** (pas d'acteur ni garde). 9. **Pas** de séparation profil/sécurité (un seul use case fourre-tout). 10. **Lost update possible** (pas de token). 11. Course unicité **non** traduite en message métier. 12. Introuvable distingué (`UserFound=false`). 13. Résultats partiellement P3-1 (`UserFound`/`UsernameTaken`). 14. Entité **trackée mutée avant** l'unique `SaveChanges` — pas de validation intermédiaire.

> Règle « dernier administrateur » : **analysée comme option**, non prouvée par une exigence métier explicite. Aucune décision d'implémentation ici.

---

## 12. Authentification locale (`AuthenticateAsync` + `LoginViewModel`)

1. **Ne distingue pas** inconnu / mauvais MDP (les deux → `null`). 2. Message UI générique (« … incorrect, ou compte désactivé ») ⇒ ne révèle pas l'existence. 3. Compte inactif **ne peut pas** se connecter (garde `!IsActive → null`). 4. Vérif d'activité **avant** le hash (l.37 avant l.40) — écart de temps mesurable en théorie, mais message uniforme. 5. Lecture `GetByUsername` = **AsNoTracking** ⇒ entité fraîche, non périmée. 6. Désactivation sur un autre poste : **vue à la prochaine connexion** (lecture fraîche). 7. Session déjà ouverte **reste valide** après désactivation distante (session mémoire, pas de re-contrôle). 8. Expiration : **timeout d'inactivité 30 min** (UI, `SessionService`). 9. « Se souvenir de moi » : **absent**. 10. MDP **non** journalisé. 11. `LoginViewModel` peut exposer `ex.Message` (technique) — pas le MDP. 12. **Aucun** délai/verrouillage après échecs. 13. Protection anti-énumération : **présente** (résultat/message uniformes), non renforcée par throttling. 14. Login met à jour `LastLogin` (écriture séparée). 15. Écriture de `LastLogin` : `SaveChanges` simple, **sans** transaction (mono-écriture best-effort).

> Aucun système de session n'est créé/analysé pour modification ici.

---

## 13. Changement et réinitialisation du mot de passe

| Chemin | MDP actuel requis | Politique appliquée | Hash régénéré | Transaction | Audit |
|---|---:|---:|---:|---:|---:|
| `AuthenticationService.ChangePasswordAsync` (UI profil) | **oui** | **non** (service) / oui (UI amont) | oui (nouveau sel) | mono-écriture | non |

Réponses : 1. Use case de changement : **oui** (dans le service Infra). 2. Exige le MDP actuel : oui. 3. Réinitialisation admin d'un autre compte : **inexistante** (pas de `ResetPassword`). Un admin passerait par `UpdateUserUseCase` (qui **n'applique pas** la politique et **n'exige pas** l'ancien MDP). 4. Nouveau MDP validé : **seulement par l'UI**, pas par le service. 5. Réutilisation du même MDP : autorisée. 6. Sel renouvelé : oui. 7. Erreur → `SaveChanges` unique non atteint (garde avant écriture). 8. Course de deux changements → **lost update** possible. 9. Session existante **non** invalidée. 10. MDP temporaire : néant. 11. Le seed **contourne** ces chemins (écrit directement le hash) — mais toujours via BCrypt et sous contrôle d'environnement.

> **`ResetPassword` (admin) et politique côté service : absents.**

---

## 14. Activation et désactivation

1. État **absolu** (valeur cible), non basculé. 2. Deuxième appel identique **idempotent**. 3. Introuvable distingué. 4. Mise à jour **non conditionnelle** (pas de CAS). 5. Deux postes peuvent modifier simultanément → **lost update**. 6. Compte désactivé **exclu** de l'auth. 7. Listes de gestion (`GetAllAsync`) montrent les inactifs. 8. Sélecteurs (`GetActiveUsers`, `GetByRole`) **excluent** les inactifs. 9. Réactivation autorisée. 10. Désactivation **ne touche pas** l'historique. 11. **Dernier administrateur désactivable** (aucune garde). 12. **Auto-désactivation possible**. 13. `UpdateUserUseCase` écrit **aussi** `IsActive` ⇒ chemin concurrent. 14. **Deux chemins** vers `IsActive` (SetUserActive + UpdateUser), non coordonnés.

---

## 15. Suppression physique et historique

**Aucun `DeleteUserUseCase`** et **aucun `Users.Remove`/`ExecuteDelete`** n'existe (recherche exhaustive : néant). La suppression physique n'est **pas exposée** (Application/UI).

| Table/entité | Colonne utilisateur | Nullable | DeleteBehavior | Historique concerné |
|---|---|---:|---|---|
| `StockMovement` | `PerformedByUserId` | oui | `SetNull` | Auteur du mouvement de stock |
| `Sale` | `StaffId` | oui | `SetNull` | Vendeur de la vente |

Réponses : 1. Suppression physique : **non exposée**. 2-3. Si un `User` était supprimé (SQL brut/EF direct), les FK `SetNull` **anonymiseraient** l'historique (auteur → `null`) sans le détruire. 4. Pas d'identifiants utilisateur stockés hors FK. 5. Mouvements de stock : connaissent leur auteur (`PerformedByUserId`, nullable). 6. Fiches atelier : **ne connaissent pas** l'auteur QC (report roadmap). 7. Ventes : connaissent le vendeur (`StaffId`, nullable). 8. Commandes : liées aux ventes (pas de FK User directe). 9. Suppression peut **anonymiser** (SetNull) mais pas laisser d'orphelin FK. 10. **La désactivation suffit** à préserver l'historique. 11. Un utilisateur sans historique pourrait être supprimé sans risque FK — mais aucun chemin ne le fait. 12. **Roadmap : désactivation, pas suppression** (§P3-10 l.556). 13. Comportement minimal prouvé : désactivation. 14. Migration nécessaire pour *bloquer* la suppression (passer FK en `Restrict`) — **non requis** puisque la suppression n'est pas exposée.

---

## 16. Rôles et privilèges

1. Rôles réels : `Admin`, `Optician`, `Technician`. 2. Rôle validé : `UserValidator.RuleFor(Role).IsInEnum()` **mais** ce validateur **n'est jamais invoqué** par les use cases. 3. Valeur numérique inconnue : Command typée `UserRole` (cast hors-plage théoriquement persistable, stocké en chaîne). 4. **Rôle par défaut = `Admin`** (première valeur de l'enum = 0) si l'appelant omet `Role`. 5. Qui peut créer un Admin : **n'importe qui** (aucun acteur en Application). 6. Qui peut changer le rôle : **n'importe quel appelant** de `UpdateUserUseCase`. 7. Vérification d'autorisation en Application : **aucune**. 8. L'UI **seule** masque les actions (`SessionService.IsAdmin`, menus). 9. Un appel direct de use case **contourne** l'UI. 10. Utilisateur courant injecté en Application : **non** (aucun `ICurrentUser`/acteur). 11. Use cases sensibles **ignorent** l'acteur. 12. **Élévation de privilèges possible** par construction directe de commande. 13. Notion de permission : **inexistante** — rôle décoratif consommé par l'UI. 14. Durcissement P3-10 réaliste : validation du rôle (`IsInEnum`) côté Application + rôle par défaut non-Admin ; un système d'autorisation par acteur est **hors périmètre** (report).

---

## 17. Seed administrateur et environnements

| Option | Production sûre | Première ouverture | Tests/démo | Migration | Risque |
|---|---:|---:|---:|---:|---|
| Conserver le seed partout | ❌ (admin/admin) | ✅ | ✅ | non | 🔴 compte faible en prod |
| Seed démo uniquement test/démo | ✅ | ⚠️ (aucun admin sans secret) | ✅ | non | 🟠 app inutilisable sans onboarding |
| Seed conditionné par variable d'env | ✅ | ⚠️ | ✅ | non | 🟡 dépend de la config |
| Création initiale interactive (UI) | ✅ | ✅ | n/a | non | UI hors P3-10 |
| Échec sûr si aucun admin | ✅ | ❌ (blocage) | ⚠️ | non | 🟠 DoS accidentel |
| MDP généré à usage unique | ✅ | ✅ | ⚠️ | non | 🟡 exposition du MDP |
| Compte désactivé par défaut | ✅ | ⚠️ | ✅ | non | 🟠 nécessite réactivation |

**État réel (déjà implémenté par P2A-1F).** 1. Compte admin auto : **oui** via migration/démo, mais **neutralisé/sécurisé** en prod. 2. Login : `admin`. 3. MDP initial : `admin` (démo/migration). 4. Codé en dur : oui (hash figés) **mais catalogués comme faibles**. 5. Hash identique sur toutes les installs (démo). 6. Seed prod (bootstrap) passe par BCrypt WF11. 7. Compte démo actif ; en prod le faible est **désactivé** faute de secret. 8. Seed s'exécute au démarrage via `DatabaseSeeder.Seed`. 9-11. Environnement détecté par `MMV_ENVIRONMENT` (défaut sûr = **Production**) ; **distingue** dev/demo/test/prod. 12. Base vide de **prod** : **aucun** compte démo. 13. Base existante : idempotent (ne réécrase pas un admin réel). 14. Idempotent. 15. Install sans admin **et** sans secret → **aucun login possible** (fail-safe, mais bloquant — pas d'onboarding). 16. Onboarding/premier compte explicite : **inexistant**. 17. Changement de MDP à la première connexion : **inexistant**. 18. Secret externe : **oui** (`MMV_BOOTSTRAP_ADMIN_PASSWORD`, validé contre la politique). 19. Seed requis par les tests : oui (tests d'intégration seed/lifecycle). 20. Supprimer le seed casserait la **démo** ; la migration `InsertData(admin)` reste, mais neutralisée en prod.

> **Le seed prod est déjà sûr.** Reste ouvert : le cas « base auparavant démo-seedée puis passée en prod » où `SecureBootstrap` ne neutralise **qu'un seul** compte faible (voir §19 & §24 🟠), et l'absence d'onboarding (§24 🟠).

---

## 18. Normalisation et unicité du login

1. `admin`, `Admin`, ` ADMIN ` : **non** identiques (Trim retire les espaces, mais la casse reste ⇒ 3 valeurs distinctes après trim : `admin`/`Admin`/`ADMIN`). 2. Code et base utilisent la **même** règle (comparaison binaire sensible casse). 3. **Aucune** valeur normalisée dédiée (pas de `NormalizedUsername`). 4. Contrainte unique présente (index). 5. Deux postes peuvent créer le même login **à casse différente** (`admin`/`Admin`) → deux comptes. 6. Course même-casse → **exception technique** (`DbUpdateException`), non métier. 7. Doublons historiques de casse : possibles. 8. Migration pour normaliser : nécessaire **seulement si** on ajoute une colonne normalisée + index (option). 9. Backfill : pourrait échouer sur collisions de casse existantes. 10. Modèle `NormalizedReference` (P3-4B) : **pertinent mais peut être excessif** ici — une normalisation `Trim().ToLowerInvariant()` + comparaison applicative + index unique suffit au besoin minimal. 11. Provider futur (PostgreSQL/SQL Server) comparerait **différemment** (collation par défaut souvent insensible casse) → risque d'incohérence. 12. Garantie minimale d'auth non ambiguë : **normaliser le login** (au moins casse) et faire l'unicité applicative sur la valeur normalisée.

> Aucune migration créée. Décision de périmètre en §25.

---

## 19. Concurrence multi-poste

| Acte | Transaction | CAS/condition | Contrainte DB | Risque résiduel |
|---|---|---|---|---|
| Double création même login (même casse) | mono-écriture | pré-check `GetByUsername` | index unique (BINARY) | **TOCTOU** → 2ᵉ lève exception **non traduite** |
| Double création même login (casse ≠) | mono-écriture | pré-check | index ne matche pas | **Login ambigu** (2 comptes) |
| Création vs seed bootstrap | mono | garde `Any(Admin)` | index unique | faible (démarrage mono-poste) |
| Double modification (Update) | mono | aucun | aucune | **Lost update** |
| Changement MDP concurrent | mono | aucun | aucune | **Lost update** (hash écrasé) |
| Désactivation vs connexion | mono / lecture fraîche | lecture AsNoTracking | — | login voit l'état frais (OK) ; session ouverte non affectée |
| Désactivation vs réactivation | mono | aucun (valeur absolue) | — | **Lost update** / réactivation accidentelle |
| Suppression vs référence historique | — | — | FK `SetNull` | non exposé (pas de delete) |
| Changement de rôle concurrent | mono | aucun | — | **Lost update** |

Identifiés : **lost update** (Update/SetActive/ChangePassword) ; **login ambigu** (casse) ; **TOCTOU** (unicité) ; **réactivation accidentelle** ; hash écrasé ; **exception technique exposée** ; double seed (idempotent, sûr).

Séparation :

- **Protection déjà acquise** : lecture d'auth fraîche (AsNoTracking), seed idempotent, index unique même-casse.
- **Défaut prouvé** : absence de token de concurrence ⇒ lost update ; TOCTOU unicité non traduit ; ambiguïté de casse.
- **Limite SQLite** : `HasMaxLength` non contraignant ; pas de collation insensible par défaut.
- **Besoin futur PostgreSQL/SQL Server** : normalisation explicite pour éviter les divergences de collation ; token de concurrence natif.

---

## 20. Persistance et migration éventuelle

1. Contrainte unique login : **oui** (`idx_users_username_unique`). 2. `IsActive` `NOT NULL` défaut `true` : oui. 3. Rôle contraint : stocké `TEXT` **sans** `CHECK` ; pas de contrainte de domaine en base. 4. Longueurs imposées sur SQLite : **non** (SQLite ignore `HasMaxLength`). 5. Hash `NOT NULL` : oui ; sel : n/a (dans le hash). 6. Migration nécessaire pour les règles candidates : **non** pour la politique MDP, la validation de rôle et la désactivation (applicatif pur) ; **oui uniquement si** on ajoute une colonne `NormalizedUsername`, un `CHECK` rôle, un token de concurrence, ou un passage FK `Restrict` — toutes **optionnelles**. 7. Données historiques invalides : possibles (doublons de casse, MDP faibles pré-existants). 8. Normalisation login → **backfill** requis. 9. Index unique sur normalisé pourrait **échouer** sur collisions. 10. Champ de version de hash : non nécessaire (coût dans la chaîne). 11. Protéger la suppression → modifier FK (non requis). 12. `SqliteSchemaVerifier` : **ne vérifie pas** l'index/les colonnes User (aucune mention) → extension possible (dette). 13. États partiels à refuser : Username hors regex/longueur, Role hors enum, MDP faible — à garantir **côté Application**. 14. **Solution sans migration : possible** pour le cœur P3-10 (politique + validation rôle + normalisation applicative + gardes désactivation).

---

## 21. Lecture et exposition de données sensibles

1. `PasswordHash` **non** projeté dans `UserListItemDto`. 2. Sel non exposé (n'existe pas). 3. Commandes non journalisées avec le MDP. 4. Exceptions : `LoginViewModel` affiche `ex.Message` (technique), **pas** le MDP. 5. `User.ToString()` non surchargé → pas de fuite. 6. `UserFormViewModel` **efface** `Password`/`ConfirmPassword` après submit (l.259-276). 7. Pas de propriété longue durée portant le clair. 8. Listes : `GetAllAsync` charge l'entité complète (dont hash) **mais** la projection DTO l'écarte avant la frontière UI. 9. Lectures **AsNoTracking** (`GetAllAsync`, `GetQueryable`). 10. Un écran ne récupère l'entité `User` complète que via `SessionService.CurrentUser` (login) — hash présent en mémoire de session (non affiché). 11. Tests : pas de secret réel en sortie (mots de passe de test factices). 12. Le MDP de démo `admin` apparaît dans `DbInitializer`/commentaires (documenté, neutralisé en prod) ; le secret bootstrap **jamais** dans le `SeedResult`/logs.

---

## 22. Tests existants (backend)

| Fichier | Niveau | Nb tests | Provider réel | Sujet |
|---|---|---:|---:|---|
| `ValidatorTests/UserValidatorTests.cs` | Domain | 21 | non | Champs + **politique MDP** (chaque branche) + strength + génération |
| `RepositoryTests/UserRepositoryTests.cs` | Domain | 3 | SQLite | Persist, filtre actifs, filtre rôle |
| `ServiceTests/AuthenticationServiceTests.cs` | Domain | 15 | SQLite/InMemory | Hash, verify, **auth (inconnu/mauvais MDP/inactif)**, change password |
| `Configuration/DatabaseSeederTests.cs` | Domain | 10 | SQLite | Seed prod/dev/test, neutralisation, bootstrap, idempotence |
| `Configuration/SeedOptionsResolverTests.cs` | Domain | 7 | non | Résolution env + validation secret |
| `UseCases/Users/UserUseCasesTests.cs` | Application | 8 | mocks | Create/Update/SetActive (hash, unicité, introuvable) |
| `UseCases/Users/ListUsersUseCaseTests.cs` | Application | 4 | mocks | Projection DTO |
| `ViewModels/LoginViewModelTests.cs` | **App (UI)** | 9 | — | *(non compté comme protection backend)* |
| `ViewModels/SessionServiceTests.cs` | **App (UI)** | 10 | — | *(non compté comme protection backend)* |

Couverture réelle : politique MDP (**Domain seul**), hash/sel (via BCrypt distinct par appel), auth (inconnu/mauvais/inactif), création, unicité (filtre), modification, change password, désactivation (toggle), seed (prod/dev/test/idempotent/neutralisation). **Non couvert** : voir §23.

---

## 23. Trous de couverture

| Règle ou risque | Test existant | Suffisant ? | Test manquant |
|---|---|---:|---|
| Login vide / espaces seuls | validator | oui | — |
| Login trop long (>50) | validator (max) | oui | (SQLite ne l'impose pas — cf. Application) |
| Doublon exact (même casse) | use case pré-check ; repo | partiel | **race DB → message métier** |
| Doublon de **casse** (`admin`/`Admin`) | — | **non** | **création casse ≠ doit être refusée** |
| Politique MDP, chaque branche | validator | oui (Domain) | **application dans Create/Update/ChangePassword** |
| MDP extrêmement long | — | non | borne max / coût |
| MDP absent (create) | `HashPassword_Empty_Throws` | partiel | comportement Create sur MDP blanc |
| Hash ≠ pour même MDP | *(implicite BCrypt)* | partiel | **test explicite 2 users même MDP → hash ≠** |
| Verify valide/invalide | auth tests | oui | — |
| Aucun clair persisté | — | non | **assert `PasswordHash` ≠ clair en base** |
| Création utilisateur | use case | oui | — |
| Modif sans changement de MDP | `Update_…RehashesOnlyWhenPasswordProvided` | oui | — |
| Modif avec MDP invalide | — | **non** | **doit être refusée (Application)** |
| Changement de MDP | auth tests | oui | — |
| Utilisateur introuvable | use cases + auth | oui | — |
| Compte inactif refusé au login | `AuthenticateAsync_InactiveUser` | oui | — |
| Désactivation idempotente | `SetActive_TogglesState` | partiel | **idempotence explicite (2 appels identiques)** |
| Réactivation | — | non | test dédié |
| Double création concurrente | — | **non** | test de course |
| Désactivation concurrente vs connexion | — | non | test |
| Suppression avec historique | n/a (pas de delete) | — | (documenter l'absence) |
| Seed base neuve / existante / prod | seeder tests | oui | — |
| Absence de compte démo en prod | `Production_…NoDemoData` | oui | — |
| Rôles invalides | validator (IsInEnum) | partiel | **validation rôle dans use cases** |
| Élévation de privilège | — | **non** | (dépend d'une décision acteur — cf. §25) |
| Migration éventuelle | lifecycle tests | oui (si aucune) | — |

---

## 24. Risques classés

### 🔴 Critique (prouvés)

- **R1 — Politique de mot de passe contournable.** Preuve : `ValidatePasswordPolicy` n'est appelée que dans l'UI et le resolver ; **absente** de `CreateUserUseCase` (l.54), `UpdateUserUseCase` (l.60), `AuthenticationService.ChangePasswordAsync` (l.108). Chemin : appel Application direct. Scénario : `Password="a"` → hash BCrypt d'un MDP faible persisté. Impact : comptes faibles. Couche : **Application/Infrastructure**. Étape : **P3-10**.

*(Aucun autre risque atteignant le seuil 🔴 n'est prouvé : pas de clair persisté/exposé ; compte inactif non authentifiable ; pas d'admin démo actif en prod ; MDP admin prod non figé — neutralisé/sécurisé ; pas de suppression destructive ; login non doublonné en **même** casse.)*

### 🟠 Important

- **R2 — Login ambigu par la casse.** `admin` ≠ `Admin` en Application **et** en base (BINARY). Chemin : Create/Update. Impact : deux comptes « équivalents », auth ambiguë, divergence future avec un provider serveur. Couche : Application (+ base optionnelle). Étape : **P3-10**.
- **R3 — Rôle non validé + défaut `Admin`.** `UserValidator.RuleFor(Role)` jamais invoqué ; `CreateUserCommand.Role` par défaut = `Admin` (0). Impact : rôle inattendu/privilégié par omission. Couche : Application. Étape : **P3-10**.
- **R4 — Neutralisation partielle du compte faible.** `SecureBootstrap` neutralise **un seul** compte via `FirstOrDefault(known hash)` ; une base autrefois démo-seedée puis passée en prod garde `marie.optic`/`pierre.tech`/`sophie.optic` (hash « admin ») **actifs**. Chemin : `DatabaseSeeder.SecureBootstrap`. Impact : identifiants faibles résiduels. Couche : Infrastructure. Étape : **P3-10 (option)**.
- **R5 — Lost update / pas de token de concurrence.** Update/SetActive/ChangePassword sans CAS ni `RowVersion`. Impact : écrasement silencieux (dont hash). Couche : Domain/Infra. Étape : **P3-10 partiel / report**.
- **R6 — TOCTOU d'unicité non traduit.** Course même-casse → `DbUpdateException` brute au lieu d'un message métier P3-1. Couche : Application/Infra. Étape : **P3-10**.
- **R7 — Politique appliquée seulement par l'UI (généralise R1).** Étape : **P3-10**.
- **R8 — Session non invalidée après désactivation/-changement de rôle/-MDP.** Session mémoire, timeout 30 min seul. Couche : UI. Étape : **report**.
- **R9 — Aucune protection « dernier admin » / auto-désactivation.** Option métier. Étape : **P3-10 (à décider) / report**.
- **R10 — Absence d'onboarding.** Prod sans secret bootstrap ⇒ aucun login. Couche : App/UI. Étape : **report** (UI).

### 🟡 Dette

Timestamps d'audit, `LastLogin` fiabilisé, historique des MDP, expiration/rotation, MFA, complexité MDP avancée (spécial/commun/inclusion login), audit utilisateur, récupération de MDP, pagination, localisation des messages, extension de `SqliteSchemaVerifier` aux tables User, borne max de longueur de MDP.

### 🔵 Report explicite

SSO, OAuth/OIDC, rôles fins & permissions, MFA, récupération par email, politique de rotation, audit trail complet, chiffrement de base, gestion centralisée d'identité, multi-magasin, SaaS, provider serveur.

Chaque 🔴/🟠 ci-dessus porte preuve, chemin, scénario, impact, couche et étape cible.

---

## 25. Plan candidat P3-10

**Objectif : le plus petit périmètre backend sûr, sans migration si possible.**

### Obligatoire P3-10

1. **Politique de mot de passe appliquée partout (R1/R7).** Faire passer Create/Update (si MDP fourni)/ChangePassword par `ValidatePasswordPolicy` **avant hachage**, avec refus structuré P3-1 (résultat `WeakPassword` ou exception typée). Aucune écriture si invalide.
   - Chemins : `CreateUserUseCase`, `UpdateUserUseCase`, `AuthenticationService.ChangePasswordAsync` (ou déplacement de la garde en Application).
2. **Normalisation & unicité de login non ambiguë (R2/R6).** Normaliser le login (`Trim().ToLowerInvariant()`) pour la **comparaison d'unicité** en Application ; refuser une création/renommage entrant en collision insensible à la casse ; traduire la course DB en message métier stable (P3-1). Décision : **normalisation applicative** (pas obligatoirement une colonne `NormalizedUsername`) ⇒ **sans migration**. Colonne + index normalisé = **option** documentée si l'on veut la garantie en base.
3. **Validation du rôle (R3).** Invoquer `IsInEnum` (ou garde équivalente) dans Create/Update ; **rôle par défaut non-`Admin`** (exiger un rôle explicite ou défaut `Optician`/`Technician`).
4. **Activation/désactivation robuste (R9 minimal).** Garder l'idempotence ; **analyser** (sans imposer si non prouvé métier) une garde « ne pas désactiver/rétrograder le dernier Admin actif » — à confirmer par le porteur métier.
5. **Suppression : rester en désactivation.** Ne pas ajouter de `DeleteUserUseCase` ; documenter que l'historique est préservé par la désactivation (FK `SetNull` conservées).
6. **Seed (R4).** Neutraliser **tous** les comptes portant un hash faible connu (pas seulement le premier) dans `SecureBootstrap`.
7. **Hachage/authentification : inchangés** (déjà sains ; BCrypt WF11, anti-énumération présente).
8. **Concurrence (R5/R6).** Traduire la course d'unicité ; token de concurrence = **option/report** (documenter la limite SQLite).
9. **Tests** (voir §23) : application de la politique sur les 3 chemins ; refus casse-différente ; rôle invalide refusé ; deux users même MDP → hash ≠ ; aucun clair persisté ; réactivation ; idempotence explicite ; neutralisation multi-comptes faibles.
10. **Documentation** : rapport d'implémentation P3-10 (à la livraison), mise à jour de la roadmap si nécessaire.

### Reportable (ne pas implémenter en P3-10)

UI/onboarding, système d'autorisation par acteur (`ICurrentUser`), invalidation de session, MFA, récupération email, permissions fines, audit trail complet, rotation, verrouillage/throttling, token de concurrence natif, provider serveur.

---

## 26. Reports explicites

- Auteur du QC sur fiches atelier → **P3-10+ / non couvert** (pas de colonne aujourd'hui).
- `PerformedByUserId`/`StaffId` restent nullables `SetNull` — **conservés** (report roadmap l.283).
- Onboarding / premier compte interactif → **UI (report)**.
- Autorisation par acteur & permissions fines → **report**.
- SSO/OAuth/OIDC, MFA, récupération email, rotation, audit trail, chiffrement, multi-magasin/SaaS, provider serveur → **report**.

---

## 27. Verdict

**P3-10 AUDIT = GO**

Justification :

- ✅ Toutes les écritures `User` cartographiées (§7).
- ✅ Politique MDP comprise ; chaque chemin évalué (§8) — gap R1 prouvé.
- ✅ Algorithme de hachage et stockage cartographiés (§9).
- ✅ Authentification et `IsActive` prouvés (§12).
- ✅ Activation/désactivation et (absence de) suppression analysées (§14, §15).
- ✅ Références historiques cartographiées (§5, §15).
- ✅ Rôles et élévation analysés (§16).
- ✅ Seed administrateur analysé par environnement (§17).
- ✅ Normalisation/unicité décidées (§18, §25).
- ✅ Concurrence multi-poste traitée (§19).
- ✅ Migration : **décidée non nécessaire** pour le cœur P3-10 (option documentée) (§20, §25).
- ✅ Tests manquants listés (§23).
- ✅ Plan candidat suffisamment précis (§25).
- ✅ Rapport `.md` créé.
- ✅ **Aucun code, test, migration ou UI modifié.**

Aucun commit. Aucun push. Aucune UI. **STOP.**

---

## Décisions retenues pour l'implémentation

> Ajouté lors de l'implémentation P3-10 (2026-07-22). Rapport d'implémentation :
> `docs/implementation/P3-10-local-users-business-rules-implementation-report.md`.

| Sujet | Décision retenue | Écart avec le plan candidat §25 |
|---|---|---|
| **Politique de mot de passe** | Appliquée aux **trois** chemins runtime (`CreateUserUseCase`, `UpdateUserUseCase` si mot de passe fourni, `AuthenticationService.ChangePasswordAsync`), **avant hachage**. `UserValidator.ValidatePasswordPolicy` reste l'unique source de vérité. | Conforme |
| **Normalisation du login** | Colonne **persistée** `User.NormalizedUsername` (`Trim().ToLowerInvariant()`), propriétaire Domain unique `UserIdentityPolicy`. | **Renforcé** : §25 envisageait une normalisation applicative sans migration, la colonne étant « option ». Retenue **obligatoire** — voir ci-dessous. |
| **Migration** | **Requise** pour la garantie multi-poste. Une seule migration additive `AddNormalizedUsernameAndSecureLocalUsers`. | **Écart assumé** vs §25 (« sans migration ») |
| **Backfill** | **Exact ou échec sûr** (principe P3-4B) : `lower(trim(Username))` n'est appliqué que si chaque login hérité appartient au jeu ASCII autorisé par `UserValidator`. Collision insensible à la casse, login vide, hors bornes ou hors jeu ⇒ migration **avortée**, transaction annulée, rien inscrit dans `__EFMigrationsHistory`, aucune ligne supprimée, fusionnée ni renommée. | Nouveau (conséquence de la migration) |
| **Index unique** | `idx_users_normalized_username_unique` sur `NormalizedUsername`. L'ancien `idx_users_username_unique` (BINARY) est **supprimé**. | Conforme (option §25 retenue) |
| **Rôle** | `Role` devient `UserRole?` sur les deux commandes ; `null` ⇒ erreur de validation. `IsInEnum` réellement invoqué. **Aucune omission ne crée `Admin`.** | Conforme |
| **Autorisation par acteur** | **Reportée** : aucun `ICurrentUser`. P3-10 valide la *valeur* du rôle, pas le *droit* de la demander. | Conforme |
| **Cycle de vie** | Désactivation conservée ; aucun `DeleteUserUseCase`, aucune suppression physique, FK historiques laissées en `SetNull`. | Conforme |
| **Comptes faibles (R4)** | **Tous** les comptes portant un hash faible connu sont neutralisés en production, plus seulement le premier. | Conforme |
| **BCrypt / authentification** | Inchangés (WF11, anti-énumération, refus des comptes inactifs). Seule la **recherche** passe par la clé normalisée. | Conforme |
| **Dernier administrateur** | **Non implémenté** — aucune exigence métier ne l'a prouvé (§11, §25.4). Report explicite. | Conforme |
| **Token de concurrence** | **Non implémenté** : le *lost update* (R5) reste possible. Report explicite. | Conforme |
| **UI** | Aucune modification. Onboarding et invalidation de session restent reportés. | Conforme |

### Pourquoi la colonne persistée plutôt que la normalisation applicative seule

Le plan candidat §25 jugeait la colonne « optionnelle », une comparaison applicative sur `Trim().ToLowerInvariant()`
paraissant suffire. L'implémentation a retenu la colonne pour trois raisons que l'audit avait lui-même établies :

1. **Une garde applicative ne garantit rien entre postes** (§19). Sans index unique sur la valeur normalisée, deux
   postes créant `admin` et `Admin` quasi simultanément passent tous deux leur pré-contrôle : la base accepte les
   deux comptes. L'ambiguïté d'authentification (R2) subsisterait exactement là où elle fait le plus de dégâts —
   en multi-poste, cas d'usage central du produit (ADR-PROD-DB-001).
2. **Sans colonne, l'unicité insensible à la casse ne peut pas être indexée.** Il faudrait filtrer sur
   `lower(trim(Username))`, expression non-SARGable : l'index devient inutilisable, chaque authentification
   parcourt la table, et le résultat dépend de la collation du provider — donc changerait au passage à
   PostgreSQL/SQL Server (§18.11).
3. **Le coût réel de la migration est le backfill, et il est de toute façon dû.** Les données héritées douteuses
   (doublons de casse, logins hors jeu) existent indépendamment du choix ; les découvrir à froid en production
   serait pire que les refuser explicitement à la migration.

Le prix de cette décision est assumé et documenté : une base contenant deux comptes convergents **ne migre pas**
tant que l'exploitant n'a pas tranché. C'est délibéré — fusionner détruirait un historique (ventes, mouvements de
stock), renommer inventerait un identifiant que personne n'a choisi.

---

## Suite de l'audit — revue ciblée avant commit (2026-07-22)

> Cette section **complète** l'audit sans en réécrire l'historique : les constats des §1-§27 restent tels qu'ils
> ont été établis. Détail complet et preuves :
> [rapport d'implémentation §« Revue ciblée avant commit »](P3-10-local-users-business-rules-implementation-report.md#revue-ciblée-avant-commit).

**Écart assumé conservé.** La décision de créer une migration (colonne persistée `NormalizedUsername` + index
unique), là où le plan candidat §25 la jugeait optionnelle, **reste l'écart assumé** de cette étape — motivé par
la garantie multi-poste (§19, ADR-PROD-DB-001) et documenté ci-dessus.

**Trois défauts concrets trouvés et corrigés par la revue ciblée**, tous invisibles à l'audit statique car
n'apparaissant qu'à l'inspection **physique** du schéma ou à l'exécution :

1. **Défaut permanent `''` sur `NormalizedUsername`.** Le défaut vide, censé être un artifice transitoire exigé
   par `ALTER TABLE ADD COLUMN NOT NULL`, **subsistait dans le schéma final**. Une insertion SQL omettant la
   colonne était acceptée en silence avec une clé métier vide au lieu d'échouer `NOT NULL`. Corrigé dans la
   migration P3-10 **existante** (`AlterColumn` final supprimant le défaut) ; `dflt_value` final = `NULL`,
   prouvé par PRAGMA **et** par insertion brute.
2. **Entité rejetée laissée suivie après un refus `UsernameTaken`.** Le rollback SQL était correct, mais le
   suivi EF en mémoire ne l'était pas : un `SaveChangesAsync` ultérieur **sans rapport**, dans la même portée,
   retentait l'écriture rejetée et échouait. Corrigé par un détachement **ciblé** (`DetachIfTracked`), sur le
   patron déjà retenu en P3-9 — sans `ChangeTracker.Clear()`, sans dépendance EF en Domain/Application, sans
   modifier `EfTransactionRunner`.
3. **Seed : renommage arbitraire vers le login bootstrap.** `SecureBootstrap` pouvait renommer un compte faible
   **quelconque** vers le login bootstrap ; si un administrateur **réel** l'occupait déjà, l'index unique P3-10
   faisait échouer le seed **au démarrage de l'application**. De plus, la branche « administrateur réel déjà
   présent » **ne neutralisait aucun** compte faible, les laissant actifs en production. Les deux points sont
   corrigés (R4 désormais couvert sur **toutes** les branches).

**Affirmation documentaire corrigée.** Le `Down` était décrit comme pouvant légitimement échouer à cause de
comptes ne différant que par la casse. C'est **faux** sous un schéma P3-10 intact : l'index unique interdit
précisément ces comptes. Un rollback normal est **réversible** pour toute base respectant le schéma P3-10 ;
seul un schéma manuellement altéré pourrait le faire échouer.

**Points revus sans défaut trouvé** : classe de caractères `GLOB` du backfill (tiret en position finale, aucune
plage accidentelle) ; collisions historiques et échec sûr ; matrice complète des états d'adoption partiels ou
altérés ; classification des contraintes (`UniqueConstraint` seule traitée comme doublon) ; politique de mot de
passe et validation des rôles.

**Total après revue** : **1413** tests (Domain 593 · Application 581 · App 239), 0 échec, 0 ignoré, 0
vulnérabilité, aucune migration en attente, **aucune UI modifiée**, **une seule** migration P3-10.
