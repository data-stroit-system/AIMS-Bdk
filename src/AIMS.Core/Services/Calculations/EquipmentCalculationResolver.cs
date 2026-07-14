using AIMS.Core.Entities;
using System;
using System.Collections.Generic;

namespace AIMS.Core.Services.Calculations;

/// <summary>
/// Picks the <see cref="IEquipmentCalculation"/> for an asset's EquipmentCode,
/// falling back to <see cref="DefaultEquipmentCalculation"/> (empty code) when no
/// dedicated strategy exists. The injected collection comes from Autofac's
/// assembly scan, already wrapped in any registered decorators.
/// </summary>
public interface IEquipmentCalculationResolver
{
    IEquipmentCalculation Resolve(string? equipmentCode);

    EquipmentCalculationResult Calculate(AssetItem item);
}

public sealed class EquipmentCalculationResolver : IEquipmentCalculationResolver
{
    private readonly Dictionary<string, IEquipmentCalculation> _byCode;
    private readonly IEquipmentCalculation _fallback;

    public EquipmentCalculationResolver(IEnumerable<IEquipmentCalculation> calculations)
    {
        _byCode = new Dictionary<string, IEquipmentCalculation>(StringComparer.OrdinalIgnoreCase);
        foreach (var calculation in calculations)
        {
            // Last registration wins on duplicates, mirroring Autofac's default.
            _byCode[calculation.EquipmentCode] = calculation;
        }

        if (!_byCode.TryGetValue(string.Empty, out var fallback))
        {
            throw new InvalidOperationException(
                $"No fallback {nameof(IEquipmentCalculation)} registered (implementation with an empty EquipmentCode).");
        }
        _fallback = fallback;
    }

    public IEquipmentCalculation Resolve(string? equipmentCode) =>
        equipmentCode != null && _byCode.TryGetValue(equipmentCode, out var calculation)
            ? calculation
            : _fallback;

    public EquipmentCalculationResult Calculate(AssetItem item) =>
        Resolve(item.EquipmentCode).Calculate(item);
}
