using AIMS.Core.Entities;
using AIMS.Infrastructure.Data;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AIMS.Infrastructure.Services;

public sealed class PlantService
{
    private readonly IDapperContext _context;
    private readonly ISqlDialect _dialect;

    public PlantService(IDapperContext context, ISqlDialect dialect)
    {
        _context = context;
        _dialect = dialect;
    }

    public async Task<List<Plant>> ListAsync()
    {
        using var conn = _context.CreateConnection();
        return (await conn.QueryAsync<Plant>(
            "SELECT Id, Code, Name, Description FROM Plants ORDER BY Code,Name,Description")).ToList();
    }

    public async Task<Plant?> GetByIdAsync(int id)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Plant>(
            "SELECT Id, Code, Name, Description FROM Plants WHERE Id = @Id", new { Id = id });
    }

    public Task<int> CreateAsync(Plant plant)
    {
        using var conn = _context.CreateConnection();
        // Table names are created unquoted (uppercase on Oracle) — do not quote them,
        // only reserved column names need quoting.
        var id = _dialect.InsertAndGetId(conn,
            "Plants",
            "Code, Name, Description",
            "@Code, @Name, @Description",
            new { plant.Code, plant.Name, plant.Description });
        return Task.FromResult(id);
    }

    public async Task UpdateAsync(int id, Plant updates)
    {
        using var conn = _context.CreateConnection();
        await _dialect.ExecuteUpdateAsync(conn,
            "UPDATE Plants SET Code = @Code, Name = @Name, Description = @Description WHERE Id = @Id",
            new Dictionary<string, object?>
            {
                { "Code", updates.Code },
                { "Name", updates.Name },
                { "Description", updates.Description },
                { "Id", id }
            });
    }

    public async Task<Plant?> DeleteAsync(int id)
    {
        using var conn = _context.CreateConnection();
        var plant = await conn.QuerySingleOrDefaultAsync<Plant>(
            "SELECT Id, Code, Name, Description FROM Plants WHERE Id = @Id", new { Id = id });
        if (plant == null) return null;

        await conn.ExecuteAsync("UPDATE AssetItems SET PlantId = NULL WHERE PlantId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM Plants WHERE Id = @Id", new { Id = id });
        return plant;
    }

    public async Task<Dictionary<int, int>> GetAssetCountsAsync()
    {
        using var conn = _context.CreateConnection();
        var rows = await conn.QueryAsync<(int PlantId, int AssetCount)>(
            "SELECT PlantId, COUNT(*) AS AssetCount FROM AssetItems WHERE PlantId IS NOT NULL GROUP BY PlantId");
        return rows.ToDictionary(r => r.PlantId, r => r.AssetCount);
    }

    /// <summary>
    /// Plants with their asset items (Id + AssetId tag only) for the sidebar tree.
    /// </summary>
    public async Task<List<PlantTreeNode>> GetTreeAsync()
    {
        using var conn = _context.CreateConnection();
        var plants = (await conn.QueryAsync<Plant>(
            "SELECT Id, Code, Name, Description FROM Plants ORDER BY Name")).ToList();
        var assets = (await conn.QueryAsync<PlantTreeAsset>(
            "SELECT Id, AssetId, Title, PlantId FROM AssetItems WHERE PlantId IS NOT NULL ORDER BY AssetId")).ToList();

        return plants.Select(p => new PlantTreeNode
        {
            Plant = p,
            Assets = assets.Where(a => a.PlantId == p.Id).ToList()
        }).ToList();
    }
}

public sealed class PlantTreeNode
{
    public Plant Plant { get; set; } = new();
    public List<PlantTreeAsset> Assets { get; set; } = new();
}

public sealed class PlantTreeAsset
{
    public int Id { get; set; }
    public string? AssetId { get; set; }
    public string? Title { get; set; }
    public int? PlantId { get; set; }
}
