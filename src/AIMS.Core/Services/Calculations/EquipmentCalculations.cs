using AIMS.Core.Entities;

namespace AIMS.Core.Services.Calculations;

// NOTE: the interval values below are placeholders to demonstrate the pattern —
// replace them with the real engineering values per equipment type.

/// <summary>Tanks ("D").</summary>
public sealed class TankCalculation : IAssetCalculation
{
    public string AssetCode => "D";

    public AssetCalculationResult Calculate(AssetItem item)
    {
        const int intervalYears = 5;
        return new AssetCalculationResult
        {
            InspectionIntervalYears = intervalYears,
            NextInspectionDue = item.DateOfInspection?.AddYears(intervalYears)
        };
    }
}

/// <summary>Pressure Vessels ("C").</summary>
public sealed class PressureVesselCalculation : IAssetCalculation
{
    public string AssetCode => "C";

    public AssetCalculationResult Calculate(AssetItem item)
    {
        const int intervalYears = 3;
        return new AssetCalculationResult
        {
            InspectionIntervalYears = intervalYears,
            NextInspectionDue = item.DateOfInspection?.AddYears(intervalYears)
        };
    }
}

/// <summary>
/// Fallback for equipment codes without a dedicated implementation. Identified by
/// the empty <see cref="AssetCode"/>.
/// </summary>
public sealed class DefaultAssetCalculation : IAssetCalculation
{
    public string AssetCode => string.Empty;

    public AssetCalculationResult Calculate(AssetItem item)
    {
        const int intervalYears = 10;
        return new AssetCalculationResult
        {
            InspectionIntervalYears = intervalYears,
            NextInspectionDue = item.DateOfInspection?.AddYears(intervalYears)
        };
    }
}
