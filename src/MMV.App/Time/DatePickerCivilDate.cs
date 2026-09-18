using System;

namespace MMV.App.Time;

/// <summary>
/// Pont <b>unique</b> entre le <c>DatePicker</c> d'Avalonia — qui ne sait lier qu'un
/// <see cref="DateTimeOffset"/> — et les <b>dates civiles</b> du Domain, qui sont des
/// <see cref="DateOnly"/> depuis P4-5D (ADR-PROD-DB-004 §5, décision 7 ; obligation T6).
///
/// <para>
/// <b>Le bogue que ce pont supprime.</b> Avant P4-5D, les deux formulaires concernés
/// (<c>CustomerFormViewModel.BirthDate</c>, <c>PrescriptionFormViewModel.IssueDate</c>) écrivaient
/// <c>picker.UtcDateTime</c> vers la couche Application. Le <c>DatePicker</c> restitue la date choisie à
/// minuit avec le décalage <b>local</b> : en heure d'été française, <c>15/03/1985 00:00 +02:00</c> devient
/// <c>14/03/1985 22:00</c> en UTC. <b>Le jour saisi n'était pas le jour enregistré</b> — sur une date de
/// naissance et une date d'ordonnance (ADR-PROD-DB-004 §2.4).
/// </para>
///
/// <para>
/// <b>La règle appliquée ici.</b> Seule la <i>composante calendaire</i> du <see cref="DateTimeOffset"/> est
/// lue ou écrite ; l'heure et le décalage sont ignorés, car ils n'ont aucun sens pour une date civile.
/// Aucune conversion de fuseau n'a lieu, donc le jour ne peut plus changer.
/// </para>
/// </summary>
public static class DatePickerCivilDate
{
    /// <summary>
    /// Valeur à donner au <c>DatePicker</c> pour une date civile du Domain.
    /// </summary>
    /// <remarks>
    /// Le <see cref="DateTimeOffset"/> produit porte le décalage local afin que le contrôle affiche
    /// exactement le jour demandé. La composante horaire est minuit : elle n'est jamais relue
    /// (cf. <see cref="ToCivilDate(DateTimeOffset)"/>).
    /// </remarks>
    public static DateTimeOffset ToPickerValue(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue));

    /// <inheritdoc cref="ToPickerValue(DateOnly)"/>
    public static DateTimeOffset? ToPickerValue(DateOnly? date) =>
        date.HasValue ? ToPickerValue(date.Value) : null;

    /// <summary>
    /// Date civile choisie dans le <c>DatePicker</c>, <b>sans aucune conversion de fuseau</b>.
    /// </summary>
    public static DateOnly ToCivilDate(DateTimeOffset pickerValue) =>
        DateOnly.FromDateTime(pickerValue.Date);

    /// <inheritdoc cref="ToCivilDate(DateTimeOffset)"/>
    public static DateOnly? ToCivilDate(DateTimeOffset? pickerValue) =>
        pickerValue.HasValue ? ToCivilDate(pickerValue.Value) : null;
}
