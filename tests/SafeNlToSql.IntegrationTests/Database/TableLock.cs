using Microsoft.Data.SqlClient;

namespace SafeNlToSql.IntegrationTests.Database;

/// <summary>Holds an exclusive lock on dbo.Certificates until disposed (the transaction is rolled back).</summary>
internal sealed class TableLock : IAsyncDisposable
{
    private readonly SqlConnection _connection;

    private TableLock(SqlConnection connection) => _connection = connection;

    public static async Task<TableLock> AcquireAsync(string saConnectionString)
    {
        var connection = new SqlConnection(saConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "BEGIN TRAN; SELECT TOP 1 Id FROM dbo.Certificates WITH (TABLOCKX, HOLDLOCK);", connection);
        await command.ExecuteNonQueryAsync();
        return new TableLock(connection);
    }

    public async ValueTask DisposeAsync()
    {
        await using var command = new SqlCommand("IF @@TRANCOUNT > 0 ROLLBACK;", _connection);
        await command.ExecuteNonQueryAsync();
        await _connection.DisposeAsync();
    }
}
