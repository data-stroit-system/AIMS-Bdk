using AIMS.Core.Entities;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AIMS.WebFrontend.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _singInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IActivityLogger _activityLogger;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<ApplicationUser> singInManager,
            UserManager<ApplicationUser> userManager,
            IActivityLogger activityLogger,
            ILogger<LoginModel> logger)
        {
            _singInManager = singInManager;
            _userManager = userManager;
            _activityLogger = activityLogger;
            _logger = logger;
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPost()
        {
            if (!ModelState.IsValid)
            {
                ModelState.AddModelError("Default", "Invalid username or password");
                return Page();
            }

            try
            {
                // Indexed lookup via NormalizedUserName — case-insensitive, and doesn't
                // materialize the whole AspNetUsers table like _userManager.Users does.
                var user = await _userManager.FindByNameAsync(LoginInput.UserName);
                if (user == null)
                {
                    await _activityLogger.LogSecurityActivityAsync(
                        ActivityType.LoginFailed,
                        $"Login attempt with unknown username: {LoginInput.UserName}",
                        "Failed",
                        null,
                        LoginInput.UserName);

                    ModelState.AddModelError("Default", "Invalid username or password");
                    return Page();
                }

                var signin = await _singInManager.CheckPasswordSignInAsync(user, LoginInput.Password, false);
                if (signin.Succeeded)
                {
                    await _singInManager.SignInAsync(user, false);

                    await _activityLogger.LogSecurityActivityAsync(
                        ActivityType.Login,
                        $"User '{user.UserName}' logged in successfully",
                        "Success",
                        user.Id,
                        user.UserName);

                    return Redirect("/");
                }

                await _activityLogger.LogSecurityActivityAsync(
                    ActivityType.LoginFailed,
                    $"Failed login attempt for user: {user.UserName} (invalid password)",
                    "Failed",
                    user.Id,
                    user.UserName);

                ModelState.AddModelError("Default", "Invalid username or password");
                return Page();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unhandled exception during login for user {UserName}", LoginInput.UserName);
                throw;
            }
        }

        [BindProperty]
        public LoginViewModel LoginInput { get; set; } = new LoginViewModel();
    }

    public class LoginViewModel
    {
        // Caps match the AspNetUsers schema (UserName nvarchar(256)); unbounded input
        // previously overflowed the AuditLogs.Description nvarchar(500) column on the
        // failed-login logging path and turned into an HTTP 500.
        [StringLength(256)]
        public string UserName { get; set; } = string.Empty;

        [StringLength(128)]
        public string Password { get; set; } = string.Empty;
    }
}
