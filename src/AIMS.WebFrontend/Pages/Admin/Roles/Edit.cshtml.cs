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
public class EditModel : PageModel
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IActivityLogger _activityLogger;

    public EditModel(
        RoleManager<ApplicationRole> roleManager, 
        UserManager<ApplicationUser> userManager,
        IActivityLogger activityLogger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _activityLogger = activityLogger;
    }

    [BindProperty]
    public EditRoleInput Input { get; set; } = new();

    public List<ApplicationUser> UsersInRole { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null)
        {
            return NotFound();
        }

        Input = new EditRoleInput
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            Description = role.Description
        };

        UsersInRole = (await _userManager.GetUsersInRoleAsync(role.Name!)).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            var role = await _roleManager.FindByIdAsync(Input.Id);
            if (role != null)
            {
                UsersInRole = (await _userManager.GetUsersInRoleAsync(role.Name!)).ToList();
            }
            return Page();
        }

        var existingRole = await _roleManager.FindByIdAsync(Input.Id);
        if (existingRole == null)
        {
            return NotFound();
        }

        // Prevent renaming Admin role
        if (existingRole.Name == "Admin" && Input.Name != "Admin")
        {
            ModelState.AddModelError("Input.Name", "The Admin role cannot be renamed.");
            UsersInRole = (await _userManager.GetUsersInRoleAsync(existingRole.Name!)).ToList();
            return Page();
        }

        // Check if new name already exists (if name is being changed)
        if (existingRole.Name != Input.Name)
        {
            var roleWithSameName = await _roleManager.FindByNameAsync(Input.Name);
            if (roleWithSameName != null)
            {
                ModelState.AddModelError("Input.Name", "A role with this name already exists.");
                UsersInRole = (await _userManager.GetUsersInRoleAsync(existingRole.Name!)).ToList();
                return Page();
            }
        }

        existingRole.Name = Input.Name;
        existingRole.Description = Input.Description ?? string.Empty;

        var result = await _roleManager.UpdateAsync(existingRole);

        if (result.Succeeded)
        {
            // Log role update activity
            await _activityLogger.LogActivityAsync(
                ActivityType.RoleUpdated,
                $"Updated role '{existingRole.Name}'",
                "ApplicationRole",
                existingRole.Id,
                "Success");

            TempData["Success"] = $"Role '{existingRole.Name}' has been updated successfully.";
            return RedirectToPage("Index");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        UsersInRole = (await _userManager.GetUsersInRoleAsync(existingRole.Name!)).ToList();
        return Page();
    }
}

public class EditRoleInput
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    [Display(Name = "Role Name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}
