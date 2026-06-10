using System;
using System.Data;
using Microsoft.Extensions.Configuration;

namespace AIMS.Infrastructure.Data;

internal sealed class OracleDapperContext : IDapperContext
{
    private readonly string _connectionString;

    public OracleDapperContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Oracle")
            ?? throw new InvalidOperationException("Connection string 'Oracle' not found.");
    }

    public IDbConnection CreateConnection() => new OracleParamConnection(_connectionString);
}
