# plsql-net

.NET 10, Dapper, Oracle.ManagedDataAccess.Core 기반으로 PL/SQL 텍스트를 실행하는 예제입니다.

기존 시스템의 바인딩 값은 `Dictionary<string, object?>`를 그대로 사용하고, output parameter는 `SYS_REFCURSOR`와 `VARCHAR2` 기준으로 처리합니다.

## Packages

```bash
dotnet add package Dapper
dotnet add package Oracle.ManagedDataAccess.Core
```

## PL/SQL 규칙

기본 output parameter 이름은 아래와 같습니다.

- `:OUT_CURSOR` - `SYS_REFCURSOR`
- `:OUT_MESSAGE` - `VARCHAR2`

예시:

```sql
BEGIN
  OPEN :OUT_CURSOR FOR
    SELECT USER_ID, USER_NAME, CREATED_AT
    FROM USERS
    WHERE (:USER_ID IS NULL OR USER_ID = :USER_ID);

  :OUT_MESSAGE := 'OK';
END;
```

## Usage

```csharp
using PlSqlNet;

var plSql = """
BEGIN
  OPEN :OUT_CURSOR FOR
    SELECT USER_ID, USER_NAME, CREATED_AT
    FROM USERS
    WHERE USER_ID = :USER_ID;

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

Console.WriteLine(result.Message);

foreach (var row in result.Rows)
{
    Console.WriteLine(row["USER_NAME"]);
}
```

## Custom output names

기존 시스템에서 output parameter 이름이 다르면 옵션으로 바꿀 수 있습니다.

```csharp
var options = new OraclePlSqlOptions
{
    CursorParameterName = "P_CURSOR",
    MessageParameterName = "P_MESSAGE",
    MessageSize = 1000
};

var result = await OraclePlSqlExecutor.ExecuteSelectAsync(
    connectionString,
    plSql,
    bindValues,
    options);
```

## Multiple cursors

`SELECT` 결과를 2개 이상 받아야 하면 PL/SQL에서 cursor를 여러 개 열고 `ExecuteSelectManyAsync`를 사용하면 됩니다.

```sql
BEGIN
  OPEN :OUT_USERS FOR
    SELECT USER_ID, USER_NAME
    FROM USERS
    WHERE USER_ID = :USER_ID;

  OPEN :OUT_ORDERS FOR
    SELECT ORDER_ID, USER_ID, ORDER_DATE
    FROM ORDERS
    WHERE USER_ID = :USER_ID;

  :OUT_MESSAGE := 'OK';
END;
```

```csharp
var result = await OraclePlSqlExecutor.ExecuteSelectManyAsync(
    connectionString,
    plSql,
    bindValues,
    new OraclePlSqlOptions
    {
        CursorParameterNames = ["OUT_USERS", "OUT_ORDERS"]
    });

var users = result.GetCursor("OUT_USERS");
var orders = result.GetCursor("OUT_ORDERS");

Console.WriteLine(result.Message);
```

## Notes

- Oracle 바인딩은 `BindByName = true`로 동작합니다.
- 입력 파라미터 이름은 `USER_ID`, `:USER_ID` 둘 다 허용합니다.
- `null` 값은 `DBNull.Value`로 변환됩니다.
- `bool`은 Oracle에 직접 boolean bind가 어려운 경우가 많아 `Int16`으로 처리합니다.
