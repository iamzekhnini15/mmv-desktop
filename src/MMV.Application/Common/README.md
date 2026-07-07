# Common

Types transverses de la couche Application. **Aucune** dépendance EF / Avalonia / MVVM.

## Socle validation / Result (P3-1)

Convention minimale posée en **P3-1** (cf. `docs/implementation/P3-1-validation-result-report.md`) :

- **`ValidationError`** — DTO neutre `(PropertyName, Message)` décrivant une erreur de **validation de
  commande** (précondition d'un use case d'écriture), sans exposer FluentValidation au contrat public.
- **`CommandValidation`** — helper unique qui exécute un **validateur FluentValidation existant du Domain**
  (ex. `CustomerValidator`) et projette son résultat vers `IReadOnlyList<ValidationError>`. La règle métier
  reste **propriétaire du Domain** (aucune duplication, cf. [ADR frontières §9](../../../docs/architecture/adr-application-boundaries.md)).

### Contrat des `*Result` d'écriture

| Cas | Signalisation |
|---|---|
| **Commande invalide** | `*Result.ValidationErrors` renseignée, `IsValid = false`, **aucune écriture** (nouveau P3-1) |
| **Entité introuvable** | drapeau `*Found = false` existant, **aucune écriture** (inchangé) |
| **Règle métier refusée** (invariant dur) | exception typée `DomainException` / `BusinessRuleException` (cf. [ADR frontières §8](../../../docs/architecture/adr-application-boundaries.md)) — appliqué aux phases suivantes |
| **Succès** | `*Result` peuplé, `ValidationErrors` vide, `*Found = true` |

**Pas** de monade `Result<T>` complète, **pas** de framework de validation maison, **aucun** paquet NuGet de
niveau supérieur ajouté à `MMV.Application` : FluentValidation transite **déjà** depuis le Domain.

Domaine pilote appliqué en P3-1 : **Clients** (`CreateCustomerUseCase`, `UpdateCustomerUseCase`).
