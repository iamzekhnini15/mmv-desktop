# Contribuer à ManageMyVision

Ce guide s'adresse à toute personne qui modifie le code de MMV. Il couvre aujourd'hui **le modèle EF Core et
ses migrations**. Dans cette zone, une erreur passe facilement inaperçue et coûte cher : une migration oubliée
sur une chaîne ne casse aucun test fonctionnel, et elle n'apparaît qu'à l'installation d'un serveur.

Vous n'avez pas besoin de lire les ADR pour appliquer ce guide. Ils sont cités en fin de document, pour qui
veut les raisons détaillées.

> **Toutes les commandes se lancent depuis la racine du dépôt.**

---

## Prérequis

| Outil | Version | Obtention et vérification |
|---|---|---|
| SDK .NET | 8.0, fixé par [`global.json`](global.json) (`8.0.417`, `rollForward: latestFeature`) | `dotnet --version` doit afficher `8.0.4xx` |
| `dotnet-ef` | **8.0.27**, verrouillé par [`.config/dotnet-tools.json`](.config/dotnet-tools.json) | `dotnet tool restore`, puis `dotnet ef --version` doit afficher `8.0.27` |

Les commandes marquées `--no-build` réutilisent les binaires Debug. Il faut donc compiler avant, et recompiler
après chaque modification, sinon EF lit l'ancien modèle :

```bash
dotnet tool restore
dotnet build MMV.sln -c Debug
```

---

## Un modèle, deux chaînes de migrations

MMV a **un seul modèle EF** : `OpticDbContext` et les configurations de
[`src/MMV.Infrastructure/Data/Configurations/`](src/MMV.Infrastructure/Data/Configurations/). Il a en revanche
**deux chaînes de migrations**, une par moteur, qui ne se mélangent jamais.

Les 14 migrations SQLite contiennent des opérations propres à SQLite (reconstructions de table, `GLOB`, filtres
entiers). Elles ne peuvent pas être rejouées sur PostgreSQL. Et EF ne gère qu'un instantané par assembly de
migrations. La chaîne PostgreSQL vit donc dans sa propre assembly. Elle part d'une baseline générée depuis le
modèle actuel.

**Quelle chaîne ?**

| | Chaîne SQLite | Chaîne PostgreSQL |
|---|---|---|
| Usage | développement, tests, démonstration, mono-poste local | serveur multi-poste. En préparation : l'application refuse encore de démarrer sur PostgreSQL |
| Dossier des migrations | `src/MMV.Infrastructure/Migrations/` | `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/` |
| `--project` **et** `--startup-project` | `src/MMV.Infrastructure` | `src/MMV.Infrastructure.PostgreSQL.Migrations` |
| `MMV_DESIGNTIME_DATABASE_PROVIDER` | **absente** | `postgresql` |
| Début de la chaîne | `20260127184542_InitialCreate` (14 migrations historiques) | `20260922001219_InitialPostgreSqlBaseline` (baseline unique) |
| Step CI | *Check EF Core pending model changes* | *Check EF Core pending model changes (PostgreSQL chain)* |
| Test qui énumère la chaîne | `MigrationChainsTests.SqliteChain_IsExactlyThe14HistoricalMigrations_InOrder` | `MigrationChainsTests.PostgreSqlChain_IsExactlyTheBaseline` |

Deux règles de structure en découlent :

- **Aucune migration PostgreSQL dans `MMV.Infrastructure`.** Et `MMV.Infrastructure` ne référence jamais le
  projet PostgreSQL : la référence serait circulaire. `MMV.Domain` et `MMV.Application` ne le référencent pas
  non plus.
- **Le projet PostgreSQL ne contient que** ses migrations, son instantané et la factory de délégation décrite
  plus bas. Aucune configuration d'entité, aucun repository, aucun service.

---

## Comment `dotnet ef` choisit la chaîne

### `OpticDbContextFactory` : la seule logique design-time

[`src/MMV.Infrastructure/Data/OpticDbContextFactory.cs`](src/MMV.Infrastructure/Data/OpticDbContextFactory.cs)
construit le contexte pour `dotnet ef`, sur les deux chaînes. Elle ne lit **qu'une** variable :

