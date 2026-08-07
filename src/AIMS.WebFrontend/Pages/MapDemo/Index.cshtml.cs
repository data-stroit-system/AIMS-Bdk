using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.MapDemo;

public class IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration) : PageModel
{
    public string QgisServerUrl { get; private set; } = string.Empty;
    public string MapProject { get; private set; } = string.Empty;

    public void OnGet()
    {
        QgisServerUrl = configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
        MapProject = configuration["QgisServer:MapProject"] ?? "/home/deli/OrthoProject1/OrthoProject1.qgs";
    }

    public async Task<IActionResult> OnGetFeatureInfoAsync(
        string layers, string bbox, int width, int height, int i, int j)
    {
        var serverUrl = configuration["QgisServer:ServerUrl"] ?? "http://192.168.0.8/qgisserver";
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
