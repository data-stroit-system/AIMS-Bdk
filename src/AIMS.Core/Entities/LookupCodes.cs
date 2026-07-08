using System.Collections.Generic;
using System.Linq;

namespace AIMS.Core.Entities;

public sealed class EquipmentCode
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public static readonly IReadOnlyList<EquipmentCode> All =
    [
        new() { Code = "A",  Description = "General" },
        new() { Code = "B",  Description = "Process" },
        new() { Code = "C",  Description = "Pressure Vessels" },
        new() { Code = "D",  Description = "Tanks" },
        new() { Code = "E",  Description = "Heat Exchangers" },
        new() { Code = "EM", Description = "Fan Motor" },
        new() { Code = "F",  Description = "Fired Heaters" },
        new() { Code = "FX", Description = "Flame Ignitor" },
        new() { Code = "G",  Description = "Pumps" },
        new() { Code = "GA", Description = "Air Motor Driver for Pump" },
        new() { Code = "GE", Description = "Diesel Engine for Pump" },
        new() { Code = "GM", Description = "Electric Motor Driver for Pump" },
        new() { Code = "GT", Description = "Gas Turbine" },
        new() { Code = "H",  Description = "Vacuum" },
        new() { Code = "J",  Description = "Instruments" },
        new() { Code = "KE", Description = "Diesel Engine Driver for Compressor" },
        new() { Code = "KG", Description = "Gear" },
        new() { Code = "KM", Description = "Electric Motor for Compressor" },
        new() { Code = "KT", Description = "Gas Turbine Driver for Compressor" },
        new() { Code = "KTM",Description = "Turning Motor for Compressor" },
        new() { Code = "KX", Description = "Clutch" },
        new() { Code = "L",  Description = "Piping" },
        new() { Code = "LM", Description = "Motor for Valves" },
        new() { Code = "N",  Description = "Insulation" },
        new() { Code = "P",  Description = "Electrical" },
        new() { Code = "PD", Description = "Batteries" },
        new() { Code = "PE", Description = "Diesel Engine Driver for Turbine Generator" },
        new() { Code = "PG", Description = "Steam and Gas Turbine Generators" },
        new() { Code = "PGR",Description = "Ground System" },
        new() { Code = "PH", Description = "Electric Heater" },
        new() { Code = "PM", Description = "Motor Control Center for Switchgear" },
        new() { Code = "PS", Description = "Switchgear" },
        new() { Code = "PSW",Description = "Primary Switch" },
        new() { Code = "PT", Description = "Transformer" },
        new() { Code = "PU", Description = "UPS Units" },
        new() { Code = "PUT",Description = "UPS Transformer" },
        new() { Code = "PY", Description = "Battery Charger" },
        new() { Code = "T",  Description = "Material Handling Equipment" },
        new() { Code = "TM", Description = "Electric Motor for Material Handling Equipment" },
        new() { Code = "TX", Description = "Bearing for Material Handling Equipment" },
        new() { Code = "U",  Description = "Expandables" },
        new() { Code = "V",  Description = "Package Units" },
        new() { Code = "VG", Description = "Pump for Package Unit" },
        new() { Code = "VO", Description = "Valve Operators" },
        new() { Code = "VM", Description = "Electric Motor for Package Units" },
        new() { Code = "W",  Description = "Welding and Metal Processing" },
        new() { Code = "X",  Description = "Painting" },
        new() { Code = "Y",  Description = "Processing" },
        new() { Code = "YM", Description = "Electric Motor for Processing Equipment" },
        new() { Code = "YX", Description = "Cylinder Positioner Driver for Starting Air System" },
        new() { Code = "Z",  Description = "Water Treating Equipment" },
    ];

    public static string? GetDescription(string code) =>
        All.FirstOrDefault(e => e.Code == code)?.Description;

    public static EquipmentCode? Find(string code) =>
        All.FirstOrDefault(e => e.Code == code);
}

public sealed class CivilAssetCode
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public static readonly IReadOnlyList<CivilAssetCode> All =
    [
        new() { Code = "M", Description = "Structure" },
        new() { Code = "Q", Description = "Foundation" },
        new() { Code = "R", Description = "Buildings" },
        new() { Code = "S", Description = "Site Improvement" },
    ];

    public static string? GetDescription(string code) =>
        All.FirstOrDefault(c => c.Code == code)?.Description;

    public static CivilAssetCode? Find(string code) =>
        All.FirstOrDefault(c => c.Code == code);
}
