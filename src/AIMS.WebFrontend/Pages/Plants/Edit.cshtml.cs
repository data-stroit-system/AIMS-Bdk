using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AIMS.WebFrontend.Pages.Plants;

[Authorize(Roles = "Admin,Manager")]
public class EditModel : PageModel
{
    private readonly PlantService _plantService;
    private readonly IActivityLogger _activityLogger;

    public EditModel(PlantService plantService, IActivityLogger activityLogger)
    {
        _plantService = plantService;
        _activityLogger = activityLogger;
    }

    [BindProperty]
    public PlantInput Input { get; set; } = new();

    public int PlantId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var plant = await _plantService.GetByIdAsync(id);
        if (plant == null) return NotFound();

        PlantId = plant.Id;
        Input = new PlantInput
        {
            Code = plant.Code,
            Name = plant.Name ?? string.Empty,
            Description = plant.Description
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            PlantId = id;
            return Page();
        }

        var plant = await _plantService.GetByIdAsync(id);
        if (plant == null) return NotFound();

        await _plantService.UpdateAsync(id, new Plant
        {
            Code = Input.Code,
            Name = Input.Name,
            Description = Input.Description ?? string.Empty
        });

        await _activityLogger.LogActivityAsync(
            "PlantUpdated",
            $"Plant '{Input.Name}' updated",
            "Plant",
            id.ToString());

        return RedirectToPage("Index");
    }

    public class PlantInput
    {
        public int? Code { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }
    }
}
