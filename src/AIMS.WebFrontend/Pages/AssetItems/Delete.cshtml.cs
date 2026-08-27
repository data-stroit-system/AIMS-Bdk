using AIMS.Infrastructure.FileTransfer;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize(Roles = "Admin,Manager")]
public class DeleteModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly IActivityLogger _activityLogger;
    private readonly FileUploadHelper _fileUpload;
    private readonly IWebHostEnvironment _env;

    public DeleteModel(AssetItemService assetItemService, IActivityLogger activityLogger, FileUploadHelper fileUpload, IWebHostEnvironment env)
    {
        _assetItemService = assetItemService;
        _activityLogger = activityLogger;
        _fileUpload = fileUpload;
        _env = env;
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var (asset, docs) = await _assetItemService.DeleteAsync(id);
        if (asset == null) return NotFound();

        if (!string.IsNullOrEmpty(asset.PicturePath))
            _fileUpload.DeleteFile(asset.PicturePath, _env.WebRootPath);
        foreach (var doc in docs)
            if (!string.IsNullOrEmpty(doc.FilePath))
                _fileUpload.DeleteFile(doc.FilePath, _env.WebRootPath);

        await _activityLogger.LogActivityAsync(
            "AssetItemDeleted",
            $"Asset item '{asset.Title}' deleted",
            "AssetItem",
            id.ToString());

        return RedirectToPage("Index");
    }
}
