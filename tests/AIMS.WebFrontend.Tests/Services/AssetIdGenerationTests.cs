using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Dapper;

namespace AIMS.WebFrontend.Tests.Services;

public class AssetIdGenerationTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();

    private AssetItemService CreateService() => new(_context, new SqliteTestDialect());

    [Theory]
    [InlineData(20, "D", "Q", 1, "20D/Q-1")]
    [InlineData(31, "F", "FDN", 2, "31F/FDN-2")]
    [InlineData(null, "D", "Q", 1, "D/Q-1")]
    public void GenerateAssetId_FormatsPlantAndCivilParts(
        int? plantCode, string equipmentCode,
        string civilAssetCode, int? civilAssetOrder, string expected)
    {
        var assetId = AssetItem.GenerateAssetId(
            plantCode, equipmentCode, civilAssetCode, civilAssetOrder);

        Assert.Equal(expected, assetId);
    }

    [Theory]
    [InlineData(17, "D", "Q", 1, "17D")]
    [InlineData(17, "F", "Q", 1, "17Q-1-Q")]
    public void GenerateAssetId_RespectsCategory(
        int? plantCode, string equipmentCode,
        string civilAssetCode, int? civilAssetOrder, string expected)
    {
        var categories = new[] { "Equipment / Main Structure", "Foundation / Supporting Structure" };
        var index = expected.EndsWith("-Q") ? 1 : 0;
        var assetId = AssetItem.GenerateAssetId(
            plantCode, equipmentCode, civilAssetCode, civilAssetOrder, categories[index]);

        Assert.Equal(expected, assetId);
    }

    [Fact]
    public async Task CreateAsync_GeneratesAssetIdFromPlantCodeAndPersistsIt()
    {
        _context.AddPlant(1, 20, "Plant 20");
        var service = CreateService();
        var item = new AssetItem
        {
            PlantId = 1,
            EquipmentCode = "D",
            CivilAssetCode = "Q",
            CivilAssetOrder = 1
        };

        var id = await service.CreateAsync(item);

        Assert.Equal("20D/Q-1", item.AssetId);
        var stored = await service.GetByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("20D/Q-1", stored.AssetId);
    }

    [Fact]
    public async Task CreateAsync_OverwritesClientSuppliedAssetId()
    {
        _context.AddPlant(1, 20, "Plant 20");
        var service = CreateService();
        var item = new AssetItem
        {
            AssetId = "TAMPERED-TAG",
            PlantId = 1,
            EquipmentCode = "D",
            CivilAssetCode = "Q",
            CivilAssetOrder = 1
        };

        var id = await service.CreateAsync(item);

        var stored = await service.GetByIdAsync(id);
        Assert.Equal("20D/Q-1", stored!.AssetId);
    }

    [Fact]
    public async Task UpdateAsync_RegeneratesAssetIdWhenPlantOrCodesChange()
    {
        _context.AddPlant(1, 20, "Plant 20");
        _context.AddPlant(2, 31, "Plant 31");
        var service = CreateService();
        var item = new AssetItem
        {
            PlantId = 1,
            EquipmentCode = "D",
            CivilAssetCode = "Q",
            CivilAssetOrder = 1
        };
        var id = await service.CreateAsync(item);

        item.PlantId = 2;
        item.CivilAssetOrder = 7;
        await service.UpdateAsync(id, item);

        var stored = await service.GetByIdAsync(id);
        Assert.Equal("31D/Q-7", stored!.AssetId);
    }

    public void Dispose() => _context.Dispose();
}
