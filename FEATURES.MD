#2. Database Providers

# How to switch providers
## SQL Server
set DATABASE_PROVIDER=SqlServer
podman compose --profile sqlserver up -d

## Oracle
set DATABASE_PROVIDER=Oracle
podman compose --profile oracle up -d

(In PowerShell use $env:DATABASE_PROVIDER="Oracle" instead of set. Note: DatabaseProvider currently defaults to Oracle if you don't set the env var at all, so for SQL Server you must set DATABASE_PROVIDER=SqlServer explicitly.)

Useful follow-ups

podman compose --profile oracle ps          REM check container status
podman compose --profile oracle logs -f webfrontend   REM follow app logs
podman compose --profile oracle down        REM stop and remove containers

---------------------------------------------------------------------------------

When you add a new entity, do this:

1. Add the CREATE TABLE block to the Schema constant
In DatabaseInitializer.cs:20, append a new IF OBJECT_ID(...) IS NULL CREATE TABLE block for your entity, following the same pattern as the existing tables:


IF OBJECT_ID('YourNewTable', 'U') IS NULL
CREATE TABLE YourNewTable (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    -- your columns here
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy nvarchar(200) NULL
);
2. For existing databases — add column/alter statements
If the database already exists and you're adding columns to an existing table, use this pattern:


IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ExistingTable') AND name = 'NewColumn')
    ALTER TABLE ExistingTable ADD NewColumn nvarchar(250) NULL;
3. Restart the app — Initialize() runs on startup
The DatabaseInitializer.Initialize() is called during app startup via services.InitializeDatabase() in StartupSetup.cs:31. It re-runs every time, but the IF OBJECT_ID ... IS NULL guards make the DDL idempotent, so it's safe.

Summary of the workflow:

New table → append IF OBJECT_ID ... IS NULL CREATE TABLE block to Schema
New column on existing table → append IF NOT EXISTS ... ALTER TABLE ... ADD block to Schema
Run the app → schema applies automatically
