namespace SafeNlToSql.Application;

public static class QueryExecutionReasons
{
    public const string QueryTimeout = "The query took too long and was cancelled";
    public const string QueryNotPermitted = "The database refused this query; ask about the certificate columns directly";
}
