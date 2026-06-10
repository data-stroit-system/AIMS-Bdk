using System.Data;

namespace AIMS.Infrastructure.Data;

public interface ISqlDialect
{
    string Quote(string identifier);
    string SelectFromDual { get; }
    int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param);

    /// <summary>
    /// Wraps a SELECT query with paging, ordered by <paramref name="orderBy"/>.
    /// The query parameters must include integer "Offset" and "PageSize" values.
    /// </summary>
    string Paginate(string selectSql, string orderBy);
}
