# Parent/Child Asset Cascade within a Plant

## Goal

Allow an asset to be cascaded as a child of another asset in the same Plant, forming a parent/child hierarchy visible across the UI.

## Design decisions

- **Model**: `AssetItems.ParentAssetId int NULL` — self-referencing FK, exactly one parent, unlimited children, **unrestricted depth**.
- **Delete**: **Block** deleting any asset that still has children.
- **UI**: Parent selector in Create/Edit + a Parent/Child block in the Details Asset Register tab + **nested rendering in the left sidebar Asset Tree**.
- **Plant moves**: when an asset's `PlantId` changes, its **whole subtree is moved with it**. If the moved asset has a parent living in the old Plant, that is a **hard validation error** — no auto-detach — the form re-renders with the error.
- **Invariant**: a child and its parent must always be in the same Plant; the parent link is never silently broken.

## 1. Domain & schema

**`src/AIMS.Core/Entities/AssetItem.cs`** — add `public int? ParentAssetId { get; set; }` to `AssetItem` (next to `PlantId`); add a `ParentAsset` nav prop for symmetry with `Plant`.

**`src/AIMS.Infrastructure/Services/DuplicateAssetIdException.cs`** — mirror this with a new `AssetHasChildrenException` (sealed, holds the asset id) in the same folder.

**`src/AIMS.Infrastructure/Data/DatabaseInitializer.cs`** (SQL Server)
- `CREATE TABLE AssetItems` (line ~217): add `ParentAssetId int NULL` + `CONSTRAINT FK_AssetItems_AssetItems_ParentId FOREIGN KEY (ParentAssetId) REFERENCES AssetItems(Id)`. (No `ON DELETE` clause → default NO ACTION, matches the block-on-delete rule.)
- New guarded upgrade block next to the `PlantId` one (line ~285): `IF ... COL_LENGTH('AssetItems','ParentAssetId') IS NULL BEGIN ALTER TABLE AssetItems ADD ParentAssetId int NULL; EXEC('ALTER TABLE AssetItems ADD CONSTRAINT FK_AssetItems_AssetItems_ParentId ...'); END;`

**`src/AIMS.Infrastructure/Data/OracleSchemaInitializer.cs`**
- `CREATE TABLE AssetItems` (line ~249): add `ParentAssetId NUMBER(10,0),` + `CONSTRAINT FK_AssetItems_AssetItems_Parent FOREIGN KEY (ParentAssetId) REFERENCES AssetItems(Id)`.
- New upgrade entries next to the `PlantId` ones (line ~301): guarded `ALTER TABLE AssetItems ADD (ParentAssetId NUMBER(10,0))` (catch `-1430`) and the FK (catch `-2275, -2264`, same pattern as `FK_AssetItems_Plants`).

## 2. `AssetItemService` changes

