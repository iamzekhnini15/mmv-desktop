using FluentAssertions;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P4-6C (ADR-PROD-DB-009 DP-4) — verdict → écran : E1 et E3a démarrent ; tout autre état bloque avec un titre, un
/// constat et une action propres. DP-4 exige au moins trois messages distincts (E2, E3b, E5).
/// </summary>
public sealed class ServerStartupCheckTests
{
    private static ServerSchemaVerdict Verdict(ServerSchemaState state, bool canStart = false, bool initialization = false) =>
        new(state, null, canStart, initialization, $"constat {state}");

    [Theory]
    [InlineData(ServerSchemaState.E1)]
    [InlineData(ServerSchemaState.E3a)]
    public void Served_states_start_normally(ServerSchemaState state)
    {
        ServerStartupCheck.For(Verdict(state, canStart: true)).Should().BeNull();
    }

    [Theory]
    [InlineData(ServerSchemaState.E2, "Mise à jour de la base requise", "opérateur")]
    [InlineData(ServerSchemaState.E3b, "Mise à jour du poste requise", "Installez la version")]
    [InlineData(ServerSchemaState.E3c, "Version incompatible", "support")]
    [InlineData(ServerSchemaState.E4, "Base non installée", "migrate")]
    [InlineData(ServerSchemaState.E5, "Maintenance en cours", "Relancez MMV")]
    [InlineData(ServerSchemaState.E7, "Base non reconnue", "aucune donnée n'a été modifiée")]
    public void Every_blocked_state_has_its_own_screen_with_the_guard_message(ServerSchemaState state, string title, string action)
    {
        var block = ServerStartupCheck.For(Verdict(state))!;

        block.Title.Should().Be(title);
        block.Message.Should().Be($"constat {state}");
        block.Action.Should().Contain(action);
    }

    [Fact]
    public void Unfinished_installation_is_not_presented_as_a_maintenance()
    {
        ServerStartupCheck.For(Verdict(ServerSchemaState.E5, initialization: true))!
            .Title.Should().Be("Installation de la base inachevée");
    }

    [Fact]
    public void The_three_mandatory_blocking_messages_are_distinct()
    {
        new[] { ServerSchemaState.E2, ServerSchemaState.E3b, ServerSchemaState.E5 }
            .Select(s => ServerStartupCheck.For(Verdict(s))!.Title).Should().OnlyHaveUniqueItems();
    }
}
