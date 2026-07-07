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
public class EditConditionModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public EditConditionModel(
        AssetItemService assetItemService,
        IActivityLogger activityLogger,
        IWebHostEnvironment env,
        FileUploadHelper fileUpload)
    {
        _assetItemService = assetItemService;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    private static readonly string[] AllowedPictureExtensions = { ".jpg", ".jpeg", ".png" };

    [BindProperty]
    public EditConditionInput Input { get; set; } = new();

    [BindProperty]
    public List<AddFileInput> Files { get; set; } = new();

    public AssetItem AssetItem { get; set; } = null!;
    public List<AssetItemDocuments> ExistingFiles { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        AssetItem = assetItem;
        Input = new EditConditionInput
        {
            DateOfInspection = assetItem.DateOfInspection,
            Inspector = assetItem.Inspector,
            Condition = assetItem.Condition,
            Comment = assetItem.Comment
        };
        ExistingFiles = (await _assetItemService.GetDocumentsAsync(id))
            .Where(d => d.DocumentType == DocumentTypeCode.Picture)
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        AssetItem = assetItem;

        for (var i = 0; i < Files.Count; i++)
        {
            ModelState.Remove($"Files[{i}].Description");
            ModelState.Remove($"Files[{i}].File");
        }

        if (!ModelState.IsValid) return Page();

        // Only the Condition (GVI) fields change here — every other column is carried
        // over unchanged so UpdateAsync's full-row UPDATE doesn't clobber the rest of
        // the asset register data.
        assetItem.DateOfInspection = Input.DateOfInspection;
        assetItem.Inspector = Input.Inspector;
        assetItem.Condition = Input.Condition;
        assetItem.Comment = Input.Comment;

        await _assetItemService.UpdateAsync(id, assetItem);

        await _activityLogger.LogActivityAsync(
            "AssetItemConditionUpdated",
            $"Condition (GVI) updated for asset item '{assetItem.Title}'",
            "AssetItem",
            id.ToString());

        // Files are only picked client-side (see EditCondition.cshtml); they ride along
        // in this same POST and get saved now, alongside the condition update.
        var dir = Path.Combine(_env.WebRootPath, "asset-documents");
        foreach (var entry in Files)
        {
            if (entry.File == null || entry.File.Length == 0) continue;

            var ext = Path.GetExtension(entry.File.FileName).ToLowerInvariant();
            if (!AllowedPictureExtensions.Contains(ext)) continue;
            if (entry.File.Length > 10 * 1024 * 1024) continue;

            var fileName = _fileUpload.GetFileName(entry.File.FileName, buildUniqueName: true);
            await _fileUpload.SaveFileAsync(entry.File.OpenReadStream(), dir, fileName);

            var description = string.IsNullOrWhiteSpace(entry.Description) ? entry.File.FileName : entry.Description;
            await _assetItemService.AddDocumentAsync(id, description, $"/asset-documents/{fileName}", User.Identity?.Name ?? "Unknown", DocumentTypeCode.Picture);

            await _activityLogger.LogActivityAsync(
                "AssetItemDocumentUploaded",
                $"Document '{description}' uploaded to asset item '{assetItem.Title}'",
                "AssetItem",
                id.ToString());
        }

        return RedirectToPage("Details", new { id, tab = "condition" });
    }

    public async Task<IActionResult> OnPostDeleteFileAsync(int id, int documentId)
    {
        var doc = await _assetItemService.DeleteDocumentAsync(documentId);
        if (doc == null) return NotFound();

        _fileUpload.DeleteFile(doc.FilePath, _env.WebRootPath);

        await _activityLogger.LogActivityAsync(
            "AssetItemDocumentDeleted",
            $"Document '{doc.DocumentTitle}' deleted",
            "AssetItem",
            id.ToString());

        return RedirectToPage("EditCondition", new { id });
    }

    public class EditConditionInput
    {
        [Display(Name = "Date of Inspection")]
        public DateTime? DateOfInspection { get; set; }

        [StringLength(200)]
        public string? Inspector { get; set; }

        [StringLength(200)]
        public string? Condition { get; set; }

        [StringLength(1000)]
        public string? Comment { get; set; }
    }

    public class AddFileInput
    {
        [StringLength(250)]
        public string? Description { get; set; }

        public IFormFile? File { get; set; }
    }
}
