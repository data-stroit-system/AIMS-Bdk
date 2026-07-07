using AIMS.SharedKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Core.Entities;

public class AssetItemDocuments : BaseEntity
{
    [MaxLength(250)]
    public string DocumentTitle { get; set; }
    [MaxLength(500)]
    public string FilePath { get; set; }
    /// <summary>DocumentTypeCode.Picture or DocumentTypeCode.Document.</summary>
    [MaxLength(20)]
    public string? DocumentType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]
    public string CreatedBy { get; set; }

    public AssetItem AssetItem { get; set; }
}

/// <summary>Distinguishes picture uploads (jpg/png) from general document uploads (pdf/doc/etc.) within AssetItemDocuments.</summary>
public static class DocumentTypeCode
{
    public const string Picture = "Picture";
    public const string Document = "Document";
}

public class AssetItemRemarks : BaseEntity
{
    [MaxLength(250)]
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]
    public string CreatedBy { get; set; }

    public AssetItem AssetItem { get; set; }
}
public class Plant : BaseEntity
{
    public int? Code { get; set; }
    [MaxLength(200)] public string Name { get; set; }
    [MaxLength(200)] public string Description { get; set; }
    public List<AssetItem> AssetItems { get; set; }
}
public class AssetItem : BaseEntity
{
    [MaxLength(200)] public string? GisRefNo { get; set; }

    public string AssetId { get; set; } = string.Empty;

    [MaxLength(200)] public string? Title { get; set; }

    [MaxLength(200)] public string? EquipmentCode { get; set; }
    [MaxLength(200)] public string? EquipmentDescription { get; set; }
    [MaxLength(200)] public string? EquipmentDesc { get; set; }
    public int? EquipmentOrder { get; set; }

    [MaxLength(200)] public string? CivilAssetCode { get; set; }
    [MaxLength(200)] public string? CivilAssetDescription { get; set; }
    [MaxLength(200)] public string? CivilAssetDesc { get; set; }
    public int? CivilAssetOrder { get; set; }

    [MaxLength(200)] public string? Function { get; set; }
    [MaxLength(200)] public string? Material { get; set; }
    public int? YearInstalled { get; set; }

    [MaxLength(200)] public string? Owner { get; set; }
    [MaxLength(200)] public string? Constrain { get; set; }
    [MaxLength(200)] public string? Access { get; set; }

    [MaxLength(200)] public string? CoordinateN { get; set; }
    [MaxLength(200)] public string? CoordinateE { get; set; }

    [MaxLength(200)] public string? Zone { get; set; }
    [MaxLength(200)] public string? Area { get; set; }
    [MaxLength(200)] public string? Train { get; set; }

    public DateTime? DateOfInspection { get; set; }
    [MaxLength(200)] public string? Inspector { get; set; }
    [MaxLength(200)] public string? Condition { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }

    [MaxLength(500)]
    public string? PicturePath { get; set; }

    public List<AssetItemRemarks> AssetItemRemarks { get; set; }
    public List<AssetItemDocuments> AssetItemDocuments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    public int? PlantId { get; set; }
    public Plant Plant { get; set; }

    public static string GenerateAssetId(
        int? plantCode, string equipmentCode, int? equipmentOrder,
        string civilAssetCode, int? civilAssetOrder) =>
        $"{plantCode}{equipmentCode}-{equipmentOrder}/{civilAssetCode}-{civilAssetOrder}";
}
