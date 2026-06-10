using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.MapDemo;

public class IndexModel(IHttpClientFactory httpClientFactory) : PageModel
{
    private const string QgisServer = "http://159.223.33.82/qgisserver";
    private const string MapProject = "/home/qgis/projects/world.qgs";

    public void OnGet() { }

    public async Task<IActionResult> OnGetFeatureInfoAsync(
        string layers, string bbox, int width, int height, int i, int j)
    {
        var url = QgisServer
            + $"?MAP={Uri.EscapeDataString(MapProject)}"
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
