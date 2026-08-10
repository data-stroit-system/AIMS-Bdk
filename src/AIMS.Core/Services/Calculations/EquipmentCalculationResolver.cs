using AIMS.Core.Entities;
using System;
using System.Collections.Generic;

namespace AIMS.Core.Services.Calculations;

/// <summary>
/// Picks the <see cref="IAssetCalculation"/> for an asset's AssetCode,
/// falling back to <see cref="DefaultAssetCalculation"/> (empty code) when no
/// dedicated strategy exists. The injected collection comes from Autofac's
/// assembly scan, already wrapped in any registered decorators.
/// </summary>
public interface IAssetCalculationResolver
{
    IAssetCalculation Resolve(string? assetCode);

    AssetCalculationResult Calculate(AssetItem item);
}

public sealed class AssetCalculationResolver : IAssetCalculationResolver
{
    private readonly Dictionary<string, IAssetCalculation> _byCode;
    private readonly IAssetCalculation _fallback;

    public AssetCalculationResolver(IEnumerable<IAssetCalculation> calculations)
    {
        _byCode = new Dictionary<string, IAssetCalculation>(StringComparer.OrdinalIgnoreCase);
        foreach (var calculation in calculations)
        {
            // Last registration wins on duplicates, mirroring Autofac's default.
            _byCode[calculation.AssetCode] = calculation;
        }

        if (!_byCode.TryGetValue(string.Empty, out var fallback))
        {
            throw new InvalidOperationException(
                $"No fallback {nameof(IAssetCalculation)} registered (implementation with an empty AssetCode).");
        }
        _fallback = fallback;
    }

    public IAssetCalculation Resolve(string? assetCode) =>
        assetCode != null && _byCode.TryGetValue(assetCode, out var calculation)
            ? calculation
            : _fallback;

    public AssetCalculationResult Calculate(AssetItem item) =>
        Resolve(item.AssetCode).Calculate(item);
}