| Valeur de `MMV_DESIGNTIME_DATABASE_PROVIDER` | Résultat |
|---|---|
| absente ou vide | **SQLite**, chaîne de `MMV.Infrastructure`. La factory résout le chemin de la base locale (`MMV_DATABASE_PATH`, sinon `%LOCALAPPDATA%\ManageMyVision\mmv.db`) et crée son dossier s'il manque. Elle n'ouvre pas la base |
| `sqlite` | SQLite, comme si la variable était absente |
| `postgresql`, ou les alias `postgres` et `npgsql` (casse et espaces ignorés) | **PostgreSQL** : Npgsql, assembly `MMV.Infrastructure.PostgreSQL.Migrations`, chaîne de connexion factice constante `Host=mmv-design-time.invalid;Database=mmv_design_time`. Aucun fichier SQLite résolu ni créé |
| toute autre valeur | **erreur** : `Valeur invalide dans MMV_DESIGNTIME_DATABASE_PROVIDER…`. La valeur reçue n'est jamais recopiée dans le message |

La chaîne de connexion PostgreSQL ne porte ni utilisateur ni mot de passe. Son hôte appartient au domaine
réservé `.invalid`, qui ne se résout jamais. Aucune commande design-time ne peut donc atteindre un serveur, pas
même un PostgreSQL installé sur votre poste.

La factory **ne lit jamais** les variables de l'application (`MMV_DATABASE_PROVIDER`,
`MMV_DATABASE_CONNECTION_STRING`). À l'inverse, l'application ignore `MMV_DESIGNTIME_DATABASE_PROVIDER`.
Conséquence : poser `MMV_DATABASE_PROVIDER=postgresql` ne sélectionne **pas** la chaîne PostgreSQL.

### `OpticDbContextDesignTimeFactory` : le point d'entrée de la chaîne PostgreSQL

[`src/MMV.Infrastructure.PostgreSQL.Migrations/OpticDbContextDesignTimeFactory.cs`](src/MMV.Infrastructure.PostgreSQL.Migrations/OpticDbContextDesignTimeFactory.cs)
est une classe `internal sealed` d'une ligne. Elle **délègue** à `OpticDbContextFactory`.

Elle existe pour une seule raison. Quand le projet PostgreSQL est son propre projet de démarrage, EF ne cherche
le type de contexte que dans les assemblys cible et de démarrage. Sans cette classe, `OpticDbContext` serait
introuvable tant que la chaîne est vide, par exemple pour régénérer la baseline.

Elle **ne choisit rien** : la chaîne dépend toujours de la variable. Sans variable, une commande dirigée vers le
projet PostgreSQL construit donc un contexte **SQLite** (voir *Les garde-fous, et le piège qui reste*).

Ne lui ajoutez **rien** : ni logique, ni chaîne de connexion, ni `Migrate()`, ni `EnsureCreated()`. Ne créez
pas non plus de seconde factory. `OpticDbContextDesignTimeFactoryTests` vérifie son contenu sur l'IL compilé.

### Les variables d'environnement

| Variable | Lue par | Usage |
|---|---|---|
| `MMV_DESIGNTIME_DATABASE_PROVIDER` | `OpticDbContextFactory` seulement | choisir la chaîne visée par `dotnet ef` |
| `MMV_DATABASE_PROVIDER`, `MMV_DATABASE_CONNECTION_STRING` | l'application seulement | choisir la base au démarrage. **Jamais** pour générer une migration |
| `MMV_DATABASE_PATH` | l'application et la branche SQLite de la factory | chemin du fichier SQLite |
| `MMV_DESIGNTIME_DATABASE_CONNECTION_STRING` | personne | **nom réservé, cette variable n'existe pas.** Ne la créez pas |

**Limiter la variable design-time à la commande.** Ne la posez jamais dans les variables d'environnement de
l'utilisateur ou du système, dans un profil de shell ou dans un fichier `.env`.

PowerShell (Windows PowerShell 5.1 et PowerShell 7). `$env:` dure jusqu'à la fin de la session ; le `finally`
retire la variable, même si la commande échoue :

```powershell
$env:MMV_DESIGNTIME_DATABASE_PROVIDER = 'postgresql'
try {
    dotnet ef migrations add <Nom> `
        --project src/MMV.Infrastructure.PostgreSQL.Migrations `
        --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
}
finally {
    Remove-Item Env:MMV_DESIGNTIME_DATABASE_PROVIDER
}
```

