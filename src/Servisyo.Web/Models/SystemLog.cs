namespace Servisyo.Web.Models;

/// One line in the system audit log (table SYSTEM_LOGS). Written by the database audit triggers (database v2.10:
/// one standard format, see Data/AuditStandardSql.cs), by the app for sign-ins (AuditLogService) and by the
/// inactive-account clean-up. There is deliberately no foreign key to USERS: a log line must survive the account.
public class SystemLog
{
    public int SystemLogId { get; set; }
    public int? UserId { get; set; }                 // who did it (null = the system / not signed in)
    public string UserRole { get; set; } = "System"; // Customer, Service Provider, Administrator, Guest, System
    public string ActionType { get; set; } = string.Empty;   // AuditActionType: INSERT, UPDATE, DELETE, LOGIN
    public string EntityName { get; set; } = string.Empty;   // User, Booking, Payment, Service, Review, Provider, ...
    public int? EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// Database v2.10: the ONLY four values of SYSTEM_LOGS.ACTION_TYPE (the CHECK CK_SYSTEM_LOGS_ACTION_TYPE refuses anything else).
///   INSERT  "{Role} {Full Name} created {Entity} #{Id}: {summary}."
///   UPDATE  "{Role} {Full Name} updated {Entity} #{Id}: {field} '{old}' -> '{new}'."   (one line per changed field)
///   DELETE  "{Role} {Full Name} deleted {Entity} #{Id}: {summary}."
///   LOGIN   "{Full Name} ({Email}) signed in."  /  "Failed login attempt for {Email}."
public static class AuditActionType
{
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Login = "LOGIN";
    public static readonly IReadOnlyList<string> All = new[] { Insert, Update, Delete, Login };

    /// The CHECK constraint text (kept here so the model, the migration and the tests use the same one).
    public const string Check = "[ACTION_TYPE] IN ('INSERT','UPDATE','DELETE','LOGIN')";
}
