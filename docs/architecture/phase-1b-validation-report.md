# PHASE 1B — RAPPORT DE CORRECTION ET DE VALIDATION DE L'AUDIT TECHNIQUE

> **Date : 9 juin 2026.** Passe de correction factuelle et de cohérence sur les cinq livrables de la Phase 1, **avant tout démarrage de la Phase 2**.
>
> **Aucun code applicatif, aucun test, aucune migration n'a été modifié.** Seuls les documents Markdown de `docs/architecture/` ont été touchés. Aucune décision d'ADR n'est présentée comme définitivement acceptée. Aucune valeur réglementaire n'est codée ni déduite.

---

## 1. Erreurs détectées

| # | Document(s) | Erreur / faiblesse détectée | Type |
|---|---|---|---|
| E-01 | audit §3.3 | « 17 `DbSet` » alors qu'`OpticDbContext` en déclare **18** (lignes 23-115) | Erreur factuelle |
| E-02 | audit §1/§3.3/§6, dependency-map V4 | « trois chemins de base de données » : en réalité **2 chemins physiques distincts** répartis sur **4 sites de configuration** (la chaîne `%LOCALAPPDATA%` apparaît dans 3 sites) | Erreur factuelle |
| E-03 | audit §4.8 | « Risque N+1 via navigations `virtual` » : **lazy loading désactivé** (aucun proxy) → lien causal `virtual`⇒N+1 **infondé** | Lien causal erroné |
| E-04 | adr-candidates ADR-004 | « colonnes `decimal`/`numeric` » présentées comme solution correcte sous SQLite, sans séparer domaine / représentation / provider | Imprécision technique |
| E-05 | adr/risk | Concurrence imposée comme `RowVersion` (inexistant en SQLite auto-géré) | Imprécision technique |
| E-06 | adr ADR-003, roadmap | `Database.Migrate()` au démarrage présenté comme acquis | Décision prématurée |
| E-07 | roadmap §3/§6 | `Down()` présenté comme mécanisme de restauration des données | Stratégie erronée |
| E-08 | roadmap §3/§4/§7 | Contradictions de dépendances (use cases avant couche Application ; slice dépendant d'étapes postérieures ; « après étapes 0-3 » incorrect) | Incohérence |
| E-09 | roadmap §7 | « moteur fiscal à taux 0 » : un 0 % est une vraie règle, pas une absence de configuration | Modélisation erronée |
| E-10 | audit §4.7, risk R-08, adr ADR-009 | Absences (audit/chiffrement/multi-tenant) formulées comme non-conformité juridique | Sur-affirmation |
| E-11 | audit §5 | Table de confrontation incomplète (manquaient : risque, dette, remédiation, prérequis, confiance) | Complétude |
| E-12 | tous | Contrôle des dépendances vulnérables et analyse de livraison non réalisés | Complétude |
| E-13 | audit §9 | Verdict unique « GO CONDITIONNEL » sans distinguer Phase 2A des fonctionnalités nationales | Clarté |

---

## 2. Corrections réalisées

| Réf. | Correction apportée | Où |
|---|---|---|
| C-01 | `17` → **`18`** `DbSet` (recompté lignes 23-115) | audit §3.3 |
| C-02 | Reformulation : **2 chemins physiques / 2 chaînes / 4 sites** de configuration | audit §1, §3.3, §6 ; dependency-map V4 ; risk R-04 |
| C-03 | Retrait du lien `virtual`⇒N+1 ; ajout du constat **lazy loading désactivé (vérifié)** + over-fetching d'`Include` ; seuls les problèmes démontrés (chargement complet + filtrage mémoire) conservés | audit §4.8 |
| C-04 | ADR-004 réécrit : **5 options** comparées sur précision/agrégations/tri/migrations/portabilité/complexité/performance/auditabilité ; **séparation domaine / représentation / provider** ; **owned type EF non recommandé définitivement** | adr ADR-004 |
| C-05 | Terme générique **« concurrency token »** ; comparaison Guid / version entière / update atomique conditionnel / isolation transactionnelle / token natif serveur ; **gestion `DbUpdateConcurrencyException`** dans critères et tests | adr ADR-010 (nouveau) ; risk R-09 ; roadmap §5 |
| C-06 | ADR-003 : **4 options de déclencheur** (démarrage / outil dédié / installeur / pipeline) + spécificités SQLite desktop (sauvegarde, détection d'instance, verrou, reprise, vérification de schéma, journal, restauration) ; `Migrate()` non imposé | adr ADR-003 ; roadmap §2/§3 |
| C-07 | Stratégie de rollback **à 5 mécanismes** (code / schéma / données / forward-fix / fichier SQLite) ; `Down()` non garant des données ; sauvegarde vérifiée + contrôle d'intégrité obligatoires pour migrations destructrices | adr ADR-003 ; roadmap §6 |
| C-08 | Roadmap **réordonnée** : couche Application (Étape 2) **avant** les use cases transactionnels ; lots **séparés** (i18n/RTL, identités, audit/sécurité, facturation/fiscalité) ; slice repositionné après ses fondations réelles (0→1→2→3→4) ; graphe de dépendances cohérent | roadmap §3, §4, §7 |
| C-09 | Fiscalité exprimée en **cycle de vie d'états** `NotConfigured`/`Configured`/`InvalidOrExpired` ; suppression de « taux 0 » ; émission définitive conditionnée à `Configured` | audit §4.6 ; adr ADR-011 (nouveau) ; roadmap §3/§7 |
| C-10 | Nuance juridique : absences = **lacunes d'accountability/sécurité à fort risque**, **mesures probablement nécessaires**, **à valider juridiquement** ; distinction **mono-organisation / multi-organisation / SaaS** | audit §1, §4.4, §4.7 ; risk R-01, R-08, threat model ; adr ADR-009 |
| C-11 | Table de confrontation enrichie aux **11 colonnes** (statut, preuve, **risque, dette**, BE, MA, **remédiation, prérequis**, priorité, **confiance**) | audit §5 |
| C-12 | Ajout de l'analyse **dépendances vulnérables** (§2.5) et **sécurité/livraison** (packaging, signature, MAJ, secrets, logs, verrouillage connexion, rotation mot de passe initial) | audit §2.5, §4.7 ; risk R-24/R-25/R-26 |
| C-13 | **Verdict scindé** : Phase 2A vs fonctionnalités nationales BE/MA | audit §9 ; présent rapport §10-11 |

---

## 3. Commandes exécutées (journal de preuves reproductible)

Outil : `ripgrep 14.1.1`. Portée : `rg` respecte `.gitignore` (donc `bin/`/`obj/` exclus) et parcourt l'**arbre de travail** (inclut les fichiers non suivis comme `IThemeService.cs`). Scope `src` = code applicatif (`src/`) ; certaines recherches portent sur tout le dépôt. Les faux positifs ont été levés par inspection du contenu (commande de contrôle exécutée).

| Sujet | Commande | Dossiers | Résultats | Limites / faux positifs | Statut final |
|---|---|---|---|---|---|
| Organisation / Store / Tenant | `rg -nP 'OrganizationId\|StoreId\|TenantId\|MagasinId\|OrganisationId' src` | `src/` | **0** | — | `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` |
| Country / Pays | `rg -nP '\bCountryId\b\|\bCountry\b\|\bPays\b' src` | `src/` | **5** | **tous** dans `Address.cs` (VO **inutilisé**) ; aucun sur entité/magasin | Pays absent des entités (VO mort) |
| Audit / journal | `rg -ni 'audit\|journal' src` | `src/` | **6** | **tous faux positifs** (« Journalières »/`JOURNALIERE`) | `ABSENCE_VÉRIFIÉE` (aucun mécanisme d'audit) |
| TVA / taxe | `rg -niP '\bVAT\b\|\bTVA\b\|\bTax(Rate\|Category)?\b\|\bTaxe\b' src` | `src/` | **0** | — | `ABSENCE_VÉRIFIÉE` |
| Facture / invoice | `rg -niP '\bInvoice\b\|\bFacture\b' src` | `src/` | **1** | en-tête de colonne `N° FACTURE` lié à `SaleNumber` ([CustomerInfoView.axaml:148](../../src/MMV.App/Views/Clients/CustomerInfoView.axaml#L148)) ; pas d'entité | `ABSENCE_VÉRIFIÉE` (aucune entité Facture) |
| Ressources de localisation | `rg --files -g '*.resx'` | dépôt | **0** | — | `ABSENCE_VÉRIFIÉE` |
| FlowDirection (RTL) | `rg -n 'FlowDirection' src` | `src/` | **0** | — | `ABSENCE_VÉRIFIÉE` |
| `DateTime.Now` | `rg -n 'DateTime\.Now' src` | `src/` | **23** | — | CONFIRMÉ (usage local) |
| `DateTime.UtcNow` | `rg -n 'DateTime\.UtcNow' src` | `src/` | **59** | — | CONFIRMÉ (mélange Now/UtcNow) |
| Money/Address/Email/PhoneNumber (instanciation) | `rg -nP 'new (Money\|Address\|Email\|PhoneNumber)\(' src` | `src/` | **0** | — | `ABSENCE_VÉRIFIÉE` (value objects morts) |
| `AddInfrastructure` | `rg -n 'AddInfrastructure' --glob '*.cs'` | dépôt | **1** | uniquement la **définition** ([DependencyInjection.cs:17](../../src/MMV.Infrastructure/DependencyInjection.cs#L17)) | CONFIRMÉ jamais appelé |
| Concurrence | `rg -ni 'IsRowVersion\|IsConcurrencyToken\|ConcurrencyToken\|RowVersion\|\bxmin\b' --glob '*.cs'` | dépôt | **0** | — | `ABSENCE_VÉRIFIÉE` (aucun concurrency token) |
| Lazy loading | `rg -n 'UseLazyLoadingProxies\|ILazyLoader\|EntityFrameworkCore\.Proxies'` | dépôt | **0** | — | `ABSENCE_VÉRIFIÉE` (proxies désactivés → pas de N+1 par lazy) |
| Packaging / mises à jour | `rg -niP 'velopack\|squirrel\|clickonce\|\bwix\b\|\bmsix\b\|innosetup\|UpdateManager\|autoupdate\|PublishSingleFile\|RuntimeIdentifier'` | dépôt | **4** | **uniquement en documentation** (`ARCHITECTURE.md`, `SPRINTS.md`, `docs/SAAS_LICENSING.md`) | `ABSENCE_VÉRIFIÉE` (non implémenté dans le build) |
| Limitation des connexions | `rg -niP 'lockout\|throttl\|failedattempt\|tentative' src` | `src/` | **0** | — | `ABSENCE_VÉRIFIÉE` (pas de verrouillage) |
| CI | `test -d .github` | dépôt | **ABSENT** | — | `ABSENCE_VÉRIFIÉE` |
| Nombre de `DbSet` | `rg -n 'public DbSet<' src/MMV.Infrastructure/Data/OpticDbContext.cs` | fichier | **18** | — | CORRIGÉ (était indiqué 17) |
| Build | `dotnet build MMV.sln --no-restore -c Debug` | solution | **0 warning / 0 erreur** | — | CONFIRMÉ |
| Tests | `dotnet test MMV.sln --no-build -c Debug` | solution | **187 ✅ / 5 ❌** | 5 échecs (seed/FK) déjà documentés | CONFIRMÉ (suite rouge) |

> Note de portée : les recherches `src` ne couvrent pas les Markdown ni `tests/`. Les recherches « dépôt » couvrent l'arbre de travail hors `bin/obj`. Aucune absence n'est affirmée hors de la portée déclarée.

---

## 4. Résultats du contrôle des dépendances

Commande : `dotnet list MMV.sln package --vulnerable --include-transitive` (source : `api.nuget.org` ; restauration à jour).

| Package (transitif) | Version | Gravité | Avis | Projets |
|---|---|---|---|---|
| `Microsoft.Extensions.Caching.Memory` | 8.0.0 | **High** | GHSA-qj66-m88j-hmgj | tous |
| `System.Text.Json` | 8.0.0 | **High** | GHSA-hh2w-p6rv-4g7w ; GHSA-8g4q-xg66-9fp4 | tous |
| `Tmds.DBus.Protocol` | 0.20.0 | **High** | GHSA-xrw6-gwf8-vvr9 | `MMV.App`, `MMV.App.Tests` |
| `System.Net.Http` | 4.3.0 | **High** | GHSA-7jgj-8wvc-jh57 | `MMV.App.Tests` |
| `System.Text.RegularExpressions` | 4.3.0 | **High** | GHSA-cmhx-cq75-c4mj | `MMV.App.Tests` |

- `MMV.Domain` : **aucun** package vulnérable.
- Toutes les vulnérabilités sont **transitives** et de gravité **High** (aucune `Critical`).
- **Aucun package n'est déclaré vulnérable sans ce résultat de commande.**
- Remédiation (hors Phase 1) : mise à jour des versions cadres (EF Core / Avalonia / SDK de test) + scan `--vulnerable` intégré au CI → risque **R-24**, traité en **Étape 0** de la roadmap.

---

## 5. Contradictions de roadmap corrigées

| Contradiction initiale | Correction |
|---|---|
| L'Étape de persistance évoquait des « use cases » alors que la couche Application n'existait pas encore | Les **transactions sont d'abord traitées à la frontière (UoW) en Étape 1** ; la **couche Application (use cases) devient l'Étape 2** ; les use cases transactionnels n'apparaissent qu'**après**. |
| Le vertical slice utilisait Organisation/Magasin, horloge et audit, capacités situées dans des étapes postérieures | Le slice est **explicitement repositionné après ses fondations** : 0 → 1 → 2 → 3 → 4 (avec **primitive d'audit minimale** intégrée à l'Étape 4). |
| « le slice peut démarrer après les étapes 0 à 3 » | Corrigé : il dépend de **0 → 4** (Application **et** monétaire **et** fondations org/magasin) ; la localisation, les identités, l'audit approfondi et la facturation **ne** sont **pas** requis pour le slice. |
| Étapes horizontales trop larges (i18n + identités dans un même lot ; facture + fiscalité + audit dans un autre) | **Lots séparés** : Étape 5 (Localisation/RTL), Étape 6 (Client/Porteur/Payeur & identités), Étape 7 (Audit & sécurité), Étape 8 (Documents & fiscalité). Dépendances distinctes explicitées dans le graphe §4. |
| `Down()` comme rollback de données | Remplacé par une **stratégie à 5 mécanismes** (cf. §6 roadmap et ADR-003). |
| « moteur fiscal à taux 0 » | Remplacé par les **états** `NotConfigured/Configured/InvalidOrExpired` (ADR-011). |

---

## 6. Changements apportés aux ADR

| ADR | Changement | Statut |
|---|---|---|
| ADR-003 | Scindé en **A. source de vérité**, **B. déclencheur de migration (4 options)**, **C. rollback (5 mécanismes)** ; spécificités SQLite desktop ; `Migrate()` non imposé | Décision **différée** |
| ADR-004 | **5 options** de représentation monétaire ; séparation domaine/représentation/provider ; tableau multi-critères ; owned type EF **non** recommandé définitivement | Décision **différée** |
| ADR-007 | Inchangé sur le fond ; complété par le renvoi au cycle de vie fiscal (ADR-011) | Décision **différée** |
| ADR-009 | Ajout d'une **nuance** : lacune de sécurité/accountability, validation juridique requise, pas conclusion de non-conformité | Décision **différée** |
| **ADR-010** (nouveau) | Stratégie de **concurrency token** (Guid / version entière / update conditionnel / isolation / token natif serveur) + gestion `DbUpdateConcurrencyException` | Décision **différée** |
| **ADR-011** (nouveau) | **Cycle de vie de la configuration fiscale** (`NotConfigured/Configured/InvalidOrExpired`) ; émission définitive conditionnée ; aucune valeur de taux codée | Décision **différée** |

**Aucun ADR n'est présenté comme définitivement accepté.** Tous restent « décision différée » avec informations manquantes explicitées.

---

## 7. Éléments toujours non vérifiés (`NON_VÉRIFIÉ_DANS_LE_DÉPÔT`)

1. **Présence de N+1 réels à l'exécution** : le lazy loading est exclu, mais le coût réel des `.Include()` larges et des écrans non audités n'a pas été profilé (analyse dynamique hors périmètre Phase 1).
2. **Couverture de tests chiffrée** : `coverlet.collector` présent mais aucun rapport de couverture généré.
3. **Bases utilisateurs déjà créées par `EnsureCreated`** : leur existence/contenu sur des postes réels est inconnue (impact sur le plan de reprise).
4. **Comportement multi-postes concurrent réel** (contention SQLite) : non testé.
5. **Exactitude des données de santé/financières en `REAL`** : l'ampleur des écarts d'arrondi déjà introduits en base n'a pas été mesurée.
6. **Conformité juridique** (RGPD/APD, loi 09-08/CNDP, fiscalité) : hors compétence de l'audit — requiert experts locaux et reste soumise aux gates.
7. **Traductions NL/AR** du glossaire 0.5D : non certifiées.

---

## 8. Preuve git status avant / après

### 8.1 Avant corrections documentaires (début Phase 1B)

`git status --short` et `git diff --stat -- src tests` : **16 fichiers `src/MMV.App/**` modifiés** (préexistants, refonte thème clair/sombre), **176 insertions / 60 suppressions**, **aucun fichier `tests/`** modifié. Untracked : `docs/architecture/` (5 livrables Phase 1), `docs/domain/`, `docs/*.md`, `docs/*.zip`, `src/MMV.App/Services/IThemeService.cs`, `ThemeService.cs`.

### 8.2 Après corrections documentaires (fin Phase 1B)

`git status --short` et `git diff --stat -- src tests` : **strictement identiques** — toujours les **mêmes 16 fichiers** préexistants, **mêmes 176/60**, **aucune** nouvelle modification de `src/` ou `tests/`. Seul ajout : `docs/architecture/phase-1b-validation-report.md` (présent rapport) dans le dossier untracked `docs/architecture/`.

### 8.3 Distinction des origines

| Catégorie | Fichiers | Origine |
|---|---|---|
| Modifications applicatives **préexistantes** | 16 fichiers `src/MMV.App/**` (`App.axaml(.cs)`, `Controls/*`, `Styles/AppStyles.axaml`, `ViewModels/PageViewModels.cs`, `ViewModels/UserProfileViewModel.cs`, plusieurs `Views/**`) + untracked `IThemeService.cs`/`ThemeService.cs` | Branche `refactor/redesignLightAndDarkMode` (avant l'audit) — **non touchés** par les Phases 1 et 1B |
| Markdown **créés par l'audit (Phase 1)** | `docs/architecture/{phase-1-technical-audit, dependency-map, risk-register, adr-candidates, migration-roadmap}.md` | Phase 1 |
| Markdown **créés/modifiés par cette phase (Phase 1B)** | Modifs des 5 fichiers ci-dessus + **création** de `phase-1b-validation-report.md` | Phase 1B |
| Artefacts **externes non créés par l'audit** | `docs/architecture.zip`, `docs/domain.zip`, `docs/domain/`, `docs/PHASE0*.md` | Hors audit — **non touchés** |

> **Preuve** : `git diff -- src tests` ne contient **que** les modifications préexistantes de la refonte thème (identiques avant/après). Aucune ligne de `src/` ou `tests/` n'a été modifiée par les Phases 1 ou 1B. Le build (`bin/`/`obj/`) n'apparaît pas (ignoré par git).

---

## 9. Liste exacte des documents modifiés par la Phase 1B

Tous sous `docs/architecture/` (et **eux seuls**) :

1. `phase-1-technical-audit.md` — *modifié* (DbSet 18 ; chemins DB ; N+1 ; §2.5 vulnérabilités ; §4.4/§4.6/§4.7 nuances + livraison ; §5 table 11 colonnes ; §9 verdict scindé).
2. `dependency-map.md` — *modifié* (anomalie V4 : 2 chemins physiques / 4 sites).
3. `risk-register.md` — *modifié* (R-01/R-02/R-04/R-08/R-09 reformulés ; ajout R-24/R-25/R-26 ; threat model nuancé).
4. `adr-candidates.md` — *modifié* (ADR-003 et ADR-004 réécrits ; ADR-009 nuancé ; **ajout ADR-010 et ADR-011** ; index mis à jour).
5. `migration-roadmap.md` — *réécrit* (réordonnancement, lots séparés, graphe, fiscalité en états, rollback à 5 mécanismes).
6. `phase-1b-validation-report.md` — *créé* (présent rapport).

Aucun autre fichier du dépôt n'a été modifié.

---

## 10. Verdict — Phase 2A (stabilisation & fondations techniques)

**`GO` sous conditions.**

Motivation par les preuves : domaine découplé réutilisable (0 dépendance EF/Avalonia/Infra), suite de tests et outillage présents, périmètre des manques cartographié, aucune règle réglementaire codée. Rien n'empêche un travail de fondation **non national**.

Conditions de levée, dans l'ordre (cf. roadmap) :
1. Étape 0 — tests verts + CI + **traitement des dépendances vulnérables (R-24)**.
2. Étape 1 — persistance fiable (ADR-003 : source de vérité, déclencheur choisi, rollback ; concurrency token ADR-010 ; transactions).
3. Étape 2 — couche Application (use cases, DI unifiée, `ExecuteSave` migré).
4. Étape 3 — modèle monétaire fiable (ADR-004 : représentation adaptée au provider, **pas forcément `decimal`-columns**).
5. Étape 4 — fondations Organisation/Magasin/Pays + numérotation + horloge + stock/magasin + audit minimal.

Premier livrable de valeur : le **vertical slice** « Vente B2C multi-devise, numérotée, par magasin, configuration fiscale `NotConfigured` » (roadmap §7).

---

## 11. Verdict — Développement des fonctionnalités nationales Belgique/Maroc

**`NO-GO` à ce stade.**

Motivation par les preuves : aucune des fondations requises (devise fiable, isolation tenant pour SaaS, i18n/RTL, identités client/porteur/payeur, modèle Facture, configuration fiscale, audit/traçabilité, politiques datées) n'existe ; et **les gates métier Phase 0.5D ne sont pas levées**. Démarrer un vertical pays maintenant coderait des hypothèses non confirmées ou bâtirait sur des fondations absentes.

**Conditions de bascule vers `GO` national :**
- livraison **vérifiée** des Étapes 2A (fondations) **et** des lots Localisation (5), Identités (6), Audit/sécurité (7), Documents/fiscalité (8) selon le marché visé ;
- **levée des gates concernées** par des experts locaux (fiscaliste, juriste, autorités) — `G-TVA-*`, `G-PRESC-*`, `G-R-MA`, `G-CNDP`, `G-FACT-MA`, `G-EINV-*`, `L-1`.

Tant que ces conditions ne sont pas réunies, seul le **cœur commun non national** doit être développé.

---

> **Fin de la Phase 1B.** Aucune implémentation n'a été engagée. Les seules écritures de cette phase portent sur les documents `docs/architecture/`.
