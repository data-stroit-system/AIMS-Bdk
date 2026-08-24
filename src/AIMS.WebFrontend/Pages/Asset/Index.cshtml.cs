using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Asset;

/// <summary>
/// Anonymous asset lookup by tag (/asset/{tag}) — renders the same asset summary as the
/// Asset Items page's right panel, for QR-tag scans without a login. Deliberately no
/// [Authorize]: this page is the target of the public /Scanner page.
/// </summary>
public class IndexModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly PlantService _plantService;

    public IndexModel(AssetItemService assetItemService, PlantService plantService)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
    }

    /// <summary>Catch-all route value — already URL-decoded by routing; may contain '/' (legacy tags like 17D/4-2).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }

    public AssetItem? Asset { get; set; }
    public Plant? Plant { get; set; }
    public bool AssetNotFound { get; set; }

    public async Task OnGetAsync()
    {
        var tag = Tag?.Trim();
        if (string.IsNullOrWhiteSpace(tag))
        {
            AssetNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var asset = await _assetItemService.GetByAssetIdAsync(tag);
        if (asset == null)
        {
            AssetNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Asset = asset;
        if (asset.PlantId.HasValue)
            Plant = await _plantService.GetByIdAsync(asset.PlantId.Value);
    }
}
