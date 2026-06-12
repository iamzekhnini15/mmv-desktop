# UseCases

Use cases applicatifs (un dossier par use case : *verbe + agrégat*, ex. `Sales/RegisterSale/`).
Chaque use case prend une `Command`/`Query` en entrée et renvoie un `Result` en sortie
(compatibilité CQRS léger ultérieure — cf. [ADR frontières](../../../docs/architecture/adr-application-boundaries.md)).

**Vide en P2B-2B** (création contrôlée de la couche Application, sans logique métier). Le premier
use case `EnregistrerVente` (`RegisterSaleCommand` / `RegisterSaleResult` / `RegisterSaleUseCase`)
sera ajouté en **P2B-2C** (premier vertical slice), conformément au
[plan de migration](../../../docs/architecture/application-layer-migration-plan.md).
