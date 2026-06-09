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
public class CreateModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public CreateModel(AssetItemService assetItemService, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload)
    {
        _assetItemService = assetItemService;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    [BindProperty]
    public CreateAssetItemInput Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        string? picturePath = null;

        if (Input.Picture != null && Input.Picture.Length > 0)
        {
            var (isValid, error) = _fileUpload.ValidatePicture(Input.Picture);
            if (!isValid)
            {
                ModelState.AddModelError("Input.Picture", error);
                return Page();
            }

            picturePath = await _fileUpload.SaveAssetPictureAsync(Input.Picture, _env.WebRootPath);
        }

        var item = new AssetItem
        {
            Title = Input.Title,
            AssetId = Input.AssetId,
            Description = Input.Description,
            Type = Input.Type,
            Location = Input.Location,
            Priority = Input.Priority,
            IntegrityStatus = Input.IntegrityStatus,
            PicturePath = picturePath,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "Unknown"
        };

        var newId = await _assetItemService.CreateAsync(item);

        await _activityLogger.LogActivityAsync(
            "AssetItemCreated",
            $"Asset item '{Input.Title}' created",
            "AssetItem",
            newId.ToString());

        return RedirectToPage("Index");
    }

    public class CreateAssetItemInput
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
