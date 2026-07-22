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

namespace MMV.Application.UseCases.Users.CreateUser;

/// <summary>
/// Implémentation du use case « Créer un utilisateur ». En P3-10, les règles jusque-là portées par la seule UI
/// sont réellement appliquées <b>avant</b> toute écriture, et l'unicité du login porte sur sa forme normalisée.
/// </summary>
/// <remarks>
/// <para>
/// <b>Politique de mot de passe (R1/R7).</b> <see cref="UserValidator.ValidatePasswordPolicy"/> est exécutée
/// <b>avant tout hachage</b>. Jusqu'ici, seuls <c>UserFormViewModel</c> et <c>UserProfileViewModel</c>
/// l'appliquaient : tout appelant entrant par la couche Application persistait un mot de passe faible
/// (<c>Password = "a"</c> était haché et stocké). La politique reste <b>propriétaire du Domain</b> — aucune
/// seconde règle n'est écrite ici, seule son application change de couche.
/// </para>
/// <para>
/// <b>Rôle explicite (R3).</b> <c>CreateUserCommand.Role</c> est nullable : une omission est refusée au lieu de
/// valoir <c>Admin</c> (défaut CLR de l'enum). La <b>valeur</b> du rôle est ensuite arbitrée par
/// <see cref="UserValidator"/> (<c>IsInEnum</c>), jusque-là jamais invoqué par ce use case.
/// </para>
/// <para>
/// <b>Unicité normalisée (R2/R6).</b> La garde applicative interroge <c>NormalizedUsername</c> : une variante de
/// casse est un doublon. Le filet réel reste l'index unique en base — si deux postes créent le même login
/// quasi simultanément, l'écriture perdante est rejetée par la base, traduite par <c>PersistenceErrorMapper</c>
/// (via <see cref="ITransactionRunner"/>) en <c>PersistenceException</c> neutre, puis convertie ici en la
/// <b>même</b> erreur métier stable que la garde pré-écriture. Aucun message SQLite/EF brut n'échappe, et seule
/// la catégorie <c>UniqueConstraint</c> est traitée comme doublon : toute autre panne reste propagée.
/// </para>
/// <para>
/// <b>Ordre des étapes.</b> Le hachage n'a lieu qu'après le refus éventuel du mot de passe et du rôle, et le
/// candidat est <b>détaché</b> : une commande invalide ne produit ni hash, ni entité suivie, ni écriture.
/// </para>
/// </remarks>
public sealed class CreateUserUseCase : ICreateUserUseCase
{
    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication). Stateless, partagé.
    private static readonly UserValidator UserValidator = new();

    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthenticationService _authenticationService;
    private readonly ITransactionRunner _transactionRunner;

    public CreateUserUseCase(
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
    public async Task<CreateUserResult> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // --- Refus AVANT tout hachage : ni BCrypt, ni entité, ni écriture pour une commande invalide. ---
        var preErrors = new List<ValidationError>();

        var passwordError = UserCommandGuards.ValidatePassword(command.Password);
        if (passwordError is not null) preErrors.Add(passwordError);

        var roleError = UserCommandGuards.ValidateRolePresent(command.Role);
        if (roleError is not null) preErrors.Add(roleError);

        if (preErrors.Count > 0)
            return new CreateUserResult { ValidationErrors = preErrors };

        var username = command.Username.Trim();

        // Le hachage n'intervient qu'ici : le mot de passe clair a déjà passé la politique, et ne sera jamais
        // porté par l'entité (seul PasswordHash l'est).
        var user = new User
        {
            Username = username,
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername(command.Username),
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Role = command.Role!.Value,
            IsActive = command.IsActive,
            PasswordHash = _authenticationService.HashPassword(command.Password),
            CreatedAt = DateTime.UtcNow,
        };

        // P3-1 : validation du candidat DÉTACHÉ (login, longueurs, jeu de caractères, profil, rôle dans l'enum).
        var validationErrors = CommandValidation.Validate(UserValidator, user);
        if (validationErrors.Count > 0)
            return new CreateUserResult { ValidationErrors = validationErrors };

        // P3-10 : unicité normalisée — message métier clair (le filet DB reste l'index unique).
        var duplicate = await _userRepository.ExistsByNormalizedUsernameAsync(
            user.NormalizedUsername, excludingUserId: null, cancellationToken);
        if (duplicate)
            return new CreateUserResult
            {
                UsernameTaken = true,
                ValidationErrors = UserCommandGuards.UsernameTakenErrors(),
            };

        try
        {
            await _transactionRunner.RunAsync(async ct =>
            {
                await _userRepository.CreateAsync(user, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.UniqueConstraint)
        {
            // Course concurrente : entre la garde applicative et l'écriture, un autre poste a persisté un login
            // équivalent. L'index unique a rejeté l'écriture perdante ; on renvoie EXACTEMENT le même résultat
            // métier stable que la garde pré-écriture. Users ne porte qu'un seul index unique
            // (NormalizedUsername) : aucune autre contrainte d'unicité ne peut être confondue avec ce doublon.
            //
            // Revue ciblée avant commit : le rollback SQL de ITransactionRunner ne détache pas l'entité candidate
            // du ChangeTracker. Sans ce détachement ciblé, un SaveChangesAsync ultérieur et SANS RAPPORT, dans la
            // même portée de contexte, retenterait l'insertion rejetée et échouerait à son tour.
            _userRepository.DetachIfTracked(user);
            return new CreateUserResult
            {
                UsernameTaken = true,
                ValidationErrors = UserCommandGuards.UsernameTakenErrors(),
            };
        }

        return new CreateUserResult { UserId = user.UserId };
    }
}
