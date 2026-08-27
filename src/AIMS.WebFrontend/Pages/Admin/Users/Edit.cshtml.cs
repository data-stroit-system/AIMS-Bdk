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
public class EditModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IActivityLogger _activityLogger;

    public EditModel(UserManager<ApplicationUser> userManager, IActivityLogger activityLogger)
    {
        _userManager = userManager;
        _activityLogger = activityLogger;
    }

    [BindProperty]
    public EditUserInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        Input = new EditUserInput
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            UserName = user.UserName ?? string.Empty,
            JobTitle = user.JobTitle,
            EmailConfirmed = user.EmailConfirmed
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByIdAsync(Input.Id);
        if (user == null)
        {
            return NotFound();
        }

        // Update basic properties
        user.FullName = Input.FullName;
        user.Email = Input.Email;
        user.UserName = Input.UserName;
        user.JobTitle = Input.JobTitle ?? string.Empty;
        user.EmailConfirmed = Input.EmailConfirmed;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // Log user update activity
        await _activityLogger.LogActivityAsync(
            ActivityType.UserUpdated,
            $"Updated user '{user.UserName}' ({user.FullName})",
            "ApplicationUser",
            user.Id,
            "Success");

        // Update password if provided
        if (!string.IsNullOrEmpty(Input.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, Input.NewPassword);

            if (!passwordResult.Succeeded)
            {
                foreach (var error in passwordResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return Page();
            }

            // Log password change activity
            await _activityLogger.LogSecurityActivityAsync(
                ActivityType.PasswordReset,
                $"Password reset for user '{user.UserName}' by administrator",
                "Success",
                user.Id,
                user.UserName);
        }

        TempData["Success"] = $"User '{user.FullName}' has been updated successfully.";
        return RedirectToPage("Index");
    }
}

public class EditUserInput
{
    public string Id { get; set; } = string.Empty;

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

    [Display(Name = "Email Confirmed")]
    public bool EmailConfirmed { get; set; }

    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least {2} characters long.")]
    [Display(Name = "New Password")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string? ConfirmPassword { get; set; }
}
