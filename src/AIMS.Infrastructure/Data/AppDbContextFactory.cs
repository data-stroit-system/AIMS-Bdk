using System;
using System.IO;
using System.Threading.Tasks;
using AIMS.SharedKernel;
using AIMS.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AIMS.Infrastructure.Data
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../AIMS.WebFrontend"))
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var raw = (configuration["DatabaseProvider"] ?? "PostgreSQL").Trim();
            var isSqlServer = raw.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                           || raw.Equals("MSSQL", StringComparison.OrdinalIgnoreCase)
                           || raw.Equals("SQLServer", StringComparison.OrdinalIgnoreCase);
            var connKey = isSqlServer ? "SqlServer" : "PostgreSQL";
            var connStr = configuration.GetConnectionString(connKey)!;

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            if (isSqlServer)
                optionsBuilder.UseSqlServer(connStr, b => b.MigrationsAssembly("AIMS.Migrations.SqlServer"));
            else
                optionsBuilder.UseNpgsql(connStr, b => b.MigrationsAssembly("AIMS.Migrations.PostgreSQL"));

            return new AppDbContext(optionsBuilder.Options, new NoOpDispatcher());
        }

        private class NoOpDispatcher : IDomainEventDispatcher
        {
            public Task Dispatch(BaseDomainEvent domainEvent) => Task.CompletedTask;
        }
    }
}