Bash (Git Bash, Linux, macOS). Une variable posée en préfixe n'existe que pour cette commande :

```bash
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations add <Nom> \
  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
```

Sous `cmd.exe`, `set` dure jusqu'à la fermeture de la fenêtre : utilisez PowerShell.

### Les garde-fous, et le piège qui reste

| Erreur | Ce que fait EF |
|---|---|
| variable posée, `migrations add` ou `has-pending-model-changes` visant `src/MMV.Infrastructure` | **refus** : `Could not load file or assembly 'MMV.Infrastructure.PostgreSQL.Migrations'` |
| variable absente, `migrations add` visant le projet PostgreSQL | **refus** : `Your target project 'MMV.Infrastructure.PostgreSQL.Migrations' doesn't match your migrations assembly 'MMV.Infrastructure'` |
| valeur mal orthographiée (`postgre`, `pg`…) | **refus** : `Valeur invalide dans MMV_DESIGNTIME_DATABASE_PROVIDER` |
| variable absente **ou clé mal orthographiée**, `has-pending-model-changes` ou `migrations list` visant le projet PostgreSQL | **aucune erreur.** EF contrôle la chaîne **SQLite** et répond vert : c'est un **faux vert** |

Avant de croire un contrôle PostgreSQL, vérifiez donc la chaîne réellement visée :
`migrations list --no-connect` doit afficher `InitialPostgreSqlBaseline` et **jamais** `InitialCreate`. La CI
fait cette vérification automatiquement, avant son contrôle de dérive PostgreSQL.

---

## Règle de double migration

