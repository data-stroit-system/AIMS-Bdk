using AIMS.Core.Entities;
using Dapper;
using System.Data;
using System.Linq;

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
        MigratePlantCodeToInt(conn);
        BackfillPlantIdFromLegacyColumn(conn);
        MigrateLegacyIntegrityStatus(conn);
        DropLegacyAssetItemColumns(conn);
        RegenerateAssetIds(conn);
        BackfillDocumentType(conn);
    }

    /// <summary>
    /// Backfills DocumentType for rows uploaded before the column existed, inferring it from
    /// the file extension (jpg/jpeg/png = Picture, everything else = Document). Safe to run
    /// on every startup.
    /// </summary>
    private static void BackfillDocumentType(IDbConnection conn)
    {
        conn.Execute($@"
            UPDATE AssetItemDocuments
            SET DocumentType = CASE
                WHEN LOWER(FilePath) LIKE '%.jpg' OR LOWER(FilePath) LIKE '%.jpeg' OR LOWER(FilePath) LIKE '%.png' THEN '{DocumentTypeCode.Picture}'
                ELSE '{DocumentTypeCode.Document}'
            END
            WHERE DocumentType IS NULL");
    }

    /// <summary>
    /// Drops the unused legacy Description/Type/Location/Priority/QrCode columns from AssetItems.
    /// Safe to run on every startup.
    /// </summary>
    private static void DropLegacyAssetItemColumns(IDbConnection conn)
    {
        foreach (var column in new[] { "Description", "Type", "Location", "Priority", "QrCode" })
        {
            var exists = conn.ExecuteScalar<int?>($"SELECT COL_LENGTH('AssetItems', '{column}')") != null;
            if (exists)
                conn.Execute($"ALTER TABLE AssetItems DROP COLUMN {column}");
        }
    }

    /// <summary>
    /// Backfills Condition from the old IntegrityStatus enum column (1=Good, 2=Fair, 3=Poor)
    /// for rows that never had Condition set, then drops the now-redundant column. Safe to
    /// run on every startup.
    /// </summary>
    private static void MigrateLegacyIntegrityStatus(IDbConnection conn)
    {
        var hasIntegrityStatus = conn.ExecuteScalar<int?>("SELECT COL_LENGTH('AssetItems', 'IntegrityStatus')") != null;
        if (!hasIntegrityStatus) return;

        conn.Execute(@"
            UPDATE AssetItems
            SET Condition = CASE IntegrityStatus WHEN 1 THEN 'Good' WHEN 2 THEN 'Fair' WHEN 3 THEN 'Poor' END
            WHERE Condition IS NULL AND IntegrityStatus IN (1, 2, 3)");

        conn.Execute("ALTER TABLE AssetItems DROP COLUMN IntegrityStatus");
    }

    /// <summary>
    /// Recomputes AssetId (Asset Tag No.) in C# for rows that have full Plant/Equipment/Civil
    /// data, so it always matches AssetItemService's generation formula. Rows lacking that
    /// structured data (legacy freeform tags) are left untouched. Safe to run on every startup.
    /// </summary>
    private static void RegenerateAssetIds(IDbConnection conn)
    {
        var plantCodes = conn.Query<(int Id, int? Code)>("SELECT Id, Code FROM Plants")
            .ToDictionary(p => p.Id, p => p.Code);

        var items = conn.Query<(int Id, int? PlantId, string? EquipmentCode, string? CivilAssetCode, int? CivilAssetOrder, string? AssetId, string? Category)>(
            "SELECT Id, PlantId, EquipmentCode, CivilAssetCode, CivilAssetOrder, AssetId, Category FROM AssetItems " +
            "WHERE PlantId IS NOT NULL AND EquipmentCode IS NOT NULL AND CivilAssetCode IS NOT NULL");

        foreach (var item in items)
        {
            var plantCode = item.PlantId.HasValue && plantCodes.TryGetValue(item.PlantId.Value, out var code) ? code : null;
            var generated = AssetItem.GenerateAssetId(
                plantCode, item.EquipmentCode ?? string.Empty,
                item.CivilAssetCode ?? string.Empty, item.CivilAssetOrder, item.Category);

            if (generated != item.AssetId)
            {
                conn.Execute("UPDATE AssetItems SET AssetId = @AssetId WHERE Id = @Id", new { AssetId = generated, Id = item.Id });
            }
        }
    }

    /// <summary>
    /// Converts Plants.Code from its original nvarchar(20) to int, nulling out any non-numeric
    /// legacy values first so the ALTER can't fail on bad data. No-op once already int. Safe to
    /// run on every startup.
    /// </summary>
    private static void MigratePlantCodeToInt(IDbConnection conn)
    {
        var currentType = conn.ExecuteScalar<string?>(
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Plants' AND COLUMN_NAME = 'Code'");
        if (currentType == null || currentType == "int") return;

        conn.Execute("UPDATE Plants SET Code = NULL WHERE Code IS NOT NULL AND (LEN(Code) = 0 OR Code LIKE '%[^0-9]%')");
        conn.Execute("ALTER TABLE Plants ALTER COLUMN Code int NULL");
    }

    /// <summary>
    /// Backfills AssetItems.PlantId from any leftover legacy PlantCode text column (matched
    /// against existing Plants rows by Code) and drops it. Safe to run on every startup.
    /// </summary>
    private static void BackfillPlantIdFromLegacyColumn(IDbConnection conn)
    {
        var hasPlantCode = conn.ExecuteScalar<int?>("SELECT COL_LENGTH('AssetItems', 'PlantCode')") != null;
        if (!hasPlantCode) return;

        conn.Execute(@"
            UPDATE AssetItems
            SET PlantId = (SELECT p.Id FROM Plants p WHERE p.Code = TRY_CAST(AssetItems.PlantCode AS int))
            WHERE PlantId IS NULL AND PlantCode IS NOT NULL
              AND EXISTS (SELECT 1 FROM Plants p WHERE p.Code = TRY_CAST(AssetItems.PlantCode AS int))");

        conn.Execute("ALTER TABLE AssetItems DROP COLUMN PlantCode");
        conn.Execute("ALTER TABLE AssetItems DROP COLUMN PlantDescription");
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

IF OBJECT_ID('Plants', 'U') IS NULL
CREATE TABLE Plants (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code int NULL,
    Name nvarchar(200) NULL,
    Description nvarchar(200) NULL
);

IF OBJECT_ID('Plants', 'U') IS NOT NULL AND COL_LENGTH('Plants', 'Code') IS NULL
ALTER TABLE Plants ADD Code int NULL;

IF OBJECT_ID('AssetItems', 'U') IS NULL
CREATE TABLE AssetItems (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    GisRefNo nvarchar(200) NULL,
    AssetId nvarchar(max) NULL,
    Title nvarchar(200) NULL,
    EquipmentCode nvarchar(200) NULL,
    EquipmentDescription nvarchar(200) NULL,
    EquipmentDesc nvarchar(200) NULL,
    CivilAssetCode nvarchar(200) NULL,
    CivilAssetDescription nvarchar(200) NULL,
    CivilAssetDesc nvarchar(200) NULL,
    CivilAssetOrder int NULL,
    [Function] nvarchar(200) NULL,
    Material nvarchar(200) NULL,
    YearInstalled int NULL,
    [Owner] nvarchar(200) NULL,
    Constrain nvarchar(200) NULL,
    [Access] nvarchar(200) NULL,
    CoordinateN nvarchar(200) NULL,
    CoordinateE nvarchar(200) NULL,
    Zone nvarchar(200) NULL,
    Area nvarchar(200) NULL,
    Train nvarchar(200) NULL,
    DateOfInspection datetime2 NULL,
    Inspector nvarchar(200) NULL,
    Condition nvarchar(200) NULL,
    Comment nvarchar(1000) NULL,
    PicturePath nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy nvarchar(200) NULL,
    PlantId int NULL,
    CONSTRAINT FK_AssetItems_Plants_PlantId FOREIGN KEY (PlantId) REFERENCES Plants(Id)
);

-- Add new columns for existing tables (idempotent)
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'GisRefNo') IS NULL
ALTER TABLE AssetItems ADD GisRefNo nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'EquipmentCode') IS NULL
ALTER TABLE AssetItems ADD EquipmentCode nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'EquipmentDescription') IS NULL
ALTER TABLE AssetItems ADD EquipmentDescription nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'EquipmentDesc') IS NULL
ALTER TABLE AssetItems ADD EquipmentDesc nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'CivilAssetCode') IS NULL
ALTER TABLE AssetItems ADD CivilAssetCode nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'CivilAssetDescription') IS NULL
ALTER TABLE AssetItems ADD CivilAssetDescription nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'CivilAssetDesc') IS NULL
ALTER TABLE AssetItems ADD CivilAssetDesc nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'CivilAssetOrder') IS NULL
ALTER TABLE AssetItems ADD CivilAssetOrder int NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Function') IS NULL
ALTER TABLE AssetItems ADD [Function] nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Material') IS NULL
ALTER TABLE AssetItems ADD Material nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'YearInstalled') IS NULL
ALTER TABLE AssetItems ADD YearInstalled int NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Owner') IS NULL
ALTER TABLE AssetItems ADD [Owner] nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Constrain') IS NULL
ALTER TABLE AssetItems ADD Constrain nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Access') IS NULL
ALTER TABLE AssetItems ADD [Access] nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'CoordinateN') IS NULL
ALTER TABLE AssetItems ADD CoordinateN nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'CoordinateE') IS NULL
ALTER TABLE AssetItems ADD CoordinateE nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Zone') IS NULL
ALTER TABLE AssetItems ADD Zone nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Area') IS NULL
ALTER TABLE AssetItems ADD Area nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Train') IS NULL
ALTER TABLE AssetItems ADD Train nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Category') IS NULL
ALTER TABLE AssetItems ADD Category nvarchar(250) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'DateOfInspection') IS NULL
ALTER TABLE AssetItems ADD DateOfInspection datetime2 NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Inspector') IS NULL
ALTER TABLE AssetItems ADD Inspector nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Condition') IS NULL
ALTER TABLE AssetItems ADD Condition nvarchar(200) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'Comment') IS NULL
ALTER TABLE AssetItems ADD Comment nvarchar(1000) NULL;
IF OBJECT_ID('AssetItems', 'U') IS NOT NULL AND COL_LENGTH('AssetItems', 'PlantId') IS NULL
BEGIN
    ALTER TABLE AssetItems ADD PlantId int NULL;
    EXEC('ALTER TABLE AssetItems ADD CONSTRAINT FK_AssetItems_Plants_PlantId FOREIGN KEY (PlantId) REFERENCES Plants(Id)');
END;

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
    DocumentType nvarchar(20) NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy nvarchar(200) NULL,
    AssetItemId int NULL,
    CONSTRAINT FK_AssetItemDocuments_AssetItems_AssetItemId FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
);
IF OBJECT_ID('AssetItemDocuments', 'U') IS NOT NULL AND COL_LENGTH('AssetItemDocuments', 'DocumentType') IS NULL
ALTER TABLE AssetItemDocuments ADD DocumentType nvarchar(20) NULL;

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
