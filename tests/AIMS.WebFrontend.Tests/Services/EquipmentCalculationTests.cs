using AIMS.Core.Entities;
using AIMS.Core.Services.Calculations;
using AIMS.Infrastructure.DependencyInjection;
using Autofac;

namespace AIMS.WebFrontend.Tests.Services;

public class EquipmentCalculationResolverTests
{
    private static EquipmentCalculationResolver CreateResolver() =>
        new([new TankCalculation(), new PressureVesselCalculation(), new DefaultEquipmentCalculation()]);

    [Fact]
    public void Resolve_TankCode_ReturnsTankCalculation()
    {
        var resolver = CreateResolver();

        Assert.IsType<TankCalculation>(resolver.Resolve("D"));
    }

    [Fact]
    public void Resolve_UnknownOrMissingCode_FallsBackToDefault()
    {
        var resolver = CreateResolver();

        Assert.IsType<DefaultEquipmentCalculation>(resolver.Resolve("ZZ"));
        Assert.IsType<DefaultEquipmentCalculation>(resolver.Resolve(null));
    }

    [Fact]
    public void Constructor_WithoutFallback_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => new EquipmentCalculationResolver([new TankCalculation()]));
    }

    [Fact]
    public void Calculate_Tank_UsesTankIntervalAndInspectionDate()
    {
        var resolver = CreateResolver();
        var inspected = new DateTime(2026, 1, 1);
        var item = new AssetItem { EquipmentCode = "D", DateOfInspection = inspected };

        var result = resolver.Calculate(item);

        Assert.Equal(5, result.InspectionIntervalYears);
        Assert.Equal(inspected.AddYears(5), result.NextInspectionDue);
    }

    [Fact]
    public void Calculate_NeverInspected_HasNoDueDate()
    {
        var result = CreateResolver().Calculate(new AssetItem { EquipmentCode = "D" });

        Assert.Null(result.NextInspectionDue);
    }
}

public class ConditionAdjustedCalculationTests
{
    [Fact]
    public void PoorCondition_HalvesInterval()
    {
        var decorated = new ConditionAdjustedCalculation(new TankCalculation());
        var inspected = new DateTime(2026, 1, 1);
        var item = new AssetItem { EquipmentCode = "D", Condition = "Poor", DateOfInspection = inspected };

        var result = decorated.Calculate(item);

        Assert.Equal(2, result.InspectionIntervalYears); // 5 / 2, floor
        Assert.Equal(inspected.AddYears(2), result.NextInspectionDue);
    }

    [Fact]
    public void FairCondition_ShortensIntervalByOneYear()
    {
        var decorated = new ConditionAdjustedCalculation(new PressureVesselCalculation());

        var result = decorated.Calculate(new AssetItem { Condition = "fair" });

        Assert.Equal(2, result.InspectionIntervalYears); // 3 - 1, case-insensitive
    }

    [Fact]
    public void GoodOrUnknownCondition_LeavesIntervalUnchanged()
    {
        var decorated = new ConditionAdjustedCalculation(new TankCalculation());

        Assert.Equal(5, decorated.Calculate(new AssetItem { Condition = "Good" }).InspectionIntervalYears);
        Assert.Equal(5, decorated.Calculate(new AssetItem()).InspectionIntervalYears);
    }

    [Fact]
    public void IntervalNeverDropsBelowOneYear()
    {
        var decorated = new ConditionAdjustedCalculation(new PressureVesselCalculation());

        var result = decorated.Calculate(new AssetItem { Condition = "Poor" });

        Assert.Equal(1, result.InspectionIntervalYears); // 3 / 2 = 1, clamped
    }
}

/// <summary>
/// Verifies the actual Autofac wiring: assembly scan finds every strategy,
/// the decorator wraps each of them exactly once, and the resolver composes.
/// </summary>
public class CalculationsModuleTests
{
    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<CalculationsModule>();
        return builder.Build();
    }

    [Fact]
    public void ResolvesResolver_WithAllScannedStrategies()
    {
        using var container = BuildContainer();

        var resolver = container.Resolve<IEquipmentCalculationResolver>();

        // One strategy per code from the scan; unknown falls back.
        Assert.Equal("D", resolver.Resolve("D").EquipmentCode);
        Assert.Equal("C", resolver.Resolve("C").EquipmentCode);
        Assert.Equal(string.Empty, resolver.Resolve("ZZ").EquipmentCode);
    }

    [Fact]
    public void EveryStrategy_IsWrappedInConditionDecorator()
    {
        using var container = BuildContainer();

        var strategies = container.Resolve<IEnumerable<IEquipmentCalculation>>().ToList();

        Assert.NotEmpty(strategies);
        Assert.All(strategies, s => Assert.IsType<ConditionAdjustedCalculation>(s));
    }

    [Fact]
    public void DecoratorIsApplied_PoorTankGetsHalvedInterval_ThroughContainer()
    {
        using var container = BuildContainer();
        var resolver = container.Resolve<IEquipmentCalculationResolver>();

        var result = resolver.Calculate(new AssetItem { EquipmentCode = "D", Condition = "Poor" });

        Assert.Equal(2, result.InspectionIntervalYears);
    }
}
