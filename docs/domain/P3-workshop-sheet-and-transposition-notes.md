# P3 — Notes domaine : fiche atelier de montage & transposition optique

> Notes de **cadrage domaine** produites en **P3-0** (audit). **Aucune implémentation ici.** Ce document
> alimente les étapes futures **P3-6B** (fiche atelier) et **P3-3** (cohérence ordonnance) ainsi que la règle
> optique transverse. **Le dépôt réel prime toujours sur ce document.**
>
> Références : [feuille de route P3](../architecture/P3-business-rules-roadmap.md),
> [rapport d'audit P3-0](../implementation/P3-0-business-audit-report.md).

---

## 1. État actuel dans le code (vérifié 2026-07-07)

- Une **fiche de fabrication existe déjà**, mais uniquement comme **vue UI éphémère** :
  [`FabricationSheetViewModel`](../../src/MMV.App/ViewModels/FabricationSheetViewModel.cs) +
  [`FabricationSheetView.axaml`](../../src/MMV.App/Views/Orders/FabricationSheetView.axaml). Elle se construit
  **à la volée** depuis un `OrderDetailsDto` (query `GetOrderDetails`) et n'est **pas persistée**.
- Ce qu'elle **fait** aujourd'hui : afficher/imprimer, par commande, la monture, les verres OD/OG, l'accessoire,
  quelques infos client minimales, le statut de commande, les notes, le montant total.
- Ce qu'elle **ne fait pas** : versionnement, contrôle qualité, transposition, mesures porteur, cotes de
  montage, calculs de décentrement, obsolescence si les données techniques changent, minimisation stricte des
  données client.
- Les paramètres optiques (`Sphere`, `Cylinder`, `Axis`, `Addition`, `PrismValue`, `PrismBase`, `VisualAcuity`)
  existent sur `Prescription`, `SaleItem` et `OrderItem`. **Aucun** service de transposition n'existe
  (`rg Transposition` = 0 résultat métier).

**Conclusion** : P3-6B **formalise et fiabilise** un concept déjà amorcé ; il ne part pas d'une page blanche.

---

## 2. Fiche atelier de montage / technicien (cible P3-6B)

### 2.1 Nature

Une **fiche de travaux destinée au technicien monteur / au labo**, générée **depuis une commande**.

- **Ce qu'elle est** : un bon d'atelier imprimable/exportable, **versionné**, avec **contrôle qualité**.
- **Ce qu'elle n'est pas** : ni une **facture**, ni un **devis**, ni un document comptable/fiscal.

### 2.2 Blocs de données à prévoir

| Bloc | Contenu | Note |
|---|---|---|
| Identification commande | numéro, date, échéance, statut | dérivé de `Order` |
| Client minimal | nom, contact strictement nécessaire | **minimisation** des données |
| Monture | référence, désignation, dimensions utiles | `Product` / `AccessoryDetail` |
| Verres OD / OG | type, matériau, usage, correction | `OrderItem`/`SaleItem` (LensOd/LensOg) |
| Prescription originale | valeurs d'examen **intangibles** | copie de lecture, jamais modifiée |
| Notation atelier | correction **transposée** si appliquée | affichage seulement (cf. §3) |
| Mesures porteur | écarts pupillaires, hauteurs… | **distinctes** des valeurs d'examen |
| Cotes de montage | cotes/positionnement dans la monture | **distinctes** des mesures porteur |
| Calculs de décentrement | décentrement calculé | dérivé (monture + porteur) |
| Instructions atelier | consignes de montage | libre |
| Alertes | incohérences, valeurs limites | dérivé des règles |
| Contrôle qualité | résultat QC atelier | **requis** avant Prête/Livrée (à terme) |
| Version / historique | n° de version, horodatage, obsolescence | traçabilité |

### 2.3 Règles métier futures (minimum)

1. Une fiche atelier ne **modifie jamais** l'ordonnance source.
2. Elle peut **afficher** une correction **transposée** pour le technicien/labo (vue, pas mutation).
3. Elle **distingue** trois natures de mesures : **mesures d'examen** (ordonnance), **mesures porteur**,
   **cotes de montage**. Ne pas les confondre ni les fusionner.
4. Une fiche **validée/imprimée ne se modifie pas** directement : toute évolution crée une **nouvelle version**.
5. Si commande / prescription / verres / monture / mesures changent après génération, la fiche devient
   **obsolète** (signalée, régénérable).
6. **Contrôle qualité requis** avant le passage en statut `Ready`/`Delivered` (à terme, lié à P3-6).
7. **Minimisation** des données client affichées.
8. **Pas** de prix / acompte / reste à payer sur la fiche, sauf besoin explicitement décidé.

### 2.4 Cible technique probable (indicative)

- Entité `WorkshopSheet` (persistée, **versionnée**), liée à `Order`.
- `GenerateWorkshopSheetUseCase` (Application) orchestrant : lecture commande + ordonnance, application
  optionnelle de la transposition (via service Domain §3), calculs de décentrement, snapshot versionné.
- Détection d'obsolescence par comparaison de snapshot (hash/version des données techniques).

---

## 3. Transposition sphère / cylindre / axe

### 3.1 Principe

La **transposition** exprime une même correction cylindrique sous sa forme équivalente (cylindre de signe
opposé, axe tourné de 90°). Utile pour le **labo/technicien** ; **sans effet** sur la vérité de l'ordonnance.

- L'**ordonnance originale est toujours conservée**.
- La transposition est utilisée **uniquement** pour l'**affichage atelier/labo** si nécessaire.
- La transposition **ne remplace jamais** la prescription source.

### 3.2 Formule

```
sphère'   = sphère + cylindre
cylindre' = cylindre × (−1)
axe'      = axe + 90
si axe' > 180  →  axe' = axe' − 180
si axe' = 0    →  normaliser à 180 (convention retenue)
```

### 3.3 Exemple

```
Original  : +2.00 (−1.00) axe 180
Transposé : +1.00 (+1.00) axe 90
```

Vérification : `sphère' = 2.00 + (−1.00) = +1.00` ; `cylindre' = −(−1.00) = +1.00` ;
`axe' = 180 + 90 = 270 → 270 − 180 = 90`. ✔

### 3.4 Points à auditer / vigilance dans le code

- `Sphere` / `Cylinder` / `Axis` sont stockés (nullable) sur `Prescription`, `SaleItem`, `OrderItem`.
- `Axis` est déjà borné **0–180** dans `PrescriptionValidator` (par œil), mais :
  - la cohérence **cylindre nul ⇒ axe absent/non significatif** n'est **pas** validée ;
  - la cohérence **cylindre non nul ⇒ axe requis** n'est **pas** validée (→ règle P3-3).
- L'UI affiche les valeurs brutes ; aucune notation transposée n'existe encore.
- Convention de normalisation `0 ↔ 180` à **figer** avant implémentation (impacte tests et affichage).

### 3.5 Cible technique probable (indicative)

- `OpticalPrescriptionTranspositionService` : service **Domain pur** (sans état, sans dépendance EF), avec
  **tests exhaustifs** (signes, bornes d'axe, normalisation 0/180, valeurs nulles, cylindre nul).
- Consommé par `GenerateWorkshopSheetUseCase` (P3-6B) pour la **notation atelier**.
- **Ne pas** injecter la transposition dans la saisie/écriture d'ordonnance : c'est une projection de lecture.

---

## 4. Rappel de périmètre

Ces notes cadrent un **futur** travail (P3-3 / P3-6B). Elles n'introduisent **aucune** logique applicative,
**aucune** entité, **aucune** migration en P3-0. Toute implémentation suivra le protocole d'étape P3
(une étape = un commit + un rapport + CI verte), avec le dépôt réel comme référence.
