using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages;

/// <summary>
/// Anonymous QR tag scanner: the camera box decodes the Asset Tag No. printed on asset
/// QR codes, then the button opens the matching anonymous asset page (/asset/{tag}).
/// Deliberately no [Authorize] — field staff scan tags without logging in.
/// </summary>
public class ScannerModel : PageModel
{
    public void OnGet()
    {
    }
}
