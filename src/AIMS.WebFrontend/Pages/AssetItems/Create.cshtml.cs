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
    private readonly PlantService _plantService;
    private readonly IActivityLogger _activityLogger;
    private readonly IWebHostEnvironment _env;
    private readonly FileUploadHelper _fileUpload;

    public CreateModel(AssetItemService assetItemService, PlantService plantService, IActivityLogger activityLogger, IWebHostEnvironment env, FileUploadHelper fileUpload)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
        _activityLogger = activityLogger;
        _env = env;
        _fileUpload = fileUpload;
    }

    [BindProperty]
    public CreateAssetItemInput Input { get; set; } = new();

    public List<EquipmentCode> EquipmentCodes => EquipmentCode.All.ToList();
    public List<CivilAssetCode> CivilAssetCodes => CivilAssetCode.All.ToList();
    public List<Plant> Plants { get; set; } = new();

    public async Task OnGetAsync(int? plantId)
    {
        Plants = await _plantService.ListAsync();
        if (plantId.HasValue)
            Input.PlantId = plantId;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Plants = await _plantService.ListAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

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

        var equipDesc = EquipmentCode.GetDescription(Input.EquipmentCode);
        var civilDesc = CivilAssetCode.GetDescription(Input.CivilAssetCode);

        // AssetId (Asset Tag No.) is generated server-side by AssetItemService from
        // Plant/Equipment/Civil codes — it is never accepted as client input.
        var item = new AssetItem
        {
            GisRefNo = Input.GisRefNo,
            Title = Input.Title,
            EquipmentCode = Input.EquipmentCode,
            EquipmentDescription = equipDesc,
            EquipmentDesc = Input.EquipmentDesc,
            CivilAssetCode = Input.CivilAssetCode,
            CivilAssetDescription = civilDesc,
            CivilAssetDesc = Input.CivilAssetDesc,
            CivilAssetOrder = Input.CivilAssetOrder,
            Function = Input.Function,
            Material = Input.Material,
            YearInstalled = Input.YearInstalled,
            Owner = Input.Owner,
            Constrain = Input.Constrain,
            Access = Input.Access,
            CoordinateN = Input.CoordinateN,
            CoordinateE = Input.CoordinateE,
            Zone = Input.Zone,
            Area = Input.Area,
            Train = Input.Train,
            Category = Input.Category,
            DateOfInspection = Input.DateOfInspection,
            Inspector = Input.Inspector,
            Condition = Input.Condition,
            Comment = Input.Comment,
            PicturePath = picturePath,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "Unknown",
            PlantId = Input.PlantId
        };

        var newId = 0;
        try
        {
            newId = await _assetItemService.CreateAsync(item);
        }
        catch (DuplicateAssetIdException ex)
        {
            // The tag already belongs to another asset — discard the picture that was
            // staged for this never-created asset before re-rendering the form.
            if (!string.IsNullOrEmpty(picturePath))
                _fileUpload.DeleteFile(picturePath, _env.WebRootPath);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        await _activityLogger.LogActivityAsync(
            "AssetItemCreated",
            $"Asset item '{Input.Title}' created",
            "AssetItem",
            newId.ToString());

        return RedirectToPage("Index");
    }

    public class CreateAssetItemInput
    {
        [Display(Name = "Plant")]
        public int? PlantId { get; set; }

        [StringLength(200)] public string? GisRefNo { get; set; }

        [Required, StringLength(200)] public string Title { get; set; } = string.Empty;

        [Required, StringLength(200)] public string EquipmentCode { get; set; } = string.Empty;

        [StringLength(200)] public string? EquipmentDesc { get; set; }

        [Required, StringLength(200)] public string CivilAssetCode { get; set; } = string.Empty;

        [StringLength(200)] public string? CivilAssetDesc { get; set; }
        public int? CivilAssetOrder { get; set; }

        [StringLength(200)] public string? Function { get; set; }
        [StringLength(200)] public string? Material { get; set; }
        public int? YearInstalled { get; set; }

        [StringLength(200)] public string? Owner { get; set; }
        [StringLength(200)] public string? Constrain { get; set; }
        [StringLength(200)] public string? Access { get; set; }

        [StringLength(200)] public string? CoordinateN { get; set; }
        [StringLength(200)] public string? CoordinateE { get; set; }

        [StringLength(200)] public string? Zone { get; set; }
        [StringLength(200)] public string? Area { get; set; }
        [StringLength(200)] public string? Train { get; set; }
        [StringLength(250)] public string? Category { get; set; }

        public DateTime? DateOfInspection { get; set; }
        [StringLength(200)] public string? Inspector { get; set; }
        [StringLength(200)] public string? Condition { get; set; }
        [StringLength(1000)] public string? Comment { get; set; }

        public IFormFile? Picture { get; set; }
    }
}
