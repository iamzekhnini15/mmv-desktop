using System.Data;
using System.Data.Common;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// P4-4A0 — SONDE DE FORME STRUCTURELLE des exceptions provider.
///
/// <para>
/// Motif : quatre cas PostgreSQL (timeout client, timeout de verrou, connexion refusée, perte de
/// connexion) ont été mesurés en P4-1 SANS <c>SqlState</c> et avec <c>connection.State = Open</c>.
/// <see cref="ErrorFacts"/> ne regarde que la PREMIÈRE exception provider de la chaîne et ne relève
/// ni <c>InnerException</c>, ni <see cref="SocketException"/>, ni <see cref="TimeoutException"/>,
/// ni l'annulation. Cette sonde relève la forme COMPLÈTE, afin de décider s'il existe un signal
/// TYPÉ permettant de classer ces cas.
/// </para>
///
/// <para>
/// RÈGLE ABSOLUE — aucune décision par le TEXTE. Ce fichier ne lit jamais
/// <see cref="Exception.Message"/>, ni aucune sous-chaîne, ni aucune expression régulière sur un
/// message : seules des propriétés STRUCTURÉES et des tests de TYPE sont utilisés. Un message est
/// localisé et non contractuel ; il ne peut donc fonder aucune classification.
/// </para>
///
/// <para>
/// Sonde de MESURE uniquement : ce type appartient au harness de spike, hors <c>MMV.sln</c>. Aucun
/// code de production n'est modifié, et <c>PersistenceErrorMapper</c> n'est pas touché.
/// </para>
/// </summary>
public sealed class ExceptionShape
{
    /// <summary>Garde-fou : une chaîne d'exceptions ne doit jamais faire boucler la sonde.</summary>
    private const int MaxDepth = 16;

    private ExceptionShape(IReadOnlyList<ExceptionFrame> frames, ConnectionState? before, ConnectionState? after)
    {
        Frames = frames;
        ConnectionStateBefore = before;
        ConnectionStateAfter = after;
    }

    /// <summary>Chaîne complète, exception externe en position 0.</summary>
    public IReadOnlyList<ExceptionFrame> Frames { get; }

    /// <summary>État de la connexion OBSERVÉ avant l'échec. Donnée d'observation, jamais un critère.</summary>
    public ConnectionState? ConnectionStateBefore { get; }

    /// <summary>État de la connexion OBSERVÉ après l'échec. Donnée d'observation, jamais un critère.</summary>
    public ConnectionState? ConnectionStateAfter { get; }

    public string OuterType => Frames.Count == 0 ? "(aucune)" : Frames[0].TypeName;

    /// <summary>Premier <c>SqlState</c> non vide rencontré dans la chaîne, quel que soit le niveau.</summary>
    public string? SqlState => Frames
        .Select(frame => frame.SqlState)
        .FirstOrDefault(state => !string.IsNullOrEmpty(state));

    public string? ConstraintName => Frames
        .Select(frame => frame.ConstraintName)
        .FirstOrDefault(name => !string.IsNullOrEmpty(name));

    public bool HasPostgresException => HasType<PostgresException>();

    public bool HasNpgsqlException => HasType<NpgsqlException>();

    public bool HasSqlServerException => HasType<SqlException>();

    public bool HasSocketException => HasType<SocketException>();

    /// <summary>
    /// <c>TaskCanceledException</c> DÉRIVE de <c>OperationCanceledException</c> : cette propriété est
    /// donc vraie dans les deux cas. C'est voulu — la règle candidate « ne jamais convertir une
    /// annulation en erreur de persistance » porte sur la famille entière.
    /// </summary>
    public bool HasOperationCanceledException => HasType<OperationCanceledException>();

    public bool HasTaskCanceledException => HasType<TaskCanceledException>();

    public bool HasTimeoutException => HasType<TimeoutException>();

    public SocketError? SocketErrorCode => Frames
        .Select(frame => frame.SocketErrorCode)
        .FirstOrDefault(code => code is not null);

    /// <summary>
    /// <c>DbException.IsTransient</c> tel que renseigné par le provider. Observation : c'est le seul
    /// indicateur typé que Npgsql expose de lui-même sur les échecs sans <c>SqlState</c>.
    /// </summary>
    public bool AnyTransient => Frames.Any(frame => frame.IsTransient == true);

    /// <summary>
    /// Vrai si au moins une trame a été annulée via un <c>CancellationToken</c> effectivement
    /// déclenché — distingue une annulation DEMANDÉE d'une annulation subie.
    /// </summary>
    public bool CancellationWasRequested => Frames.Any(frame => frame.CancellationRequested == true);

