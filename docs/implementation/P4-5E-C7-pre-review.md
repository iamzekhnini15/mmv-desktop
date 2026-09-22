# P4-5E-C — C7 : contrôle CI de la chaîne PostgreSQL — PRÉ-REVUE

> **Statut : C7 FAIT LOCALEMENT. PRÉ-REVUE, AUCUN COMMIT, AUCUN PUSH.**
> Reprise après la validation architecte de P4-5E-C (Q1, Q2, Q4, Q5, Q6 et Q8 validées). Périmètre :
> **C7 uniquement**. Un step est ajouté à la fin de `ci.yml`. Il porte la variable design-time, vérifie la
> chaîne réellement listée, puis contrôle la dérive du modèle. Rien d'autre n'est modifié.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> Ce document **complète** [la pré-revue v2](P4-5E-C-pre-review-v2.md), qui reste la référence pour C1 à C6.
> **Le dépôt réel prime sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `EXÉCUTÉ` | commande exécutée sur le dépôt, résultat observé |
| `HARNAIS LOCAL` | le step est lu dans `ci.yml` par un parseur YAML, puis exécuté **sur le poste** avec l'enveloppe `pwsh` de GitHub Actions (§6.1). **Ce n'est pas une exécution sur le runner** |
| `MUTATION — HORS DÉPÔT` | exécuté sur une copie jetable, hors du dépôt, supprimée ensuite. **Rien n'en a été reporté** |
| `STATIC_CODE_PROOF` | constaté par lecture du code |

---

## 1. Résumé exécutif

- **Un seul step ajouté** à la fin du job, après le step SQLite : `+55 / −0` lignes, un seul bloc de diff. Les
  9 steps existants sont **identiques à HEAD** une fois le YAML analysé. La variable design-time apparaît **une
  seule fois** dans le fichier, dans l'`env:` de ce step (D-05.6).
- **Q2 appliquée** : sous le même `env:`, le step lance d'abord `migrations list --no-connect`. Il exige la
  baseline PostgreSQL et **refuse** `InitialCreate`. Il lance ensuite `has-pending-model-changes`.
- **Démonstration exigée par C7, faite** (`MUTATION — HORS DÉPÔT`) : `Customer.Phone` `HasMaxLength(20)` → `25`.
  Le step PostgreSQL devient **rouge**. Le step SQLite, lancé sur la même copie, reste **vert**.
- **Faux vert fermé** (`HARNAIS LOCAL`) : variable absente, clé mal orthographiée, valeur `sqlite`, variable
  runtime à la place de la variable design-time : **les quatre cas sont rouges**. Une valeur inconnue est rouge,
  et le message de la factory apparaît dans le journal.
- **Résultats identiques** sur Windows PowerShell 5.1, PowerShell 7.4.20 et PowerShell 7.6.6 : 12 scénarios,
  3 interpréteurs.
- **Non prouvé** : « CI verte sur le SHA exact », car cela exige un push. Ce critère de C7 reste **ouvert**.
- **Tests** : 1953 / 1953 verts, 0 ignoré, inchangé depuis la v2. C7 n'ajoute aucun test (Q10).

---

## 2. Décisions appliquées

| Décision validée | Application dans C7 | Preuve |
|---|---|---|
| **Q2** : variable obligatoire | `env: MMV_DESIGNTIME_DATABASE_PROVIDER: postgresql` **sur ce step seul** | §5 |
| **Q2** : vérification `migrations list --no-connect` | commande (1) du step, sortie JSON officielle d'EF (`--json --prefix-output`) | §6.2, T1 à T6 |
| **Q2** : refuser `InitialCreate` SQLite | assertion explicite sur le champ `name` | §6.2, U1 |
| **D-05.6** : `env:` limité au step | aucun `env:` au niveau du workflow ni du job | §5 |
| **D-06.6** : step SQLite non modifié | step identique à HEAD après analyse | §5 |
| Q4, Q5, Q8 | **hors périmètre de C7**, rien à faire | — |
| Q6 (`CONTRIBUTING.md`, `ARCHITECTURE.md`) | **C8, non commencé** | — |

