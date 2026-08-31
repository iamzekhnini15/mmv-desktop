using System.Data;
using System.Net.Sockets;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// P4-4A0 §6 — TESTS DE LA SONDE elle-même.
///
/// <para>
/// PORTÉE STRICTE : ces tests vérifient que <see cref="ExceptionShape"/> parcourt correctement une
/// chaîne d'exceptions et détecte les types attendus. Les exceptions y sont FABRIQUÉES à la main,
/// ce qui est légitime pour éprouver l'outil de mesure — mais ne constitue AUCUNE preuve sur le
/// comportement réel de Npgsql. Les faits provider ne peuvent venir que d'un serveur réel
/// (scénarios A–E, <c>E19</c>).
/// </para>
///
/// <para>Hors <c>MMV.sln</c> : ce projet ne participe ni au build ni à la CI de production.</para>
/// </summary>
public class ExceptionShapeProbeTests
{
    [Fact]
    public void Capture_traverses_the_whole_inner_exception_chain()
    {
        var root = new InvalidOperationException("racine");
        var middle = new ApplicationException("intermediaire", root);
        var outer = new Exception("externe", middle);

        var shape = ExceptionShape.Capture(outer);

        Assert.Equal(3, shape.Frames.Count);
        Assert.Equal("System.Exception", shape.Frames[0].TypeName);
        Assert.Equal("System.ApplicationException", shape.Frames[1].TypeName);
        Assert.Equal("System.InvalidOperationException", shape.Frames[2].TypeName);
        Assert.Equal(new[] { 0, 1, 2 }, shape.Frames.Select(frame => frame.Depth));
        Assert.Equal("System.Exception", shape.OuterType);
    }

    [Fact]
    public void Capture_follows_every_branch_of_an_aggregate_exception_without_duplicating()
    {
        var first = new TimeoutException("premiere cause");
        var second = new SocketException((int)SocketError.ConnectionRefused);
        var aggregate = new AggregateException(first, second);

        var shape = ExceptionShape.Capture(aggregate);

        // 1 agrégat + 2 causes, chacune comptée UNE seule fois bien que
        // AggregateException.InnerException renvoie déjà InnerExceptions[0].
        Assert.Equal(3, shape.Frames.Count);
        Assert.True(shape.HasTimeoutException);
        Assert.True(shape.HasSocketException);
    }

    [Fact]
    public void Capture_detects_a_nested_socket_exception_by_type_and_reports_its_socket_error_code()
    {
        var socket = new SocketException((int)SocketError.ConnectionRefused);
        var outer = new Exception("externe", new Exception("intermediaire", socket));

        var shape = ExceptionShape.Capture(outer);

        Assert.True(shape.HasSocketException);
        Assert.Equal(SocketError.ConnectionRefused, shape.SocketErrorCode);
        Assert.Contains(shape.Frames, frame => frame.SocketErrorCode == SocketError.ConnectionRefused);
    }

    [Fact]
    public void Capture_detects_a_nested_timeout_exception_by_type()
    {
        var outer = new Exception("externe", new TimeoutException("interne"));

        var shape = ExceptionShape.Capture(outer);

        Assert.True(shape.HasTimeoutException);
        Assert.False(shape.HasSocketException);
        Assert.False(shape.HasOperationCanceledException);
    }

    [Fact]
    public void Capture_detects_cancellation_and_records_whether_the_token_was_actually_requested()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var shape = ExceptionShape.Capture(new OperationCanceledException("annulee", cts.Token));

        Assert.True(shape.HasOperationCanceledException);
        Assert.False(shape.HasTaskCanceledException);
        Assert.True(shape.CancellationWasRequested);
    }

    [Fact]
    public void Capture_treats_a_task_canceled_exception_as_part_of_the_cancellation_family()
    {
        // TaskCanceledException DÉRIVE de OperationCanceledException : la règle candidate
        // « ne jamais convertir une annulation en erreur de persistance » doit couvrir les deux.
        var shape = ExceptionShape.Capture(new TaskCanceledException("annulee"));

        Assert.True(shape.HasTaskCanceledException);
        Assert.True(shape.HasOperationCanceledException);
        Assert.False(shape.CancellationWasRequested);
    }

    [Fact]
    public void Capture_never_derives_anything_from_the_message_text()
    {
        // Message VOLONTAIREMENT trompeur : il contient un SqlState, le mot timeout et le mot socket.
        // Une sonde qui parserait le texte les relèverait ; celle-ci ne doit RIEN en tirer.
        var misleading = new InvalidOperationException(
            "SqlState=23505 timeout socket ConnectionRefused constraint 'IX_Notifications_LowStock'");

        var shape = ExceptionShape.Capture(misleading);

        Assert.Null(shape.SqlState);
        Assert.Null(shape.ConstraintName);
        Assert.False(shape.HasTimeoutException);
        Assert.False(shape.HasSocketException);
        Assert.False(shape.HasPostgresException);
        Assert.Null(shape.SocketErrorCode);
        Assert.DoesNotContain("23505", shape.ToEvidenceLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_line_carries_no_exception_message()
    {
        var shape = ExceptionShape.Capture(
            new Exception("SECRET-EXTERNE", new TimeoutException("SECRET-INTERNE")),
            ConnectionState.Open,
            ConnectionState.Closed);

        var line = shape.ToEvidenceLine();

        Assert.DoesNotContain("SECRET", line, StringComparison.Ordinal);
        Assert.Contains("TimeoutException=OUI", line, StringComparison.Ordinal);
        Assert.Contains("conn.avant=Open", line, StringComparison.Ordinal);
        Assert.Contains("conn.apres=Closed", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_bounds_a_very_deep_chain_instead_of_walking_it_forever()
    {
        Exception current = new InvalidOperationException("fond");
        for (var i = 0; i < 40; i++)
        {
            current = new Exception($"niveau-{i}", current);
        }

        var shape = ExceptionShape.Capture(current);

        // Garde-fou MaxDepth = 16 : profondeurs 0..16 relevées, soit 17 trames au maximum.
        Assert.Equal(17, shape.Frames.Count);
    }

    [Fact]
    public void Connection_states_are_recorded_as_observation_only_when_supplied()
    {
        var without = ExceptionShape.Capture(new TimeoutException());
        Assert.Null(without.ConnectionStateBefore);
        Assert.Null(without.ConnectionStateAfter);

        var with = ExceptionShape.Capture(new TimeoutException(), ConnectionState.Open, ConnectionState.Broken);
        Assert.Equal(ConnectionState.Open, with.ConnectionStateBefore);
        Assert.Equal(ConnectionState.Broken, with.ConnectionStateAfter);
    }
}
