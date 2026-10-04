using System.Collections;
using System.Data;
using System.Data.Common;

namespace MMV.Domain.Tests.TestDoubles;

/// <summary>
/// Connexion ADO.NET scriptée, sans serveur (P4-6B, U-B6). Chaque commande exécutée est <b>enregistrée</b>
/// (texte + paramètres) puis résolue par <see cref="Respond"/> : un <see cref="DataTable"/> pour un lecteur,
/// une valeur pour un scalaire, ou une exception à lever (serveur injoignable, objet absent…).
/// </summary>
public sealed class ScriptedDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public ScriptedDbConnection(Func<ExecutedCommand, object?> respond) => Respond = respond;

    public Func<ExecutedCommand, object?> Respond { get; }

    public List<ExecutedCommand> Executed { get; } = new();

    public int OpenCount { get; private set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "scripted";
    public override string DataSource => "scripted";
    public override string ServerVersion => "17.10";
    public override ConnectionState State => _state;

    public override void Open()
    {
        OpenCount++;
        _state = ConnectionState.Open;
    }

    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException("Aucune transaction n'est attendue d'un composant en lecture seule.");
    protected override DbCommand CreateDbCommand() => new ScriptedDbCommand(this);

    internal object? Execute(ScriptedDbCommand command)
    {
        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("Commande exécutée sur une connexion fermée.");
        }

        var executed = new ExecutedCommand(
            command.CommandText,
            command.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => p.Value));
        Executed.Add(executed);

        var response = Respond(executed);
        if (response is Exception exception)
        {
            throw exception;
        }

        return response;
    }
}

public sealed record ExecutedCommand(string Text, IReadOnlyDictionary<string, object?> Parameters);

internal sealed class ScriptedDbCommand : DbCommand
{
    private readonly ScriptedDbConnection _connection;
    private readonly ScriptedParameterCollection _parameters = new();

    public ScriptedDbCommand(ScriptedDbConnection connection) => _connection = connection;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get => _connection; set { } }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new ScriptedParameter();

    public override int ExecuteNonQuery() => _connection.Execute(this) is int rows ? rows : 0;

    public override object? ExecuteScalar()
    {
        var response = _connection.Execute(this);
        if (response is DataTable table)
        {
            return table.Rows.Count == 0 ? null : table.Rows[0][0];
        }

        return response;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        _connection.Execute(this) switch
        {
            DataTable table => table.CreateDataReader(),
            _ => new DataTable().CreateDataReader()
        };
}

internal sealed class ScriptedParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    public override int Size { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}

internal sealed class ScriptedParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();

    public override int Count => _items.Count;
    public override object SyncRoot => _items;

    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
        {
            Add(value!);
        }
    }

    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
}
