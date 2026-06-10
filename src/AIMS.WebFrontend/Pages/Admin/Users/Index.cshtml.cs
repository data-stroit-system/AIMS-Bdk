using AIMS.Core.Entities;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Admin.Users;

[Authorize(Roles = "Admin,Manager")]
public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IActivityLogger _activityLogger;
    private const int PageSize = 15;

    public IndexModel(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IActivityLogger activityLogger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _activityLogger = activityLogger;
    }

    public List<UserViewModel> Users { get; set; } = new();
    public List<ApplicationRole> AvailableRoles { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RoleFilter { get; set; }

    public async Task OnGetAsync(int page = 1)
    {
        CurrentPage = page < 1 ? 1 : page;
        AvailableRoles = _roleManager.Roles.OrderBy(r => r.Name).ToList();

        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrEmpty(SearchTerm))
        {
            query = query.Where(u =>
                (u.FullName != null && u.FullName.Contains(SearchTerm)) ||
                (u.Email != null && u.Email.Contains(SearchTerm)) ||
                (u.UserName != null && u.UserName.Contains(SearchTerm)));
        }

        var allUsers = query.OrderBy(u => u.FullName).ToList();

        var userViewModels = new List<UserViewModel>();
        foreach (var user in allUsers)
        {
            var roles = await _userManager.GetRolesAsync(user);

            if (!string.IsNullOrEmpty(RoleFilter) && !roles.Contains(RoleFilter))
                continue;

            userViewModels.Add(new UserViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                UserName = user.UserName ?? string.Empty,
                JobTitle = user.JobTitle,
                EmailConfirmed = user.EmailConfirmed,
                Roles = roles.ToList()
            });
        }

        var totalCount = userViewModels.Count;
        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        TotalPages = TotalPages < 1 ? 1 : TotalPages;
        CurrentPage = CurrentPage > TotalPages ? TotalPages : CurrentPage;

        Users = userViewModels
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        if (!User.IsInRole("Admin"))
        {
            TempData["Error"] = "You do not have permission to delete users.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user != null)
        {
            if (user.UserName == User.Identity?.Name)
            {
                TempData["Error"] = "You cannot delete your own account.";
                return RedirectToPage();
            }

            var userName = user.UserName;
            var fullName = user.FullName;
            var userId = user.Id;

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                await _activityLogger.LogActivityAsync(
                    ActivityType.UserDeleted,
                    $"Deleted user '{userName}' ({fullName})",
                    "ApplicationUser",
                    userId,
                    "Success");

                TempData["Success"] = $"User '{fullName}' has been deleted.";
            }
            else
            {
                TempData["Error"] = "Failed to delete user: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }

        return RedirectToPage();
    }
}

public class UserViewModel
{
    public string Id { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public bool EmailConfirmed { get; set; }
    public List<string> Roles { get; set; } = new();
}
