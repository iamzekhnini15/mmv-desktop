namespace MMV.Domain.ValueObjects;

/// <summary>
/// Numéro de téléphone basique (validation longueur/format minimal).
/// </summary>
public readonly record struct PhoneNumber
{
    public string Value { get; }

    public PhoneNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Le numéro de téléphone est requis.", nameof(value));
        }

        var normalized = value.Replace(" ", string.Empty)
                               .Replace("-", string.Empty)
                               .Replace(".", string.Empty);

        if (normalized.Length < 6 || normalized.Length > 20)
        {
            throw new ArgumentException("Le numéro de téléphone doit contenir entre 6 et 20 caractères.", nameof(value));
        }

        Value = normalized;
    }

    public override string ToString() => Value;
}
