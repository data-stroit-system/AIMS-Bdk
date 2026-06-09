using Dapper;

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
            CREATE TABLE AssetItems (
                Id              NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
                Title           NVARCHAR2(150),
                AssetId         VARCHAR2(4000),
                Description     NVARCHAR2(250),
                Type            NUMBER(10,0) DEFAULT 0 NOT NULL,
                Location        NVARCHAR2(250),
                Priority        NUMBER(10,0) DEFAULT 0 NOT NULL,
                IntegrityStatus NUMBER(10,0) DEFAULT 0 NOT NULL,
                PicturePath     NVARCHAR2(500),
                CreatedAt       TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                CreatedBy       NVARCHAR2(200),
                CONSTRAINT PK_AssetItems PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AuditLogs (
                Id             NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
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

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE ToDoItems (
                Id          NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
                Title       VARCHAR2(4000) NOT NULL,
                Description VARCHAR2(4000),
                IsDone      NUMBER(1,0) DEFAULT 0 NOT NULL,
                CONSTRAINT PK_ToDoItems PRIMARY KEY (Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetRoleClaims (
                Id         NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
                RoleId     NVARCHAR2(450) NOT NULL,
                ClaimType  VARCHAR2(4000),
                ClaimValue VARCHAR2(4000),
                CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY (Id),
                CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId
                    FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserClaims (
                Id         NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
                UserId     NVARCHAR2(450) NOT NULL,
                ClaimType  VARCHAR2(4000),
                ClaimValue VARCHAR2(4000),
                CONSTRAINT PK_AspNetUserClaims PRIMARY KEY (Id),
                CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserLogins (
                LoginProvider       NVARCHAR2(450) NOT NULL,
                ProviderKey         NVARCHAR2(450) NOT NULL,
                ProviderDisplayName VARCHAR2(4000),
                UserId              NVARCHAR2(450) NOT NULL,
                CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
                CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AspNetUserRoles (
                UserId NVARCHAR2(450) NOT NULL,
                RoleId NVARCHAR2(450) NOT NULL,
                CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
                CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId
                    FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE,
                CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId
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
                CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId
                    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AssetItemDocuments (
                Id             NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
                DocumentTitle  NVARCHAR2(250),
                FilePath       NVARCHAR2(500),
                CreatedAt      TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                CreatedBy      NVARCHAR2(200),
                AssetItemId    NUMBER(10,0),
                CONSTRAINT PK_AssetItemDocuments PRIMARY KEY (Id),
                CONSTRAINT FK_AssetItemDocuments_AssetItems_AssetItemId
                    FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
            )';
        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;",

        @"BEGIN EXECUTE IMMEDIATE '
            CREATE TABLE AssetRemarks (
                Id          NUMBER(10,0) GENERATED ALWAYS AS IDENTITY NOT NULL,
                Description NVARCHAR2(250),
                CreatedAt   TIMESTAMP DEFAULT SYS_EXTRACT_UTC(SYSTIMESTAMP) NOT NULL,
                CreatedBy   NVARCHAR2(200),
                AssetItemId NUMBER(10,0),
                CONSTRAINT PK_AssetRemarks PRIMARY KEY (Id),
                CONSTRAINT FK_AssetRemarks_AssetItems_AssetItemId
                    FOREIGN KEY (AssetItemId) REFERENCES AssetItems(Id)
            )';
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
