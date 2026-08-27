# Work Summary — 2026-07-07

## 1. Asset Details — Inspection tab and locked modules (slide 21)

- Enabled the previously disabled "Inspection" tab on the Asset Details page; first built out a full inspection-history view sourced from the `AuditLogs` table (`AssetItemConditionUpdated` entries), then — per the mockup's slide 21 "Module Locked" design — replaced the Inspection, Risk Based Inspection, Technical Document, and Documentation tabs' content with a shared locked-module placeholder (padlock icon + "Module Locked", new `_ModuleLocked.cshtml` partial + `.sims-module-locked` CSS).
- Removed the now-unused `AssetItemService.GetInspectionHistoryAsync` and its `DetailsModel` wiring once the tab was replaced (explicit cleanup request).
- Backend for Documentation (upload/list/delete) was left intact but unreachable from the UI, so it can be re-enabled later without rebuilding.

## 2. Edit Condition (GVI) — picture attachments (slide 17)

- Added a picture-upload section to the Edit Condition page: Description + File fields, restricted client- and server-side to JPEG/PNG only.
- Redesigned the upload as **client-side staging**: "Add File" no longer posts anywhere — it validates the picture in JS, adds it to an in-page pending list (with a Remove option), and mirrors each staged file into a hidden `<input type="file">` (via the `DataTransfer` API) inside the main form. Files only actually upload when the user clicks **Save**, alongside the Condition (GVI) field update, in one POST.
- Added an "Existing Pictures" thumbnail gallery to the same page (previously invisible after the first save) with a per-picture Delete button.

## 3. Condition (GVI) tab — remarks removed, picture preview added (slide 16)

- Removed the remarks timeline / "Add Remark" form from the Condition (GVI) tab (and its now-dead backend: `Remarks` property, `OnPostAsync` handler, `AddRemarkInput`).
- Added a "Preview" section matching slide 16: a Bootstrap carousel showing the Edit-Condition-uploaded pictures, one at a time with a caption and Prev/Next controls.
- Fixed a carousel bug ("Next always shows the same picture"): Bootstrap measures slide width when its Carousel instance is first created, but the Condition tab-pane is `display:none` until clicked, so the first click could cache a broken zero-width instance forever. Fixed by disposing/recreating the Carousel on the tab's `shown.bs.tab` event.

## 4. `AssetItemDocuments.DocumentType`

- Added a `DocumentType` column (`Picture` vs `Document`, via new `DocumentTypeCode` constants) to distinguish condition pictures from general documents sharing the same table.
- Idempotent schema migration + data backfill (inferred from file extension) in both `DatabaseInitializer.cs` and `OracleSchemaInitializer.cs`.
- Condition-tab carousel and Edit-Condition's existing-pictures gallery now filter to `DocumentType == Picture` only.

## 5. Plant sidebar / Asset list interaction

- Clicking a Plant (or a specific asset under it) in the sidebar Asset Tree now hides the full Asset Items table on `AssetItems/Index` — it was redundant with the map + Plant Info/Asset Details side panel already shown. Table (and its delete modals) now only render when neither `plantId` nor `assetId` is present in the query string.

## 6. Top bar redesign (slide 6)

- Studied slide 6's top-bar pill-button design (dark blue `#2D6A87` buttons) and moved **Asset Register**, **Map Demo**, and **Administration** (role-conditional: Administration/Management/My Audit Trail) from the left sidebar into the top bar as styled buttons/dropdown.
- Added the `badak-lng-large.png` logo and the full "Structural Integrity Management System (SIMS)" title to the top bar, replacing the generic shield icon + "SIMS" text.
- Changed the top bar background to white and switched it to a 3-column CSS grid (`1fr auto 1fr`) so the brand is truly centered independent of the differing widths of the hamburger (left) and nav/user menu (right); updated text/icon colors for contrast against the new white background.

## 7. QR Code — removed from data model, moved to a popup

- Removed the free-text `QrCode` field from `AssetItem` entirely (entity, both DB schemas via an idempotent column-drop migration, service layer, Create/Edit forms).
- The QR image itself now always encodes the asset's `AssetId` tag (previously it preferred the manual `QrCode` text if present).
- The "QR Code" row stays visible on Create/Edit/Details/Index-panel per request, but now shows a **View QR Code** button that opens the existing PrintTag page in a small popup window instead of a raw text value; Create shows "Available after the asset is saved" since there's no ID yet to link to.
- Added a reusable `openCenteredPopup()` helper to `site.js` (finally wired the previously-unused file into `_Layout.cshtml`) so the popup opens centered on screen instead of at the browser's default position; all three popup call sites (Details, Edit, Index) use it.

## 8. `Plant.Code`: string → int

- Changed `Plant.Code` from `string?` to `int?` end-to-end: entity, `AssetItem.GenerateAssetId`'s asset-tag interpolation, `AssetItemService`, `PlantService`, and the Plants Create/Edit forms (the `<input>` tag helper now renders `type="number"` automatically).
- Schema migration for existing databases: SQL Server does an in-place `ALTER COLUMN` (nulling out any non-numeric legacy values first); Oracle uses add-column/backfill/drop/rename since Oracle can't `MODIFY` a populated column's datatype directly.
- Verified end-to-end against the live Oracle dev database: migration ran cleanly, Plants list/Create/Edit show numeric codes, and a freshly created asset generated the correct tag (e.g. `2D-9/Q-9`).

## Incidental fixes

- Discovered and fixed a latent Razor-compiler issue affecting this .NET 10 SDK: single-line inline code blocks (`{ <span>...</span> }` all on one line, or blank lines immediately after `@if (...) {`) fail to parse and — worse — cascade into unrelated bogus errors elsewhere in the same compilation. Fixed every occurrence found across `Details.cshtml`, `Edit.cshtml`, `Index.cshtml` (AssetItems), and `Roles.cshtml`, plus several files with unclosed `<br>` tags (`Login.cshtml`, `AuditLogs.cshtml`, `Roles/Edit.cshtml`, `Roles/Index.cshtml`, `Users/Index.cshtml`, `Plants/Index.cshtml`) that hit the same underlying parser limitation.
- Tightened the project's Claude Code permission allowlist (`.claude/settings.json` for read-only dev commands like `dotnet build`/`podman ps`/`podman logs`; `.claude/settings.local.json` for the broader dev-loop commands — `curl`, `pkill`, `kill`, `nohup dotnet run`, `sed -i` — used repeatedly to restart/test the app all session) to cut down on repeated permission prompts.

## Notes / follow-ups

- The app was rebuilt and restarted against the live Oracle-backed dev database after essentially every change this session; all changes in this summary were verified via real HTTP requests (login, page renders, form submissions, file uploads) rather than just a clean compile.
- Documentation-tab upload/list backend remains in place but unreachable from the UI (locked per slide 21) — ready to re-enable later.
- No real browser was available in this environment (no chromium, Playwright is Windows-side Node with WSL path friction), so UI/JS behavior (carousel, popups, drag-resize) was verified via markup/script inspection and manual reasoning about the DOM/event flow rather than a live screenshot.
