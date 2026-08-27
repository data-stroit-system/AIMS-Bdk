using AIMS.Infrastructure.Services;

namespace AIMS.WebFrontend.Tests.Services;

/// <summary>
/// Covers the transactional delete paths (asset + children, plant + detach) against
/// the shared in-memory SQLite database — the same Dapper SQL and BeginTransaction
/// code that runs against SQL Server/Oracle.
/// </summary>
public class AssetItemDeleteAsyncTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();
    private readonly AssetItemService _service;

    public AssetItemDeleteAsyncTests()
    {
        _service = new AssetItemService(_context, new SqliteTestDialect());
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task DeleteAsync_RemovesAssetChildRowsAndReturnsDocs()
    {
        _context.AddAsset("20D-4/Q-1", null, "Good"); // Id = 1
        _context.AddDocument(1, "photo", "/asset-documents/doc1.jpg");
        _context.AddDocument(1, "report", "/asset-documents/doc2.pdf");
        _context.AddRemark(1, "checked");

        var (asset, docs) = await _service.DeleteAsync(1);

        Assert.NotNull(asset);
        Assert.Equal("20D-4/Q-1", asset.AssetId);
        Assert.Equal(2, docs.Count);
        Assert.Equal(0, _context.Count("AssetItems"));
        Assert.Equal(0, _context.Count("AssetItemDocuments"));
        Assert.Equal(0, _context.Count("AssetRemarks"));
    }

    [Fact]
    public async Task DeleteAsync_OnlyTouchesTheTargetAsset()
    {
        _context.AddAsset("20D-4/Q-1", null, "Good"); // Id = 1
        _context.AddAsset("20D-4/Q-2", null, "Fair"); // Id = 2
        _context.AddDocument(2, "other asset's doc", "/asset-documents/keep.jpg");

        await _service.DeleteAsync(1);

        Assert.Equal(1, _context.Count("AssetItems"));
        Assert.Equal(1, _context.Count("AssetItemDocuments"));
    }

    [Fact]
    public async Task DeleteAsync_MissingAsset_ReturnsNullWithoutDeleting()
    {
        _context.AddAsset("20D-4/Q-1", null, "Good");

        var (asset, docs) = await _service.DeleteAsync(999);

        Assert.Null(asset);
        Assert.Empty(docs);
        Assert.Equal(1, _context.Count("AssetItems"));
    }
}

public class PlantDeleteAsyncTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();
    private readonly PlantService _service;

    public PlantDeleteAsyncTests()
    {
        _service = new PlantService(_context, new SqliteTestDialect());
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task DeleteAsync_RemovesPlantAndDetachesItsAssets()
    {
        _context.AddPlant(1, 20, "Plant 20");
        _context.AddPlant(2, 31, "Plant 31");
        _context.AddAsset("20D-4/Q-1", 1, "Good");
        _context.AddAsset("31D-1/F-1", 2, "Fair");

        var plant = await _service.DeleteAsync(1);

        Assert.NotNull(plant);
        Assert.Equal("Plant 20", plant.Name);
        Assert.Equal(1, _context.Count("Plants"));
        // Its asset survives, detached; the other plant's asset is untouched.
        Assert.Equal(2, _context.Count("AssetItems"));
        Assert.Equal(1, _context.Count("AssetItems WHERE PlantId IS NULL"));
        Assert.Equal(1, _context.Count("AssetItems WHERE PlantId = 2"));
    }

    [Fact]
    public async Task DeleteAsync_MissingPlant_ReturnsNull()
    {
        Assert.Null(await _service.DeleteAsync(999));
    }
}
