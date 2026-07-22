# P3-10 — Implémentation backend des utilisateurs locaux et de la sécurité minimale

> **MODE = IMPLEMENT_TEST_MIGRATE_AND_WRITE_REPORT_NO_COMMIT.**
> Aucun commit, aucun push, aucune modification d'UI. Verdict en §36.

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
| EXPECTED_BASELINE_TESTS | 1308 | 1308 | ✅ |

---

## 2. État Git et CI de départ

```
git branch --show-current      → p3-business-rules
git rev-parse HEAD             → 057df0f3fad91f344a6333f3a52f32b6d1552ba1
git rev-parse origin/p3-...    → 057df0f3fad91f344a6333f3a52f32b6d1552ba1
git status --short             → design-handoff/, design/, docs/ui/,
                                  docs/implementation/P3-10-...-audit-report.md (tous non suivis)
git diff --check               → (rien)
```

- Branche = `p3-business-rules` ✅
- HEAD local = HEAD distant = `057df0f…` ✅
- Aucun fichier **suivi** modifié au départ ✅
- Seul nouveau fichier P3-10 présent : le rapport d'audit ✅
- Aucun fichier sous `src/MMV.App/**` ✅

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

## 3. Baseline

| Contrôle | Attendu | Observé | OK |
|---|---|---|---:|
| `dotnet build` | 0 erreur / 0 avertissement | 0 erreur / 0 avertissement | ✅ |
| Domain | 537 | 537 | ✅ |
| Application | 532 | 532 | ✅ |
| App | 239 | 239 | ✅ |
| **Total** | **1308** | **1308** | ✅ |
| Échecs | 0 | 0 | ✅ |
| Ignorés | 0 | 0 | ✅ |
| Vulnérabilités | aucune | aucune (7 projets) | ✅ |
| Migrations en attente | aucune | *No changes have been made to the model…* | ✅ |
| Références `MMV.Application` | `MMV.Domain` seul | `MMV.Domain` seul (+ pkg `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1) | ✅ |

**Gate baseline : PASS.** Implémentation démarrée après cette section.

---

## 4. Décisions finales

| # | Décision | Portée |
|---|---|---|
| D1 | La politique de mot de passe existante (`UserValidator.ValidatePasswordPolicy`) reste **l'unique source de vérité** et est appliquée **avant hachage** sur les trois chemins runtime : `CreateUserUseCase`, `UpdateUserUseCase` (si mot de passe fourni), `AuthenticationService.ChangePasswordAsync`. Aucune seconde politique, aucune regex parallèle. | Application + Infrastructure |
| D2 | Clé persistée `User.NormalizedUsername` (`Trim().ToLowerInvariant()`), propriétaire Domain unique `UserIdentityPolicy.NormalizeUsername`. `Username` reste la forme affichable. | Domain |
| D3 | Unicité garantie **en base** par `idx_users_normalized_username_unique` ; l'ancien `idx_users_username_unique` (BINARY, donc jamais une garantie métier) est **supprimé**. | Infrastructure |
| D4 | Migration unique et additive `AddNormalizedUsernameAndSecureLocalUsers`, backfill **exact ou échec sûr** (principe P3-4B). | Infrastructure |
| D5 | Course d'unicité traduite en erreur métier stable `UsernameTaken` (catégorie `UniqueConstraint` via `ITransactionRunner`) — jamais un message SQLite/EF brut. | Application |
| D6 | `Role` devient `UserRole?` sur les deux commandes ; `null` ⇒ erreur de validation. La **valeur** est arbitrée par `UserValidator.IsInEnum`, désormais réellement invoqué. | Application |
| D7 | **Aucune omission ne crée un `Admin`** : le défaut CLR de l'enum n'est plus jamais interprété. | Application |
| D8 | BCrypt WF11, anti-énumération et refus des comptes inactifs : **inchangés**. | Infrastructure |
| D9 | Tous les comptes portant un hash faible connu sont neutralisés en production (plus seulement le premier) — correction de R4. | Infrastructure |
| D10 | Désactivation conservée ; **aucun** `DeleteUserUseCase`, aucune suppression physique, aucune FK passée en `Restrict`. | — |
| D11 | Aucune UI modifiée. Autorisation par acteur (`ICurrentUser`), garde « dernier administrateur », token de concurrence et invalidation de session : **reports explicites**. | Report |

> **Limite assumée (D6/D7).** P3-10 valide la **valeur** du rôle et supprime l'élévation **par omission**. Il ne
> peut pas autoriser **l'acteur** qui demande le changement, car la couche Application ne possède aucun
> `ICurrentUser`. Un appelant direct peut donc toujours demander explicitement `Admin`. Cette limite reste
> explicitement reportée — les tests de §21 ne prouvent **pas** l'autorisation de l'acteur.

---

## 5. Modèle User

Ajout d'une seule propriété (`src/MMV.Domain/Entities/User.cs`) :

```csharp
public string NormalizedUsername { get; set; } = string.Empty;
```

Configuration EF (`UserConfiguration`) :

| Élément | Valeur |
|---|---|
| `Username` | `IsRequired()`, `HasMaxLength(50)` — **conservé**, index unique retiré |
| `NormalizedUsername` | `IsRequired()`, `HasMaxLength(50)` |
| Index | `idx_users_normalized_username_unique` (**unique**, colonne `NormalizedUsername`) |
| Index supprimé | `idx_users_username_unique` |

Aucun autre champ modifié : pas de `RowVersion`, pas de compteur d'échecs, pas de colonne de sel (le sel reste
encodé dans la chaîne BCrypt).

---

## 6. Normalisation

`src/MMV.Domain/Policies/UserIdentityPolicy.cs` — Domain pur, aucune dépendance EF/SQLite/Application/UI.

```csharp
public static string NormalizeUsername(string? username)
    => username is null ? string.Empty : username.Trim().ToLowerInvariant();
```

- `null` ⇒ chaîne vide, **pas** d'exception : une entrée manquante reste une **erreur de saisie**, refusée en
  aval par `UserValidator.NotEmpty` avec un message lisible (convention P3-1). Lever ici transformerait une
  saisie invalide en panne technique.
- `ToLowerInvariant` (jamais `ToLower`) : la culture de la machine ne doit pas influer sur l'identité d'un
  compte, et l'invariant coïncide exactement avec le `lower()` natif de SQLite sur le jeu ASCII — propriété
  décisive pour le backfill (§19).

**Appelants (tous, et eux seuls) :** `CreateUserUseCase`, `UpdateUserUseCase`, `UserRepository`
(`GetByNormalizedUsernameAsync`, `ExistsByNormalizedUsernameAsync`, donc `AuthenticationService` par
délégation), `DatabaseSeeder`, `DbInitializer`, tests. Aucune recopie de `Trim().ToLowerInvariant()` sur un
chemin runtime User (garde d'architecture, §33).

---

## 7. Validation du profil

`UserValidator` est **inchangé** (aucune règle ajoutée, retirée ni modifiée) mais désormais **réellement
invoqué** par Create et Update via `CommandValidation.Validate` sur un candidat **détaché** :

- `Username` requis, 3–50, sans espace, jeu `[a-zA-Z0-9._-]` ;
- `FirstName` / `LastName` requis, ≤ 100 ;
- `Role` `IsInEnum` — jusque-là jamais exécuté par les use cases.

Aucune règle n'a eu besoin de porter sur `NormalizedUsername` : la normalisation ne fait que retirer des espaces
périphériques et replier la casse, donc un `Username` valide produit toujours une forme normalisée non vide,
≤ 50 et dans le même jeu de caractères. `PasswordHash` n'est **jamais** utilisé comme mot de passe clair.

---

## 8. Politique de mot de passe

| Chemin | Politique appliquée | Avant hachage | Aucune écriture si invalide | Forme du refus |
|---|---:|---:|---:|---|
| `CreateUserUseCase` | ✅ | ✅ | ✅ | `ValidationErrors` (P3-1) |
| `UpdateUserUseCase` (mot de passe fourni) | ✅ | ✅ | ✅ (aucune propriété mutée) | `ValidationErrors` (P3-1) |
| `UpdateUserUseCase` (mot de passe absent/blanc) | n/a — « conserver » | n/a | hash strictement inchangé | — |
| `AuthenticationService.ChangePasswordAsync` | ✅ | ✅ | ✅ | `BusinessRuleException` |
| Seed bootstrap | ✅ (resolver, inchangé) | ✅ | ✅ | `SeedConfigurationException` |

Propriétaire unique côté Application : `UserCommandGuards` (`UseCases/Users/Common/`), partagé par Create et
Update pour qu'aucun des deux chemins ne puisse durcir ou relâcher la règle sans l'autre.

**Ordre dans `ChangePasswordAsync`** : mot de passe actuel vérifié **d'abord**, politique **ensuite**. Valider la
politique en premier révélerait, à un appelant ne connaissant pas le mot de passe actuel, que le compte existe et
que sa proposition était acceptable — une fuite gratuite.

**Forme du refus dans `ChangePasswordAsync`** : exception typée, pas `false`. Le contrat renvoie déjà `false`
pour « utilisateur introuvable » et « mot de passe actuel erroné » ; un troisième sens rendrait un mot de passe
faible indiscernable d'une erreur d'authentification et l'UI existante afficherait un message faux.

**Non modifié** : longueur minimale 8, exigences majuscule/minuscule/chiffre, absence de caractère spécial
obligatoire, aucun historique, aucune rotation, aucune expiration. `ValidatePasswordPolicy` s'arrête à la
première exigence non satisfaite et renvoie **un** message : l'ordre est déjà déterministe, et les erreurs ne
sont ni concaténées ni agrégées.

---

## 9. Résultats P3-1

Extension **additive** (aucun champ retiré, aucun appelant cassé) :

| Résultat | Champs conservés | Champs ajoutés |
|---|---|---|
| `CreateUserResult` | `UserId`, `UsernameTaken` | `ValidationErrors`, `IsValid` |
| `UpdateUserResult` | `UserId`, `UserFound`, `UsernameTaken` | `ValidationErrors`, `IsValid` |

Messages stables :

| Cas | Message |
|---|---|
| Mot de passe faible | messages Domain de `ValidatePasswordPolicy` (inchangés) |
| Rôle manquant | `Le rôle est obligatoire.` (`UserCommandGuards.RoleRequiredMessage`) |
| Rôle invalide | `Le rôle sélectionné est invalide.` (`UserValidator`, inchangé) |
| Login déjà utilisé | `Ce nom d'utilisateur est déjà utilisé.` (`UserCommandGuards.UsernameTakenMessage`) |

`UsernameTaken` reste cohérent avec `ValidationErrors` : quand il vaut `true`, la liste porte l'erreur de login
correspondante — garde pré-écriture **et** filet concurrent renvoient le même texte. Aucun détail BCrypt, EF ou
SQLite n'est exposé.

---

## 10. CreateUser

Flux effectif (`CreateUserUseCase.ExecuteAsync`) :

1. garde `command != null` (`ArgumentNullException`) ;
2. **politique de mot de passe** et **présence du rôle** — les deux refus sont collectés ensemble et renvoyés
   d'un coup (l'appelant corrige tout en une passe) ;
