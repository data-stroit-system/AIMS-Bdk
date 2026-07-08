# Work Summary — 2026-07-08

## 1. Ran the app locally end-to-end

- Started the existing `aims-oracle` podman container (Oracle XE 11g), waited out its first-boot initialization, then ran `AIMS.WebFrontend` with `dotnet run` against it.
- Hit the known stale-password gotcha (`appsettings.json` has `Password=del123`; the container's real password is `oracle`) and worked around it with a `ConnectionStrings__Oracle` env var override, per the existing `CLAUDE.md` note.
- Verified via raw HTTP (`curl`/Python `urllib`, no headless browser available in this environment): `/` redirects unauthenticated users to `/Account/Login` (302), the login page renders with correct SIMS branding, and the seeded `admin`/`Admin@123` credentials log in successfully.

## 2. Plant side menu — sort by Code

- `PlantService.GetTreeAsync()` (used only by the Asset Tree sidebar) ordered Plants by `Name` alphabetically, so "Plant 15" sorted before "Plant 2". Changed to `ORDER BY Code, Name`, matching the ordering already used by `PlantService.ListAsync()` for the `/Plants` management page.
- First attempt appeared to not take effect because restarting the app with `dotnet run --no-build` reused a stale copy of `AIMS.Infrastructure.dll` already sitting in `AIMS.WebFrontend`'s bin output; a full rebuild fixed it. Verified the sidebar now lists Plants in true numeric order (1, 2, 3, 4, 5, 6, 15, 16, 17, 19, 20…).
- Commit `ad0dcf6`.

## 3. Removed Condition (GVI)/Inspection and Picture upload from the Edit Asset page

- `Pages/AssetItems/Edit.cshtml`/`.cs` had its own Condition (GVI) & Inspection fields and a legacy single-Picture upload, duplicating the dedicated Condition tab (`EditCondition.cshtml`), which now manages that data plus a picture carousel.
- Removed the section, the `enctype="multipart/form-data"` it required, and the now-unused `IWebHostEnvironment`/`FileUploadHelper` dependencies from the page model.
- The important part: since `AssetItemService.UpdateAsync` does a full-row `UPDATE`, simply dropping those `Input` fields would have silently nulled out Condition/Inspector/Picture data on every save from this page. Fixed by carrying `DateOfInspection`/`Inspector`/`Condition`/`Comment`/`PicturePath` over unchanged from the already-fetched entity instead.
- Verified live: edited an asset's Title/Equipment/Civil fields via the page and confirmed its Condition ("Good") was untouched afterward. Along the way, a test script bug of mine (blind-posted an empty `Input.PlantId`) briefly nulled out asset 1's Plant assignment — caught and restored (Plant 1 / Gas Purification Section) before moving on; also surfaced a pre-existing, separate bug where this page's Plant `<select>` doesn't pre-select the asset's current plant on load (raw `<option>` tags without `asp-items`, left unfixed — flagged to the user).
- Commit `1835be7`.

## 4. Publish/deploy tooling for Ubuntu + nginx + local Oracle

Built out `deploy/` iteratively, changing approach twice after hitting real environment limitations:

- **v1 (dev-machine-driven, abandoned):** `deploy.sh` published locally and `rsync`/`ssh`'d the result to a remote host, provisioning via a heredoc piped over `ssh -t`. Broke in practice: password-based SSH can't satisfy an interactive `sudo` prompt through `rsync`'s non-tty internal `ssh` (falls back to a nonexistent `ssh-askpass` GUI helper), and even after moving to key-based auth, piping the provisioning script over `ssh`'s stdin left no channel free for `sudo` to prompt on, even with `-t`.
- **v2 (current):** `deploy/deploy.sh` now runs directly **on the target server** in an SSH session (no more `ssh`/`rsync`/`scp` inside it at all), so `sudo` just prompts normally in a real terminal. It publishes straight into a new timestamped `/opt/aims/releases/<id>/`, installs the ASP.NET Core runtime + nginx + a dedicated `aims` system user on first run (idempotent), symlinks persistent data (`appsettings.Production.json`, `wwwroot/asset-pictures`, `wwwroot/asset-documents`) in from `/opt/aims/shared/`, flips `/opt/aims/current`, writes/reloads the systemd unit and nginx site, prunes old releases, and health-checks `/Account/Login`.
- Added `deploy/upload.sh` (runs on the dev machine) to get source onto the server for `deploy.sh` to build from: zips the repo with `python3 -m zipfile` (excluding `.git`, `bin`/`obj`, IDE dirs, logs, `deploy.conf`, and the runtime upload dirs — no dependency on the `zip`/`unzip` CLIs, consistent with the existing `.pptx`-extraction convention noted in `CLAUDE.md`), `scp`s it over, and extracts it remotely the same way.
- Fixed a real bug this surfaced in `Program.cs`: `UseHttpsRedirection()`/`UseHsts()` were called outside Development, which would redirect-loop every request forever under this plain-HTTP-only nginx setup (no TLS anywhere in the chain). Removed both, with a comment explaining why, since this deployment model deliberately has no TLS.
- `deploy/deploy.conf(.example)` now holds both scripts' settings in one gitignored file (`REMOTE_HOST`/`REMOTE_SSH_*`/`REMOTE_DIR` for `upload.sh`; `APP_USER`/`BASE_DIR`/`SERVICE_NAME`/etc. for `deploy.sh`).
- Not yet verified end-to-end against the real target (`deli@192.168.0.8`) from within this session — `upload.sh`'s `scp`/`ssh` calls don't need `sudo` over a piped connection, so they should sidestep the earlier failure mode, but that still needs a live run to confirm.
- The user separately committed the working v2 scripts (`ec75008`) and added `deploy/setup_oracle_xe.sh` (`1371ac6`, not written by me) — a podman-based Oracle XE 11g provisioning script that installs podman, starts/reuses the container, unlocks `SYSTEM`, and creates/verifies an `aims`/`del123` DB user with `CONNECT, RESOURCE` — for setting up Oracle on a target environment.

## 5. Stopped seeding Plants from a static lookup table

- `DatabaseInitializer.cs`/`OracleSchemaInitializer.cs` re-inserted from a hardcoded 49-row `PlantCode.All` list into the `Plants` table on every startup (idempotent, but redundant now that Plants are a fully DB-backed entity managed via the `/Plants` CRUD pages).
- Removed the seeding loop; the method (`MigrateLegacyPlantData` → renamed `BackfillPlantIdFromLegacyColumn`) now only does its other job — backfilling `AssetItems.PlantId` from the legacy free-text `PlantCode` column and dropping it.
- Deleted the now-unreferenced `PlantCode` class from `LookupCodes.cs`.
- Verified: app still builds and boots cleanly, and all 49 previously-seeded plants remain in the DB (removing the seed step only stops *future* re-inserts, it doesn't touch existing rows).
- Commit `2986cdf`.

## Notes / follow-ups

- CLAUDE.md was updated alongside each change (deployment model + rationale, `Program.cs`'s TLS-redirect note, and the corrected `AIMS.Core`/`LookupCodes.cs` description now that `PlantCode` is gone).
- Known pre-existing bug, not fixed today: the Plant `<select>` on `Edit.cshtml` doesn't pre-select the asset's current plant on page load (see §3).
- `deploy/upload.sh`'s end-to-end path (zip → scp → extract → `deploy.sh` on the server) still needs a live run against `deli@192.168.0.8` to confirm it actually works now that the sudo/pty issue is designed around rather than patched.
