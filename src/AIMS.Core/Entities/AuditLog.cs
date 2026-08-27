#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Core.Entities;

/// <summary>
/// Represents an audit trail entry for tracking changes to entities and user activities in the system.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    /// <summary>
    /// The category of the audit log: Entity for data changes, Activity for user actions.
    /// </summary>
    [MaxLength(50)]
    public string Category { get; set; } = AuditCategory.Entity;

    /// <summary>
    /// The name of the entity that was changed (for entity audits) or the activity type (for activity audits).
    /// </summary>
    [MaxLength(100)]
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// The primary key of the entity that was changed.
    /// </summary>
    [MaxLength(50)]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// The type of action performed: Created, Updated, Deleted, Login, Logout, etc.
    /// </summary>
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// JSON representation of the old values (for updates and deletes).
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// JSON representation of the new values (for creates and updates).
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// The properties that were changed (for updates).
    /// </summary>
    public string? ChangedColumns { get; set; }

    /// <summary>
    /// Additional details or description of the activity.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The user who made the change.
    /// </summary>
    [MaxLength(256)]
    public string? UserId { get; set; }

    /// <summary>
    /// The username of the user who made the change.
    /// </summary>
    [MaxLength(256)]
    public string? UserName { get; set; }

    /// <summary>
    /// The timestamp when the change occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The IP address from which the change was made.
    /// </summary>
    [MaxLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// The user agent (browser/client info) from which the action was performed.
    /// </summary>
    [MaxLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// The URL or page where the action was performed.
    /// </summary>
    [MaxLength(500)]
    public string? RequestPath { get; set; }

    /// <summary>
    /// The result of the action: Success, Failure, etc.
    /// </summary>
    [MaxLength(50)]
    public string? Result { get; set; }
}

/// <summary>
/// Categories for audit logs.
/// </summary>
public static class AuditCategory
{
    public const string Entity = "Entity";
    public const string Activity = "Activity";
    public const string Security = "Security";
}

/// <summary>
/// Enum representing the type of audit action.
/// </summary>
public enum AuditAction
{
    Created,
    Updated,
    Deleted
}

/// <summary>
/// Activity types for user activity logging.
/// </summary>
public static class ActivityType
{
    // Authentication activities
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";

    // User management activities
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeleted = "UserDeleted";
    public const string RoleAssigned = "RoleAssigned";
    public const string RoleRemoved = "RoleRemoved";

    // Role management activities
    public const string RoleCreated = "RoleCreated";
    public const string RoleUpdated = "RoleUpdated";
    public const string RoleDeleted = "RoleDeleted";

    // Data access activities
    public const string DataExported = "DataExported";
    public const string DataImported = "DataImported";
    public const string ReportGenerated = "ReportGenerated";

    // System activities
    public const string SettingsChanged = "SettingsChanged";
    public const string AccessDenied = "AccessDenied";
    public const string SessionExpired = "SessionExpired";
}