3. si refus ⇒ retour immédiat : **aucun hash**, **aucune entité**, **aucune écriture** ;
4. hachage **une seule fois** (`IAuthenticationService.HashPassword`) ;
5. construction d'un `User` **détaché** : `Username` trimé, `NormalizedUsername` normalisé, rôle explicite,
   `IsActive` selon le contrat existant, `CreatedAt = UtcNow` ;
6. `UserValidator` sur le candidat détaché ;
7. unicité par `ExistsByNormalizedUsernameAsync(..., excludingUserId: null)` ⇒ `UsernameTaken` ;
8. écriture dans `ITransactionRunner` (`CreateAsync` + `SaveChangesAsync`) ;
9. `PersistenceException` de catégorie `UniqueConstraint` ⇒ **même** résultat métier stable que la garde
   pré-écriture. Toute autre catégorie est **propagée**.

Le mot de passe clair n'est jamais porté par `User` ; seul `PasswordHash` l'est.

> **Sur le point 9.** `Users` ne porte **qu'un seul** index unique (`NormalizedUsername`) : aucune autre
> contrainte d'unicité ne peut être confondue avec ce doublon. C'est pourquoi la confirmation explicite que
> `CreateProductUseCase` effectue pour les FK (P3-9) n'a pas d'équivalent nécessaire ici.

---

## 11. UpdateUser