**Écart de lettre, déjà validé par Q2** : C7 prévoyait « un seul step… `has-pending-model-changes` ». Il y a
toujours **un seul step**, mais il contient deux commandes `dotnet ef` sous le même `env:`. C'est la forme
proposée en v2 (§10) et validée.

**Choix de réalisation à valider**

| Choix | Raison |
|---|---|
| `shell: pwsh` explicite | même idiome que le step d'audit du même fichier ; la sémantique du script ne dépend plus du shell par défaut du runner |
| JSON (`--json --prefix-output`) plutôt qu'une recherche de texte | format documenté par EF pour l'analyse machine ; même principe que le step d'audit (« on ne se fie plus à un grep de texte ») |
| comparaison sur `name`, pas sur l'identifiant complet | survit à une régénération avant fusion (D-03.5, horodatage changé) **sans modifier `ci.yml`**. L'identifiant exact est déjà figé par le test `PostgreSqlChain_IsExactlyTheBaseline` |
| « contient la baseline », pas « est exactement la baseline » | les migrations PostgreSQL futures passent **sans modifier `ci.yml`** (U5) |
| contrôle explicite de `$LASTEXITCODE` après **chaque** commande | sous PowerShell 7.4 et 7.6, `$PSNativeCommandUseErrorActionPreference` vaut `False` (`EXÉCUTÉ`) : l'échec d'une commande native n'arrête pas le script. Sans ce contrôle, un échec de `migrations list` passerait à la suite |
| les lignes hors `data:` sont renvoyées au journal | ajouté **pendant** C7, après le scénario T4 : l'erreur de la factory partait sur stdout avec `--prefix-output`, était captée par le script et **n'apparaissait pas** dans le journal |
| messages sans accents | même convention que le step d'audit |
| step placé **après** le step SQLite | le contrôle SQLite s'exécute en premier, à l'identique |

---

## 3. SHA avant / après

| | Valeur |
|---|---|
| HEAD | `440ddcb352b12e0c6900cfd5d6df550e234856c5`. **Aucun commit.** |
| blob `ci.yml` avant | `009c28577924a5672eaf697c739a62602acb0c89`, identique à `HEAD:.github/workflows/ci.yml` |
| blob `ci.yml` après | `904475410806c4b44d17a1e1399e6f9977467516`, calculé par `git hash-object`, donc après normalisation LF |
| SHA-256 du fichier de travail | avant `238de02d…c84c`, après `3189a7bb…ed9f`, en CRLF |

---

## 4. Fichiers

