using System;
using System.Data;
using System.Reflection;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace AIMS.Infrastructure.Data;

internal sealed class OracleDialect : ISqlDialect
{
    public string Quote(string identifier) => identifier;

    public string SelectFromDual => "FROM DUAL";

    public int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param)
    {
        var oraConn = (OracleConnection)conn;
        if (oraConn.State != ConnectionState.Open)
            oraConn.Open();

        var oraParams = atParams.Replace("@", ":");
        using var cmd = oraConn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = $"INSERT INTO {quotedTable} ({cols}) VALUES ({oraParams}) RETURNING Id INTO :returnedId";

        foreach (var prop in param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = ToOracleValue(prop.GetValue(param));
            cmd.Parameters.Add(new OracleParameter(prop.Name, value));
        }

        var returnParam = new OracleParameter("returnedId", OracleDbType.Int32)
        {
            Direction = ParameterDirection.Output
        };
        cmd.Parameters.Add(returnParam);
        cmd.ExecuteNonQuery();

        return ((OracleDecimal)returnParam.Value).ToInt32();
    }

    private static object ToOracleValue(object? value) => value switch
    {
        null => DBNull.Value,
        Enum e => Convert.ToInt32(e),
        bool b => b ? 1 : 0,
        _ => value
    };
}
