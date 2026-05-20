using AIMS.SharedKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace AIMS.Core.Entities;

public class AssetItemDocuments : BaseEntity
{
    [MaxLength(250)]
    public string DocumentTitle { get; set; }
    [MaxLength(500)]
    public string FilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]
    public string CreatedBy { get; set; }

    public AssetItem AssetItem { get; set; }
}

public class AssetItemRemarks : BaseEntity
{
    [MaxLength(250)]
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]
    public string CreatedBy {get;set; }

    public AssetItem AssetItem { get; set; }

}
public class AssetItem : BaseEntity
{
    [MaxLength(150)] public string Title { get; set; }
    public string AssetId { get; set; } = string.Empty;
    [MaxLength(250)]
    public string Description { get; set; }

    public AssetType Type { get; set; }

    [MaxLength(250)]
    public string Location { get; set; }
    public AssetPriority Priority { get; set; }
    public IntegrityStatus IntegrityStatus { get; set; } 
    public List<AssetItemRemarks> AssetItemRemarks { get; set; }
    public List<AssetItemDocuments> AssetItemDocuments { get; set; }
    [MaxLength(500)]
    public string? PicturePath { get; set; }

    public void UpdateStatus(IntegrityStatus status)
    {
        IntegrityStatus = status;
        Events.Add(new AssetItemStatusUpdateEvent(this));
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)]
    public string CreatedBy { get; set; }
}
public enum IntegrityStatus
{
    Good = 1,
    Fair = 2,
    Poor = 3,
}

public class AssetItemStatusUpdateEvent : BaseDomainEvent
{
    public AssetItem AssetItem { get; }
    public AssetItemStatusUpdateEvent(AssetItem assetItem)
    {
        AssetItem = assetItem;
    }
}
