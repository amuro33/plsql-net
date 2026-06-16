using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace PlSqlNet;

public sealed record OraclePlSqlOptions
{
    public string CursorParameterName { get; init; } = "OUT_CURSOR";
    public IReadOnlyList<string>? CursorParameterNames { get; init; }
    public string MessageParameterName { get; init; } = "OUT_MESSAGE";
    public int MessageSize { get; init; } = 4000;

    internal IReadOnlyList<string> GetCursorParameterNames()
    {
        return CursorParameterNames is { Count: > 0 }
            ? CursorParameterNames
            : [CursorParameterName];
    }
}

public sealed record OraclePlSqlResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string? Message);

public sealed record OraclePlSqlMultiResult(
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Cursors,
    string? Message)
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetCursor(string name)
    {
        return Cursors.TryGetValue(name.TrimStart(':'), out var rows)
            ? rows
            : [];
    }
}

public static class OraclePlSqlExecutor
{
    public static async Task<OraclePlSqlResult> ExecuteSelectAsync(
        string connectionString,
        string plSqlText,
        IDictionary<string, object?> bindValues,
        OraclePlSqlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new OraclePlSqlOptions();
        var result = await ExecuteSelectManyAsync(
            connectionString,
            plSqlText,
            bindValues,
            options,
            cancellationToken);

        var cursorName = options.GetCursorParameterNames()[0];

        return new OraclePlSqlResult(
            result.GetCursor(cursorName),
            result.Message);
    }

    public static async Task<OraclePlSqlMultiResult> ExecuteSelectManyAsync(
        string connectionString,
        string plSqlText,
        IDictionary<string, object?> bindValues,
        OraclePlSqlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(plSqlText);
        ArgumentNullException.ThrowIfNull(bindValues);

        options ??= new OraclePlSqlOptions();
        var cursorNames = options.GetCursorParameterNames();

        var parameters = new OracleDynamicParameters();

        foreach (var (name, value) in bindValues)
        {
            parameters.AddInput(name, value);
        }

        foreach (var cursorName in cursorNames)
        {
            parameters.AddRefCursor(cursorName);
        }

        parameters.AddOutputVarchar(options.MessageParameterName, options.MessageSize);

        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            plSqlText,
            parameters,
            commandType: CommandType.Text,
            cancellationToken: cancellationToken);

        var cursors = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            StringComparer.OrdinalIgnoreCase);

        using (var grid = await connection.QueryMultipleAsync(command))
        {
            foreach (var cursorName in cursorNames)
            {
                var rows = await grid.ReadAsync();
                cursors[cursorName.TrimStart(':')] = ToDictionaries(rows);
            }
        }

        var message = parameters.GetNullableString(options.MessageParameterName);

        return new OraclePlSqlMultiResult(cursors, message);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDictionaries(
        IEnumerable<dynamic> rows)
    {
        return rows
            .Select(row => (IReadOnlyDictionary<string, object?>)
                new Dictionary<string, object?>(
                    (IDictionary<string, object?>)row,
                    StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}

public sealed class OracleDynamicParameters : SqlMapper.IDynamicParameters
{
    private readonly List<OracleParameter> _parameters = [];

    public void AddInput(string name, object? value)
    {
        _parameters.Add(new OracleParameter
        {
            ParameterName = NormalizeName(name),
            Direction = ParameterDirection.Input,
            OracleDbType = InferOracleDbType(value),
            Value = ToOracleValue(value)
        });
    }

    public void AddRefCursor(string name)
    {
        _parameters.Add(new OracleParameter
        {
            ParameterName = NormalizeName(name),
            Direction = ParameterDirection.Output,
            OracleDbType = OracleDbType.RefCursor
        });
    }

    public void AddOutputVarchar(string name, int size = 4000)
    {
        _parameters.Add(new OracleParameter
        {
            ParameterName = NormalizeName(name),
            Direction = ParameterDirection.Output,
            OracleDbType = OracleDbType.Varchar2,
            Size = size
        });
    }

    public string? GetNullableString(string name)
    {
        var value = GetParameter(name).Value;

        return value switch
        {
            null => null,
            DBNull => null,
            OracleString oracleString when oracleString.IsNull => null,
            OracleString oracleString => oracleString.Value,
            _ => value.ToString()
        };
    }

    public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
    {
        if (command is not OracleCommand oracleCommand)
        {
            throw new InvalidOperationException("Oracle.ManagedDataAccess.Client.OracleCommand is required.");
        }

        oracleCommand.BindByName = true;

        foreach (var parameter in _parameters)
        {
            oracleCommand.Parameters.Add(parameter);
        }
    }

    private OracleParameter GetParameter(string name)
    {
        var normalizedName = NormalizeName(name);
        return _parameters.Single(parameter =>
            string.Equals(parameter.ParameterName, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.TrimStart(':');
    }

    private static object ToOracleValue(object? value) => value ?? DBNull.Value;

    private static OracleDbType InferOracleDbType(object? value)
    {
        return value switch
        {
            null => OracleDbType.Varchar2,
            string => OracleDbType.Varchar2,
            char => OracleDbType.Char,
            bool => OracleDbType.Int16,
            byte => OracleDbType.Byte,
            short => OracleDbType.Int16,
            int => OracleDbType.Int32,
            long => OracleDbType.Int64,
            float => OracleDbType.Single,
            double => OracleDbType.Double,
            decimal => OracleDbType.Decimal,
            DateTime => OracleDbType.Date,
            DateTimeOffset => OracleDbType.TimeStampTZ,
            Guid => OracleDbType.Varchar2,
            byte[] => OracleDbType.Blob,
            _ => OracleDbType.Varchar2
        };
    }
}
