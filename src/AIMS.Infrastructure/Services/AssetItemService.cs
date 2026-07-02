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

    private string Fn => "Function";
    private string Ow => "Owner";
    private string Ac => "Access";
    private string Cn => "Condition";
    private string Cm => "Comment";

    private string AllColumns => $@"
        Id, GisRefNo, AssetId, Title,
        PlantCode, PlantDescription,
        EquipmentCode, EquipmentDescription, EquipmentDesc, EquipmentOrder,
        CivilAssetCode, CivilAssetDescription, CivilAssetDesc, CivilAssetOrder,
        QrCode, {_dialect.Quote(Fn)}, Material, YearInstalled, {_dialect.Quote(Ow)}, Constrain, {_dialect.Quote(Ac)},
        CoordinateN, CoordinateE, Zone, Area, Train,
        DateOfInspection, Inspector, {_dialect.Quote(Cn)}, {_dialect.Quote(Cm)},
        Description, Type, Location, Priority, IntegrityStatus, PicturePath,
        CreatedAt, CreatedBy";

    public async Task<(List<AssetItem> Items, int TotalCount)> GetPagedAsync(
        string? searchTerm, AssetType? typeFilter, AssetPriority? priorityFilter,
        IntegrityStatus? statusFilter, int page, int pageSize)
    {
        var where = new StringBuilder("WHERE 1=1");
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(searchTerm))
        {
            where.Append(" AND (Title LIKE @Search OR AssetId LIKE @Search OR Description LIKE @Search OR Location LIKE @Search OR PlantDescription LIKE @Search OR EquipmentDesc LIKE @Search OR CivilAssetDesc LIKE @Search)");
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
            _dialect.Paginate($"SELECT {AllColumns} FROM AssetItems {whereClause}", "Id DESC"),
            p)).ToList();

        return (items, totalCount);
    }

    public async Task<AssetItem?> GetByIdAsync(int id)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<AssetItem>(
            $"SELECT {AllColumns} FROM AssetItems WHERE Id = @Id", new { Id = id });
    }

    public Task<int> CreateAsync(AssetItem item)
    {
        using var conn = _context.CreateConnection();
        var id = _dialect.InsertAndGetId(conn,
            _dialect.Quote("AssetItems"),
            $"GisRefNo, AssetId, Title, " +
            "PlantCode, PlantDescription, " +
            "EquipmentCode, EquipmentDescription, EquipmentDesc, EquipmentOrder, " +
            "CivilAssetCode, CivilAssetDescription, CivilAssetDesc, CivilAssetOrder, " +
            $"QrCode, {_dialect.Quote(Fn)}, Material, YearInstalled, {_dialect.Quote(Ow)}, Constrain, {_dialect.Quote(Ac)}, " +
            "CoordinateN, CoordinateE, Zone, Area, Train, " +
            $"DateOfInspection, Inspector, {_dialect.Quote(Cn)}, {_dialect.Quote(Cm)}, " +
            "Description, Type, Location, Priority, IntegrityStatus, PicturePath, CreatedAt, CreatedBy",
            "@GisRefNo, @AssetId, @Title, " +
            "@PlantCode, @PlantDescription, " +
            "@EquipmentCode, @EquipmentDescription, @EquipmentDesc, @EquipmentOrder, " +
            "@CivilAssetCode, @CivilAssetDescription, @CivilAssetDesc, @CivilAssetOrder, " +
            "@QrCode, @Function, @Material, @YearInstalled, @Owner, @Constrain, @Access, " +
            "@CoordinateN, @CoordinateE, @Zone, @Area, @Train, " +
            "@DateOfInspection, @Inspector, @Condition, @Comment, " +
            "@Description, @Type, @Location, @Priority, @IntegrityStatus, @PicturePath, @CreatedAt, @CreatedBy",
            new
            {
                item.GisRefNo, item.AssetId, item.Title,
                item.PlantCode, item.PlantDescription,
                item.EquipmentCode, item.EquipmentDescription, item.EquipmentDesc, item.EquipmentOrder,
                item.CivilAssetCode, item.CivilAssetDescription, item.CivilAssetDesc, item.CivilAssetOrder,
                item.QrCode, item.Function, item.Material, item.YearInstalled, item.Owner, item.Constrain, item.Access,
                item.CoordinateN, item.CoordinateE, item.Zone, item.Area, item.Train,
                item.DateOfInspection, item.Inspector, item.Condition, item.Comment,
                item.Description, Type = (int?)item.Type,
                item.Location, Priority = (int?)item.Priority,
                IntegrityStatus = (int?)item.IntegrityStatus,
                item.PicturePath, item.CreatedAt, item.CreatedBy
            });
        return Task.FromResult(id);
    }

    public async Task UpdateAsync(int id, AssetItem updates)
    {
        using var conn = _context.CreateConnection();
        var sql = $@"
            UPDATE {_dialect.Quote("AssetItems")}
            SET GisRefNo = @GisRefNo, AssetId = @AssetId, Title = @Title,
                PlantCode = @PlantCode, PlantDescription = @PlantDescription,
                EquipmentCode = @EquipmentCode, EquipmentDescription = @EquipmentDescription,
                EquipmentDesc = @EquipmentDesc, EquipmentOrder = @EquipmentOrder,
                CivilAssetCode = @CivilAssetCode, CivilAssetDescription = @CivilAssetDescription,
                CivilAssetDesc = @CivilAssetDesc, CivilAssetOrder = @CivilAssetOrder,
                QrCode = @QrCode,
                {_dialect.Quote(Fn)} = @Function, Material = @Material, YearInstalled = @YearInstalled,
                {_dialect.Quote(Ow)} = @Owner, Constrain = @Constrain, {_dialect.Quote(Ac)} = @Access,
                CoordinateN = @CoordinateN, CoordinateE = @CoordinateE,
                Zone = @Zone, Area = @Area, Train = @Train,
                DateOfInspection = @DateOfInspection, Inspector = @Inspector,
                {_dialect.Quote(Cn)} = @Condition, {_dialect.Quote(Cm)} = @Comment,
                Description = @Description, Type = @Type, Location = @Location,
                Priority = @Priority, IntegrityStatus = @IntegrityStatus,
                PicturePath = @PicturePath
            WHERE Id = @Id";

        var parameters = new Dictionary<string, object?>
        {
            { "GisRefNo", updates.GisRefNo },
            { "AssetId", updates.AssetId },
            { "Title", updates.Title },
            { "PlantCode", updates.PlantCode },
            { "PlantDescription", updates.PlantDescription },
            { "EquipmentCode", updates.EquipmentCode },
            { "EquipmentDescription", updates.EquipmentDescription },
            { "EquipmentDesc", updates.EquipmentDesc },
            { "EquipmentOrder", updates.EquipmentOrder },
            { "CivilAssetCode", updates.CivilAssetCode },
            { "CivilAssetDescription", updates.CivilAssetDescription },
            { "CivilAssetDesc", updates.CivilAssetDesc },
            { "CivilAssetOrder", updates.CivilAssetOrder },
            { "QrCode", updates.QrCode },
            { "Function", updates.Function },
            { "Material", updates.Material },
            { "YearInstalled", updates.YearInstalled },
            { "Owner", updates.Owner },
            { "Constrain", updates.Constrain },
            { "Access", updates.Access },
            { "CoordinateN", updates.CoordinateN },
            { "CoordinateE", updates.CoordinateE },
            { "Zone", updates.Zone },
            { "Area", updates.Area },
            { "Train", updates.Train },
            { "DateOfInspection", updates.DateOfInspection },
            { "Inspector", updates.Inspector },
            { "Condition", updates.Condition },
            { "Comment", updates.Comment },
            { "Description", updates.Description },
            { "Type", (int?)updates.Type },
            { "Location", updates.Location },
            { "Priority", (int?)updates.Priority },
            { "IntegrityStatus", (int?)updates.IntegrityStatus },
            { "PicturePath", updates.PicturePath },
            { "Id", id }
        };

        await _dialect.ExecuteUpdateAsync(conn, sql, parameters);
    }

    public async Task<(AssetItem? Asset, List<AssetItemDocuments> Documents)> DeleteAsync(int id)
    {
        using var conn = _context.CreateConnection();
        var asset = await conn.QuerySingleOrDefaultAsync<AssetItem>(
            $"SELECT {AllColumns} FROM AssetItems WHERE Id = @Id", new { Id = id });
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
