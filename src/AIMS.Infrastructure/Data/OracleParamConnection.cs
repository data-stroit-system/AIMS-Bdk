using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace AIMS.Infrastructure.Data;

/// <summary>
/// Wraps OracleConnection so Dapper's "@Name" style parameters (written for SQL Server)
/// work against Oracle, which only recognizes ":Name" bind variables.
/// </summary>
internal sealed class OracleParamConnection : DbConnection
{
    private readonly OracleConnection _inner;

    public OracleParamConnection(string connectionString)
    {
        _inner = new OracleConnection(connectionString);
    }

    protected override DbProviderFactory DbProviderFactory => OracleClientFactory.Instance;

    /// <summary>The underlying ODP.NET connection, for code that needs Oracle-specific APIs.</summary>
    public OracleConnection Inner => _inner;

    [AllowNull]
    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }

    public override string Database => _inner.Database;
    public override string DataSource => _inner.DataSource;
    public override string ServerVersion => _inner.ServerVersion;
    public override ConnectionState State => _inner.State;
    public override int ConnectionTimeout => _inner.ConnectionTimeout;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public override void Close() => _inner.Close();
    public override void Open() => _inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        _inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() => new OracleParamCommand(_inner.CreateCommand(), this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class OracleParamCommand : DbCommand
{
    private static readonly Regex ParamRegex = new(@"@(\w+)", RegexOptions.Compiled);

    private readonly OracleCommand _inner;
    private readonly OracleParamConnection _connection;

    public OracleParamCommand(OracleCommand inner, OracleParamConnection connection)
    {
        _inner = inner;
        _connection = connection;
        _inner.BindByName = true;
    }

    [AllowNull]
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value is null ? string.Empty : ParamRegex.Replace(value, ":$1");
    }

    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set { }
    }

    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = (OracleTransaction?)value;
    }

    public override void Cancel() => _inner.Cancel();

    public override int ExecuteNonQuery()
    {
        FixParameters();
        return _inner.ExecuteNonQuery();
    }

    public override object? ExecuteScalar()
    {
        FixParameters();
        return _inner.ExecuteScalar();
    }

    public override void Prepare() => _inner.Prepare();

    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        FixParameters();
        return _inner.ExecuteReader(behavior);
    }

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        FixParameters();
        return _inner.ExecuteNonQueryAsync(cancellationToken);
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        FixParameters();
        return _inner.ExecuteScalarAsync(cancellationToken)!;
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        FixParameters();
        return await _inner.ExecuteReaderAsync(behavior, cancellationToken);
    }

    /// <summary>
    /// Dapper adds parameters with @Name prefix, but our CommandText regex converts
    /// @Name to :Name in the SQL. Oracle requires parameter names to match without @.
    /// Strip @ prefix from all parameter names so they match the :Name bind variables.
    /// </summary>
    private void FixParameterNames()
    {
        foreach (OracleParameter p in _inner.Parameters)
        {
            if (p.ParameterName.StartsWith("@"))
                p.ParameterName = p.ParameterName.Substring(1);
        }
    }

    /// <summary>
    /// ODP.NET cannot bind DbType.Boolean parameters (ORA-03115: unsupported network datatype).
    /// Dapper maps .NET bool to DbType.Boolean for the bit/NUMBER(1,0) columns used by
    /// IdentityUser etc., so translate those to Int32 0/1 here.
    /// </summary>
    private void FixBooleanParameters()
    {
        foreach (OracleParameter p in _inner.Parameters)
        {
            if (p.Value is bool b)
                p.Value = b ? 1 : 0;
            if (p.DbType == DbType.Boolean)
                p.DbType = DbType.Int32;
        }
    }

    private void FixParameters()
    {
        FixParameterNames();
        FixBooleanParameters();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
