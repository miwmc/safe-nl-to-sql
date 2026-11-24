using Microsoft.Data.SqlClient;

namespace SafeNlToSql.IntegrationTests.Database;

internal static class SqlHelper
{
    public static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Runs a query and returns the first column of every row as strings.</summary>
    public static async Task<List<string>> FirstColumnAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return values;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Asserts that SQL Server refuses the statement with exactly this error number.</summary>
    public static async Task AssertDeniedAsync(string connectionString, string sql, int expectedNumber)
    {
        var exception = await Assert.ThrowsAnyAsync<SqlException>(() => ExecuteAsync(connectionString, sql));
        Assert.Equal(expectedNumber, exception.Number);
    }
}
