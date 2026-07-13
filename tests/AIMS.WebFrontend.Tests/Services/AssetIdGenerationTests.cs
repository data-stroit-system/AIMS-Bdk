using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Dapper;

namespace AIMS.WebFrontend.Tests.Services;

public class AssetIdGenerationTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();

    private AssetItemService CreateService() => new(_context, new SqliteTestDialect());

    [Theory]
    [InlineData(20, "D", 4, "Q", 1, "20D-4/Q-1")]
    [InlineData(31, "F", 1, "FDN", 2, "31F-1/FDN-2")]
    [InlineData(null, "D", 4, "Q", 1, "D-4/Q-1")] // no plant → no numeric prefix
    public void GenerateAssetId_FormatsPlantEquipmentAndCivilParts(
        int? plantCode, string equipmentCode, int? equipmentOrder,
        string civilAssetCode, int? civilAssetOrder, string expected)
    {
        var assetId = AssetItem.GenerateAssetId(
            plantCode, equipmentCode, equipmentOrder, civilAssetCode, civilAssetOrder);

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
            EquipmentOrder = 4,
            CivilAssetCode = "Q",
            CivilAssetOrder = 1
        };

        var id = await service.CreateAsync(item);

        Assert.Equal("20D-4/Q-1", item.AssetId);
        var stored = await service.GetByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("20D-4/Q-1", stored.AssetId);
    }

    [Fact]
    public async Task CreateAsync_OverwritesClientSuppliedAssetId()
    {
        _context.AddPlant(1, 20, "Plant 20");
        var service = CreateService();
        var item = new AssetItem
        {
            AssetId = "TAMPERED-TAG", // must never be accepted from client input
            PlantId = 1,
            EquipmentCode = "D",
            EquipmentOrder = 4,
            CivilAssetCode = "Q",
            CivilAssetOrder = 1
        };

        var id = await service.CreateAsync(item);

        var stored = await service.GetByIdAsync(id);
        Assert.Equal("20D-4/Q-1", stored!.AssetId);
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
            EquipmentOrder = 4,
            CivilAssetCode = "Q",
            CivilAssetOrder = 1
        };
        var id = await service.CreateAsync(item);

        item.PlantId = 2;
        item.EquipmentOrder = 7;
        await service.UpdateAsync(id, item);

        var stored = await service.GetByIdAsync(id);
        Assert.Equal("31D-7/Q-1", stored!.AssetId);
    }

    public void Dispose() => _context.Dispose();
}
