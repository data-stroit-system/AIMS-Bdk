using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Infrastructure.Data;
using Dapper;
using Microsoft.AspNetCore.Identity;

namespace AIMS.Infrastructure.IdentityClass;

public class DapperRoleStore :
    IRoleStore<ApplicationRole>,
    IQueryableRoleStore<ApplicationRole>,
    IRoleClaimStore<ApplicationRole>
{
    private readonly IDapperContext _context;

    public DapperRoleStore(IDapperContext context)
    {
        _context = context;
    }

    public void Dispose() { }

    // IQueryableRoleStore
    public IQueryable<ApplicationRole> Roles
    {
        get
        {
            using var conn = _context.CreateConnection();
            return conn.Query<ApplicationRole>("SELECT * FROM AspNetRoles").AsQueryable();
        }
    }

    // ── IRoleStore ──────────────────────────────────────────────────────────

    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken ct)
    {
        role.ConcurrencyStamp ??= Guid.NewGuid().ToString();
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp, Description)
            VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp, @Description)", role);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE AspNetRoles
            SET Name = @Name, NormalizedName = @NormalizedName,
                ConcurrencyStamp = @ConcurrencyStamp, Description = @Description
            WHERE Id = @Id", role);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM AspNetRoles WHERE Id = @Id", new { role.Id });
        return IdentityResult.Success;
    }

    public async Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApplicationRole>(
            "SELECT * FROM AspNetRoles WHERE Id = @Id", new { Id = roleId });
    }

    public async Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApplicationRole>(
            "SELECT * FROM AspNetRoles WHERE NormalizedName = @NormalizedName",
            new { NormalizedName = normalizedRoleName });
    }

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken ct) =>
        Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken ct) =>
        Task.FromResult(role.Name);

    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken ct)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken ct) =>
        Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken ct)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    // ── IRoleClaimStore ─────────────────────────────────────────────────────

    public async Task<IList<Claim>> GetClaimsAsync(ApplicationRole role, CancellationToken ct = default)
    {
        using var conn = _context.CreateConnection();
        var rows = await conn.QueryAsync(
            "SELECT ClaimType, ClaimValue FROM AspNetRoleClaims WHERE RoleId = @RoleId",
            new { RoleId = role.Id });
        return rows.Select(r => new Claim((string)r.ClaimType, (string)(r.ClaimValue ?? ""))).ToList();
    }

    public async Task AddClaimAsync(ApplicationRole role, Claim claim, CancellationToken ct = default)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue) VALUES (@RoleId, @ClaimType, @ClaimValue)",
            new { RoleId = role.Id, ClaimType = claim.Type, ClaimValue = claim.Value });
    }

    public async Task RemoveClaimAsync(ApplicationRole role, Claim claim, CancellationToken ct = default)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            DELETE FROM AspNetRoleClaims
            WHERE RoleId = @RoleId AND ClaimType = @ClaimType AND ClaimValue = @ClaimValue",
            new { RoleId = role.Id, ClaimType = claim.Type, ClaimValue = claim.Value });
    }
}
