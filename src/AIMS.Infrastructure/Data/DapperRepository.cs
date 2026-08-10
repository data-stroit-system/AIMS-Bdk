using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIMS.SharedKernel;
using AIMS.SharedKernel.Interfaces;
using Dapper;

namespace AIMS.Infrastructure.Data;

public class DapperRepository : IRepository
{
    private readonly IDapperContext _context;
    private readonly ISqlDialect _dialect;

    public DapperRepository(IDapperContext context, ISqlDialect dialect)
    {
        _context = context;
        _dialect = dialect;
    }

    public T GetById<T>(int id) where T : BaseEntity
    {
        using var conn = _context.CreateConnection();
        return conn.QuerySingleOrDefault<T>(
            $"SELECT * FROM {_dialect.Quote(TableName<T>())} WHERE Id = @Id", new { Id = id })!;
    }

    public List<T> List<T>() where T : BaseEntity
    {
        using var conn = _context.CreateConnection();
        return conn.Query<T>($"SELECT * FROM {_dialect.Quote(TableName<T>())}").ToList();
    }

    public T Add<T>(T entity) where T : BaseEntity
    {
        var props = ScalarProperties<T>();
        var cols = string.Join(", ", props.Select(p => p.Name));
        var atParams = string.Join(", ", props.Select(p => "@" + p.Name));
        using var conn = _context.CreateConnection();
        entity.Id = _dialect.InsertAndGetId(conn, _dialect.Quote(TableName<T>()), cols, atParams, entity);
        return entity;
    }

    public void Update<T>(T entity) where T : BaseEntity
    {
        var props = ScalarProperties<T>();
        var sets = string.Join(", ", props.Select(p => $"{p.Name} = @{p.Name}"));
        using var conn = _context.CreateConnection();
        conn.Execute($"UPDATE {_dialect.Quote(TableName<T>())} SET {sets} WHERE Id = @Id", entity);
    }

    public void Delete<T>(T entity) where T : BaseEntity
    {
        using var conn = _context.CreateConnection();
        conn.Execute($"DELETE FROM {_dialect.Quote(TableName<T>())} WHERE Id = @Id", new { entity.Id });
    }

    private static string TableName<T>() => typeof(T).Name + "s";

    private static PropertyInfo[] ScalarProperties<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.Name != "Id")
            .Where(p => IsScalarType(p.PropertyType))
            .ToArray();

    private static bool IsScalarType(Type type)
    {
        if (type == typeof(string)) return true;
        if (type.IsEnum) return true;
        if (type.IsValueType) return true;
        if (Nullable.GetUnderlyingType(type) != null) return true;
        return false;
    }
}
