using System;
using System.Threading.Tasks;
using AIMS.Infrastructure.Audit;
using AIMS.Infrastructure.Data;
using AIMS.Infrastructure.FileTransfer;
using AIMS.Infrastructure.IdentityClass;
using AIMS.Infrastructure.Services;
using AIMS.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Infrastructure
{
    public static class StartupSetup
    {
        public static void AddDapperContext(this IServiceCollection services, IConfiguration configuration)
        {
            var provider = configuration["DatabaseProvider"] ?? "SqlServer";
            if (provider.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSingleton<IDapperContext, OracleDapperContext>();
                services.AddSingleton<ISqlDialect, OracleDialect>();
                services.AddSingleton<ISchemaInitializer, OracleSchemaInitializer>();
            }
            else
            {
                services.AddSingleton<IDapperContext, DapperContext>();
                services.AddSingleton<ISqlDialect, SqlServerDialect>();
                services.AddSingleton<ISchemaInitializer, DatabaseInitializer>();
            }
        }

        public static void AddAuditTrail(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IAuditUserProvider, HttpContextAuditUserProvider>();
            services.AddScoped<IActivityLogger, ActivityLogger>();
            services.AddScoped<FileUploadHelper>();
            services.AddScoped<AssetItemService>();
            services.AddScoped<PlantService>();
        }

        public static void InitializeDatabase(this IServiceCollection services)
        {
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<ISchemaInitializer>().Initialize();
        }

        public static async Task SeedRolesAndAdminUserAsync(this IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

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
                    await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }
    }
}
