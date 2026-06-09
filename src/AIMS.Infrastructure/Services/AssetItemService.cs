using AIMS.Core.Entities;
using AIMS.Infrastructure.Data;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIMS.Infrastructure.Services;

public sealed class AssetItemService
{
    private readonly IDapperContext _context;
    private readonly ISqlDialect _dialect;

    public AssetItemService(IDapperContext context, ISqlDialect dialect)
    {
        _context = context;
        _dialect = dialect;
    }

    public async Task<(List<AssetItem> Items, int TotalCount)> GetPagedAsync(
        string? searchTerm, AssetType? typeFilter, AssetPriority? priorityFilter,
        IntegrityStatus? statusFilter, int page, int pageSize)
    {
        var where = new StringBuilder("WHERE 1=1");
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(searchTerm))
        {
            where.Append(" AND (Title LIKE @Search OR AssetId LIKE @Search OR Description LIKE @Search OR Location LIKE @Search)");
            p.Add("Search", $"%{searchTerm}%");
        }
        if (typeFilter.HasValue)
        {
            where.Append(" AND Type = @Type");
            p.Add("Type", (int)typeFilter.Value);
        }
        if (priorityFilter.HasValue)
        {
            where.Append(" AND Priority = @Priority");
            p.Add("Priority", (int)priorityFilter.Value);
        }
        if (statusFilter.HasValue)
        {
            where.Append(" AND IntegrityStatus = @IntegrityStatus");
            p.Add("IntegrityStatus", (int)statusFilter.Value);
        }

        var whereClause = where.ToString();
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        using var conn = _context.CreateConnection();
        var totalCount = await conn.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM AssetItems {whereClause}", p);

        var items = (await conn.QueryAsync<AssetItem>(
            $"SELECT * FROM AssetItems {whereClause} ORDER BY Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            p)).ToList();

        return (items, totalCount);
    }

    public async Task<AssetItem?> GetByIdAsync(int id)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<AssetItem>(
            "SELECT * FROM AssetItems WHERE Id = @Id", new { Id = id });
    }

    public Task<int> CreateAsync(AssetItem item)
    {
        using var conn = _context.CreateConnection();
        var id = _dialect.InsertAndGetId(conn,
            _dialect.Quote("AssetItems"),
            "Title, AssetId, Description, Type, Location, Priority, IntegrityStatus, PicturePath, CreatedAt, CreatedBy",
            "@Title, @AssetId, @Description, @Type, @Location, @Priority, @IntegrityStatus, @PicturePath, @CreatedAt, @CreatedBy",
            new
            {
                item.Title, item.AssetId, item.Description,
                Type = (int)item.Type, item.Location,
                Priority = (int)item.Priority,
                IntegrityStatus = (int)item.IntegrityStatus,
                item.PicturePath, item.CreatedAt, item.CreatedBy
            });
        return Task.FromResult(id);
    }

    public async Task UpdateAsync(int id, AssetItem updates)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE AssetItems
            SET Title = @Title, AssetId = @AssetId, Description = @Description,
                Type = @Type, Location = @Location, Priority = @Priority,
                IntegrityStatus = @IntegrityStatus, PicturePath = @PicturePath
            WHERE Id = @Id",
            new
            {
                updates.Title, updates.AssetId, updates.Description,
                Type = (int)updates.Type, updates.Location,
                Priority = (int)updates.Priority,
                IntegrityStatus = (int)updates.IntegrityStatus,
                PicturePath = updates.PicturePath,
                Id = id
            });
    }

    public async Task<(AssetItem? Asset, List<AssetItemDocuments> Documents)> DeleteAsync(int id)
    {
        using var conn = _context.CreateConnection();
        var asset = await conn.QuerySingleOrDefaultAsync<AssetItem>(
            "SELECT * FROM AssetItems WHERE Id = @Id", new { Id = id });
        if (asset == null) return (null, new List<AssetItemDocuments>());

        var docs = (await conn.QueryAsync<AssetItemDocuments>(
            "SELECT * FROM AssetItemDocuments WHERE AssetItemId = @Id", new { Id = id })).ToList();

        await conn.ExecuteAsync("DELETE FROM AssetItemDocuments WHERE AssetItemId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM AssetRemarks WHERE AssetItemId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM AssetItems WHERE Id = @Id", new { Id = id });

        return (asset, docs);
    }

    public async Task<List<AssetItemRemarks>> GetRemarksAsync(int id)
    {
        using var conn = _context.CreateConnection();
        return (await conn.QueryAsync<AssetItemRemarks>(
            "SELECT * FROM AssetRemarks WHERE AssetItemId = @Id ORDER BY CreatedAt DESC",
            new { Id = id })).ToList();
    }

    public async Task AddRemarkAsync(int assetItemId, string description, string createdBy)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AssetRemarks (Description, CreatedAt, CreatedBy, AssetItemId)
            VALUES (@Description, @CreatedAt, @CreatedBy, @AssetItemId)",
            new { Description = description, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy, AssetItemId = assetItemId });
    }

    public async Task<List<AssetItemDocuments>> GetDocumentsAsync(int id)
    {
        using var conn = _context.CreateConnection();
        return (await conn.QueryAsync<AssetItemDocuments>(
            "SELECT * FROM AssetItemDocuments WHERE AssetItemId = @Id ORDER BY CreatedAt DESC",
            new { Id = id })).ToList();
    }

    public async Task<AssetItemDocuments?> GetDocumentByIdAsync(int documentId)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<AssetItemDocuments>(
            "SELECT * FROM AssetItemDocuments WHERE Id = @Id", new { Id = documentId });
    }

    public async Task AddDocumentAsync(int assetItemId, string documentTitle, string filePath, string createdBy)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AssetItemDocuments (DocumentTitle, FilePath, CreatedAt, CreatedBy, AssetItemId)
            VALUES (@DocumentTitle, @FilePath, @CreatedAt, @CreatedBy, @AssetItemId)",
            new
            {
                DocumentTitle = documentTitle, FilePath = filePath,
                CreatedAt = DateTime.UtcNow, CreatedBy = createdBy, AssetItemId = assetItemId
            });
    }

    public async Task<AssetItemDocuments?> DeleteDocumentAsync(int documentId)
    {
        using var conn = _context.CreateConnection();
        var doc = await conn.QuerySingleOrDefaultAsync<AssetItemDocuments>(
            "SELECT * FROM AssetItemDocuments WHERE Id = @Id", new { Id = documentId });
        if (doc == null) return null;

        await conn.ExecuteAsync("DELETE FROM AssetItemDocuments WHERE Id = @Id", new { Id = documentId });
        return doc;
    }
}
