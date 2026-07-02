using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Plants;

[Authorize]
public class IndexModel : PageModel
{
    private readonly PlantService _plantService;
    private readonly IActivityLogger _activityLogger;

    public IndexModel(PlantService plantService, IActivityLogger activityLogger)
    {
        _plantService = plantService;
        _activityLogger = activityLogger;
    }

    public List<Plant> Plants { get; set; } = new();
    public Dictionary<int, int> AssetCounts { get; set; } = new();

    public async Task OnGetAsync()
    {
        Plants = await _plantService.ListAsync();
        AssetCounts = await _plantService.GetAssetCountsAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            return Forbid();

        var plant = await _plantService.DeleteAsync(id);
        if (plant == null) return NotFound();

        await _activityLogger.LogActivityAsync(
            "PlantDeleted",
            $"Plant '{plant.Name}' deleted",
            "Plant",
            id.ToString());

        return RedirectToPage();
    }
}
