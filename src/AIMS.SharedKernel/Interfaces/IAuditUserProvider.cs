#nullable enable
namespace AIMS.SharedKernel.Interfaces;

/// <summary>
/// Interface to provide current user information for audit trail.
/// </summary>
public interface IAuditUserProvider
{
    /// <summary>
    /// Gets the current user's ID.
    /// </summary>
    string? GetUserId();

    /// <summary>
    /// Gets the current user's name.
    /// </summary>
    string? GetUserName();

    /// <summary>
    /// Gets the current user's IP address.
    /// </summary>
    string? GetIpAddress();
}
