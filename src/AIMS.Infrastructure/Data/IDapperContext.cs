using System.Data;

namespace AIMS.Infrastructure.Data;

public interface IDapperContext
{
    IDbConnection CreateConnection();
}
