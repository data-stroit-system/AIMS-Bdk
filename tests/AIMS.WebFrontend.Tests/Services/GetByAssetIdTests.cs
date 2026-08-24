using AIMS.Infrastructure.Services;

namespace AIMS.WebFrontend.Tests.Services;

/// <summary>
/// Covers the tag lookup used by the anonymous /asset/{tag} page — case-insensitive
/// exact match on AssetId against the shared in-memory SQLite database.
/// </summary>
public class GetByAssetIdTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();
    private readonly AssetItemService _service;

    public GetByAssetIdTests()
    {
        _service = new AssetItemService(_context, new SqliteTestDialect());
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetByAssetIdAsync_ExactMatch_ReturnsAsset()
    {
        _context.AddAsset("17D-1-Q", 1, "Good", "Test asset");

        var asset = await _service.GetByAssetIdAsync("17D-1-Q");

        Assert.NotNull(asset);
        Assert.Equal("17D-1-Q", asset.AssetId);
        Assert.Equal(1, asset.PlantId);
        Assert.Equal("Test asset", asset.Title);
    }

    [Fact]
    public async Task GetByAssetIdAsync_IsCaseInsensitive()
    {
        _context.AddAsset("17D-1-Q", 1, "Good");

        var asset = await _service.GetByAssetIdAsync("17d-1-q");

        Assert.NotNull(asset);
        Assert.Equal("17D-1-Q", asset.AssetId);
    }

    [Fact]
    public async Task GetByAssetIdAsync_LegacySlashTag_Matches()
    {
        _context.AddAsset("17D/4-2", 1, "Fair");

        var asset = await _service.GetByAssetIdAsync("17D/4-2");

        Assert.NotNull(asset);
        Assert.Equal("17D/4-2", asset.AssetId);
    }

    [Fact]
    public async Task GetByAssetIdAsync_MissingTag_ReturnsNull()
    {
        _context.AddAsset("17D-1-Q", 1, "Good");

        Assert.Null(await _service.GetByAssetIdAsync("NOPE"));
    }
}
