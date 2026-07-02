using AIMS.Core.Entities;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AIMS.WebFrontend.Pages.Plants;

[Authorize(Roles = "Admin,Manager")]
public class CreateModel : PageModel
{
    private readonly PlantService _plantService;
    private readonly IActivityLogger _activityLogger;

    public CreateModel(PlantService plantService, IActivityLogger activityLogger)
    {
        _plantService = plantService;
        _activityLogger = activityLogger;
    }

    [BindProperty]
    public PlantInput Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var newId = await _plantService.CreateAsync(new Plant
        {
            Code = Input.Code,
            Name = Input.Name,
            Description = Input.Description
        });

        await _activityLogger.LogActivityAsync(
            "PlantCreated",
            $"Plant '{Input.Name}' created",
            "Plant",
            newId.ToString());

        return RedirectToPage("Index");
    }

    public class PlantInput
    {
        [StringLength(20)]
        public string? Code { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }
    }
}
