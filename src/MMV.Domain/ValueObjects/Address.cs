namespace MMV.Domain.ValueObjects;

/// <summary>
/// Adresse postale structurée.
/// </summary>
public record Address
{
    public string Line1 { get; init; }
    public string? Line2 { get; init; }
    public string City { get; init; }
    public string PostalCode { get; init; }
    public string Country { get; init; }

    public Address(string line1, string city, string postalCode, string country, string? line2 = null)
    {
        if (string.IsNullOrWhiteSpace(line1)) throw new ArgumentException("Ligne d'adresse requise.", nameof(line1));
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("Ville requise.", nameof(city));
        if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("Code postal requis.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(country)) throw new ArgumentException("Pays requis.", nameof(country));

        Line1 = line1.Trim();
        Line2 = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim();
        City = city.Trim();
        PostalCode = postalCode.Trim();
        Country = country.Trim();
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Line2)
            ? $"{Line1}, {PostalCode} {City}, {Country}"
            : $"{Line1}, {Line2}, {PostalCode} {City}, {Country}";
    }
}
