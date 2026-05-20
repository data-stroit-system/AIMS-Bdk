using System;
using System.Threading.Tasks;
using AIMS.Infrastructure.Data;
using AIMS.Infrastructure.DomainEvents;
using AIMS.Infrastructure.FileTransfer;
using AIMS.Infrastructure.IdentityClass;
using AIMS.Infrastructure.Audit;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Infrastructure
{
    public static class StartupSetup
    {
        public static void AddDbContext(this IServiceCollection services, string connectionString)
        {

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString))
                //.AddIdentity<ApplicationUser, ApplicationRole>(options => options.SignIn.RequireConfirmedAccount = false)
                //.AddEntityFrameworkStores<AppDbContext>()
                ; // will be created in web project root}


        }

        /// <summary>
        /// Registers the audit trail services including the HttpContext-based user provider and activity logger.
        /// </summary>
        public static void AddAuditTrail(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IAuditUserProvider, HttpContextAuditUserProvider>();
            services.AddScoped<IActivityLogger, ActivityLogger>();
            services.AddScoped<FileUploadHelper>();
        }

        public static void SeedData(this IServiceCollection services)
        {
            using var fac = services.BuildServiceProvider();
            fac.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        /// <summary>
        /// Seeds the default Admin role and optionally creates a default admin user.
        /// </summary>
        public static async Task SeedRolesAndAdminUserAsync(this IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Seed default roles
            string[] roles = { "Admin", "Manager", "User" };
            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var role = new ApplicationRole
                    {
                        Name = roleName,
                        Description = roleName switch
                        {
                            "Admin" => "Full system access to all features including user management, role management, and audit trails",
                            "Manager" => "Can manage users and view audit trails",
                            "User" => "Can view their own data and audit trails",
                            _ => null
                        }
                    };
                    await roleManager.CreateAsync(role);
                }
            }

            // Seed default admin user if it doesn't exist
            var adminEmail = "admin@aims.local";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin",
                    Email = adminEmail,
                    FullName = "System Administrator",
                    JobTitle = "Administrator",
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }
        }
    }
}
