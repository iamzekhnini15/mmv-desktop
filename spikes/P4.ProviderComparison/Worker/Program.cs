using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

// Worker E9 — un processus, une connexion, une opération.
//
// Usage : MMV.P4.Worker <postgres|sqlserver> <operation> <gateName> <argument>
//   operation ∈ { decrement-stock, low-stock-alert, next-number }
//
// La chaîne de connexion provient de MMV_P4_WORKER_CONNECTION (jamais d'un argument, pour ne
// pas l'exposer dans la ligne de commande visible par les autres processus).
//
// Sortie stdout, une ligne : "PID=<pid> RESULT=<résultat> ROWS=<n>".

if (args.Length < 4)
{
    Console.WriteLine("PID=0 RESULT=BAD_ARGS ROWS=0");
    return 2;
}

var provider = args[0];
var operation = args[1];
var gateName = args[2];
var argument = args[3];

var connectionString = Environment.GetEnvironmentVariable("MMV_P4_WORKER_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("PID=0 RESULT=NO_CONNECTION ROWS=0");
    return 3;
}

var pid = Environment.ProcessId;

try
{
    // 1) Connexion ouverte AVANT le rendez-vous : le coût de connexion ne doit pas fausser la course.
    await using DbConnection connection = provider == "postgres"
        ? new NpgsqlConnection(connectionString)
        : new SqlConnection(connectionString);

    await connection.OpenAsync();

    // 2) Rendez-vous par ÉVÉNEMENT NOMMÉ du système (signal externe réel, pas une temporisation).
    using (var gate = EventWaitHandle.OpenExisting(gateName))
    {
        Console.Error.WriteLine("READY");
        gate.WaitOne(TimeSpan.FromSeconds(60));
    }

    await using var command = connection.CreateCommand();

    switch (operation)
    {
        case "decrement-stock":
            // CAS : ne décrémente que si le stock est suffisant. rows==1 ⇒ ce poste a gagné.
            command.CommandText = provider == "postgres"
                ? "UPDATE \"Products\" SET \"StockQuantity\" = \"StockQuantity\" - 1 WHERE \"ProductId\" = @id AND \"StockQuantity\" >= 1;"
                : "UPDATE [Products] SET [StockQuantity] = [StockQuantity] - 1 WHERE [ProductId] = @id AND [StockQuantity] >= 1;";
            AddParameter(command, "@id", long.Parse(argument));
            break;

        case "low-stock-alert":
            command.CommandText = provider == "postgres"
                ? "INSERT INTO \"Notifications\" (\"Type\",\"Title\",\"Message\",\"EntityId\",\"EntityType\",\"IsRead\",\"CreatedAt\",\"ResolvedAt\") " +
                  "VALUES ('LowStock','Stock bas','Alerte multi-processus',@id,'Product',false,TIMESTAMP '2026-07-24 12:00:00',NULL) ON CONFLICT DO NOTHING;"
                : "INSERT INTO [Notifications] ([Type],[Title],[Message],[EntityId],[EntityType],[IsRead],[CreatedAt],[ResolvedAt]) " +
                  "SELECT 'LowStock','Stock bas','Alerte multi-processus',@id,'Product',0,'2026-07-24T12:00:00',NULL " +
                  "WHERE NOT EXISTS (SELECT 1 FROM [Notifications] WITH (UPDLOCK, HOLDLOCK) " +
                  "WHERE [Type]='LowStock' AND [EntityType]='Product' AND [EntityId]=@id AND [ResolvedAt] IS NULL);";
            AddParameter(command, "@id", long.Parse(argument));
            break;

        case "next-number":
            // Incrément conditionnel : reproduit le CAS de EfNumberSequenceService.
            command.CommandText = provider == "postgres"
                ? "UPDATE \"DocumentSequences\" SET \"CurrentValue\" = \"CurrentValue\" + 1, \"UpdatedAt\" = TIMESTAMP '2026-07-24 12:00:00' WHERE \"SequenceName\" = @name;"
                : "UPDATE [DocumentSequences] SET [CurrentValue] = [CurrentValue] + 1, [UpdatedAt] = '2026-07-24T12:00:00' WHERE [SequenceName] = @name;";
            AddParameter(command, "@name", argument);
            break;

        default:
            Console.WriteLine($"PID={pid} RESULT=UNKNOWN_OP ROWS=0");
            return 4;
    }

    var rows = await command.ExecuteNonQueryAsync();
    Console.WriteLine($"PID={pid} RESULT={(rows == 1 ? "WON" : "LOST")} ROWS={rows}");
    return 0;
}
catch (Exception ex)
{
    var code = ex switch
    {
        PostgresException pg => pg.SqlState,
        SqlException sql => sql.Number.ToString(),
        _ => "n/a"
    };
    Console.WriteLine($"PID={pid} RESULT=ERROR[{ex.GetType().Name}/{code}] ROWS=0");
    return 1;
}

static void AddParameter(DbCommand command, string name, object value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.Value = value;
    command.Parameters.Add(parameter);
}
