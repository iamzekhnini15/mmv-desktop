# ManageMyVision (MMV) - Desktop Application

Application de gestion pour opticiens développée avec .NET 10 et Avalonia UI.

## Stack Technique

- **Framework**: .NET 10.0 LTS
- **Langage**: C# 12
- **UI**: Avalonia UI 11.1+ (FluentAvalonia)
- **Base de données**: SQLite + EF Core 10
- **Architecture**: Clean Architecture + MVVM

## Structure du Projet

```
mmv-desktop/
├── src/
│   ├── MMV.Domain/          # Couche métier (Entities, Interfaces, ValueObjects)
│   ├── MMV.Infrastructure/  # Accès données (EF Core, Repositories)
│   └── MMV.App/             # Présentation (Avalonia UI, ViewModels)
├── tests/
│   └── MMV.Domain.Tests/    # Tests unitaires
└── MMV.sln                  # Solution Visual Studio
```

## Prérequis

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio Code avec extensions :
  - C# Dev Kit
  - Avalonia for VSCode

## Installation

```bash
# Restaurer les packages NuGet
dotnet restore

# Compiler la solution
dotnet build

# Lancer l'application
dotnet run --project src/MMV.App
```

## Conventions de Code

- **Casing**: PascalCase (public), camelCase (paramètres), _camelCase (champs privés)
- **Async**: Suffixe obligatoire `...Async` pour toutes les méthodes asynchrones
- **Documentation**: XML Docs pour tous les membres publics
- **Nullable**: Activé (`enable`) sur tous les projets

## Développement

Voir le fichier [ARCHITECTURE.md](ARCHITECTURE.md) pour plus de détails sur l'architecture.

---

**Version**: 1.0.0-sprint1  
**Date**: Janvier 2026
