using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.UpdateUser;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Users;

/// <summary>
/// P3-10 — Identifiant de connexion non ambigu (normalisation + unicité insensible à la casse, y compris sous
/// concurrence) et validation réelle du rôle. Sur de <b>vraies</b> bases SQLite.
/// </summary>
public sealed class UserIdentityAndRoleTests : IDisposable
{
    private readonly string _workDirectory;

    private sealed class FakeAuth : IAuthenticationService
    {
        public string HashPassword(string password) => "HASHED::" + password;
        public bool ValidatePassword(string password, string hash) => hash == HashPassword(password);
        public Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task UpdateLastLoginAsync(long userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>
    /// Décorateur <b>déterministe</b> reproduisant la perte d'une course d'unicité : la garde applicative ne voit
    /// aucun doublon (comme si, au moment du contrôle, l'autre poste n'avait pas encore validé), alors que la
    /// ligne concurrente est bien présente en base et que l'index unique rejettera l'écriture. Aucun
    /// <c>Thread.Sleep</c>, aucun parallélisme réel : le scénario est reproductible à l'identique. Même patron
    /// que <c>RaceLosingProductRepository</c> (P3-4B).
    /// </summary>
    private sealed class RaceLosingUserRepository : IUserRepository
    {
        private readonly IUserRepository _inner;

        public RaceLosingUserRepository(IUserRepository inner) => _inner = inner;

        public Task<bool> ExistsByNormalizedUsernameAsync(string username, long? excludingUserId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<User> CreateAsync(User entity, CancellationToken ct = default) => _inner.CreateAsync(entity, ct);
        public Task<User> UpdateAsync(User entity, CancellationToken ct = default) => _inner.UpdateAsync(entity, ct);
        public void DetachIfTracked(User user) => _inner.DetachIfTracked(user);
        public Task<User?> GetByNormalizedUsernameAsync(string username, CancellationToken ct = default) => _inner.GetByNormalizedUsernameAsync(username, ct);
        public Task<User?> GetByIdAsync(long id, CancellationToken ct = default) => _inner.GetByIdAsync(id, ct);
        public Task<IList<User>> GetAllAsync(CancellationToken ct = default) => _inner.GetAllAsync(ct);
        public Task<IList<User>> GetActiveUsersAsync(CancellationToken ct = default) => _inner.GetActiveUsersAsync(ct);
        public Task<IList<User>> GetByRoleAsync(UserRole role, CancellationToken ct = default) => _inner.GetByRoleAsync(role, ct);
        public Task DeleteAsync(long id, CancellationToken ct = default) => _inner.DeleteAsync(id, ct);
        public Task DeleteAsync(User entity, CancellationToken ct = default) => _inner.DeleteAsync(entity, ct);
        public Task<bool> ExistsAsync(long id, CancellationToken ct = default) => _inner.ExistsAsync(id, ct);
    }

    public UserIdentityAndRoleTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p310-ident-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
        => new(new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options);

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static CreateUserUseCase CreateUseCase(OpticDbContext ctx)
        => new(new UserRepository(ctx), new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));

    private static UpdateUserUseCase UpdateUseCase(OpticDbContext ctx)
        => new(new UserRepository(ctx), new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));

    private static CreateUserCommand Create(string username, UserRole? role = UserRole.Optician) => new()
    {
        Username = username, FirstName = "Jean", LastName = "Dupont",
        Role = role, IsActive = true, Password = "S3cretPass1",
    };

    // ---------------------------------------------------------------- Normalisation / unicité

    /// <summary>
    /// « admin », « Admin » et «  ADMIN  » désignent le MÊME login : seule la première création réussit.
    /// </summary>
    [Theory]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("  ADMIN  ")]
    [InlineData("aDmIn")]
    public async Task Create_CaseOrSpaceVariantOfExistingLogin_IsRejected(string variant)
    {
        var dbPath = PathFor($"case-{Math.Abs(variant.GetHashCode())}.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var first = await CreateUseCase(context).ExecuteAsync(Create("admin"));
            first.IsValid.Should().BeTrue();
        }

        using (var context = CreateContext(dbPath))
        {
            var second = await CreateUseCase(context).ExecuteAsync(Create(variant));

            second.UsernameTaken.Should().BeTrue();
            second.IsValid.Should().BeFalse();
            second.ValidationErrors.Should().ContainSingle()
                .Which.Message.Should().Be("Ce nom d'utilisateur est déjà utilisé.");
        }

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Should().ContainSingle("une variante de casse ne crée pas un second compte");
    }

