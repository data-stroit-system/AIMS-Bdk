using AIMS.Core.Entities;
using AIMS.Infrastructure.FileTransfer;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize(Roles = "Admin,Manager")]
public class EditModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public EditModel(AssetItemService assetItemService, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload)
    {
        _assetItemService = assetItemService;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    [BindProperty]
    public EditAssetItemInput Input { get; set; } = new();

    public string? ExistingPicturePath { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        ExistingPicturePath = assetItem.PicturePath;
        Input = new EditAssetItemInput
        {
            Title = assetItem.Title,
            AssetId = assetItem.AssetId,
            Description = assetItem.Description,
            Type = assetItem.Type,
            Location = assetItem.Location,
            Priority = assetItem.Priority,
            IntegrityStatus = assetItem.IntegrityStatus
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        ExistingPicturePath = assetItem.PicturePath;

        if (!ModelState.IsValid) return Page();

        string? newPicturePath = assetItem.PicturePath;

        if (Input.Picture != null && Input.Picture.Length > 0)
        {
            var (isValid, error) = _fileUpload.ValidatePicture(Input.Picture);
            if (!isValid)
            {
                ModelState.AddModelError("Input.Picture", error);
                return Page();
            }

            _fileUpload.DeleteFile(assetItem.PicturePath, _env.WebRootPath);
            newPicturePath = await _fileUpload.SaveAssetPictureAsync(Input.Picture, _env.WebRootPath);
        }

        var updates = new AssetItem
        {
            Title = Input.Title,
            AssetId = Input.AssetId,
            Description = Input.Description,
            Type = Input.Type,
            Location = Input.Location,
            Priority = Input.Priority,
            IntegrityStatus = Input.IntegrityStatus,
            PicturePath = newPicturePath
        };

        await _assetItemService.UpdateAsync(id, updates);

        await _activityLogger.LogActivityAsync(
            "AssetItemUpdated",
            $"Asset item '{assetItem.Title}' updated to '{Input.Title}'",
            "AssetItem",
            id.ToString());

        return RedirectToPage("Index");
    }

    public class EditAssetItemInput
    {
        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string AssetId { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }

        [Required]
        public AssetType Type { get; set; }

        [StringLength(250)]
        public string? Location { get; set; }

        [Required]
        public AssetPriority Priority { get; set; }

        [Required]
        public IntegrityStatus IntegrityStatus { get; set; }

        public IFormFile? Picture { get; set; }
    }
}
