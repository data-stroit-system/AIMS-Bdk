---
name: aims-clean-start
description: Use when the user wants to start/restart the AIMS (SIMS) .NET app cleanly with a fresh Oracle database — e.g. "start the app clean", "rebuild restart the app", "stop and remove the podman container for oracle", "wipe the database". Covers stopping the app + the oracle-xe podman container, recreating a fresh one, waiting for Oracle readiness, cleaning/building the solution, launching the app detached via setsid, and verifying HTTP.
---

# AIMS Clean Start (fresh Oracle DB)

Full reset sequence: stop the running app, destroy the podman Oracle container
(no volumes => removal wipes all data), recreate it, wait until Oracle actually
answers a query, clean-rebuild the solution, then launch the app detached and
verify.

## Why this exists

- `oracle-xe` (image `oracleinanutshell/oracle-xe-11g`) is created with **no
  volumes**, so `podman rm` wipes the database — that IS the clean start.
- App defaults to `DatabaseProvider: Oracle` in `appsettings.json`, but the
  checked-in connection string has a stale `Password=del123`; the container's
  real password is `oracle`, so override it via the env var.
- Running `dotnet run` under the bash tool and letting a tool call time out
  kills the process with it — use `setsid -f` + redirect + `</dev/null` so it
  survives.
- A `dotnet build` right after heavy prior builds can die with
  `Fatal error. / Internal CLR error. (0x80131506)` from stale `obj`/`bin`;
  `dotnet clean` first fixes it.

## Steps

### 1. Stop the app

```bash
APP_PID=$(ps -ef | grep 'dotnet run' | grep -v grep | awk '{print $2}' | head -1)
[ -n "$APP_PID" ] && kill $APP_PID
sleep 3
ps -ef | grep -E 'dotnet run|AIMS.WebFrontend' | grep -v grep || echo "app stopped"
```

### 2. Remove the Oracle container

```bash
podman stop oracle-xe && podman rm oracle-xe
podman ps -a | grep -i oracle || echo "oracle container removed"
```

Note: `SIGTERM failed to stop... resorting to SIGKILL` after 10s is normal for
this image.

### 3. Create a fresh one

```bash
podman run -d -p 1521:1521 --name oracle-xe oracleinanutshell/oracle-xe-11g
```

### 4. Wait for Oracle readiness

The listener port opening is NOT enough — schema-init would race the instance
coming up. Poll with a real query (the in-container sqlplus path needs
`ORACLE_HOME`/`PATH` set; local `sqlplus` has no message file):

```bash
for i in $(seq 1 60); do
  if podman exec -e ORACLE_HOME=/u01/app/oracle/product/11.2.0/xe \
      -e PATH=/u01/app/oracle/product/11.2.0/xe/bin:/bin oracle-xe /bin/bash \
      -c 'echo "select 1 from dual;" | sqlplus -s system/oracle@//localhost:1521/xe' \
      | grep -q '^         1$'; then
    echo "Oracle READY after ${i}x5s"; break
  fi
  sleep 5
done
```

If the loop exhausts with no output, just re-run the query directly to check —
fresh XE init can take a couple of minutes.

### 5. Clean-rebuild

```bash
dotnet clean AIMS.sln 2>&1 | tail -2
dotnet build AIMS.sln -c Debug 2>&1 | tail -6
```

Expect `0 Warning(s), 0 Error(s)`. If `dotnet build` alone throws the CLR
`Fatal error`, run `dotnet clean` first (this doubles as the fix).

### 6. Launch detached

```bash
setsid -f env DatabaseProvider=Oracle \
  'ConnectionStrings__Oracle=Data Source=localhost:1521/xe;User Id=system;Password=oracle;' \
  dotnet run --project src/AIMS.WebFrontend --no-launch-profile \
  > /tmp/opencode/aims/app.log 2>&1 < /dev/null
```

### 7. Verify live

```bash
for i in $(seq 1 36); do
  if grep -q "Now listening on" /tmp/opencode/aims/app.log 2>/dev/null; then echo "listening"; break; fi
  sleep 5
done
curl -s -o /dev/null -w "root: HTTP %{http_code} -> %{redirect_url}\n" http://localhost:5000/
curl -s -o /dev/null -w "login: HTTP %{http_code}\n" -L http://localhost:5000/
```

Expect `/` → `302 -> http://localhost:5000/Account/Login?ReturnUrl=%2F`, then
`-L` login → `200`. Process must be alive (detached session, separate PID).

## Seeded login

The fresh DB is initialized by `services.InitializeDatabase()` on startup —
only seeded content exists: `admin` / `Admin@123`.

## Gotchas

- Never edit `src/AIMS.WebFrontend/appsettings.json`'s Oracle password; always
  pass `ConnectionStrings__Oracle` as an environment override.
- App listens on `http://localhost:5000` by default (no launch profile).
- Serilog writes to `Logs/aims-.log` plus the console; the detached run's
  stdout copies to `/tmp/opencode/aims/app.log`.
- Any data created in a previous session (assets, users, etc.) is gone after
  step 2 — that's expected for a clean start.