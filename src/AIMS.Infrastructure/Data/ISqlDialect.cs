using System.Data;

namespace AIMS.Infrastructure.Data;

public interface ISqlDialect
{
    string Quote(string identifier);
    string SelectFromDual { get; }
    int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param);
}
