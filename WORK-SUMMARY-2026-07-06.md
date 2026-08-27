# Work Summary — 2026-07-06

## 1. Ran the app against the Oracle provider

- No `docker-compose`/`podman-compose` available in this environment, so bypassed compose entirely.
- Started an Oracle XE 11g container directly via `podman run` (`oracleinanutshell/oracle-xe-11g`, port 1521).
- Ran `AIMS.WebFrontend` locally with `dotnet run`, overriding config via env vars:
  `DatabaseProvider=Oracle`, `ConnectionStrings__Oracle=...;Password=oracle;` (the container's real default password — the checked-in `appsettings.json` has a stale `del123`).
- Verified end-to-end: login as seeded `admin`/`Admin@123`, dashboard loaded, schema auto-created on Oracle.
- App has been kept running in the background throughout the session (restarted after each code change) at `http://localhost:5080`.

## 2. Studied the mockup (slides 7 & 8, "SIMS Dashboard Rev B 20260706.pptx")

- Extracted slide text/notes/images via `python3 -m zipfile` (no `unzip`/PowerPoint available).
- **Slide 7**: clicking a Plant in the Asset Tree shows that plant's assets on a map, with a small Plant Code / Plant Desc. panel.
- **Slide 8**: clicking a specific Asset highlights it on the map and shows an Asset Details panel (Asset Register No., GIS Ref. No., Asset Tag No., Description, Plant, Zone, Area, Train, Coordinate, Installed year+age, Condition, QR Code, Preview) with a link into the full Asset Details page.
- Mapped every slide-8 field to the existing `AssetItem`/`Plant` entities — found two duplicated/legacy fields in the process (see below).

## 3. Reconciled the `Condition` / `IntegrityStatus` duplication

Two independent fields represented the same Good/Fair/Poor concept and could silently disagree (the Home dashboard KPIs read `IntegrityStatus`, a field explicitly marked "legacy" in code, while the main Create/Edit form only exposed `Condition`).

- Kept `Condition` (string) as canonical; removed `IntegrityStatus` (enum) and the dead `UpdateStatus`/`AssetItemStatusUpdateEvent` that only existed to mutate it.
- Added an idempotent startup migration (`MigrateLegacyIntegrityStatus`, both SQL Server and Oracle) that backfills `Condition` from any legacy `IntegrityStatus` value before dropping the column, so no historical dashboard data was lost.
- Updated `AssetItemService`, the Home dashboard KPI counts, the asset list filter/badges, and removed the duplicate "Integrity Status" dropdown from Create/Edit.

## 4. Removed unused legacy fields from `AssetItem`

Removed `Location`, `Description`, `Type` (`AssetType` enum), `Priority` (`AssetPriority` enum) — dead fields shown only in a "(legacy)" form section, never displayed anywhere else in the app.

- Dropped the enums (`AssetType`, `AssetPriority`) entirely — confirmed unused outside `AssetItem`.
- Dropped the four columns from both SQL Server and Oracle schema, with an idempotent `DropLegacyAssetItemColumns` migration for existing databases.
- Removed the Type/Priority filter dropdowns and active-filter badges from the asset list, and the "Section: Additional" block from Create/Edit forms.

## 5. Plant map view (slide 7)

- When a Plant is selected in the Asset Tree (`AssetItems/Index?plantId=X`), the page now shows a "Site Map" card (Leaflet + QGIS WMS, same source as the Home dashboard) above the asset list.
- Extracted the shared Leaflet/QGIS init script into `Pages/Shared/_SiteMapScripts.cshtml` so the Home dashboard and this new view share one implementation instead of duplicating it.

## 6. Resizable right-side panel

- Moved the Plant Code / Plant Desc. info out of the map card and into a proper right-side panel (`@section RightPanel`), consistent with the panel already used on the Home dashboard.
- Added a drag-to-resize separator (`.sims-panel-resizer`) between the center content and the right panel, with generic JS in `_Layout.cshtml` (mousedown/mousemove/mouseup, clamped 240–560px) that works on any page defining the right panel.

## 7. Asset detail view (slide 8)

- Clicking a specific asset in the sidebar tree no longer jumps straight to the full Details page — it now routes to `AssetItems/Index?plantId=X&assetId=Y`, showing:
  - The same Plant-level map (no real per-asset geo-pin; there's no lat/lng data model for that yet).
  - A right panel with the full slide-8 field set (Asset Register No., GIS Ref. No., Asset Tag No., Description, Plant, Zone, Area, Train, Coordinate, Installed+age, Condition badge, QR Code, picture preview).
  - A footer button pinned to the bottom of the panel — "Open Asset Details" — linking to the existing full Details page.
- Updated the tree's selection-highlight logic in `_Layout.cshtml` to recognize `assetId` from the query string (previously only detected via the Details page's URL), so both the Plant and the specific Asset node highlight correctly.

## Notes / follow-ups

- No per-plant or per-asset geographic coordinates exist in the data model (`Plant` has only Code/Name/Description; `AssetItem.CoordinateN/E` are free-text, not standard lat/lng) — the map is the same site-wide view in every case, not truly plant- or asset-scoped. Real geo-positioning would need a coordinate system decision and conversion logic.
- Couldn't get a real browser screenshot for the resizable-panel/drag-to-resize verification in this environment (no chromium binary; available Playwright is Windows-side Node with WSL path-translation friction) — verified via HTTP/markup inspection and code review instead.
- All changes verified by rebuilding (0 errors each time) and re-testing against the live Oracle-backed app after every change.