- `AllColumns`: append `ParentAssetId` (so every query/entity round-trip carries it).
- `CreateAsync` / `UpdateAsync`: include `ParentAssetId` in the column lists, params, and UPDATE SET.
- New private `ValidateParentAsync(conn, parentId, plantId, excludeId)` — runs inside both create/update, throws a validation error when:
  - parent id set but asset has no `PlantId`, **or** parent's `PlantId != asset.PlantId` (same-plant rule);
  - parent row doesn't exist;
  - **self-parent** (`parentId == excludeId`); **cycle** (walk up the candidate parent's `ParentAssetId` chain — if any ancestor id equals `excludeId`, reject).
- New private `GetSubtreeIdsAsync(conn, rootId)` — BFS via `SELECT Id FROM AssetItems WHERE ParentAssetId IN (...)` on repeated rounds; returns all descendant ids at any depth.
- **`UpdateAsync` plant-cascade**: inside one transaction, when `updates.PlantId != current.PlantId`, cascade the new `PlantId` to the whole subtree. **Hard validation error** when the current asset has a `ParentAssetId` and the new `PlantId` differs from the parent's plant: *"Cannot move asset <tag> to Plant X — it is a child of <parent tag> (Plant Y). Detach it from its parent first."* Pure validation rejection — no auto-detach, no partial write. Moving a top-level root asset (no parent) still moves all its descendants.
- **`DeleteAsync`**: after the asset-exists check, `SELECT COUNT(*) FROM AssetItems WHERE ParentAssetId = @Id`; if > 0 throw `AssetHasChildrenException`. Delete path unchanged otherwise.
- New `GetChildrenAsync(int parentId)` → direct children (`AllColumns`, `ORDER BY AssetId`).
- New `SetParentAsync(int childId, int? parentId)` (detach / re-parent) used by the Details-page "Detach" action.

## 3. Create / Edit pages

- `CreateAssetItemInput` / `EditAssetItemInput`: add `int? ParentAssetId` (`[Display(Name="Parent Asset")]`), plus non-bound lists for: plant options (existing), parent options (id/AssetId/Title/PlantId) for the currently-selected plant, excluding the asset itself on Edit.
- Add a `?handler=ParentOptions&plantId=X` JSON handler to both PageModels returning `{id, label:"AssetId — Title"}` for that plant (used when the Plant dropdown changes). On initial GET, server-render the options for the pre-selected plant (covers the sidebar "+ add asset" path).
- `Create.cshtml` / `Edit.cshtml`: add the "Parent Asset" select in the Asset Register section (after the Plant row). JS: on Plant change, fetch `ParentOptions` and rebuild the select (keep current selection if still valid). Validation message keeps the same-plant rule.
- On create/update save, map `Input.ParentAssetId` onto the entity; create/update audit descriptions mention `as child of <parent tag>` / `reparented` / `moved to Plant X (with N child(ren))` when relevant.

## 4. Details page

- `DetailsModel`: add `List<AssetItem> Children`; load via `GetChildrenAsync`; add `OnPostDetachChildAsync(int id, int childId)` (Admin/Manager) → `SetParentAsync`, activity log + `TempData["Success"]`, stay on the Details page.
- Wrap `OnPostDeleteAsync`'s `DeleteAsync` call: catch `AssetHasChildrenException` → `TempData["Error"]` ("Cannot delete: asset has N child asset(s). Detach them first.") and redirect back to Details instead of Index.
- `Details.cshtml` Asset Register tab: new "Parent / Child" section (below Location) — **Parent** row (link to parent Details, or "—"), **Child Assets** list (each linked, with per-row "Detach" for Admin/Manager) + an "Add child asset" button → `/AssetItems/Create?plantId=<plant>&parentId=<id>` (Create pre-selects both), and a hint that deleting an asset with children is blocked.
- `Delete.cshtml.cs`: same exception catch → `TempData["Error"]` → redirect to Index.

## 5. Global TempData banner + sidebar nesting

- **`_Layout.cshtml`**: render `TempData["Success"]`/`TempData["Error"]` alerts above `@RenderBody()` (shared banner; consistent with the per-page pattern in `Admin/Roles/Index.cshtml`).
- **`PlantService`**: `PlantTreeAsset` gains `ParentAssetId`; `GetTreeAsync` selects it (keep flat list output).
- **`_Layout.cshtml` asset tree**: build the nesting in the view — group each plant's assets by `ParentAssetId`, render roots, then children recursively (a small local recursive rendering block, or a `_AssetTreeNode.cshtml` partial for readability). Child rows indent under the parent with a branch glyph, keep the existing `plant-selected` highlight logic and the "+ add asset" top-level link. Clicking a child still goes to `?plantId=…&assetId=…` (right-panel asset summary) — unchanged.

## 6. Tests

**`SqliteTestSupport.cs`** — add `ParentAssetId INTEGER NULL` to the in-memory `AssetItems` DDL; extend `AddAsset` (optional `int? parentId`) and add `SetParent(id, parentId)`.

**New `tests/.../Services/ParentChildTests.cs`** (SqliteDapperContext + SqliteTestDialect, matching `DeleteAsyncTests` style):
- Create with valid same-plant parent succeeds; child row stores `ParentAssetId`.
- Create with cross-plant parent throws.
- Create with a missing parent throws.
- Update: self-parent throws; cycle (A→B then B's parent=A) throws.
- Update: plant change cascades to child and grandchild (all rows in new plant); a parent in the old plant causes a hard validation error with no write.
- Delete: parent with children throws `AssetHasChildrenException` and nothing is deleted; childless asset deletes normally.
- `GetChildrenAsync` returns only direct children.

## Verification

Run:

```bash
dotnet build AIMS.sln
dotnet test tests/AIMS.WebFrontend.Tests/AIMS.WebFrontend.Tests.csproj
```

Then manually verify against the local Oracle container:

- Guarded `ParentAssetId` DDL applies idempotently on restart.
- Create/edit parent selector (same-plant options only, rebuilt on Plant change).
- Details Parent/Child section, detach, blocked delete.
- Sidebar Asset Tree nests children under parents.
- Re-planting a childed asset whose parent stays behind shows the hard validation error and leaves all rows unchanged.

## Scope Notes

- No new page or route beyond handlers on existing pages.
- `AssetItemRemarks`/`AssetItemService.GetRemarksAsync`/`AddRemarkAsync` remain unreachable from the UI as before — not part of this feature.
- Deleting is blocked only for assets with children; there is no recursive delete.