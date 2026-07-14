using AIMS.Infrastructure.Data;
using AIMS.Infrastructure.Services;

namespace AIMS.WebFrontend.Tests.Services;

/// <summary>
/// GetPagedAsync search must treat LIKE wildcards in the user's term literally:
/// searching "100%" may not match "100x".
/// </summary>
public class SearchEscapingTests : IDisposable
{
    private readonly SqliteDapperContext _context = new();
    private readonly AssetItemService _service;

    public SearchEscapingTests()
    {
        _service = new AssetItemService(_context, new SqliteTestDialect());
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Search_PercentIsLiteral()
    {
        _context.AddAsset("A-1", null, null, title: "Load 100%");
        _context.AddAsset("A-2", null, null, title: "Load 100x");

        var (items, total) = await _service.GetPagedAsync("100%", null, 1, 10);

        var item = Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Equal("Load 100%", item.Title);
    }

    [Fact]
    public async Task Search_UnderscoreIsLiteral()
    {
        _context.AddAsset("A-1", null, null, title: "TAG_7");
        _context.AddAsset("A-2", null, null, title: "TAGX7");

        var (items, _) = await _service.GetPagedAsync("TAG_7", null, 1, 10);

        Assert.Equal("TAG_7", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Search_BackslashIsLiteral()
    {
        _context.AddAsset("A-1", null, null, title: @"unit\7");
        _context.AddAsset("A-2", null, null, title: "unit-7");

        var (items, _) = await _service.GetPagedAsync(@"unit\7", null, 1, 10);

        Assert.Equal(@"unit\7", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Search_PlainSubstringStillMatches()
    {
        _context.AddAsset("A-1", null, null, title: "Storage Tank 20D-4");
        _context.AddAsset("A-2", null, null, title: "Pipe Rack");

        var (items, _) = await _service.GetPagedAsync("Tank", null, 1, 10);

        Assert.Equal("Storage Tank 20D-4", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        _context.AddAsset("A-1", null, null, title: "Storage TANK 20D-4");
        _context.AddAsset("A-2", null, null, title: "Pipe Rack");

        var (items, _) = await _service.GetPagedAsync("tank", null, 1, 10);

        Assert.Equal("Storage TANK 20D-4", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Search_CaseInsensitive_AndWildcardsStillLiteral()
    {
        _context.AddAsset("A-1", null, null, title: "LOAD 100%");
        _context.AddAsset("A-2", null, null, title: "LOAD 100X");

        var (items, _) = await _service.GetPagedAsync("load 100%", null, 1, 10);

        Assert.Equal("LOAD 100%", Assert.Single(items).Title);
    }
}

public class EscapeLikeDialectTests
{
    [Fact]
    public void SqlServer_EscapesBracketToo()
    {
        var d = new SqlServerDialect();
        Assert.Equal(@"\\ \% \_ \[", d.EscapeLike(@"\ % _ ["));
    }

    [Fact]
    public void Oracle_LeavesBracketAlone()
    {
        // Escaping a non-wildcard char raises ORA-01424, so '[' must pass through.
        var d = new OracleDialect();
        Assert.Equal(@"\\ \% \_ [", d.EscapeLike(@"\ % _ ["));
    }
}
