using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize]
public class PrintTagModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly PlantService _plantService;

    public PrintTagModel(AssetItemService assetItemService, PlantService plantService)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
    }

    public AssetItem AssetItem { get; set; } = null!;
    public Plant? Plant { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var asset = await _assetItemService.GetByIdAsync(id);
        if (asset == null) return NotFound();

        AssetItem = asset;
        if (asset.PlantId.HasValue)
            Plant = await _plantService.GetByIdAsync(asset.PlantId.Value);

        return Page();
    }
}
