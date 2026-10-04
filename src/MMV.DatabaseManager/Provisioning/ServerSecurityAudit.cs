using System.Data.Common;

namespace MMV.DatabaseManager.Provisioning;

/// <summary>Ligne de <c>pg_hba_file_rules</c>.</summary>
public sealed record HbaRule(int LineNumber, string Type, IReadOnlyList<string> Databases, IReadOnlyList<string> Users,
    string? AuthMethod, string? Error);

/// <summary>État de sécurité du serveur, lu par l'identité de provisioning.</summary>
public sealed record ServerSecuritySnapshot(bool IsSuperuser, string Ssl, string PasswordEncryption, IReadOnlyList<HbaRule> Rules);

/// <summary>
/// Audit de conformité du serveur <b>avant</b> tout provisioning (P4-8, D-14) : un serveur qui admettrait, pour la
/// base ou les rôles de MMV, une connexion non chiffrée, une authentification MD5, en clair ou <c>trust</c>, est
/// <b>refusé</b> — l'outil ne provisionne pas une installation non conforme.
///
/// <para>
/// <b>Conservateur par construction.</b> Toute règle réseau qui <i>pourrait</i> s'appliquer (base <c>all</c>,
/// <c>sameuser</c>, fichier <c>@…</c> ; utilisateur <c>all</c>, groupe <c>+…</c>, fichier <c>@…</c>) compte, même
/// masquée par une règle antérieure : l'ordre de <c>pg_hba.conf</c> n'est pas un contrôle de sécurité fiable à
/// relire. Les règles <c>local</c> (socket du serveur, accès système) sont hors du périmètre réseau de D-14.
/// </para>
/// </summary>
public static class ServerSecurityAudit
{
    private static readonly string[] InsecureMethods = ["trust", "password", "md5", "ident"];

    /// <summary>Constats bloquants ; vide ⇒ conforme.</summary>
    public static IReadOnlyList<string> Evaluate(ServerSecuritySnapshot snapshot, string database, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var findings = new List<string>();

        if (!snapshot.IsSuperuser)
        {
            findings.Add("l'identité de provisioning n'est pas superutilisateur (création de base, de rôles, privilèges par défaut, lecture de pg_hba)");
        }

        if (!string.Equals(snapshot.Ssl, "on", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("TLS désactivé sur le serveur (ssl = off)");
        }

        if (!string.Equals(snapshot.PasswordEncryption, "scram-sha-256", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add($"password_encryption = {snapshot.PasswordEncryption} (scram-sha-256 exigé)");
        }

        if (snapshot.Rules.Count == 0)
        {
            findings.Add("pg_hba illisible ou vide : la conformité ne peut pas être établie");
        }

        foreach (var rule in snapshot.Rules)
        {
            if (rule.Error is not null)
            {
                findings.Add($"pg_hba ligne {rule.LineNumber} invalide");
                continue;
            }

            if (rule.Type == "local" || !Matches(rule.Databases, [database]) || !Matches(rule.Users, roles))
            {
                continue;
            }

            var method = rule.AuthMethod ?? string.Empty;
            if (method == "reject")
            {
                continue;
            }

            if (rule.Type != "hostssl")
            {
                findings.Add($"pg_hba ligne {rule.LineNumber} : connexion « {rule.Type} » sans TLS obligatoire");
            }

            if (InsecureMethods.Contains(method))
            {
                findings.Add($"pg_hba ligne {rule.LineNumber} : authentification « {method} » (SCRAM-SHA-256 exigé)");
            }
        }

        return findings;
    }

    public static async Task<ServerSecuritySnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var isSuperuser = await ServerCommand.ScalarAsync(connection,
            "SELECT rolsuper FROM pg_catalog.pg_roles WHERE rolname = current_user", cancellationToken) is true;
        var ssl = (string)(await ServerCommand.ScalarAsync(connection, "SELECT current_setting('ssl')", cancellationToken))!;
        var encryption = (string)(await ServerCommand.ScalarAsync(connection,
            "SELECT current_setting('password_encryption')", cancellationToken))!;

        var rules = new List<HbaRule>();
        if (isSuperuser)
        {
            await using var command = ServerCommand.Create(connection,
                "SELECT line_number, type, database, user_name, auth_method, error FROM pg_catalog.pg_hba_file_rules ORDER BY line_number");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rules.Add(new HbaRule(
                    reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? [] : (string[])reader.GetValue(2),
                    reader.IsDBNull(3) ? [] : (string[])reader.GetValue(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        return new ServerSecuritySnapshot(isSuperuser, ssl, encryption, rules);
    }

    /// <summary>
    /// <c>replication</c> n'est pas compté : il ne vise que les connexions de réplication, que les rôles MMV
    /// (<c>NOREPLICATION</c>) ne peuvent pas ouvrir. <c>/…</c> est une expression régulière (PostgreSQL 16+).
    /// </summary>
    private static bool Matches(IReadOnlyList<string> entries, IReadOnlyCollection<string> names) =>
        entries.Any(e => e is "all" or "sameuser" or "samerole" or "samegroup"
                         || e.StartsWith('+') || e.StartsWith('@') || e.StartsWith('/') || names.Contains(e));
}
