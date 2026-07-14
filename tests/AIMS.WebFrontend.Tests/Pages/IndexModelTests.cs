using AIMS.Infrastructure.Services;
using AIMS.WebFrontend.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIMS.WebFrontend.Tests.Pages;

public class IndexModelTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();

    private IndexModel CreateModel() =>
        new(_context, new SqliteTestDialect(), new PlantService(_context, new SqliteTestDialect()));

    [Fact]
    public async Task OnGetAsync_EmptyDatabase_ReportsZeroAssetsAndOnlyAllPlantSummary()
    {
        var model = CreateModel();

        await model.OnGetAsync();

        Assert.Equal(0, model.TotalAssets);
        Assert.Equal(0, model.GoodStatusCount);
        Assert.Equal(0, model.FairStatusCount);
        Assert.Equal(0, model.PoorStatusCount);
        var summary = Assert.Single(model.PlantConditionSummaries);
        Assert.Equal("All Plant", summary.PlantName);
    }

    [Fact]
    public async Task OnGetAsync_CountsConditionsCaseInsensitively()
    {
        _context.AddAsset("20D-4/Q-1", null, "Good");
        _context.AddAsset("20D-4/Q-2", null, "good");
        _context.AddAsset("20D-4/Q-3", null, "FAIR");
        _context.AddAsset("20D-4/Q-4", null, "Poor");
        _context.AddAsset("20D-4/Q-5", null, null);

        var model = CreateModel();
        await model.OnGetAsync();

        Assert.Equal(5, model.TotalAssets);
        Assert.Equal(2, model.GoodStatusCount);
        Assert.Equal(1, model.FairStatusCount);
        Assert.Equal(1, model.PoorStatusCount);
    }

    [Fact]
    public async Task OnGetAsync_BuildsAllPlantAggregateFollowedByPerPlantSummaries()
    {
        _context.AddPlant(1, 20, "Plant 20");
        _context.AddPlant(2, 31, "Plant 31");
        _context.AddAsset("20D-4/Q-1", 1, "Good");
        _context.AddAsset("20D-4/Q-2", 1, "Poor");
        _context.AddAsset("31D-1/F-1", 2, "Fair");
        _context.AddAsset("XX-ORPHAN", null, null); // no plant assigned

        var model = CreateModel();
        await model.OnGetAsync();

        Assert.Equal(3, model.PlantConditionSummaries.Count);

        var all = model.PlantConditionSummaries[0];
        Assert.Equal("All Plant", all.PlantName);
        Assert.Equal(1, all.Good);
        Assert.Equal(1, all.Fair);
        Assert.Equal(1, all.Poor);
        Assert.Equal(1, all.Unknown);

        var plant20 = model.PlantConditionSummaries[1];
        Assert.Equal("Plant 20", plant20.PlantName);
        Assert.Equal(1, plant20.Good);
        Assert.Equal(0, plant20.Fair);
        Assert.Equal(1, plant20.Poor);
        Assert.Equal(0, plant20.Unknown);

        var plant31 = model.PlantConditionSummaries[2];
        Assert.Equal("Plant 31", plant31.PlantName);
        Assert.Equal(0, plant31.Good);
        Assert.Equal(1, plant31.Fair);
        Assert.Equal(0, plant31.Poor);
        Assert.Equal(0, plant31.Unknown);
    }

    [Fact]
    public async Task OnGetAsync_UnrecognizedConditionIsNeitherCountedNorUnknown()
    {
        _context.AddAsset("20D-4/Q-1", null, "Excellent");

        var model = CreateModel();
        await model.OnGetAsync();

        Assert.Equal(1, model.TotalAssets);
        Assert.Equal(0, model.GoodStatusCount);
        Assert.Equal(0, model.FairStatusCount);
        Assert.Equal(0, model.PoorStatusCount);
        Assert.Equal(0, model.PlantConditionSummaries[0].Unknown);
    }

    public void Dispose() => _context.Dispose();
}

public class ErrorModelTests
{
    [Fact]
    public void OnGet_UsesTraceIdentifierWhenNoActivity()
    {
        var model = new ErrorModel(NullLogger<ErrorModel>.Instance)
        {
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-123" }
            }
        };

        model.OnGet();

        Assert.Equal("trace-123", model.RequestId);
        Assert.True(model.ShowRequestId);
    }
}
