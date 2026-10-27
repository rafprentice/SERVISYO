namespace Servisyo.Web.Models;

/// One line in the system audit log (table SYSTEM_LOGS). Written by the database triggers on BOOKINGS, USERS and SERVICES,
/// and by the app for sign-ins. There is deliberately no foreign key to USERS: a log line must survive the account.
public class SystemLog
{
    public int SystemLogId { get; set; }
    public int? UserId { get; set; }                 // who did it (null = the system / not signed in)
    public string UserRole { get; set; } = "System"; // Customer, Service Provider, Administrator, Guest, System
    public string ActionType { get; set; } = string.Empty;   // INSERT, UPDATE, DELETE, LOGIN
    public string EntityName { get; set; } = string.Empty;   // Booking, Payment, User, Service
    public int? EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class SystemLogActions
{
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Login = "LOGIN";
    public static readonly string[] All = { Insert, Update, Delete, Login };
}
