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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Infrastructure
{
    public static class StartupSetup
    {
        public static void AddDbContext(this IServiceCollection services, IConfiguration configuration)
        {
            var raw = (configuration["DatabaseProvider"] ?? "PostgreSQL").Trim();
            var isSqlServer = raw.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                           || raw.Equals("MSSQL", StringComparison.OrdinalIgnoreCase)
                           || raw.Equals("SQLServer", StringComparison.OrdinalIgnoreCase);
            var connKey = isSqlServer ? "SqlServer" : "PostgreSQL";
            var connStr = configuration.GetConnectionString(connKey)
                ?? throw new InvalidOperationException($"Connection string '{connKey}' not found.");

            services.AddDbContext<AppDbContext>(options =>
            {
                if (isSqlServer)
                    options.UseSqlServer(connStr, b => b.MigrationsAssembly("AIMS.Migrations.SqlServer"));
                else
                    options.UseNpgsql(connStr, b => b.MigrationsAssembly("AIMS.Migrations.PostgreSQL"));
            });
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
            fac.GetRequiredService<AppDbContext>().Database.Migrate();
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
