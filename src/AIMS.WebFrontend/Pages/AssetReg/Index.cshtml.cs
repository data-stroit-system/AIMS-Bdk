using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.AssetReg;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly PlantService _plantService;
    private const int PageSize = 25;

    public IndexModel(AssetItemService assetItemService, PlantService plantService)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
    }

    public List<AssetItem> AssetItems { get; set; } = new();
    public Dictionary<int, Plant> PlantsById { get; set; } = new();
    public List<Plant> Plants { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PlantId { get; set; }

    public async Task OnGetAsync(int page = 1)
    {
        CurrentPage = page < 1 ? 1 : page;

        Plants = await _plantService.ListAsync();
        PlantsById = Plants.ToDictionary(p => p.Id, p => p);

        var (items, totalCount) = await _assetItemService.GetPagedAsync(
            SearchTerm, StatusFilter, CurrentPage, PageSize, PlantId);

        AssetItems = items;
        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        TotalPages = TotalPages < 1 ? 1 : TotalPages;
    }
}
