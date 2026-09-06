using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Domain.Tests.Repositories;

/// <summary>
/// P4-4B1 — Rollback défensif de <c>UnitOfWork.CommitAsync</c>.
///
/// <para>
/// Défaut mesuré en P4-4B0 : le rollback exécuté dans le <c>catch</c> de <c>CommitAsync</c>
/// recevait le jeton d'annulation client. Lorsque ce jeton était déjà annulé, le rollback
/// échouait <b>avant</b> d'être exécuté (transaction restée active) et son
/// <c>OperationCanceledException</c> pouvait masquer l'erreur initiale.
/// </para>
///
/// <para>
/// Correctif verrouillé ici : le rollback défensif utilise <see cref="CancellationToken.None"/>,
/// même garantie que <c>EfTransactionRunner.SafeRollbackAsync</c> (déjà prouvé SAFE_AS_IS).
/// </para>
///
/// <para>
/// Ces tests s'exécutent dans MMV.sln <b>sans serveur</b> : la transaction est un double
/// enregistreur qui reproduit le comportement réel observé sur PostgreSQL (un jeton déjà annulé
/// fait échouer <c>RollbackAsync</c> avant l'annulation). La preuve serveur reste celle du spike
/// E21 (EXECUTED_SPIKE local).
/// </para>
/// </summary>
public sealed class UnitOfWorkTests
{
    // ------------------------------------------------------------------
    // B1-1 — Un jeton client déjà annulé n'empêche pas le rollback
    // ------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_WhenClientTokenAlreadyCancelled_RollsBackWithNonCancellableToken()
    {
        var transaction = new RecordingDbContextTransaction();
        await using var context = new ControllableDbContext(transaction);
        var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        context.SaveChangesException = new InvalidOperationException("sentinelle P4-4B1");

        var act = async () => await unitOfWork.CommitAsync(cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>();

        transaction.RollbackCallCount.Should().Be(1, "le rollback défensif doit être exécuté exactement une fois");
        transaction.RollbackTokens.Should().ContainSingle();
        transaction.RollbackTokens[0].IsCancellationRequested.Should()
            .BeFalse("le rollback ne doit pas recevoir un jeton déjà annulé");
        transaction.RollbackTokens[0].Should().Be(CancellationToken.None);
        transaction.Disposed.Should().BeTrue("la transaction annulée doit être libérée");
    }

    // ------------------------------------------------------------------
    // B1-2 — L'exception initiale est préservée
    // ------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_WhenClientTokenAlreadyCancelled_PreservesOriginalException()
    {
        var sentinel = new InvalidOperationException("sentinelle P4-4B1");
        var transaction = new RecordingDbContextTransaction();
        await using var context = new ControllableDbContext(transaction);
        var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        context.SaveChangesException = sentinel;

        var act = async () => await unitOfWork.CommitAsync(cts.Token);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(sentinel, "l'erreur initiale ne doit pas être masquée par le rollback");
        thrown.Which.Should().NotBeOfType<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    // B1-3 — Jeton client normal : le rollback reste indépendant de ce jeton
    // ------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_WhenClientTokenNotCancelled_RollsBackWithTokenIndependentOfClient()
    {
        var transaction = new RecordingDbContextTransaction();
        await using var context = new ControllableDbContext(transaction);
        var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();

        using var cts = new CancellationTokenSource();
        context.SaveChangesException = new InvalidOperationException("sentinelle P4-4B1");

        var act = async () => await unitOfWork.CommitAsync(cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>();

        transaction.RollbackCallCount.Should().Be(1);
        transaction.RollbackTokens.Should().ContainSingle();
        transaction.RollbackTokens[0].IsCancellationRequested.Should().BeFalse();
        transaction.RollbackTokens[0].CanBeCanceled.Should()
            .BeFalse("le rollback ne doit pas dépendre du jeton client, même non annulé");
        transaction.RollbackTokens[0].Should().Be(CancellationToken.None);
    }

    // ------------------------------------------------------------------
    // B1-4 — Commit réussi : aucun rollback
    // ------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_WhenSaveSucceeds_CommitsAndDoesNotRollBack()
    {
        var transaction = new RecordingDbContextTransaction();
        await using var context = new ControllableDbContext(transaction);
        var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.CommitAsync();

        transaction.CommitCallCount.Should().Be(1);
        transaction.RollbackCallCount.Should().Be(0);
        transaction.Disposed.Should().BeTrue();
    }

    // ==================================================================
    // Doubles de test (aucun serveur, aucune connexion ouverte)
    // ==================================================================

    /// <summary>
    /// Transaction enregistreuse : mémorise les jetons reçus et reproduit le comportement réel
    /// d'un rollback recevant un jeton déjà annulé (échec avant exécution).
    /// </summary>
    private sealed class RecordingDbContextTransaction : IDbContextTransaction
    {
        private readonly List<CancellationToken> _rollbackTokens = new();

        public Guid TransactionId { get; } = Guid.NewGuid();

        public IReadOnlyList<CancellationToken> RollbackTokens => _rollbackTokens;

        public int CommitCallCount { get; private set; }

        public int RollbackCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCallCount++;
            _rollbackTokens.Add(cancellationToken);

            // Comportement réel mesuré en P4-4B0 : un jeton déjà annulé fait échouer le rollback
            // AVANT qu'il ne soit effectué, laissant la transaction active.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Commit() => throw new NotSupportedException("Chemin synchrone non utilisé par CommitAsync.");

        public void Rollback() => throw new NotSupportedException("Chemin synchrone non utilisé par CommitAsync.");

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Façade rendant la transaction contrôlable sans ouvrir de connexion.</summary>
    private sealed class FakeDatabaseFacade : DatabaseFacade
    {
        private readonly IDbContextTransaction _transaction;

        public FakeDatabaseFacade(DbContext context, IDbContextTransaction transaction)
            : base(context)
        {
            _transaction = transaction;
        }

        public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_transaction);
        }
    }

    /// <summary>
    /// Contexte de test : <c>SaveChangesAsync</c> pilotable et transaction contrôlée.
    /// Aucune connexion n'est ouverte (aucune requête n'est exécutée).
    /// </summary>
    private sealed class ControllableDbContext : OpticDbContext
    {
        private readonly FakeDatabaseFacade _database;

        public ControllableDbContext(IDbContextTransaction transaction)
            : base(new DbContextOptionsBuilder<OpticDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options)
        {
            _database = new FakeDatabaseFacade(this, transaction);
        }

        public Exception? SaveChangesException { get; set; }

        public override DatabaseFacade Database => _database;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SaveChangesException is not null)
            {
                throw SaveChangesException;
            }

            return Task.FromResult(0);
        }
    }
}
