# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**AIMS** (Asset/Infrastructure Management System) — branded **SIMS** (Structural Integrity Management System) in the UI — a .NET 10 Clean Architecture web app for Badak LNG (Bontang, Indonesia) to track structural/civil assets (tanks, foundations, pipelines, etc.) across plants, with condition inspection (Good/Fair/Poor), GIS mapping, and audit trail. Razor Pages + Dapper (no EF Core), Autofac DI, dual SQL Server/Oracle support.

`docs/PROJECT_STUDY.md`-style deep dive lives in `PROJECT_STUDY.md` at the repo root — read it for exhaustive entity/service/page tables. **It is dated 2026-06-29 and predates the Plant-entity refactor, the `LookupCodes.cs` lookup tables, and the `Condition`/legacy-field cleanup described below** — trust the current code over that file for `AssetItem`/`Plant` shape. `WORK-SUMMARY-*.md` files log day-by-day changes chronologically and are more current.

The `SIMS Dashboard Rev A/B *.pptx` files are the UI design spec (wireframes built as native PowerPoint shapes, not screenshots) that pages under `Pages/AssetItems`, `Pages/Plants`, and `Pages/Index.cshtml` are being built to match. When asked to study or implement against them, extract via `python3 -m zipfile` (no `unzip`/LibreOffice available in this environment) — slide XML text runs (`<a:t>`) give the copy, shape `<a:xfrm>`/`<a:solidFill>` give layout and color, embedded media are photos/charts, not screenshots of the actual UI.

## Commands

```bash
# Build
dotnet restore AIMS.sln
dotnet build AIMS.sln

# Run (DatabaseProvider defaults to Oracle per appsettings.json)
dotnet run --project src/AIMS.WebFrontend
# ...or force SQL Server:
DatabaseProvider=SqlServer dotnet run --project src/AIMS.WebFrontend   # bash
$env:DatabaseProvider = "SqlServer"; dotnet run --project src/AIMS.WebFrontend  # PowerShell
```

There are **no test projects** in the solution — do not go looking for a test command.

### Database via containers

`docker-compose.yml` defines `oracle`/`sqlserver`/`webfrontend` profiles, but **`docker-compose`/`podman-compose` may not be available** in this environment. Fallback that has worked before:

```bash
podman run -d -p 1521:1521 --name oracle-xe oracleinanutshell/oracle-xe-11g
DatabaseProvider=Oracle ConnectionStrings__Oracle="Data Source=localhost:1521/xe;User Id=system;Password=oracle;" \
  dotnet run --project src/AIMS.WebFrontend
```

Note: the container's actual default password is `oracle`, but the checked-in `src/AIMS.WebFrontend/appsettings.json` has a stale `Password=del123` — override with `ConnectionStrings__Oracle` env var rather than editing the file.

Login with the seeded default admin: `admin` / `Admin@123`.

There's no headless browser (no chromium, Playwright is Windows-side Node with WSL path friction) — verify UI changes by rebuilding, running the app, and inspecting HTTP responses/markup, or ask the user to check in a real browser.

## Architecture

Dependency direction: `AIMS.WebFrontend → AIMS.Infrastructure → AIMS.Core → AIMS.SharedKernel` (Core has zero infrastructure dependencies).

- **AIMS.SharedKernel** — `BaseEntity`, `ValueObject`, `IRepository`, `IHandle<T>`, `IDomainEventDispatcher`, `IAuditUserProvider`, `IActivityLogger`.
- **AIMS.Core** — domain entities (`Entities/AssetItem.cs` holds `AssetItem`, `Plant`, `AssetItemRemarks`, `AssetItemDocuments`; `Entities/LookupCodes.cs` holds static lookup tables `PlantCode`/`EquipmentCode`/`CivilAssetCode` used for dropdowns and tag generation), domain events/handlers, `AuditLog`.
- **AIMS.Infrastructure** — Dapper repositories, the SQL Server/Oracle strategy-pattern abstraction, Dapper-backed ASP.NET Core Identity stores, audit logging, Autofac wiring (`ContainerSetup.cs`), startup extensions (`StartupSetup.cs`).
- **AIMS.WebFrontend** — Razor Pages UI. `AIMS.Migrations.{SqlServer,Oracle,PostgreSQL}` are empty placeholder projects (no EF migrations are used anywhere).

### No EF Core, no migrations — idempotent DDL instead

Schema is created/altered by `ISchemaInitializer` (`DatabaseInitializer.cs` for SQL Server, `OracleSchemaInitializer.cs` for Oracle), run on every app startup via `services.InitializeDatabase()`. All DDL is guarded (`IF OBJECT_ID(...) IS NULL` / Oracle `EXECUTE IMMEDIATE` with `ORA-00955` catch) so re-running is always safe. **Adding a field means adding a guarded `ALTER TABLE` block to both schema initializers**, not writing an EF migration.

One-time data backfills (e.g. seeding `Plants` from the old hardcoded lookup, migrating a legacy `IntegrityStatus` enum into the `Condition` string field, dropping now-unused legacy columns) are also written as idempotent C# migration steps inside these same initializer classes — see `MigrateLegacyIntegrityStatus` / `DropLegacyAssetItemColumns` for the pattern: backfill before drop, and run every boot behind existence checks.

### Oracle compatibility layer

Oracle uses `:Name` bind params while Dapper/SQL Server code is written with `@Name` — `OracleParamConnection`/`OracleParamCommand` rewrite parameters and coerce `DbType.Boolean` to `Int32` transparently. `ISqlDialect` (`SqlServerDialect`/`OracleDialect`) abstracts identifier quoting, insert-and-return-id, and pagination (`OFFSET/FETCH` vs `ROW_NUMBER()`) so `DapperRepository`/service classes stay provider-agnostic.

### Asset tagging

`AssetItem.AssetId` (the human-facing tag, e.g. `20D-4/Q-1`) is **always computed server-side** from `Plant.Code` + `EquipmentCode`/`EquipmentOrder` + `CivilAssetCode`/`CivilAssetOrder` via `AssetItem.GenerateAssetId(...)`, called from `AssetItemService.CreateAsync`/`UpdateAsync`. It is never a bound/editable form field — the UI shows a read-only, JS-live-updated preview instead. Keep this generation logic out of any new form input binding.

### Shared UI conventions

- `Pages/Shared/_Layout.cshtml` defines a role-based sidebar and a generic drag-to-resize right panel (`.sims-panel-resizer`, 240–560px clamp) that any page can opt into via `@section RightPanel`.
- `Pages/Shared/_SiteMapScripts.cshtml` is the single shared Leaflet + QGIS-Server-WMS init (auto-discovers layers via `GetCapabilities`) reused by the Home dashboard and the Plant asset-tree map view — don't duplicate map init JS per page.
- Condition badges (`Good`/`Fair`/`Poor`) use a fixed color mapping (green/yellow/red) consistent across the dashboard, asset list, and mockup spec — check `site.css` (`.condition-dot`, `.sims-progress-bar`) before introducing new status colors.
- There is no lat/lng geo model yet — `AssetItem.CoordinateN`/`CoordinateE` are free-text, not a real coordinate system, so all map views currently render the same site-wide QGIS layer rather than a true per-plant/per-asset pin.

### Roles

Three seeded roles — `Admin` (full access), `Manager` (user management + audit view), `User` (self-service) — enforced via Razor Pages `[Authorize(Roles=...)]`. The `Admin` role and the seeded `admin` account are protected from deletion/self-deletion in the Roles/Users pages.
