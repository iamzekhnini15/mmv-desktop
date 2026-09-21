using System.Globalization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using MMV.Infrastructure.Data.Time;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D-R — <b>la règle de reprise des dates civiles, prouvée valeur par valeur.</b>
///
/// <para>
/// <see cref="LegacyCivilDateFormatTests"/> (P4-5D) a établi <b>que</b> le problème existe. Ce fichier
/// établit <b>ce que la reprise doit faire</b>, cas par cas, sans base de données : la règle est une
/// fonction pure, donc prouvable seule.
/// </para>
///
/// <para>
/// Le test le plus important du fichier est
/// <see cref="LaClassification_EstFidele_AuLecteurReel"/> : il confronte la règle au <b>vrai</b> lecteur
/// <c>Microsoft.Data.Sqlite</c>, sur toutes les formes recensées. Un détecteur qui divergerait du lecteur
/// signalerait des lignes saines ou en manquerait de cassées — il serait pire qu'absent.
/// </para>
/// </summary>
public sealed class CivilDateFormatTests
{
    // =====================================================================
    // (1) Conversion valide — le cas nominal d'une base antérieure à P4-5D
    // =====================================================================

    [Theory]
    // Format exact écrit par Microsoft.Data.Sqlite pour un DateTime à minuit — le cas de très loin le plus
    // fréquent : une date civile saisie n'a pas d'heure.
    [InlineData("1985-03-15 00:00:00", "1985-03-15")]
    // Une heure non nulle : elle existe en base (saisie passée par un DateTime.Now, import…). Elle est
    // perdue VOLONTAIREMENT — une date de naissance n'a pas d'heure (ADR-PROD-DB-004 §2.4).
    [InlineData("1985-03-15 12:30:45", "1985-03-15")]
    // Fractions de seconde : le pilote écrit jusqu'à sept décimales, zéros de fin omis. Les trois variantes
    // existent donc réellement en base, et une reprise qui n'en traiterait qu'une laisserait des lignes cassées.
    [InlineData("1985-03-15 12:30:45.123", "1985-03-15")]
    [InlineData("1985-03-15 00:00:00.0000000", "1985-03-15")]
    [InlineData("1985-03-15 12:30:45.1234567", "1985-03-15")]
    public void UneDateHistorique_EstReprise_ParTroncature(string legacy, string expected)
    {
        CivilDateFormat.Classify(legacy, out var repaired)
            .Should().Be(CivilDateFormatState.RepairableLegacyDateTime);
        repaired.Should().Be(expected);
    }

    [Fact]
    public void LaReprise_NeDecaleJamaisLeJour()
    {
        // LE test qui interdit la classe de bug que tout le lot vise. Une conversion de fuseau sur une date
        // civile déplacerait le jour — 23:59:59 basculerait au 16 en UTC+1, 00:00:00 au 14 en UTC-1. La
        // transformation est une TRONCATURE : le jour est un fait, il ne se recalcule pas.
        CivilDateFormat.Classify("1985-03-15 23:59:59", out var lateEvening);
        lateEvening.Should().Be("1985-03-15", "23:59:59 reste le 15, jamais le 16");

        CivilDateFormat.Classify("1985-03-15 00:00:00", out var midnight);
        midnight.Should().Be("1985-03-15", "minuit reste le 15, jamais le 14");
    }

