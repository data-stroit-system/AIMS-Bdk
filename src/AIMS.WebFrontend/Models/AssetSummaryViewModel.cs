using AIMS.Core.Entities;

namespace AIMS.WebFrontend.Models;

/// <summary>
/// Model for the shared asset-summary partial (the "Asset Details" right panel on
/// the Asset Items page, also rendered standalone by the anonymous /asset/{tag} page).
/// </summary>
public sealed class AssetSummaryViewModel
{
    public required AssetItem Asset { get; init; }

    public Plant? Plant { get; init; }

    /// <summary>Hides the QR-code button and the "Open Asset Details" footer link, which target login-protected pages.</summary>
    public bool ShowActions { get; init; } = true;
}
