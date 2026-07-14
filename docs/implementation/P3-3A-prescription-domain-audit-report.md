# P3-3A — Audit métier **Ordonnances** avant sécurisation

> **Mode : `AUDIT_ONLY`.** Aucun code, test, migration, package, workflow ou configuration n'est modifié.
> Le seul fichier créé est **ce rapport**. **Aucun commit, aucun push.**
> **Le dépôt réel prime toujours sur la roadmap et sur les rapports antérieurs.**
>
> Branche : `p3-business-rules`. Étape précédente : **P3-2C** (UI Clients, `GO`, CI verte).
> Cadré par la [roadmap P3-3](../architecture/P3-business-rules-roadmap.md),
> l'[ADR frontières §8](../architecture/adr-application-boundaries.md),
> l'[ADR-PROD-DB-001 multi-poste](../architecture/adr-prod-db-001-multi-poste-database-strategy.md) et les
> [notes transposition / fiche atelier](../domain/P3-workshop-sheet-and-transposition-notes.md).

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-3A |
| `EXECUTION_MODE` | `AUDIT_ONLY` |
| `SOURCE_BRANCH` | `p3-business-rules` |
| `ALLOW_CODE_CHANGES` | **`false`** |
| `ALLOW_TEST_CHANGES` | **`false`** |
| `ALLOW_MIGRATION` | **`false`** |
| `ALLOW_DOC_CHANGES` | `true` (ce rapport **uniquement**) |
| `ALLOW_COMMIT` / `ALLOW_PUSH` | `false` / `false` |

---

## 2. Préconditions Git et CI

| Contrôle | Attendu | Résultat |
|---|---|---|
| `git branch --show-current` | `p3-business-rules` | ✅ |
| `git status --short` | propre | ✅ **vide** (avant création de ce rapport) |
| Dernier commit | `8f019fb docs(P3-2C): record customer UI CI validation` | ✅ |
| Commits P3-2 présents | `30dce45`, `8a518eb`, `5ee8306`, `2f260de`, `8f019fb` | ✅ **les 5** |
| CI distante | verte sur le dernier commit | ✅ |

**Run CI inspecté** (`gh run view 29294696114`) :

| Élément | Valeur |
|---|---|
| `databaseId` | `29294696114` |
| `headSha` | `8f019fb11907af70414266dac672c537ff9cdd50` — commence bien par **`8f019fb`** ✅ |
| `headBranch` | `p3-business-rules` ✅ |
| `status` | `completed` ✅ |
| `conclusion` | **`success`** ✅ |
| Job | `Restore / Build / Test / Scan` — `success` (Checkout, Setup .NET, Restore, Build, Test, Audit vulnérables, Restore tools, EF pending model changes : **toutes vertes**) |
| URL | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29294696114 |

> *Note d'exécution* : `gh run list --branch … --commit 8f019fb` n'a **rien** renvoyé (le drapeau `--commit`
> n'est pas honoré par la version de `gh` installée). Le run a donc été vérifié **directement** par
> `gh run view 29294696114`, qui prouve l'association `headSha` ↔ `headBranch` ↔ `conclusion`. **Aucune** précondition
> n'a été supposée.

**Préconditions = GO.**

---

## 3. Baseline locale

| Contrôle | Attendu | Résultat |
|---|---|---|
| `dotnet restore MMV.sln` | OK | ✅ |
| `dotnet build --no-restore -c Debug` | vert | ✅ **0 avertissement, 0 erreur** |
| `dotnet test --no-build -c Debug` | ≥ 645 | ✅ **645** — App **211** · Application **198** · Domain **236** ; **0 échec, 0 ignoré** |
| `dotnet list … --vulnerable --include-transitive` | 0 | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK | ✅ `dotnet-ef` 8.0.27 |
| `ef migrations has-pending-model-changes` | false | ✅ « No changes … since the last migration » |
| `MMV.Application` — références projet | `MMV.Domain` **seule** | ✅ |
| `MMV.Application` — packages top-level | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` **seul** | ✅ |

**Baseline = GO.**

---

## 4. Inventaire Domain — `Prescription`

[`src/MMV.Domain/Entities/Prescription.cs`](../../src/MMV.Domain/Entities/Prescription.cs) — **20 propriétés**,
**toutes à setter public**, **aucune méthode**.

| # | Propriété | Type | Nullable | Défaut | Setter | Validation Domain | Config EF | Signification / remarques |
|---|---|---|---|---|---|---|---|---|
| 1 | `PrescriptionId` | `long` | non | 0 | **public** | — | `HasKey`, `ValueGeneratedOnAdd`, `INTEGER` | PK |
| 2 | `CustomerId` | `long` | non | 0 | **public** | — | `IsRequired`, FK, **index** `HasIndex("CustomerId")` | propriétaire ; **jamais** vérifié à l'écriture (§9) |
| 3 | `IssueDate` | `DateTime` | non | `default` | **public** | `≤ UtcNow + 1 j` *(morte, §5)* | `IsRequired` | date d'émission ; **pas** de défaut SQL (R-19) |
| 4 | `DoctorName` | `string?` | **oui** | `null` | **public** | **aucune** | `HasMaxLength(200)` | **requis par l'UI seulement** (§5, contradiction) |
| 5 | `OdSphere` | `double?` | oui | `null` | **public** | `[-20, 20]` si présent | `REAL` | sphère OD |
| 6 | `OdCylinder` | `double?` | oui | `null` | **public** | `[-6, 6]` si présent | `REAL` | cylindre OD |
| 7 | `OdAxis` | **`int?`** | oui | `null` | **public** | `[0, 180]` si présent | `INTEGER` | axe OD ; **pas** de règle croisée |
| 8 | `OdAddition` | `double?` | oui | `null` | **public** | `[0, 4]` si présent | `REAL` | addition OD |
| 9 | `OdPrismValue` | `double?` | oui | `null` | **public** | **AUCUNE** | `REAL` | **non borné, non couplé à la base** |
| 10 | `OdPrismBase` | `PrismBase?` | oui | `null` | **public** | **AUCUNE** | `HasConversion<string>()` | enum ⇒ **texte** en base |
| 11 | `OdVisualAcuity` | `string?` | oui | `null` | **public** | **AUCUNE** | `HasMaxLength(10)` | format libre (« 10/10 ») |
| 12–18 | `OgSphere`, `OgCylinder`, `OgAxis`, `OgAddition`, `OgPrismValue`, `OgPrismBase`, `OgVisualAcuity` | *idem OD* | oui | `null` | **public** | *idem OD* | *idem OD* | **strictement symétriques** (§4.8) |
| 19 | `Notes` | `string?` | oui | `null` | **public** | **AUCUNE** | `HasMaxLength(2000)` | — |
| 20 | `CreatedAt` | `DateTime` | non | **`DateTime.UtcNow`** (initialiseur) | **public** | **AUCUNE** | pas de défaut SQL (R-19) | horodatage de création |
| — | `Customer` | `virtual Customer` | `null!` | — | public | — | `HasOne … WithMany(c => c.Prescriptions) … Restrict` | navigation |

**Nommage réel** : préfixes **`Od`** (œil droit) et **`Og`** (œil gauche) — **pas** `Right`/`Left`.

### Réponses explicites

1. **Entité anémique ou méthodes métier ?** → **Totalement anémique.** Aucune méthode, aucun invariant, aucun
   setter privé. À comparer avec `Customer` qui, depuis **P3-2B**, possède `Archive()` / `Reactivate()` et un
   `IsArchived` à setter privé.
2. **Statut / verrouillage / validation / version ?** → **Aucun.** Pas de `Status`, pas de `IsLocked`, pas de
   `Version`, pas de `RowVersion`.
3. **Archivage ou suppression logique ?** → **Aucun.** Pas de `IsArchived`, pas de `DeletedAt`. La suppression est
   **physique** (§12).
4. **Token de concurrence ?** → **Aucun** (ni `IsConcurrencyToken`, ni `RowVersion`, ni `xmin`). Confirmé sur
   l'entité, la configuration Fluent **et** le snapshot.
5. **Référence à l'ordonnance originale / version antérieure ?** → **Aucune.** Pas de `OriginalPrescriptionId`,
   pas de `SupersededBy`, pas de table d'historique.
6. **Valeurs optiques nullable ?** → **Oui, toutes** (`double?`, `int?`, `PrismBase?`). Seuls `PrescriptionId`,
   `CustomerId`, `IssueDate`, `CreatedAt` sont non-nullables.
7. **Distinction zéro / null ?** → **Structurellement possible** (`double?`), mais **jamais exploitée** : aucune
   règle du dépôt ne traite `0` différemment de `null` à l'écriture. **Nuance numérique importante** : les valeurs
   optiques sont saisies au **quart de dioptrie** (0.25, 0.50, 0.75 …), qui sont des fractions binaires **exactes**
   en `double` ; `0.0` l'est aussi. Une comparaison `== 0` sur ces valeurs est donc **fiable** — l'argument
   « virgule flottante ⇒ jamais comparer à zéro » **ne s'applique pas ici**. (Une tolérance reste néanmoins plus
   robuste si une future saisie non quartée apparaissait.)
8. **OD / OG symétriques ?** → **Oui, parfaitement** : mêmes 7 champs, mêmes types, mêmes plages, mêmes colonnes,
   mêmes (absences de) règles. Aucune asymétrie dans l'entité, le validateur, l'EF ni le DTO.
9. **Dates UTC ou locale ?** → **Mixte, à surveiller.** `CreatedAt` = **`DateTime.UtcNow`** (initialiseur). Le
   validateur compare `IssueDate` à **`DateTime.UtcNow`**. Mais l'UI initialise `IssueDate` à
   **`DateTimeOffset.Now`** (heure **locale**) puis envoie **`.UtcDateTime`** : la conversion est donc correcte,
   au prix d'un décalage possible d'un jour civil à la saisie (une ordonnance « du jour » saisie tard le soir peut
   basculer au lendemain UTC — **sans conséquence** ici car la borne est `+1 jour`). Aucun `IClock` (R-19 non encore
   généralisé). **Aucune date n'est stockée en heure locale.**
10. **Ordonnance vide ou partielle ?** → **Oui.** Une ordonnance **sans aucune valeur optique** est acceptée par le
    Domain **et** par l'UI (l'UI n'exige que `DoctorName`). Une ordonnance **entièrement vide** (aucun champ optique,
    OD ni OG) est donc persistable aujourd'hui.

---

## 5. Validations actuelles — et un constat majeur

### 5.1 Le validateur Domain est du **code mort**

[`PrescriptionValidator`](../../src/MMV.Domain/Validators/PrescriptionValidator.cs) n'est **appelé par aucun use
case, aucun service, aucune ViewModel**. Recherche exhaustive de `PrescriptionValidator` / `IValidator<Prescription>`
sur tout le dépôt : les **seules** occurrences en dehors de sa définition sont ses **4 tests unitaires**
(`tests/MMV.Domain.Tests/ValidatorTests/PrescriptionValidatorTests.cs`) et de la **documentation**.

**Conséquence factuelle, à ne pas édulcorer** : **aucune** des plages ci-dessous n'est appliquée en production.
`CreatePrescriptionUseCase` et `UpdatePrescriptionUseCase` **n'exécutent aucune validation** (§9). Les seules bornes
réellement opposées à l'utilisateur sont celles de l'**UI** (§14), contournables par tout autre appelant.
`CommandValidation` (P3-1) **n'est câblé que sur les Clients** (`CreateCustomerUseCase`, `UpdateCustomerUseCase`).

### 5.2 Règles réellement présentes dans `PrescriptionValidator`

| Règle | Valeur | Par œil | Remarque |
|---|---|---|---|
| `IssueDate` | `≤ DateTime.UtcNow.AddDays(1)` | — | **Bug latent** : l'expression est évaluée **une seule fois, à la construction du validateur** (surcharge par constante, pas par lambda). Un validateur enregistré en **singleton** figerait la borne au démarrage. Sans effet aujourd'hui (code mort, instancié à chaque test), **mais à corriger en P3-3B** via `LessThanOrEqualTo(_ => DateTime.UtcNow.AddDays(1))`. |
| Sphère | `[-20, 20]` **si présente** | OD **et** OG | symétrique |
| Cylindre | `[-6, 6]` **si présent** | OD **et** OG | symétrique |
| Axe | `[0, 180]` **si présent** | OD **et** OG | symétrique |
| Addition | `[0, 4]` **si présente** | OD **et** OG | symétrique |
| **Prisme** | **— AUCUNE —** | — | `PrismValue` **non borné** ; `PrismBase` **non contrôlée** |
| **Base prismatique** | **— AUCUNE —** | — | aucun couplage valeur ↔ base |
| **Acuité visuelle** | **— AUCUNE —** | — | longueur EF (10) seule |
| **`DoctorName`** | **— AUCUNE —** | — | longueur EF (200) seule ; **l'UI l'exige** (contradiction §5.4) |
| **Champs obligatoires** | **aucun** hormis `IssueDate` (type non-nullable) | — | ordonnance vide acceptée |
| **Règles croisées** | **— AUCUNE —** | — | **cœur du manque P3-3** |

### 5.3 Matrice par couche

| Règle | Domain | Application | UI | EF / base | Tests |
|---|---|---|---|---|---|
| Plages sphère/cyl/axe/add | ✅ *(mais **morte**)* | ❌ | ✅ (dupliquée en dur) | ❌ | Domain ×4, App ViewModel |
| `IssueDate ≤ J+1` | ✅ *(morte)* | ❌ | ❌ **absente** | ❌ | Domain ×1 |
| `DoctorName` requis | ❌ | ❌ | ✅ **UI seule** | ❌ (`MaxLength(200)`) | App ViewModel |
| Prisme / base | ❌ | ❌ | ❌ | ❌ | **aucun** |
| Cylindre ⇄ axe | ❌ | ❌ | ❌ | ❌ | **aucun** |
| Client existant | ❌ | ❌ | implicite (picker) | ✅ **FK** | — |
| Client **archivé** | ❌ | ❌ | ❌ | ❌ | **aucun** |
| Longueurs / précisions | ❌ | ❌ | ❌ | ✅ | Infra |

### 5.4 Duplications et contradictions constatées

1. **Duplication** : les 4 plages sont écrites **deux fois** — Domain (morte) et UI (`PrescriptionFormViewModel`,
   8 méthodes `ValidateXxx` en dur). Toute évolution doit aujourd'hui être faite **aux deux endroits**.
2. **Contradiction réelle** : l'**UI exige `DoctorName`** (`ValidateDoctorName` ⇒ « Le nom du médecin est requis »)
   alors que le **Domain l'autorise à `null`** et que la colonne est **nullable**. Une ordonnance sans médecin est
   **persistable** par l'Application (et par le `PrescriptionService`), **pas** par l'UI. La règle « vraie » n'est
   écrite nulle part.
3. **Contradiction latente** : la borne `IssueDate ≤ J+1` **n'existe pas dans l'UI** — le formulaire accepterait une
   date future ; le Domain (mort) la refuserait ; le `PrescriptionService` (§9.4) la refuserait par exception.
4. **Angle mort total** : **prisme, base prismatique, acuité visuelle, notes** ne sont validés **par aucune couche**.

---

## 6. Cylindre et axe — analyse

### 6.1 Faits établis dans le dépôt

- OD et OG sont **strictement symétriques** ; l'analyse vaut identiquement pour les deux.
- `OdCylinder`/`OgCylinder` sont `double?` ; `OdAxis`/`OgAxis` sont `int?`. **`0` et `null` sont donc représentables
  distinctement**, et les quarts de dioptrie sont **exacts** en `double` (§4.7).
- **L'UI transforme le vide en `null`, pas en zéro** : les `TextBox` sont liés directement à des propriétés
  `double?` / `int?` (`Text="{Binding OdCylinder}"`) ; un champ vidé produit `null`. **Aucun** code ne convertit
  « vide » en `0`, et `ClearForm()` réinitialise explicitement à `null`. ✅
- **Aucune** règle croisée n'existe (Domain, Application, UI, base).
- **`Axis = 0` est autorisé** (borne `InclusiveBetween(0, 180)`) ; **`Axis = 180` aussi**.
- **Fixture existante à connaître** : `PrescriptionValidatorTests.Validate_WithValidRanges_ShouldPass` construit
  **`OgCylinder = 0` avec `OgAxis = 0`** et **attend `IsValid == true`**. Une règle « cylindre nul ⇒ axe absent »
  **ferait échouer ce test existant** — il faudra **l'amender sciemment** en P3-3B (ce n'est pas un affaiblissement :
  la fixture encode aujourd'hui une combinaison que la nouvelle règle déclare incohérente).
- **`SaleItem` et `OrderItem` utilisent les mêmes conventions** : `Sphere`/`Cylinder` `double?`, `Axis` `int?`,
  `PrismValue` `double?`, `PrismBase?` — **mais aucun n'est validé nulle part** (§15).
- **`0` et `180` sont équivalents en optique**, et le dépôt le **dit déjà** : les
  [notes de transposition §3.2](../domain/P3-workshop-sheet-and-transposition-notes.md) fixent la convention
  « si `axe' = 0` → **normaliser à 180** ». La convention produit **est donc déjà tranchée** : l'intervalle utile est
  **]0, 180]**, `0` étant l'écriture alternative de `180`.

