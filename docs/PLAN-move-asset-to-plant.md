# Move AssetItem to Another Plant

## Goal

Allow an Admin or Manager to move an existing AssetItem to a different Plant. The Asset Tag No. must be regenerated automatically from the destination Plant code while preserving the asset's existing asset code, order, and category.

## Implementation Plan

### 1. Add a focused service operation

File: `src/AIMS.Infrastructure/Services/AssetItemService.cs`

Add `MoveToPlantAsync(int id, int newPlantId)` that:

- Loads the existing AssetItem.
- Changes only its `PlantId` in memory.
- Regenerates `AssetId` through the existing server-side generation logic.
- Checks tag uniqueness while excluding the current asset ID.
- Updates only `AssetId` and `PlantId`.
- Returns the old and new tag values, or `null` if the asset does not exist.
- Preserves the existing `DuplicateAssetIdException` behavior when the destination tag already exists.

The destination must be an actual, different Plant. Unassigning an asset from a Plant is not part of this feature.

### 2. Add the move handler to Details

File: `src/AIMS.WebFrontend/Pages/AssetItems/Details.cshtml.cs`

- Add a bound input model containing `TargetPlantId`.
- Load the available Plants for the move form.
- Add `OnPostMoveAsync(int id)`.
- Authorize Admin and Manager roles only.
- Validate that a target Plant was selected and differs from the current Plant.
- Call `AssetItemService.MoveToPlantAsync`.
- Display duplicate-tag and validation errors while keeping the modal open.
- Log an `AssetItemMoved` activity containing the old tag, destination Plant, and new tag.
- Redirect back to the Asset Details page after a successful move.

### 3. Add the Details-page modal

File: `src/AIMS.WebFrontend/Pages/AssetItems/Details.cshtml`

- Add a `Move to Plant` button beside Edit/Delete for Admin and Manager users.
- Add a Bootstrap modal with a destination Plant dropdown.
- Display Plants using the existing `Code - Description` convention.
- Show a read-only live Tag No. preview based on the selected Plant, existing AssetCode, AssetOrder, and Category.
- Automatically reopen the modal when the server returns validation or duplicate-tag errors.

### 4. Add service tests

Files:

- `tests/AIMS.WebFrontend.Tests/Services/MoveToPlantAsyncTests.cs`
- `tests/AIMS.WebFrontend.Tests/SqliteTestSupport.cs`

Add coverage for:

1. Moving an asset changes `PlantId` and regenerates the tag.
2. Foundation assets retain the `-Q` suffix after moving.
3. A destination tag collision throws `DuplicateAssetIdException` and leaves the original row unchanged.
4. Moving a missing asset returns `null`.

Extend the SQLite test helper's asset seed method only as needed to specify asset code, asset order, and category while keeping existing callers compatible.

## Verification

Run:

```bash
dotnet build AIMS.sln
dotnet test tests/AIMS.WebFrontend.Tests/AIMS.WebFrontend.Tests.csproj
```

Then manually verify the Details-page flow: open Move, select a destination Plant, confirm the preview, save, and confirm the new tag and QR/print tag output. Also verify the duplicate-tag error path.

## Scope Notes

- No database schema or initializer changes are needed.
- No new page or route is needed.
- Existing full Edit behavior remains unchanged.