    [Fact]
    public async Task Create_TrimsDisplayName_AndStoresNormalizedKey()
    {
        var dbPath = PathFor("trim.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var result = await CreateUseCase(context).ExecuteAsync(Create("  Marie.Optic  "));
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.Username.Should().Be("Marie.Optic", "la forme affichable conserve la casse saisie");
        user.NormalizedUsername.Should().Be("marie.optic");
    }

    [Fact]
    public async Task Update_ToCaseVariantOfAnotherUser_IsRejected()
    {
        var dbPath = PathFor("update-collide.db");
        EnsureSchema(dbPath);

        long secondId;
        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Create("admin"));
            var second = await CreateUseCase(context).ExecuteAsync(Create("marie.optic"));
            secondId = second.UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(context).ExecuteAsync(new UpdateUserCommand
            {
                UserId = secondId, Username = "ADMIN", FirstName = "M", LastName = "O",
                Role = UserRole.Optician, IsActive = true,
            });

            result.UsernameTaken.Should().BeTrue();
            result.IsValid.Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single(u => u.UserId == secondId)
            .NormalizedUsername.Should().Be("marie.optic", "le refus ne modifie rien");
    }

    /// <summary>
    /// Ne changer que la CASSE de son propre login reste accepté : un compte n'est pas son propre doublon.
    /// </summary>
    [Fact]
    public async Task Update_OwnLoginCaseOnly_IsAccepted()
    {
        var dbPath = PathFor("update-own-case.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            id = (await CreateUseCase(context).ExecuteAsync(Create("marie.optic"))).UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(context).ExecuteAsync(new UpdateUserCommand
            {
                UserId = id, Username = "Marie.Optic", FirstName = "Marie", LastName = "Durand",
                Role = UserRole.Optician, IsActive = true,
            });

            result.IsValid.Should().BeTrue();
            result.UsernameTaken.Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.Username.Should().Be("Marie.Optic");
        user.NormalizedUsername.Should().Be("marie.optic");
    }

    [Fact]
    public async Task NormalizedUsername_IsNeverEmpty_ForPersistedUsers()
    {
        var dbPath = PathFor("never-empty.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Create("admin"));
            await CreateUseCase(context).ExecuteAsync(Create("Marie.Optic"));
            await CreateUseCase(context).ExecuteAsync(Create("PIERRE.TECH"));
        }

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Should().HaveCount(3);
        verify.Users.AsNoTracking().Should().OnlyContain(u => u.NormalizedUsername != "");
    }

    // ---------------------------------------------------------------- Concurrence

