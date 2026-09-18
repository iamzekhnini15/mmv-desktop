using FluentAssertions;
using MMV.Domain.Interfaces.Time;
using MMV.Domain.Tests.TestDoubles;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D — contrat d'<see cref="IClock"/> et de son implémentation système
/// (ADR-PROD-DB-004 §5 décision 2, obligation T1).
///
/// <para>
/// Deux propriétés sont vérifiées séparément parce qu'elles répondent à deux objectifs distincts du lot :
/// le <b>Kind</b> renvoyé par <see cref="IClock.UtcNow"/> conditionne l'écriture en base (Npgsql refuse
/// tout autre Kind), tandis que le <b>déterminisme</b> conditionne la testabilité des scénarios datés.
/// </para>
/// </summary>
public sealed class ClockTests
{
    // ------------------------------------------------------------------ SystemClock : le contrat de Kind

    [Fact]
    public void SystemClock_UtcNow_PorteToujoursKindUtc()
    {
        // C'est LE contrat qui rend le convertisseur validant satisfaisable : si l'horloge de production
        // renvoyait un Local, toute écriture lèverait — le lot entier reposerait sur du sable.
        SystemClock.Instance.UtcNow.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void SystemClock_UtcNow_Avance()
    {
        // Contrepoint indispensable au test de Kind : une implémentation qui renverrait une constante UTC
        // satisferait le contrat de Kind tout en étant inutilisable. L'horloge système doit AVANCER.
        var first = SystemClock.Instance.UtcNow;
        Thread.Sleep(20);
        var second = SystemClock.Instance.UtcNow;

        second.Should().BeAfter(first);
    }

    [Fact]
    public void SystemClock_Instance_EstUnique()
    {
        // Le composition root enregistre CETTE instance ; les chemins non injectés la lisent directement.
        // Si deux appels renvoyaient deux instances, « une seule horloge dans le processus » serait faux.
        SystemClock.Instance.Should().BeSameAs(SystemClock.Instance);
    }

    [Fact]
    public void SystemClock_LocalToday_EstLaDateCivileDuPoste()
    {
        // Une date civile suit le calendrier de l'opérateur, pas UTC (ADR-PROD-DB-004 §2.4). La valeur
        // attendue est donc calculée depuis l'heure locale — et l'assertion tolère le franchissement de
        // minuit entre les deux lectures, seule imprécision légitime ici.
        var before = DateOnly.FromDateTime(DateTime.Now);
        var today = SystemClock.Instance.LocalToday;
        var after = DateOnly.FromDateTime(DateTime.Now);

        today.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ------------------------------------------------------------------ FixedClock : le déterminisme

    [Fact]
    public void FixedClock_RenvoieExactementLaMemeValeurAChaqueLecture()
    {
        var instant = new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Utc);
        IClock clock = new FixedClock(instant);

        clock.UtcNow.Should().Be(instant);
        clock.UtcNow.Should().Be(instant);
        clock.UtcNow.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void FixedClock_RefuseUnInstantQuiNEstPasUtc()
    {
        // L'échec est déplacé au point où la valeur est CHOISIE. Sans cette garde, une horloge de test mal
        // construite ferait échouer le test bien plus loin, dans le convertisseur, sur une pile EF illisible.
        var act = () => new FixedClock(new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Local));

        act.Should().Throw<ArgumentException>().WithParameterName("utcNow");
    }

    [Fact]
    public void FixedClock_PeutDissocierLaDateCivileDeLInstant()
    {
        // Le cas qui justifie l'existence même de LocalToday : un poste dont la date locale n'est PAS celle
        // d'UTC. À 23:30 UTC le 18, un poste en UTC+2 est déjà le 19 — et une ordonnance saisie à cet
        // instant porte la date du 19.
        var instant = new DateTime(2026, 9, 18, 23, 30, 0, DateTimeKind.Utc);
        IClock clock = new FixedClock(instant, new DateOnly(2026, 9, 19));

        clock.UtcNow.Should().Be(instant);
        clock.LocalToday.Should().Be(new DateOnly(2026, 9, 19));
        clock.LocalToday.Should().NotBe(DateOnly.FromDateTime(instant));
    }

    [Fact]
    public void FixedClock_SansDateCivileExplicite_UtiliseCelleDeLInstant()
    {
        IClock clock = new FixedClock(new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Utc));

        clock.LocalToday.Should().Be(new DateOnly(2026, 9, 18));
    }
}
