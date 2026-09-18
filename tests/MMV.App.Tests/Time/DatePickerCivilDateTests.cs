using MMV.App.Time;

namespace MMV.App.Tests.Time;

/// <summary>
/// P4-5D — la frontière d'affichage des <b>dates civiles</b> (ADR-PROD-DB-004 §5 décisions 6 et 7,
/// obligation T6 : « auditer les six conversions <c>DateTimeOffset</c> de l'UI »).
///
/// <para>
/// <b>Le bogue supprimé.</b> Les deux formulaires concernés envoyaient <c>picker.UtcDateTime</c> vers la
/// couche Application. Le <c>DatePicker</c> d'Avalonia restitue la date choisie à minuit avec le décalage
/// <b>local</b> : sous un fuseau en avance sur UTC, le jour saisi n'était pas le jour enregistré. Sur une
/// date de naissance et une date d'ordonnance — donnée de santé — c'est une erreur métier (§2.4).
/// </para>
///
/// <para>
/// Ces tests sont écrits avec des décalages <b>explicites</b> plutôt qu'avec le fuseau de la machine :
/// une preuve qui ne vaudrait qu'à Paris ne vaudrait rien en CI.
/// </para>
/// </summary>
public sealed class DatePickerCivilDateTests
{
    [Fact]
    public void UnFuseauEnAvanceSurUtc_NeReculePlusLeJour()
    {
        // LE cas de régression. En +02:00, minuit local est 22:00 la VEILLE en UTC : l'ancien
        // .UtcDateTime renvoyait le 14 mars pour une saisie du 15 mars.
        var picked = new DateTimeOffset(1985, 3, 15, 0, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(new DateOnly(1985, 3, 15), DatePickerCivilDate.ToCivilDate(picked));
        Assert.NotEqual(new DateOnly(1985, 3, 14), DatePickerCivilDate.ToCivilDate(picked));
    }

    [Fact]
    public void UnFuseauEnRetardSurUtc_NAvancePasLeJourNonPlus()
    {
        // Le symétrique, moins connu mais tout aussi faux : en -05:00, une saisie tardive basculait sur
        // le lendemain UTC.
        var picked = new DateTimeOffset(1985, 3, 15, 23, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(new DateOnly(1985, 3, 15), DatePickerCivilDate.ToCivilDate(picked));
    }

    [Fact]
    public void UtcLuiMeme_EstTraiteCommeLesAutres()
    {
        var picked = new DateTimeOffset(1985, 3, 15, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(1985, 3, 15), DatePickerCivilDate.ToCivilDate(picked));
    }

    [Fact]
    public void LHeureDuSelecteur_EstIgnoree()
    {
        // Une date civile n'a pas d'heure. Deux saisies du même jour à des heures différentes doivent
        // produire exactement la même valeur métier.
        var matin = new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.FromHours(2));
        var soir = new DateTimeOffset(2026, 7, 20, 23, 59, 59, TimeSpan.FromHours(2));

        Assert.Equal(DatePickerCivilDate.ToCivilDate(matin), DatePickerCivilDate.ToCivilDate(soir));
    }

    [Fact]
    public void LAllerRetour_PreserveLaDate()
    {
        // Propriété centrale : charger une fiche puis l'enregistrer sans y toucher ne doit RIEN changer.
        // C'est le scénario par lequel l'ancienne conversion corrompait des données existantes, un jour
        // par ouverture de formulaire.
        var date = new DateOnly(1978, 7, 22);

        var roundTripped = DatePickerCivilDate.ToCivilDate(DatePickerCivilDate.ToPickerValue(date));

        Assert.Equal(date, roundTripped);
    }

    [Theory]
    [InlineData(2024, 2, 29)]  // année bissextile
    [InlineData(2026, 1, 1)]   // premier jour de l'année
    [InlineData(2026, 12, 31)] // dernier jour de l'année
    [InlineData(1900, 1, 1)]   // date ancienne plausible pour une naissance
    public void LAllerRetour_PreserveLesDatesLimites(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);

        Assert.Equal(date, DatePickerCivilDate.ToCivilDate(DatePickerCivilDate.ToPickerValue(date)));
    }

    [Fact]
    public void LaValeurDuSelecteur_PorteLeJourDemande()
    {
        // Sens lecture : le contrôle doit AFFICHER le jour du Domain, pas un jour décalé par le fuseau.
        var value = DatePickerCivilDate.ToPickerValue(new DateOnly(1985, 3, 15));

        Assert.Equal(1985, value.Year);
        Assert.Equal(3, value.Month);
        Assert.Equal(15, value.Day);
    }

    [Fact]
    public void UneDateAbsente_ResteAbsente_DansLesDeuxSens()
    {
        // BirthDate est facultative : « pas de date » ne doit jamais devenir une date par défaut.
        Assert.Null(DatePickerCivilDate.ToPickerValue((DateOnly?)null));
        Assert.Null(DatePickerCivilDate.ToCivilDate((DateTimeOffset?)null));
    }

    [Fact]
    public void UneDatePresente_TraverseLesSurchargesNullables()
    {
        DateOnly? date = new DateOnly(1990, 11, 8);

        var picker = DatePickerCivilDate.ToPickerValue(date);

        Assert.NotNull(picker);
        Assert.Equal(date, DatePickerCivilDate.ToCivilDate(picker));
    }
}
