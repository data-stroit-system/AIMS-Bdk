using System.Text.Json;
using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.AssetItems;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AssetItemService _assetItemService;
    private readonly PlantService _plantService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private const int PageSize = 10;

    public IndexModel(
        AssetItemService assetItemService,
        PlantService plantService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _assetItemService = assetItemService;
        _plantService = plantService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public List<AssetItem> AssetItems { get; set; } = new();
    public Dictionary<int, Plant> PlantsById { get; set; } = new();
    public Plant? FilterPlant { get; set; }
    public AssetItem? SelectedAsset { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

    public string QgisServerUrl { get; private set; } = string.Empty;
    public string MapProject { get; private set; } = string.Empty;
    public string SearchTermsJson { get; private set; } = "[]";

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PlantId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? AssetId { get; set; }

    // [FromQuery] is required here: 'page' is a reserved Razor Pages route value
    // (it names the page path), so plain parameter binding resolves the route
    // value ("/AssetItems/Index") instead of the query string and always falls
    // back to the default 1.
    public async Task OnGetAsync([FromQuery] int page = 1)
    {
        CurrentPage = page < 1 ? 1 : page;

        QgisServerUrl = _configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        MapProject = _configuration["QgisServer:MapProject"] ?? "/home/deli/ProjectPelatihan/Ortho Project_QGS.qgs";

        if (PlantId.HasValue)
            FilterPlant = await _plantService.GetByIdAsync(PlantId.Value);

        if (AssetId.HasValue)
            SelectedAsset = await _assetItemService.GetByIdAsync(AssetId.Value);

        // Candidate search terms for the Site Map auto-zoom: the asset tag first
        // (falls through to the parent plant's terms when no layer matches it),
        // then the plant name and code. First term matching a WMS layer wins.
        var mapSearchTerms = new List<string>();
        if (SelectedAsset != null && !string.IsNullOrWhiteSpace(SelectedAsset.AssetId))
            mapSearchTerms.Add(SelectedAsset.AssetId);
        if (FilterPlant != null)
        {
            if (!string.IsNullOrWhiteSpace(FilterPlant.Name))
                mapSearchTerms.Add(FilterPlant.Name);
            if (FilterPlant.Code.HasValue)
                mapSearchTerms.Add(FilterPlant.Code.Value.ToString());
        }
        SearchTermsJson = JsonSerializer.Serialize(mapSearchTerms);

        PlantsById = (await _plantService.ListAsync()).ToDictionary(p => p.Id, p => p);

        var (items, totalCount) = await _assetItemService.GetPagedAsync(
            SearchTerm, StatusFilter, CurrentPage, PageSize, PlantId);

        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        TotalPages = TotalPages < 1 ? 1 : TotalPages;

        // A too-large ?page= must show the last page's rows, not an empty result.
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
            (items, _) = await _assetItemService.GetPagedAsync(
                SearchTerm, StatusFilter, CurrentPage, PageSize, PlantId);
        }

        AssetItems = items;
    }

    // Same WMS GetFeatureInfo proxy as MapDemo/Index — the Site Map partial
    // calls ?handler=FeatureInfo on this page when a popup is requested.
    public async Task<IActionResult> OnGetFeatureInfoAsync(
        string layers, string bbox, int width, int height, int i, int j)
    {
        var serverUrl = _configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        // In production ServerUrl is the same-origin path "/qgisserver" (proxied
        // by nginx so WMS requests aren't mixed content on https) — HttpClient
        // needs an absolute URL, so resolve it against the current request.
        if (serverUrl.StartsWith('/'))
            serverUrl = $"{Request.Scheme}://{Request.Host}{serverUrl}";
        var mapProject = _configuration["QgisServer:MapProject"] ?? "/home/deli/ProjectPelatihan/Ortho Project_QGS.qgs";

        var url = serverUrl
            + $"?MAP={Uri.EscapeDataString(mapProject)}"
            + "&SERVICE=WMS&VERSION=1.3.0&REQUEST=GetFeatureInfo"
            + "&CRS=EPSG:4326"
            + "&INFO_FORMAT=text/plain"
            + $"&LAYERS={Uri.EscapeDataString(layers)}"
            + $"&QUERY_LAYERS={Uri.EscapeDataString(layers)}"
            + $"&BBOX={bbox}"
            + $"&WIDTH={width}&HEIGHT={height}"
            + $"&I={i}&J={j}";

        try
        {
            var client = _httpClientFactory.CreateClient();
            var text = await client.GetStringAsync(url);
            return Content(text, "text/plain");
        }
        catch
        {
            return Content(string.Empty, "text/plain");
        }
    }
}
