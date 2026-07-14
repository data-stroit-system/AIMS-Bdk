using AIMS.Core.Entities;

namespace AIMS.Core.Services.Calculations;

// NOTE: the interval values below are placeholders to demonstrate the pattern —
// replace them with the real engineering values per equipment type.

/// <summary>Tanks ("D").</summary>
public sealed class TankCalculation : IEquipmentCalculation
{
    public string EquipmentCode => "D";

    public EquipmentCalculationResult Calculate(AssetItem item)
    {
        const int intervalYears = 5;
        return new EquipmentCalculationResult
        {
            InspectionIntervalYears = intervalYears,
            NextInspectionDue = item.DateOfInspection?.AddYears(intervalYears)
        };
    }
}

/// <summary>Pressure Vessels ("C").</summary>
public sealed class PressureVesselCalculation : IEquipmentCalculation
{
    public string EquipmentCode => "C";

    public EquipmentCalculationResult Calculate(AssetItem item)
    {
        const int intervalYears = 3;
        return new EquipmentCalculationResult
        {
            InspectionIntervalYears = intervalYears,
            NextInspectionDue = item.DateOfInspection?.AddYears(intervalYears)
        };
    }
}

/// <summary>
/// Fallback for equipment codes without a dedicated implementation. Identified by
/// the empty <see cref="EquipmentCode"/>.
/// </summary>
public sealed class DefaultEquipmentCalculation : IEquipmentCalculation
{
    public string EquipmentCode => string.Empty;

    public EquipmentCalculationResult Calculate(AssetItem item)
    {
        const int intervalYears = 10;
        return new EquipmentCalculationResult
        {
            InspectionIntervalYears = intervalYears,
            NextInspectionDue = item.DateOfInspection?.AddYears(intervalYears)
        };
    }
}
