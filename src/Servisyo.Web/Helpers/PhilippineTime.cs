namespace Servisyo.Web.Helpers;

/// Philippine Standard Time: UTC+8, no daylight saving. The time zone is "Asia/Manila" (Linux, macOS, and Windows with
/// ICU, the .NET default) or "Singapore Standard Time" (the Windows name for UTC+8 without daylight saving). If neither
/// exists, a fixed UTC+8 zone is used. The database works with the same fixed 8 hours (dbo.fn_ManilaNow).
public static class PhilippineTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static TimeZoneInfo Zone { get; } = Find();

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Philippine Standard Time", Offset, "Philippine Standard Time", "Philippine Standard Time");
    }

    /// A UTC moment (as stored in the database) as the clock on a wall in Cebu shows it.
    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// Now, in Cebu.
    public static DateTime Now(TimeProvider clock) => FromUtc(clock.GetUtcNow().UtcDateTime);
}
