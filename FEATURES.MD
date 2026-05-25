#2. Database Providers

How to switch providers

Local development — edit appsettings.json:
{ "DatabaseProvider": "PostgreSQL" }   // or "SqlServer"

Podman Compose:
# PostgreSQL
$env:DATABASE_PROVIDER = "PostgreSql"
podman compose --profile postgresql up --build -d


# SQL Server — any of these work now:
$env:DATABASE_PROVIDER = "SqlServer"   # or "SQLServer" or "MSSQL"
podman compose --profile sqlserver up --build -d


Key changes

┌─────────────────────────────┬───────────────────────────────────────────────────────────────────────────────────┐
│            File             │                                   What changed                                    │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ AIMS.Migrations.PostgreSQL/ │ New project — Npgsql migrations                                                   │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ AIMS.Migrations.SqlServer/  │ New project — SQL Server migrations                                               │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ StartupSetup.cs             │ Reads DatabaseProvider from config, branches on provider, sets MigrationsAssembly │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ StartupSetup.cs             │ EnsureCreated() → Migrate() (proper tracked migrations)                           │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ Program.cs                  │ Passes IConfiguration instead of raw connection string                            │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ AppDbContextFactory.cs      │ Same provider-aware logic for dotnet ef tooling                                   │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ appsettings.json            │ Two named connection strings + DatabaseProvider key                               │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ docker-compose.yml          │ Profiles (postgresql / sqlserver), required: false depends_on                     │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
│ Dockerfile                  │ Copies all 6 csproj files for proper restore layer caching                        │
└─────────────────────────────┴───────────────────────────────────────────────────────────────────────────────────┘