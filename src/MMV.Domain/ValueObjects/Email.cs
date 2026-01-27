using System.Net.Mail;

namespace MMV.Domain.ValueObjects;

/// <summary>
/// Adresse email validée.
/// </summary>
public readonly record struct Email
{
    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("L'email est requis.", nameof(value));
        }

        try
        {
            var mailAddress = new MailAddress(value);
            Value = mailAddress.Address;
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Email invalide.", nameof(value), ex);
        }
    }

    public override string ToString() => Value;
}
