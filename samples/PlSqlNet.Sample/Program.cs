using PlSqlNet;

var connectionString = Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("ORACLE_CONNECTION_STRING environment variable is required.");

var plSql = """
BEGIN
  OPEN :OUT_CURSOR FOR
    SELECT
      USER_ID,
      USER_NAME,
      CREATED_AT
    FROM USERS
    WHERE (:USER_ID IS NULL OR USER_ID = :USER_ID);

  :OUT_MESSAGE := 'OK';
END;
""";

var bindValues = new Dictionary<string, object?>
{
    ["USER_ID"] = 1001
};

var result = await OraclePlSqlExecutor.ExecuteSelectAsync(
    connectionString,
    plSql,
    bindValues);

Console.WriteLine($"Message: {result.Message}");

foreach (var row in result.Rows)
{
    Console.WriteLine(string.Join(", ", row.Select(column => $"{column.Key}={column.Value}")));
}
