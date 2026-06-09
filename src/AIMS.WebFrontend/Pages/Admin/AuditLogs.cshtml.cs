using AIMS.Core.Entities;
using AIMS.Infrastructure.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AIMS.WebFrontend.Pages.Admin;

[Authorize(Roles = "Admin,Manager,User")]
public class AuditLogsModel : PageModel
{
    private readonly DapperContext _context;
    private const int PageSize = 25;

    public AuditLogsModel(DapperContext context)
    {
        _context = context;
    }

    public List<AuditLog> AuditLogs { get; set; } = new();
    public List<string> AvailableEntities { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public bool CanViewAllLogs { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EntityNameFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ActionFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserNameFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    public async Task OnGetAsync([FromQuery] int page = 1)
    {
        CurrentPage = page < 1 ? 1 : page;
        CanViewAllLogs = User.IsInRole("Admin") || User.IsInRole("Manager");
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentUserName = User.Identity?.Name;

        using var conn = _context.CreateConnection();

        // Build base restriction clause
        var userRestriction = CanViewAllLogs
            ? string.Empty
            : " AND (UserId = @CurrentUserId OR UserName = @CurrentUserName)";

        // Distinct entity names for the filter dropdown
        AvailableEntities = (await conn.QueryAsync<string>(
            $"SELECT DISTINCT EntityName FROM AuditLogs WHERE EntityName IS NOT NULL {userRestriction} ORDER BY EntityName",
            new { CurrentUserId = currentUserId, CurrentUserName = currentUserName })).ToList();

        // Build filter clause
        var where = new StringBuilder($"WHERE 1=1 {userRestriction}");
        var p = new DynamicParameters();
        p.Add("CurrentUserId", currentUserId);
        p.Add("CurrentUserName", currentUserName);

        if (!string.IsNullOrEmpty(EntityNameFilter))
        {
            where.Append(" AND EntityName = @EntityName");
            p.Add("EntityName", EntityNameFilter);
        }

        if (!string.IsNullOrEmpty(ActionFilter))
        {
            where.Append(" AND Action = @Action");
            p.Add("Action", ActionFilter);
        }

        if (!string.IsNullOrEmpty(UserNameFilter) && CanViewAllLogs)
        {
            where.Append(" AND UserName LIKE @UserNameFilter");
            p.Add("UserNameFilter", $"%{UserNameFilter}%");
        }

        if (FromDate.HasValue)
        {
            where.Append(" AND Timestamp >= @FromDate");
            p.Add("FromDate", FromDate.Value.ToUniversalTime());
        }

        if (ToDate.HasValue)
        {
            where.Append(" AND Timestamp < @ToDate");
            p.Add("ToDate", ToDate.Value.AddDays(1).ToUniversalTime());
        }

        var whereClause = where.ToString();
        p.Add("Offset", (CurrentPage - 1) * PageSize);
        p.Add("PageSize", PageSize);

        var totalCount = await conn.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM AuditLogs {whereClause}", p);

        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        TotalPages = TotalPages < 1 ? 1 : TotalPages;
        CurrentPage = CurrentPage > TotalPages ? TotalPages : CurrentPage;

        AuditLogs = (await conn.QueryAsync<AuditLog>(
            $"SELECT * FROM AuditLogs {whereClause} ORDER BY Timestamp DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            p)).ToList();
    }

    public string FormatJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
