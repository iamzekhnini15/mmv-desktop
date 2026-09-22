# P4-RECON-B-light — Alignement de la roadmap avant P4-6

> **Note d'enregistrement (22 septembre 2026).** Ce rapport est **enregistré au dépôt** par le commit documentaire
> de la [revue d'acceptation P4-6A](P4-6A-adr-acceptance-report.md). Les mentions « non commité », « aucun commit »
> ou « non suivi » ci-dessous décrivent l'état **au moment de sa rédaction** : elles restent exactes pour le lot
> qu'elles décrivent, qui n'a produit **aucun commit de code**.

> **Statut : APPLIQUÉ LOCALEMENT — NON COMMITÉ.** Seule la documentation est modifiée. Aucun code, aucun
> test, aucune migration, aucune ADR, aucune CI. Aucun commit, aucun push.
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> HEAD de référence : **`ccbeb3259444d26264e20323058764dd59318e1c`** (= `origin/p4-multi-poste`).
> **Le dépôt réel prime sur ce document.**

**Mandat.** Corriger **uniquement** les statuts bloquants avant l'ouverture de P4-6 : P4-5C, P4-5D, P4-5D-R et
P4-5E `COMPLETED`, P4-5F `NEXT`, P4-5G intégré à P4-6C. Ajouter une note courte. **Ne corriger aucune autre
incohérence documentaire.** C'est une version réduite du lot RECON-B (réconciliation P4-5, C-01 … C-22).

---

## 1. Fichiers modifiés

| Fichier | Opération | Lignes |
|---|---|---|
| [P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) | **modifié** | +17 / −8 (7 hunks) |
| `docs/implementation/P4-RECON-B-light-report.md` (ce rapport) | **créé** | — |

Aucun autre fichier n'est touché. Les fichiers déjà non suivis avant ce lot (roadmap P5, rapports P4-5 et
P4-6) sont **inchangés**.

---

## 2. Justification

| Demande | Traitement | Pourquoi |
|---|---|---|
| P4-5C = COMPLETED | statut + commit `b5c2073` + lien vers le rapport P4-5C (suivi) | commit présent sur `origin/p4-multi-poste` (`git log`) |
| P4-5D = COMPLETED | statut + commit `023048f`, **sans lien** | le rapport final P4-5D n'est pas suivi : un lien serait cassé une fois commité |
| P4-5D-R = COMPLETED | **aucune modification** | déjà `COMPLETED — 45f67a2` dans la table et dans le bloc d'état |
| P4-5E = COMPLETED | statut + commits `a06c19f` (implémentation) et `ccbeb32` (documentation C9) | `ccbeb32` est le SHA validé par l'architecte |
| P4-5F = NEXT | `NEXT — non commencé` | aucun commit P4-5F (`git log`) |
| P4-5G → P4-6C | statut `ABSORBÉ PAR P4-6C`. Contenu de la ligne et condition « uniquement après P4-5F vert » **conservés** | la condition reste valable dans P4-6C |
| Note courte | texte demandé, **mot pour mot**, sous la table des sous-lots P4-5 | c'est là que figure la ligne P4-5G |

**Pourquoi quatre endroits, et pas seulement la table.** La roadmap écrit le statut de ces sous-lots à quatre
endroits : la table des sous-lots, le titre de P4-5, la ligne P4-5 du tableau §6 et le bloc d'état en vigueur.
Corriger la table seule laissait la roadmap se contredire **sur ces mêmes statuts**. Ailleurs, rien n'a été
modifié.

**Pourquoi une puce dans P4-6.** Sans elle, la ligne P4-5G renvoie à un sous-lot P4-6C dont la fiche P4-6 ne
parle pas. La puce enregistre le transfert de périmètre. Elle **ne définit pas** le découpage de P4-6 (voir
§5, point R-1).

