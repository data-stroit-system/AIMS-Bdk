# AIMS-Bdk -- Project Study

**AIMS (Asset/Infrastructure Management System)** -- branded as **SIMS** (Structural Integrity Management System) in the UI.
A .NET 10 web application built with Clean Architecture / SOLID principles, Razor Pages, Dapper micro-ORM, and Autofac DI.

---

## 1. Solution Structure

```
AIMS-Bdk/
├── AIMS.sln                          # Visual Studio 2022 Solution
├── README.md                         # Brief architecture overview
├── FEATURES.MD                       # Database provider switching guide
├── Copilot-Q.md                      # Task list / feature backlog (10 items)
├── docker-compose.yml                # Multi-profile (Oracle, SqlServer) + web app
├── .dockerignore / .gitignore
├── .claude/                          # Claude/opencode agent settings
├── .vscode/                          # VS Code launch & settings
│
└── src/
    ├── AIMS.Core/                    # Domain layer
    ├── AIMS.Infrastructure/          # Data access & cross-cutting concerns
    ├── AIMS.SharedKernel/            # Shared base classes & interfaces
    ├── AIMS.WebFrontend/             # ASP.NET Core Razor Pages UI
    ├── AIMS.Migrations.SqlServer/    # (empty shell)
    ├── AIMS.Migrations.Oracle/       # (empty shell)
    └── AIMS.Migrations.PostgreSQL/   # (empty shell)
```

| Project | Target | Key Dependencies |
|---|---|---|
| **AIMS.SharedKernel** | `net10.0` | (none) |
| **AIMS.Core** | `net10.0` | Ardalis.GuardClauses, System.ComponentModel.Annotations, `→AIMS.SharedKernel` |
| **AIMS.Infrastructure** | `net10.0` | Dapper, Autofac, Microsoft.Data.SqlClient, Oracle.ManagedDataAccess.Core, FluentFTP, `Microsoft.AspNetCore.App`, `→AIMS.Core`, `→AIMS.SharedKernel` |
| **AIMS.WebFrontend** | `net10.0` | ASP.NET Core Razor Pages, `→AIMS.Core`, `→AIMS.Infrastructure`, `→AIMS.SharedKernel` |

---

## 2. Architecture (Clean Architecture / SOLID)

```
┌─────────────────────────────────────────────────┐
│                 AIMS.WebFrontend                  │
│         (Razor Pages UI, Controllers)             │
├─────────────────────────────────────────────────┤
│                 AIMS.Infrastructure               │
│   (Dapper Repos, Identity Stores, Audit, FTP,     │
│    Schema Init, DI Wiring)                        │
├────────────────┬────────────────────────────────┤
│   AIMS.Core    │     AIMS.SharedKernel           │
│  (Entities,    │   (BaseEntity, ValueObject,     │
│   DTOs, Events,│    IRepository, IHandle<T>,     │
│   Handlers,    │    IActivityLogger, etc.)       │
│   Services)    │                                 │
└────────────────┴────────────────────────────────┘
```

---

## 3. AIMS.SharedKernel -- Building Blocks

**Location:** `src/AIMS.SharedKernel/`

### Base Classes

| Type | Description |
|---|---|
| `BaseEntity` (abstract) | `int Id { get; set; }`, `List<BaseDomainEvent> Events` for domain event collection |
| `BaseDomainEvent` (abstract) | `DateTime DateOccurred { get; }` initialized to `DateTime.UtcNow` |
| `ValueObject` (abstract) | Structural equality via reflection; `IEquatable<ValueObject>`; supports `[IgnoreMemberAttribute]` |
| `IgnoreMemberAttribute` | Attribute to exclude properties/fields from `ValueObject` equality |

### Interfaces

