using AIMS.Core.Entities;
using System;

namespace AIMS.Core.Services.Calculations;

/// <summary>
/// Decorator applied over every <see cref="IAssetCalculation"/> (wired with
/// Autofac's RegisterDecorator in CalculationsModule): degraded assets get
/// inspected more often regardless of equipment type — Poor halves the base
/// interval, Fair shaves a year off. The equipment-specific strategy stays
/// oblivious to this rule.
/// </summary>
public sealed class ConditionAdjustedCalculation : IAssetCalculation
{
    private readonly IAssetCalculation _inner;

    public ConditionAdjustedCalculation(IAssetCalculation inner)
    {
        _inner = inner;
    }

    public string AssetCode => _inner.AssetCode;

    public AssetCalculationResult Calculate(AssetItem item)
    {
        var result = _inner.Calculate(item);

        var interval = result.InspectionIntervalYears;
        if (string.Equals(item.Condition, "Poor", StringComparison.OrdinalIgnoreCase))
        {
            interval = Math.Max(1, interval / 2);
        }
        else if (string.Equals(item.Condition, "Fair", StringComparison.OrdinalIgnoreCase))
        {
            interval = Math.Max(1, interval - 1);
        }

        if (interval == result.InspectionIntervalYears)
        {
            return result;
        }

        return new AssetCalculationResult
        {
            InspectionIntervalYears = interval,
            NextInspectionDue = item.DateOfInspection?.AddYears(interval)
        };
    }
}
