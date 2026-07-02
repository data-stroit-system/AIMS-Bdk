using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly PlantService _plantService;
    private const int PageSize = 10;

    public IndexModel(AssetItemService assetItemService, PlantService plantService)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
    }

    public List<AssetItem> AssetItems { get; set; } = new();
    public Dictionary<int, Plant> PlantsById { get; set; } = new();
    public Plant? FilterPlant { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PriorityFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PlantId { get; set; }

    public async Task OnGetAsync(int page = 1)
    {
        CurrentPage = page < 1 ? 1 : page;

        AssetType? typeFilter = Enum.TryParse<AssetType>(TypeFilter, out var t) ? t : null;
        AssetPriority? priorityFilter = Enum.TryParse<AssetPriority>(PriorityFilter, out var pr) ? pr : null;
        IntegrityStatus? statusFilter = Enum.TryParse<IntegrityStatus>(StatusFilter, out var s) ? s : null;

        if (PlantId.HasValue)
            FilterPlant = await _plantService.GetByIdAsync(PlantId.Value);

        PlantsById = (await _plantService.ListAsync()).ToDictionary(p => p.Id, p => p);

        var (items, totalCount) = await _assetItemService.GetPagedAsync(
            SearchTerm, typeFilter, priorityFilter, statusFilter, CurrentPage, PageSize, PlantId);

        AssetItems = items;
        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        TotalPages = TotalPages < 1 ? 1 : TotalPages;
    }
}