| Interface | Method(s) |
|---|---|
| `IRepository` | `GetById<T>(int)`, `List<T>()`, `Add<T>(T)`, `Update<T>(T)`, `Delete<T>(T)` where `T : BaseEntity` |
| `IHandle<in T>` | `Task Handle(T domainEvent)` where `T : BaseDomainEvent` |
| `IDomainEventDispatcher` | `Task Dispatch(BaseDomainEvent)` |
| `IAuditUserProvider` | `GetUserId()`, `GetUserName()`, `GetIpAddress()` -- all return `string?` |
| `IActivityLogger` | `LogActivityAsync(...)`, `LogSecurityActivityAsync(...)` |

---

## 4. AIMS.Core -- Domain Layer

**Location:** `src/AIMS.Core/`

### Entities

#### AssetItem (`Entities/AssetItem.cs:33-60`)
Principal domain entity for structural integrity assets.

| Property | Type / MaxLength | Notes |
|---|---|---|
| `Id` | `int` (PK) | Inherited from `BaseEntity` |
| `Title` | `string` (150) | |
| `AssetId` | `string` | |
| `Description` | `string` (250) | |
| `Type` | `AssetType` enum | Pipe=1, PSV=2, PressureTank=3, Other=4 |
| `Location` | `string` (250) | |
| `Priority` | `AssetPriority` enum | Low=1, Medium=2, High=3 |
| `IntegrityStatus` | `IntegrityStatus` enum | Good=1, Fair=2, Poor=3 |
| `PicturePath` | `string?` (500) | |
| `CreatedAt` / `CreatedBy` | `DateTime` / `string` (200) | |
| `AssetItemRemarks` | `List<AssetItemRemarks>` | Navigation (1→many) |
| `AssetItemDocuments` | `List<AssetItemDocuments>` | Navigation (1→many) |

**Method:** `UpdateStatus(IntegrityStatus)` -- sets `IntegrityStatus` and raises `AssetItemStatusUpdateEvent`.

#### AssetItemRemarks (`Entities/AssetItem.cs:22-32`)
| Property | Type | Notes |
|---|---|---|
| `Description` | `string` (250) | |
| `CreatedAt` / `CreatedBy` | `DateTime` / `string` (200) | `CreatedBy` = name of user who input the remark |
| `AssetItem` | `AssetItem` | Navigation back to parent |

#### AssetItemDocuments (`Entities/AssetItem.cs:9-20`)
| Property | Type | Notes |
|---|---|---|
| `DocumentTitle` | `string` (250) | |
| `FilePath` | `string` (500) | |
| `CreatedAt` / `CreatedBy` | `DateTime` / `string` (200) | |
| `AssetItem` | `AssetItem` | Navigation back to parent |

#### AuditLog (`Entities/AuditLog.cs:9-98`)
Full audit trail entity (standalone, not extending `BaseEntity`).

| Property | Type | Notes |
|---|---|---|
| `Category` | `string` (50) | "Entity", "Activity", or "Security" |
| `EntityName` | `string` (100) | |
| `EntityId` | `string` (50) | |
| `Action` | `string` (50) | e.g. Created, Updated, Deleted |
| `OldValues` / `NewValues` | `string?` (JSON) | |
| `ChangedColumns` | `string?` | |
| `Description` | `string?` (500) | |
| `UserId` / `UserName` | `string?` (256) | |
| `Timestamp` | `DateTime` | Default `DateTime.UtcNow` |
| `IpAddress` | `string?` (50) | |
| `UserAgent` | `string?` (500) | |
| `RequestPath` | `string?` (500) | |
| `Result` | `string?` (50) | |

**Constants:** `AuditCategory` (Entity/Activity/Security), `AuditAction` (Created/Updated/Deleted), `ActivityType` (Login, Logout, UserCreated, RoleAssigned, etc. -- 19 types).

#### ToDoItem (`Entities/ToDoItem.cs:9-20`)
Sample/seed entity. `Title`, `Description`, `IsDone` (private set). `MarkComplete()` sets `IsDone=true` and raises `ToDoItemCompletedEvent`.

#### UserAccount (`Entities/UserAccount.cs:6-17`)
Legacy credential entity. `UserName`, `Email`, `FullName`, `Password`. Not used by the Identity system.

