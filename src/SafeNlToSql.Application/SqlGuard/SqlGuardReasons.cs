namespace SafeNlToSql.Application;

public static class SqlGuardReasons
{
    public const string Empty = "SQL is empty";
    public const string NotParseable = "SQL could not be parsed";
    public const string TooLong = "SQL is longer than 2000 characters";
    public const string NoStatement = "SQL contains no statement";
    public const string MultipleStatements = "SQL contains more than one statement";
    public const string NotSelect = "Only a SELECT statement is allowed";
    public const string TableNotAllowed = "Only dbo.Certificates may be queried";
    public const string ColumnNotAllowed = "Only the columns CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate and IsCompliant may be used";
    public const string SelectStarNotAllowed = "SELECT * is not allowed; list the allowed columns";
    public const string DateFilterNotAllowed = "Date functions and date comparisons may only use CalibrationDate";
    public const string FunctionNotAllowed = "Only COUNT, MIN, MAX, GETDATE, SYSDATETIME, DATEADD, DATEDIFF, YEAR, MONTH, DAY, LOWER, UPPER, CAST, CONVERT, TRY_CAST, TRY_CONVERT and CURRENT_TIMESTAMP may be used";
    public const string ExpressionNotAllowed = "This SQL construct is not allowed";
    public const string SelectIntoNotAllowed = "SELECT INTO is not allowed";
    public const string ForClauseNotAllowed = "FOR XML, FOR JSON and FOR BROWSE are not allowed";
    public const string QueryHintNotAllowed = "Query and table hints are not allowed";
}
