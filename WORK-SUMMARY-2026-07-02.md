# Work Summary — 2026-07-02

Branch: `feature/mockup1-0`

All changes below were implemented, built, and verified live against the running app (Oracle XE dev DB) before being reported complete.

## 1. Dashboard layout iterations

Progressive changes to `src/AIMS.WebFrontend/Pages/Index.cshtml` and `Index.cshtml.cs`:

1. **Moved "Priority Breakdown" into the Summary right panel** (`@section RightPanel`), out of the main content row.
2. **Added a "Plant Summary" section** matching slide 3 of `SIMS Dashboard Rev A 20260701.pptx`: a grouped bar chart (Chart.js, loaded from cdnjs) showing Good/Fair/Poor/Unknown asset counts per plant, with an "All Plant" aggregate bar first. Backed by a new `PlantConditionSummaries` list computed in `IndexModel.OnGetAsync`.
3. **Moved the Plant Summary chart into the right panel**, above the breakdown card; "Asset Types" became a full-width card in the main content area where Plant Summary used to sit.
4. **Replaced "Priority Breakdown" with "Condition Breakdown – All Plant"** in the right panel (Good/Fair/Poor/Unknown counts instead of High/Medium/Low priority). Added matching `.sims-progress-bar.gray` / `.condition-dot.unknown` styles to `site.css`.
5. **Added a "Site Map" section** embedding the same Leaflet + QGIS Server WMS map used on the `MapDemo` page (auto-discovers WMS layers via GetCapabilities, falls back to a default layer), replacing "Asset Types" and "Recently Added Assets" entirely. Removed the now-unused `AssetsByType`/`RecentAssets` model properties and queries.
6. **Removed the 4 stat boxes** (Total Assets / High Priority / Fair Integrity / Poor Integrity) above the Site Map. Removed the now-dead `HighPriorityCount`/`MediumPriorityCount`/`LowPriorityCount` properties from `IndexModel`.

Net result: dashboard is now Header → Site Map (main) / Plant Summary + Condition Breakdown (right panel).

## 2. Plant/AssetItem entity refactor

**Goal:** move Plant Code and Description off `AssetItem` and onto the real `Plant` entity, using `PlantId` as the sole foreign key.

- **`AIMS.Core/Entities/AssetItem.cs`**: added `Plant.Code`; removed `AssetItem.PlantCode` / `AssetItem.PlantDescription`.
- **Schema** (`DatabaseInitializer.cs` for SQL Server, `OracleSchemaInitializer.cs` for Oracle): added `Plants.Code`; removed `PlantCode`/`PlantDescription` from `AssetItems` (new installs) and stopped re-adding them on upgrade.
- **Startup migration** (idempotent, runs every boot, in C# — no DB functions): seeds `Plants` from the existing hardcoded 49-entry `PlantCode` lookup table (`LookupCodes.cs`, matched by `Code`), backfills any leftover `AssetItems.PlantId` from an old `PlantCode` column if still present, then drops the legacy columns.
- **`PlantService`**: `Code` added to all SELECT/INSERT/UPDATE statements.
- **`AssetItemService`**: removed `PlantCode`/`PlantDescription` from all queries; removed `PlantDescription` from the search filter.
- **Asset Create/Edit forms**: removed the separate "Plant Code" dropdown (previously a second, disconnected picker). Asset Tag preview now reads the Code from the selected Plant via a `data-code` attribute on the Plant `<option>`.
- **Details/Index Asset pages**: display Plant Code/Description via the `Plant` navigation/lookup instead of the removed columns.
- **Plants Create/Edit/Index pages**: added a `Code` field/column.

Verified: schema migration seeded exactly 50 plants (49 legacy + 1 pre-existing test plant), full create→list round-trip succeeded, no console errors.

## 3. Automatic Asset Tag (AssetId) generation

**Goal:** Asset Tag No. (`<PlantCode><EquipmentCode>-<EquipmentOrder>/<CivilAssetCode>-<CivilAssetOrder>`, e.g. `20D-4/Q-1`) should always be computed in C#, never editable by the user, never a database function.

- **`AssetItemService.CreateAsync`/`UpdateAsync`**: added a `GenerateAssetIdAsync` helper that resolves the selected Plant's `Code` and calls the existing `AssetItem.GenerateAssetId(...)` static method, always overwriting `AssetId` regardless of what's passed in. This runs for every create/update, independent of caller.
- **Create/Edit page models**: removed `Input.AssetId` entirely from the bound input — it's no longer part of the form's data contract.
- **Create/Edit views**: replaced the editable "Asset Tag No." input with a **read-only** field (`#assetTagPreview`) that live-updates via JS as Plant/Equipment/Civil fields change, so the user sees the tag that will be assigned.
- **Startup migration** (C# code in both schema initializers, no DB functions): recomputes `AssetId` for existing rows that already have full Plant/Equipment/Civil data, so it matches the canonical formula; rows lacking that structured data (legacy freeform tags) are left untouched to avoid destroying meaningful existing data.

**Side effect / bug fix**: removing `Input.AssetId` also fixed a pre-existing bug where leaving "Asset Tag No." blank (the intended way to trigger auto-generation) was silently blocked by client-side validation, because `Input.AssetId` was a non-nullable `string` which ASP.NET Core implicitly treats as required.

Verified: live preview showed `20D-4/Q-1` while filling the Create form; the created asset persisted with that exact tag; the Edit page showed the persisted tag read-only, live-updated to `20D-9/Q-1` when Equipment Order was changed, and the save persisted the regenerated value correctly.

## Files touched (non-exhaustive, by area)

- Dashboard: `Pages/Index.cshtml`, `Pages/Index.cshtml.cs`, `wwwroot/css/site.css`
- Plant/AssetItem entities: `AIMS.Core/Entities/AssetItem.cs`
- Schema/migrations: `AIMS.Infrastructure/Data/DatabaseInitializer.cs`, `AIMS.Infrastructure/Data/OracleSchemaInitializer.cs`
- Services: `AIMS.Infrastructure/Services/PlantService.cs`, `AIMS.Infrastructure/Services/AssetItemService.cs`
- Asset pages: `Pages/AssetItems/Create.cshtml(.cs)`, `Pages/AssetItems/Edit.cshtml(.cs)`, `Pages/AssetItems/Details.cshtml`, `Pages/AssetItems/Index.cshtml(.cs)`
- Plant pages: `Pages/Plants/Create.cshtml(.cs)`, `Pages/Plants/Edit.cshtml(.cs)`, `Pages/Plants/Index.cshtml`
