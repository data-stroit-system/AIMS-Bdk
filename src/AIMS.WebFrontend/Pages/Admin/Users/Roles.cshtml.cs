using AIMS.Core.Entities;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public class RolesModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IActivityLogger _activityLogger;

    public RolesModel(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IActivityLogger activityLogger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _activityLogger = activityLogger;
    }

    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<string> UserRoles { get; set; } = new();
    public List<ApplicationRole> AvailableRoles { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        UserId = user.Id;
        UserFullName = user.FullName ?? "Unknown";
        UserEmail = user.Email ?? string.Empty;
        UserRoles = (await _userManager.GetRolesAsync(user)).ToList();
        AvailableRoles = _roleManager.Roles.OrderBy(r => r.Name).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, List<string> selectedRoles)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);

        if (user.UserName == User.Identity?.Name &&
            currentRoles.Contains("Admin") &&
            !selectedRoles.Contains("Admin"))
        {
            TempData["Error"] = "You cannot remove the Admin role from your own account.";
            return RedirectToPage(new { id });
        }

        var rolesToRemove = currentRoles.Except(selectedRoles).ToList();
        if (rolesToRemove.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                TempData["Error"] = "Failed to remove roles: " + string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return RedirectToPage(new { id });
            }

            foreach (var role in rolesToRemove)
                await _activityLogger.LogActivityAsync(
                    ActivityType.RoleRemoved,
                    $"Removed role '{role}' from user '{user.UserName}'",
                    "ApplicationUser",
                    user.Id,
                    "Success");
        }

        var rolesToAdd = selectedRoles.Except(currentRoles).ToList();
        if (rolesToAdd.Any())
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                TempData["Error"] = "Failed to add roles: " + string.Join(", ", addResult.Errors.Select(e => e.Description));
                return RedirectToPage(new { id });
            }

            foreach (var role in rolesToAdd)
                await _activityLogger.LogActivityAsync(
                    ActivityType.RoleAssigned,
                    $"Assigned role '{role}' to user '{user.UserName}'",
                    "ApplicationUser",
                    user.Id,
                    "Success");
        }

        TempData["Success"] = $"Roles for '{user.FullName}' have been updated successfully.";
        return RedirectToPage(new { id });
    }
}