1. garde `command != null` ;
2. chargement ; introuvable ⇒ `UserFound = false` ;
3. mot de passe fourni (non blanc) ? ⇒ politique appliquée ;
4. présence du rôle vérifiée ;
5. refus ⇒ retour **sans aucune mutation** de l'entité suivie ;
6. candidat **détaché** complet (y compris `LastLogin` et `CreatedAt` repris de l'existant) ;
7. `UserValidator` sur le candidat ;
8. collision `NormalizedUsername` **hors utilisateur courant** ;
9. **seulement alors** : application des valeurs à l'entité suivie, puis re-hachage si un mot de passe a été
   fourni ;
10. écriture dans `ITransactionRunner` ; course d'unicité ⇒ `UsernameTaken`.

**Pourquoi le candidat détaché.** `GetByIdAsync` renvoie une entité **suivie** (`FindAsync`). La muter puis
découvrir l'invalidité laisserait des modifications en attente qu'un `SaveChanges` ultérieur — même déclenché
par un autre use case partageant l'unité de travail — persisterait. C'est ce qui rend vraie l'affirmation
« un refus ne modifie aucune propriété », vérifiée dans un **nouveau contexte** par les tests.

Non traités (reports) : *lost update* (R5), protection du dernier administrateur (R9).

---

## 12. Repository

Port `IUserRepository` (Domain, provider-neutre) :

| Avant | Après |
|---|---|
| `GetByUsernameAsync(string)` — comparaison binaire sur `Username` | `GetByNormalizedUsernameAsync(string)` — **normalise elle-même** son argument, filtre sur `NormalizedUsername` |
| — | `ExistsByNormalizedUsernameAsync(string, long? excludingUserId, ...)` |

La méthode a été **renommée** plutôt que dédoublée : deux surcharges dont la différence serait implicite
rouvriraient exactement l'ambiguïté corrigée. Le nom porte désormais la clé réellement interrogée, pour
qu'aucun appelant ne puisse croire à une recherche sensible à la casse.

Implémentation : filtre **direct** sur la colonne indexée (jamais une fonction SQL appliquée à `Username`, qui
serait non-SARGable et dépendante de la collation) ; `AsNoTracking` sur l'authentification et les contrôles
d'unicité ; `AnyAsync` (aucune entité matérialisée, aucune lecture complète de table) ; aucun `FindAsync` ;
aucun `IQueryable` exposé.

---

## 13. Authentification

**Inchangé** : BCrypt WF11, anti-énumération (résultat `null` uniforme pour compte inconnu / inactif / mot de
passe erroné), refus des comptes inactifs, mise à jour de `LastLogin`.

**Seule évolution** : la recherche passe par `GetByNormalizedUsernameAsync`. `admin`, `Admin`, `ADMIN` et
` ADMIN ` atteignent donc le même compte. La lecture reste fraîche (`AsNoTracking`) : une désactivation faite
sur un autre poste est vue à la connexion suivante.

---

## 14. ChangePassword

| Aspect | État |
|---|---|
| Mot de passe actuel requis | conservé |
| Mauvais mot de passe actuel | conservé (`false`) |
| Utilisateur introuvable | conservé (`false`) |
| Nouveau sel à chaque changement | conservé (propriété BCrypt) |
| Réutiliser le même mot de passe | autorisé (aucun historique introduit) |
| **Nouveau mot de passe faible** | **`BusinessRuleException`, aucune écriture** |

**Ordre** : mot de passe actuel vérifié **d'abord**, politique ensuite (§8).

**Forme du refus** : exception typée du Domain, non `false` (§8). Aucune exception BCrypt n'est exposée.
Le contrat `IAuthenticationService` documente désormais cette exception. **Aucune UI n'a été modifiée.**

---

## 15. Rôles

- `CreateUserCommand.Role` et `UpdateUserCommand.Role` : `UserRole` → **`UserRole?`**.
- `null` ⇒ `Le rôle est obligatoire.` (erreur de validation, aucune écriture).
- Valeur hors enum (obtenue par cast) ⇒ refusée par `UserValidator.IsInEnum`, désormais réellement invoqué.
- Les trois rôles (`Admin`, `Optician`, `Technician`) sont inchangés.

Le passage au nullable n'a **pas** cassé les appels existants : l'affectation d'un `UserRole` à un `UserRole?`
est implicite, et l'UI fournit toujours un rôle explicite. Le défaut `Optician` envisagé en repli n'a donc pas
été nécessaire — l'option préférée du cahier des charges a pu être retenue telle quelle.

> **P3-10 valide la valeur du rôle et supprime l'élévation par omission. Il ne peut pas autoriser l'acteur qui
> demande le changement, car Application ne possède aucun `ICurrentUser`. Cette limite reste explicitement
> reportée.** Un appelant direct peut toujours demander `Admin` explicitement ; les tests de §21 ne prouvent
> **pas** l'autorisation de l'acteur.

---

## 16. Activation et désactivation

`SetUserActiveUseCase` est **inchangé** (aucune modification de code). Le comportement est désormais **prouvé**
par des tests dédiés : désactivation, seconde désactivation identique (idempotente), réactivation, seconde
réactivation identique, utilisateur introuvable, refus d'authentification d'un compte inactif, exclusion des
sélecteurs (`GetActiveUsers`, `GetByRole`) mais présence dans la liste de gestion (`GetAllAsync`), et
**historique intact** (`Sale.StaffId`, `StockMovement.PerformedByUserId` conservés).

Aucune suppression ajoutée. Aucune garde « dernier administrateur ». Aucune modification de session UI.

---

## 17. Seed production

Correction de **R4** dans `DatabaseSeeder.SecureBootstrap` : l'ensemble des comptes portant un hash faible connu
est matérialisé (`ToList`) et traité **en bloc**, là où un `FirstOrDefault` n'en traitait qu'un.

Comportement en production :

| Situation | Résultat |
|---|---|
| Sans secret bootstrap | **Tous** les comptes faibles connus encore actifs sont désactivés |
| Avec secret bootstrap | Le compte canonique (identifié par sa forme **normalisée**) est sécurisé/créé, rehashé BCrypt WF11, actif ; **tous les autres** comptes faibles sont désactivés |
| Administrateur réel déjà présent | Non écrasé |
| Compte non faible | **Jamais** désactivé ni modifié |
| Second passage | Idempotent — aucun changement, aucune écriture |
| Données de démonstration | Aucune en production |
| Secret | Absent de `SeedResult`, de ses notes et des logs |

Le jeu de démonstration (`DbInitializer`) est inchangé en Dev/Demo/Test, à l'exception de l'ajout de
`NormalizedUsername` sur les quatre comptes et du passage de son garde d'idempotence sur la clé normalisée —
comparer les formes affichables laisserait passer un `Admin` à côté d'un `admin` existant, et l'ajout échouerait
sur l'index unique au lieu d'être ignoré.

---

## 18. Migration

`20260721134634_AddNormalizedUsernameAndSecureLocalUsers` — **une seule** migration, additive. Aucune ancienne
migration modifiée.

Ordre du `Up` :

1. `AddColumn NormalizedUsername` (`TEXT`, `maxLength: 50`, `NOT NULL`, défaut `''` **transitoire** — SQLite
   l'exige pour un `ALTER TABLE ADD COLUMN NOT NULL`) ;
2. garde « login hérité invalide » (§19) ;
3. garde « collision insensible à la casse » (§19) ;
4. backfill `UPDATE Users SET NormalizedUsername = lower(trim(Username))` ;
5. garde « backfill incomplet » : aucune ligne ne conserve la valeur vide transitoire ;
6. `DropIndex idx_users_username_unique` ;
7. `CreateIndex idx_users_normalized_username_unique` (unique).

**Base neuve** : produit directement `Username` et `NormalizedUsername` non nuls, l'index unique normalisé, et
**aucun** index unique sensible à la casse utilisé comme clé métier.

**`Down`** — documenté honnêtement dans le code :
- supprime l'index normalisé, supprime la colonne, restaure `idx_users_username_unique` ;
- **restaure donc l'ancien comportement sensible à la casse** (`admin` ≠ `Admin`) pour les opérations
  **futures**, avec l'ambiguïté que P3-10 corrige ;
- ne fusionne, ne recrée et ne renomme **aucune** donnée ; les `Username` affichables restent inchangés ;
- **un rollback normal est réversible pour toute base respectant le schéma P3-10.** Sous un schéma intact,
  l'index unique sur `NormalizedUsername` interdit précisément les comptes ne différant que par la casse : ils
  ne peuvent donc pas exister, et la restauration de l'ancien index aboutit toujours. *(Correction : une version
  antérieure de ce rapport affirmait l'inverse.)*
- un échec ne serait possible qu'après **altération manuelle du schéma** ou contournement de la contrainte ;
  aucune donnée n'est alors supprimée pour réparer une base altérée.

---

## 19. Backfill — exact ou échec

Règle applicative, source unique de vérité : `UserIdentityPolicy.NormalizeUsername(u) = u.Trim().ToLowerInvariant()`.

`lower(trim(x))` de SQLite égale `Trim().ToLowerInvariant()` **si et seulement si** `x` appartient au jeu ASCII
autorisé par `UserValidator` (`[a-zA-Z0-9._-]`) :

- le `lower()` natif de SQLite ne replie que `A–Z`, comme l'invariant sur l'ASCII — aucune extension ICU, aucune
  dépendance à la culture de la machine, même résultat que la migration soit jouée par l'application ou par
  `dotnet ef` ;
- le `trim()` natif ne retire que l'espace `U+0020`, là où `.NET Trim()` retire **toute** espace Unicode. Un
  login portant une tabulation ou une espace insécable périphérique tombe donc hors du jeu autorisé et est
  **rejeté** — jamais normalisé de travers.

Cas **refusés** (migration avortée, transaction annulée, rien inscrit, aucune ligne touchée) :

| Cas | Détection |
|---|---|
| `Username` NULL | garde SQL |
| vide ou blanc après `trim` | garde SQL |
| longueur hors bornes 3–50 (celles de `UserValidator`) | garde SQL — SQLite n'impose pas `HasMaxLength` |
| caractère hors du jeu autorisé (espace interne, `@`, non-ASCII…) | garde SQL (`GLOB`) |
| collision insensible à la casse entre comptes distincts | garde SQL dédiée |
| backfill incomplet (valeur vide résiduelle) | garde SQL finale |

Mécanisme (repris de P3-4B) : insertion des valeurs fautives dans une table à contrainte `CHECK` impossible, dont
le **nom porte l'action attendue de l'opérateur** (`__abort_invalid_username_needs_manual_fix`,
`__abort_case_insensitive_username_collision_needs_manual_fix`,
`__abort_normalized_username_backfill_incomplete`).

**Aucune fusion automatique, aucun renommage inventé, aucune ligne supprimée.** Fusionner deux comptes détruirait
un historique (ventes, mouvements de stock) ; renommer inventerait un identifiant que personne n'a choisi.
L'exploitant tranche, puis rejoue la migration.

---

## 20. Adoption historique

`SqliteSchemaVerifier` — `Users.NormalizedUsername` ajouté aux colonnes additives tolérées quand absentes, et
`Users.NormalizedUsername` aux index uniques additifs tolérés. L'ancien index unique sur `Users.Username`
subsiste alors en base : élément supplémentaire, toléré, supprimé par la migration exécutée pendant l'adoption.

`SqliteDatabaseManager` :

- **Refus avant toute écriture d'historique** d'un schéma P3-10 **partiel ou altéré** : index présent sans la
  colonne, ou colonne présente sans un index unique **valide**.
- **Exécution (et non baseline)** de la migration si `Users.NormalizedUsername` est physiquement absente.
  Baseliner marquerait à tort la colonne et l'index comme créés : le login resterait comparé en binaire et le
  backfill n'aurait jamais lieu.
- **Vérification physique inconditionnelle** dans `VerifyAfterPreparation` (tous chemins : installation neuve,
  base managée, base adoptée) : colonne présente **et** index unique valide, sinon `DatabaseMigrationException`.

`InspectNormalizedUsernameUniqueIndex` lit l'unicité et les colonnes via PRAGMA `index_list` / `index_info`,
**et non** par recherche du mot « UNIQUE » dans `sqlite_master.sql`.

> **Défaut trouvé par les tests, corrigé.** La première version cherchait `"UNIQUE"` dans le texte SQL. Le nom de
> l'index se terminant lui-même par `_unique`, la recherche était vraie **même pour un index non unique** portant
> ce nom — exactement l'homonyme mal défini que la fonction doit détecter. Le test
> `Adoption_HomonymIndexNotUnique_IsRefused` a révélé le faux positif ; la lecture PRAGMA est autoritaire.

**Jamais** la migration n'est inscrite sur la seule présence de la colonne.

---

## 21. Concurrence

| Acte | Protection | Résultat |
|---|---|---|
| Double création, même login normalisé | index unique + traduction `UniqueConstraint` | une réussite, une erreur **métier** `UsernameTaken` |
| Double création, variantes de casse | idem (c'est le même login normalisé) | idem |
| Renommage concurrent vers un login pris | idem | `UsernameTaken` |
| Autre violation de contrainte | filtre `when` sur la seule catégorie `UniqueConstraint` | `PersistenceException` propagée, **jamais** maquillée en doublon de login |
| Désactivation vs connexion | lecture `AsNoTracking` | le login voit l'état frais |
| Modification concurrente (profil, rôle, mot de passe) | **aucune** | *lost update* possible — **report explicite (R5)** |

Aucun message SQLite/EF brut n'atteint l'appelant sur le chemin doublon. Les tests de course sont
**déterministes** (décorateur de repository ; aucun `Thread.Sleep`, aucun parallélisme réel).

---

## 22. Données sensibles

- Aucun mot de passe clair sur `User` (seul `PasswordHash`) — vérifié par garde d'architecture.
- `UserListItemDto` n'expose ni hash, ni sel, ni mot de passe — garde d'architecture.
- `SeedResult` n'expose aucun secret bootstrap — garde d'architecture, et test vérifiant que le secret n'apparaît
  pas dans les notes.
- Aucun clair persisté — vérifié en base après création et après changement de mot de passe.
- Aucun détail BCrypt/EF/SQLite dans les messages métier.
- BCrypt reste confiné à l'Infrastructure (Domain et Application ne le référencent pas) — garde d'architecture.

---

## 23. Fichiers créés / modifiés

### Créés (12 — dont 2 rapports)

**Code et tests (10) :**

| Fichier | Rôle |
|---|---|
| `src/MMV.Domain/Policies/UserIdentityPolicy.cs` | Propriétaire unique de la normalisation |
| `src/MMV.Application/UseCases/Users/Common/UserCommandGuards.cs` | Gardes partagées Create/Update + messages stables |
| `src/MMV.Infrastructure/Migrations/20260721134634_AddNormalizedUsernameAndSecureLocalUsers.cs` | Migration + backfill |
| `src/MMV.Infrastructure/Migrations/20260721134634_….Designer.cs` | Généré |
| `tests/MMV.Domain.Tests/PolicyTests/UserIdentityPolicyTests.cs` | Normalisation |
| `tests/MMV.Domain.Tests/Data/NormalizedUsernameMigrationTests.cs` | Migration, backfill, adoption, états partiels, défaut physique, Down/rejeu |
| `tests/MMV.Application.Tests/UseCases/Users/UserPasswordPolicyTests.cs` | Politique sur Create/Update |
| `tests/MMV.Application.Tests/UseCases/Users/UserIdentityAndRoleTests.cs` | Normalisation, unicité, concurrence, rôles, change tracker |
| `tests/MMV.Application.Tests/UseCases/Users/UserLifecycleTests.cs` | Activation/désactivation, historique |
| `tests/MMV.Application.Tests/Architecture/UsersApplicationArchitectureTests.cs` | Garde-fous structurels |

**Documentation (2) :** `docs/implementation/P3-10-local-users-business-rules-audit-report.md`,
`docs/implementation/P3-10-local-users-business-rules-implementation-report.md` (ce document).

> **Correction.** Les versions antérieures de cette section annonçaient « Créés (8) » tout en énumérant dix
> fichiers, et omettaient les deux rapports. Le décompte ci-dessus est recalculé sur l'état Git réel final.

### Modifiés — production (17)

`User.cs`, `IUserRepository.cs`, `IAuthenticationService.cs` (documentation de l'exception),
`CreateUserCommand/Result/UseCase.cs`, `UpdateUserCommand/Result/UseCase.cs`, `UserRepository.cs`,
`AuthenticationService.cs`, `UserConfiguration.cs`, `DatabaseSeeder.cs`, `DbInitializer.cs`,
`SqliteDatabaseManager.cs`, `SqliteSchemaVerifier.cs`, `OpticDbContextModelSnapshot.cs`.

### Modifiés — tests (6)

`UserUseCasesTests.cs`, `ListUsersUseCaseTests.cs`, `UserRepositoryTests.cs`, `AuthenticationServiceTests.cs`,
`DatabaseSeederTests.cs`, `SqliteDatabaseManagerTests.cs`.

> **Note sur `SqliteDatabaseManagerTests`.** Le fixture « base historique antérieure à P3-8 » construisait son
> schéma par `EnsureCreated()` (donc le modèle **courant**), puis retirait les seuls éléments P3-8. Il décrivait
> ainsi une base impossible — P3-8 absent mais P3-10 présent — et l'exécution forcée de `AddNotificationResolution`
> rejouait ensuite la migration P3-10 sur une colonne déjà là. Le fixture retire désormais aussi la colonne et
> l'index P3-10 et restaure l'ancien index : il décrit une base réellement de cette époque. L'intention du test
> (P3-8 absent ⇒ migration exécutée) est **inchangée**.

**Aucun fichier sous `src/MMV.App/**`, `design/`, `design-handoff/` ou `docs/ui/`.**

---

## 24. Tests Domain

| Fichier | Sujet | Nb |
|---|---|---:|
| `PolicyTests/UserIdentityPolicyTests.cs` | Trim + minuscules, variantes ⇒ clé identique, `null`/blanc sans exception, idempotence, longueur max | 15 |
| `ServiceTests/AuthenticationServiceTests.cs` (étendu) | Politique sur `ChangePassword` (4 branches), ordre des gardes, nouveau sel, même mot de passe ⇒ hash différent, deux hachages ⇒ hash différents, variantes de casse au login (4) | +14 |
| `ValidatorTests/UserValidatorTests.cs` | **Inchangé** — les branches internes de la politique y sont déjà couvertes, non dupliquées ailleurs | 21 |

---

## 25. Tests Application

| Fichier | Sujet | Nb |
|---|---|---:|
| `UserPasswordPolicyTests.cs` | Create : 6 branches faibles ⇒ rien écrit **et aucun appel BCrypt** ; création valide ⇒ clair jamais persisté ; Update : faible ⇒ **aucune propriété modifiée**, absent ⇒ hash conservé, valide ⇒ nouveau hash | 10 |
| `UserIdentityAndRoleTests.cs` | Variantes de casse/espaces refusées (4) ; trim + clé normalisée ; collision en Update ; changement de sa **propre** casse accepté ; `NormalizedUsername` jamais vide ; course Create et Update ⇒ erreur métier ; autre contrainte **non** classée en doublon ; rôle absent (Create/Update), hors enum, trois rôles valides | 16 |
| `UserLifecycleTests.cs` | Désactivation/réactivation idempotentes, introuvable, refus d'authentification puis restauration, historique `Sale`/`StockMovement` intact, exclusion des sélecteurs mais présence en gestion | 6 |
| `UserUseCasesTests.cs` (adapté) | Contrats existants, sous les nouvelles règles | 8 |

**Preuve d'ordre.** Le fake d'authentification **compte** les hachages : `HashCallCount == 0` sur chaque refus
prouve que la politique s'exécute **avant** BCrypt, et non après — propriété qu'aucune assertion sur l'état final
ne pourrait établir.

---

## 26. Tests Infrastructure

Couverts via Application et Domain sur de **vraies** bases SQLite : repository (filtre normalisé, `AsNoTracking`,
exclusion de l'utilisateur courant), `AuthenticationService` (recherche normalisée, politique), index unique
réellement appliqué par la base.

---

## 27. Tests migration

`NormalizedUsernameMigrationTests` — **22 tests**, vraies bases SQLite jetables :

| Scénario | Attendu | ✓ |
|---|---|---:|
| Base neuve | colonne `NOT NULL`, index unique non filtré, ancien index absent, aucune migration en attente, modèle EF aligné | ✅ |
| Adoption valide (`admin`, `Marie.Optic`, `PIERRE.TECH`) | normalisées en `admin`/`marie.optic`/`pierre.tech`, `Username` conservés, aucune ligne perdue, historique inscrit | ✅ |
| Backfill = règle applicative | `NormalizedUsername == UserIdentityPolicy.NormalizeUsername(Username)` pour chaque ligne | ✅ |
| Collision de casse (3 variantes) | échec, rien inscrit, 2 lignes intactes, aucun renommage | ✅ |
| Login invalide (vide, blanc, trop court, espace interne, `@`, non-ASCII) | échec sûr, colonne absente, rien inscrit | ✅ |
| Login trop long | échec sûr (SQLite n'impose pas `HasMaxLength`) | ✅ |
| Espaces **périphériques** | **accepté**, normalisé exactement | ✅ |
| Unicité effective après migration | insertion d'une variante de casse rejetée par la base | ✅ |
| Adoption : tout absent | migration **exécutée**, backfill effectué | ✅ |
| Adoption : tout présent, historique cohérent | baseline sans rejouer l'ajout de colonne | ✅ |
| Colonne présente / index absent | **refusé**, rien inscrit | ✅ |
| Index homonyme non unique | **refusé** | ✅ |
| Index homonyme sur mauvaise colonne | **refusé** | ✅ |
| Index présent / colonne absente | **refusé** | ✅ |
| Base managée, historique présent, schéma altéré | **refusé** | ✅ |
| Colonne nullable | **refusé** | ✅ |

Aucun état partiel n'est accepté comme migration complète.

---

## 28. Tests seed

`DatabaseSeederTests` (étendu) — scénario transition démonstration → production avec quatre comptes faibles
(`admin`, `marie.optic`, `pierre.tech`, `sophie.optic`) :

| Test | Attendu | ✓ |
|---|---|---:|
| Sans secret | **tous** inactifs, 4 comptes conservés (aucune suppression) | ✅ |
| Avec secret | admin canonique actif, rehashé `$2a$11$`, vérifiable ; **3 autres inactifs** ; secret absent des notes | ✅ |
| Second passage | idempotent, aucun changement signalé | ✅ |
| Compte réel présent | jamais désactivé ni modifié ; comptes faibles neutralisés ; aucune donnée de démonstration en production | ✅ |

---

## 29. Résultats ciblés

```
dotnet test tests/MMV.Domain.Tests       → Passed: 589, Failed: 0, Skipped: 0
dotnet test tests/MMV.Application.Tests  → Passed: 579, Failed: 0, Skipped: 0
```

---

## 30. Résultat complet

```
dotnet build MMV.sln --no-restore -c Debug   → 0 avertissement, 0 erreur
dotnet test MMV.sln --no-build -c Debug
  MMV.App.Tests           → Passed: 239, Failed: 0, Skipped: 0
  MMV.Application.Tests   → Passed: 579, Failed: 0, Skipped: 0
  MMV.Domain.Tests        → Passed: 589, Failed: 0, Skipped: 0
```

| | Baseline | Après P3-10 | Δ |
|---|---:|---:|---:|
| Domain | 537 | **589** | +52 |
| Application | 532 | **579** | +47 |
| App (UI) | 239 | **239** | **0** |
| **Total** | **1308** | **1407** | **+99** |

0 échec, 0 ignoré. **Aucun test UI ajouté ni modifié.**

> **Totaux après la revue ciblée** (section dédiée ci-dessous, 6 tests ajoutés) : Domain **593**,
> Application **581**, App **239**, **total 1413** — 0 échec, 0 ignoré.

Autres portes :

| Contrôle | Résultat |
|---|---|
| `dotnet list package --vulnerable --include-transitive` | aucune vulnérabilité (7 projets) |
| `dotnet ef migrations has-pending-model-changes` | *No changes have been made to the model…* |
| `dotnet list MMV.Application reference` | `MMV.Domain` **seul** |
| `dotnet list MMV.Application package` | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (inchangé) |
| `git diff --check` | propre |

---

## 31. Sécurité

| Propriété | État |
|---|---|
| Politique appliquée avant hachage sur Create, Update, ChangePassword | ✅ prouvé |
| Aucune écriture pour un mot de passe faible | ✅ prouvé (y compris : aucun appel BCrypt) |
| Aucun clair persisté | ✅ prouvé en base |
| BCrypt WF11 inchangé | ✅ |
| Sel unique par hachage | ✅ prouvé |
| Compte inactif non authentifiable | ✅ prouvé |
| Anti-énumération conservée | ✅ (résultat `null` uniforme) |
| Login non ambigu (casse/espaces) | ✅ prouvé, garanti par index unique |
| Tous les comptes faibles connus neutralisés en production | ✅ prouvé |
| Aucun secret dans logs/résultats | ✅ prouvé |
| Aucune suppression d'utilisateur | ✅ garde d'architecture |

**Non couvert (reports)** : verrouillage/throttling après échecs, MFA, rotation, historique de mot de passe,
invalidation de session, autorisation par acteur, borne maximale de longueur de mot de passe.

---

## 32. EF

- Une seule migration additive ; aucune ancienne migration modifiée.
- Snapshot régénéré par `dotnet ef` (aucune retouche manuelle).
- `has-pending-model-changes` : aucun changement en attente.
- `EnsureCreated` (tests) et `Migrate` (production) produisent le même schéma — vérifié.
- FK historiques `Sale.StaffId` / `StockMovement.PerformedByUserId` : **inchangées** (nullables, `SetNull`).

---

## 33. Architecture

`UsersApplicationArchitectureTests` — 14 gardes :

- Domain et Application ne référencent ni EF, ni SQLite, ni BCrypt ;
- Application ne référence que `MMV.Domain` parmi les projets ;
- port utilisateur provider-neutre (aucun `IQueryable`/EF/SQLite dans les signatures) ;
- **un seul propriétaire de normalisation** : aucun `ToLowerInvariant()` dans les six fichiers du chemin runtime
  User (vérifié sur le source réel) ;
- `UserIdentityPolicy` vit dans le Domain, statique et sans état ;
- `User` ne porte aucun mot de passe clair ;
- `UserListItemDto` n'expose ni hash ni secret ; `SeedResult` non plus ;
- aucun `DeleteUser*` ; aucun `ICurrentUser`/`IPermissionService`/`IAuthorizationService` ;
- `UserRole` conserve exactement ses trois valeurs ;
- `NormalizedUsername` requis, longueur 50, **unique index unique** sur `Users` ;
- FK historiques inchangées (`SetNull`, nullables) ;
- `Role` nullable sur les deux commandes.

---

## 34. Reports explicites

Non implémentés en P3-10, délibérément :

- **Autorisation par acteur** (`ICurrentUser`) et permissions fines — P3-10 valide la *valeur* du rôle, pas le
  *droit* de la demander ;
- **Protection du dernier administrateur** (R9) — aucune exigence métier ne l'a prouvée ;
- **Token de concurrence** (R5) — le *lost update* reste possible sur Update/SetActive/ChangePassword ;
- **Invalidation de session** après désactivation / changement de rôle ou de mot de passe (R8) — UI ;
- **Onboarding / premier compte interactif** (R10) — UI ;
- Auteur du QC sur fiches atelier ; MFA ; SSO/OAuth/OIDC ; récupération par e-mail ; rotation et historique de
  mot de passe ; verrouillage/throttling ; audit trail complet ; chiffrement de base ; multi-magasin/SaaS ;
  provider serveur ; extension de `SqliteSchemaVerifier` aux autres aspects User ; borne maximale de longueur de
  mot de passe.

---

## 35. État Git final

```
git status --short
 M  17 fichiers de production  (Domain 3, Application 6, Infrastructure 8)
 M   6 fichiers de tests
 ??  8 fichiers créés (1 Domain, 1 Application, 2 migration, 4 tests)
 ??  2 rapports docs/implementation/P3-10-*
 ??  design/, design-handoff/, docs/ui/  (hors périmètre, non touchés)

git diff --check   → propre
```

- Branche : `p3-business-rules` — **aucun commit, aucun push**.
- HEAD inchangé : `057df0f3fad91f344a6333f3a52f32b6d1552ba1`.
- **Aucun** fichier sous `src/MMV.App/**`, `design/`, `design-handoff/`, `docs/ui/`.
- Une seule migration P3-10 ; aucune ancienne migration modifiée.
- Aucun `DeleteUser`, aucun `ICurrentUser`, aucune permission fine.
- Aucune modification fonctionnelle de P3-9 ou antérieur.

---

## Revue ciblée avant commit

> Revue **ciblée** (non générale) menée avant le commit, sur les six points désignés : définition physique finale
> de `NormalizedUsername`, backfill/rollback de la migration, adoption des schémas partiels ou altérés, état du
> change tracker après une violation d'unicité traduite en `UsernameTaken`, neutralisation exhaustive des comptes
> faibles, cohérence des rapports. **Trois défauts concrets ont été trouvés et corrigés.** Aucune seconde
> migration n'a été créée ; aucune ancienne migration n'a été modifiée ; aucune UI n'a été touchée.

### 1. Valeur physique finale de `dflt_value` — **défaut trouvé et corrigé**

`PRAGMA table_info('Users')` sur une base neuve migrée jusqu'à la tête donnait, **avant correction** :

```
(6, 'NormalizedUsername', 'TEXT', 1, "''", 0)
                                       ^^^^ dflt_value = '' — défaut PERMANENT
```

Le défaut vide n'était censé être qu'un **artifice transitoire** (SQLite l'exige pour
`ALTER TABLE ADD COLUMN NOT NULL`), mais il **subsistait dans le schéma final**. Conséquence prouvée par SQL
brut : une insertion omettant `NormalizedUsername` était **acceptée en silence** avec une clé métier vide, au
lieu d'échouer `NOT NULL` — exactement ce que la contrainte doit empêcher.

**Correction, dans la migration P3-10 existante** (aucune seconde migration) : un `AlterColumn` final déclenche
la reconstruction de table que le générateur SQLite d'EF Core effectue pour ce type d'opération, recréant la
colonne **sans** défaut. Ordre final du `Up` : `AddColumn` (défaut transitoire) → gardes → backfill → garde
finale → `DropIndex` legacy → **`AlterColumn` (suppression du défaut)** → `CreateIndex` unique.

**Valeur physique finale vérifiée** : `dflt_value` = **`NULL`**.

```
(6, 'NormalizedUsername', 'TEXT', 1, None, 0)
```

`sqlite_master` confirme le schéma final : `"NormalizedUsername" TEXT NOT NULL` — aucun `DEFAULT`.

### 2. Preuve par insertion SQL brute omettant la colonne

Exécuté hors EF, sur une base réellement migrée :

| Insertion brute | Résultat |
|---|---|
| `NormalizedUsername` **omise** | ❌ `NOT NULL constraint failed: Users.NormalizedUsername` — **0 ligne créée** |
| `NormalizedUsername = NULL` | ❌ `NOT NULL constraint failed` — refusée |
| Doublon normalisé (`Admin`/`admin` → `admin`) | ❌ `UNIQUE constraint failed: Users.NormalizedUsername` |

Deux tests ajoutés : `FreshDatabase_NormalizedUsername_HasNoPermanentDefault` (lit `dflt_value` via
`PRAGMA table_info`, colonne 4) et `FreshDatabase_RawInsertOmittingNormalizedUsername_FailsNotNull`. **Tous deux
échouaient avant la correction** — c'est ce qui a révélé le défaut.

### 3. Définition exacte de l'index

```
PRAGMA index_list('Users')
  → (0, 'idx_users_normalized_username_unique', 1, 'c', 0)
                                                ^unique=1     ^partial=0

PRAGMA index_info(...)  → (0, 6, 'NormalizedUsername')          — une seule colonne métier
PRAGMA index_xinfo(...) → (0, 6, 'NormalizedUsername', 0, 'BINARY', 1)
                          (1, -1, None, 0, 'BINARY', 0)         — rowid, normal

sqlite_master → CREATE UNIQUE INDEX "idx_users_normalized_username_unique"
                ON "Users" ("NormalizedUsername")
```

- nom exact conforme ; **unicité réelle** lue via `PRAGMA index_list` (drapeau `unique = 1`), **jamais** par
  recherche du mot « UNIQUE » dans le texte SQL — le nom de l'index se terminant lui-même par `_unique`, une
  telle recherche serait vraie même pour un index non unique (défaut déjà corrigé en §20) ;
- **exactement une** colonne métier, `NormalizedUsername`, en position 0 ;
- **non partiel** (`partial = 0`, aucune clause `WHERE`) ;
- **aucun** index homonyme non unique ; **aucun** ancien `idx_users_username_unique` — `index_list` ne retourne
  que ce seul index sur `Users`.

Les tests d'adoption existants échouent déjà si l'index est homonyme non unique, sur `Username`, multi-colonnes,
partiel, absent, ou si l'ancien index unique subsiste comme seule protection.

### 4. Résultat de la revue `GLOB` — **aucun défaut**

Classe testée : `'*[^a-zA-Z0-9._-]*'` appliquée à `trim("Username")`.

Le tiret est en **dernière position** de la classe : SQLite le traite alors comme un littéral, jamais comme
borne de plage. Vérifié par balayage exhaustif des codes `0x2E`–`0x5F` (entre `.` et `_`) : **aucun** caractère
n'est accidentellement autorisé — `/`, `:`, `;`, `<`, `=`, `>`, `?`, `@`, `[`, `\`, `]`, `^` sont tous rejetés,
seuls `.`, `0-9`, `A-Z`, `_` passent.

| Accepté (attendu valide) | Rejeté (attendu invalide) |
|---|---|
| `admin`, `Marie.Optic`, `pierre-tech`, `TECH_01` | espace interne, tabulation, saut de ligne, espace insécable |
| `-abc`, `abc-`, `a-b` (tiret en tête/fin/milieu) | `@`, `/`, `:`, `+` |
| espaces ASCII **périphériques** (retirés par `trim` avant le `GLOB`) | accentués (`MARIE.ÉLÈVE`), non latins |
| | longueur 2, longueur 51, chaîne vide, espaces seuls, `NULL` |

Les bornes 3–50 et la nullité sont couvertes par les prédicats SQL voisins, pas par le `GLOB`. Pour chaque login
accepté, `lower(trim(Username))` est prouvé **égal** à `UserIdentityPolicy.NormalizeUsername(Username)` par
assertion directe dans les tests, ligne à ligne.

### 5. Résultat des collisions et rollbacks — **aucun défaut**

Groupes testés (`admin`/`Admin`, `admin`/`ADMIN`, `Marie.Optic`/`marie.optic`), plus données invalides :

- migration **entièrement refusée** ; aucune paire arbitrairement conservée ;
- **aucune** ligne supprimée, renommée ni fusionnée ; les `Username` affichables sont vérifiés identiques
  après l'échec ;
- **aucune** valeur normalisée partielle persistée — la colonne P3-10 est **absente** après le rollback ;
- migration **non inscrite** dans `__EFMigrationsHistory` ;
- ancien index intact, état antérieur intégralement restauré.

L'état « deux utilisateurs au même `Username` exact » n'est **pas** revendiqué comme provenant d'une base
historique saine : l'ancien index unique l'interdisait. Il n'est donc pas testé sous ce prétexte.

### 6. États d'adoption testés — **aucun défaut**

La matrice complète est couverte par `NormalizedUsernameMigrationTests` et `SqliteDatabaseManagerTests` :
migration **exécutée** (jamais baselinée) quand la colonne est physiquement absente ; adoption acceptée quand
colonne + index exact + historique sont cohérents ; **refus** pour colonne sans index, index sans colonne,
homonyme non unique, homonyme sur mauvaise colonne, colonne nullable, et base **managée** dont le schéma a été
altéré après coup. La vérification physique post-préparation (`VerifyAfterPreparation`) est
**inconditionnelle** : installation neuve, base managée, base adoptée et base déjà à jour passent toutes par
elle. La migration n'est **jamais** inscrite sur la seule présence de la colonne, et un défaut permanent `''`
n'est jamais considéré comme un schéma final valide (§1).

### 7. Résultat réel du `Down` puis du rejeu de `Up` — **affirmation documentaire corrigée**

Test ajouté `Down_OnValidDatabase_RestoresLegacyIndex_KeepsAllData_ThenUpReproducesNormalizedKeys`, sur base
P3-10 valide portant trois comptes :

| Étape | Vérifié |
|---|---|
| `Down` | colonne normalisée **supprimée** ; index normalisé **supprimé** ; `idx_users_username_unique` **restauré et unique** ; 3 lignes intactes ; `Username` affichables strictement inchangés ; migration retirée de l'historique |
| rejeu `Up` | clés normalisées reproduites **exactement** (`admin`, `marie.optic`, `pierre-tech`), égales à `UserIdentityPolicy.NormalizeUsername` ligne à ligne ; index unique rétabli ; ancien index absent ; **`dflt_value` de nouveau `NULL`** |

**Correction documentaire appliquée** (§18). L'affirmation « le `Down` peut échouer si des comptes ne différant
que par la casse ont été créés pendant que P3-10 était appliqué » était **fausse** : sous un schéma P3-10 intact,
l'index unique sur `NormalizedUsername` interdit précisément ces comptes. Formulation retenue : un rollback
normal est **réversible pour toute base respectant le schéma P3-10** ; il restaure l'ancienne sémantique
sensible à la casse pour les opérations **futures** ; un échec ne serait possible qu'après **altération manuelle
du schéma** ; aucune donnée n'est supprimée pour réparer une base altérée.

### 8. État du tracker après un `CreateUser` refusé — **défaut trouvé et corrigé**

Scénario déterministe (décorateur `RaceLosingUserRepository` : garde applicative périmée « libre », login déjà
présent en base, vraie violation de l'index, rollback par `ITransactionRunner`, résultat `UsernameTaken`).

**Constat avant correction**, dans la même portée de contexte :

| Question | Réponse mesurée |
|---|---|
| Entité candidate encore `Added` ? | **oui** |
| Encore dans `ChangeTracker.Entries<User>()` ? | **oui** |
| Un `SaveChangesAsync` ultérieur **sans rapport** retente-t-il l'insertion ? | **oui** — `DbUpdateException` brute |
| Un utilisateur fantôme existe-t-il en base ? | non (le rollback SQL était correct) |

Le rollback **SQL** était donc correct, mais le **suivi EF en mémoire** ne l'était pas : EF ne détache jamais
automatiquement une entité après un `SaveChanges` en échec. Un appelant innocent partageant l'unité de travail
voyait son écriture casser à cause d'un refus qu'il ignorait.

**Correction — le plus petit mécanisme conforme à l'architecture**, repris du patron déjà retenu en P3-9
(`SupplierRepository`) : `IUserRepository.DetachIfTracked(User)`, implémenté dans `UserRepository` via
`_context.Entry(user)`, appelé **uniquement** dans le `catch (PersistenceException … UniqueConstraint)` des deux
use cases.

- **aucun** `ChangeTracker.Clear()` opportuniste ;
- **aucune** dépendance EF dans Domain/Application — le port reste provider-neutre (`void DetachIfTracked(User)`),
  et la garde d'architecture existante le vérifie ;
- **aucune** entité sans rapport détachée (recherche **par référence**, ciblée sur l'écriture rejetée) ;
- **aucune** requête SQL supplémentaire (opération en mémoire) ;
- `EfTransactionRunner` **non modifié** : le défaut n'est pas général et son contrat de rollback ne promet pas
  cette remise à zéro. Une correction ciblée est préférée, cohérente avec le précédent P3-9.

**Après correction** : plus aucune trace suivie de l'entité rejetée ; un `SaveChangesAsync` ultérieur sans
rapport **réussit normalement** ; la base ne contient que l'écriture gagnante et l'écriture sans rapport.

### 9. État du tracker après un `UpdateUser` refusé — **même défaut, même correction**

Scénario déterministe (A chargé et suivi, B porte déjà le login cible, garde périmée, mutation de A vers le login
normalisé de B, vraie violation de l'index, résultat `UsernameTaken`).

**Avant correction** : A restait `Modified` en portant le login **refusé** ; `FindAsync(A.Id)` renvoyait
l'instance mutée rejetée depuis l'*identity map* au lieu d'interroger la base ; les autres propriétés refusées
(prénom, rôle, `IsActive`) survivaient également dans le tracker.

**Après correction** (`DetachIfTracked` dans le `catch`), vérifié :

- aucune mutation rejetée ne peut être persistée ultérieurement ;
- une lecture ultérieure dans la même portée (`FindAsync`) ne présente **plus** le login refusé : elle renvoie
  `marie.optic` et le prénom d'origine ;
- un **nouveau** contexte retrouve intégralement les valeurs originales ;
- **aucune** entité voisine n'est détachée ni modifiée.

### 10. Correction appliquée — récapitulatif

| # | Défaut concret | Correction | Portée |
|---|---|---|---|
| D1 | `NormalizedUsername` portait un **défaut permanent `''`** ; une insertion omettant la colonne était acceptée en silence avec une clé métier vide | `AlterColumn` final dans la migration P3-10 **existante** (reconstruction SQLite sans défaut) | Migration |
| D2 | Après un refus `UsernameTaken`, l'**entité rejetée restait suivie** ; un `SaveChanges` ultérieur sans rapport retentait l'écriture et échouait | `IUserRepository.DetachIfTracked` appelé dans le `catch` de `CreateUser` **et** `UpdateUser` | Domain (port) + Infrastructure + Application |
| D3 | `SecureBootstrap` renommait un compte faible **arbitraire** vers le login bootstrap : collision d'unicité **au démarrage** si un administrateur réel l'occupait déjà ; et la branche « administrateur réel déjà présent » **ne neutralisait aucun** compte faible | repli limité aux cas où le login bootstrap est **libre** ; neutralisation des comptes faibles **ajoutée** sur cette branche | Infrastructure (seed) |

**Aucune** seconde migration créée. **Aucune** ancienne migration modifiée. **Aucun** fichier UI touché.

### 11. Classification des contraintes — **aucun défaut**

Les tests de course provoquent une **vraie** violation de l'index SQLite (écriture réelle rejetée par la base),
et non une `PersistenceException` lancée artificiellement.

- `UniqueConstraint` → `UsernameTaken` (message métier stable, identique à la garde pré-écriture) ;
- **aucun** texte EF/SQLite exposé — vérifié par assertion négative (`SQLITE`, `constraint failed`) ;
- une catégorie **autre** que `UniqueConstraint` n'est **jamais** traduite en doublon de login : le filtre `when`
  ne capture que cette catégorie, prouvé par `OtherConstraintViolation_IsNotReportedAsDuplicateUsername` (une
  violation de FK remonte en `ConstraintViolation`, propagée) ;
- toute autre erreur est propagée conformément aux frontières existantes.

**Honnêteté du périmètre** : après P3-10, `Users` ne possède qu'**un seul** index unique métier
(`NormalizedUsername`). Aucune distinction entre plusieurs index uniques `User` n'est revendiquée — il n'y en a
pas d'autre.

### 12. Neutralisation exhaustive des comptes faibles — **défaut trouvé et corrigé**

Testé sur base réellement migrée P3-10.

**Sans secret bootstrap** (`admin`, `marie.optic`, `pierre.tech`, `sophie.optic`, tous actifs, hash faible
connu) : les quatre comptes sont **conservés**, les quatre deviennent **inactifs**, aucun hash n'est modifié
arbitrairement, aucun compte supplémentaire n'est créé, second passage **idempotent**.

**Avec secret bootstrap** : un seul compte canonique `admin`, `NormalizedUsername = admin`, actif, hash BCrypt
WF11 vérifiable et non faible ; les trois autres comptes faibles **inactifs** ; **aucun secret** dans
`SeedResult`, ses notes, les logs ou les exceptions ; second passage **idempotent**.

**Comptes à préserver** (administrateur, opticien, technicien réels au hash fort, plus un compte inactif au hash
fort) : aucun n'est désactivé, rehashé ni renommé ; aucune valeur de profil n'est modifiée.

**Défaut trouvé (D3).** La sélection du compte canonique retombait sur `weakAccounts.FirstOrDefault()` — un
compte faible **arbitraire** — puis le **renommait** vers le login bootstrap. Sur une base portant un
administrateur **réel** sous `admin` et un compte faible sous un autre login, ce renommage entrait en collision
avec l'index unique P3-10 : `UNIQUE constraint failed: Users.NormalizedUsername`, levée **pendant le seed de
démarrage** — l'application ne démarrait plus. Et la branche « administrateur réel déjà présent » retournait
**sans neutraliser** les comptes faibles restants, les laissant **actifs** en production avec des mots de passe
publiquement connus (R4 non couvert sur cette branche).

**Correction.** Le repli « premier compte faible venu » ne s'applique plus que si le login bootstrap est
**libre** ; la neutralisation est **ajoutée** à la branche « administrateur réel déjà présent ». Aucune logique
de fusion n'a été introduite dans le seed. Les variantes de casse du compte canonique (`Admin`, `admin`) sont
rendues non ambiguës par la clé normalisée, et les collisions historiques sont refusées **par la migration**,
donc avant que le seed ne s'exécute.

### 13. Politique de mot de passe et rôles — reconfirmés, aucun défaut

Aucune règle rouverte (aucun défaut concret trouvé). Reconfirmé : Create faible → **aucun hash** ; Update faible
→ **aucune mutation** ; ChangePassword faible → **hash inchangé** ; rôle absent → refus ; rôle hors enum →
refus ; `Admin` explicite reste accepté ; **aucune omission ne crée `Admin`** ; aucune autorisation par acteur
n'est prétendue ; **aucun fichier UI modifié**.

### 14. Tests ajoutés par la revue (6)

| Test | Sujet | A révélé |
|---|---|---|
| `FreshDatabase_NormalizedUsername_HasNoPermanentDefault` | `dflt_value` physique | **D1** |
| `FreshDatabase_RawInsertOmittingNormalizedUsername_FailsNotNull` | preuve SQL brute | **D1** |
| `Down_OnValidDatabase_RestoresLegacyIndex_KeepsAllData_ThenUpReproducesNormalizedKeys` | `Down` + rejeu `Up` | affirmation documentaire fausse |
| `Create_ConcurrentRace_RejectedEntity_IsDetached_UnrelatedLaterSaveSucceeds` | tracker après Create refusé | **D2** |
| `Update_ConcurrentRace_RejectedEntity_IsDetached_FindAsyncReturnsFreshValue` | tracker après Update refusé | **D2** |
| `Production_WithBootstrapSecret_RealAdminAlreadyOwnsBootstrapLogin_DoesNotCollideOrRenameArbitraryWeakAccount` | seed multi-comptes | **D3** |

### 15. Total final exact

| | Avant revue | Après revue | Δ |
|---|---:|---:|---:|
| Domain | 589 | **593** | +4 |
| Application | 579 | **581** | +2 |
| App (UI) | 239 | **239** | **0** |
| **Total** | **1407** | **1413** | **+6** |

0 échec, 0 ignoré. `MMV.App.Tests` reste **exactement** à 239 : aucune UI modifiée.

---

## Préparation du commit

| Élément | Valeur |
|---|---|
| HEAD de départ | `057df0f3fad91f344a6333f3a52f32b6d1552ba1` |
| CI de départ | run `29831430629` — `push` / `completed` / **`success`** sur ce SHA exact |
| Build final | `dotnet build MMV.sln --no-restore -c Debug` → **0 erreur / 0 avertissement** |
| Domain | **593** (0 échec, 0 ignoré) |
| Application | **581** (0 échec, 0 ignoré) |
| App (UI) | **239** (0 échec, 0 ignoré) — inchangé |
| **Total** | **1413** |
| Vulnérabilités | **aucune** (7 projets, `--include-transitive`) |
| État EF | `has-pending-model-changes` → *No changes have been made to the model…* |
| Migration | **une seule** migration P3-10 `AddNormalizedUsernameAndSecureLocalUsers` ; aucune ancienne migration modifiée |
| Frontières | `MMV.Application` → `MMV.Domain` **seul** (+ pkg `DI.Abstractions` 8.0.1, inchangé) |
| UI | **aucun** fichier sous `src/MMV.App/**` |
| `git diff --check` | propre |

**Périmètre exact du commit** : 12 fichiers créés (10 code/test + 2 rapports) et 24 fichiers modifiés
(17 production, 6 tests, 1 roadmap). **Hors commit et non touchés** : `design-handoff/`, `design/`, `docs/ui/`.

**Verdict : `P3-10 = GO LOCAL`** — sous réserve de la CI verte du futur commit.

---

## 36. Verdict

**P3-10 = GO LOCAL**

| Condition | État |
|---|---:|
| Politique appliquée avant hachage sur Create, Update, ChangePassword | ✅ |
| Aucune écriture pour un mot de passe faible | ✅ |
| Aucun clair persisté | ✅ |
| `NormalizedUsername` rempli et unique | ✅ |
| Variantes de casse ⇒ même login | ✅ |
| Course d'unicité ⇒ erreur métier | ✅ |
| Migration échoue sûrement sur collision ou donnée historique invalide | ✅ |
| Rôle explicitement validé | ✅ |
| Aucune omission ne crée `Admin` | ✅ |
| BCrypt WF11 inchangé | ✅ |
| Compte inactif non authentifiable | ✅ |
| Tous les comptes faibles connus neutralisés en production | ✅ |
| Aucune suppression `User` ajoutée | ✅ |
| Tous les tests passent (1407, 0 échec, 0 ignoré) | ✅ |
| Aucune vulnérabilité | ✅ |
| Aucune migration en attente | ✅ |
| Rapport `.md` présent et complet | ✅ |
| Aucune UI modifiée | ✅ |

Aucun commit. Aucun push. Aucune UI. **STOP.**
