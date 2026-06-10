using System;
using System.Threading.Tasks;
using AIMS.Core.Entities;
using AIMS.Infrastructure.Data;
using AIMS.SharedKernel.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Http;

namespace AIMS.Infrastructure.Audit;

public class ActivityLogger : IActivityLogger
{
    private readonly IDapperContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditUserProvider _auditUserProvider;

    public ActivityLogger(
        IDapperContext context,
        IHttpContextAccessor httpContextAccessor,
        IAuditUserProvider auditUserProvider)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _auditUserProvider = auditUserProvider;
    }

    public async Task LogActivityAsync(
        string activityType,
        string? description = null,
        string? entityName = null,
        string? entityId = null,
        string? result = null,
        string? userId = null,
        string? userName = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AuditLogs
                (Category, EntityName, EntityId, Action, Description, UserId, UserName,
                 IpAddress, UserAgent, RequestPath, Result, Timestamp)
            VALUES
                (@Category, @EntityName, @EntityId, @Action, @Description, @UserId, @UserName,
                 @IpAddress, @UserAgent, @RequestPath, @Result, @Timestamp)",
            new
            {
                Category = AuditCategory.Activity,
                EntityName = entityName ?? activityType,
                EntityId = entityId ?? string.Empty,
                Action = activityType,
                Description = description,
                UserId = userId ?? _auditUserProvider.GetUserId(),
                UserName = userName ?? _auditUserProvider.GetUserName(),
                IpAddress = _auditUserProvider.GetIpAddress(),
                UserAgent = GetUserAgent(httpContext),
                RequestPath = GetRequestPath(httpContext),
                Result = result ?? "Success",
                Timestamp = DateTime.UtcNow
            });
    }

    public async Task LogSecurityActivityAsync(
        string activityType,
        string? description = null,
        string? result = null,
        string? userId = null,
        string? userName = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AuditLogs
                (Category, EntityName, EntityId, Action, Description, UserId, UserName,
                 IpAddress, UserAgent, RequestPath, Result, Timestamp)
            VALUES
                (@Category, @EntityName, @EntityId, @Action, @Description, @UserId, @UserName,
                 @IpAddress, @UserAgent, @RequestPath, @Result, @Timestamp)",
            new
            {
                Category = AuditCategory.Security,
                EntityName = "Security",
                EntityId = string.Empty,
                Action = activityType,
                Description = description,
                UserId = userId ?? _auditUserProvider.GetUserId(),
                UserName = userName ?? _auditUserProvider.GetUserName(),
                IpAddress = _auditUserProvider.GetIpAddress(),
                UserAgent = GetUserAgent(httpContext),
                RequestPath = GetRequestPath(httpContext),
                Result = result ?? "Success",
                Timestamp = DateTime.UtcNow
            });
    }

    private static string? GetUserAgent(HttpContext? httpContext)
    {
        if (httpContext == null) return null;
        var ua = httpContext.Request.Headers["User-Agent"].ToString();
        return ua.Length > 500 ? ua[..500] : ua;
    }

    private static string? GetRequestPath(HttpContext? httpContext)
    {
        if (httpContext == null) return null;
        var full = httpContext.Request.Path + httpContext.Request.QueryString;
        return full.Length > 500 ? full[..500] : full;
    }
}