| Fichier | Nature | Lignes |
|---|---|---|
| [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml#L120-L173) | **un** step ajouté ; un seul bloc `@@ -118,0 +119,55 @@` | 118 → 173 (+55, −0) |
| `docs/implementation/P4-5E-C7-pre-review.md` | ce rapport | — |

Depuis la v2, **aucun autre fichier** n'a changé (`git status`, `EXÉCUTÉ`).

**Zones interdites intactes** (`EXÉCUTÉ`, chemins réels suivis par git) : `git diff 440ddcb` est vide sur
`src/MMV.Infrastructure/Migrations/` (29 fichiers), `SqliteDatabaseManager.cs`, `App.axaml.cs`,
`OpticDbContext.cs`, `Data/Configurations/`, `Data/Portability/`, `Data/Time/` et `ARCHITECTURE.md`. Aucun
fichier `adr*` n'est modifié. Aucun paquet NuGet nouveau.

**Hors dépôt, non versionné** : le harnais (`c7_harness.py`), PyYAML 6.0.3 installé dans le dossier de travail
temporaire, et les exécutables PowerShell 7 portables (supprimés).

---

## 5. Contrôles structurels (`EXÉCUTÉ`, PyYAML 6.0.3)

| Contrôle | Résultat |
|---|---|
| YAML valide | **OUI** |
| steps : HEAD 9, maintenant 10 ; les 9 premiers **identiques** à HEAD une fois analysés | **OUI** |
| clés du workflow (`on`, `permissions: contents: read`, `name`) et du job (`runs-on: windows-latest`, `name`) identiques | **OUI** |
| `env:` au niveau du workflow / du job | **aucun** / **aucun** |
| steps portant `MMV_DESIGNTIME_DATABASE_PROVIDER` | **un seul** : le nouveau step. **1** occurrence dans le fichier |
| step SQLite | `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build`, **inchangé** |
| `secrets.`, `Host=`, `Password`, `MMV_DATABASE_CONNECTION_STRING` dans le step | **aucun** |
| fins de ligne | 173 CRLF sur 173, aucune tabulation : convention du fichier conservée |

Le step ajouté figure en annexe A.

---

## 6. Démonstrations locales

### 6.1 Méthode

Le harnais lit `ci.yml` avec PyYAML, extrait le `run:` et l'`env:` du step, et reproduit l'enveloppe documentée
de GitHub Actions pour `shell: pwsh` :
- il ajoute `$ErrorActionPreference = 'stop'` en tête ;
- il ajoute `if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE }` en fin ;
- il lance `pwsh -command ". '<script>'"` depuis la racine du dépôt.

Les variables `MMV_*` du poste sont retirées avant d'appliquer l'`env:` du step. Au préalable :
`dotnet build MMV.sln -c Debug` (0 avertissement, 0 erreur) et `dotnet tool restore` (`dotnet-ef` 8.0.27).

**Interpréteurs.** Windows PowerShell 5.1.26100, installé sur le poste. PowerShell **7.4.20** et **7.6.6** en
zip portable officiel, tiré des *releases* GitHub. Chaque zip a été vérifié par SHA-256 contre le
`hashes.sha256` de sa release (7.4.20 `fb88cd37…2d40` et 7.6.6 `02fe458b…c860` : concordants). Ils ont été
extraits dans le dossier de travail temporaire, puis **supprimés**. La version exacte de `pwsh` sur
`windows-latest` n'est pas connue localement. Les deux lignes 7.4 et 7.6 sont couvertes.

### 6.2 Résultats, identiques sur les trois interpréteurs

| # | Scénario | Ce qu'il simule | Code | Message décisif |
|---|---|---|---|---|
| **T1** | `env:` tel que dans `ci.yml` | nominal | **0** | `Chaine controlee : 1 migration(s).` · `20260922001219_InitialPostgreSqlBaseline` · `No changes have been made to the model since the last migration.` |
| **T2** | `env:` retiré | step copié sans son `env:` | **1** | 14 migrations SQLite listées · `Baseline 'InitialPostgreSqlBaseline' absente` |
| **T3** | clé `MMV_DESIGNTIME_DATABASE_PROVIDR` | faute de frappe sur la clé | **1** | idem T2 |
| **T4** | valeur `postgre` | faute de frappe sur la valeur | **1** | `error: … Valeur invalide dans MMV_DESIGNTIME_DATABASE_PROVIDER. Valeurs acceptées : …`, puis `Echec de 'dotnet ef migrations list' (code 1)`. La valeur reçue **n'est pas** restituée (D-05.3) |
| **T5** | valeur `sqlite` | mauvaise chaîne demandée | **1** | idem T2 |
| **T6** | `MMV_DATABASE_PROVIDER=postgresql` seule | variable runtime utilisée à tort | **1** | idem T2 : la factory ignore la variable runtime (D-05.1) |
| **U1** | *stub* : baseline + `InitialCreate` | chaîne mêlée | **1** | `Migration SQLite 'InitialCreate' presente` |
| **U2** | *stub* : `[]` | chaîne vide | **1** | `Baseline … absente` |
| **U3** | *stub* : aucune ligne `data:` | sortie inattendue | **1** | `Sortie JSON vide` |
| **U4** | *stub* : JSON tronqué | sortie corrompue | **1** | `JSON de 'migrations list' invalide` |
| **U5** | *stub* : baseline + une migration PostgreSQL ultérieure | chaîne future | **0** | la suite du step s'exécute normalement |
| **M1** | copie mutée, `env:` nominal | **dérive sans migration PostgreSQL** | **1** | chaîne vérifiée (1 migration), puis `Changes have been made to the model since the last migration. Add a new migration.` |

**Stubs U1 à U5** (`MUTATION — HORS DÉPÔT`) : sur une copie temporaire du script, seule la commande
`migrations list` est remplacée par une fonction qui renvoie un contenu fixé. Le reste du step est inchangé.
C'est le seul moyen d'exercer la seconde assertion : dans les cas réels T2 à T6, la première assertion échoue
avant elle.

**Mutation M1** (`MUTATION — HORS DÉPÔT`) : copie de `src/`, `.config/`, `global.json` et
`Directory.Build.props` dans le dossier de travail temporaire. `Customer.Phone` `HasMaxLength(20)` → `25`
([CustomerConfiguration.cs:24-25](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs#L24-L25)),
puis build Debug de la copie (0 avertissement, 0 erreur). La copie est **supprimée**, et le fichier du dépôt
n'a pas de diff.

| Même copie mutée | Code |
|---|---|
| **M1** — step PostgreSQL (C7) | **1**, rouge |
| **M2** — step SQLite, commande inchangée | **0**, vert |

C'est la démonstration exigée par C7 : le nouveau step attrape une dérive que le step SQLite laisse passer. Elle
reproduit S5 (v1) et le test `PostgreSqlChain_HasNoPendingModelChanges` (v2 §7.3), cette fois au niveau du step
CI.

### 6.3 Observation : D-05.6 échoue bruyamment

Si la variable était posée au niveau du job, le step SQLite la recevrait aussi. Simulation (`EXÉCUTÉ`, sur le
dépôt et sur la copie mutée) : `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations
has-pending-model-changes --project src/MMV.Infrastructure --no-build` donne **code 1** : `Could not load file or
assembly 'MMV.Infrastructure.PostgreSQL.Migrations'`.

Une violation de D-05.6 rendrait donc le step SQLite **rouge**, et non faussement vert. La raison est
structurelle (`STATIC_CODE_PROOF`) : le projet PostgreSQL référence `MMV.Infrastructure`, et la référence
inverse serait circulaire. L'assembly PostgreSQL n'est donc jamais dans la sortie de `MMV.Infrastructure`.

### 6.4 Aucune connexion, aucun effet de bord

- Aucune commande connectée : `--no-connect` sur `list`, et un hôte design-time `mmv-design-time.invalid` (Q4).
- `mmv.db` dans `%LOCALAPPDATA%\ManageMyVision\` : 258 048 octets, `2026-09-21 23:42:47Z`, **identique avant et
  après** toutes les exécutions (`EXÉCUTÉ`).
- Les scénarios T2, T3, T5 et T6 passent par la branche SQLite de la factory. Elle résout le chemin de la base
  locale (effet de bord confiné à cette branche, D-05.5), mais `--no-connect` n'ouvre aucune base. Sur le
  runner, c'est le même comportement que celui du step SQLite existant.

### 6.5 Coût

Mesure locale, pas sur le runner (`EXÉCUTÉ`) : `migrations list` 4,5 s, `has-pending-model-changes` 5,0 s, soit
environ **+10 s** par exécution. Le step SQLite prend 4,0 s.

---

## 7. Tests (`EXÉCUTÉ`)

| Commande | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | **0 avertissement, 0 erreur** |
| `dotnet test MMV.sln --no-build -c Debug` | **1953 réussis, 0 échec, 0 ignoré**. Détail : App 260, Application 628, Domain 1065 |
| step SQLite (commande de `ci.yml`), dépôt | **vert**, code 0 |
| step PostgreSQL, dépôt (T1) | **vert**, code 0 |

Le compte est inchangé depuis la v2 : `ci.yml` n'entre pas dans la compilation, et C7 n'ajoute aucun test.

---

## 8. Validation des décisions (delta v2 → C7)

| Décision ou critère | v2 | **C7** | Preuve |
|---|---|---|---|
| **D-05.6** | sans objet (C7 non ajouté) | **CONFORME** | §5 : une seule occurrence, sur le step ; aucun `env:` de job ; §6.3 |
| **D-05.3** (message sans valeur) | CONFORME | **CONFORME**, vu aussi dans le journal CI simulé | T4 |
| **D-06.6** | CONFORME | **CONFORME** | step SQLite identique après analyse ; vert |
| **Q2** | ouverte | **APPLIQUÉE** | T2, T3, T5, T6, U1 |
| C7 : démonstration locale d'un échec en cas de dérive | — | **FAIT** | M1 rouge, M2 vert |
| C7 : CI verte sur le SHA exact | — | **NON PROUVÉ** : exige un commit et un push | — |
| Clôture n°2 : `has-pending-model-changes` vert et bloquant sur les deux chaînes, en CI, sans serveur | non | **prouvé localement ; CI en attente** | T1, M1 |
| Clôture n°6 : aucune variable ni chaîne de production dans le workflow | — | **CONFORME** | §5 |

---

## 9. Limitations

1. **Le step n'a jamais tourné sur un runner GitHub.** La preuve « CI verte sur le SHA exact » exige un commit
   et un push, non autorisés à ce stade. Le numéro d'exécution sera référencé en C9.
2. Le harnais reproduit l'enveloppe `pwsh` documentée, **pas le runner** : image, `PATH`, emplacement des outils
   restaurés. Le step utilise des chemins relatifs à la racine du dépôt, comme les steps existants.
3. La version de `pwsh` sur `windows-latest` n'est pas connue localement. Le script est vérifié sur 5.1, 7.4.20
   et 7.6.6.
4. La seconde assertion (`InitialCreate`) n'est atteinte que par stub (U1). Dans les cas réels, la première
   suffit (T2 à T6). Elle est gardée parce que Q2 la demande, et comme défense en profondeur.
5. **Inchangé depuis la v2** : pas de serveur PostgreSQL (P4-5F) ; garde-fou de démarrage prouvé statiquement
   (Q5) ; fichiers `mmv.db` et `migration-journal.log` de l'incident v1 toujours présents.

---

## 10. Questions ouvertes

**Q10 — Verrouiller D-05.6 par un test ?** Un test pourrait vérifier que la variable n'apparaît que dans ce step.
Mais il faudrait soit un parseur YAML dans les tests, donc un nouveau paquet NuGet (interdit), soit une
recherche de texte fragile. **Proposition : non.** La revue de `ci.yml` suffit, et §6.3 montre qu'une violation
échoue bruyamment.

**Q11 — Preuve CI et suite.** La clôture de C7 exige une exécution CI sur le SHA exact, donc un commit et un
push. Quand ce push est-il autorisé : maintenant pour C7 seul, ou à la fin de C8 ? Faut-il ensuite reprendre
C8 (`CONTRIBUTING.md` et correction de `ARCHITECTURE.md`, selon Q6), puis C9, toujours en pré-revue sans commit ?

```
P4-5E-C7                     = PRE-REVIEW — C7 DONE LOCALLY — AWAITING VALIDATION
CI FILE                      = .github/workflows/ci.yml — ONE STEP ADDED (+55 / -0), 9 EXISTING STEPS IDENTICAL
DESIGN-TIME VARIABLE         = STEP-LEVEL env: ONLY (D-05.6) — 1 OCCURRENCE IN FILE
Q2 GUARD                     = migrations list --no-connect (JSON) — BASELINE REQUIRED, InitialCreate REFUSED
FALSE GREEN (Q2)             = CLOSED — ABSENT / MISSPELT KEY / sqlite / RUNTIME VAR → RED
DRIFT DEMONSTRATION          = MUTATION OUT OF REPO — PG STEP RED, SQLITE STEP GREEN
SHELLS                       = WINDOWS POWERSHELL 5.1, PWSH 7.4.20, PWSH 7.6.6 — IDENTICAL VERDICTS (12 SCENARIOS)
SQLITE STEP                  = UNCHANGED — GREEN
TESTS                        = 1953 / 1953 GREEN, 0 SKIPPED (UNCHANGED SINCE v2)
CONNECTION / SIDE EFFECTS    = NONE — mmv.db UNCHANGED
CI GREEN ON EXACT SHA        = NOT PROVEN — REQUIRES COMMIT + PUSH (Q11)
C8 CONTRIBUTING (D-19)       = NOT STARTED
C9 CLOSURE                   = NOT STARTED
COMMIT / PUSH                = NONE
```

---

## Annexe A — Step ajouté

```yaml
      # Même contrôle sur la chaîne de migrations PostgreSQL (P4-5E-C, C7). Aucun serveur contacté :
      # la factory design-time utilise une chaîne factice constante et `migrations list` tourne en
      # `--no-connect`. La variable design-time est posée sur CE SEUL step, jamais au niveau du job
      # (D-05.6) : le step SQLite ci-dessus s'exécute sans elle, donc à l'identique.
      #   1) Garde contre le faux vert (Q2) : sans la variable (clé absente ou mal orthographiée), la
      #      factory retombe sur SQLite et `has-pending-model-changes` contrôlerait la chaîne SQLite en
      #      répondant vert. On exige donc d'abord, sous le même `env:`, que la chaîne listée contienne
      #      la baseline PostgreSQL et PAS `InitialCreate` (SQLite). Contrôle MACHINE sur le JSON officiel.
      #   2) Dérive du modèle : attrape des écarts que le step SQLite ne voit pas (ex. HasMaxLength,
      #      TEXT des deux côtés sur SQLite, varchar(n) sur PostgreSQL).
      - name: Check EF Core pending model changes (PostgreSQL chain)
        shell: pwsh
        env:
          MMV_DESIGNTIME_DATABASE_PROVIDER: postgresql
        run: |
          $project = 'src/MMV.Infrastructure.PostgreSQL.Migrations'

          # (1) Chaîne réellement contrôlée. Avec --prefix-output, seules les lignes 'data:' portent le JSON ;
          #     les autres (info:, warn:, error:) sont renvoyées au journal pour que la cause d'un échec reste visible.
          $lines = @(dotnet ef migrations list --no-connect --json --prefix-output --project $project --startup-project $project --no-build)
          $lines | Where-Object { $_ -notlike 'data:*' } | ForEach-Object { Write-Host $_ }
          if ($LASTEXITCODE -ne 0) {
            Write-Error "Echec de 'dotnet ef migrations list' (code $LASTEXITCODE) : chaine PostgreSQL NON verifiee."
            exit 1
          }
          $json = ($lines | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5) }) -join "`n"
          if ([string]::IsNullOrWhiteSpace($json)) {
            Write-Error "Sortie JSON vide : chaine PostgreSQL NON verifiee."
            exit 1
          }
          try {
            $migrations = $json | ConvertFrom-Json
          } catch {
            Write-Error "JSON de 'migrations list' invalide : $($_.Exception.Message)"
            exit 1
          }
          $names = @(foreach ($m in $migrations) { $m.name })
          Write-Host ("Chaine controlee : {0} migration(s)." -f $names.Count)
          foreach ($m in $migrations) { Write-Host "  - $($m.id)" }
          if ($names -notcontains 'InitialPostgreSqlBaseline') {
            Write-Error "Baseline 'InitialPostgreSqlBaseline' absente : la chaine controlee n'est PAS la chaine PostgreSQL."
            exit 1
          }
          if ($names -contains 'InitialCreate') {
            Write-Error "Migration SQLite 'InitialCreate' presente : la chaine controlee n'est PAS la chaine PostgreSQL."
            exit 1
          }

          # (2) Dérive du modèle sur la chaîne PostgreSQL.
          dotnet ef migrations has-pending-model-changes --project $project --startup-project $project --no-build
          if ($LASTEXITCODE -ne 0) {
            Write-Error "Controle de derive PostgreSQL en echec (code $LASTEXITCODE) : changement de modele sans migration PostgreSQL, ou commande en echec."
            exit 1
          }
```

## Annexe B — Sortie brute de `migrations list` (`EXÉCUTÉ`)

Avec la variable (`postgresql`) :

```
data:    [
data:      {
data:        "id": "20260922001219_InitialPostgreSqlBaseline",
data:        "name": "InitialPostgreSqlBaseline",
data:        "safeName": "InitialPostgreSqlBaseline",
data:        "applied": null
data:      }
data:    ]
```

Sans la variable, la même commande sort en code 0 et liste les 14 migrations SQLite, à partir de
`20260127184542_InitialCreate`. C'est le faux vert que la garde (1) intercepte. Dans les deux cas, stderr est
vide.