    /// <summary>
    /// Relève la forme structurelle complète de <paramref name="exception"/>.
    /// </summary>
    /// <param name="exception">Exception réellement levée par le provider.</param>
    /// <param name="before">État de connexion observé avant l'échec, si connu.</param>
    /// <param name="after">État de connexion observé après l'échec, si connu.</param>
    public static ExceptionShape Capture(
        Exception exception,
        ConnectionState? before = null,
        ConnectionState? after = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var frames = new List<ExceptionFrame>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        Walk(exception, 0, frames, seen);

        return new ExceptionShape(frames, before, after);
    }

    private bool HasType<T>() where T : Exception
        => Frames.Any(frame => typeof(T).IsAssignableFrom(frame.ClrType));

    private static void Walk(Exception? exception, int depth, List<ExceptionFrame> frames, HashSet<Exception> seen)
    {
        if (exception is null || depth > MaxDepth || !seen.Add(exception))
        {
            return;
        }

        frames.Add(Describe(exception, depth));

        // Une AggregateException porte PLUSIEURS causes : n'en suivre qu'une masquerait la preuve.
        // `InnerException` y renvoie déjà `InnerExceptions[0]`, que le garde `seen` empêche de compter deux fois.
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                Walk(inner, depth + 1, frames, seen);
            }

