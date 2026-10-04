using System.Text;

namespace MMV.DatabaseManager.CommandLine;

/// <summary>
/// Lecture des secrets sur l'entrée standard (P4-8) : <b>jamais</b> en argument (historique du shell, liste des
/// processus), <b>jamais</b> en variable d'environnement de production, <b>jamais</b> réécrits. En console
/// interactive, la saisie est masquée ; en entrée redirigée (procédure scriptée), une ligne par secret.
/// </summary>
public sealed class SecretInput
{
    private readonly TextReader _input;
    private readonly TextWriter _prompt;
    private readonly bool _interactive;

    public SecretInput(TextReader input, TextWriter prompt, bool interactive)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _interactive = interactive;
    }

    /// <summary>Secret saisi, ou <c>null</c> si l'entrée est épuisée ou vide.</summary>
    public string? Read(string label)
    {
        _prompt.Write($"{label} : ");
        var value = _interactive ? ReadMasked() : _input.ReadLine();
        _prompt.WriteLine();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string ReadMasked()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
