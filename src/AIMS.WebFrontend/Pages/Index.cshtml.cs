using AIMS.Core.Entities;
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
    private readonly PlantService _plantService;

    public IndexModel(IDapperContext context, PlantService plantService)
    {
        _context = context;
        _plantService = plantService;
    }

    public int TotalAssets { get; set; }

    public int GoodStatusCount { get; set; }
    public int FairStatusCount { get; set; }
    public int PoorStatusCount { get; set; }

    public List<PlantConditionSummary> PlantConditionSummaries { get; set; } = new();

    public async Task OnGetAsync()
    {
        using var conn = _context.CreateConnection();
        var assets = (await conn.QueryAsync<AssetItem>("SELECT * FROM AssetItems")).ToList();

        TotalAssets = assets.Count;

        GoodStatusCount = assets.Count(a => IsCondition(a, "Good"));
        FairStatusCount = assets.Count(a => IsCondition(a, "Fair"));
        PoorStatusCount = assets.Count(a => IsCondition(a, "Poor"));

        var plants = await _plantService.ListAsync();

        PlantConditionSummaries.Add(new PlantConditionSummary
        {
            PlantName = "All Plant",
            Good = GoodStatusCount,
            Fair = FairStatusCount,
            Poor = PoorStatusCount,
            Unknown = assets.Count(a => string.IsNullOrEmpty(a.Condition))
        });

        foreach (var plant in plants)
        {
            var plantAssets = assets.Where(a => a.PlantId == plant.Id).ToList();
            PlantConditionSummaries.Add(new PlantConditionSummary
            {
                PlantName = plant.Name,
                Good = plantAssets.Count(a => IsCondition(a, "Good")),
                Fair = plantAssets.Count(a => IsCondition(a, "Fair")),
                Poor = plantAssets.Count(a => IsCondition(a, "Poor")),
                Unknown = plantAssets.Count(a => string.IsNullOrEmpty(a.Condition))
            });
        }
    }

    private static bool IsCondition(AssetItem asset, string condition) =>
        string.Equals(asset.Condition, condition, StringComparison.OrdinalIgnoreCase);
}

public class PlantConditionSummary
{
    public string PlantName { get; set; } = string.Empty;
    public int Good { get; set; }
    public int Fair { get; set; }
    public int Poor { get; set; }
    public int Unknown { get; set; }
}
