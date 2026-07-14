using System.Data;
using AIMS.Infrastructure.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AIMS.WebFrontend.Tests;

/// <summary>
/// Backs IDapperContext with a shared in-memory SQLite database so the services' and
/// page models' real Dapper SQL runs unchanged. The keeper connection holds the database
/// alive across the short-lived connections the code under test opens and disposes.
/// </summary>
public sealed class SqliteDapperContext : IDapperContext, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _keeper;

    public SqliteDapperContext()
    {
        _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(_connectionString);
        _keeper.Open();

        _keeper.Execute("""
            CREATE TABLE Plants (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code INTEGER NULL,
                Name TEXT NULL,
                Description TEXT NULL
            );
            CREATE TABLE AssetItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GisRefNo TEXT NULL,
                AssetId TEXT NOT NULL DEFAULT '',
                Title TEXT NULL,
                EquipmentCode TEXT NULL,
                EquipmentDescription TEXT NULL,
                EquipmentDesc TEXT NULL,
                EquipmentOrder INTEGER NULL,
                CivilAssetCode TEXT NULL,
                CivilAssetDescription TEXT NULL,
                CivilAssetDesc TEXT NULL,
                CivilAssetOrder INTEGER NULL,
                "Function" TEXT NULL,
                Material TEXT NULL,
                YearInstalled INTEGER NULL,
                "Owner" TEXT NULL,
                Constrain TEXT NULL,
                "Access" TEXT NULL,
                CoordinateN TEXT NULL,
                CoordinateE TEXT NULL,
                Zone TEXT NULL,
                Area TEXT NULL,
                Train TEXT NULL,
                DateOfInspection TEXT NULL,
                Inspector TEXT NULL,
                "Condition" TEXT NULL,
                "Comment" TEXT NULL,
                PicturePath TEXT NULL,
                CreatedAt TEXT NULL,
                CreatedBy TEXT NULL,
                PlantId INTEGER NULL
            );
            CREATE TABLE AssetItemDocuments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DocumentTitle TEXT NULL,
                FilePath TEXT NULL,
                DocumentType TEXT NULL,
                CreatedAt TEXT NULL,
                CreatedBy TEXT NULL,
                AssetItemId INTEGER NULL
            );
            CREATE TABLE AssetRemarks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Description TEXT NULL,
                CreatedAt TEXT NULL,
                CreatedBy TEXT NULL,
                AssetItemId INTEGER NULL
            );
            """);
    }

    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public void AddPlant(int id, int code, string name) =>
        _keeper.Execute(
            "INSERT INTO Plants (Id, Code, Name) VALUES (@Id, @Code, @Name)",
            new { Id = id, Code = code, Name = name });

    public void AddAsset(string assetId, int? plantId, string? condition, string? title = null) =>
        _keeper.Execute(
            "INSERT INTO AssetItems (AssetId, PlantId, Condition, Title) VALUES (@AssetId, @PlantId, @Condition, @Title)",
            new { AssetId = assetId, PlantId = plantId, Condition = condition, Title = title });

    public void AddDocument(int assetItemId, string title, string filePath) =>
        _keeper.Execute(
            "INSERT INTO AssetItemDocuments (DocumentTitle, FilePath, AssetItemId) VALUES (@Title, @FilePath, @AssetItemId)",
            new { Title = title, FilePath = filePath, AssetItemId = assetItemId });

    public void AddRemark(int assetItemId, string description) =>
        _keeper.Execute(
            "INSERT INTO AssetRemarks (Description, AssetItemId) VALUES (@Description, @AssetItemId)",
            new { Description = description, AssetItemId = assetItemId });

    public int Count(string table) =>
        _keeper.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");

    public void Dispose() => _keeper.Dispose();
}

/// <summary>ISqlDialect for the in-memory SQLite database the tests run against.</summary>
public sealed class SqliteTestDialect : ISqlDialect
{
    public string Quote(string identifier) => $"\"{identifier}\"";

    public string SelectFromDual => string.Empty;

    public int InsertAndGetId(IDbConnection conn, string quotedTable, string cols, string atParams, object param) =>
        conn.QuerySingle<int>(
            $"INSERT INTO {quotedTable} ({cols}) VALUES ({atParams}); SELECT last_insert_rowid();", param);

    public Task<int> ExecuteUpdateAsync(IDbConnection conn, string sql, Dictionary<string, object?> parameters)
    {
        var p = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            p.Add(name, value);
        }
        return conn.ExecuteAsync(sql, p);
    }

    public string Paginate(string selectSql, string orderBy) =>
        $"{selectSql} ORDER BY {orderBy} LIMIT @PageSize OFFSET @Offset";

    // SQLite LIKE wildcards are % and _, same as Oracle.
    public string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