### 6.2 Comparaison des options

| Option | Règle | Analyse |
|---|---|---|
| **A — Non-null** | axe obligatoire dès que `Cylinder != null` | **Rejetée.** `Cylinder = 0.00` est explicitement saisissable et signifie « pas d'astigmatisme » : exiger un axe dans ce cas est **médicalement absurde** et forcerait l'utilisateur à inventer une valeur. |
| **B — Non-zéro** | axe obligatoire **ssi** `|Cylinder| > 0` ; et si `Cylinder` est `null` **ou** `0` ⇒ `Axis` **doit être** `null` | **Retenue.** Seule règle qui reflète l'optique : un axe n'a de sens **que** s'il y a un cylindre à orienter. Le volet réciproque (« axe sans cylindre ⇒ refus ») élimine la donnée orpheline qui, transposée en P3-6B, produirait une notation fausse. |
| **C — Tolérance historique** | accepter temporairement l'incohérent | **Inutile en base, utile en tests.** Voir §6.3. |

### 6.3 Pourquoi la « tolérance historique » n'est **pas** nécessaire en base

La roadmap prévient : « **Risques** : casser des données historiques partielles. **Mitigation : règles à la saisie,
pas rétroactives.** » Le dépôt confirme que la mitigation est **suffisante et gratuite** :

- la règle vivra dans **`PrescriptionValidator`**, exécuté **à l'écriture** via `CommandValidation` — **aucune**
  contrainte `CHECK` en base, **aucune** migration, **aucune** relecture des lignes existantes ;
- une ordonnance historique incohérente reste **lisible, affichable et copiable** ; elle ne devient bloquante que si
  l'utilisateur **la ré-enregistre** — et c'est précisément le moment où on **veut** qu'il la corrige.

Le **seul** point de vigilance réel est le **test existant** (`OgCylinder = 0` + `OgAxis = 0`), pas les données.

### 6.4 Recommandation (cylindre / axe)

