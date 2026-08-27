using System.ComponentModel.DataAnnotations;
using AIMS.Core.Entities;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIMS.WebFrontend.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public class CreateModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IActivityLogger _activityLogger;

    public CreateModel(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IActivityLogger activityLogger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _activityLogger = activityLogger;
    }

    [BindProperty]
    public CreateUserInput Input { get; set; } = new();

    public List<ApplicationRole> AvailableRoles { get; set; } = new();

    public void OnGet()
    {
        AvailableRoles = _roleManager.Roles.OrderBy(r => r.Name).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        AvailableRoles = _roleManager.Roles.OrderBy(r => r.Name).ToList();

        if (!ModelState.IsValid)
            return Page();

        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            FullName = Input.FullName,
            JobTitle = Input.JobTitle ?? string.Empty,
            EmailConfirmed = Input.EmailConfirmed
        };

        var result = await _userManager.CreateAsync(user, Input.Password);

        if (result.Succeeded)
        {
            if (Input.SelectedRoles.Any())
            {
                var roleResult = await _userManager.AddToRolesAsync(user, Input.SelectedRoles);
                if (!roleResult.Succeeded)
                {
                    foreach (var error in roleResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);
                    await _userManager.DeleteAsync(user);
                    return Page();
                }
            }

            await _activityLogger.LogActivityAsync(
                ActivityType.UserCreated,
                $"Created new user '{user.UserName}' ({user.FullName}) with roles: {string.Join(", ", Input.SelectedRoles)}",
                "ApplicationUser",
                user.Id,
                "Success");

            TempData["Success"] = $"User '{user.FullName}' has been created successfully.";
            return RedirectToPage("Index");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }
}

public class CreateUserInput
{
    [Required]
    [MaxLength(250)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(250)]
    [Display(Name = "Job Title")]
    public string? JobTitle { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Email Confirmed")]
    public bool EmailConfirmed { get; set; } = true;

    public List<string> SelectedRoles { get; set; } = new();
}
