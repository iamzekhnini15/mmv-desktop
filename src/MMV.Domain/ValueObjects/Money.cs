namespace MMV.Domain.ValueObjects;

/// <summary>
/// Représente un montant monétaire avec devise.
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "EUR")
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Le montant ne peut pas être négatif.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("La devise est requise.", nameof(currency));
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";

    public static Money Zero(string currency = "EUR") => new(0m, currency);
}
