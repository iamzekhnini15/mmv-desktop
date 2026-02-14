using FluentValidation;
using MMV.Domain.Entities;

namespace MMV.Domain.Validators;

/// <summary>
/// Validateur FluentValidation pour l'entité User.
/// Applique les règles de validation pour la création et la modification des utilisateurs.
/// </summary>
public class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        RuleFor(u => u.Username)
            .NotEmpty().WithMessage("Le nom d'utilisateur est requis.")
            .MinimumLength(3).WithMessage("Le nom d'utilisateur doit contenir au moins 3 caractères.")
            .MaximumLength(50).WithMessage("Le nom d'utilisateur ne peut pas dépasser 50 caractères.")
            .Matches(@"^\S+$").WithMessage("Le nom d'utilisateur ne doit pas contenir d'espaces.")
            .Matches(@"^[a-zA-Z0-9._-]+$").WithMessage("Le nom d'utilisateur ne peut contenir que des lettres, chiffres, points, tirets et underscores.");

        RuleFor(u => u.FirstName)
            .NotEmpty().WithMessage("Le prénom est requis.")
            .MaximumLength(100).WithMessage("Le prénom ne peut pas dépasser 100 caractères.");

        RuleFor(u => u.LastName)
            .NotEmpty().WithMessage("Le nom est requis.")
            .MaximumLength(100).WithMessage("Le nom ne peut pas dépasser 100 caractères.");

        RuleFor(u => u.Role)
            .IsInEnum().WithMessage("Le rôle sélectionné est invalide.");
    }

    /// <summary>
    /// Valide la politique de mot de passe forte.
    /// Minimum 8 caractères, 1 majuscule, 1 minuscule, 1 chiffre.
    /// </summary>
    public static (bool IsValid, string ErrorMessage) ValidatePasswordPolicy(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return (false, "Le mot de passe est requis.");

        if (password.Length < 8)
            return (false, "Le mot de passe doit contenir au moins 8 caractères.");

        if (!password.Any(char.IsUpper))
            return (false, "Le mot de passe doit contenir au moins une majuscule.");

        if (!password.Any(char.IsLower))
            return (false, "Le mot de passe doit contenir au moins une minuscule.");

        if (!password.Any(char.IsDigit))
            return (false, "Le mot de passe doit contenir au moins un chiffre.");

        return (true, string.Empty);
    }

    /// <summary>
    /// Calcule le score de force du mot de passe (0-4).
    /// 0 = très faible, 1 = faible, 2 = moyen, 3 = fort, 4 = très fort.
    /// </summary>
    public static int GetPasswordStrength(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return 0;

        int score = 0;

        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score++;

        return Math.Min(score, 4);
    }

    /// <summary>
    /// Génère un mot de passe fort aléatoire.
    /// </summary>
    public static string GenerateStrongPassword(int length = 16)
    {
        if (length < 8) length = 8;

        const string upperChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lowerChars = "abcdefghjkmnpqrstuvwxyz";
        const string digitChars = "23456789";
        const string specialChars = "!@#$%&*?";

        var random = new Random();
        var password = new char[length];

        // Garantir au moins un de chaque type
        password[0] = upperChars[random.Next(upperChars.Length)];
        password[1] = lowerChars[random.Next(lowerChars.Length)];
        password[2] = digitChars[random.Next(digitChars.Length)];
        password[3] = specialChars[random.Next(specialChars.Length)];

        // Remplir le reste
        var allChars = upperChars + lowerChars + digitChars + specialChars;
        for (int i = 4; i < length; i++)
        {
            password[i] = allChars[random.Next(allChars.Length)];
        }

        // Mélanger les caractères
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
