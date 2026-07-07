namespace MMV.Application.UseCases.Users.ListUsers;

/// <summary>
/// Entrée (query) du use case <see cref="ListUsersUseCase"/> — « Lister les utilisateurs » (P2D-1).
/// <para>
/// La recherche, le filtrage (rôle, actifs), le tri et la pagination restent <b>en présentation</b>
/// (<c>UsersListViewModel</c>, comportement iso-fonctionnel P2C) : la query ne porte donc aucun critère et
/// renvoie la liste complète. L'objet existe pour la cohérence de convention et la garde « query nulle ».
/// </para>
/// </summary>
public sealed class ListUsersQuery
{
}
