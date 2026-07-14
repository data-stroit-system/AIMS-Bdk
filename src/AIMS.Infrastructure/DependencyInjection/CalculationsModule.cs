using AIMS.Core.Services.Calculations;
using Autofac;
using Module = Autofac.Module;

namespace AIMS.Infrastructure.DependencyInjection;

/// <summary>
/// Autofac wiring for the per-EquipmentCode calculation strategies in AIMS.Core.
///
/// - Assembly-scans AIMS.Core for <see cref="IEquipmentCalculation"/> implementations,
///   so adding a new equipment type calculation (e.g. HeatExchangerCalculation) in
///   Core is all that's needed — no registration edits here.
/// - Applies <see cref="ConditionAdjustedCalculation"/> as a decorator over every
///   strategy (it is excluded from the scan so it doesn't register as a standalone
///   strategy and decorate itself).
///
/// Registered from Program.cs via builder.Host.ConfigureContainer — plain
/// IServiceCollection registrations are Populate()d into the same container by
/// AutofacServiceProviderFactory, so both styles coexist.
/// </summary>
public sealed class CalculationsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var coreAssembly = typeof(IEquipmentCalculation).Assembly;

        builder.RegisterAssemblyTypes(coreAssembly)
            .Where(t => typeof(IEquipmentCalculation).IsAssignableFrom(t))
            .Except<ConditionAdjustedCalculation>()
            .As<IEquipmentCalculation>()
            .SingleInstance(); // strategies are stateless

        builder.RegisterDecorator<ConditionAdjustedCalculation, IEquipmentCalculation>();

        builder.RegisterType<EquipmentCalculationResolver>()
            .As<IEquipmentCalculationResolver>()
            .SingleInstance();
    }
}
