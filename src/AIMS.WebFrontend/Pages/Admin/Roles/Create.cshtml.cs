using System.ComponentModel.DataAnnotations;
using AIMS.Core.Entities;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Admin.Roles;

[Authorize(Roles = "Admin")]
public class CreateModel : PageModel
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IActivityLogger _activityLogger;

    public CreateModel(RoleManager<ApplicationRole> roleManager, IActivityLogger activityLogger)
    {
        _roleManager = roleManager;
        _activityLogger = activityLogger;
    }

    [BindProperty]
    public CreateRoleInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var roleExists = await _roleManager.RoleExistsAsync(Input.Name);
        if (roleExists)
        {
            ModelState.AddModelError("Input.Name", "A role with this name already exists.");
            return Page();
        }

        var role = new ApplicationRole
        {
            Name = Input.Name,
            Description = Input.Description ?? string.Empty
        };

        var result = await _roleManager.CreateAsync(role);

        if (result.Succeeded)
        {
            // Log role creation activity
            await _activityLogger.LogActivityAsync(
                ActivityType.RoleCreated,
                $"Created new role '{role.Name}'",
                "ApplicationRole",
                role.Id,
                "Success");

            TempData["Success"] = $"Role '{role.Name}' has been created successfully.";
            return RedirectToPage("Index");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return Page();
    }
}

public class CreateRoleInput
{
    [Required]
    [MaxLength(256)]
    [Display(Name = "Role Name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}
