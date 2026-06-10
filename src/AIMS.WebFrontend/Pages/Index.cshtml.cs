using AIMS.Core.Entities;
using AIMS.Infrastructure.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IDapperContext _context;

    public IndexModel(IDapperContext context)
    {
        _context = context;
    }

    public int TotalAssets { get; set; }

    public int HighPriorityCount { get; set; }
    public int MediumPriorityCount { get; set; }
    public int LowPriorityCount { get; set; }

    public int GoodStatusCount { get; set; }
    public int FairStatusCount { get; set; }
    public int PoorStatusCount { get; set; }

    public Dictionary<AssetType, int> AssetsByType { get; set; } = new();

    public List<AssetItem> RecentAssets { get; set; } = new();

    public async Task OnGetAsync()
    {
        using var conn = _context.CreateConnection();
        var assets = (await conn.QueryAsync<AssetItem>("SELECT * FROM AssetItems")).ToList();

        TotalAssets = assets.Count;

        HighPriorityCount = assets.Count(a => a.Priority == AssetPriority.High);
        MediumPriorityCount = assets.Count(a => a.Priority == AssetPriority.Medium);
        LowPriorityCount = assets.Count(a => a.Priority == AssetPriority.Low);

        GoodStatusCount = assets.Count(a => a.IntegrityStatus == IntegrityStatus.Good);
        FairStatusCount = assets.Count(a => a.IntegrityStatus == IntegrityStatus.Fair);
        PoorStatusCount = assets.Count(a => a.IntegrityStatus == IntegrityStatus.Poor);

        AssetsByType = assets
            .GroupBy(a => a.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        RecentAssets = assets
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .ToList();
    }
}
