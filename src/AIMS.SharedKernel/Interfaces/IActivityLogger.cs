#nullable enable
using System.Threading.Tasks;

namespace AIMS.SharedKernel.Interfaces;

/// <summary>
/// Interface for logging user activities in the system.
/// </summary>
public interface IActivityLogger
{
    /// <summary>
    /// Logs a user activity asynchronously.
    /// </summary>
    /// <param name="activityType">The type of activity (e.g., Login, Logout, UserCreated).</param>
    /// <param name="description">A description of the activity.</param>
    /// <param name="entityName">The name of the related entity (optional).</param>
    /// <param name="entityId">The ID of the related entity (optional).</param>
    /// <param name="result">The result of the activity (Success, Failure, etc.).</param>
    /// <param name="userId">Override the current user ID (optional).</param>
    /// <param name="userName">Override the current user name (optional).</param>
    Task LogActivityAsync(
        string activityType,
        string? description = null,
        string? entityName = null,
        string? entityId = null,
        string? result = null,
        string? userId = null,
        string? userName = null);

    /// <summary>
    /// Logs a security-related activity asynchronously.
    /// </summary>
    Task LogSecurityActivityAsync(
        string activityType,
        string? description = null,
        string? result = null,
        string? userId = null,
        string? userName = null);
}
