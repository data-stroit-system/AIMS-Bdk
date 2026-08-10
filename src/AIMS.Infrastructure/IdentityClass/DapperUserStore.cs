#nullable disable
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

public class DapperUserStore :
    IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserRoleStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserClaimStore<ApplicationUser>,
    IUserLoginStore<ApplicationUser>,
    IUserTwoFactorStore<ApplicationUser>,
    IUserPhoneNumberStore<ApplicationUser>,
    IUserAuthenticatorKeyStore<ApplicationUser>,
    IQueryableUserStore<ApplicationUser>
{
    private const string AuthenticatorStoreLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";

    private readonly IDapperContext _context;
    private readonly ISqlDialect _dialect;

    public DapperUserStore(IDapperContext context, ISqlDialect dialect)
    {
        _context = context;
        _dialect = dialect;
    }

    public void Dispose() { }

    // IQueryableUserStore
    public IQueryable<ApplicationUser> Users
    {
        get
        {
            using var conn = _context.CreateConnection();
            // Safe only because Query defaults to buffered: true — the full result set
            // is materialized before the connection is disposed, and the IQueryable is
            // LINQ-to-Objects over that list. Never pass buffered: false here: the lazy
            // iterator would outlive the connection and throw on first enumeration.
            return conn.Query<ApplicationUser>("SELECT * FROM AspNetUsers").AsQueryable();
        }
    }

    // ── IUserStore ──────────────────────────────────────────────────────────

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
    {
        user.ConcurrencyStamp ??= Guid.NewGuid().ToString();
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AspNetUsers
                (Id, UserName, NormalizedUserName, Email, NormalizedEmail,
                 EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
                 PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
                 LockoutEnd, LockoutEnabled, AccessFailedCount, FullName, JobTitle)
            VALUES
                (@Id, @UserName, @NormalizedUserName, @Email, @NormalizedEmail,
                 @EmailConfirmed, @PasswordHash, @SecurityStamp, @ConcurrencyStamp,
                 @PhoneNumber, @PhoneNumberConfirmed, @TwoFactorEnabled,
                 @LockoutEnd, @LockoutEnabled, @AccessFailedCount, @FullName, @JobTitle)", user);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE AspNetUsers SET
                UserName = @UserName, NormalizedUserName = @NormalizedUserName,
                Email = @Email, NormalizedEmail = @NormalizedEmail,
                EmailConfirmed = @EmailConfirmed, PasswordHash = @PasswordHash,
                SecurityStamp = @SecurityStamp, ConcurrencyStamp = @ConcurrencyStamp,
                PhoneNumber = @PhoneNumber, PhoneNumberConfirmed = @PhoneNumberConfirmed,
                TwoFactorEnabled = @TwoFactorEnabled, LockoutEnd = @LockoutEnd,
                LockoutEnabled = @LockoutEnabled, AccessFailedCount = @AccessFailedCount,
                FullName = @FullName, JobTitle = @JobTitle
            WHERE Id = @Id", user);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM AspNetUsers WHERE Id = @Id", new { user.Id });
        return IdentityResult.Success;
    }

    public async Task<ApplicationUser> FindByIdAsync(string userId, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApplicationUser>(
            "SELECT * FROM AspNetUsers WHERE Id = @Id", new { Id = userId });
    }

    public async Task<ApplicationUser> FindByNameAsync(string normalizedUserName, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApplicationUser>(
            "SELECT * FROM AspNetUsers WHERE NormalizedUserName = @NormalizedUserName",
            new { NormalizedUserName = normalizedUserName });
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.Id);

    public Task<string> GetUserNameAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string userName, CancellationToken ct)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string normalizedName, CancellationToken ct)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    // ── IUserPasswordStore ──────────────────────────────────────────────────

    public Task SetPasswordHashAsync(ApplicationUser user, string passwordHash, CancellationToken ct)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash != null);

    // ── IUserEmailStore ─────────────────────────────────────────────────────

    public Task SetEmailAsync(ApplicationUser user, string email, CancellationToken ct)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string> GetEmailAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<ApplicationUser> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApplicationUser>(
            "SELECT * FROM AspNetUsers WHERE NormalizedEmail = @NormalizedEmail",
            new { NormalizedEmail = normalizedEmail });
    }

    public Task<string> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(ApplicationUser user, string normalizedEmail, CancellationToken ct)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // ── IUserRoleStore ──────────────────────────────────────────────────────

    public async Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var roleId = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT Id FROM AspNetRoles WHERE NormalizedName = @NormalizedName",
            new { NormalizedName = roleName.ToUpperInvariant() });
        if (roleId != null)
            await conn.ExecuteAsync(
                $"INSERT INTO AspNetUserRoles (UserId, RoleId) " +
                $"SELECT @UserId, @RoleId {_dialect.SelectFromDual} " +
                $"WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId = @UserId AND RoleId = @RoleId)",
                new { UserId = user.Id, RoleId = roleId });
    }

    public async Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            DELETE FROM AspNetUserRoles
            WHERE UserId = @UserId
            AND RoleId = (SELECT Id FROM AspNetRoles WHERE NormalizedName = @NormalizedName)",
            new { UserId = user.Id, NormalizedName = roleName.ToUpperInvariant() });
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var roles = await conn.QueryAsync<string>(@"
            SELECT r.Name FROM AspNetRoles r
            INNER JOIN AspNetUserRoles ur ON ur.RoleId = r.Id
            WHERE ur.UserId = @UserId", new { UserId = user.Id });
        return roles.ToList();
    }

    public async Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var count = await conn.QuerySingleAsync<int>(@"
            SELECT COUNT(*) FROM AspNetUserRoles ur
            INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId AND r.NormalizedName = @NormalizedName",
            new { UserId = user.Id, NormalizedName = roleName.ToUpperInvariant() });
        return count > 0;
    }

    public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var users = await conn.QueryAsync<ApplicationUser>(@"
            SELECT u.* FROM AspNetUsers u
            INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id
            INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
            WHERE r.NormalizedName = @NormalizedName",
            new { NormalizedName = roleName.ToUpperInvariant() });
        return users.ToList();
    }

    // ── IUserSecurityStampStore ─────────────────────────────────────────────

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken ct)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string> GetSecurityStampAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.SecurityStamp);

    // ── IUserLockoutStore ───────────────────────────────────────────────────

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken ct)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken ct)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken ct)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // ── IUserClaimStore ─────────────────────────────────────────────────────

    public async Task<IList<Claim>> GetClaimsAsync(ApplicationUser user, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var rows = await conn.QueryAsync(
            "SELECT ClaimType, ClaimValue FROM AspNetUserClaims WHERE UserId = @UserId",
            new { UserId = user.Id });
        return rows.Select(r => new Claim((string)r.ClaimType, (string)(r.ClaimValue ?? ""))).ToList();
    }

    public async Task AddClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        foreach (var claim in claims)
            await conn.ExecuteAsync(
                "INSERT INTO AspNetUserClaims (UserId, ClaimType, ClaimValue) VALUES (@UserId, @ClaimType, @ClaimValue)",
                new { UserId = user.Id, ClaimType = claim.Type, ClaimValue = claim.Value });
    }

    public async Task ReplaceClaimAsync(ApplicationUser user, Claim claim, Claim newClaim, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE AspNetUserClaims
            SET ClaimType = @NewType, ClaimValue = @NewValue
            WHERE UserId = @UserId AND ClaimType = @OldType AND ClaimValue = @OldValue",
            new { UserId = user.Id, NewType = newClaim.Type, NewValue = newClaim.Value,
                  OldType = claim.Type, OldValue = claim.Value });
    }

    public async Task RemoveClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        foreach (var claim in claims)
            await conn.ExecuteAsync(@"
                DELETE FROM AspNetUserClaims
                WHERE UserId = @UserId AND ClaimType = @ClaimType AND ClaimValue = @ClaimValue",
                new { UserId = user.Id, ClaimType = claim.Type, ClaimValue = claim.Value });
    }

    public async Task<IList<ApplicationUser>> GetUsersForClaimAsync(Claim claim, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var users = await conn.QueryAsync<ApplicationUser>(@"
            SELECT u.* FROM AspNetUsers u
            INNER JOIN AspNetUserClaims c ON c.UserId = u.Id
            WHERE c.ClaimType = @ClaimType AND c.ClaimValue = @ClaimValue",
            new { ClaimType = claim.Type, ClaimValue = claim.Value });
        return users.ToList();
    }

    // ── IUserLoginStore ─────────────────────────────────────────────────────

    public async Task AddLoginAsync(ApplicationUser user, UserLoginInfo login, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AspNetUserLogins (LoginProvider, ProviderKey, ProviderDisplayName, UserId)
            VALUES (@LoginProvider, @ProviderKey, @ProviderDisplayName, @UserId)",
            new { LoginProvider = login.LoginProvider, ProviderKey = login.ProviderKey,
                  ProviderDisplayName = login.ProviderDisplayName, UserId = user.Id });
    }

    public async Task RemoveLoginAsync(ApplicationUser user, string loginProvider, string providerKey, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(@"
            DELETE FROM AspNetUserLogins
            WHERE UserId = @UserId AND LoginProvider = @LoginProvider AND ProviderKey = @ProviderKey",
            new { UserId = user.Id, LoginProvider = loginProvider, ProviderKey = providerKey });
    }

    public async Task<IList<UserLoginInfo>> GetLoginsAsync(ApplicationUser user, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        var rows = await conn.QueryAsync(@"
            SELECT LoginProvider, ProviderKey, ProviderDisplayName
            FROM AspNetUserLogins WHERE UserId = @UserId", new { UserId = user.Id });
        return rows.Select(r =>
            new UserLoginInfo((string)r.LoginProvider, (string)r.ProviderKey, (string)r.ProviderDisplayName))
            .ToList();
    }

    public async Task<ApplicationUser> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApplicationUser>(@"
            SELECT u.* FROM AspNetUsers u
            INNER JOIN AspNetUserLogins l ON l.UserId = u.Id
            WHERE l.LoginProvider = @LoginProvider AND l.ProviderKey = @ProviderKey",
            new { LoginProvider = loginProvider, ProviderKey = providerKey });
    }

    // ── IUserTwoFactorStore ─────────────────────────────────────────────────

    public Task<bool> GetTwoFactorEnabledAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.TwoFactorEnabled);

    public Task SetTwoFactorEnabledAsync(ApplicationUser user, bool enabled, CancellationToken ct)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    // ── IUserPhoneNumberStore ───────────────────────────────────────────────

    public Task SetPhoneNumberAsync(ApplicationUser user, string phoneNumber, CancellationToken ct)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string> GetPhoneNumberAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // ── IUserAuthenticatorKeyStore ──────────────────────────────────────────

    public async Task SetAuthenticatorKeyAsync(ApplicationUser user, string key, CancellationToken ct)
    {
        var p = new { UserId = user.Id, LoginProvider = AuthenticatorStoreLoginProvider,
                      Name = AuthenticatorKeyTokenName, Value = key };
        using var conn = _context.CreateConnection();
        var updated = await conn.ExecuteAsync(@"
            UPDATE AspNetUserTokens SET Value = @Value
            WHERE UserId = @UserId AND LoginProvider = @LoginProvider AND Name = @Name", p);
        if (updated == 0)
            await conn.ExecuteAsync(@"
                INSERT INTO AspNetUserTokens (UserId, LoginProvider, Name, Value)
                VALUES (@UserId, @LoginProvider, @Name, @Value)", p);
    }

    public async Task<string> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken ct)
    {
        using var conn = _context.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<string>(@"
            SELECT Value FROM AspNetUserTokens
            WHERE UserId = @UserId AND LoginProvider = @LoginProvider AND Name = @Name",
            new { UserId = user.Id, LoginProvider = AuthenticatorStoreLoginProvider,
                  Name = AuthenticatorKeyTokenName });
    }
}
