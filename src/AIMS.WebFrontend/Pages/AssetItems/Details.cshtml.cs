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

[Authorize]
public class DetailsModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public DetailsModel(AppDbContext context, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload)
    {
        _context = context;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    public AssetItem AssetItem { get; set; }
    public List<AssetItemRemarks> Remarks { get; set; } = new();
    public List<AssetItemDocuments> Documents { get; set; } = new();
    public string ActiveTab { get; set; } = "remarks";

    [BindProperty]
    public AddRemarkInput Input { get; set; } = new();

    [BindProperty]
    public AddDocumentInput DocumentInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        AssetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == id);
        if (AssetItem == null)
            return NotFound();

        Remarks = await _context.AssetRemarks
            .Where(r => r.AssetItem.Id == id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        Documents = await _context.AssetItemDocuments
            .Where(d => d.AssetItem.Id == id)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        ActiveTab = Request.Query["tab"].ToString() == "documents" ? "documents" : "remarks";

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("User"))
            return Forbid();

        ModelState.Remove("DocumentInput.DocumentTitle");
        ModelState.Remove("DocumentInput.DocumentFile");

        if (!ModelState.IsValid)
        {
            ActiveTab = "remarks";
            AssetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == id);
            Remarks = await _context.AssetRemarks
                .Where(r => r.AssetItem.Id == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            Documents = await _context.AssetItemDocuments
                .Where(d => d.AssetItem.Id == id)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
            return Page();
        }

        var assetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == id);
        if (assetItem == null)
            return NotFound();

        var remark = new AssetItemRemarks
        {
            Description = Input.Description,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "Unknown",
            AssetItem = assetItem
        };

        _context.AssetRemarks.Add(remark);
        await _context.SaveChangesAsync();

        await _activityLogger.LogActivityAsync(
            "AssetItemRemarkAdded",
            $"Remark added to asset item '{assetItem.Title}'",
            "AssetItem",
            assetItem.Id.ToString()
        );

        return RedirectToPage(new { id = assetItem.Id });
    }

    public async Task<IActionResult> OnPostUploadDocumentAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("User"))
            return Forbid();

        ModelState.Remove("Description");

        async Task<IActionResult> ReturnWithDocumentTab(int assetId)
        {
            ActiveTab = "documents";
            AssetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == assetId);
            Remarks = await _context.AssetRemarks
                .Where(r => r.AssetItem.Id == assetId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            Documents = await _context.AssetItemDocuments
                .Where(d => d.AssetItem.Id == assetId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
            return Page();
        }

        if (!ModelState.IsValid)
            return await ReturnWithDocumentTab(id);

        var assetItem = await _context.AssetItems.FirstOrDefaultAsync(a => a.Id == id);
        if (assetItem == null)
            return NotFound();

        var file = DocumentInput.DocumentFile;
        var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv", ".jpg", ".jpeg", ".png" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(ext))
        {
            ModelState.AddModelError("DocumentInput.DocumentFile", "Unsupported file type. Allowed: PDF, DOC, DOCX, XLS, XLSX, TXT, CSV, JPG, PNG.");
            return await ReturnWithDocumentTab(id);
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            ModelState.AddModelError("DocumentInput.DocumentFile", "File size must not exceed 10 MB.");
            return await ReturnWithDocumentTab(id);
        }

        var dir = Path.Combine(_env.WebRootPath, "asset-documents");
        var fileName = _fileUpload.GetFileName(file.FileName, buildUniqueName: true);
        await _fileUpload.SaveFileAsync(file.OpenReadStream(), dir, fileName);

        var doc = new AssetItemDocuments
        {
            DocumentTitle = DocumentInput.DocumentTitle,
            FilePath = $"/asset-documents/{fileName}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "Unknown",
            AssetItem = assetItem
        };

        _context.AssetItemDocuments.Add(doc);
        await _context.SaveChangesAsync();

        await _activityLogger.LogActivityAsync(
            "AssetItemDocumentUploaded",
            $"Document '{DocumentInput.DocumentTitle}' uploaded to asset item '{assetItem.Title}'",
            "AssetItem",
            assetItem.Id.ToString()
        );

        return RedirectToPage(new { id = assetItem.Id, tab = "documents" });
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, int documentId)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            return Forbid();

        var doc = await _context.AssetItemDocuments.FindAsync(documentId);
        if (doc == null)
            return NotFound();

        _fileUpload.DeleteFile(doc.FilePath, _env.WebRootPath);
        _context.AssetItemDocuments.Remove(doc);
        await _context.SaveChangesAsync();

        await _activityLogger.LogActivityAsync(
            "AssetItemDocumentDeleted",
            $"Document '{doc.DocumentTitle}' deleted",
            "AssetItem",
            id.ToString()
        );

        return RedirectToPage(new { id, tab = "documents" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            return Forbid();

        var assetItem = await _context.AssetItems
            .Include(a => a.AssetItemRemarks)
            .Include(a => a.AssetItemDocuments)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (assetItem == null)
            return NotFound();

        foreach (var doc in assetItem.AssetItemDocuments)
            _fileUpload.DeleteFile(doc.FilePath, _env.WebRootPath);

        _context.AssetItemDocuments.RemoveRange(assetItem.AssetItemDocuments);
        _context.AssetRemarks.RemoveRange(assetItem.AssetItemRemarks);
        _context.AssetItems.Remove(assetItem);
        await _context.SaveChangesAsync();

        await _activityLogger.LogActivityAsync(
            "AssetItemDeleted",
            $"Asset item '{assetItem.Title}' deleted",
            "AssetItem",
            id.ToString()
        );

        return RedirectToPage("Index");
    }

    public class AddRemarkInput
    {
        [Required]
        [StringLength(250, MinimumLength = 1)]
        [Display(Name = "Remark")]
        public string Description { get; set; }
    }

    public class AddDocumentInput
    {
        [Required]
        [StringLength(250)]
        [Display(Name = "Document Title")]
        public string DocumentTitle { get; set; }

        [Required]
        [Display(Name = "Document File")]
        public IFormFile DocumentFile { get; set; }
    }
}