    // =====================================================================
    // (2) Cas invalides — on signale, on n'invente pas
    // =====================================================================

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("15/03/1985")]        // format français : la reprise ne fait AUCUNE hypothèse de culture.
    [InlineData("not-a-date at all")]
    [InlineData("0")]
    public void UneValeurIncomprehensible_NEstJamaisTransformee(string invalid)
    {
        CivilDateFormat.Classify(invalid, out var repaired)
            .Should().Be(CivilDateFormatState.Unrepairable);
        repaired.Should().BeEmpty("aucune valeur de remplacement n'est proposée pour ce qu'on ne comprend pas");
    }

    [Fact]
    public void UneDateInexistanteAuCalendrier_EstRefusee_JamaisRecalee()
    {
        // Le piège le plus sournois du lot. SQLite « corrige » tout seul date('2026-02-30') en '2026-03-02' :
        // écrire la reprise avec date() aurait donc INVENTÉ une date plausible, définitivement, sans trace.
        // La règle refuse. Une donnée fausse doit rester visible, pas devenir crédible.
        CivilDateFormat.Classify("2026-02-30", out var repaired)
            .Should().Be(CivilDateFormatState.Unrepairable);
        repaired.Should().BeEmpty();

        CivilDateFormat.Classify("2025-02-29 00:00:00", out _)
            .Should().Be(CivilDateFormatState.Unrepairable, "2025 n'est pas bissextile");
    }

    [Fact]
    public void SqliteRecaleraitSilencieusement_CeQueLaRegleRefuse()
    {
        // La preuve, exécutée, de ce qu'affirme le test précédent : ce n'est pas une précaution théorique.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT date('2026-02-30')";

        command.ExecuteScalar().Should().Be("2026-03-02",
            "SQLite reporte le débordement sur mars — c'est exactement pourquoi date() est proscrite ici");
    }

    // =====================================================================
    // (3) Cas limites
    // =====================================================================

    [Fact]
    public void UneValeurNulle_EstHorsPerimetre()
    {
        // BirthDate est nullable : une date de naissance absente n'a pas de format à corriger. Lui en donner
        // un reviendrait à inventer une naissance.
        CivilDateFormat.Classify(null, out var repaired)
            .Should().Be(CivilDateFormatState.NotApplicable);
        repaired.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1900-01-01 00:00:00", "1900-01-01")]   // patient centenaire : plausible en optique.
    [InlineData("1899-12-31 00:00:00", "1899-12-31")]   // avant 1900 : aucune borne implicite.
    [InlineData("0001-01-01 00:00:00", "0001-01-01")]   // DateTime.MinValue : un « non renseigné » historique.
    [InlineData("2099-06-01 00:00:00", "2099-06-01")]   // futur lointain (ordonnance mal saisie).
    [InlineData("9999-12-31 23:59:59", "9999-12-31")]   // DateTime.MaxValue.
    [InlineData("2024-02-29 00:00:00", "2024-02-29")]   // 29 février d'une année réellement bissextile.
    public void LesDatesExtremes_SontReprises_SansBorneArbitraire(string legacy, string expected)
    {
        // La reprise corrige un FORMAT, elle n'arbitre pas la vraisemblance métier. Rejeter 0001-01-01 ferait
        // échouer le démarrage sur une valeur que l'application acceptait hier.
        CivilDateFormat.Classify(legacy, out var repaired)
            .Should().Be(CivilDateFormatState.RepairableLegacyDateTime);
        repaired.Should().Be(expected);
    }

    [Fact]
    public void UneValeurDejaCanonique_NEstPasTouchee()
    {
        CivilDateFormat.Classify("1985-03-15", out var repaired)
            .Should().Be(CivilDateFormatState.Canonical);
        repaired.Should().BeEmpty("aucune écriture n'est nécessaire sur une valeur déjà à la forme cible");
    }

    // =====================================================================
    // (4) Le piège : des valeurs QUI FONCTIONNENT et qu'il ne faut pas casser
    // =====================================================================

    [Theory]
    [InlineData("1985-03-15T00:00:00", "1985-03-15")]
    [InlineData("1985-03-15T12:30:45.123", "1985-03-15")]
    [InlineData("1985-03-15 ", "1985-03-15")]
    public void UneValeurLisibleNonCanonique_EstRemiseEnForme_SansChangerDeValeur(string raw, string expected)
    {
        // Ces valeurs sont LUES SANS ERREUR par le modèle courant (la forme ISO avec « T » notamment). Les
        // remettre en forme reste utile — la cible est « yyyy-MM-dd » — mais n'est autorisé que parce que la
        // troncature redonne EXACTEMENT la même date. La réécriture est démontrablement neutre.
        CivilDateFormat.IsReadableByCurrentModel(raw, out var readValue).Should().BeTrue();

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.NormalizableReadable);
        repaired.Should().Be(expected);
        DateOnly.ParseExact(repaired, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .Should().Be(readValue, "la remise en forme ne doit RIEN changer à la date lue");
    }

    [Theory]
    [InlineData(" 1985-03-15")]   // tronquée, elle donnerait « ␣1985-03-1 » — qui se lit 1er mars.
    [InlineData("1985-3-5")]      // huit caractères : la troncature ne s'applique pas.
    [InlineData("1985/03/15")]    // séparateurs « / » : lue correctement, mais pas canonique pour autant.
    public void UneValeurLisible_QueLaTroncatureCasserait_EstLaisseeIntacte(string raw)
    {
        // Le cœur de la sûreté de la règle, et la raison pour laquelle la lisibilité est testée EN PREMIER.
        // Ces valeurs fonctionnent aujourd'hui ; une reprise naïve « tout ce qui dépasse dix caractères est
        // tronqué » les détruirait. Mieux vaut une valeur non canonique qui marche qu'une valeur normalisée
        // qui a changé de sens — ou qui ne se lit plus.
        CivilDateFormat.IsReadableByCurrentModel(raw, out _).Should().BeTrue();

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.ReadableLeftAsIs);
        repaired.Should().BeEmpty();
        CivilDateFormat.AllowsRewrite(CivilDateFormatState.ReadableLeftAsIs).Should().BeFalse();
    }

    [Fact]
    public void LaRepriseNaive_EnSqlDeMasse_ChangeraitSilencieusementUneDate()
    {
        // LA contre-preuve, exécutée, et le résultat est pire que prévu. P4-5D notait que la transformation
        // « est exprimable en SQL pur » : elle l'est, mais la forme globale évidente est FAUSSE. Appliquée
        // sans discernement à « ␣1985-03-15 » — onze caractères, lue parfaitement comme le 15 mars — elle
        // produit « ␣1985-03-1 », qui ne lève RIEN : la valeur se lit désormais comme le 1er mars.
        //
        // Une date de naissance changée de quatorze jours, sans erreur, sans trace, sur une donnée de santé.
        // C'est ce constat, et non une préférence de style, qui impose la reprise ligne à ligne guidée par la
        // classification plutôt qu'un UPDATE de masse.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T (V TEXT); INSERT INTO T (V) VALUES (' 1985-03-15')";
            create.ExecuteNonQuery();
        }

        using (var naive = connection.CreateCommand())
        {
            naive.CommandText = "UPDATE T SET V = substr(V, 1, 10) WHERE length(V) > 10";
            naive.ExecuteNonQuery();
        }

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT V FROM T";
        var corrupted = (string)read.ExecuteScalar()!;

        corrupted.Should().Be(" 1985-03-1");

        CivilDateFormat.IsReadableByCurrentModel(" 1985-03-15", out var avant).Should().BeTrue();
        CivilDateFormat.IsReadableByCurrentModel(corrupted, out var apres).Should().BeTrue(
            "et c'est bien là le danger : la valeur corrompue se lit toujours, donc rien ne signale la faute");

        avant.Should().Be(new DateOnly(1985, 3, 15));
        apres.Should().Be(new DateOnly(1985, 3, 1));
        apres.Should().NotBe(avant, "la reprise naïve a changé la date de naissance sans lever la moindre erreur");

        // La règle retenue, elle, laisse cette valeur intacte.
        CivilDateFormat.Classify(" 1985-03-15", out _).Should().Be(CivilDateFormatState.ReadableLeftAsIs);
    }

    // =====================================================================
    // (5) Idempotence — la règle est un point fixe
    // =====================================================================

    [Theory]
    [InlineData("1985-03-15 00:00:00")]
    [InlineData("1985-03-15 12:30:45.1234567")]
    [InlineData("1985-03-15T12:30:45.123")]
    [InlineData("1985-03-15")]
    public void AppliquerLaRegleDeuxFois_DonneLeMemeResultat(string raw)
    {
        var state = CivilDateFormat.Classify(raw, out var once);
        var value = CivilDateFormat.AllowsRewrite(state) ? once : raw;

        // Deuxième passage sur le résultat du premier : plus rien à faire, par construction.
        CivilDateFormat.Classify(value, out var twice).Should().Be(CivilDateFormatState.Canonical);
        twice.Should().BeEmpty();
        CivilDateFormat.IsCanonical(value).Should().BeTrue();
    }

    // =====================================================================
    // (6) Fidélité au lecteur réel — le test qui garde tous les autres honnêtes
    // =====================================================================

    [Fact]
    public void LaClassification_EstFidele_AuLecteurReel()
    {
        // On confronte la règle au VRAI lecteur Microsoft.Data.Sqlite, pour chaque forme recensée : même
        // verdict (lisible ou non) ET même date produite. Si une montée de version du pilote changeait le
        // comportement de lecture, ce test le signalerait ici plutôt qu'en production, sur la base d'un
        // client.
        string[] corpus =
        {
            "1985-03-15", "1985-03-15 00:00:00", "1985-03-15 00:00:00.0000000",
            "1985-03-15 12:30:45", "1985-03-15 12:30:45.1234567", "1985-03-15T12:30:45.123",
            "1985-03-15T00:00:00", "1985-03-15 12:30:45+01:00", "0001-01-01 00:00:00",
            "9999-12-31 23:59:59", "abc", "", "   ", "15/03/1985", "1985-3-5",
            "1985-03-15 24:00:00", "2026-02-30", " 1985-03-15", "1985-03-15 ",
            "2024-02-29 00:00:00", "1899-12-31 00:00:00", "2099-06-01 00:00:00",
            // P4-5D-R Phase C (D-B1) : suffixes de fuseau, suffixes inconnus, formes lisibles hors grammaire.
            "1985-03-15T12:30:45Z", "1985-03-15T12:30:45+01:00", "1985-03-15T23:30:00-05:00",
            "1985-03-15garbage", "1985-03-15 12:30", "1985-03-15T12:30", "1985-03-15t12:30:45",
            "1985-03-15 12:30:45\n",
            // P4-5D-R Phase C2 (Q-C2) : heures hors bornes, dans les deux séparateurs, et huit décimales.
            "1985-03-15 25:00:00", "1985-03-15 12:60:00", "1985-03-15 12:00:60", "1985-03-15 23:59:60",
            "1985-03-15T24:00:00", "1985-03-15T12:60:00", "1985-03-15T23:59:60",
            "1985-03-15 12:30:45.12345678", "1985-03-15T12:30:45.12345678",
        };

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T (V TEXT)";
            create.ExecuteNonQuery();
        }

        foreach (var raw in corpus)
        {
            using (var reset = connection.CreateCommand())
            {
                reset.CommandText = "DELETE FROM T; INSERT INTO T (V) VALUES ($v)";
                reset.Parameters.AddWithValue("$v", raw);
                reset.ExecuteNonQuery();
            }

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT V FROM T";
            using var reader = select.ExecuteReader();
            reader.Read();

            DateOnly readerValue = default;
            var readerSucceeds = true;
            try
            {
                readerValue = reader.GetFieldValue<DateOnly>(0);
            }
            catch (FormatException)
            {
                readerSucceeds = false;
            }

            var ruleSucceeds = CivilDateFormat.IsReadableByCurrentModel(raw, out var ruleValue);

            ruleSucceeds.Should().Be(readerSucceeds,
                $"la règle et le lecteur doivent s'accorder sur la lisibilité de '{raw}'");
            if (readerSucceeds)
            {
                ruleValue.Should().Be(readerValue,
                    $"la règle et le lecteur doivent produire la MÊME date pour '{raw}'");
            }
        }
    }

    [Fact]
    public void ToutFormatIllisible_EstSoitReparable_SoitSignale()
    {
        // Totalité de la classification : aucune valeur ne peut échapper aux cinq états. Une valeur illisible
        // est donc toujours soit reprise, soit explicitement signalée — jamais ignorée en silence.
        string[] corpus =
        {
            "1985-03-15", "1985-03-15 00:00:00", "abc", "", "2026-02-30", " 1985-03-15",
            "1985-3-5", "1985-03-15T00:00:00", "15/03/1985", "1985-03-15 24:00:00",
        };

        foreach (var raw in corpus)
        {
            var state = CivilDateFormat.Classify(raw, out var repaired);
            var readable = CivilDateFormat.IsReadableByCurrentModel(raw, out _);

            if (!readable)
            {
                state.Should().BeOneOf(
                    CivilDateFormatState.RepairableLegacyDateTime,
                    CivilDateFormatState.Unrepairable);
            }

            if (CivilDateFormat.AllowsRewrite(state))
            {
                CivilDateFormat.IsCanonical(repaired).Should().BeTrue(
                    $"toute valeur de remplacement produite pour '{raw}' doit être canonique");
            }
        }
    }

    // =====================================================================
    // (7) P4-5D-R Phase C — D-B1 : la troncature n'est accordée qu'aux formes DateTime connues
    // =====================================================================

    [Theory]
    // T-B2 — les trois cas de la décision.
    [InlineData("1985-03-15T12:30:45Z")]
    [InlineData("1985-03-15T12:30:45+01:00")]
    [InlineData("1985-03-15T23:30:00-05:00")]      // en UTC : le 16 à 04:30.
    // Mêmes suffixes avec le séparateur espace et des fractions de seconde.
    [InlineData("1985-03-15 12:30:45Z")]
    [InlineData("1985-03-15 00:30:00+01:00")]      // en UTC : le 14 à 23:30.
    [InlineData("1985-03-15T12:30:45.1234567Z")]
    [InlineData("1985-03-15 12:30:45.123-05:00")]
    public void LesValeursAvecDecalage_SontTronquees_SansConversion(string raw)
    {
        // Le décalage n'est jamais appliqué : il disparaît avec l'heure. Le jour retenu est le jour ÉCRIT, seule
        // lecture correcte d'une date civile.
        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.RepairableLegacyDateTime);
        repaired.Should().Be("1985-03-15");
    }

    [Fact]
    public void UneConversionUtc_AuraitChangeLeJour_CeQueLaRegleNeFaitPas()
    {
        // Ce qui rend le test précédent discriminant : lues comme des instants, ces valeurs changent de jour.
        DateTimeOffset.Parse("1985-03-15T23:30:00-05:00", CultureInfo.InvariantCulture)
            .UtcDateTime.Day.Should().Be(16);
        DateTimeOffset.Parse("1985-03-15 00:30:00+01:00", CultureInfo.InvariantCulture)
            .UtcDateTime.Day.Should().Be(14);

        CivilDateFormat.Classify("1985-03-15T23:30:00-05:00", out var west);
        CivilDateFormat.Classify("1985-03-15 00:30:00+01:00", out var east);

        west.Should().Be("1985-03-15", "jamais le 16");
        east.Should().Be("1985-03-15", "jamais le 14");
    }

    [Theory]
    // T-B3 — le cas de la décision.
    [InlineData("1985-03-15garbage")]
    [InlineData("1985-03-15 12:30")]               // heure sans secondes : jamais écrite par DateTime.
    [InlineData("1985-03-15 12:30:45 garbage")]
    [InlineData("1985-03-15X12:30:45")]            // séparateur inconnu.
    [InlineData("1985-03-15  12:30:45")]           // double espace.
    [InlineData("1985-03-15 12:30:4")]
    [InlineData("1985-03-15 12:30:45.")]           // point sans fraction.
    [InlineData("1985-03-15 12:30:45.12345678")]   // huit décimales : au-delà de la précision de DateTime.
    [InlineData("1985-03-15 12:30:45z")]
    [InlineData("1985-03-15 12:30:45ZZ")]
    [InlineData("1985-03-15 12:30:45+0100")]
    [InlineData("1985-03-15 12:30:45+01")]
    [InlineData("1985-03-15 12:30:45 +01:00")]
    [InlineData("1985-03-15 12:30:45+01:00:00")]
    [InlineData("1985-03-15 12:30:45Z+01:00")]
    [InlineData("1985-03-15 12:30:45\n")]          // « $ » aurait accepté ce saut de ligne final, « \z » non.
    [InlineData("1985-03-15 ١٢:30:45")]  // chiffres arabes-indiens : « \d » les aurait acceptés.
    public void UnSuffixeInconnu_EstRefuse_JamaisTronque(string raw)
    {
        // La tête « 1985-03-15 » est valide, mais ce qui suit n'a pas été écrit par DateTime : on ne sait pas ce
        // que la valeur signifie. La tronquer fabriquerait une date « propre » d'origine inconnue ; elle est donc
        // signalée (et refuse le démarrage, Q2), jamais transformée.
        CivilDateFormat.IsReadableByCurrentModel(raw, out _).Should().BeFalse("précondition mesurée : illisible");
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeFalse();

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.Unrepairable);
        repaired.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1985-03-15 12:30:45")]
    [InlineData("1985-03-15 12:30:45.1")]
    [InlineData("1985-03-15 12:30:45.1234567")]
    [InlineData("1985-03-15T12:30:45")]
    [InlineData("1985-03-15T12:30:45.1234567")]
    [InlineData("1985-03-15 12:30:45Z")]
    [InlineData("1985-03-15 12:30:45.1234567+01:00")]
    [InlineData("1985-03-15T12:30:45-05:00")]
    [InlineData("1985-03-15T12:30:45.1234567Z")]
    public void LesFormesHistoriquesAutorisees_RestentReprises(string raw)
    {
        // Non-régression : les quatre formes validées, avec ou sans suffixe ISO, sont toujours réécrites. La forme
        // en « T » sans suffixe est lue par le modèle (réécriture neutre), les autres ne le sont pas (troncature) :
        // dans les deux cas le résultat est le jour écrit.
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeTrue();

        var state = CivilDateFormat.Classify(raw, out var repaired);

        CivilDateFormat.AllowsRewrite(state).Should().BeTrue();
        repaired.Should().Be("1985-03-15");
    }

    [Theory]
    [InlineData("2026-02-30 00:00:00")]
    [InlineData("2026-02-30T00:00:00Z")]
    [InlineData("2025-02-29T00:00:00+01:00")]
    [InlineData("1985-13-01 00:00:00")]
    [InlineData("1985-00-10 00:00:00")]
    [InlineData("0000-01-01 00:00:00")]
    public void UneDateInexistante_ResteRefusee_MemeSousUneFormeConforme(string raw)
    {
        // La grammaire ne remplace pas le contrôle calendaire : elle s'y ajoute.
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeTrue("la forme est conforme, c'est la date qui ne l'est pas");

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.Unrepairable);
        repaired.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1985-03-15T12:30:45Z")]
    [InlineData("1985-03-15T23:30:00-05:00")]
    [InlineData("1985-03-15 12:30:45.123+01:00")]
    public void LaRepriseDesFormesAvecDecalage_EstUnPointFixe(string raw)
    {
        var state = CivilDateFormat.Classify(raw, out var once);
        CivilDateFormat.AllowsRewrite(state).Should().BeTrue();

        CivilDateFormat.Classify(once, out var twice).Should().Be(CivilDateFormatState.Canonical);
        twice.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1985-03-15T12:30")]
    [InlineData("1985-03-15t12:30:45")]
    [InlineData("1985-03-15 ")]
    public void D_B1_NeRestreintPas_LaMiseEnFormeNeutre_DUneValeurDejaLue(string raw)
    {
        // Portée de D-B1 : la grammaire gouverne la TRONCATURE d'une valeur illisible, dont elle fixe le sens. Une
        // valeur hors grammaire mais DÉJÀ LUE par le modèle a un sens établi par le lecteur lui-même ; sa remise en
        // forme reste permise parce qu'elle est prouvée neutre (même date avant et après).
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeFalse();
        CivilDateFormat.IsReadableByCurrentModel(raw, out var readValue).Should().BeTrue();

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.NormalizableReadable);
        DateOnly.ParseExact(repaired, "yyyy-MM-dd", CultureInfo.InvariantCulture).Should().Be(readValue);
    }

    // =====================================================================
    // (8) P4-5D-R Phase C2 — Q-C2 : seule une heure que DateTime a pu écrire autorise la troncature
    // =====================================================================

    [Theory]
    // Les quatre cas de la décision.
    [InlineData("1985-03-15 24:00:00")]            // ISO 8601 : minuit DU LENDEMAIN — jamais écrit par DateTime.
    [InlineData("1985-03-15 25:00:00")]
    [InlineData("1985-03-15 12:60:00")]
    [InlineData("1985-03-15 12:00:60")]
    // Mêmes bornes, sous toutes les formes que la grammaire accepte par ailleurs.
    [InlineData("1985-03-15 23:59:60")]            // seconde intercalaire : DateTime ne la représente pas.
    [InlineData("1985-03-15 30:00:00")]
    [InlineData("1985-03-15 99:99:99")]
    [InlineData("1985-03-15 24:00:00.0000000")]
    [InlineData("1985-03-15 12:00:60.5")]
    [InlineData("1985-03-15T24:00:00")]
    [InlineData("1985-03-15T12:60:00")]
    [InlineData("1985-03-15T23:59:60")]
    [InlineData("1985-03-15 24:00:00Z")]
    [InlineData("1985-03-15 12:60:00+01:00")]
    [InlineData("1985-03-15T12:00:60-05:00")]
    [InlineData("2024-02-29 24:00:00")]            // ISO 8601 le lirait 1er mars : le jour même serait incertain.
    public void UneHeureHorsBornes_EstRefusee_JamaisTronquee(string raw)
    {
        // Remplace UneHeureInvalide_NEmpechePasLaReprise_DeLaPartieDate (Phase C), qui figeait l'inverse. Une heure
        // que DateTime n'a jamais pu écrire prouve que la valeur ne vient pas de l'application : son sens n'est pas
        // établi, et la réduire à sa tête fabriquerait une date « propre » d'origine inconnue.
        CivilDateFormat.IsReadableByCurrentModel(raw, out _).Should().BeFalse("précondition mesurée : illisible");
        CivilDateFormat.TryTruncateToCivilDate(raw, out _, out _)
            .Should().BeTrue("la tête est une date valide : c'est l'heure seule qui refuse la reprise");
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeFalse();

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.Unrepairable);
        repaired.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1985-03-15 00:00:00")]
    [InlineData("1985-03-15 23:59:59")]
    [InlineData("1985-03-15 00:00:59")]
    [InlineData("1985-03-15 00:59:00")]
    [InlineData("1985-03-15 19:59:59")]            // « [01][0-9] » : dernière heure de la première branche.
    [InlineData("1985-03-15 20:00:00")]            // « 2[0-3] » : première heure de la seconde branche.
    [InlineData("1985-03-15 23:00:00")]
    [InlineData("1985-03-15 23:59:59.9999999")]    // dernier instant représentable du jour.
    public void LesBornesHorairesIncluses_RestentReparables(string raw)
    {
        // Non-régression Q-C2 : les bornes sont inclusives, et le cas nominal (minuit) reste repris.
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeTrue();

        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.RepairableLegacyDateTime);
        repaired.Should().Be("1985-03-15", "23:59:59 reste le 15, minuit aussi");
    }

    [Theory]
    // Les cinq cas acceptés de la décision. La forme « T » sans suffixe est lue par le modèle (réécriture neutre),
    // les autres ne le sont pas (troncature) : dans les deux cas, le résultat est le jour écrit.
    [InlineData("1985-03-15 00:00:00")]
    [InlineData("1985-03-15 23:59:59")]
    [InlineData("1985-03-15T12:30:45.123")]
    [InlineData("1985-03-15T12:30:45Z")]
    [InlineData("1985-03-15T12:30:45+01:00")]
    public void LesCasAcceptesDeQ_C2_SontReecrits_AuJourEcrit(string raw)
    {
        CivilDateFormat.IsLegacyDateTimeForm(raw).Should().BeTrue();

        var state = CivilDateFormat.Classify(raw, out var repaired);

        CivilDateFormat.AllowsRewrite(state).Should().BeTrue();
        repaired.Should().Be("1985-03-15");
    }

    [Theory]
    [InlineData("1985-03-15T23:59:59-05:00")]      // en UTC : le 16 à 04:59:59.
    [InlineData("1985-03-15 00:00:00+01:00")]      // en UTC : le 14 à 23:00.
    [InlineData("1985-03-15T23:59:59.9999999Z")]
    [InlineData("1985-03-15 00:00:00Z")]
    public void LesDecalages_AuxBornesHoraires_SontTronques_SansConversion(string raw)
    {
        // Q-C2 ne touche pas au suffixe : Z, +01:00 et -05:00 restent repris, et le jour retenu reste le jour ÉCRIT,
        // même aux deux extrémités de la journée où une conversion changerait le jour.
        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.RepairableLegacyDateTime);
        repaired.Should().Be("1985-03-15");
    }

    [Theory]
    [InlineData("1985-03-15 12:30:45+99:99")]
    [InlineData("1985-03-15 12:30:45-23:59")]
    [InlineData("1985-03-15T12:30:45+00:00")]
    public void LeDecalage_NEstControleQueSurSaStructure(string raw)
    {
        // Décision : le suffixe de fuseau est contrôlé sur sa STRUCTURE seule. Il n'est jamais appliqué ; sa valeur
        // est donc sans effet sur le résultat, et le borner n'apporterait rien à la sûreté de la reprise.
        CivilDateFormat.Classify(raw, out var repaired)
            .Should().Be(CivilDateFormatState.RepairableLegacyDateTime);
        repaired.Should().Be("1985-03-15");
    }

    [Fact]
    public void HuitDecimales_NeSontJamaisTronquees_MaisUneValeurDejaLueResteNormalisee()
    {
        // Q-C2 limite la fraction à sept chiffres, précision de DateTime. Illisible, la forme « ␣ » est refusée.
        CivilDateFormat.IsReadableByCurrentModel("1985-03-15 12:30:45.12345678", out _).Should().BeFalse();
        CivilDateFormat.Classify("1985-03-15 12:30:45.12345678", out var refused)
            .Should().Be(CivilDateFormatState.Unrepairable);
        refused.Should().BeEmpty();

        // Mesuré : la forme « T » est LUE par le lecteur réel, qui la comprend comme le 15 mars (voir
        // LaClassification_EstFidele_AuLecteurReel). Elle ne relève donc pas de la troncature mais de la mise en forme
        // neutre (Q-C1). La classer Unrepairable refuserait le démarrage sur une valeur que l'application lit aujourd'hui.
        const string readable = "1985-03-15T12:30:45.12345678";
        CivilDateFormat.IsLegacyDateTimeForm(readable).Should().BeFalse();
        CivilDateFormat.IsReadableByCurrentModel(readable, out var readValue).Should().BeTrue();
        readValue.Should().Be(new DateOnly(1985, 3, 15));

        CivilDateFormat.Classify(readable, out var normalized)
            .Should().Be(CivilDateFormatState.NormalizableReadable);
        normalized.Should().Be("1985-03-15");
    }
}
