using System;
using System.Collections.Generic;
using System.Data;

namespace AIMS.Infrastructure.Data;

public interface ISqlDialect
{
    string Quote(string identifier);
    string SelectFromDual { get; }
    int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param);

    /// <summary>
    /// Executes an UPDATE statement with proper Oracle parameter binding.
    /// </summary>
    Task<int> ExecuteUpdateAsync(IDbConnection conn, string sql, Dictionary<string, object?> parameters);

    /// <summary>
    /// Wraps a SELECT query with paging, ordered by <paramref name="orderBy"/>.
    /// The query parameters must include integer "Offset" and "PageSize" values.
    /// </summary>
    string Paginate(string selectSql, string orderBy);

    /// <summary>
    /// Escapes LIKE wildcards in user-supplied search text so they match literally
    /// (a search for "100%" must not match "100x"). The corresponding LIKE clause
    /// must specify <c>ESCAPE '\'</c>. Which characters need escaping is
    /// provider-specific: SQL Server adds '[' character classes, while escaping a
    /// non-wildcard character on Oracle raises ORA-01424.
    /// </summary>
    string EscapeLike(string value);
}