            return;
        }

        Walk(exception.InnerException, depth + 1, frames, seen);
    }

    private static ExceptionFrame Describe(Exception exception, int depth)
    {
        string? sqlState = null;
        string? constraintName = null;
        string? tableName = null;
        string? columnName = null;
        string? schemaName = null;
        int? errorCode = null;
        bool? isTransient = null;
        SocketError? socketErrorCode = null;
        int? sqlServerNumber = null;
        byte? sqlServerState = null;
        byte? sqlServerClass = null;
        bool? cancellationRequested = null;

        // Contrat COMMUN aux providers ADO.NET : renseigné même quand aucun type spécifique ne l'est.
        if (exception is DbException dbException)
        {
            sqlState = dbException.SqlState;
            errorCode = dbException.ErrorCode;
            isTransient = dbException.IsTransient;
        }

        switch (exception)
        {
            // PostgresException DÉRIVE de NpgsqlException : elle doit être testée EN PREMIER.
            case PostgresException postgres:
                sqlState = postgres.SqlState;
                constraintName = Blank(postgres.ConstraintName);
                tableName = Blank(postgres.TableName);
                columnName = Blank(postgres.ColumnName);
                schemaName = Blank(postgres.SchemaName);
                break;

            case SqlException sqlServer:
                sqlServerNumber = sqlServer.Number;
                sqlServerState = sqlServer.State;
                sqlServerClass = sqlServer.Class;
                break;

            case SocketException socket:
                socketErrorCode = socket.SocketErrorCode;
                errorCode = socket.ErrorCode;
                break;

            case OperationCanceledException canceled:
                cancellationRequested = canceled.CancellationToken.IsCancellationRequested;
                break;
        }

        return new ExceptionFrame
        {
            Depth = depth,
            ClrType = exception.GetType(),
            SqlState = Blank(sqlState),
            ConstraintName = constraintName,
            TableName = tableName,
            ColumnName = columnName,
            SchemaName = schemaName,
            ErrorCode = errorCode,
            IsTransient = isTransient,
            SocketErrorCode = socketErrorCode,
            SqlServerNumber = sqlServerNumber,
            SqlServerState = sqlServerState,
            SqlServerClass = sqlServerClass,
            CancellationRequested = cancellationRequested
        };
    }

    /// <summary>Chaîne des types, du plus externe au plus interne. Aucun message.</summary>
    public string TypeChain() => Frames.Count == 0
        ? "(aucune)"
        : string.Join(" -> ", Frames.Select(frame => frame.TypeName));

    /// <summary>
    /// Ligne de preuve destinée au journal du spike. Ne contient QUE des faits structurés :
    /// aucun message, donc aucun secret ni texte localisé.
    /// </summary>
    public string ToEvidenceLine()
    {
        var line = new StringBuilder();
        line.Append("chaine=").Append(TypeChain());
        line.Append(" | sqlstate=").Append(SqlState ?? "(aucun)");
        line.Append(" | contrainte=").Append(ConstraintName ?? "(aucune)");
        line.Append(" | socket=").Append(HasSocketException ? SocketErrorCode?.ToString() ?? "OUI (code absent)" : "NON");
        line.Append(" | TimeoutException=").Append(HasTimeoutException ? "OUI" : "NON");
        line.Append(" | OperationCanceled=").Append(HasOperationCanceledException ? "OUI" : "NON");
        line.Append(" | TaskCanceled=").Append(HasTaskCanceledException ? "OUI" : "NON");
        line.Append(" | annulation demandee=").Append(CancellationWasRequested ? "OUI" : "NON");
        line.Append(" | IsTransient=").Append(AnyTransient ? "OUI" : "NON");
        line.Append(" | conn.avant=").Append(ConnectionStateBefore?.ToString() ?? "(non observe)");
        line.Append(" | conn.apres=").Append(ConnectionStateAfter?.ToString() ?? "(non observe)");

        foreach (var frame in Frames.Where(frame => frame.HasProviderDetail))
        {
            line.Append(" | [").Append(frame.Depth).Append("] ").Append(frame.Detail());
        }

        return line.ToString();
    }

    /// <summary>
    /// Ligne de la matrice §8 du rapport P4-4A0. La colonne « distinguable » n'est PAS calculée ici :
    /// elle résulte d'une comparaison ENTRE cas, que seule l'analyse peut faire.
    /// </summary>
    public string ToMatrixRow(string caseLabel)
    {
        var inner1 = Frames.Count > 1 ? Frames[1].TypeName : "(aucune)";
        var inner2 = Frames.Count > 2 ? Frames[2].TypeName : "(aucune)";

        return $"| {caseLabel} | {OuterType} | {inner1} | {inner2} | {SqlState ?? "(aucun)"} | " +
               $"{(HasSocketException ? "OUI" : "NON")} | {SocketErrorCode?.ToString() ?? "(n/a)"} | " +
               $"{(HasTimeoutException ? "OUI" : "NON")} | {(HasOperationCanceledException ? "OUI" : "NON")} | " +
               $"avant={ConnectionStateBefore?.ToString() ?? "(n/a)"} apres={ConnectionStateAfter?.ToString() ?? "(n/a)"} | " +
               "(a comparer) |";
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Une exception de la chaîne, réduite à ses FAITS STRUCTURÉS. Le message n'est volontairement pas
/// conservé : il ne doit fonder aucune classification.
/// </summary>
public sealed record ExceptionFrame
{
    public required int Depth { get; init; }

    /// <summary>Type CLR réel : la détection se fait par type, jamais par comparaison de nom.</summary>
    public required Type ClrType { get; init; }

    public string TypeName => ClrType.FullName ?? ClrType.Name;

    public string? SqlState { get; init; }

    public string? ConstraintName { get; init; }

    public string? TableName { get; init; }

    public string? ColumnName { get; init; }

    public string? SchemaName { get; init; }

    /// <summary>Observation seule (P4-4A0 §3) : n'est proposé comme critère d'aucune catégorie.</summary>
    public int? ErrorCode { get; init; }

    public bool? IsTransient { get; init; }

    public SocketError? SocketErrorCode { get; init; }

    public int? SqlServerNumber { get; init; }

    public byte? SqlServerState { get; init; }

    public byte? SqlServerClass { get; init; }

    /// <summary>Renseigné uniquement sur une <c>OperationCanceledException</c>.</summary>
    public bool? CancellationRequested { get; init; }

    public bool HasProviderDetail =>
        SqlState is not null || ConstraintName is not null || TableName is not null ||
        ColumnName is not null || SchemaName is not null || SocketErrorCode is not null ||
        SqlServerNumber is not null || IsTransient is not null || ErrorCode is not null;

    public string Detail()
    {
        var parts = new List<string>();

        if (SqlState is not null) parts.Add($"SqlState={SqlState}");
        if (ConstraintName is not null) parts.Add($"contrainte={ConstraintName}");
        if (SchemaName is not null) parts.Add($"schema={SchemaName}");
        if (TableName is not null) parts.Add($"table={TableName}");
        if (ColumnName is not null) parts.Add($"colonne={ColumnName}");
        if (SocketErrorCode is not null) parts.Add($"SocketErrorCode={SocketErrorCode}");
        if (SqlServerNumber is not null) parts.Add($"Number={SqlServerNumber} State={SqlServerState} Class={SqlServerClass}");
        if (IsTransient is not null) parts.Add($"IsTransient={IsTransient}");
        if (ErrorCode is not null) parts.Add($"ErrorCode={ErrorCode}");
        if (CancellationRequested is not null) parts.Add($"annulation demandee={CancellationRequested}");

        return string.Join(" ", parts);
    }
}
