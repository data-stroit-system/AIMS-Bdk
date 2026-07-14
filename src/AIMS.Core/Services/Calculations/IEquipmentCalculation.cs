using AIMS.Core.Entities;
using System;

namespace AIMS.Core.Services.Calculations;

/// <summary>
/// Per-EquipmentCode calculation strategy. Each equipment type (Tanks, Pressure
/// Vessels, ...) can supply its own implementation of the calculated fields; add a
/// new implementation in this namespace and it is picked up automatically by the
/// Autofac assembly scan in AIMS.Infrastructure (CalculationsModule) — no explicit
/// registration needed. Cross-cutting rules are layered on top as decorators (see
/// <see cref="ConditionAdjustedCalculation"/>).
/// </summary>
public interface IEquipmentCalculation
{
    /// <summary>
    /// The <see cref="Entities.EquipmentCode"/>.Code this strategy applies to
    /// (e.g. "D" = Tanks). Empty string marks the fallback used for codes without
    /// a dedicated implementation.
    /// </summary>
    string EquipmentCode { get; }

    EquipmentCalculationResult Calculate(AssetItem item);
}

public sealed class EquipmentCalculationResult
{
    /// <summary>How often this equipment type should get a GVI, in years.</summary>
    public int InspectionIntervalYears { get; init; }

    /// <summary>Last inspection + interval; null when the asset was never inspected.</summary>
    public DateTime? NextInspectionDue { get; init; }
}