**Correspondance avec la réconciliation P4-5** : ce lot couvre C-03 et, **en partie**, C-04, C-07, C-08 et
C-09. Il rend C-06 caduque (P4-5E n'est plus `NEXT` mais `COMPLETED`). Les autres items restent ouverts (§5).

---

## 3. Diff résumé

Numéros de ligne **après** modification.

| # | Emplacement | Avant | Après |
|---|---|---|---|
| 1 | l. 573 — titre de P4-5 | `IN PROGRESS` (P4-5A et P4-5B livrés) | `IN PROGRESS` (P4-5A … P4-5E livrés) |
| 2 | l. 600 — ligne P4-5C | `NOT STARTED` | `COMPLETED` — `b5c2073` ([rapport](P4-5C-ef-model-portability-report.md)) |
| 3 | l. 601 — ligne P4-5D | `NOT STARTED` | `COMPLETED` — `023048f` |
| 4 | l. 603 — ligne P4-5E | `NOT STARTED` | `COMPLETED` — `a06c19f` · `ccbeb32` (documentation C9) |
| 5 | l. 604 — ligne P4-5F | `NOT STARTED` | `NEXT` — non commencé |
| 6 | l. 605 — ligne P4-5G | `NOT STARTED` | `ABSORBÉ PAR P4-6C` — voir note ci-dessous |
| 7 | l. 607–608 — **ajout** | — | note RECON-B-light (texte ci-dessous) |
| 8 | l. 626–627 — fiche P4-6, **ajout** | — | puce « **Absorbe P4-5G** … dans le sous-lot **P4-6C**, toujours **après P4-5F vert** » |
| 9 | l. 775 — tableau §6, ligne P4-5 | « **Restant** : P4-5C … P4-5G, **aucune ligne implémentée** » | « **Livrés depuis** : P4-5C (`b5c2073`), P4-5D (`023048f`), P4-5D-R (`45f67a2`), P4-5E (`a06c19f`, `ccbeb32`). **Restant** : P4-5F (**NEXT**) ; P4-5G **absorbé par P4-6C** » |
| 10 | l. 822–827 — bloc d'état en vigueur | `P4-5C … P4-5G = NOT STARTED` (1 ligne) | 5 lignes (voir ci-dessous), autour de la ligne P4-5D-R existante, inchangée |

**Note ajoutée (l. 607–608)**

```
> **Note RECON-B-light (2026-09-22, HEAD `ccbeb32`).** P4-5E completed. P4-5F remains for PostgreSQL
> integration validation. P4-5G absorbed by P4-6C.
```

**Bloc d'état en vigueur (l. 822–827)**

```
P4-5C MODEL PORTABILITY     = COMPLETED — b5c2073
P4-5D TEMPORAL STRATEGY     = COMPLETED — 023048f
P4-5D-R CIVIL DATE REPRISE  = COMPLETED — 45f67a2 — RR4 DRYRUN ON PROD COPY REQUIRED BEFORE DEPLOYMENT   (inchangée)
P4-5E POSTGRESQL MIGRATIONS = COMPLETED — a06c19f + ccbeb32 — PUSHED
P4-5F INTEGRATION TESTS     = NEXT — NOT STARTED
P4-5G STARTUP GUARD LIFT    = ABSORBED BY P4-6C
```

**Inchangés volontairement** : `P4-5 = IN PROGRESS — NOT CLOSE`, `POSTGRESQL CLEAN START = BLOCKED`,
`P4-6 … P4-12 = NOT STARTED`, `V1 MULTI-POSTE = NOT GO`. Ces quatre statuts restent exacts.

---

## 4. Aucun impact code

| Contrôle | Résultat |
|---|---|
| Fichiers modifiés hors `docs/` | **aucun** (`git status`) |
| `src/`, `tests/`, migrations, snapshots EF | **non touchés** |
| `.github/workflows/ci.yml` | **non touché** |
| ADR (`adr-prod-db-*.md`) | **non touchées** |
| Build / tests | **non relancés** : aucun fichier compilé n'a changé. La mesure de référence reste 1953 tests à `ccbeb32` (vérification finale P4-5) |
| Commit / push | **aucun** |

---

## 5. Points restants — hors périmètre, non corrigés

### 5.1 Résidus créés ou révélés par ce lot

| # | Point | Action suggérée |
|---|---|---|
| R-1 | **P4-6C est nommé, pas défini.** La roadmap ne contient aucun découpage de P4-6. Le découpage A / B / C n'existe que dans le rapport de transition P4-6, **non suivi** | enregistrer le découpage de P4-6 dans P4-6A |
| R-2 | **Critère de clôture de P4-5.** Maintenant que P4-5G est absorbé, P4-5 ne dépend plus que de P4-5F. La roadmap ne l'écrit pas | confirmation de l'architecte, puis une ligne dans la fiche P4-5 |
| R-3 | **CI non enregistrées.** Les numéros de run de `b5c2073`, `023048f`, `a06c19f` et `ccbeb32` ne figurent pas dans le dépôt. La roadmap fixe la règle « commit + CI verte sur le SHA exact ». Ce lot écrit donc `COMPLETED`, **pas `CLOSE`** | enregistrer le run de `ccbeb32` (réconciliation C-18, D-1) |

### 5.2 Incohérences connues, laissées en l'état sur instruction

Numéros de ligne après modification. Références : [réconciliation P4-5](P4-5-reconciliation-audit-report.md) (non suivie).

| Item | Ligne(s) | Passage périmé |
|---|---|---|
| C-01, C-02 | 598–599 | P4-5A `COMPLETE — DECISIONS REQUIRED`, P4-5B `COMPLETE — ADRs ACCEPTED`, sans commit |
| C-04 (reste) | 601 | P4-5D décrit encore une « migration de données écrite à la main » (la reprise est P4-5D-R) |
| C-05 | 602 | commit de clôture `440ddcb` absent de la ligne P4-5D-R |
| C-10 | 16–21 | bandeau : « aucune ligne de code n'a encore été écrite pour le schéma serveur » |
| C-11 | 859–860 | « P4-5 n'est pas commencée » non marqué historique |
| C-12, C-13 | 524–532, 774, 814, 820 | O2, O3 et O4 `OPEN` sous P4-4 et `DECIDED — NOT IMPLEMENTED` sous P4-5 |
| C-14 | 610–615 | note (1) : le passage à `DateOnly` n'a produit aucune migration EF |
| C-15 | 834–841 | baseline 1569 ; trois causes de `CLEAN START = BLOCKED`, dont une levée au niveau du modèle |
| C-16 | 889, 902–903 | « trois chantiers restent ouverts » |
| C-17 | 751, 759 | points durs 1 et 4 sans annotation P4-5C |
| C-19 | 605 | ancre `App.axaml.cs:198`, au lieu de 205 à HEAD |
| C-20 | 910–962 | section anglaise P4-5D-R. La l. 951 dit encore « `45f67a2` is not pushed yet » |
| C-21, C-22 | — | rapport final P4-5D non suivi ; rapport de clôture P4-5D-R sans note post-commit |

---

## 6. Conclusion

```
RECON-B-LIGHT              = APPLIED LOCALLY — NOT COMMITTED
HEAD                       = ccbeb3259444d26264e20323058764dd59318e1c (= origin/p4-multi-poste)
FILES                      = 1 MODIFIED (roadmap P4, +17 / −8) · 1 CREATED (this report)
P4-5C · P4-5D · P4-5D-R    = COMPLETED
P4-5E                      = COMPLETED — a06c19f + ccbeb32
P4-5F                      = NEXT
P4-5G                      = ABSORBED BY P4-6C
P4-5                       = IN PROGRESS — NOT CLOSE (unchanged)
OTHER INCONSISTENCIES      = NOT CORRECTED — LISTED IN §5
CODE / TESTS / CI / ADR    = UNCHANGED
COMMIT / PUSH              = NONE — STOPPED BEFORE COMMIT
```
