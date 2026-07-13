using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Customers.SetCustomerArchived;

/// <summary>
/// Cas d'utilisation « Archiver / réactiver un client » (P3-2B). Alternative <b>non destructive</b> à la
/// suppression : un client porteur d'historique (ordonnances ou ventes) ne peut pas être supprimé, mais il peut
/// être archivé — il disparaît alors des listes et des sélecteurs sans qu'aucune donnée ne soit perdue.
/// </summary>
public interface ISetCustomerArchivedUseCase
{
    /// <summary>
    /// Applique l'état d'archivage décrit par <paramref name="command"/>. Si le client est introuvable, aucune
    /// écriture n'est effectuée (<see cref="SetCustomerArchivedResult.CustomerFound"/> = <c>false</c>).
    /// <b>Idempotent</b> : si le client est déjà dans l'état demandé, le résultat est un succès et <b>aucune
    /// écriture n'a lieu</b> (pas d'<c>UpdateAsync</c>, pas de <c>SaveChangesAsync</c>, <c>UpdatedAt</c>
    /// inchangé). Aucune suppression.
    /// </summary>
    Task<SetCustomerArchivedResult> ExecuteAsync(SetCustomerArchivedCommand command, CancellationToken cancellationToken = default);
}
