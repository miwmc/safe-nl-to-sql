namespace SafeNlToSql.Application;

public static class SqlGuardReasons
{
    public const string Empty = "SQL is empty";
    public const string NotParseable = "SQL could not be parsed";
    public const string NoStatement = "SQL contains no statement";
    public const string MultipleStatements = "SQL contains more than one statement";
    public const string NotSelect = "Only a SELECT statement is allowed";
}
