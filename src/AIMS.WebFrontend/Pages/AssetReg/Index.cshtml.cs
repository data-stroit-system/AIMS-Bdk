using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Text;

namespace AIMS.WebFrontend.Pages.AssetReg;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly PlantService _plantService;
    private const int PageSize = 25;

    public IndexModel(AssetItemService assetItemService, PlantService plantService)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
    }

    public List<AssetItem> AssetItems { get; set; } = new();
    public Dictionary<int, Plant> PlantsById { get; set; } = new();
    public List<Plant> Plants { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PlantId { get; set; }

    public async Task OnGetAsync(int page = 1)
    {
        CurrentPage = page < 1 ? 1 : page;

        Plants = await _plantService.ListAsync();
        PlantsById = Plants.ToDictionary(p => p.Id, p => p);

        var (items, totalCount) = await _assetItemService.GetPagedAsync(
            SearchTerm, StatusFilter, CurrentPage, PageSize, PlantId);

        AssetItems = items;
        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        TotalPages = TotalPages < 1 ? 1 : TotalPages;
    }

    /// <summary>
    /// Downloads the whole (filtered) register as a CSV — not just the current page —
    /// with the same columns as the on-screen table.
    /// </summary>
    public async Task<IActionResult> OnGetExportAsync()
    {
        Plants = await _plantService.ListAsync();
        PlantsById = Plants.ToDictionary(p => p.Id, p => p);

        var items = await _assetItemService.GetAllFilteredAsync(SearchTerm, StatusFilter, PlantId);

        var rows = items.Select(item =>
        {
            var plant = item.PlantId.HasValue ? PlantsById.GetValueOrDefault(item.PlantId.Value) : null;
            return new AssetRegisterRow
            {
                RegNo = item.Id.ToString(CultureInfo.InvariantCulture),
                GisRefNo = item.GisRefNo,
                AssetTagNo = item.AssetId,
                AssetDescription = item.Title,
                PlantCode = plant?.Code?.ToString(CultureInfo.InvariantCulture),
                PlantDescription = plant?.Description,
                Zone = item.Zone,
                Area = item.Area,
                Train = item.Train,
                CoordinateN = item.CoordinateN,
                CoordinateE = item.CoordinateE,
                Function = item.Function,
                Material = item.Material,
                YearInstalled = item.YearInstalled?.ToString(CultureInfo.InvariantCulture),
                AgeYears = item.YearInstalled.HasValue
                    ? (DateTime.Now.Year - item.YearInstalled.Value).ToString(CultureInfo.InvariantCulture)
                    : null,
                InspectionDate = item.DateOfInspection?.ToString("dd/MMM/yyyy", CultureInfo.InvariantCulture),
                Condition = item.Condition,
            };
        }).ToList();

        var bytes = WriteCsv(rows);
        return File(bytes, "text/csv", $"AssetRegister_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    private static byte[] WriteCsv(IEnumerable<AssetRegisterRow> rows)
    {
        using var stream = new MemoryStream();
        // UTF-8 with BOM so Excel detects the encoding and renders non-ASCII text correctly.
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
        {
            csv.Context.RegisterClassMap(new AssetRegisterRowMap());
            csv.WriteRecords(rows);
        }
        return stream.ToArray();
    }
}

internal sealed class AssetRegisterRow
{
    public string? RegNo { get; set; }
    public string? GisRefNo { get; set; }
    public string? AssetTagNo { get; set; }
    public string? AssetDescription { get; set; }
    public string? PlantCode { get; set; }
    public string? PlantDescription { get; set; }
    public string? Zone { get; set; }
    public string? Area { get; set; }
    public string? Train { get; set; }
    public string? CoordinateN { get; set; }
    public string? CoordinateE { get; set; }
    public string? Function { get; set; }
    public string? Material { get; set; }
    public string? YearInstalled { get; set; }
    public string? AgeYears { get; set; }
    public string? InspectionDate { get; set; }
    public string? Condition { get; set; }
}

internal sealed class AssetRegisterRowMap : ClassMap<AssetRegisterRow>
{
    public AssetRegisterRowMap()
    {
        Map(m => m.RegNo).Name("Reg No.");
        Map(m => m.GisRefNo).Name("GIS Ref. No.");
        Map(m => m.AssetTagNo).Name("Asset Tag No.");
        Map(m => m.AssetDescription).Name("Asset Description");
        Map(m => m.PlantCode).Name("Plant Code");
        Map(m => m.PlantDescription).Name("Plant Description");
        Map(m => m.Zone).Name("Zone");
        Map(m => m.Area).Name("Area");
        Map(m => m.Train).Name("Train");
        Map(m => m.CoordinateN).Name("Coordinate N");
        Map(m => m.CoordinateE).Name("Coordinate E");
        Map(m => m.Function).Name("Function");
        Map(m => m.Material).Name("Material");
        Map(m => m.YearInstalled).Name("Installed");
        Map(m => m.AgeYears).Name("Age (yr)");
        Map(m => m.InspectionDate).Name("Insp. Date");
        Map(m => m.Condition).Name("Condition (GVI)");
    }
}
