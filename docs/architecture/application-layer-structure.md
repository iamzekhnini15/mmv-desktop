# Structure de la couche Application (`MMV.Application`)

> **Créée en P2B-2B** (création contrôlée, **sans logique métier**). Ce document décrit la structure
> minimale posée et les invariants à respecter. Références :
> [ADR frontières](adr-application-boundaries.md), [plan de migration](application-layer-migration-plan.md).
> Date : 13 juin 2026. Branche : `p2b-architecture`.

---

## 1. Projet

`src/MMV.Application/MMV.Application.csproj` — `net8.0`, `Nullable=enable`, `ImplicitUsings=enable`,
`LangVersion=12.0` (cohérent avec les autres projets).

| Aspect | Valeur |
|---|---|
| Référence projet | **`..\MMV.Domain\MMV.Domain.csproj` uniquement** |
| Packages | `Microsoft.Extensions.DependencyInjection.Abstractions` **8.0.1** (neutre : ni EF, ni Avalonia, ni MVVM) — requis pour exposer `AddApplication(this IServiceCollection)` |
| Interdits | `MMV.Infrastructure`, `MMV.App`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Data.Sqlite`, `Avalonia*`, `CommunityToolkit.Mvvm` |

## 2. Arborescence posée

```
src/MMV.Application/
  MMV.Application.csproj
  DependencyInjection.cs          # AddApplication(this IServiceCollection) — squelette neutre (P2B-2B)
  Abstractions/
    IApplicationMarker.cs         # marqueur d'assembly neutre (ancre de type ; aucune sémantique métier)
  UseCases/
    README.md                     # vide en P2B-2B ; 1er use case EnregistrerVente en P2B-2C
  Common/
    README.md                     # types transverses (Result, gardes) ultérieurs ; vide en P2B-2B
```

> Convention cible (P2B-2C+) : **un dossier par use case** (`UseCases/Sales/RegisterSale/` :
> `RegisterSaleCommand` / `RegisterSaleResult` / `RegisterSaleUseCase` / `RegisterSaleValidator`),
> compatible CQRS léger ultérieur.

## 3. Invariants de dépendances (ADR §4.2)

1. `MMV.Domain` : 0 référence projet, 0 package EF/Avalonia (préservé).
2. `MMV.Application` → `MMV.Domain` **seulement** (+ abstractions DI neutres). **Aucun** EF/SQLite/Avalonia/MVVM.
3. `MMV.Infrastructure` → `MMV.Domain` (ne référence pas `MMV.Application`).
4. `MMV.App` → `MMV.Domain`, `MMV.Application`, `MMV.Infrastructure` (composition root unique).
5. Sens : **UI → Application → Domain ← Infrastructure**. Aucune flèche ne remonte.

**Garantie structurelle actuelle** : les invariants 1–4 sont assurés par les `.csproj` (références projet
explicites + absence des packages interdits) et vérifiés par `dotnet build` + `dotnet list package`. Un test
d'architecture par réflexion est un **contrôle différé** (P2B-2C, avec le projet de tests Application).

## 4. DI

`AddApplication(this IServiceCollection)` est appelée par le composition root unique
([`App.ConfigureServices`](../../src/MMV.App/App.axaml.cs)). En P2B-2B elle **n'enregistre aucun use case
métier réel** (squelette). Les use cases seront enregistrés ici en P2B-2C, en portée `Scoped` (même portée
que `OpticDbContext` / repos / `ITransactionRunner`) pour partager la transaction.

## 5. Note de nommage (collision `Application`)

Le namespace `MMV.Application` (membre du parent `MMV`) **masque**, par résolution de nom simple, le type
`Avalonia.Application` dans tout l'assembly `MMV.App`. Conséquence neutre, corrigée en qualifiant
explicitement `Avalonia.Application` aux 2 seuls points concernés
([`App.axaml.cs`](../../src/MMV.App/App.axaml.cs) base de classe ; [`ThemeService.cs`](../../src/MMV.App/Services/ThemeService.cs) `Avalonia.Application.Current`).
Aucun comportement modifié : `Application` y désignait déjà `Avalonia.Application`.