> **Toute évolution du modèle EF produit, dans le même commit :**
>
> 1. **le modèle validé** : les configurations d'entités. Une construction volontairement dépendante du moteur
>    (type physique, filtre d'index) est choisie **uniquement** dans
>    [`ModelPortability`](src/MMV.Infrastructure/Data/Portability/ModelPortability.cs). Aucune configuration
>    d'entité n'interroge le provider elle-même ;
> 2. **la migration SQLite**, sans variable :
>    `dotnet ef migrations add <Nom> --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure` ;
> 3. **la migration PostgreSQL**, sous le même nom, avec `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` pour
>    cette seule commande :
>    `dotnet ef migrations add <Nom> --project src/MMV.Infrastructure.PostgreSQL.Migrations --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations` ;
> 4. **les deux chaînes vérifiées** : contrôle de dérive vert sur chacune et suite de tests verte. Quand les
>    tests d'intégration PostgreSQL existeront (lot P4-5F), ils devront l'être aussi.
>
> Les formes PowerShell et Bash, et les commandes de contrôle, sont dans *Commandes EF de référence*.

**Exception : changement propre à un seul moteur.** Par exemple, une forme que `ModelPortability` choisit pour
un seul provider. Il ne produit qu'**une** migration. Le commit doit alors **prouver** que l'autre chaîne est
inchangée : son contrôle de dérive reste vert sans nouvelle migration. Le message de commit dit quelle chaîne
est concernée, et pourquoi.

**À respecter aussi**

- **Même nom de migration sur les deux chaînes**, pour la traçabilité. Les horodatages diffèrent : c'est normal.
  Les deux classes vivent dans des espaces de noms distincts et ne se gênent pas.
- **Une migration de données s'écrit pour chaque moteur.** C'est une migration qui reprend des valeurs
  existantes, comme la reprise des dates civiles de P4-5D-R. Son SQL diffère d'un moteur à l'autre.
- **Aucune retouche manuelle** d'une migration générée, de son `.Designer.cs` ou d'un instantané. Seule
  exception : une migration de données, avec une retouche commentée et justifiée. Jamais la baseline
  PostgreSQL.
- **Les migrations existantes sont figées** : les 14 migrations SQLite et la baseline PostgreSQL ne se
  modifient, ne se renomment, ne se déplacent et ne se suppriment jamais. Une correction passe par une
  **nouvelle** migration.
- **Les tests qui énumèrent les chaînes échoueront** tant que vous ne les aurez pas mis à jour. Ce n'est pas un
  faux positif : ils rendent tout ajout visible en revue. `MigrationChainsTests` fige la liste des
  identifiants SQLite et l'identifiant de la baseline. D'autres tests SQLite énumèrent `GetMigrations()`.
  Lancez donc toute la suite.
- **Chaîne SQLite : pensez aux bases historiques.** Une base créée sans `__EFMigrationsHistory` est adoptée
  au démarrage par
  [`SqliteDatabaseManager`](src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs). Il inscrit comme appliquées,
  sans les exécuter, les migrations que le schéma physique reflète déjà. Chaque migration additive depuis
  `AddDocumentSequences` figure dans ce mécanisme : dans les listes d'éléments tolérés de
  [`SqliteSchemaVerifier`](src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs) et, pour la plupart, dans une
  détection par suffixe de `SqliteDatabaseManager`. Évaluez l'effet de votre migration sur ce mécanisme, et
  signalez-le en revue. Ces deux fichiers sont sensibles : leur modification se décide, elle ne s'improvise pas.

---

## Commandes EF de référence

**Passez toujours `--project` et `--startup-project`.** Sans `--startup-project`, `dotnet ef` prend comme projet
de démarrage **le projet du dossier courant**. Depuis la racine, qui ne contient aucun projet, il se rabat sur
`--project`. Mais lancé depuis `src/MMV.App`, il prendrait l'application (voir *Interdits*).

Les commandes de création, de liste, de contrôle et de script ne se connectent à aucune base. Seul
`migrations remove` le tente (voir *Annuler une migration pas encore fusionnée*).

### Chaîne SQLite (sans variable)

```bash
# Créer une migration
dotnet ef migrations add <Nom> \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure

# Lister la chaîne, sans ouvrir de base
dotnet ef migrations list --no-connect \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build

# Contrôle de dérive : même contrôle que la CI
dotnet ef migrations has-pending-model-changes \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build

# Script SQL de revue : de <Précédente> (exclue) à <Nom> (incluse). Hors dépôt, jamais exécuté
dotnet ef migrations script <Précédente> <Nom> \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build \
  --output <chemin hors dépôt>.sql
```

La CI lance le contrôle SQLite sans `--startup-project`. C'est équivalent, parce qu'elle part de la racine.

### Chaîne PostgreSQL (variable `postgresql`)

PowerShell :

```powershell
$env:MMV_DESIGNTIME_DATABASE_PROVIDER = 'postgresql'
try {
    $pg = 'src/MMV.Infrastructure.PostgreSQL.Migrations'

    # Créer une migration (même nom que côté SQLite)
    dotnet ef migrations add <Nom> --project $pg --startup-project $pg

    # Vérifier la chaîne visée : InitialPostgreSqlBaseline présente, InitialCreate absente
    dotnet ef migrations list --no-connect --project $pg --startup-project $pg --no-build

    # Contrôle de dérive
    dotnet ef migrations has-pending-model-changes --project $pg --startup-project $pg --no-build

    # Script SQL de revue. Hors dépôt, jamais exécuté
    dotnet ef migrations script <Précédente> <Nom> --project $pg --startup-project $pg --no-build `
        --output <chemin hors dépôt>.sql
}
finally {
    Remove-Item Env:MMV_DESIGNTIME_DATABASE_PROVIDER
}
```

Bash :

```bash
# La variable est posée en préfixe de CHAQUE commande : elle n'existe que pour celle-ci.
PG=src/MMV.Infrastructure.PostgreSQL.Migrations

MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations add <Nom> --project $PG --startup-project $PG
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations list --no-connect --project $PG --startup-project $PG --no-build
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations has-pending-model-changes --project $PG --startup-project $PG --no-build
```

Ces blocs listent les commandes : ils ne forment pas une séquence. Après un `migrations add`, relancez
`dotnet build MMV.sln -c Debug` avant toute commande `--no-build`, sinon la nouvelle migration n'est pas vue.

### Annuler une migration pas encore fusionnée

**Après fusion, n'annulez jamais** une migration : corrigez par une nouvelle migration.

**Avant commit (méthode recommandée)** : supprimez les deux fichiers générés, `<horodatage>_<Nom>.cs` et
`<horodatage>_<Nom>.Designer.cs`, puis restaurez l'instantané de la chaîne concernée :

```bash
git restore src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs
git restore src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/OpticDbContextModelSnapshot.cs
```

Le retour est exact. Aucune commande EF n'est lancée, aucune base n'est lue.

**`dotnet ef migrations remove`** fonctionne aussi avant fusion, en particulier si la migration est déjà
commitée sur une branche non fusionnée. Trois précautions :

- l'instantané est réécrit avec des écarts de forme, par exemple `ToTable("Customers", (string)null)` au lieu
  de `ToTable("Customers")`. Relisez `git diff` et restaurez ce qui ne vient pas de vous ;
- **chaîne SQLite** : sans option. EF ouvre la base locale design-time, si elle existe, pour vérifier que la
  migration n'y est pas appliquée. **N'utilisez jamais `--force` sur SQLite** : si la migration est appliquée,
  EF la défait dans votre base locale ;
- **chaîne PostgreSQL** : avec la variable, **`--force` est nécessaire**. Sans lui, EF tente de se connecter
  pour faire cette vérification, et l'hôte design-time ne se résout pas (`Hôte inconnu`). Avec `--force`, il
  affiche un avertissement et ne retire que les fichiers.

---

## Interdits

| # | Interdit | Pourquoi |
|---|---|---|
| 1 | **`--startup-project src/MMV.App`**, ou `dotnet ef` lancé depuis `src/MMV.App` sans `--startup-project` | EF **exécute le point d'entrée de l'application** pour y chercher un hôte. La fenêtre Avalonia s'ouvre, et la base SQLite locale est réellement préparée : installation, migrations, journal dans `%LOCALAPPDATA%\ManageMyVision\`. L'incident a été constaté. Et la chaîne PostgreSQL ne peut pas être générée ainsi |
| 2 | **`dotnet ef database update`** et **`database drop`**, sur toute chaîne | SQLite : l'application prépare la base locale au démarrage (`SqliteDatabaseManager` : sauvegarde, adoption des bases historiques, journal) ; `database update` contournerait tout cela. PostgreSQL : la façon d'appliquer les migrations n'est pas encore décidée (lot P4-6). La commande échoue faute de serveur, et c'est voulu |
| 3 | Modifier, renommer, déplacer ou supprimer une migration existante ; retoucher une migration générée | un instantané et sa migration ne sont cohérents que par construction. Voir *Règle de double migration* |
| 4 | `migrations remove` après fusion | une migration fusionnée peut déjà être appliquée ailleurs |
| 5 | Migration PostgreSQL dans `MMV.Infrastructure`, ou référence de `MMV.Infrastructure` vers le projet PostgreSQL | les deux chaînes se mélangeraient ; la référence serait circulaire |
| 6 | Modifier `OpticDbContextDesignTimeFactory`, ou créer une autre factory design-time | il doit rester **un seul** chemin de génération : `OpticDbContextFactory` |
| 7 | Une chaîne de connexion réelle ou un secret dans le dépôt, dans la CI ou dans une variable design-time | la seule chaîne autorisée est la constante factice de la factory |
| 8 | `MMV_DESIGNTIME_DATABASE_PROVIDER` posée durablement (variables utilisateur ou système, profil, `.env`) ou au niveau d'un job CI | la variable ne doit exister que pour une commande, ou pour un seul step CI. Posée au niveau du job, elle ferait échouer le step SQLite |
| 9 | Un test du provider dans une configuration d'entité | le choix se fait en **un seul point**, `ModelPortability` |

---

## Procédure de revue avant commit

L'auteur l'applique avant de committer. Le relecteur la refait. Chaque point doit être vrai.

```bash
dotnet tool restore
dotnet build MMV.sln -c Debug      # 0 erreur
```

| # | Contrôle | Comment |
|---|---|---|
| 1 | **Fichiers** : seuls les dossiers de migrations attendus ont changé | `git status`, puis `git diff --stat -- src/MMV.Infrastructure/Migrations/ src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/`. Attendu, par chaîne : 2 fichiers nouveaux et l'instantané modifié. **Aucune** migration existante modifiée |
| 2 | **Noms** : même nom de migration sur les deux chaînes | sinon, l'exception « un seul moteur » est justifiée dans le message de commit |
| 3 | **SQLite** : la nouvelle migration clôt la chaîne ; la dérive est verte | `migrations list --no-connect` puis `has-pending-model-changes` (commandes SQLite) |
| 4 | **PostgreSQL** : la chaîne commence par `InitialPostgreSqlBaseline`, se termine par la nouvelle migration, ne contient **aucun** `InitialCreate` ; la dérive est verte | mêmes commandes, avec la variable |
| 5 | **SQL relu** sur chaque chaîne | script incrémental `<Précédente> <Nom>`. Côté PostgreSQL : colonnes monétaires en `numeric(12,2)`, jamais `real` ; filtres d'index attendus ; comportement des clés étrangères. Le script reste hors dépôt et n'est jamais exécuté |
| 6 | **Tests** : tout est vert, **0 ignoré** | `dotnet test MMV.sln --no-build -c Debug`. `MigrationChainsTests` est à jour des nouveaux identifiants |
| 7 | **Portabilité** : aucun test du provider hors `ModelPortability`, aucun type propre à un moteur sans justification écrite | relecture du diff des configurations |
| 8 | **Données** : une migration de données existe pour chaque moteur | relecture |
| 9 | **Bases historiques SQLite** : effet sur l'adoption par `SqliteDatabaseManager` évalué | relecture ; signalé en revue |
| 10 | **Secrets** : aucune chaîne de connexion ni aucun mot de passe ajouté | relecture de `git diff --cached` |
| 11 | **Un seul commit** : modèle, deux migrations et tests ensemble | `git show --stat` |
| 12 | **Après le push** : les deux contrôles de dérive de la CI sont verts sur ce commit | onglet *Actions* |

---

## Dépannage

| Symptôme | Cause probable | Correction |
|---|---|---|
| `migrations list` sur le projet PostgreSQL affiche `…_InitialCreate` et 14 migrations | variable absente, ou clé mal orthographiée | poser `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` pour la commande |
| `Your target project 'MMV.Infrastructure.PostgreSQL.Migrations' doesn't match your migrations assembly 'MMV.Infrastructure'` | même cause, sur `migrations add` | idem |
| `Could not load file or assembly 'MMV.Infrastructure.PostgreSQL.Migrations'` | variable posée, commande visant `src/MMV.Infrastructure` | retirer la variable : `Remove-Item Env:MMV_DESIGNTIME_DATABASE_PROVIDER` |
| `Valeur invalide dans MMV_DESIGNTIME_DATABASE_PROVIDER` | faute de frappe sur la valeur | `postgresql` |
| `Your startup project '…' doesn't reference Microsoft.EntityFrameworkCore.Design` | `--startup-project` omis, commande lancée depuis un dossier de projet | relancer depuis la racine, avec `--startup-project` |
| la fenêtre de l'application s'ouvre pendant une commande `dotnet ef` | `MMV.App` pris comme projet de démarrage | fermer l'application et relancer avec le bon `--startup-project`. Le signaler en revue : la base locale a pu être créée ou migrée |
| `Hôte inconnu` sur la chaîne PostgreSQL (le texte dépend de la langue du poste) | commande qui tente une connexion, par exemple `migrations remove` sans `--force` | voulu : aucune connexion n'est prévue. Voir *Annuler une migration pas encore fusionnée* |
| `Changes have been made to the model since the last migration` | modèle modifié sans migration sur cette chaîne, ou binaires `--no-build` périmés | recompiler ; si le message reste, générer la migration manquante |

---

## Références

- Architecture des migrations : [ADR-PROD-DB-005](docs/architecture/adr-prod-db-005-migration-architecture.md)
- Portabilité du modèle : [ADR-PROD-DB-006](docs/architecture/adr-prod-db-006-index-and-model-portability.md)
- Prévention de la dérive : [ADR-PROD-DB-007](docs/architecture/adr-prod-db-007-schema-drift-prevention.md)
- Décisions d'exécution (D-01, D-03 à D-06, D-19) : [P4-5E-B](docs/implementation/P4-5E-B-architecture-decisions.md)
- Contrôles CI : [`.github/workflows/ci.yml`](.github/workflows/ci.yml)
- Vue d'ensemble : [ARCHITECTURE.md](ARCHITECTURE.md)
