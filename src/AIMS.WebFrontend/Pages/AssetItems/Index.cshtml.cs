using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
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
    // Browser-facing QGIS URL (falls back to ServerUrl — see MapDemo/IndexModel).
    public string QgisBrowserUrl { get; private set; } = string.Empty;
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
        QgisBrowserUrl = _configuration["QgisServer:BrowserUrl"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(QgisBrowserUrl))
            QgisBrowserUrl = QgisServerUrl;

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

    // Live-lookup of an asset's center point. The QGIS project holds one asset
    // point layer inside each plant group (e.g. "Point Aset Plant 17" under
    // group "Plan 17"), so the layer is resolved dynamically from the plant
    // code/name via WMS GetCapabilities — never hardcoded. WFS GetFeature on
    // that layer is then filtered by the asset tag; the layer's NAME field
    // carries the tag with a sequence prefix ("(04) 17D-4-Q"), so match by
    // suffix (which also covers an exact match). Returns
    // {found, lat, lng, name, layer} as JSON.
    public async Task<IActionResult> OnGetAssetPointAsync(string tag, string? plantCode, string? plantName)
    {
        var serverUrl = _configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        if (serverUrl.StartsWith('/'))
            serverUrl = $"{Request.Scheme}://{Request.Host}{serverUrl}";
        var mapProject = _configuration["QgisServer:MapProject"] ?? "/home/deli/Sample Citra/Project Sample format qgs.qgs";

        string? layer = null;
        try
        {
            var capsXml = await GetWmsCapabilitiesAsync(serverUrl, mapProject);
            layer = ResolveAssetPointLayer(capsXml, plantCode, plantName);
            if (layer == null)
                return new JsonResult(new { found = false, layer = (string?)null });

            // QGIS Server names WFS feature types by the layer name with
            // spaces turned into underscores ("Point Aset Plant 17" →
            // "Point_Aset_Plant_17"), and this server build ignores
            // CQL_FILTER — use the OGC XML FILTER instead. The NAME field
            // carries the tag with a sequence prefix ("(04) 17D-4-Q"), so
            // match by suffix with PropertyIsLike.
            var typeName = layer.Replace(' ', '_');
            var filterXml = "<ogc:Filter xmlns:ogc=\"http://www.opengis.net/ogc\">"
                + "<ogc:PropertyIsLike wildCard=\"*\" singleChar=\".\" escapeChar=\"!\">"
                + "<ogc:PropertyName>NAME</ogc:PropertyName>"
                + $"<ogc:Literal>*{System.Security.SecurityElement.Escape(tag)}</ogc:Literal>"
                + "</ogc:PropertyIsLike></ogc:Filter>";
            var url = serverUrl
                + $"?MAP={Uri.EscapeDataString(mapProject)}"
                + "&SERVICE=WFS&VERSION=1.1.0&REQUEST=GetFeature"
                + $"&TYPENAME={Uri.EscapeDataString(typeName)}"
                + "&OUTPUTFORMAT=application/vnd.geo+json"
                + $"&FILTER={Uri.EscapeDataString(filterXml)}";

            var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
                return new JsonResult(new { found = false, layer });

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geom)
                    || geom.ValueKind != JsonValueKind.Object
                    || !geom.TryGetProperty("coordinates", out var coords)
                    || coords.ValueKind != JsonValueKind.Array
                    || coords.GetArrayLength() < 2)
                    continue;

                var lng = coords[0].GetDouble();
                var lat = coords[1].GetDouble();
                var name = tag;
                if (feature.TryGetProperty("properties", out var props)
                    && props.ValueKind == JsonValueKind.Object
                    && props.TryGetProperty("NAME", out var nameEl)
                    && nameEl.ValueKind == JsonValueKind.String)
                    name = nameEl.GetString() ?? tag;

                return new JsonResult(new { found = true, lat, lng, name, layer });
            }

            return new JsonResult(new { found = false, layer });
        }
        catch
        {
            // WFS may be unpublished on the QGIS project (GetFeature then
            // returns a ServiceException XML) or the server unreachable —
            // report not-found so the client falls back to layer search.
            return new JsonResult(new { found = false, layer });
        }
    }

    // WMS GetCapabilities, cached briefly — every point lookup needs it to
    // resolve the plant group's point layer, and the layer tree changes rarely.
    private static readonly object CapsCacheLock = new();
    private static string? CapsCacheXml;
    private static DateTime CapsCacheFetchedUtc = DateTime.MinValue;
    private static readonly TimeSpan CapsCacheTtl = TimeSpan.FromSeconds(60);

    private async Task<string> GetWmsCapabilitiesAsync(string serverUrl, string mapProject)
    {
        lock (CapsCacheLock)
        {
            if (CapsCacheXml != null && DateTime.UtcNow - CapsCacheFetchedUtc < CapsCacheTtl)
                return CapsCacheXml;
        }

        var url = serverUrl
            + $"?MAP={Uri.EscapeDataString(mapProject)}"
            + "&SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities";
        var client = _httpClientFactory.CreateClient();
        var caps = await client.GetStringAsync(url);

        lock (CapsCacheLock)
        {
            CapsCacheXml = caps;
            CapsCacheFetchedUtc = DateTime.UtcNow;
        }
        return caps;
    }

    // Finds the asset point layer inside the plant group. Groups are named
    // after the plant ("Plan 17", "Plant 18", ...) — match by numeric code
    // token; the point layer is the group's child whose name contains "Point"
    // (or whose geometryType is Point).
    private static string? ResolveAssetPointLayer(string capsXml, string? plantCode, string? plantName)
    {
        var doc = new XmlDocument();
        doc.LoadXml(capsXml);
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("wms", "http://www.opengis.net/wms");

        foreach (XmlElement group in doc.SelectNodes("//wms:Layer[wms:Layer]", nsm)!)
        {
            var groupName = group.SelectSingleNode("wms:Name", nsm)?.InnerText ?? "";
            if (!GroupMatchesPlant(groupName, plantCode, plantName)) continue;

            foreach (XmlElement child in group.SelectNodes("wms:Layer", nsm)!)
            {
                var childName = child.SelectSingleNode("wms:Name", nsm)?.InnerText ?? "";
                var geometryType = child.GetAttribute("geometryType");
                if (childName.Contains("Point", StringComparison.OrdinalIgnoreCase)
                    || geometryType.StartsWith("Point", StringComparison.OrdinalIgnoreCase))
                    return childName;
            }
        }
        return null;
    }

    private static bool GroupMatchesPlant(string groupName, string? plantCode, string? plantName)
    {
        var tokens = Regex.Split(groupName.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(plantCode))
        {
            var code = plantCode.Trim().ToLowerInvariant();
            if (tokens.Any(t => t == code || t == "plant" + code || t == "plan" + code))
                return true;
        }

        // No code available — fall back to shared tokens with the plant name.
        if (string.IsNullOrWhiteSpace(plantCode) && !string.IsNullOrWhiteSpace(plantName))
        {
            var nameTokens = Regex.Split(plantName.ToLowerInvariant(), "[^a-z0-9]+")
                .Where(t => t.Length > 0)
                .ToArray();
            if (tokens.Any(nameTokens.Contains)) return true;
        }
        return false;
    }
}
