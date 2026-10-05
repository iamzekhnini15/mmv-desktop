using System.Net.Sockets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace MMV.Domain.Tests.Persistence;

/// <summary>
/// P4-10 — <see cref="PersistenceErrorMapper.MapCommitFailure"/> : une validation dont la réponse s'est perdue a
/// une issue <b>inconnue</b> ; une validation refusée par une erreur serveur structurée a <b>certainement</b> échoué.
/// La preuve contre un vrai serveur (réponse au <c>COMMIT</c> perdue, ligne bien validée) est dans
/// <c>Resilience/NetworkFaultTests</c> (intégration PostgreSQL).
/// </summary>
public sealed class PersistenceCommitFailureMappingTests
{
    private static PostgresException Postgres(string sqlState) =>
        new("message synthétique", severity: "FATAL", invariantSeverity: "FATAL", sqlState: sqlState);

    private static PersistenceException Mapped(Exception exception) =>
        PersistenceErrorMapper.MapCommitFailure(exception).Should().BeOfType<PersistenceException>().Subject;

    public static TheoryData<Exception> LostResponses => new()
    {
        new NpgsqlException("exception during reading from stream", new EndOfStreamException()),
        new NpgsqlException("reset", new IOException("stream", new SocketException((int)SocketError.ConnectionReset))),
        new NpgsqlException("write timeout", new TimeoutException()),
        new InvalidOperationException("enveloppe EF", new NpgsqlException("sans cause")),
        Postgres("57P01"), // admin_shutdown : serveur arrêté pendant la validation
        Postgres("57P02"), // crash_shutdown
        Postgres("08006")  // connection_failure
    };

    [Theory]
    [MemberData(nameof(LostResponses))]
    public void Lost_commit_response_is_an_unknown_outcome_that_never_claims_nothing_was_kept(Exception failure)
    {
        var mapped = Mapped(failure);

        mapped.Category.Should().Be(PersistenceErrorCategory.CommitOutcomeUnknown);
        mapped.Message.Should().NotContain("Aucune modification").And.Contain("Vérifiez avant de le saisir à nouveau");
        mapped.InnerException.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData("23505", PersistenceErrorCategory.UniqueConstraint)]
    [InlineData("23503", PersistenceErrorCategory.ConstraintViolation)]
    [InlineData("40P01", PersistenceErrorCategory.Concurrency)]
    [InlineData("40001", PersistenceErrorCategory.Unknown)]
    public void Commit_refused_by_a_structured_server_error_certainly_failed(string sqlState, PersistenceErrorCategory expected)
    {
        var mapped = Mapped(new DbUpdateException("commit refusé", Postgres(sqlState)));

        mapped.Category.Should().Be(expected);
        mapped.Message.Should().Contain("Aucune modification n'a été conservée");
    }

    [Fact]
    public void Requested_cancellation_and_non_persistence_errors_are_relayed_unchanged()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var canceled = new OperationCanceledException(source.Token);
        var other = new ArgumentException("pas une erreur de persistance");

        PersistenceErrorMapper.MapCommitFailure(canceled).Should().BeSameAs(canceled);
        PersistenceErrorMapper.MapCommitFailure(other).Should().BeSameAs(other);
    }

    [Fact]
    public void The_ordinary_mapping_is_unchanged_outside_commit()
    {
        var lost = new NpgsqlException("reading", new EndOfStreamException());

        ((PersistenceException)PersistenceErrorMapper.Map(lost)).Category.Should().Be(PersistenceErrorCategory.ConnectionFailure,
            "avant la validation, une perte de connexion annule la transaction : rien n'est conservé");
    }
}
