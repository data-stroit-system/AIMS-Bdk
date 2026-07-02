using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace AIMS.Infrastructure.Data;

internal sealed class OracleDialect : ISqlDialect
{
    // Bind names like :Access or :Comment hit ORA-01745 because they are Oracle
    // reserved words; prefix every bind variable so the name is never reserved.
    private static readonly Regex ParamRegex = new(@"@(\w+)", RegexOptions.Compiled);

    public string Quote(string identifier) => $"\"{identifier}\"";

    public string SelectFromDual => "FROM DUAL";

    public int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param)
    {
        var oraConn = conn is OracleParamConnection wrapper ? wrapper.Inner : (OracleConnection)conn;
        if (oraConn.State != ConnectionState.Open)
            oraConn.Open();

        var oraParams = ParamRegex.Replace(atParams, ":p_$1");
        using var cmd = oraConn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = $"INSERT INTO {quotedTable} ({cols}) VALUES ({oraParams}) RETURNING Id INTO :returnedId";

        foreach (var prop in param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = ToOracleValue(prop.GetValue(param));
            cmd.Parameters.Add(new OracleParameter("p_" + prop.Name, value));
        }

        var returnParam = new OracleParameter("returnedId", OracleDbType.Int32)
        {
            Direction = ParameterDirection.Output
        };
        cmd.Parameters.Add(returnParam);
        cmd.ExecuteNonQuery();

        return ((OracleDecimal)returnParam.Value).ToInt32();
    }

    public async Task<int> ExecuteUpdateAsync(IDbConnection conn, string sql, Dictionary<string, object?> parameters)
    {
        var oraConn = conn is OracleParamConnection wrapper ? wrapper.Inner : (OracleConnection)conn;
        if (oraConn.State != ConnectionState.Open)
            oraConn.Open();

        var oraSql = ParamRegex.Replace(sql, ":p_$1");

        using var cmd = oraConn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = oraSql;

        foreach (var kvp in parameters)
        {
            var value = ToOracleValue(kvp.Value);
            cmd.Parameters.Add(new OracleParameter("p_" + kvp.Key, value));
        }

        // Must await here: returning the task un-awaited lets `using var cmd`
        // dispose the command while the statement is still executing.
        return await cmd.ExecuteNonQueryAsync();
    }

    public string Paginate(string selectSql, string orderBy) =>
        $@"SELECT * FROM (
            SELECT page_.*, ROW_NUMBER() OVER (ORDER BY {orderBy}) AS RN_
            FROM ({selectSql}) page_
        ) WHERE RN_ > @Offset AND RN_ <= @Offset + @PageSize";

    private static object ToOracleValue(object? value) => value switch
    {
        null => DBNull.Value,
        Enum e => Convert.ToInt32(e),
        bool b => b ? 1 : 0,
        _ => value
    };
}
