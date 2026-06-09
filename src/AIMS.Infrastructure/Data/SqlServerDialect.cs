using System.Data;
using Dapper;

namespace AIMS.Infrastructure.Data;

internal sealed class SqlServerDialect : ISqlDialect
{
    public string Quote(string identifier) => $"[{identifier}]";

    public string SelectFromDual => string.Empty;

    public int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param)
    {
        var sql = $"INSERT INTO {quotedTable} ({cols}) OUTPUT INSERTED.Id VALUES ({atParams})";
        return conn.QuerySingle<int>(sql, param);
    }
}
