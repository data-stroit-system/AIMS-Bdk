using AIMS.Core.Entities;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Admin.Roles;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IActivityLogger _activityLogger;

    public IndexModel(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IActivityLogger activityLogger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _activityLogger = activityLogger;
    }

    public List<RoleViewModel> Roles { get; set; } = new();

    public async Task OnGetAsync()
    {
        var roles = _roleManager.Roles.OrderBy(r => r.Name).ToList();

        foreach (var role in roles)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            Roles.Add(new RoleViewModel
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Description = role.Description,
                UserCount = usersInRole.Count
            });
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null)
        {
            TempData["Error"] = "Role not found.";
            return RedirectToPage();
        }

        if (role.Name == "Admin")
        {
            TempData["Error"] = "Cannot delete the Admin role.";
            return RedirectToPage();
        }

        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Any())
        {
            TempData["Error"] = $"Cannot delete role '{role.Name}' because it has {usersInRole.Count} user(s) assigned.";
            return RedirectToPage();
        }

        var roleName = role.Name;
        var roleId = role.Id;

        var result = await _roleManager.DeleteAsync(role);
        if (result.Succeeded)
        {
            await _activityLogger.LogActivityAsync(
                ActivityType.RoleDeleted,
                $"Deleted role '{roleName}'",
                "ApplicationRole",
                roleId,
                "Success");

            TempData["Success"] = $"Role '{roleName}' has been deleted.";
        }
        else
        {
            TempData["Error"] = "Failed to delete role: " + string.Join(", ", result.Errors.Select(e => e.Description));
        }

        return RedirectToPage();
    }
}

public class RoleViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int UserCount { get; set; }
}