    /// <summary>
    /// Course de deux créations sur le même login normalisé : une réussit, l'autre reçoit une erreur
    /// <b>métier</b> (<c>UsernameTaken</c>) — jamais une exception provider (SQLite/EF).
    /// </summary>
    [Fact]
    public async Task Create_ConcurrentSameNormalizedUsername_YieldsBusinessError_NotProviderException()
    {
        var dbPath = PathFor("race-create.db");
        EnsureSchema(dbPath);

        // Le « gagnant » de la course est déjà en base ; le perdant entre avec une garde applicative aveugle.
        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Create("admin"));
        }

        using var ctx = CreateContext(dbPath);
        var useCase = new CreateUserUseCase(
            new RaceLosingUserRepository(new UserRepository(ctx)),
            new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));

        // Variante de casse : c'est bien l'index unique NORMALISÉ qui arbitre, pas une égalité binaire.
        var result = await useCase.ExecuteAsync(Create("Admin"));

        result.UsernameTaken.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be("Ce nom d'utilisateur est déjà utilisé.");
        result.ValidationErrors[0].Message.Should().NotContainEquivalentOf("SQLITE");
        result.ValidationErrors[0].Message.Should().NotContainEquivalentOf("constraint failed");

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Should().ContainSingle("seule l'écriture gagnante subsiste");
    }

    /// <summary>
    /// Revue ciblée avant commit — état du <c>ChangeTracker</c> APRÈS un refus <c>UsernameTaken</c> par course, DANS
    /// LA MÊME portée de contexte que l'écriture rejetée. Le rollback SQL est prouvé (<c>OtherConstraintViolation_…</c>
    /// et les tests de course existants) ; ce qui n'était pas prouvé est l'état du suivi EF en mémoire, qui survit
    /// au rollback transactionnel (EF ne détache jamais automatiquement une entité sur échec de <c>SaveChanges</c>).
    /// </summary>
    /// <remarks>
    /// <b>Défaut trouvé et corrigé.</b> Avant le détachement ciblé ajouté par cette revue
    /// (<c>IUserRepository.DetachIfTracked</c>, appelé dans le <c>catch</c> de <c>CreateUserUseCase</c>), l'entité
    /// rejetée restait suivie en état <c>Added</c> : un <c>SaveChangesAsync</c> ultérieur et SANS RAPPORT, dans la
    /// même portée de contexte, retentait de la persister et échouait avec une <c>DbUpdateException</c> brute — un
    /// appelant innocent voyait son écriture casser à cause d'un refus qu'il ignorait. Ce test prouve l'état
    /// FINAL (corrigé) : plus aucune trace suivie, l'écriture sans rapport aboutit normalement.
    /// </remarks>
    [Fact]
    public async Task Create_ConcurrentRace_RejectedEntity_IsDetached_UnrelatedLaterSaveSucceeds()
    {
        var dbPath = PathFor("race-create-tracker.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Create("admin"));
        }

        using var ctx = CreateContext(dbPath);
        var useCase = new CreateUserUseCase(
            new RaceLosingUserRepository(new UserRepository(ctx)),
            new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));

        var result = await useCase.ExecuteAsync(Create("Admin"));
        result.UsernameTaken.Should().BeTrue();

        // Plus aucune trace de l'entité rejetée dans le tracker : le rollback SQL et le nettoyage du suivi EF
        // sont désormais cohérents l'un avec l'autre.
        ctx.ChangeTracker.Entries<User>()
            .Should().NotContain(e => e.Entity.NormalizedUsername == "admin" && e.Entity.UserId == 0,
                "l'entité candidate rejetée doit être détachée après le refus");

        // Une écriture ULTÉRIEURE et SANS RAPPORT, dans la même portée, doit réussir normalement — elle ne doit
        // plus jamais retenter, en silence, l'insertion déjà rejetée.
        var unrelated = new User
        {
            Username = "unrelated.user", NormalizedUsername = "unrelated.user",
            FirstName = "U", LastName = "R", Role = UserRole.Technician, IsActive = true,
            PasswordHash = "hash", CreatedAt = DateTime.UtcNow,
        };
        ctx.Users.Add(unrelated);
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync("un SaveChanges sans rapport ne doit pas hériter d'un refus antérieur");

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Select(u => u.NormalizedUsername).Should()
            .BeEquivalentTo(new[] { "admin", "unrelated.user" },
                "seule l'écriture gagnante initiale et l'écriture sans rapport subsistent ; l'entité rejetée par " +
                "la course n'a jamais été persistée");
    }

    [Fact]
    public async Task Update_ConcurrentCollision_ReturnsSameStableDuplicateResult_NoException()
    {
        var dbPath = PathFor("race-update.db");
        EnsureSchema(dbPath);

        long secondId;
        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Create("admin"));
            secondId = (await CreateUseCase(context).ExecuteAsync(Create("marie.optic"))).UserId;
        }

        using var ctx = CreateContext(dbPath);
        var useCase = new UpdateUserUseCase(
            new RaceLosingUserRepository(new UserRepository(ctx)),
            new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));

        var result = await useCase.ExecuteAsync(new UpdateUserCommand
        {
            UserId = secondId, Username = "ADMIN", FirstName = "M", LastName = "O",
            Role = UserRole.Optician, IsActive = true,
        });

        result.UserFound.Should().BeTrue();
        result.UsernameTaken.Should().BeTrue();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be("Ce nom d'utilisateur est déjà utilisé.");
    }

    /// <summary>
    /// Revue ciblée avant commit — même preuve que pour Create, côté Update. Avant le détachement ciblé, l'entité
    /// suivie (chargée par <c>GetByIdAsync</c>, mutée en place) restait en état <c>Modified</c> avec le login
    /// REFUSÉ après le rollback SQL : un <c>FindAsync</c> ultérieur dans la même portée aurait renvoyé cette
    /// instance corrompue au lieu d'interroger la base, et un <c>SaveChangesAsync</c> sans rapport aurait retenté
    /// de persister la mutation refusée. Ce test prouve l'état FINAL (corrigé).
    /// </summary>
    [Fact]
    public async Task Update_ConcurrentRace_RejectedEntity_IsDetached_FindAsyncReturnsFreshValue()
    {
        var dbPath = PathFor("race-update-tracker.db");
        EnsureSchema(dbPath);

        long secondId;
        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Create("admin"));
            secondId = (await CreateUseCase(context).ExecuteAsync(Create("marie.optic"))).UserId;
        }

        using var ctx = CreateContext(dbPath);
        var useCase = new UpdateUserUseCase(
            new RaceLosingUserRepository(new UserRepository(ctx)),
            new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));

        var result = await useCase.ExecuteAsync(new UpdateUserCommand
        {
            UserId = secondId, Username = "ADMIN", FirstName = "M", LastName = "O",
            Role = UserRole.Optician, IsActive = true,
        });
        result.UsernameTaken.Should().BeTrue();

        // Plus aucune trace suivie de la mutation refusée.
        ctx.ChangeTracker.Entries<User>()
            .Should().NotContain(e => e.Entity.UserId == secondId,
                "l'entité mutée puis rejetée doit être détachée après le refus");

        // FindAsync, dans la même portée, doit désormais interroger la base (identity map vidée de cette entrée)
        // et renvoyer les valeurs ORIGINALES — jamais l'instance en mémoire portant le login refusé.
        var reread = await ctx.Users.FindAsync(secondId);
        reread.Should().NotBeNull();
        reread!.NormalizedUsername.Should().Be("marie.optic", "le refus ne doit laisser aucune trace, même en mémoire");
        reread.FirstName.Should().Be("Jean", "les autres propriétés refusées (prénom, etc.) ne doivent pas non plus survivre");
    }

    /// <summary>
    /// Une violation de contrainte qui n'est <b>pas</b> une unicité de login ne doit jamais être maquillée en
    /// « nom d'utilisateur déjà utilisé » : elle remonte en <c>PersistenceException</c> neutre, catégorie
    /// <c>ConstraintViolation</c>. Le filtre <c>when</c> des use cases ne capture que <c>UniqueConstraint</c> —
    /// sans cela, une panne réelle serait signalée à l'exploitant comme une erreur de saisie.
    /// </summary>
    [Fact]
    public async Task OtherConstraintViolation_IsNotReportedAsDuplicateUsername()
    {
        var dbPath = PathFor("other-constraint.db");
        EnsureSchema(dbPath);

        using var ctx = CreateContext(dbPath);
        var runner = new EfTransactionRunner(ctx);

        // FK inexistante sur un mouvement de stock : contrainte violée, mais sans rapport avec le login.
        var act = async () => await runner.RunAsync(async token =>
        {
            ctx.StockMovements.Add(new StockMovement
            {
                ProductId = 999_999, Quantity = 1, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync(token);
        });

        var ex = (await act.Should().ThrowAsync<PersistenceException>()).Which;
        ex.Category.Should().Be(PersistenceErrorCategory.ConstraintViolation);
        ex.Category.Should().NotBe(PersistenceErrorCategory.UniqueConstraint);
        ex.Message.Should().NotContainEquivalentOf("nom d'utilisateur");
    }

    // ---------------------------------------------------------------- Rôles

    [Fact]
    public async Task Create_WithoutRole_IsRejected_AndNeverCreatesAdmin()
    {
        var dbPath = PathFor("role-missing.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var result = await CreateUseCase(context).ExecuteAsync(Create("jdupont", role: null));

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be("Le rôle est obligatoire.");

        context.Users.AsNoTracking().Should().BeEmpty(
            "une omission de rôle ne doit jamais produire un compte, et surtout pas un Admin");
    }

    [Fact]
    public async Task Update_WithoutRole_IsRejected_NoPropertyChanged()
    {
        var dbPath = PathFor("role-missing-update.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            id = (await CreateUseCase(context).ExecuteAsync(Create("jdupont", UserRole.Technician))).UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(context).ExecuteAsync(new UpdateUserCommand
            {
                UserId = id, Username = "renamed", FirstName = "N", LastName = "W", Role = null, IsActive = false,
            });

            result.UserFound.Should().BeTrue();
            result.IsValid.Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.Username.Should().Be("jdupont");
        user.Role.Should().Be(UserRole.Technician);
        user.IsActive.Should().BeTrue();
    }

    /// <summary>
    /// Valeur d'enum hors plage (obtenue par cast, comme le ferait un appelant mal informé) : refusée par
    /// <c>UserValidator.IsInEnum</c>, désormais réellement invoqué. Aucune valeur inconnue n'est persistée.
    /// </summary>
    [Fact]
    public async Task Create_WithOutOfRangeRole_IsRejected()
    {
        var dbPath = PathFor("role-invalid.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var result = await CreateUseCase(context).ExecuteAsync(Create("jdupont", (UserRole)999));

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.PropertyName == nameof(User.Role));
        context.Users.AsNoTracking().Should().BeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Optician)]
    [InlineData(UserRole.Technician)]
    public async Task Create_WithExplicitValidRole_IsAccepted(UserRole role)
    {
        var dbPath = PathFor($"role-ok-{role}.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            // Admin explicite reste accepté : sans ICurrentUser, la couche Application ne peut pas autoriser
            // l'ACTEUR d'une telle demande. Ce test ne prouve donc PAS l'autorisation (report explicite P3-10).
            var result = await CreateUseCase(context).ExecuteAsync(Create("jdupont", role));
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().Role.Should().Be(role);
    }
}
