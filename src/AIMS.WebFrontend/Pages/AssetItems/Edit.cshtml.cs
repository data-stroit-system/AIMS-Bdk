using AIMS.Core.Entities;
using AIMS.Infrastructure.Data;
using AIMS.Infrastructure.FileTransfer;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize(Roles = "Admin,Manager")]
public class EditModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public EditModel(AppDbContext context, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload)
    {
        _context = context;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    [BindProperty]
    public EditAssetItemInput Input { get; set; } = new();

    public string? ExistingPicturePath { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var assetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == id);

        if (assetItem == null)
            return NotFound();

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
        var assetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == id);

        if (assetItem == null)
            return NotFound();

        ExistingPicturePath = assetItem.PicturePath;

        if (!ModelState.IsValid)
            return Page();

        if (Input.Picture != null && Input.Picture.Length > 0)
        {
            var (isValid, error) = _fileUpload.ValidatePicture(Input.Picture);
            if (!isValid)
            {
                ModelState.AddModelError("Input.Picture", error);
                return Page();
            }

            _fileUpload.DeleteFile(assetItem.PicturePath, _env.WebRootPath);
            assetItem.PicturePath = await _fileUpload.SaveAssetPictureAsync(Input.Picture, _env.WebRootPath);
        }

        var originalTitle = assetItem.Title;
        assetItem.Title = Input.Title;
        assetItem.AssetId = Input.AssetId;
        assetItem.Description = Input.Description;
        assetItem.Type = Input.Type;
        assetItem.Location = Input.Location;
        assetItem.Priority = Input.Priority;
        assetItem.IntegrityStatus = Input.IntegrityStatus;

        _context.AssetItems.Update(assetItem);
        await _context.SaveChangesAsync();

        await _activityLogger.LogActivityAsync(
            "AssetItemUpdated",
            $"Asset item '{originalTitle}' updated to '{assetItem.Title}'",
            "AssetItem",
            assetItem.Id.ToString()
        );

        return RedirectToPage("Index");
    }

    public class EditAssetItemInput
    {
        [Required]
        [StringLength(150)]
        public string Title { get; set; }

        [Required]
        [StringLength(50)]
        public string AssetId { get; set; }

        [StringLength(250)]
        public string Description { get; set; }

        [Required]
        public AssetType Type { get; set; }

        [StringLength(250)]
        public string Location { get; set; }

        [Required]
        public AssetPriority Priority { get; set; }

        [Required]
        public IntegrityStatus IntegrityStatus { get; set; }

        public IFormFile? Picture { get; set; }
    }
}
