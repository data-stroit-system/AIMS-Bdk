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

    public List<PlantCode> PlantCodes => PlantCode.All.ToList();
    public List<EquipmentCode> EquipmentCodes => EquipmentCode.All.ToList();
    public List<CivilAssetCode> CivilAssetCodes => CivilAssetCode.All.ToList();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var assetItem = await _assetItemService.GetByIdAsync(id);
        if (assetItem == null) return NotFound();

        ExistingPicturePath = assetItem.PicturePath;
        Input = new EditAssetItemInput
        {
            GisRefNo = assetItem.GisRefNo,
            AssetId = assetItem.AssetId,
            Title = assetItem.Title,
            PlantCode = assetItem.PlantCode,
            EquipmentCode = assetItem.EquipmentCode,
            EquipmentDesc = assetItem.EquipmentDesc,
            EquipmentOrder = assetItem.EquipmentOrder,
            CivilAssetCode = assetItem.CivilAssetCode,
            CivilAssetDesc = assetItem.CivilAssetDesc,
            CivilAssetOrder = assetItem.CivilAssetOrder,
            QrCode = assetItem.QrCode,
            Function = assetItem.Function,
            Material = assetItem.Material,
            YearInstalled = assetItem.YearInstalled,
            Owner = assetItem.Owner,
            Constrain = assetItem.Constrain,
            Access = assetItem.Access,
            CoordinateN = assetItem.CoordinateN,
            CoordinateE = assetItem.CoordinateE,
            Zone = assetItem.Zone,
            Area = assetItem.Area,
            Train = assetItem.Train,
            DateOfInspection = assetItem.DateOfInspection,
            Inspector = assetItem.Inspector,
            Condition = assetItem.Condition,
            Comment = assetItem.Comment,
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

        var plantDesc = PlantCode.GetDescription(Input.PlantCode);
        var equipDesc = EquipmentCode.GetDescription(Input.EquipmentCode);
        var civilDesc = CivilAssetCode.GetDescription(Input.CivilAssetCode);

        var assetId = Input.AssetId;
        if (string.IsNullOrWhiteSpace(assetId))
        {
            assetId = AssetItem.GenerateAssetId(
                Input.PlantCode, Input.EquipmentCode,
                Input.EquipmentOrder,
                Input.CivilAssetCode, Input.CivilAssetOrder);
        }

        var updates = new AssetItem
        {
            GisRefNo = Input.GisRefNo,
            AssetId = assetId,
            Title = Input.Title,
            PlantCode = Input.PlantCode,
            PlantDescription = plantDesc,
            EquipmentCode = Input.EquipmentCode,
            EquipmentDescription = equipDesc,
            EquipmentDesc = Input.EquipmentDesc,
            EquipmentOrder = Input.EquipmentOrder,
            CivilAssetCode = Input.CivilAssetCode,
            CivilAssetDescription = civilDesc,
            CivilAssetDesc = Input.CivilAssetDesc,
            CivilAssetOrder = Input.CivilAssetOrder,
            QrCode = Input.QrCode,
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
            DateOfInspection = Input.DateOfInspection,
            Inspector = Input.Inspector,
            Condition = Input.Condition,
            Comment = Input.Comment,
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
        [StringLength(200)] public string? GisRefNo { get; set; }

        [StringLength(100)] public string AssetId { get; set; } = string.Empty;

        [Required, StringLength(200)] public string Title { get; set; } = string.Empty;

        [Required, StringLength(200)] public string PlantCode { get; set; } = string.Empty;

        [Required, StringLength(200)] public string EquipmentCode { get; set; } = string.Empty;

        [StringLength(200)] public string? EquipmentDesc { get; set; }
        public int? EquipmentOrder { get; set; }

        [Required, StringLength(200)] public string CivilAssetCode { get; set; } = string.Empty;

        [StringLength(200)] public string? CivilAssetDesc { get; set; }
        public int? CivilAssetOrder { get; set; }

        [StringLength(500)] public string? QrCode { get; set; }

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

        public DateTime? DateOfInspection { get; set; }
        [StringLength(200)] public string? Inspector { get; set; }
        [StringLength(200)] public string? Condition { get; set; }
        [StringLength(1000)] public string? Comment { get; set; }

        [StringLength(250)] public string? Description { get; set; }
        public AssetType? Type { get; set; }
        [StringLength(250)] public string? Location { get; set; }
        public AssetPriority? Priority { get; set; }
        public IntegrityStatus? IntegrityStatus { get; set; }

        public IFormFile? Picture { get; set; }
    }
}
