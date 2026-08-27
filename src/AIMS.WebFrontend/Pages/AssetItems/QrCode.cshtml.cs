using AIMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize]
public class QrCodeModel : PageModel
{
    private readonly AssetItemService _assetItemService;

    public QrCodeModel(AssetItemService assetItemService)
    {
        _assetItemService = assetItemService;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var asset = await _assetItemService.GetByIdAsync(id);
        if (asset == null) return NotFound();

        var png = QrCodeHelper.GeneratePng(asset.AssetId);
        return File(png, "image/png");
    }
}
