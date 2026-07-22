using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Application.UseCases.Users.Common;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Policies;
using MMV.Domain.Services;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Users.UpdateUser;

/// <summary>
/// Implémentation du use case « Modifier un utilisateur ». En P3-10, la modification applique les mêmes règles
/// que la création : politique de mot de passe avant hachage, rôle explicite et valide, unicité du login sur sa
/// forme normalisée.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune mutation avant validation complète.</b> <c>GetByIdAsync</c> renvoie une entité <b>suivie</b> par le
/// contexte : la muter puis découvrir l'invalidité laisserait des modifications en attente qu'un
/// <c>SaveChanges</c> ultérieur — même déclenché par un tout autre use case partageant l'unité de travail —
/// persisterait. Les valeurs cibles sont donc portées par un candidat <b>détaché</b>, validé intégralement ;
/// l'entité suivie n'est touchée qu'une fois toutes les gardes franchies. C'est ce qui rend vraie l'affirmation
/// « un refus ne modifie aucune propriété », y compris le hash.
/// </para>
/// <para>
/// <b>Mot de passe (R1/R7).</b> Mot de passe absent ou blanc = « conserver » (contrat d'origine, inchangé).
/// Mot de passe fourni ⇒ la politique s'applique <b>avant</b> le hachage : un mot de passe faible ne remplace
/// jamais le hash existant. Un mot de passe valide produit un nouveau hash, donc un nouveau sel.
/// </para>
/// <para>
/// <b>Unicité normalisée (R2/R6).</b> La collision est cherchée hors utilisateur courant : renommer un compte en
/// ne changeant que la casse de son propre login reste accepté (il n'est pas son propre doublon), tandis que
/// prendre la variante de casse du login d'un <b>autre</b> compte est refusé. La course entre postes est arbitrée
/// par l'index unique et traduite en la même erreur métier stable.
/// </para>
/// <para>
/// <b>Hors périmètre P3-10</b> (reports explicites) : ni token de concurrence (le <i>lost update</i> reste
/// possible, R5), ni protection du dernier administrateur (R9), ni autorisation par acteur — Application ne
/// possède aucun <c>ICurrentUser</c>.
/// </para>
/// </remarks>
public sealed class UpdateUserUseCase : IUpdateUserUseCase
{
    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication). Stateless, partagé.
    private static readonly UserValidator UserValidator = new();

    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthenticationService _authenticationService;
    private readonly ITransactionRunner _transactionRunner;

    public UpdateUserUseCase(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IAuthenticationService authenticationService,
        ITransactionRunner transactionRunner)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<UpdateUserResult> ExecuteAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return new UpdateUserResult { UserFound = false, UserId = command.UserId };

        // Mot de passe blanc ou absent = « conserver le mot de passe actuel » (contrat d'origine).
        var passwordProvided = !string.IsNullOrWhiteSpace(command.Password);

        // --- Refus AVANT tout hachage ET avant toute mutation de l'entité suivie. ---
        var preErrors = new List<ValidationError>();

        if (passwordProvided)
        {
            var passwordError = UserCommandGuards.ValidatePassword(command.Password);
            if (passwordError is not null) preErrors.Add(passwordError);
        }

        var roleError = UserCommandGuards.ValidateRolePresent(command.Role);
        if (roleError is not null) preErrors.Add(roleError);

        if (preErrors.Count > 0)
            return new UpdateUserResult { UserFound = true, UserId = command.UserId, ValidationErrors = preErrors };

        var username = command.Username.Trim();
        var normalizedUsername = UserIdentityPolicy.NormalizeUsername(command.Username);

        // Candidat DÉTACHÉ portant les valeurs cibles : l'entité suivie reste intacte tant que tout n'est pas validé.
        var candidate = new User
        {
            UserId = user.UserId,
            Username = username,
            NormalizedUsername = normalizedUsername,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Role = command.Role!.Value,
            IsActive = command.IsActive,
            PasswordHash = user.PasswordHash,
            LastLogin = user.LastLogin,
            CreatedAt = user.CreatedAt,
        };

        var validationErrors = CommandValidation.Validate(UserValidator, candidate);
        if (validationErrors.Count > 0)
            return new UpdateUserResult { UserFound = true, UserId = command.UserId, ValidationErrors = validationErrors };

        // Collision hors utilisateur courant : un compte n'est jamais son propre doublon (changer la seule casse
        // de son propre login reste donc possible).
        var duplicate = await _userRepository.ExistsByNormalizedUsernameAsync(
            normalizedUsername, excludingUserId: user.UserId, cancellationToken);
        if (duplicate)
            return new UpdateUserResult
            {
                UserFound = true,
                UsernameTaken = true,
                UserId = command.UserId,
                ValidationErrors = UserCommandGuards.UsernameTakenErrors(),
            };

        // --- Toutes les gardes sont franchies : l'entité suivie peut être mutée. ---
        user.Username = candidate.Username;
        user.NormalizedUsername = candidate.NormalizedUsername;
        user.FirstName = candidate.FirstName;
        user.LastName = candidate.LastName;
        user.Role = candidate.Role;
        user.IsActive = candidate.IsActive;

        if (passwordProvided)
            user.PasswordHash = _authenticationService.HashPassword(command.Password!);

        try
        {
            await _transactionRunner.RunAsync(async ct =>
            {
                await _userRepository.UpdateAsync(user, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.UniqueConstraint)
        {
            // Course concurrente sur le login : même erreur métier stable que la garde pré-écriture (cf.
            // CreateUserUseCase). Users ne porte qu'un seul index unique, donc aucune confusion possible.
            //
            // Revue ciblée avant commit : l'entité suivie porte encore les valeurs mutées rejetées après le
            // rollback SQL (EF ne les réinitialise jamais sur échec de SaveChanges). Sans ce détachement ciblé,
            // un FindAsync ultérieur dans la même portée renverrait cette instance rejetée au lieu d'interroger
            // la base, et un SaveChangesAsync sans rapport retenterait de persister la mutation refusée.
            _userRepository.DetachIfTracked(user);
            return new UpdateUserResult
            {
                UserFound = true,
                UsernameTaken = true,
                UserId = command.UserId,
                ValidationErrors = UserCommandGuards.UsernameTakenErrors(),
            };
        }

        return new UpdateUserResult { UserFound = true, UserId = command.UserId };
    }
}
