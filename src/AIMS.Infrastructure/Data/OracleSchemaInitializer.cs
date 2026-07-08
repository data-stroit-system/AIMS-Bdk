using AIMS.Core.Entities;
using Dapper;
using System.Data;
using System.Linq;

namespace AIMS.Infrastructure.Data;

internal sealed class OracleSchemaInitializer : ISchemaInitializer
{
    private readonly IDapperContext _context;

    public OracleSchemaInitializer(IDapperContext context)
    {
        _context = context;
    }

    public void Initialize()
    {
        using var conn = _context.CreateConnection();
        conn.Open();
        foreach (var stmt in SchemaStatements)
            conn.Execute(stmt);

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
        foreach (var column in new[] { "DESCRIPTION", "TYPE", "LOCATION", "PRIORITY", "QRCODE" })
        {
            var exists = conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'ASSETITEMS' AND COLUMN_NAME = '{column}'") > 0;
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
        var hasIntegrityStatus = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'ASSETITEMS' AND COLUMN_NAME = 'INTEGRITYSTATUS'") > 0;
        if (!hasIntegrityStatus) return;

        conn.Execute(@"
            UPDATE AssetItems
            SET ""Condition"" = CASE IntegrityStatus WHEN 1 THEN 'Good' WHEN 2 THEN 'Fair' WHEN 3 THEN 'Poor' END
            WHERE ""Condition"" IS NULL AND IntegrityStatus IN (1, 2, 3)");

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

        var items = conn.Query<(int Id, int? PlantId, string? EquipmentCode, int? EquipmentOrder, string? CivilAssetCode, int? CivilAssetOrder, string? AssetId)>(
            "SELECT Id, PlantId, EquipmentCode, EquipmentOrder, CivilAssetCode, CivilAssetOrder, AssetId FROM AssetItems " +
            "WHERE PlantId IS NOT NULL AND EquipmentCode IS NOT NULL AND CivilAssetCode IS NOT NULL");

        foreach (var item in items)
        {
            var plantCode = item.PlantId.HasValue && plantCodes.TryGetValue(item.PlantId.Value, out var code) ? code : null;
            var generated = AssetItem.GenerateAssetId(
                plantCode, item.EquipmentCode ?? string.Empty, item.EquipmentOrder,
                item.CivilAssetCode ?? string.Empty, item.CivilAssetOrder);

            if (generated != item.AssetId)
            {
                conn.Execute("UPDATE AssetItems SET AssetId = @AssetId WHERE Id = @Id", new { AssetId = generated, Id = item.Id });
            }
        }
    }

    /// <summary>
    /// Converts Plants.Code from its original NVARCHAR2(20) to NUMBER. Oracle can't MODIFY a
    /// column's datatype while it holds data, so this adds a new numeric column, backfills it
    /// from any cleanly-numeric legacy values, then drops the old column and renames the new
    /// one into place. No-op once already NUMBER. Safe to run on every startup.
    /// </summary>
    private static void MigratePlantCodeToInt(IDbConnection conn)
    {
        var dataType = conn.ExecuteScalar<string?>(
            "SELECT DATA_TYPE FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'PLANTS' AND COLUMN_NAME = 'CODE'");
        if (dataType == null || dataType == "NUMBER") return;

        conn.Execute("ALTER TABLE Plants ADD (Code_New NUMBER(10,0))");
        conn.Execute("UPDATE Plants SET Code_New = TO_NUMBER(Code) WHERE Code IS NOT NULL AND REGEXP_LIKE(Code, '^[0-9]+$')");
        conn.Execute("ALTER TABLE Plants DROP COLUMN Code");
        conn.Execute("ALTER TABLE Plants RENAME COLUMN Code_New TO Code");
    }

    /// <summary>
    /// Backfills AssetItems.PlantId from any leftover legacy PlantCode text column (matched
    /// against existing Plants rows by Code) and drops it. Safe to run on every startup.
    /// </summary>
    private static void BackfillPlantIdFromLegacyColumn(IDbConnection conn)
    {
        var hasPlantCode = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'ASSETITEMS' AND COLUMN_NAME = 'PLANTCODE'") > 0;
        if (!hasPlantCode) return;

        conn.Execute(@"
            UPDATE AssetItems
            SET PlantId = (SELECT p.Id FROM Plants p WHERE TO_CHAR(p.Code) = AssetItems.PlantCode)
            WHERE PlantId IS NULL AND PlantCode IS NOT NULL
              AND EXISTS (SELECT 1 FROM Plants p WHERE TO_CHAR(p.Code) = AssetItems.PlantCode)");

        conn.Execute("ALTER TABLE AssetItems DROP COLUMN PlantCode");
        conn.Execute("ALTER TABLE AssetItems DROP COLUMN PlantDescription");
    }

    // ORA-00955 = name already used by an existing object (table/index exists)
    private static readonly string[] SchemaStatements =
    [
        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetRoles (
                Id          NVARCHAR2(450)  NOT NULL,
                Description NVARCHAR2(250),
                Name        NVARCHAR2(256),
                NormalizedName NVARCHAR2(256),
                ConcurrencyStamp VARCHAR2(4000),
                CONSTRAINT PK_AspNetRoles PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUsers (
                Id                   NVARCHAR2(450) NOT NULL,
                FullName             NVARCHAR2(250),
                JobTitle             NVARCHAR2(250),
                UserName             NVARCHAR2(256),
                NormalizedUserName   NVARCHAR2(256),
                Email                NVARCHAR2(256),
                NormalizedEmail      NVARCHAR2(256),
                EmailConfirmed       NUMBER(1,0) DEFAULT 0 NOT NULL,
                PasswordHash         VARCHAR2(4000),
                SecurityStamp        VARCHAR2(4000),
                ConcurrencyStamp     VARCHAR2(4000),
                PhoneNumber          VARCHAR2(4000),
                PhoneNumberConfirmed NUMBER(1,0) DEFAULT 0 NOT NULL,
                TwoFactorEnabled     NUMBER(1,0) DEFAULT 0 NOT NULL,
                LockoutEnd           TIMESTAMP WITH TIME ZONE,
                LockoutEnabled       NUMBER(1,0) DEFAULT 0 NOT NULL,
                AccessFailedCount    NUMBER(10,0) DEFAULT 0 NOT NULL,
                CONSTRAINT PK_AspNetUsers PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE Plants (
                Id          NUMBER(10,0) NOT NULL,
                Code        NUMBER(10,0),
                Name        NVARCHAR2(200),
                Description NVARCHAR2(200),
                CONSTRAINT PK_Plants PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE Plants ADD (Code NUMBER(10,0))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE Plants_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_Plants_BI
            BEFORE INSERT ON Plants
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT Plants_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AssetItems (
                Id                  NUMBER(10,0) NOT NULL,
                GisRefNo            NVARCHAR2(200),
                AssetId             VARCHAR2(4000),
                Title               NVARCHAR2(200),
                EquipmentCode       NVARCHAR2(200),
                EquipmentDescription NVARCHAR2(200),
                EquipmentDesc       NVARCHAR2(200),
                EquipmentOrder      NUMBER(10,0),
                CivilAssetCode      NVARCHAR2(200),
                CivilAssetDescription NVARCHAR2(200),
                CivilAssetDesc      NVARCHAR2(200),
                CivilAssetOrder     NUMBER(10,0),
                ""Function""        NVARCHAR2(200),
                Material            NVARCHAR2(200),
                YearInstalled       NUMBER(10,0),
                ""Owner""           NVARCHAR2(200),
                Constrain           NVARCHAR2(200),
                ""Access""          NVARCHAR2(200),
                CoordinateN         NVARCHAR2(200),
                CoordinateE         NVARCHAR2(200),
                Zone                NVARCHAR2(200),
                Area                NVARCHAR2(200),
                Train               NVARCHAR2(200),
                DateOfInspection    TIMESTAMP,
                Inspector           NVARCHAR2(200),
                ""Condition""       NVARCHAR2(200),
                ""Comment""         NVARCHAR2(1000),
                PicturePath         NVARCHAR2(500),
                CreatedAt           TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                CreatedBy           NVARCHAR2(200),
                PlantId             NUMBER(10,0),
                CONSTRAINT PK_AssetItems PRIMARY KEY (Id),
                CONSTRAINT FK_AssetItems_Plants
                    FOREIGN KEY (PlantId) REFERENCES Plants(Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        // Add new columns to existing AssetItems table (for upgrades)
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (GisRefNo NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (EquipmentCode NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (EquipmentDescription NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (EquipmentDesc NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (EquipmentOrder NUMBER(10,0))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (CivilAssetCode NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (CivilAssetDescription NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (CivilAssetDesc NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (CivilAssetOrder NUMBER(10,0))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (""Function"" NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (Material NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (YearInstalled NUMBER(10,0))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (""Owner"" NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (Constrain NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (""Access"" NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (CoordinateN NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (CoordinateE NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (Zone NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (Area NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (Train NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (DateOfInspection TIMESTAMP)'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (Inspector NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (""Condition"" NVARCHAR2(200))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (""Comment"" NVARCHAR2(1000))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD (PlantId NUMBER(10,0))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",
        // ORA-02275 = such a referential constraint already exists in the table
        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItems ADD CONSTRAINT FK_AssetItems_Plants FOREIGN KEY (PlantId) REFERENCES Plants(Id)'; EXCEPTION WHEN OTHERS THEN IF SQLCODE NOT IN (-2275, -2264) THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE AssetItems_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_AssetItems_BI
            BEFORE INSERT ON AssetItems
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT AssetItems_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AuditLogs (
                Id             NUMBER(10,0) NOT NULL,
                Category       NVARCHAR2(50),
                EntityName     NVARCHAR2(100),
                EntityId       NVARCHAR2(50),
                Action         NVARCHAR2(50),
                OldValues      CLOB,
                NewValues      CLOB,
                ChangedColumns CLOB,
                Description    NVARCHAR2(500),
                UserId         NVARCHAR2(256),
                UserName       NVARCHAR2(256),
                Timestamp      TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                IpAddress      NVARCHAR2(50),
                UserAgent      NVARCHAR2(500),
                RequestPath    NVARCHAR2(500),
                Result         NVARCHAR2(50),
                CONSTRAINT PK_AuditLogs PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE AuditLogs_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_AuditLogs_BI
            BEFORE INSERT ON AuditLogs
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT AuditLogs_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE ToDoItems (
                Id          NUMBER(10,0) NOT NULL,
                Title       VARCHAR2(4000) NOT NULL,
                Description VARCHAR2(4000),
                IsDone      NUMBER(1,0) DEFAULT 0 NOT NULL,
                CONSTRAINT PK_ToDoItems PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE ToDoItems_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_ToDoItems_BI
            BEFORE INSERT ON ToDoItems
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT ToDoItems_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetRoleClaims (
                Id         NUMBER(10,0) NOT NULL,
                RoleId     NVARCHAR2(450) NOT NULL,
                ClaimType  VARCHAR2(4000),
                ClaimValue VARCHAR2(4000),
                CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY (Id),
                CONSTRAINT FK_RoleClaims_Roles
                    FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE AspNetRoleClaims_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_AspNetRoleClaims_BI
            BEFORE INSERT ON AspNetRoleClaims
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT AspNetRoleClaims_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserClaims (
                Id         NUMBER(10,0) NOT NULL,
                UserId     NVARCHAR2(450) NOT NULL,
                ClaimType  VARCHAR2(4000),
                ClaimValue VARCHAR2(4000),
                CONSTRAINT PK_AspNetUserClaims PRIMARY KEY (Id),
                CONSTRAINT FK_UserClaims_Users
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE AspNetUserClaims_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_AspNetUserClaims_BI
            BEFORE INSERT ON AspNetUserClaims
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT AspNetUserClaims_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserLogins (
                LoginProvider       NVARCHAR2(450) NOT NULL,
                ProviderKey         NVARCHAR2(450) NOT NULL,
                ProviderDisplayName VARCHAR2(4000),
                UserId              NVARCHAR2(450) NOT NULL,
                CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
                CONSTRAINT FK_UserLogins_Users
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserRoles (
                UserId NVARCHAR2(450) NOT NULL,
                RoleId NVARCHAR2(450) NOT NULL,
                CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
                CONSTRAINT FK_UserRoles_Roles
                    FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE,
                CONSTRAINT FK_UserRoles_Users
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserTokens (
                UserId        NVARCHAR2(450) NOT NULL,
                LoginProvider NVARCHAR2(450) NOT NULL,
                Name          NVARCHAR2(450) NOT NULL,
                Value         VARCHAR2(4000),
                CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
                CONSTRAINT FK_UserTokens_Users
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AssetItemDocuments (
                Id             NUMBER(10,0) NOT NULL,
                DocumentTitle  NVARCHAR2(250),
                FilePath       NVARCHAR2(500),
                DocumentType   NVARCHAR2(20),
                CreatedAt      TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                CreatedBy      NVARCHAR2(200),
                AssetItemId    NUMBER(10,0),
                CONSTRAINT PK_AssetItemDocuments PRIMARY KEY (Id),
                CONSTRAINT FK_ItemDocs_Items
                    FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'ALTER TABLE AssetItemDocuments ADD (DocumentType NVARCHAR2(20))'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1430 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE AssetItemDocuments_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_AssetItemDocuments_BI
            BEFORE INSERT ON AssetItemDocuments
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT AssetItemDocuments_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AssetRemarks (
                Id          NUMBER(10,0) NOT NULL,
                Description NVARCHAR2(250),
                CreatedAt   TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                CreatedBy   NVARCHAR2(200),
                AssetItemId NUMBER(10,0),
                CONSTRAINT PK_AssetRemarks PRIMARY KEY (Id),
                CONSTRAINT FK_Remarks_Items
                    FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE AssetRemarks_SEQ START WITH 1 INCREMENT BY 1 NOCACHE NOCYCLE';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE OR REPLACE TRIGGER TRG_AssetRemarks_BI
            BEFORE INSERT ON AssetRemarks
            FOR EACH ROW
            WHEN (new.Id IS NULL)
            BEGIN
                SELECT AssetRemarks_SEQ.NEXTVAL INTO :new.Id FROM dual;
            END;';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",
        // Oracle unique indexes: NULL values are excluded from unique indexes automatically,
        // so the WHERE ... IS NOT NULL filter from SQL Server is not needed.
        @"BEGIN EXECUTE IMMEDIATE 'CREATE UNIQUE INDEX RoleNameIndex ON AspNetRoles(NormalizedName)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE UNIQUE INDEX UserNameIndex ON AspNetUsers(NormalizedUserName)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE INDEX EmailIndex ON AspNetUsers(NormalizedEmail)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE INDEX IX_AuditLogs_EntityName ON AuditLogs(EntityName)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE INDEX IX_AuditLogs_EntityId ON AuditLogs(EntityId)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE INDEX IX_AuditLogs_Timestamp ON AuditLogs(Timestamp)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE 'CREATE INDEX IX_AuditLogs_UserId ON AuditLogs(UserId)';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",
    ];
}
