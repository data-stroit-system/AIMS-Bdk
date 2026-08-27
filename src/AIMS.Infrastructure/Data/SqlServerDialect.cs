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

    public string Paginate(string selectSql, string orderBy) =>
        $"{selectSql} ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    public Task<int> ExecuteUpdateAsync(IDbConnection conn, string sql, Dictionary<string, object?> parameters)
    {
        return conn.ExecuteAsync(sql, parameters);
    }

    // SQL Server LIKE wildcards: % _ and the [...] character class opener.
    public string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
