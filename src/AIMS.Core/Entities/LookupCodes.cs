#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace AIMS.Core.Entities;

public sealed class AssetCode
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public static readonly IReadOnlyList<AssetCode> All =
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
        new() { Code = "M",  Description = "Structure" },
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
        new() { Code = "Q",  Description = "Foundation" },
        new() { Code = "R",  Description = "Buildings" },
        new() { Code = "S",  Description = "Site Improvement" },
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

    public static AssetCode? Find(string code) =>
        All.FirstOrDefault(e => e.Code == code);
}

public sealed class PlantCode
{
    public int Code { get; init; }
    public string Description { get; init; } = string.Empty;

    public static readonly IReadOnlyList<PlantCode> All =
    [
        new() { Code = 1,  Description = "Gas Purification Section" },
        new() { Code = 2,  Description = "Dehydration Section" },
        new() { Code = 3,  Description = "Fractionation Section" },
        new() { Code = 4,  Description = "Refrigeration Section" },
        new() { Code = 5,  Description = "Liquifaction Section" },
        new() { Code = 6,  Description = "Condensate Stripping Section" },
        new() { Code = 15, Description = "LPG Section" },
        new() { Code = 16, Description = "Condensate Stabilizer Section" },
        new() { Code = 17, Description = "LPG Storage and Loading" },
        new() { Code = 19, Description = "Relief & Blowdown System" },
        new() { Code = 20, Description = "Condensate Storage and Process LPG Storage" },
        new() { Code = 21, Description = "Feed Gas Knockout Drum" },
        new() { Code = 22, Description = "Marine Structures" },
        new() { Code = 24, Description = "LNG Storage and Loading Facility" },
        new() { Code = 25, Description = "Fueling Facility" },
        new() { Code = 26, Description = "LPG Filling Station for Propane and Butane" },
        new() { Code = 27, Description = "Oxygen Plant" },
        new() { Code = 28, Description = "Acetylene Plant" },
        new() { Code = 29, Description = "Nitrogen Generation" },
        new() { Code = 30, Description = "Electrical Distribution Facilities" },
        new() { Code = 31, Description = "Steam & Power Generation" },
        new() { Code = 32, Description = "Seawater Cooling System" },
        new() { Code = 33, Description = "Fire Protection System" },
        new() { Code = 34, Description = "Liquid Waste System" },
        new() { Code = 35, Description = "Compressed Air System" },
        new() { Code = 36, Description = "Water Treatment System" },
        new() { Code = 37, Description = "Communication" },
        new() { Code = 38, Description = "Inter-connecting Pipeways" },
        new() { Code = 39, Description = "Nitrogen Plant (Expansion)" },
        new() { Code = 40, Description = "Site Preparation" },
        new() { Code = 41, Description = "Buildings" },
        new() { Code = 42, Description = "Building Equipment" },
        new() { Code = 43, Description = "Tug Boats" },
        new() { Code = 48, Description = "Community Facilities" },
        new() { Code = 49, Description = "Community Facilities" },
        new() { Code = 50, Description = "Plot Plans" },
        new() { Code = 51, Description = "Mobile Equipment" },
        new() { Code = 52, Description = "Distributed Control System" },
        new() { Code = 53, Description = "Machinery Monitoring System" },
        new() { Code = 55, Description = "Maps" },
        new() { Code = 57, Description = "New 36 inch Pipeline" },
        new() { Code = 58, Description = "36 inch pipeline" },
        new() { Code = 59, Description = "42 inch pipeline" },
        new() { Code = 60, Description = "20 inch pipeline" },
        new() { Code = 61, Description = "16 inch pipeline" },
        new() { Code = 62, Description = "16 inch pipeline" },
        new() { Code = 63, Description = "6 inch pipeline" },
        new() { Code = 64, Description = "Water Recreation Facilities" },
        new() { Code = 70, Description = "Outside PT. Badak NGL Area" },
    ];

    public static string? GetDescription(int code) =>
        All.FirstOrDefault(p => p.Code == code)?.Description;

    public static PlantCode? Find(int code) =>
        All.FirstOrDefault(p => p.Code == code);
}