### Domain Events

| Event | Raised By |
|---|---|
| `ToDoItemCompletedEvent` | `ToDoItem.MarkComplete()` |
| `AssetItemStatusUpdateEvent` | `AssetItem.UpdateStatus()` |

### Handlers

| Handler | Handles | Current Behavior |
|---|---|---|
| `ItemCompletedEmailNotificationHandler` | `ToDoItemCompletedEvent` | Stub (no-op placeholder) |

### Services

| Service | Key Methods |
|---|---|
| `TodoItemServices` | `AddTodoItem`, `Update`, `MarkComplete`, `GetById`, `GetAll` |
| `SomeDomainServices` | Empty placeholder class |

### DTOs

| DTO | Fields |
|---|---|
| `ToDoItemDTO` | `Id`, `Title`, `Description`, `IsDone` + `FromToDoItem()` factory |

### Database Populator

`DatabasePopulator.PopulateDatabase(IRepository)` -- seeds 3 sample ToDo items if fewer than 5 exist.

---

## 5. AIMS.Infrastructure -- Data Access & Cross-Cutting

**Location:** `src/AIMS.Infrastructure/`

### Database Abstraction (Strategy Pattern)

Supports **SQL Server** and **Oracle** via provider switching (`DatabaseProvider` config key).

```
IDapperContext ─┬── DapperContext (SqlServer)
                └── OracleDapperContext (Oracle)

ISqlDialect ───┬── SqlServerDialect   ([] quoting, OUTPUT INSERTED.Id, OFFSET/FETCH pagination)
                └── OracleDialect      (no quoting, RETURNING Id, ROW_NUMBER() pagination)

ISchemaInitializer ─┬── DatabaseInitializer      (SQL Server DDL)
                     └── OracleSchemaInitializer  (Oracle DDL with EXECUTE IMMEDIATE + ORA-00955 guards)
```

Connection strings are read from `appsettings.json`:
- `ConnectionStrings:SqlServer` -- `Server=localhost;Database=aimsdb;...`
- `ConnectionStrings:Oracle` -- `Data Source=localhost:1521/xe;...`

### Oracle Compatibility Layer

- `OracleParamConnection` wraps `OracleConnection` to rewrite `@Name` Dapper parameters to `:Name` format
- `OracleParamCommand` converts `DbType.Boolean` to `Int32` (1/0) to avoid ORA-03115 errors
- All DDL uses `BEGIN EXECUTE IMMEDIATE '...'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;` for idempotent schema creation
- Sequences + before-insert triggers replace IDENTITY columns

### Repository (`Data/DapperRepository.cs`)

`DapperRepository : IRepository` -- Generic CRUD via Dapper. Uses `ISqlDialect` for cross-database compatibility. Table name convention: `typeof(T).Name + "s"`. Excludes navigation/complex properties from mapping.

### ASP.NET Core Identity (Dapper-backed, No EF Core)

| Class | Implements |
|---|---|
| `ApplicationUser : IdentityUser` | +`FullName`, `+JobTitle` |
| `ApplicationRole : IdentityRole` | +`Description` |
| `DapperUserStore` | `IUserStore`, `IUserPasswordStore`, `IUserEmailStore`, `IUserRoleStore`, `IUserSecurityStampStore`, `IUserLockoutStore`, `IUserClaimStore`, `IUserLoginStore`, `IUserTwoFactorStore`, `IUserPhoneNumberStore`, `IUserAuthenticatorKeyStore`, `IQueryableUserStore` (~25 async methods) |
| `DapperRoleStore` | `IRoleStore`, `IQueryableRoleStore`, `IRoleClaimStore` |

### Audit Trail

| Class | Implements |
|---|---|
| `HttpContextAuditUserProvider` | `IAuditUserProvider` -- extracts user info from `HttpContext` (claims, identity, IP with X-Forwarded-For support) |
| `ActivityLogger` | `IActivityLogger` -- inserts audit records into `AuditLogs` table with full request context |

