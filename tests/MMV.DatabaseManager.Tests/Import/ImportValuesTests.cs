using FluentAssertions;
using MMV.DatabaseManager.Import;
using MMV.Domain.Enums;

namespace MMV.DatabaseManager.Tests.Import;

/// <summary>P4-7 — conversion stricte SQLite → PostgreSQL, colonne par colonne, sans serveur.</summary>
public sealed class ImportValuesTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    private static ImportColumn Col(ImportColumnKind kind, bool nullable = false, int? max = null, int precision = 0, int scale = 0,
        Type? enumType = null, string storeType = "x") =>
        new("C", storeType, kind, nullable, max, precision, scale, enumType);

    private static ImportConversion Convert(ImportColumn column, object? raw) => ImportValues.Convert(column, raw, Paris);

    [Fact]
    public void Null_is_kept_in_a_nullable_column_and_refused_in_a_not_null_column()
    {
        Convert(Col(ImportColumnKind.Text, nullable: true), null).Should().Be(new ImportConversion(null, null, ImportNote.None, null));
        Convert(Col(ImportColumnKind.Text), null).Error.Should().Contain("NOT NULL");
        Convert(Col(ImportColumnKind.Integer), DBNull.Value).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Integers_are_range_checked_and_never_parsed_from_text()
    {
        Convert(Col(ImportColumnKind.Integer), 42L).Value.Should().Be(42);
        Convert(Col(ImportColumnKind.BigInt), long.MaxValue).Value.Should().Be(long.MaxValue);
        Convert(Col(ImportColumnKind.Integer), (long)int.MaxValue + 1).Error.Should().Contain("hors de");
        Convert(Col(ImportColumnKind.SmallInt), 40000L).IsValid.Should().BeFalse();
        Convert(Col(ImportColumnKind.Integer), "42").Error.Should().Contain("entier attendu");
        Convert(Col(ImportColumnKind.Integer), 4.0).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Booleans_accept_only_zero_and_one()
    {
        Convert(Col(ImportColumnKind.Boolean), 1L).Value.Should().Be(true);
        Convert(Col(ImportColumnKind.Boolean), 0L).Value.Should().Be(false);
        Convert(Col(ImportColumnKind.Boolean), 2L).IsValid.Should().BeFalse();
        Convert(Col(ImportColumnKind.Boolean), "true").IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.125, "0.12", true)]
    [InlineData(0.135, "0.14", true)]
    [InlineData(2.675, "2.68", true)] // valeur affichée du REAL (« R »), pas sa valeur binaire 2.67499…
    [InlineData(10.5, "10.50", false)]
    [InlineData(-3.005, "-3.00", true)]
    public void Money_is_rounded_half_to_even_from_the_displayed_real_value(double raw, string expected, bool rounded)
    {
        var conversion = Convert(Col(ImportColumnKind.Numeric, precision: 12, scale: 2), raw);

        conversion.IsValid.Should().BeTrue();
        ImportValues.Canonical(Col(ImportColumnKind.Numeric, scale: 2), conversion.Value).Should().Be(expected);
        conversion.Note.Should().Be(rounded ? ImportNote.Rounded : ImportNote.None);
        conversion.SourceDecimal.Should().Be(decimal.Parse(raw.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Money_accepts_integer_and_decimal_text_storage_and_refuses_overflow_and_non_finite_values()
    {
        Convert(Col(ImportColumnKind.Numeric, precision: 12, scale: 2), 7L).Value.Should().Be(7m);
        Convert(Col(ImportColumnKind.Numeric, precision: 5, scale: 2), "1.255").Value.Should().Be(1.26m);
        Convert(Col(ImportColumnKind.Numeric, precision: 3, scale: 2, storeType: "numeric(3,2)"), 9.995).Error
            .Should().Contain("hors de numeric(3,2)", "9.995 → 10.00 dépasse numeric(3,2)");
        Convert(Col(ImportColumnKind.Numeric, precision: 12, scale: 2), double.NaN).IsValid.Should().BeFalse();
        Convert(Col(ImportColumnKind.Numeric, precision: 12, scale: 2), "1e3").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Text_refuses_nul_characters_overlong_values_and_unknown_enum_names()
    {
        Convert(Col(ImportColumnKind.Text, max: 3), "a\0b").Error.Should().Contain("NUL");
        Convert(Col(ImportColumnKind.Text, max: 3), "abcd").Error.Should().Contain("4 caractères");
        Convert(Col(ImportColumnKind.Text, max: 3), "é😀ü").Value.Should().Be("é😀ü", "la longueur PostgreSQL compte les caractères, pas les UTF-16");
        Convert(Col(ImportColumnKind.Text, enumType: typeof(UserRole)), nameof(UserRole.Admin)).IsValid.Should().BeTrue();
        Convert(Col(ImportColumnKind.Text, enumType: typeof(UserRole)), "admin").Error.Should().Contain("UserRole");
        Convert(Col(ImportColumnKind.Text, enumType: typeof(UserRole)), "0").IsValid.Should().BeFalse("une énumération est importée par nom exact");
        Convert(Col(ImportColumnKind.Text), 5L).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Instants_are_read_as_store_local_time_and_converted_to_utc()
    {
        var summer = Convert(Col(ImportColumnKind.Instant), "2026-06-15 10:30:00");
        summer.Value.Should().Be(new DateTime(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc));
        ((DateTime)summer.Value!).Kind.Should().Be(DateTimeKind.Utc);
        Convert(Col(ImportColumnKind.Instant), "2026-01-15T10:30:00").Value.Should().Be(new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Instants_keep_microseconds_and_report_the_truncated_hundreds_of_nanoseconds()
    {
        var exact = Convert(Col(ImportColumnKind.Instant), "2026-06-15 10:30:00.123456");
        exact.Note.Should().Be(ImportNote.None);

        var truncated = Convert(Col(ImportColumnKind.Instant), "2026-06-15 10:30:00.1234567");
        truncated.Note.Should().Be(ImportNote.SubMicrosecondTruncated);
        truncated.Value.Should().Be(new DateTime(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc).AddTicks(1234560));
    }

    [Fact]
    public void Nonexistent_local_time_is_refused_and_ambiguous_local_time_takes_the_standard_offset()
    {
        Convert(Col(ImportColumnKind.Instant), "2026-03-29 02:30:00").Error.Should().Contain("inexistante");

        var ambiguous = Convert(Col(ImportColumnKind.Instant), "2026-10-25 02:30:00");
        ambiguous.Note.Should().Be(ImportNote.AmbiguousLocalTime);
        ambiguous.Value.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("2026-06-15 10:30:00Z")]
    [InlineData("2026-06-15 10:30:00+02:00")]
    [InlineData("15/06/2026 10:30")]
    [InlineData("")]
    public void Instants_with_an_offset_or_another_format_are_refused_rather_than_interpreted(string raw) =>
        Convert(Col(ImportColumnKind.Instant), raw).IsValid.Should().BeFalse();

    [Fact]
    public void Instant_before_the_representable_utc_range_is_refused()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        ImportValues.Convert(Col(ImportColumnKind.Instant), "0001-01-01 00:00:00", tokyo).Error.Should().Contain("plage");
    }

    [Theory]
    [InlineData("1985-03-15", ImportNote.None)]
    [InlineData("1985-03-15 00:00:00", ImportNote.CivilDateTruncated)]
    [InlineData("1985-03-15 12:30:45.1234567", ImportNote.CivilDateTruncated)]
    public void Civil_dates_follow_the_p4_5d_r_rule(string raw, ImportNote note)
    {
        var conversion = Convert(Col(ImportColumnKind.CivilDate), raw);

        conversion.Value.Should().Be(new DateOnly(1985, 3, 15));
        conversion.Note.Should().Be(note);
    }

    [Fact]
    public void Unrepairable_civil_date_is_refused()
    {
        Convert(Col(ImportColumnKind.CivilDate), "15/03/1985").Error.Should().Contain("irréparable");
        Convert(Col(ImportColumnKind.CivilDate), 19850315L).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Doubles_are_kept_exactly()
    {
        Convert(Col(ImportColumnKind.Double), -1.25).Value.Should().Be(-1.25);
        Convert(Col(ImportColumnKind.Double), 3L).Value.Should().Be(3.0);
        Convert(Col(ImportColumnKind.Double), "3").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Canonical_form_is_identical_for_the_converted_value_and_the_server_value()
    {
        ImportValues.Canonical(Col(ImportColumnKind.Integer), 5).Should().Be(ImportValues.Canonical(Col(ImportColumnKind.Integer), 5L));
        ImportValues.Canonical(Col(ImportColumnKind.Numeric, scale: 2), 1.5m).Should().Be("1.50");
        ImportValues.Canonical(Col(ImportColumnKind.CivilDate), new DateOnly(1985, 3, 15))
            .Should().Be(ImportValues.Canonical(Col(ImportColumnKind.CivilDate), new DateTime(1985, 3, 15)));
        ImportValues.Canonical(Col(ImportColumnKind.Instant, nullable: true), null).Should().Be("∅");
        var local = () => ImportValues.Canonical(Col(ImportColumnKind.Instant), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
        local.Should().Throw<InvalidCastException>("un instant non UTC n'est jamais comparé comme s'il l'était");
    }
}
