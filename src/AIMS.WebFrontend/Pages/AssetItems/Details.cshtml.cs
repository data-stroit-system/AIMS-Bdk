using AIMS.Core.Entities;
using AIMS.Core.Services.Calculations;
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
    private readonly PlantService _plantService;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;
    private readonly IAssetCalculationResolver _calculationResolver;

    public DetailsModel(AssetItemService assetItemService, PlantService plantService, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload, IAssetCalculationResolver calculationResolver)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
        _calculationResolver = calculationResolver;
    }

    public AssetItem AssetItem { get; set; } = null!;
    public Plant? Plant { get; set; }
    public List<AssetItemDocuments> Documents { get; set; } = new();
    public AssetCalculationResult? Calculation { get; set; }
    public string ActiveTab { get; set; } = "register";

    [BindProperty]
    public AddDocumentInput DocumentInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        AssetItem = assetItem;
        if (assetItem.PlantId.HasValue)
            Plant = await _plantService.GetByIdAsync(assetItem.PlantId.Value);
        Documents = await _assetItemService.GetDocumentsAsync(id);
        Calculation = _calculationResolver.Calculate(assetItem);
        var tab = Request.Query["tab"].ToString();
        ActiveTab = tab == "documents" || tab == "condition" || tab == "inspection" ? tab : "register";
        return Page();
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

        var pictureExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var documentType = pictureExtensions.Contains(ext) ? DocumentTypeCode.Picture : DocumentTypeCode.Document;
        await _assetItemService.AddDocumentAsync(id, DocumentInput.DocumentTitle, $"/asset-documents/{fileName}", User.Identity?.Name ?? "Unknown", documentType);

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

    private async Task LoadDetailsAsync(int id)
    {
        AssetItem = (await _assetItemService.GetByIdAsync(id))!;
        if (AssetItem.PlantId.HasValue)
            Plant = await _plantService.GetByIdAsync(AssetItem.PlantId.Value);
        Documents = await _assetItemService.GetDocumentsAsync(id);
        Calculation = _calculationResolver.Calculate(AssetItem);
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