### Services

**`AssetItemService`** -- Full CRUD with paging and filtering:
- `GetPagedAsync(searchTerm?, typeFilter?, priorityFilter?, statusFilter?, page, pageSize)` → `(List<AssetItem>, int totalCount)`
- `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`
- `GetRemarksAsync`, `AddRemarkAsync`
- `GetDocumentsAsync`, `GetDocumentByIdAsync`, `AddDocumentAsync`, `DeleteDocumentAsync`
- Uses `ISqlDialect.Paginate()` for cross-database paging

### File Handling

| Class | Purpose |
|---|---|
| `FileUploadHelper` | Picture validation (image types, 2MB max), save to `wwwroot/asset-pictures/`, delete files, MIME type detection for 10+ extensions |
| `FtpClientWrapper` | FluentFTP wrapper for FTP uploads |

### Domain Event Dispatching

`DomainEventDispatcher : IDomainEventDispatcher` -- resolves all `IHandle<T>` implementations from the DI container and dispatches events asynchronously via inner `DomainEventHandler<T>` adapter classes.

### DI Wiring (`ContainerSetup.cs`)

- `InitializeWeb(assembly, services)` → `IServiceProvider` -- creates `AutofacServiceProvider`, scans assemblies from AIMS.Core, AIMS.Infrastructure, AIMS.SharedKernel
- `BaseAutofacInitialization(setupAction?)` → `IContainer` -- assembly scanning registration of all types as implemented interfaces

### Startup Extensions (`StartupSetup.cs`)

| Extension Method | Purpose |
|---|---|
| `AddDapperContext(services, config)` | Reads `DatabaseProvider` key, registers `IDapperContext`, `ISqlDialect`, `ISchemaInitializer` as singletons |
| `AddAuditTrail(services)` | Registers `IHttpContextAccessor`, `IAuditUserProvider`, `IActivityLogger`, `FileUploadHelper`, `AssetItemService` |
| `InitializeDatabase(services)` | Builds temp provider, runs `ISchemaInitializer.Initialize()` to create tables |
| `SeedRolesAndAdminUserAsync(provider)` | Creates `Admin`, `Manager`, `User` roles and default admin (`admin@aims.local` / `Admin@123`) |

---

## 6. AIMS.WebFrontend -- ASP.NET Core Razor Pages UI

**Location:** `src/AIMS.WebFrontend/`
**Launch URL:** `http://localhost:5069`

### Application Bootstrap (`Program.cs`)

1. `AddDapperContext` -- database provider registration
2. `AddAuditTrail` -- audit & asset services
3. `AddIdentity<ApplicationUser, ApplicationRole>` -- Dapper-backed Identity
4. `AddRazorPages` + `AddHttpClient`
5. `ContainerSetup.InitializeWeb` -- Autofac assembly scanning
6. `InitializeDatabase` -- schema creation
7. `AddAutofac` -- swap to Autofac DI provider
8. `SeedRolesAndAdminUserAsync` -- role/user seeding on startup
9. `UseHttpsRedirection` → `UseStaticFiles` → `UseRouting` → `UseAuthorization` → `MapRazorPages`

### Page Structure

