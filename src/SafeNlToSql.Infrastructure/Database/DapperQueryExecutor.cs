using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SafeNlToSql.Application;

namespace SafeNlToSql.Infrastructure;

/// <summary>
/// Runs SQL that SqlGuard approved, over the nl_search_reader connection. A timeout, a permission denial
/// (229/230) and a listed error from the query itself become a failed <see cref="QueryResult"/> with a reason;
/// other SqlExceptions, including connection and login failures, propagate.
/// </summary>
public sealed class DapperQueryExecutor : IQueryExecutor
{
    private const int ClientTimeoutErrorNumber = -2;
    private const int ObjectPermissionDeniedErrorNumber = 229;
    private const int ColumnPermissionDeniedErrorNumber = 230;

    // Errors caused by the query itself. Deliberately small (see docs/SPEC.md): any other number propagates.
    // Severity cannot be used instead: invalid object names and lock timeouts share severity 16 with these.
    private static readonly HashSet<int> QueryErrorNumbers =
    [
        241, // date or time conversion from a string failed
        242, // conversion gave an out-of-range value
        245, // conversion of a value failed
        8114, // error converting a data type
        8115, // arithmetic overflow
        8120, // column is neither aggregated nor in GROUP BY
        8134, // divide by zero
    ];

    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly ILogger<DapperQueryExecutor> _logger;

    public DapperQueryExecutor(
        string connectionString, IOptions<DatabaseOptions> options, ILogger<DapperQueryExecutor> logger)
    {
        _connectionString = connectionString;
        _commandTimeoutSeconds = options.Value.CommandTimeoutSeconds;
        _logger = logger;
    }

    public async Task<QueryResult> ExecuteAsync(ApprovedSql sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        try
        {
            // SqlGuard parses with QUOTED_IDENTIFIER ON, so "ClientName" is an identifier for both the guard
            // and the server. Set it explicitly on this session, as its own command, rather than rely on
            // defaults or touch the approved text. Inside the try so cancellation and timeout handling cover it.
            await connection.ExecuteAsync(new CommandDefinition(
                "SET QUOTED_IDENTIFIER ON;", commandTimeout: _commandTimeoutSeconds, cancellationToken: ct));

            // Its own try: only an error from running the approved SQL or reading its rows can be QueryFailed.
            try
            {
                await using var reader = await connection.ExecuteReaderAsync(
                    new CommandDefinition(sql.Sql, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct));

                var names = UniqueColumnNames(reader);
                var rows = new List<IReadOnlyDictionary<string, object?>>();
                while (await reader.ReadAsync(ct))
                {
                    var row = new Dictionary<string, object?>(names.Length);
                    for (var i = 0; i < names.Length; i++)
                    {
                        row[names[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    rows.Add(row);
                }

                return QueryResult.Success(rows);
            }
            catch (SqlException ex) when (QueryErrorNumbers.Contains(ex.Number))
            {
                // The database message can contain values from the query or the data, so it is only logged.
                // Never retry or repair the query.
                _logger.LogError(ex, "The database could not run an approved query");
                return QueryResult.Failed(QueryExecutionReasons.QueryFailed);
            }
        }
        catch (SqlException ex) when (ex.Number == ClientTimeoutErrorNumber)
        {
            return QueryResult.Failed(QueryExecutionReasons.QueryTimeout);
        }
        catch (SqlException) when (ct.IsCancellationRequested)
        {
            // SqlClient reports a cancelled command as a SqlException ("Operation cancelled by user"),
            // not as OperationCanceledException; callers expect cancellation.
            throw new OperationCanceledException(ct);
        }
        catch (SqlException ex) when (ex.Number is ObjectPermissionDeniedErrorNumber or ColumnPermissionDeniedErrorNumber)
        {
            // The guard approved the query but the read-only login refused it, for example a bare COUNT(*)
            // that references no allowed column. Fail closed with a reason; never repair the query.
            return QueryResult.Failed(QueryExecutionReasons.QueryNotPermitted);
        }
    }

    /// <summary>
    /// One unique key per column, in column order, so no value is lost or overwritten. The guard allows
    /// unnamed columns (COUNT(Metric)) and repeated names (EquipmentName, EquipmentName).
    /// An unnamed column becomes Column{position} (counting from 1); a repeated name becomes Name_2, Name_3...
    /// A generated name never takes a name that a real column in the same result has.
    /// </summary>
    private static string[] UniqueColumnNames(DbDataReader reader)
    {
        var raw = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var reserved = new HashSet<string>(raw.Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new string[raw.Length];

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i].Length > 0 && used.Add(raw[i]))
            {
                names[i] = raw[i];
                continue;
            }

            var baseName = raw[i].Length > 0 ? raw[i] : $"Column{i + 1}";
            var candidate = baseName;
            for (var n = 2; reserved.Contains(candidate) || !used.Add(candidate); n++)
            {
                candidate = $"{baseName}_{n}";
            }

            names[i] = candidate;
        }

        return names;
    }
}