> **Option B, par œil, avec normalisation `0 → 180` à la saisie.**
>
> 1. `|Cylinder| > 0` ⇒ `Axis` **obligatoire**, dans `[0, 180]`.
> 2. `Cylinder` **null ou nul** ⇒ `Axis` **doit être null** (refus explicite de l'axe orphelin).
> 3. `Axis` renseigné **sans** cylindre non nul ⇒ **refus** (corollaire de 2, message dédié).
> 4. **`Axis = 0` accepté en saisie mais normalisé à `180`** avant persistance — la convention est **déjà écrite**
>    dans les notes de transposition et doit être **figée maintenant**, sinon P3-6B héritera de deux écritures pour
>    le même axe et ses tests seront ambigus. La normalisation est une **écriture Domain pure**, sans migration
>    (les lignes historiques à `0` restent telles quelles ; elles seront normalisées si ré-enregistrées).
>
> Ces règles restent **compatibles avec l'ordonnance partielle** : un œil entièrement vide reste valide (§8).

---

## 7. Prisme et base prismatique

### 7.1 Faits

- Propriétés exactes : `OdPrismValue` / `OgPrismValue` (`double?`) et `OdPrismBase` / `OgPrismBase`
  (`PrismBase?`, **converti en `string`** en base).
- **`enum PrismBase` = `In`, `Out`, `Up`, `Down`** — **4 valeurs**, et **elles seules**.
- **Validation : rigoureusement nulle.** Ni Domain, ni Application, ni UI, ni base. `PrismValue` n'a **même pas de
  plage** (contrairement à sphère/cylindre/axe/addition) : `999` passerait.
- **Aucune** contrainte n'empêche `PrismValue` sans `PrismBase`, ni l'inverse.
- **Défaut d'UI grave et concret** (§14) : dans
  [`PrescriptionFormView.axaml`](../../src/MMV.App/Views/Clients/PrescriptionFormView.axaml), la base prismatique est
  un **`TextBox` en texte libre** (`Text="{Binding OdPrismBase}"`), **pas** un `ComboBox` — et son *watermark*
  annonce **« H/V/In/Out »**, or **`H` et `V` n'existent pas** dans l'enum. L'écran **invite** donc l'utilisateur à
  saisir des valeurs que le modèle **ne peut pas représenter** ; la conversion texte → enum échoue silencieusement
  et **la base prismatique reste `null`** alors que la valeur du prisme, elle, est enregistrée. **La combinaison
  incohérente « valeur sans base » est donc le résultat *nominal* de l'écran, pas un cas limite.**
- **Fixtures / historique** : **aucun** test du dépôt ne renseigne prisme ou base (ni Domain, ni Application, ni
  App) — le prisme est un **angle mort de test complet**. Aucune donnée de test incohérente à préserver.
- **Symétrie OD/OG** : parfaite (dans l'absence de règles comme dans les défauts d'UI).
- **Copie partielle vers les commandes** : `SaleFormViewModel` copie bien `PrismValue`/`PrismBase` dans les
  `SaleItem`, mais `OrderFormViewModel.AutoFillFromPrescription()` ne copie que **sphère, cylindre, axe, addition** —
  **le prisme est perdu** au pré-remplissage d'une commande, bien que `OrderItem` possède les colonnes (§15).

### 7.2 Recommandation (prisme)

> **Couple strict, symétrique OD/OG, sans invention clinique :**
>
> 1. `PrismValue` **renseigné et non nul** ⇒ `PrismBase` **obligatoire**.
> 2. `PrismBase` **renseignée** ⇒ `PrismValue` **obligatoire et non nul**.
> 3. **`PrismValue = 0` ⇒ absence de prisme** : traiter `0` comme « pas de prisme » et exiger alors
>    `PrismBase == null` (strictement parallèle à la règle cylindre/axe — **même sémantique, même forme**).
> 4. **Pas de borne supérieure pour `PrismValue`** : c'est le **seul** champ optique dont la plage n'est pas déductible
>    du dépôt. **Décision retenue** (voir *Décisions retenues pour P3-3B*, §3) : n'imposer **aucune** borne supérieure
>    en P3-3B — seules une **valeur finie** (rejet `NaN`/`Infinity`) et une **non-négativité** sont exigées. Une borne
>    supérieure (par ex. `[0, 10]`, envisagée puis **écartée**) ne pourra être introduite qu'après confirmation par un
>    opticien ou un besoin produit réel. Ne pas l'inventer « par symétrie esthétique ».
> 5. **Les 4 valeurs `In`/`Out`/`Up`/`Down` sont les seules autorisées** — le typage enum le garantit déjà côté
>    Application ; **c'est l'UI qui doit être corrigée** (`ComboBox` sur les valeurs de l'enum) : la règle Domain
>    seule laisserait l'écran produire des `null` silencieux.
>
> **Le correctif UI (`TextBox` → `ComboBox`) est indissociable de la règle** : sans lui, la validation Domain
> rejetterait des ordonnances que l'utilisateur croit avoir correctement saisies, sans qu'il puisse les corriger.

---

## 8. Addition et ordonnance partielle

### 8.1 Faits

- `OdAddition` / `OgAddition` : `double?`, plage `[0, 4]` (validateur mort), **facultatives**.
- **OD et OG peuvent différer** — aucune règle ne les lie. C'est correct (une addition asymétrique est possible).
- **Une addition peut exister sans sphère** : aucune règle ne l'interdit. Aucune preuve métier dans le dépôt ne
  justifie de l'interdire (une addition seule est une prescription de près valide en soi).
- **Lien addition ↔ type de verre : il existe, mais uniquement dans l'UI, en heuristique de *suggestion*.**
  `SaleFormViewModel.DetermineSuggestedGlassType()` : `Addition > 0` ⇒ progressif suggéré, sinon unifocal ;
  `DetermineUsageType(Addition)` alimente `SaleItem.UsageType` (`LensUsageType`). **Ce lien n'existe pas dans
  `Prescription`** : l'entité **ne porte aucun type de verre ni usage**. `LensUsageType` vit sur `SaleItem` /
  `OrderItem`, **pas** sur l'ordonnance.
- Le modèle **ne permet donc pas** d'exprimer proprement « addition ⇒ verre progressif » au niveau de l'ordonnance :
  la contrainte est **structurellement** du ressort de la **vente / commande** (P3-4 / P3-7), voire de la fiche
  atelier (P3-6B).

### 8.2 Recommandation (addition et ordonnance partielle)

> **Validation minimale, cohérence verre reportée.**
>
> - Conserver `Addition` **facultative**, plage `[0, 4]`, **indépendante entre OD et OG**.
> - **N'introduire aucune** dépendance addition ↔ type de verre en P3-3 : la roadmap le dit déjà (« au niveau
>   **verre**, pas forcément ordonnance brute ») et le **modèle ne l'exprime pas**. L'y forcer imposerait un champ
>   « usage » sur `Prescription` — **une migration et un concept nouveaux, sans besoin démontré**. → **Reporté à
>   P3-4 / P3-7** (cohérence verre ↔ correction) et **P3-6B** (fiche atelier).
> - **Préserver explicitement l'ordonnance partielle** : un œil sans aucune valeur reste **valide** ; une ordonnance
>   dont **un seul** œil est renseigné reste **valide**. Les règles croisées de §6 et §7 sont **conditionnelles à la
>   présence** des champs et ne rendent **aucun** champ obligatoire.
> - **Point à trancher en P3-3B** : faut-il continuer d'accepter l'ordonnance **totalement vide** (aucun champ, ni OD
>   ni OG) ? Elle est aujourd'hui persistable. Exiger « **au moins une valeur optique renseignée** » est une règle
>   **peu risquée et utile** (elle bloque l'enregistrement accidentel d'un formulaire vierge), mais elle **n'est pas
>   dans la roadmap** : à **confirmer** plutôt qu'à imposer.

---

## 9. Use cases Application — inventaire

Dossiers réels sous [`src/MMV.Application/UseCases/Prescriptions/`](../../src/MMV.Application/UseCases/Prescriptions/) :
**`CreatePrescription`**, **`UpdatePrescription`**, **`DeletePrescription`**, **`ListPrescriptionsByCustomer`**
(4 dossiers, 16 fichiers). **Aucun** picker dédié aux ordonnances.

| Use case | Entrée | Sortie | Validation | Repositories | UoW | `ITransactionRunner` | Écritures | Horodatage | Exceptions | Effets de bord | Introuvable |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **`CreatePrescriptionUseCase`** | `CreatePrescriptionCommand` (18 champs) | `CreatePrescriptionResult { PrescriptionId }` | **AUCUNE** | `IPrescriptionRepository` | ✅ | ❌ (mono-écriture) | 1 (`CreateAsync` + `SaveChangesAsync`) | `CreatedAt` = initialiseur d'entité (`UtcNow`) | `ArgumentNullException` (commande nulle) **seulement** | aucun | **n/a — aucun chargement** |
| **`UpdatePrescriptionUseCase`** | `UpdatePrescriptionCommand` (+ `PrescriptionId`) | `UpdatePrescriptionResult { PrescriptionFound, PrescriptionId }` | **AUCUNE** | `IPrescriptionRepository` | ✅ | ❌ | 1 | **aucun** (`CustomerId` et `CreatedAt` **préservés** ; pas d'`UpdatedAt` sur l'entité) | `ArgumentNullException` | aucun | `PrescriptionFound = false`, **aucune écriture** ✅ |
| **`DeletePrescriptionUseCase`** | `DeletePrescriptionCommand { PrescriptionId }` | `DeletePrescriptionResult { PrescriptionFound, PrescriptionId }` | **AUCUNE** | `IPrescriptionRepository` | ✅ | ❌ | 1 (**suppression physique**) | — | `ArgumentNullException` | **destruction définitive** | `PrescriptionFound = false`, **aucune écriture** ✅ |
| **`ListPrescriptionsByCustomerUseCase`** | `ListPrescriptionsByCustomerQuery { CustomerId }` | `IReadOnlyList<PrescriptionListItemDto>` | — | `IPrescriptionRepository` | — | — | 0 | — | `ArgumentNullException` | aucun | liste vide |

**Consommateurs indirects** : `GetSaleFormReferenceDataUseCase` (Ventes) lit `GetLatestByCustomerIdAsync` et projette
l'ordonnance active en `PrescriptionListItemDto` (**lecture seule**, §15).

### Réponses explicites

1. **Create charge-t-il `Customer` ?** → **NON.** Aucun `ICustomerRepository` n'est même **injecté**.
2. **Create vérifie-t-il `CustomerFound` ?** → **NON.** Un `CustomerId` inexistant part jusqu'à la base, où la **FK**
   le rejette ⇒ **`DbUpdateException` brute** (`UnitOfWork` ne traduit rien, cf. P3-2B §12).
3. **Create vérifie-t-il `Customer.IsArchived` ?** → **NON.** ⚠️ **C'est l'exigence obligatoire héritée de P3-2B, et
   elle est bel et bien non tenue.** Une ordonnance **peut aujourd'hui être créée pour un client archivé.**
4. **Update peut-il changer `CustomerId` ?** → **NON** — `CustomerId` est **explicitement préservé** (commentaire +
   test `ExecuteAsync_PreservesCustomerIdAndCreatedAt`). Une ordonnance **ne change jamais de propriétaire**. ✅
5. **Update charge-t-il `Customer` ?** → **NON.**
6. **Update vérifie-t-il l'état archivé ?** → **NON.**
7. **Delete vérifie-t-il l'utilisation ailleurs ?** → **NON** — et, fait décisif, **il n'y a rien à vérifier** : aucune
   entité ne référence `Prescription` (§11, §13, §15).
8. **Validations Domain réutilisées via `CommandValidation` ?** → **NON.** `CommandValidation` n'est utilisé que par
   `CreateCustomerUseCase` et `UpdateCustomerUseCase` (P3-1). Le socle **existe et est prouvé** ; il suffit de le
   câbler.
9. **Les commandes peuvent-elles créer des valeurs que le validateur refuserait ?** → **OUI, largement** — puisque le
   validateur n'est **jamais exécuté**. `OdSphere = 999`, `OdAxis = 4000` (hors `[0,180]`), `IssueDate` en 2050,
   `OdPrismValue = 50` sans base : **tout passe** par l'Application. Seule l'**UI** borne (partiellement).
10. **`DateTime.Now` ou `UtcNow` ?** → Les use cases **ne lisent pas l'horloge** ; `CreatedAt` vient de
    l'initialiseur d'entité (**`UtcNow`**). L'UI part de `DateTimeOffset.Now` (local) mais transmet **`.UtcDateTime`**.
    **Aucun `DateTime.Now` n'est persisté.** ✅

### 9.4 Second chemin d'écriture dormant — `PrescriptionService`

[`src/MMV.Domain/Services/PrescriptionService.cs`](../../src/MMV.Domain/Services/PrescriptionService.cs) expose
`CreatePrescriptionAsync` / `UpdatePrescriptionAsync` qui écrivent **directement** via `IUnitOfWork`, **court-circuitant
les use cases**. Il applique **une** règle (`IssueDate > UtcNow + 1 j` ⇒ `BusinessRuleException`) — une règle que les
use cases, eux, **n'ont pas**.

Il est **enregistré dans la DI** (`MMV.Infrastructure/DependencyInjection.cs:67`,
`services.AddScoped<IPrescriptionService, PrescriptionService>()`) mais **consommé par personne** dans
`src/MMV.App` : c'est du **code mort câblé**, couvert par 4 tests. **Danger** : tout garde-fou posé uniquement dans
les use cases serait **contournable** par un futur appelant de ce service — qui compile, est injectable, et paraît
légitime. **P3-3B doit le traiter explicitement** (le supprimer, ou le faire déléguer), et non l'ignorer.

---

## 10. Client archivé — politique

### 10.1 Le trou est réel et prouvé

`CreatePrescriptionUseCase` n'injecte **même pas** `ICustomerRepository`. L'exclusion du picker (P3-2B/P3-2C) est
une **barrière d'UI**, pas un garde-fou : `PrescriptionFormViewModel.CustomerId` est un simple `long` positionné par
le parent, et l'écran de création **reste ouvert** pendant qu'un autre poste archive le client (§17, course 4).

### 10.2 Comparaison des politiques

| Critère | **A** — refuser **Create** seul | **B** — refuser Create **+ Update** | **C** — refuser Create + Update + **Delete** | **D** — « correction contrôlée » |
|---|---|---|---|---|
| **Cohérence métier** | ✅ « archivé = plus d'activité **nouvelle** » | ⚠️ interdit aussi de **corriger une faute de frappe** sur une ordonnance passée | ❌ incohérent : interdirait de **supprimer un doublon** créé par erreur sur un client désormais archivé | ✅ mais **flou** : « contrôlée » n'est défini nulle part |
| **Conservation historique** | ✅ (rien n'est détruit) | ✅ | ✅ | ✅ |
| **Correction d'erreur de saisie** | ✅ **reste possible** | ❌ **impose de réactiver** le client pour corriger un chiffre — puis de le ré-archiver | ❌ idem, en pire | ✅ |
| **UX** | ✅ simple, message clair, action évidente (« Réactiver ») | ⚠️ boucle réactiver / corriger / ré-archiver | ❌ frustrante | ⚠️ règles à expliquer |
| **Multi-poste** | ✅ garde à l'écriture + course résiduelle bornée (§17) | ✅ | ✅ | ⚠️ surface plus large |
| **Complexité** | **faible** (1 chargement client, 1 garde) | faible+ | faible+ | **élevée** (nouveau concept) |
| **Compatibilité existant** | ✅ **aucune** rupture (aucun client archivé n'a d'ordonnance à ce jour, l'archivage date de P3-2B) | ✅ | ⚠️ | ❌ |
| **Exigence roadmap** | ✅ **exactement** ce qu'exige P3-2B | ✅ (sur-couvre) | ✅ (sur-couvre) | ⚠️ |

### 10.3 Recommandation (client archivé)

> **Politique A — refuser la *création*, autoriser lecture et correction.**
>
> - `CreatePrescriptionUseCase` : **charger le client**, refuser si **introuvable** *(via le contrat `*Found`)* et
>   refuser si **`IsArchived`** ⇒ **`BusinessRuleException`** avec message stable exposé en **constante** (exactement
>   le patron `DeleteCustomerUseCase.CustomerHasHistoryMessage` de P3-2B, réutilisable par l'UI et les tests).
> - `UpdatePrescriptionUseCase` : **ne pas** restreindre. **Justification, et non intuition** : l'archivage d'un
>   client est **réversible et sans cérémonie** (P3-2B : `Archive()`/`Reactivate()`, idempotents, un booléen) — ce
>   n'est **pas** une clôture comptable ni un verrou légal. **Rien dans le dépôt ne prouve** que l'archivage doive
>   figer les données médicales passées ; le prompt met d'ailleurs explicitement en garde contre cette déduction. Une
>   ordonnance mal saisie sur un client archivé doit rester **corrigeable**.
> - `DeletePrescriptionUseCase` : **ne pas** restreindre **sur le critère du client archivé** (la suppression relève
>   d'une problématique **distincte**, traitée en §11–§12).
>
> **Formulation honnête de la règle** : « archivé » signifie **« plus de nouvelle activité »**, **pas**
> « données gelées ». C'est précisément le sens que P3-2B a donné à l'archivage.

---

## 11. Immutabilité de la « source intangible »

### 11.1 La roadmap et le dépôt **divergent** — constat, sans complaisance

> Roadmap : « L'ordonnance / prescription source est la vérité intangible. »

**Le dépôt ne l'implémente d'aucune manière** :

| Question | Réponse **prouvée** |
|---|---|
| `UpdatePrescriptionUseCase` existe ? | **OUI** — édition **libre**, **tous** les champs optiques, **sans aucune validation** |
| `DeletePrescriptionUseCase` existe ? | **OUI** — suppression **physique**, **inconditionnelle** |
| L'UI permet-elle modifier / supprimer ? | **OUI** — `CustomerPrescriptionsViewModel` : boutons **Modifier** et **Supprimer**, ce dernier **sans confirmation** (`// TODO: Ajouter confirmation`) |
| Historique des versions ? | **NON** — aucun |
| L'ordonnance est-elle **copiée** dans `SaleItem` / `OrderItem` ? | **OUI — copie par valeur** |
| Vente / commande gardent-elles une **FK** vers `Prescription` ? | **NON — aucune** (`grep PrescriptionId` sur `src/` : **aucune** FK hors `Prescription` elle-même) |
| Modifier une ordonnance peut-il altérer un document historique ? | **NON** ✅ — les documents portent leurs **propres** colonnes |
| Supprimer une ordonnance peut-il casser une fiche / vente / commande ? | **NON** ✅ — **aucune** dépendance référentielle |
| Une ordonnance **utilisée** peut-elle encore être supprimée ? | **OUI** — et **rien** ne s'y oppose (ni base, ni Application, ni UI) |

### 11.2 La distinction qui change tout

- **Ordonnance source (médicale)** : `Prescription`. **Modifiable et supprimable sans aucune trace.**
- **Copie technique (commerciale)** : `SaleItem` / `OrderItem` portent **leurs propres** `Sphere`, `Cylinder`,
  `Axis`, `Addition`, `PrismValue`, `PrismBase`, `VisualAcuity`. `SaleFormViewModel` (l. 715-736) les **recopie**
  au moment de la vente ; `OrderFormViewModel.AutoFillFromPrescription()` de même (mais **sans le prisme**, §7.1).
- **Fiche de fabrication** : [`FabricationSheetView.axaml`](../../src/MMV.App/Views/Orders/FabricationSheetView.axaml)
  lit **`LensOdItem.Sphere` / `.Cylinder` / `.Axis`** — c'est-à-dire les **copies** (`OrderItem`), **jamais**
  `Prescription`. ✅

**Conséquence majeure, et rassurante** : l'**intégrité des documents historiques est déjà garantie — par la copie,
pas par une règle**. Le risque réel de la suppression/modification n'est **pas** la corruption d'une vente : c'est la
**perte de la trace médicale source** (et donc la capacité à prouver *pourquoi* un verre a été taillé ainsi).

### 11.3 Comparaison des options

| Option | Bénéfice | Coût | Verdict |
|---|---|---|---|
| **A — Édition libre (statu quo)** | nul | contredit frontalement la roadmap ; perte silencieuse de la source médicale | ❌ |
| **B — Verrouillage après utilisation** | intuitif | **impraticable ici** : « utilisée » **n'est pas calculable** — il n'existe **aucune FK** vers `Prescription`. Le lien vente↔ordonnance **n'existe pas en base**. Il faudrait **créer** `SaleItem.PrescriptionId` / `OrderItem.PrescriptionId` ⇒ **migration + backfill impossible** (le lien passé est **irrécupérable**) | ❌ **pour P3-3** |
| **C — Versionnement** | idéal théorique | nouvelle table/colonnes, migration, refonte UI, refonte des lectures | ❌ hors P3-3 |
| **D — Pas de suppression physique, correction encadrée** | protège la source **sans** aucune migration | change une UX existante | ✅ **mais à confirmer** |

**Le point dur à comprendre** : l'option B — la plus « évidente » — est **techniquement infaisable aujourd'hui**,
car la donnée qui permettrait de dire « cette ordonnance est utilisée » **n'a jamais été enregistrée**. C'est un fait
structurel, pas un manque de volonté. Le prétendre faisable en P3-3B serait mentir sur le coût.

### 11.4 Recommandation (immutabilité)

> **Reporter l'immutabilité forte ; sécuriser la suppression tout de suite.**
>
> - **P3-3B ne crée aucun versionnement** et **n'ajoute pas** de FK `SaleItem.PrescriptionId` (le backfill du passé
>   étant impossible, la FK serait **nullable et mensongère** — un lien « absent » ne voudrait rien dire).
> - **P3-3B** ajoute néanmoins la **confirmation explicite** avant suppression (l'UI ne fait rien aujourd'hui : le
>   `TODO` est resté) — c'est le **plus grand gain de sécurité pour le plus faible coût**, exactement le patron
>   validé en **P3-2C** pour les clients.
> - **Le vrai débat — « faut-il interdire la suppression physique d'une ordonnance ? » — doit être tranché par le
>   métier, pas par ce rapport.** Un archivage d'ordonnance (miroir de `Customer.IsArchived`) serait la réponse
>   naturelle, mais **coûte une migration** et **n'est demandé nulle part**. → **candidat P3-3C ou phase dédiée**.

---

## 12. Suppression `Prescription`

| Question | Réponse |
|---|---|
| Physique ou logique ? | **Physique** — `DeleteAsync(entity)` + `SaveChangesAsync`. Aucune suppression logique. |
| Chargement avant suppression ? | **OUI** (`GetByIdAsync`) ⇒ contrat `PrescriptionFound` correct, **aucune écriture** si introuvable ✅ |
| Vérifications d'utilisation ? | **AUCUNE** |
| Cascade ? | **AUCUNE au départ** de `Prescription` : **rien** n'en dépend |
| FK bloquantes ? | **AUCUNE** — la seule FK est **entrante** (`Prescriptions.CustomerId → Customers`, `Restrict` depuis P3-2B) : elle protège le **client**, pas l'ordonnance |
| Entités dépendantes ? | **AUCUNE** |
| Message UI / confirmation ? | **Aucune confirmation** — `// TODO: Ajouter confirmation` ; suppression **immédiate** au clic. Erreurs affichées via un `catch` **générique** |
| Rechargement après suppression ? | **OUI** — `await LoadPrescriptionsAsync()` recharge **depuis la source** ✅ (conforme à l'esprit multi-poste de P3-2C) |
| Tests ? | 2 (`ExecuteAsync_DeletesExistingPrescription_AndPersists`, `…_PrescriptionNotFound_ReturnsNotFound_AndDoesNotWrite`) + gardes de constructeur |

### Réponses explicites

1. **Que supprime réellement un `DELETE` ?** → **La ligne `Prescriptions`, et rien d'autre.** Aucune cascade.
2. **Une vente / commande peut-elle perdre des données ?** → **NON.** ✅ Elles portent leurs **propres** colonnes
   optiques. Un `DELETE` d'ordonnance **ne touche aucun document**.
3. **Valeurs copiées ou référencées ?** → **COPIÉES** (par valeur, à la vente/commande). **Aucune** référence.
4. **La base protège-t-elle une ordonnance utilisée ?** → **NON** — et elle **ne le peut pas** : « utilisée » n'est
   **pas exprimé** dans le schéma.
5. **Cohérent avec « source intangible » ?** → **NON.** C'est **la** contradiction franche entre roadmap et dépôt.
6. **Migration nécessaire pour sécuriser ?** → **NON pour l'intégrité des documents** (déjà garantie par la copie).
   **OUI seulement** si l'on décide d'**archiver** les ordonnances au lieu de les supprimer (colonne `IsArchived`) —
   décision **non prise** et **non requise** par la roadmap P3-3.

---

## 13. Repositories et EF Core

**Interface** [`IPrescriptionRepository`](../../src/MMV.Domain/Interfaces/Repositories/IPrescriptionRepository.cs) —
hérite `IGenericRepository<Prescription, long>` + `GetByCustomerIdAsync`, `GetByDateRangeAsync`,
`GetLatestByCustomerIdAsync`, **`ExistsByCustomerIdAsync`** (ajoutée en P3-2B).

**Configuration & snapshot — concordants** :

| Élément | Valeur (Fluent **et** snapshot) |
|---|---|
| **PK** | `PrescriptionId`, `INTEGER`, `ValueGeneratedOnAdd` |
| **FK** | `CustomerId → Customers.CustomerId`, **`IsRequired`**, **`OnDelete(DeleteBehavior.Restrict)`** *(P3-2B)* |
| **Index** | **`HasIndex("CustomerId")`** ✅ (les lectures par client sont indexées) |
| **Nullabilité** | tous les champs optiques **nullable** ; `CustomerId` / `IssueDate` **requis** |
| **Numérique** | `Sphere`/`Cylinder`/`Addition`/`PrismValue` ⇒ **`REAL`** (`double`, 8 octets IEEE-754) ; `Axis` ⇒ `INTEGER` |
| **`decimal` vs `double`** | l'argent est en `decimal` ; l'optique en `double` — **séparation correcte** ; les pas au quart de dioptrie sont **exacts** en binaire (§4.7) |
| **Enum** | `PrismBase` ⇒ **`HasConversion<string>()`** ⇒ stocké **en texte** (`"In"`, `"Out"`, `"Up"`, `"Down"`) |
| **Longueurs** | `DoctorName` 200 · `VisualAcuity` 10 · `Notes` 2000 |
| **`DateTime`** | **aucun défaut SQL** (R-19 / P2A-1R19) — horodatage applicatif |
| **Contraintes** | **aucun `CHECK`**, **aucune** contrainte d'unicité |
| **Token de concurrence** | **AUCUN** |
| **Relations indirectes** | **AUCUNE** — recherche `PrescriptionId|HasOne|WithMany|DeleteBehavior` sur `src/MMV.Domain`, `src/MMV.Infrastructure`, `src/MMV.Application` : **aucune** entité ne référence `Prescription` |

**Migrations ayant touché `Prescriptions`** :
- `20260127184542_InitialCreate` — création de la table ;
- `20260713215132_AddCustomerArchivingAndProtectHistory` (**P3-2B**) — FK `Cascade` → **`Restrict`** (reconstruction
  de table SQLite, prouvée non destructive).

**Vérifié dans les trois sources** (Fluent, snapshot, SQL de migration), **pas seulement** sur les navigations C#.

---

## 14. UI Ordonnances

**Fichiers** : `PrescriptionFormViewModel`, `CustomerPrescriptionsViewModel`, `PrescriptionDetailViewModel` ·
vues `PrescriptionFormView`, `CustomerPrescriptionsView`, `PrescriptionDetailView` · tests
`PrescriptionFormViewModelTests`, `CustomerPrescriptionsViewModelTests`, `PrescriptionDetailViewModelTests`.

| Aspect | Constat |
|---|---|
| **Création** | `ExecuteCreate()` → `PrescriptionFormViewModel { CustomerId }`. **Le client n'est jamais rechargé ni vérifié.** |
| **Modification** | `ExecuteEdit(dto)` → `LoadPrescription(dto)` ⇒ `_prescriptionId > 0` ⇒ `Update`. Édition **libre de tous les champs**. |
| **Suppression** | `ExecuteDeleteAsync` — **immédiate**, `// TODO: Ajouter confirmation`. |
| **Confirmation** | ❌ **absente** (les clients, eux, l'ont depuis P3-2C ⇒ **incohérence d'UX** dans le produit). |
| **Messages d'erreur** | `catch (Exception ex)` **générique** ⇒ « Erreur lors de la sauvegarde : … ». **Une `BusinessRuleException` P3-3B y tomberait et s'afficherait comme un incident technique.** |
| **Mapping vide/null/zéro** | ✅ **Correct** : `TextBox` liés à `double?`/`int?` ⇒ **vide ⇒ `null`** ; `ClearForm()` remet **`null`**. **Aucune** conversion vide → 0. |
| **Bornes des contrôles** | `TextBox` **libres** (pas de `NumericUpDown`, pas de `Minimum`/`Maximum`) ; bornes vérifiées **après coup** en C#. |
| **Valeurs par défaut** | `IssueDate = DateTimeOffset.Now` (locale, convertie en UTC à l'envoi) ; tout le reste `null`. |
| **Listes prismatiques** | ❌ **Aucune.** `PrismBase` est un **`TextBox` libre**, *watermark* **« H/V/In/Out »** — **`H` et `V` n'existent pas** dans l'enum (`In`/`Out`/`Up`/`Down`). ⇒ saisie non convertible ⇒ **base `null` silencieuse** alors que la valeur du prisme est enregistrée. |
| **Validation UI** | 8 méthodes en dur (sphère/cyl/axe/add × OD/OG) + **`DoctorName` requis**. **Aucune** validation du prisme, de l'acuité, de la date. |
| **Duplication avec le Domain** | ✅ **totale** sur les 4 plages ; **et divergente** sur `DoctorName` (§5.4). |
| **Client sélectionné** | simple `long CustomerId`, **jamais revalidé**. |
| **Comportement si client archivé** | **Aucun.** Ni blocage, ni avertissement, ni message. |
| **Écran resté ouvert ⇒ écriture après archivage** | ✅ **OUI, possible** (§17, course 4). |
| **Rechargement multi-poste** | ✅ `LoadPrescriptionsAsync()` après **toute** mutation (création, édition, suppression) — la collection locale n'est pas la vérité. |
| **Affichage OD/OG** | symétrique, deux colonnes ; `PrescriptionDetailView` et `FabricationSheetView` en lecture. |

> *Détail relevé sans conséquence fonctionnelle* : `PrescriptionFormView.axaml` (l. 97) contient un `TextBox`
> « Facultatif » **sans aucune liaison** — contrôle orphelin.

**Aucun fichier UI n'a été modifié.**

---

## 15. Consommateurs secondaires

| Consommateur | Lien à `Prescription` | Nature |
|---|---|---|
| `Sale` | **aucun** | — |
| **`SaleItem`** | **copie par valeur** : `Sphere`, `Cylinder`, `Axis`, `Addition`, `PrismValue`, `PrismBase`, `VisualAcuity`, `UsageType` | **snapshot** (`SaleFormViewModel`, l. 715-736) |
| `Order` | **aucun** | — |
| **`OrderItem`** | **copie par valeur** (mêmes champs) | **snapshot partiel** — `AutoFillFromPrescription()` ne copie **que** sphère/cylindre/axe/addition : **le prisme est perdu** ⚠️ |
| **Fiche de fabrication** | lit `LensOdItem` / `LensOgItem` (= `OrderItem`) | **jamais** `Prescription` ✅ |
| `CustomerPrescriptions` | `ListPrescriptionsByCustomerUseCase` | lecture |
| Détails client / impressions / exports | via les DTO ci-dessus | lecture |
| `GetSaleFormReferenceDataUseCase` | `GetLatestByCustomerIdAsync` ⇒ `PrescriptionListItemDto` | **lecture seule**, pour pré-remplissage et suggestion de verre |

### Réponses

- **Données copiées au moment de la vente/commande ?** → **OUI**, par valeur.
- **Restent-elles liées à `Prescription` ?** → **NON.** **Aucune FK, aucun identifiant conservé.** Le lien
  vente ↔ ordonnance **n'existe pas en base** ; il n'est **pas reconstituable** *a posteriori*.
- **Une modification ultérieure de `Prescription` change-t-elle l'historique affiché ?** → **NON** ✅ — les documents
  sont **déjà immuables de fait**.
- **La future transposition doit-elle lire `Prescription` ou une copie figée ?** → **La copie figée** (`OrderItem` /
  `SaleItem`) : la fiche atelier décrit **ce qui a été commandé**, pas ce que l'ordonnance dit *aujourd'hui*. C'est
  déjà ce que fait `FabricationSheetView`. **Ne pas l'inverser en P3-6B.**
- **Quelles dépendances conditionnent l'immutabilité ?** → **Aucune.** L'immutabilité des **documents** est acquise
  (copie). Seule la **source médicale** est non protégée (§11).

---

## 16. Transposition optique — frontière de P3-3

**Constat : la transposition n'existe nulle part dans le dépôt.** Recherche de
`Transpos|Sphere \+ Cylinder|Axis \+ 90|\+ 90` sur **tous** les `.cs` : **zéro occurrence**. Ni service, ni
utilitaire, ni UI, ni rapport. **Aucune convention d'axe implémentée** ; les valeurs sont **affichées brutes**
(`FabricationSheetView` : `Sphere`, `Cylinder`, `Axis` tels quels).

**Types numériques utilisés** (à respecter le jour venu) : `double?` pour sphère/cylindre, **`int?`** pour l'axe.

**Formule cadrée (rappel)** :

```
Sphere'   = Sphere + Cylinder
Cylinder' = −Cylinder
Axis'     = Axis + 90   →  si > 180 : −180   ;   si = 0 : normaliser à 180
```

### Frontière retenue

| Appartient à **P3-3** | Reste pour **P3-6B** |
|---|---|
| **Figer les invariants** dont la transposition dépend : `Cylinder ≠ 0 ⇒ Axis ∈ [0,180]` requis ; `Cylinder` nul/absent ⇒ `Axis` **null** ; **normalisation `0 → 180`** | **Le calcul** de transposition (`OpticalPrescriptionTranspositionService`, Domain **pur**, sans état) |
| Garantir qu'un axe **orphelin** ou **manquant** ne puisse plus entrer en base | Sa **consommation** par `GenerateWorkshopSheetUseCase` (notation atelier) |
| — | Ses tests exhaustifs (signes, bornes, 0/180, nulls, cylindre nul) |

**Garantie que l'ordonnance originale n'est jamais mutée** : la transposition sera une **projection de lecture** —
fonction **pure** `(sphère, cylindre, axe) → (sphère', cylindre', axe')`, **sans** repository, **sans** `SaveChanges`,
appliquée à une **copie** (`OrderItem`), **jamais** à l'entité `Prescription`. Les notes de conception le disent
déjà : « **Ne pas** injecter la transposition dans la saisie/écriture d'ordonnance ».
**P3-3B n'implémente aucune transposition** — il **prépare** seulement ses invariants.

---

## 17. Multi-poste et concurrence

Rappel du socle **ADR-PROD-DB-001** et de la leçon **P3-2B** : *un « check-then-act » applicatif n'est **pas**
atomique ; seule la base est un rempart.*

### Course 1 — Client archivé pendant la création
Poste A charge le client (actif) → Poste B l'archive → Poste A crée l'ordonnance.
**La vérification préalable seule est-elle suffisante ? → NON.** Entre le `GetByIdAsync(customer)` et le
`SaveChangesAsync`, l'archivage de B peut s'intercaler : l'ordonnance **passe**.
**Rempart base possible ?** Une FK **ne peut pas** exprimer « le client ne doit pas être archivé » (ce n'est pas une
contrainte référentielle). Il faudrait un **`CHECK` inter-tables** — **impossible en SQLite** — ou un **trigger**
(non provider-neutre, hors ADR). **⇒ Aucun rempart atomique n'est disponible à coût raisonnable.**
**Risque résiduel accepté** (à documenter, comme en P3-2B) : **fenêtre de course étroite**, **aucune perte de
donnée**, situation **corrigeable** (réactiver, ou supprimer l'ordonnance indûment créée). L'issue est **sûre**.

### Course 2 — Deux modifications simultanées
`Prescription` **n'a aucun token de concurrence** (§4.4). EF exécute un `UPDATE` **sans clause de version** :
**le dernier écrivain gagne, en silence**. La première modification est **perdue sans avertissement**.
**Cas particulièrement traître ici** : `UpdatePrescriptionUseCase` réécrit **tous** les champs optiques depuis la
commande. Deux postes corrigeant **des yeux différents** (A l'OD, B l'OG) ⇒ **la correction de A est écrasée**, alors
qu'ils ne se marchaient pas dessus. **Aucun test ne couvre ce cas.**
**Solution propre** : jeton de concurrence (`RowVersion`) ⇒ **migration** + gestion de `DbUpdateConcurrencyException`
⇒ **hors périmètre P3-3B**, à traiter **transversalement** (ADR-PROD-DB-001 le prévoit déjà comme un chantier global).

### Course 3 — Suppression pendant utilisation
Un poste supprime l'ordonnance pendant qu'un autre l'utilise pour une vente / commande / fiche.
**La base bloque-t-elle ? → NON** (aucune FK entrante). **Mais aucune donnée n'est perdue** : le poste utilisateur
travaille sur une **copie en mémoire** qu'il recopiera dans `SaleItem`/`OrderItem`. La vente **aboutit**, le document
**est correct et complet**. Seule la **source** disparaît. **Issue sûre, dégradation documentaire.**

### Course 4 — Écran ancien
Un écran de création reste ouvert pendant que le client est archivé.
**Le picker filtré suffit-il ? → NON**, comme pressenti — et c'est **prouvé** : `PrescriptionFormViewModel.CustomerId`
est un `long` **capturé une fois** ; il n'est **jamais revalidé** ; `SavePrescriptionAsync` l'envoie **tel quel**.
Le filtrage du picker (P3-2B/P3-2C) n'agit **qu'à la sélection**. **Le garde-fou à l'écriture est indispensable.**

### Solutions évaluées

| Solution | Verdict |
|---|---|
| **Contrôle Application** (charger le client, refuser si archivé) | ✅ **retenu** — élimine 100 % des cas **non concurrents** (écran ancien, identifiant fourni, sélection obsolète) et fournit le **message métier clair** |
| **Transaction** | ❌ inutile : mono-écriture ; une transaction **ne sérialise pas** contre l'archivage d'une autre connexion |
| **Contrainte base** | ❌ **indisponible** — pas de `CHECK` inter-tables en SQLite ; trigger non provider-neutre |
| **Verrouillage** | ❌ disproportionné pour un poste de magasin |
| **Token de concurrence** | ⏭️ **chantier transverse** (course 2), **pas** P3-3B |
| **PostgreSQL / SQL Server** | ⏭️ à réévaluer au portage (ADR-PROD-DB-001) |
| **Parsing d'exception SQLite** | ❌ **exclu** (fragile, non provider-neutre) — cohérent avec P3-2B §12 |

**Aucun parsing d'exception n'est proposé. Aucune lecture suivie d'un `SaveChanges` n'est supposée atomique.**

---

## 18. Tests existants — cas déjà couverts

**645 tests** au total (App 211 · Application 198 · Domain 236). Périmètre Ordonnances :

### Domain — `PrescriptionValidatorTests` (4)
| Test | Comportement prouvé |
|---|---|
| `Validate_WithValidPrescription_ShouldPass` | plages nominales acceptées |
| `Validate_WithFutureDate_ShouldFail` | `IssueDate` > J+1 refusée |
| `Validate_WithOutOfRangeSphere_ShouldFail` | sphère 25 refusée |
| `Validate_WithValidRanges_ShouldPass` | bornes exactes (−20, −6, 180, 20, **0**, **0**) acceptées — ⚠️ **contient `OgCylinder = 0` + `OgAxis = 0`** : **à amender** en P3-3B (§6.1) |

### Domain — `PrescriptionServiceTests` (4)
Lecture, création (avec refus de date future ⇒ `BusinessRuleException`), mise à jour, entité introuvable — **d'un
service que personne n'utilise** (§9.4).

### Application (Create 5 / Update 6 / Delete 5 / List 4)
Création + persistance · **non-normalisation** des champs optionnels · mise à jour + persistance ·
**préservation de `CustomerId` et `CreatedAt`** ✅ · introuvable ⇒ **aucune écriture** (Update **et** Delete) ✅ ·
suppression + persistance · projection triée par `IssueDate` décroissante et filtrée par client · commandes nulles ·
gardes de constructeur.

### App / ViewModel
`PrescriptionFormViewModelTests` (validation UI des 4 plages × 2 yeux, `DoctorName` requis, création vs édition) ·
`CustomerPrescriptionsViewModelTests` (chargement, création, édition, suppression, rechargement) ·
`PrescriptionDetailViewModelTests`.

### Infrastructure / SQLite
FK `Prescriptions → Customers = RESTRICT` **prouvée sur vrai SQLite** (`CustomerDeletionConstraintsTests`,
`CustomerArchivingMigrationTests`, P3-2B) — **protège le client, pas l'ordonnance**.

---

## 19. Tests manquants

**Aucun test n'est ajouté en P3-3A.** Inventaire des **manques**, tous **non couverts aujourd'hui** :

| # | Cas manquant | Couche cible |
|---|---|---|
| 1 | **cylindre non nul sans axe** ⇒ refus | Domain |
| 2 | **axe sans cylindre** (orphelin) ⇒ refus | Domain |
| 3 | **cylindre = 0** ⇒ axe doit être `null` | Domain |
| 4 | **axe = 0** ⇒ accepté puis **normalisé à 180** | Domain |
| 5 | **axe = 180** ⇒ accepté, inchangé | Domain |
| 6 | **couple prisme / base** (4 combinaisons : ∅∅ ✅, valeur seule ❌, base seule ❌, les deux ✅) | Domain |
| 7 | **`PrismValue = 0`** ⇒ assimilé à « pas de prisme » | Domain |
| 8 | **plage de `PrismValue`** *(si la borne est confirmée par le métier)* | Domain |
| 9 | **ordonnance partielle acceptée** (un seul œil ; un seul champ) | Domain |
| 10 | **symétrie OD/OG** de **chaque** nouvelle règle | Domain |
| 11 | **`IssueDate`** : borne **non figée à la construction** du validateur (§5.2) | Domain |
| 12 | **client archivé** ⇒ `CreatePrescription` **refusée**, **aucune écriture** (ports `MockBehavior.Strict`) | Application |
| 13 | **client introuvable** ⇒ refus **sans** `DbUpdateException` brute | Application |
| 14 | **client actif** ⇒ création **acceptée** (non-régression) | Application |
| 15 | **update sur client archivé** ⇒ **autorisé** (politique A explicitement **testée**, pour qu'un durcissement futur soit un choix conscient) | Application |
| 16 | **validation câblée** : commande hors plage ⇒ `ValidationErrors`, **aucune écriture** | Application |
| 17 | **suppression d'une ordonnance « utilisée »** ⇒ **la vente/commande conserve ses valeurs** | Infrastructure / SQLite |
| 18 | **conservation des copies vente/commande** après **modification** de l'ordonnance source | Infrastructure / SQLite |
| 19 | **course multi-poste** : archivage entre le contrôle et l'écriture ⇒ **issue sûre** documentée | Infrastructure / SQLite |
| 20 | **absence de mutation pendant transposition** (le jour où elle existe — **P3-6B**) | Domain |
| 21 | **UI** : refus métier affiché **en clair** (pas de `catch` générique) ; **confirmation** avant suppression | App |
| 22 | **UI** : `PrismBase` en **`ComboBox`** ⇒ impossible de saisir une base invalide | App |
| 23 | **`NaN`** sur `Sphere`/`Cylinder`/`Addition`/`PrismValue` (OD **et** OG) ⇒ refus | Domain |
| 24 | **`PositiveInfinity`** / **`NegativeInfinity`** sur les mêmes champs ⇒ refus | Domain |
| 25 | **`PrismValue` négative** ⇒ refus | Domain |
| 26 | **`PrismValue` sans borne supérieure** ⇒ une valeur élevée mais finie et positive reste **acceptée** (non-régression de la décision « pas de plafond ») | Domain |

---

## 20. Frameworks et dépendances

| Besoin | Solution **existante** | Alternative envisagée | Impact archi | NuGet | Licence |
|---|---|---|---|---|---|
| Règles de plage et croisées | **FluentValidation** (`PrescriptionValidator`) + **`CommandValidation`** (P3-1) | — | **nul** (déjà en place, prouvé sur Clients) | **aucun ajout** | Apache-2.0 (transitif Domain) |
| Refus métier dur (client archivé) | **`BusinessRuleException`** (Domain, ADR §8, patron **P3-2B**) | `Result` monade | nul | aucun | — |
| Normalisation d'axe `0 → 180` | **méthode Domain pure** | — | nul | aucun | — |
| Transposition (**P3-6B**) | **service Domain pur** (fonction pure) | bibliothèque optique externe | — | **aucun** | — |
| Value Objects optiques (`Eye`, `Correction`) | — | refonte | **churn massif**, réécriture EF + DTO + UI + tests | — | — |
| Machine à états (statut d'ordonnance) | — | — | **aucun besoin démontré** — pas de statut dans le produit | — | — |
| Bibliothèque médicale / optique | — | — | **inutile** : les règles tiennent en quelques lignes | — | — |

> **Décision : AUCUNE nouvelle dépendance.** Réutiliser **FluentValidation** + **`CommandValidation`** ; règles
> optiques en **Domain pur**. **Pas** de framework de règles métier, **pas** de package de transposition, **pas** de
> Value Object massif sans besoin démontré. Rappel : **`FluentAssertions` reste en 6.x** (v7+ commercial) —
> **ne pas toucher**.

---

## 21. Options d'implémentation P3-3B

| | **Option 1** — correctif minimal | **Option 2** — sécurisation ciblée complète | **Option 3** — immutabilité / versionnement | **Option 4** — report de l'immutabilité |
|---|---|---|---|---|
| **Contenu** | règles croisées + garde Create + tests | **Opt. 1** + **câblage réel de la validation** (Create **et** Update) + politique Update/Delete **explicite** + **traduction UI** + tests multi-poste | versionnement, verrouillage après usage, migration, refonte UI | sécuriser incohérences + client archivé **maintenant** ; documenter l'immutabilité pour une phase dédiée |
| **Bénéfices** | rapide | **couvre les 3 risques réels** (validation morte, client archivé, UI trompeuse) ; l'utilisateur **voit** les refus | conforme à la lettre de la roadmap | — |
| **Risques** | ⚠️ **piège majeur** : ajouter des règles à un validateur **jamais appelé** ⇒ **zéro effet réel**, faux sentiment de sécurité. Et le refus tomberait dans le `catch` **générique** de l'UI ⇒ message technique | maîtrisés | **infaisable proprement** : le lien vente↔ordonnance **n'existe pas en base** et **n'est pas reconstituable** (§11.3) | dette **explicitée** |
| **Migration** | ❌ | ❌ **aucune** | ✅ lourde (+ backfill **impossible**) | ❌ |
| **UI** | ❌ | ✅ requise (confirmation, message métier, `ComboBox` prisme) | refonte | ✅ |
| **Tests** | ~10 | **~35–45** | > 80 | — |
| **Multi-poste** | inchangé | garde applicatif + risque résiduel **documenté** | — | idem Opt. 2 |
| **Complexité** | faible | **moyenne** | **très élevée** | — |
| **Conformité roadmap** | **partielle** (n'honore pas vraiment l'exigence P3-2B) | ✅ **totale** pour P3-3 | au-delà | ✅ |
| **Dette résiduelle** | **forte et masquée** | concurrence (token), archivage d'ordonnance, `PrescriptionService` | nulle | immutabilité |

> **L'Option 1 est un piège, et c'est le constat le plus important de cet audit** : enrichir `PrescriptionValidator`
> **sans le câbler** produirait un diff vert, des tests verts… et **strictement aucun changement de comportement en
> production**. Le validateur est **mort** (§5.1).

---

## 22. Recommandation pour P3-3B

> ### **Option 2**, découpée en **P3-3B (métier)** + **P3-3C (UI)** — comme P3-2.

| Question posée | Réponse |
|---|---|
| **Quelles règles croisées ?** | (a) `|Cylinder| > 0` ⇒ `Axis` **requis** ∈ `[0,180]` ; (b) `Cylinder` null/nul ⇒ `Axis` **doit être null** ; (c) `PrismValue` non nul ⇒ `PrismBase` **requise** ; (d) `PrismBase` renseignée ⇒ `PrismValue` **non nul requis**. **Par œil, symétriques.** |
| **`Cylinder = 0` ?** | **= absence d'astigmatisme.** Accepté ; **impose `Axis = null`**. |
| **`Axis = 0` / `180` ?** | **Équivalents.** `0` **accepté puis normalisé à `180`** (convention **déjà fixée** par les notes de transposition — la figer **maintenant**). |
| **`PrismValue` / `PrismBase` ?** | **Couple strict** ; `0` = pas de prisme ⇒ base **null**. **Aucune borne supérieure** (décision retenue, §3 des *Décisions retenues pour P3-3B* — la proposition `[0, 10]` est **écartée**, faute de preuve métier) ; `PrismValue` doit être **finie et non négative**. **Le `TextBox` libre doit devenir un `ComboBox`** — sinon la règle rend l'écran inutilisable. |
| **Ordonnance partielle ?** | **Préservée.** Aucun champ optique rendu obligatoire ; un œil vide reste valide. *(« au moins une valeur » = option à confirmer.)* |
| **Client archivé ?** | **Politique A** : **Create refusée** (`BusinessRuleException`, message en **constante**) ; **Update autorisé** (correction) ; **Delete non restreint sur ce critère**. |
| **Update / Delete restreints ?** | **Update** : **non** restreint, mais **enfin validé** (les règles optiques s'y appliquent). **Delete** : **confirmation UI obligatoire** (P3-3C) ; **pas** d'interdiction — l'interdire exigerait un archivage d'ordonnance, **non demandé**. |
| **Migration nécessaire ?** | **NON.** ✅ Aucune. Toutes les règles sont **applicatives**, appliquées **à la saisie**, **non rétroactives**. |
| **Nouvelle dépendance ?** | **NON.** ✅ FluentValidation + `CommandValidation` + Domain pur. |
| **Reporté à P3-6B ?** | **Le calcul de transposition** et sa consommation en fiche atelier. P3-3 ne fait que **figer ses invariants** (axe cohérent, normalisation `0→180`). |

### Contenu proposé de **P3-3B** (métier, **aucune UI**)
1. **Câbler `CommandValidation`** dans `CreatePrescriptionUseCase` **et** `UpdatePrescriptionUseCase` — *sans quoi
   rien de ce qui suit n'a d'effet.*
2. Enrichir `PrescriptionValidator` : règles croisées (a)–(d) **et** correction de la borne `IssueDate` figée à la
   construction (§5.2). **FluentValidation valide uniquement** — la normalisation d'axe (`0 → 180`) est **exclue**
   du validateur : elle vit dans une méthode **Domain pure et sans effet de bord**, invoquée par les use cases
   **avant** le mapping vers l'entité (voir *Décisions retenues pour P3-3B*, §1 et §9).
3. **Garde « client archivé »** dans `CreatePrescriptionUseCase` : injecter `ICustomerRepository`, charger, refuser
   si introuvable (`*Found`) ou `IsArchived` (**`BusinessRuleException`**, message constant).
4. **Trancher le sort de `PrescriptionService`** (§9.4) : le **supprimer** (recommandé — dead code, DI câblée,
   contourne tous les gardes) ou le faire **déléguer** aux use cases. **Ne pas l'ignorer.**
5. Tests : cas **1–26** du §19.

### Contenu proposé de **P3-3C** (UI)
1. **Confirmation** avant suppression (le `TODO` de `CustomerPrescriptionsViewModel`).
2. **Afficher le refus métier en clair** (attraper `BusinessRuleException`, proposer « Réactiver le client ») — le
   `catch` générique actuel afficherait un message technique.
3. **`PrismBase` : `TextBox` → `ComboBox`** sur les 4 valeurs de l'enum (**correctif de correction de données**, pas
   du confort).
4. Afficher les `ValidationErrors` du résultat ; **dédupliquer** les 8 validations en dur si possible.
5. **Recharger depuis la source** après toute mutation (déjà fait ✅).

---

## 23. Migration potentielle

**AUCUNE migration n'est nécessaire pour P3-3B.** ✅

| Élément | Migration ? | Pourquoi |
|---|---|---|
| Règles croisées cyl/axe, prisme/base | **NON** | validation **applicative**, à la **saisie** ; pas de `CHECK` en base ; données historiques **intactes et lisibles** |
| Normalisation d'axe `0 → 180` | **NON** | à l'**écriture** ; lignes existantes **non réécrites** |
| Garde « client archivé » | **NON** | `Customer.IsArchived` **existe déjà** (P3-2B) |
| Câblage de la validation | **NON** | code applicatif |
| *(hypothétique)* archivage d'ordonnance | **OUI** | ⏭️ **non retenu** — non demandé |
| *(hypothétique)* `RowVersion` | **OUI** | ⏭️ **chantier transverse**, hors P3-3 |
| *(hypothétique)* `SaleItem.PrescriptionId` | **OUI** | ⏭️ **rejeté** — backfill du passé **impossible** (§11.3) |

---

## 24. Risques

1. **🔴 Le validateur Domain est du code mort** — **le risque n°1**. Ajouter des règles sans **câbler**
   `CommandValidation` ne changerait **rien** en production tout en **donnant l'illusion** du contraire.
   *Mitigation* : le câblage est **l'étape 1** de P3-3B, avec un test prouvant qu'une commande invalide **n'écrit pas**.
2. **🔴 Ordonnance créable pour un client archivé** — exigence P3-2B **non tenue**, confirmée par le code
   (`ICustomerRepository` pas même injecté).
   *Mitigation* : garde à l'écriture (politique A).
3. **🟠 L'UI produit des prismes incohérents *par conception*** — `TextBox` libre + watermark **erroné**
   (`H`/`V` inexistants) ⇒ base `null` **silencieuse** avec une valeur de prisme enregistrée. **Ce n'est pas un cas
   limite : c'est le comportement nominal de l'écran.** Corriger la règle **sans** corriger l'écran rendrait la saisie
   du prisme **impossible**.
4. **🟠 Suppression d'ordonnance sans confirmation ni trace** — `TODO` resté, aucune cascade mais **perte définitive
   de la source médicale**. Incohérent avec les clients (protégés depuis P3-2C).
5. **🟠 Aucun token de concurrence** — deux postes corrigeant **des yeux différents** de la même ordonnance :
   **la première correction est écrasée en silence**. **Hors P3-3B**, à traiter transversalement.
6. **🟠 `PrescriptionService` : chemin d'écriture parallèle, en DI, sans garde** — contournerait **tous** les
   garde-fous de P3-3B. Dormant aujourd'hui, **piège demain**.
7. **🟡 Test existant à amender** (`OgCylinder = 0` + `OgAxis = 0`) — **assumé et documenté**, pas un affaiblissement.
8. **🟡 Contradiction `DoctorName`** (UI requis / Domain optionnel) — la règle « vraie » n'est écrite **nulle part** :
   **à trancher** en P3-3B.
9. **🟡 Prisme perdu au pré-remplissage d'une commande** (`OrderFormViewModel.AutoFillFromPrescription()` ne copie
   pas `PrismValue`/`PrismBase` alors qu'`OrderItem` les porte) — **bug préexistant**, relève de **P3-6** (Commandes).
10. **🟡 Course « archivage pendant création »** — **aucun rempart atomique disponible** (pas de `CHECK` inter-tables
    en SQLite). **Risque résiduel accepté** : **aucune perte de donnée**, situation **corrigeable**.
11. **🟢 Ordonnance totalement vide persistable** — mineur, **à confirmer** plutôt qu'à durcir d'office.

---

## 25. Fichiers modifiés

**Créé (1)** : `docs/implementation/P3-3A-prescription-domain-audit-report.md` — **ce rapport**.

**Modifié : AUCUN.**

- `src/**` — **intact** ✅
- `tests/**` — **intact** ✅
- `src/MMV.Infrastructure/Migrations/**` — **intact** ✅
- `.github/**`, `*.csproj`, `*.sln`, `global.json`, `dotnet-tools.json` — **intacts** ✅
- `docs/architecture/P3-business-rules-roadmap.md` — **NON modifiée** (conformément à la consigne d'arrêt) ✅

---

## 26. Contrôles exécutés

`git branch --show-current` · `git status --short` · `git log -10 --oneline` · `git diff --stat` · `git diff --check` ·
`gh run list` · `gh run view 29294696114 --json …` ·
`dotnet restore` · `dotnet build --no-restore -c Debug` · `dotnet test --no-build -c Debug` ·
`dotnet list MMV.sln package --vulnerable --include-transitive` · `dotnet tool restore` ·
`dotnet ef migrations has-pending-model-changes --no-build` ·
`dotnet list src/MMV.Application/MMV.Application.csproj reference` / `… package` ·
lectures intégrales (`Prescription`, `PrescriptionValidator`, `PrescriptionService`, les 4 use cases,
`PrescriptionConfiguration`, `IPrescriptionRepository`, `PrescriptionFormViewModel`, `CustomerPrescriptionsViewModel`,
`SaleItem`, snapshot EF, vues `.axaml`) ·
recherches `rg` (`Prescription|Sphere|Cylinder|Axis|Addition|Prism|PrismBase` ; `PrescriptionId` ;
`PrescriptionValidator|CommandValidation|IValidator<Prescription>` ; `Transpos|Axis \+ 90` ;
`HasOne|WithMany|DeleteBehavior`).

---

## 27. Résultats

| Contrôle | Attendu | Résultat |
|---|---|---|
| Branche | `p3-business-rules` | ✅ |
| CI du dernier commit | verte | ✅ `29294696114` — `success` sur `8f019fb…` |
| Build | vert | ✅ **0 avertissement, 0 erreur** |
| Tests | ≥ 645 | ✅ **645** (App 211 · Application 198 · Domain 236) — **0 échec** |
| Vulnérabilités | 0 | ✅ **0** (7 projets) |
| Migration en attente | aucune | ✅ « No changes … since the last migration » |
| `MMV.Application` pure | Domain + DI.Abstractions | ✅ |
| Sources modifiées | **aucune** | ✅ |
| Tests modifiés | **aucun** | ✅ |
| Migration créée | **aucune** | ✅ |
| Dépendance ajoutée | **aucune** | ✅ |
| Fichiers non suivis | **le rapport P3-3A seul** | ✅ |
| `git diff --check` | propre | ✅ |

---

## Décisions retenues pour P3-3B

> Cette section **verrouille** les points laissés ouverts par l'analyse ci-dessus. Elle prime sur toute formulation
> antérieure du présent rapport en cas de divergence (notamment §7.2 point 4, §22 et §29 point 1, corrigés en
> conséquence ci-après).

### 1. Cylindre et axe

- `Cylinder` null ou égal à zéro :
  - `Axis` doit être `null`.
- `Cylinder` non nul et différent de zéro :
  - `Axis` est **obligatoire** ;
  - `Axis` doit être compris entre 0 et 180.
- `Axis = 0` et `Axis = 180` représentent la **même orientation** dans la convention produit.
- `Axis = 0` sera **normalisé vers 180** avant persistance.
- **Cette normalisation ne doit pas être réalisée dans `PrescriptionValidator`.** FluentValidation **valide
  uniquement**. Une fonction ou méthode **Domain pure et sans effet de bord** doit produire la valeur canonique
  **avant** le mapping vers l'entité.
- Les lignes historiques **ne sont pas** migrées ni réécrites.

### 2. Valeurs numériques finies

Exigence ajoutée pour P3-3B :

- toutes les valeurs optiques de type `double` doivent être **finies** ;
- rejeter `double.NaN` ;
- rejeter `double.PositiveInfinity` ;
- rejeter `double.NegativeInfinity` ;
- appliquer cette règle à `Sphere`, `Cylinder`, `Addition` et `PrismValue`, pour OD **et** OG.

Aucune contrainte SQL ni migration n'est ajoutée pour cette règle.

### 3. Prisme

Décision retenue :

- `PrismValue` null et `PrismBase` null : **valide** ;
- `PrismValue` strictement positive : `PrismBase` **obligatoire** ;
- `PrismBase` renseignée : `PrismValue` strictement positive **obligatoire** ;
- `PrismValue` égale à zéro représente l'**absence de prisme** :
  - `PrismBase` doit être `null` ;
  - la valeur zéro peut rester zéro, aucune conversion obligatoire vers `null` ;
- `PrismValue` **négative** : refusée ;
- **aucune borne supérieure métier n'est imposée en P3-3B.**

Précision explicite : la proposition `[0, 10]` évoquée en §7.2 et §22 **n'est pas retenue**, faute de preuve métier
dans le dépôt. Une borne supérieure pourra être introduite après validation par un opticien ou selon un besoin
produit réel — **ne pas inventer une limite clinique.**

### 4. `DoctorName`

Décision retenue :

- `DoctorName` reste **facultatif** dans Domain et Application ;
- l'obligation UI actuelle est une **divergence sans propriétaire métier démontré** ;
- **P3-3C** devra aligner l'UI en supprimant l'obligation stricte ;
- une obligation légale ou pays ne doit **pas** être codée dans le socle générique sans pack pays ou exigence
  confirmée.

### 5. Ordonnance partielle ou vide

Décision retenue :

- préserver les ordonnances partielles ;
- un seul œil peut être renseigné ;
- un seul champ optique peut être renseigné si les règles croisées restent cohérentes ;
- **ne pas ajouter** en P3-3B de règle « au moins une valeur optique obligatoire » ;
- l'ordonnance totalement vide reste **techniquement autorisée** pour conserver l'iso-comportement ;
- ce point pourra être réévalué après le pilote terrain.

### 6. Client archivé

Politique retenue :

- `CreatePrescriptionUseCase` doit **charger `Customer`** ;
- client **introuvable** :
  - retourner un contrat explicite (`CustomerFound = false` ou le contrat *NotFound* homogène réellement utilisé par
    P3-1) ;
  - **ne pas laisser** une `DbUpdateException` FK devenir le chemin nominal ;
- client **archivé** :
  - refuser la création avec `BusinessRuleException` ;
  - message stable exposé en **constante** ;
  - **aucune écriture** ;
- `UpdatePrescriptionUseCase` reste **autorisé** pour corriger une ordonnance existante, même si le client est
  archivé ;
- `DeletePrescriptionUseCase` **n'est pas restreint** sur le seul critère `IsArchived`.

### 7. Suppression et immutabilité

Décision retenue pour P3-3 :

- aucun versionnement d'ordonnance ;
- aucune FK `PrescriptionId` ajoutée à `SaleItem` ou `OrderItem` ;
- aucune migration d'archivage d'ordonnance ;
- aucune prétention à une immutabilité forte de la source ;
- **P3-3C** ajoute une **confirmation explicite** avant suppression physique ;
- la dette « conservation/versionnement de la source médicale » reste **documentée** pour une phase future ;
- les copies techniques de `SaleItem` et `OrderItem` restent la source des documents historiques.

### 8. `PrescriptionService`

Décision retenue :

- **P3-3B** doit rechercher une **dernière fois** tous les appels **runtime** à `IPrescriptionService` et
  `PrescriptionService` ;
- si **aucun** consommateur réel n'existe :
  - supprimer `IPrescriptionService` ;
  - supprimer `PrescriptionService` ;
  - supprimer son enregistrement DI ;
  - supprimer ou remplacer ses tests devenus sans objet ;
- **ne pas conserver** un deuxième chemin d'écriture dormant contournant les use cases ;
- si un consommateur réel est découvert, **STOP** et documenter avant de choisir une délégation.

### 9. Validation et normalisation — ordre d'exécution

Ordre prévu dans `Create` et `Update` :

1. construire un modèle de validation à partir de la commande ;
2. exécuter `PrescriptionValidator` via `CommandValidation` ;
3. si invalide :
   - retourner `ValidationErrors` ;
   - **aucune écriture** ;
4. **normaliser** les valeurs canoniques avec du code **Domain pur** (ex. `Axis 0 → 180`) ;
5. construire ou modifier l'entité ;
6. sauvegarder.

**Ne jamais** faire muter la commande ou l'entité par FluentValidation.

### 10. Résultats Application

**P3-3B** devra :

- ajouter `ValidationErrors` et `IsValid` aux résultats `CreatePrescription` et `UpdatePrescription` selon la
  convention P3-1 ;
- conserver `PrescriptionFound` pour Update ;
- définir explicitement `CustomerFound` pour Create ou adopter le contrat *NotFound* homogène prouvé dans le
  dépôt ;
- **ne pas** transformer une validation normale en exception ;
- réserver `BusinessRuleException` au refus métier dur « client archivé ».

---

## 28. Verdict

### **P3-3A = GO local.**

- Audit **complet** des 9 axes demandés ✅
- **20 propriétés** et règles **réelles** documentées (entité **anémique**, **aucun** statut/version/token) ✅
- Comportement **create / update / delete** documenté — y compris le fait que **Create ne vérifie rien** ✅
- **Client archivé** traité : le trou est **prouvé**, 4 politiques comparées, **politique A** recommandée ✅
- **Immutabilité analysée sans l'inventer** : la roadmap **diverge du dépôt** ; les **documents** sont déjà immuables
  (copie par valeur), la **source** ne l'est pas ; le verrouillage « après usage » est **techniquement infaisable**
  (aucune FK, lien passé **irrécupérable**) ✅
- **Cascades / FK confirmées** sur Fluent **+ snapshot + SQL** : **aucune** entité ne référence `Prescription` ✅
- **UI auditée** — dont le **`TextBox` de base prismatique** au watermark erroné ✅
- **Multi-poste analysé** (4 courses) ; **aucun** parsing d'exception proposé ; risque résiduel **assumé** ✅
- **4 options comparées**, recommandation **nette** ✅
- **Aucune implémentation**, baseline finale **verte** ✅

**Découverte structurante** : **`PrescriptionValidator` n'est appelé par personne.** Toute la « validation
optique » du produit repose aujourd'hui sur **l'UI seule**. C'est ce qui fait la différence entre l'Option 1
(cosmétique) et l'Option 2 (réelle).

**Aucun commit, aucun push.**

---

## 29. Prochaine étape — découpage proposé

### **P3-3B — Métier + validation** (`IMPLEMENTATION_AND_REPORT`, **aucune UI**, **aucune migration**)
1. **Câbler `CommandValidation`** dans `Create` **et** `UpdatePrescriptionUseCase` — *prérequis absolu*.
2. Règles croisées dans `PrescriptionValidator` (cyl ⇄ axe, prisme ⇄ base) et correction de la borne `IssueDate`.
   Normalisation `0 → 180` implémentée **hors validateur**, en code Domain pur (§9 des *Décisions retenues*).
3. Garde **client archivé** dans `CreatePrescriptionUseCase` (`BusinessRuleException` + message constant).
4. **Trancher `PrescriptionService`** (suppression recommandée).
5. Tests **1–26** (§19). Attendu : **~685–696** tests.

### **P3-3C — UI Ordonnances** (`IMPLEMENTATION_AND_REPORT`)
1. **Confirmation** avant suppression.
2. Refus métier **affiché en clair** (proposer « Réactiver le client »).
3. **`PrismBase` : `ComboBox`** (correctif d'intégrité de saisie).
4. Affichage des `ValidationErrors` ; déduplication des validations en dur.

### Questions **métier** déjà tranchées (voir *Décisions retenues pour P3-3B*)
1. ~~Borne de `PrismValue`~~ — **tranché** : aucune borne supérieure en P3-3B (seulement finie et non négative).
2. ~~`DoctorName` obligatoire ou facultatif~~ — **tranché** : reste facultatif en Domain/Application ; l'UI sera
   alignée en P3-3C.
3. ~~Ordonnance totalement vide~~ — **tranché** : reste techniquement autorisée en P3-3B (iso-comportement),
   réévaluable après le pilote terrain.
4. ~~Suppression d'ordonnance : confirmation seule, ou archivage ?~~ — **tranché pour P3-3** : confirmation UI
   seule (P3-3C) ; l'archivage d'ordonnance (migration) reste une dette documentée pour une phase future.

> **Non traité en P3-3, à inscrire ailleurs** : jeton de concurrence (**transverse**, ADR-PROD-DB-001) ·
> prisme perdu au pré-remplissage des commandes (**P3-6**) · refus de **vente** pour client archivé (**P3-7**) ·
> **transposition** (**P3-6B**).
