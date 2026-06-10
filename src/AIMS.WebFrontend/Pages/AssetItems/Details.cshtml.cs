using AIMS.Core.Entities;
using AIMS.Infrastructure.FileTransfer;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public DetailsModel(AssetItemService assetItemService, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload)
    {
        _assetItemService = assetItemService;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    public AssetItem AssetItem { get; set; } = null!;
    public List<AssetItemRemarks> Remarks { get; set; } = new();
    public List<AssetItemDocuments> Documents { get; set; } = new();
    public string ActiveTab { get; set; } = "remarks";

    [BindProperty]
    public AddRemarkInput Input { get; set; } = new();

    [BindProperty]
    public AddDocumentInput DocumentInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        AssetItem = assetItem;
        Remarks = await _assetItemService.GetRemarksAsync(id);
        Documents = await _assetItemService.GetDocumentsAsync(id);
        ActiveTab = Request.Query["tab"].ToString() == "documents" ? "documents" : "remarks";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("User"))
            return Forbid();

        ModelState.Remove("DocumentInput.DocumentTitle");
        ModelState.Remove("DocumentInput.DocumentFile");
        ModelState.Remove("DocumentTitle");
        ModelState.Remove("DocumentFile");

        if (!ModelState.IsValid)
        {
            ActiveTab = "remarks";
            await LoadDetailsAsync(id);
            return Page();
        }

        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        await _assetItemService.AddRemarkAsync(id, Input.Description, User.Identity?.Name ?? "Unknown");

        await _activityLogger.LogActivityAsync(
            "AssetItemRemarkAdded",
            $"Remark added to asset item '{assetItem.Title}'",
            "AssetItem",
            id.ToString());

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUploadDocumentAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("User"))
            return Forbid();

        ModelState.Remove("Input.Description");
        ModelState.Remove("Description");

        if (!ModelState.IsValid)
        {
            ActiveTab = "documents";
            await LoadDetailsAsync(id);
            return Page();
        }

        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        var file = DocumentInput.DocumentFile;
        var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv", ".jpg", ".jpeg", ".png" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(ext))
        {
            ModelState.AddModelError("DocumentInput.DocumentFile", "Unsupported file type.");
            ActiveTab = "documents";
            await LoadDetailsAsync(id);
            return Page();
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            ModelState.AddModelError("DocumentInput.DocumentFile", "File size must not exceed 10 MB.");
            ActiveTab = "documents";
            await LoadDetailsAsync(id);
            return Page();
        }

        var dir = Path.Combine(_env.WebRootPath, "asset-documents");
        var fileName = _fileUpload.GetFileName(file.FileName, buildUniqueName: true);
        await _fileUpload.SaveFileAsync(file.OpenReadStream(), dir, fileName);

        await _assetItemService.AddDocumentAsync(id, DocumentInput.DocumentTitle, $"/asset-documents/{fileName}", User.Identity?.Name ?? "Unknown");

        await _activityLogger.LogActivityAsync(
            "AssetItemDocumentUploaded",
            $"Document '{DocumentInput.DocumentTitle}' uploaded to asset item '{assetItem.Title}'",
            "AssetItem",
            id.ToString());

        return RedirectToPage(new { id, tab = "documents" });
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, int documentId)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            return Forbid();

        var doc = await _assetItemService.DeleteDocumentAsync(documentId);
        if (doc == null) return NotFound();

        _fileUpload.DeleteFile(doc.FilePath, _env.WebRootPath);

        await _activityLogger.LogActivityAsync(
            "AssetItemDocumentDeleted",
            $"Document '{doc.DocumentTitle}' deleted",
            "AssetItem",
            id.ToString());

        return RedirectToPage(new { id, tab = "documents" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            return Forbid();

        var (asset, docs) = await _assetItemService.DeleteAsync(id);
        if (asset == null) return NotFound();

        _fileUpload.DeleteFile(asset.PicturePath, _env.WebRootPath);
        foreach (var doc in docs)
            _fileUpload.DeleteFile(doc.FilePath, _env.WebRootPath);

        await _activityLogger.LogActivityAsync(
            "AssetItemDeleted",
            $"Asset item '{asset.Title}' deleted",
            "AssetItem",
            id.ToString());

        return RedirectToPage("Index");
    }

    private async Task LoadDetailsAsync(int id)
    {
        AssetItem = (await _assetItemService.GetByIdAsync(id))!;
        Remarks = await _assetItemService.GetRemarksAsync(id);
        Documents = await _assetItemService.GetDocumentsAsync(id);
    }

    public class AddRemarkInput
    {
        [Required]
        [StringLength(250, MinimumLength = 1)]
        [Display(Name = "Remark")]
        public string Description { get; set; } = string.Empty;
    }

    public class AddDocumentInput
    {
        [Required]
        [StringLength(250)]
        [Display(Name = "Document Title")]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Document File")]
        public IFormFile DocumentFile { get; set; } = null!;
    }
}
