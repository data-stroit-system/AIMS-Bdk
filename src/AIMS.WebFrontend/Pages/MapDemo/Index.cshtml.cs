using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.MapDemo;

public class IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration) : PageModel
{
    public string QgisServerUrl { get; private set; } = string.Empty;
    public string MapProject { get; private set; } = string.Empty;
    // Browser-facing QGIS URL. Falls back to ServerUrl when BrowserUrl is
    // unset (e.g. local `dotnet run` with no nginx in front → browser hits
    // the upstream directly; that's fine because the dev page is also
    // plain HTTP, no mixed-content blocking). In prod (deploy.sh sets
    // QgisServer:BrowserUrl=/qgisserver) the browser talks same-origin to
    // nginx, which proxies to the upstream QGIS box internally — avoiding
    // mixed-content on the HTTPS deployment.
    public string QgisBrowserUrl { get; private set; } = string.Empty;

    public void OnGet()
    {
        QgisServerUrl = configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        MapProject = configuration["QgisServer:MapProject"] ?? "/home/deli/OrthoProject1/OrthoProject1.qgs";
        QgisBrowserUrl = configuration["QgisServer:BrowserUrl"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(QgisBrowserUrl))
            QgisBrowserUrl = QgisServerUrl;
    }

    public async Task<IActionResult> OnGetFeatureInfoAsync(
        string layers, string bbox, int width, int height, int i, int j)
    {
        var serverUrl = configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        // In production ServerUrl is the same-origin path "/qgisserver" (proxied
        // by nginx so WMS requests aren't mixed content on https) — HttpClient
        // needs an absolute URL, so resolve it against the current request.
        if (serverUrl.StartsWith('/'))
            serverUrl = $"{Request.Scheme}://{Request.Host}{serverUrl}";
        var mapProject = configuration["QgisServer:MapProject"] ?? "/home/deli/OrthoProject1/OrthoProject1.qgs";

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
            var client = httpClientFactory.CreateClient();
            var text = await client.GetStringAsync(url);
            return Content(text, "text/plain");
        }
        catch
        {
            return Content(string.Empty, "text/plain");
        }
    }
}
