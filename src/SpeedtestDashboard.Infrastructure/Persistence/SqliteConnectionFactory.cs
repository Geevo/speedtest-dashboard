using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory(IOptions<StorageOptions> options)
{
    private readonly string _connectionString = PersistenceServiceCollectionExtensions.BuildConnectionString(options.Value);
    private readonly int _commandTimeout = options.Value.CommandTimeoutSeconds;

    public SqliteConnection CreateConnection() => new(_connectionString);

    public SqliteCommand CreateCommand(SqliteConnection connection, string commandText, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = _commandTimeout;
        command.Transaction = transaction;
        return command;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