```
Pages/
├── Index.cshtml                    [Dashboard -- Authorize]
├── Privacy.cshtml                  [Privacy]
├── Error.cshtml                    [Error page]
│
├── AssetItems/
│   ├── Index.cshtml                [List -- search, filter, pagination, 10/page]
│   ├── Create.cshtml               [Create -- Admin,Manager only]
│   ├── Edit.cshtml                 [Edit -- Admin,Manager only]
│   ├── Details.cshtml              [Details with Remarks & Documents tabs]
│   └── Delete.cshtml               [Delete POST -- Admin,Manager only]
│
├── Account/
│   ├── Login.cshtml                [Login -- logs security events]
│   ├── Logout.cshtml               [Logout POST -- logs logout]
│   └── AccessDenied.cshtml         [Access Denied]
│
├── Admin/
│   ├── AuditLogs.cshtml            [Audit Trail -- all roles, scoped by role]
│   ├── Roles/
│   │   ├── Index.cshtml            [Role list -- Admin only]
│   │   ├── Create.cshtml           [Create role -- Admin only]
│   │   └── Edit.cshtml             [Edit role -- Admin only]
│   └── Users/
│       ├── Index.cshtml            [User list -- Admin,Manager, search/role filter/paginate]
│       ├── Create.cshtml           [Create user -- Admin only]
│       ├── Edit.cshtml             [Edit user + password reset -- Admin only]
│       └── Roles.cshtml            [Manage user roles -- Admin only]
│
├── MapDemo/
│   └── Index.cshtml                [QGIS WMS GetFeatureInfo proxy]
│
└── Shared/
    ├── _Layout.cshtml              [SB Admin theme, role-based sidebar]
    ├── _Layout.cshtml.css
    ├── _LoginLayout.cshtml         [Centered card layout for login]
    ├── _LoginPartial.cshtml
    └── _ValidationScriptsPartial.cshtml
```

### Role-Based Sidebar Navigation

| Role | Sidebar Sections |
|---|---|
| Unauthenticated | "Login" link only |
| User | Dashboard, Assets, Map Demo, My Account → My Audit Trail |
| Manager | Above + Management: View Users, Audit Trail |
| Admin | Above + Admin: User Management, Role Management, Audit Trail |

### Dashboard (`Index.cshtml.cs`)

- Queries `AssetItems` via raw Dapper
- Shows: total count, priority breakdown (High/Medium/Low), integrity status breakdown (Good/Fair/Poor), grouping by `AssetType`, 5 most recent assets

### AssetItem CRUD Details

- **Index:** Search by AssetId/Description/Type; filters by Type, Priority, Status dropdowns; paginated (10/page)
- **Create/Edit:** Picture upload (GUID filename, 2MB max, image types only)
- **Details:** Two tabs -- "Remarks" (add/view) and "Documents" (upload/view/delete, 10MB max, various MIME types)
- All CRUD operations log via `IActivityLogger`

### Security

- Cookie-based authentication (`/Account/Login`, `/Account/Logout`)
- Login logs both success and failure via `IActivityLogger.LogSecurityActivityAsync`
- Logout logs the event with user identity
- Admin role cannot be renamed or deleted; admin user cannot self-delete

### Audit Trail (`Admin/AuditLogs.cshtml`)

- 25/page pagination
- Filters: Entity Name, Action Type, Username (Admin/Manager only), Date Range
- Non-admin users can only see their own logs
- JSON pretty-print helper for audit data

---

## 7. Database Providers

### Switching Providers

Set `DatabaseProvider` in `appsettings.json` or environment variable:

| Provider | Config Value | Tables Created |
|---|---|---|
| SQL Server | `SqlServer` | 12 tables + indexes (identity columns) |
| Oracle | `Oracle` | 12 tables + 7 sequences + 7 triggers + indexes (NVARCHAR2, CLOB) |

### Table Schema (both providers)

1. `AspNetRoles` -- Identity roles
2. `AspNetUsers` -- Identity users (+ FullName, JobTitle)
3. `AspNetRoleClaims` -- Role claims
4. `AspNetUserClaims` -- User claims
5. `AspNetUserLogins` -- External logins
6. `AspNetUserRoles` -- User-role assignments
7. `AspNetUserTokens` -- Token storage
8. `AssetItems` -- Core asset data
9. `AssetItemDocuments` -- Uploaded documents (FK → AssetItems)
10. `AssetRemarks` -- Asset remarks (FK → AssetItems)
11. `AuditLogs` -- Full audit trail
12. `ToDoItems` -- Sample/seed data

---

## 8. Docker Setup

**File:** `docker-compose.yml`

Three services with profiles:

