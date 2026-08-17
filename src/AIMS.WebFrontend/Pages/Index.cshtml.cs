using AIMS.Infrastructure.Data;
using AIMS.Infrastructure.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IDapperContext _context;
    private readonly ISqlDialect _dialect;
    private readonly PlantService _plantService;
    private readonly IConfiguration _configuration;

    public IndexModel(
        IDapperContext context,
        ISqlDialect dialect,
        PlantService plantService,
        IConfiguration configuration)
    {
        _context = context;
        _dialect = dialect;
        _plantService = plantService;
        _configuration = configuration;
    }

    public string QgisServerUrl { get; private set; } = string.Empty;
    public string MapProject { get; private set; } = string.Empty;

    public int TotalAssets { get; set; }

    public int GoodStatusCount { get; set; }
    public int FairStatusCount { get; set; }
    public int PoorStatusCount { get; set; }

    public List<PlantConditionSummary> PlantConditionSummaries { get; set; } = new();

    public async Task OnGetAsync()
    {
        QgisServerUrl = _configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        MapProject = _configuration["QgisServer:MapProject"] ?? "/home/deli/ProjectPelatihan/Ortho Project_QGS.qgs";

        using var conn = _context.CreateConnection();

        // One aggregate row per (plant, condition) instead of streaming the whole
        // AssetItems table into memory. UPPER() keeps the Good/Fair/Poor matching
        // case-insensitive on every provider (Oracle compares case-sensitively).
        var cn = _dialect.Quote("Condition");
        var rows = (await conn.QueryAsync<ConditionCountRow>($@"
            SELECT PlantId, UPPER({cn}) AS ConditionKey, COUNT(*) AS AssetCount
            FROM AssetItems
            GROUP BY PlantId, UPPER({cn})")).ToList();

        var allPlants = new PlantConditionSummary { PlantName = "All Plant" };
        var perPlant = new Dictionary<int, PlantConditionSummary>();

        foreach (var row in rows)
        {
            TotalAssets += row.AssetCount;
            Accumulate(allPlants, row);

            if (row.PlantId is int plantId)
            {
                if (!perPlant.TryGetValue(plantId, out var summary))
                    perPlant[plantId] = summary = new PlantConditionSummary();
                Accumulate(summary, row);
            }
        }

        GoodStatusCount = allPlants.Good;
        FairStatusCount = allPlants.Fair;
        PoorStatusCount = allPlants.Poor;

        PlantConditionSummaries.Add(allPlants);
        foreach (var plant in await _plantService.ListAsync())
        {
            var summary = perPlant.TryGetValue(plant.Id, out var existing)
                ? existing
                : new PlantConditionSummary();
            summary.PlantName = plant.Name;
            PlantConditionSummaries.Add(summary);
        }
    }

    // Unrecognized non-empty conditions (e.g. legacy free text) intentionally count
    // toward the total only — same semantics as the previous in-memory scan.
    private static void Accumulate(PlantConditionSummary summary, ConditionCountRow row)
    {
        switch (row.ConditionKey)
        {
            case "GOOD": summary.Good += row.AssetCount; break;
            case "FAIR": summary.Fair += row.AssetCount; break;
            case "POOR": summary.Poor += row.AssetCount; break;
            case null or "": summary.Unknown += row.AssetCount; break;
        }
    }

    private sealed class ConditionCountRow
    {
        public int? PlantId { get; set; }
        public string? ConditionKey { get; set; }
        public int AssetCount { get; set; }
    }
}

public class PlantConditionSummary
{
    public string PlantName { get; set; } = string.Empty;
    public int Good { get; set; }
    public int Fair { get; set; }
    public int Poor { get; set; }
    public int Unknown { get; set; }
}
