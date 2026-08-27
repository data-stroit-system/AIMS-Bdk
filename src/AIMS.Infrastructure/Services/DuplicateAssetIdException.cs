using System;

namespace AIMS.Infrastructure.Services;

/// <summary>
/// Thrown when an Asset Tag No. generated for a create/update already belongs to
/// another asset item — the write is refused before reaching the database.
/// </summary>
public sealed class DuplicateAssetIdException : Exception
{
    public DuplicateAssetIdException(string assetId)
        : base($"An asset item with tag '{assetId}' already exists.")
    {
        AssetId = assetId;
    }

    public string AssetId { get; }
}
