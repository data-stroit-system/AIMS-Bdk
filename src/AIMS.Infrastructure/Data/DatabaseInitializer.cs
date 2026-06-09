using Dapper;

namespace AIMS.Infrastructure.Data;

public class DatabaseInitializer : ISchemaInitializer
{
    private readonly IDapperContext _context;

    public DatabaseInitializer(IDapperContext context)
    {
        _context = context;
    }

    public void Initialize()
    {
        using var conn = _context.CreateConnection();
        conn.Execute(Schema);
    }

    private const string Schema = @"
IF OBJECT_ID('AspNetRoles', 'U') IS NULL
CREATE TABLE AspNetRoles (
    Id nvarchar(450) NOT NULL PRIMARY KEY,
    Description nvarchar(250) NULL,
    Name nvarchar(256) NULL,
    NormalizedName nvarchar(256) NULL,
    ConcurrencyStamp nvarchar(max) NULL
);

IF OBJECT_ID('AspNetUsers', 'U') IS NULL
CREATE TABLE AspNetUsers (
    Id nvarchar(450) NOT NULL PRIMARY KEY,
    FullName nvarchar(250) NULL,
    JobTitle nvarchar(250) NULL,
    UserName nvarchar(256) NULL,
    NormalizedUserName nvarchar(256) NULL,
    Email nvarchar(256) NULL,
    NormalizedEmail nvarchar(256) NULL,
    EmailConfirmed bit NOT NULL DEFAULT 0,
    PasswordHash nvarchar(max) NULL,
    SecurityStamp nvarchar(max) NULL,
    ConcurrencyStamp nvarchar(max) NULL,
    PhoneNumber nvarchar(max) NULL,
    PhoneNumberConfirmed bit NOT NULL DEFAULT 0,
    TwoFactorEnabled bit NOT NULL DEFAULT 0,
    LockoutEnd datetimeoffset NULL,
    LockoutEnabled bit NOT NULL DEFAULT 0,
    AccessFailedCount int NOT NULL DEFAULT 0
);

IF OBJECT_ID('AssetItems', 'U') IS NULL
CREATE TABLE AssetItems (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title nvarchar(150) NULL,
    AssetId nvarchar(max) NULL,
    Description nvarchar(250) NULL,
    Type int NOT NULL DEFAULT 0,
    Location nvarchar(250) NULL,
    Priority int NOT NULL DEFAULT 0,
    IntegrityStatus int NOT NULL DEFAULT 0,
    PicturePath nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy nvarchar(200) NULL
);

IF OBJECT_ID('AuditLogs', 'U') IS NULL
CREATE TABLE AuditLogs (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Category nvarchar(50) NULL,
    EntityName nvarchar(100) NULL,
    EntityId nvarchar(50) NULL,
    Action nvarchar(50) NULL,
    OldValues nvarchar(max) NULL,
    NewValues nvarchar(max) NULL,
    ChangedColumns nvarchar(max) NULL,
    Description nvarchar(500) NULL,
    UserId nvarchar(256) NULL,
    UserName nvarchar(256) NULL,
    Timestamp datetime2 NOT NULL DEFAULT GETUTCDATE(),
    IpAddress nvarchar(50) NULL,
    UserAgent nvarchar(500) NULL,
    RequestPath nvarchar(500) NULL,
    Result nvarchar(50) NULL
);

IF OBJECT_ID('ToDoItems', 'U') IS NULL
CREATE TABLE ToDoItems (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title nvarchar(max) NOT NULL,
    Description nvarchar(max) NULL,
    IsDone bit NOT NULL DEFAULT 0
);

IF OBJECT_ID('AspNetRoleClaims', 'U') IS NULL
CREATE TABLE AspNetRoleClaims (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RoleId nvarchar(450) NOT NULL,
    ClaimType nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL,
    CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
);

IF OBJECT_ID('AspNetUserClaims', 'U') IS NULL
CREATE TABLE AspNetUserClaims (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId nvarchar(450) NOT NULL,
    ClaimType nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL,
    CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);

IF OBJECT_ID('AspNetUserLogins', 'U') IS NULL
CREATE TABLE AspNetUserLogins (
    LoginProvider nvarchar(450) NOT NULL,
    ProviderKey nvarchar(450) NOT NULL,
    ProviderDisplayName nvarchar(max) NULL,
    UserId nvarchar(450) NOT NULL,
    CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);

IF OBJECT_ID('AspNetUserRoles', 'U') IS NULL
CREATE TABLE AspNetUserRoles (
    UserId nvarchar(450) NOT NULL,
    RoleId nvarchar(450) NOT NULL,
    CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE,
    CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);

IF OBJECT_ID('AspNetUserTokens', 'U') IS NULL
CREATE TABLE AspNetUserTokens (
    UserId nvarchar(450) NOT NULL,
    LoginProvider nvarchar(450) NOT NULL,
    Name nvarchar(450) NOT NULL,
    Value nvarchar(max) NULL,
    CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);

IF OBJECT_ID('AssetItemDocuments', 'U') IS NULL
CREATE TABLE AssetItemDocuments (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    DocumentTitle nvarchar(250) NULL,
    FilePath nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy nvarchar(200) NULL,
    AssetItemId int NULL,
    CONSTRAINT FK_AssetItemDocuments_AssetItems_AssetItemId FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
);

IF OBJECT_ID('AssetRemarks', 'U') IS NULL
CREATE TABLE AssetRemarks (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Description nvarchar(250) NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy nvarchar(200) NULL,
    AssetItemId int NULL,
    CONSTRAINT FK_AssetRemarks_AssetItems_AssetItemId FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'RoleNameIndex' AND object_id = OBJECT_ID('AspNetRoles'))
    CREATE UNIQUE INDEX RoleNameIndex ON AspNetRoles(NormalizedName) WHERE NormalizedName IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UserNameIndex' AND object_id = OBJECT_ID('AspNetUsers'))
    CREATE UNIQUE INDEX UserNameIndex ON AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'EmailIndex' AND object_id = OBJECT_ID('AspNetUsers'))
    CREATE INDEX EmailIndex ON AspNetUsers(NormalizedEmail);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_EntityName' AND object_id = OBJECT_ID('AuditLogs'))
    CREATE INDEX IX_AuditLogs_EntityName ON AuditLogs(EntityName);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_EntityId' AND object_id = OBJECT_ID('AuditLogs'))
    CREATE INDEX IX_AuditLogs_EntityId ON AuditLogs(EntityId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_Timestamp' AND object_id = OBJECT_ID('AuditLogs'))
    CREATE INDEX IX_AuditLogs_Timestamp ON AuditLogs(Timestamp);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_UserId' AND object_id = OBJECT_ID('AuditLogs'))
    CREATE INDEX IX_AuditLogs_UserId ON AuditLogs(UserId);
";
}