| Profile | Service | Image | Port |
|---|---|---|---|
| `oracle` | oracle | `oracleinanutshell/oracle-xe-11g` | 1521 |
| `sqlserver` | sqlserver | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 |
| (both) | webfrontend | Built from `src/AIMS.WebFrontend/Dockerfile` | 8081 |

```bash
# SQL Server
$env:DATABASE_PROVIDER="SqlServer"
podman compose --profile sqlserver up -d

# Oracle
$env:DATABASE_PROVIDER="Oracle"
podman compose --profile oracle up -d
```

**Dockerfile** uses multi-stage build: `dotnet/sdk:10.0` → `dotnet publish` → `dotnet/aspnet:10.0` runtime. Creates `wwwroot/asset-pictures` and `wwwroot/asset-documents` directories at runtime.

---

## 9. Default Credentials & Roles

| Role | Description | Permissions |
|---|---|---|
| **Admin** | Full system access | All features: user/role management, asset CRUD, audit trail |
| **Manager** | User management + audit view | Manage users, view audit trails, asset CRUD |
| **User** | Self-service | View own data and audit trails, view assets |

**Default Admin Account:**
- Username: `admin`
- Email: `admin@aims.local`
- Password: `Admin@123`

---

## 10. NuGet Package Summary

| Package | Version | Project |
|---|---|---|
| Ardalis.GuardClauses | 1.3.2 | AIMS.Core |
| System.ComponentModel.Annotations | 5.0.0 | AIMS.Core |
| Autofac | 8.1.0 | AIMS.Infrastructure |
| Autofac.Extensions.DependencyInjection | 10.0.0 | AIMS.Infrastructure |
| Dapper | 2.1.35 | AIMS.Infrastructure |
| FluentFTP | 51.0.0 | AIMS.Infrastructure |
| Microsoft.Data.SqlClient | 6.0.1 | AIMS.Infrastructure |
| Oracle.ManagedDataAccess.Core | 23.7.0 | AIMS.Infrastructure |
| System.Security.Cryptography.Xml | 10.0.7 | AIMS.Infrastructure |
| `Microsoft.AspNetCore.App` | (framework ref) | AIMS.Infrastructure |

---

## 11. Build & Run

```powershell
# Build
dotnet restore AIMS.sln
dotnet build AIMS.sln

# Run (default: Oracle from appsettings.json)
dotnet run --project src/AIMS.WebFrontend

# Run with SQL Server
$env:DatabaseProvider = "SqlServer"
dotnet run --project src/AIMS.WebFrontend
```

Build status: **0 errors**, 74 nullable warnings (CS8613, CS8632, CS8767, CS8603, etc.)

---

## 12. Feature Backlog (from Copilot-Q.md)

Items documented in `Copilot-Q.md` (most appear already implemented):

1. Fix "AspNetRoles already exists" EF migration error
2. System-wide audit trail
3. Role-based access control with User Management page
4. 3 roles (Admin, Manager, User) with default admin
5. Activity logging (login, logout, data changes)
6. AssetItem CRUD page with role-based permissions
7. Search/filter on AssetItem list
8. AssetItem details page with Remarks
9. CreatedBy column on AssetItemRemarks
10. AssetItemDocuments entity with document upload on Details page (tabs)

---

## 13. Key Design Decisions

- **No Entity Framework** -- pure Dapper for data access; schema managed by idempotent DDL on startup
- **No migrations** -- `ISchemaInitializer` runs at startup, DDL is idempotent (IF NOT EXISTS / ORA-00955 guards)
- **Autofac DI** -- assembly scanning for automatic registration, not pure MS DI
- **Clean Architecture** -- Core has no infrastructure dependencies; Infrastructure depends on Core and SharedKernel
- **Dual database** -- SQL Server and Oracle supported via strategy pattern; Oracle uses parameter name rewriting
- **Custom Identity stores** -- ASP.NET Core Identity implemented on Dapper (no EF Core stores)
- **No tests** -- No test projects exist in the solution
