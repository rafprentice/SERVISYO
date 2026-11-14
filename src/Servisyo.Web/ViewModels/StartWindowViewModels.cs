using System.Globalization;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// Database v2.12: an Accepted, scheduled booking on the provider's card, against its start time (Philippine time).
///   TooEarly - no button, a muted notice;  OnTime - the blue "Mark In Progress";
///   Late - the amber "Mark In Progress (Late)" (a reason is required);  Expired - no button, the no-show notice.
public sealed class StartWindowViewModel
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public StartWindowState State { get; init; }
    /// "Nov 27, 2026 · 09:30 AM" (the format of the notice: MMM dd, yyyy · hh:mm tt).
    public string ScheduledText { get; init; } = string.Empty;
    /// Whole minutes since the scheduled time (negative before it).
    public int MinutesAfter { get; init; }
    /// Seconds until the state changes, so the page can redraw itself then; null once it will not change any more.
    public int? RefreshInSeconds { get; init; }

    public bool CanStart => State is StartWindowState.OnTime or StartWindowState.Late;

    /// The scheduled start and the server's clock as ISO times with the Philippine offset
    /// ("2026-11-27T09:30:00+08:00"): booking-start.js counts down from the difference, so a phone whose clock is
    /// wrong still unlocks the button at the right second.
    public string ScheduledIso { get; init; } = string.Empty;
    public string ServerNowIso { get; init; } = string.Empty;
    /// Whole seconds until the scheduled time (0 once it has come).
    public int SecondsUntilStart { get; init; }
    /// "Starts in 00:54" (too early only).
    public string CountdownText => Countdown(SecondsUntilStart);

    /// "Starts in 00:54", "Starts in 3:05:12", "Starts in 2d 03:05:12" (the same as booking-start.js).
    public static string Countdown(int seconds)
    {
        if (seconds <= 0) return "Starting now";
        var t = TimeSpan.FromSeconds(seconds);
        if (seconds < 3600) return $"Starts in {t.Minutes:00}:{t.Seconds:00}";
        if (seconds < 86400) return $"Starts in {t.Hours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"Starts in {t.Days}d {t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    /// The muted line shown instead of a button (null while a button is shown).
    public string? Notice => State switch
    {
        StartWindowState.TooEarly => $"Scheduled for {ScheduledText}. Start button unlocks at scheduled time.",
        StartWindowState.Expired => "Start window missed: more than 2 hours have passed since the scheduled time. " +
                                    "This booking will be cancelled automatically (Provider No-Show).",
        _ => null
    };

    /// "25 minutes late" while the late button is shown.
    public string? LateText => State == StartWindowState.Late ? Plural.Of(MinutesAfter, "minute") + " late" : null;

    public static StartWindowViewModel For(DateOnly date, TimeOnly time, DateTime manilaNow)
    {
        var minutes = StartRules.MinutesAfter(date, time, manilaNow);
        var next = StartRules.NextChange(date, time, manilaNow);
        var scheduled = StartRules.Scheduled(date, time);
        return new StartWindowViewModel
        {
            ScheduledIso = scheduled.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv) + "+08:00",
            ServerNowIso = manilaNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", Inv) + "+08:00",
            SecondsUntilStart = (int)Math.Max(0, Math.Ceiling((scheduled - manilaNow).TotalSeconds)),
            State = StartRules.StateAt(minutes),
            ScheduledText = $"{date.ToString("MMM dd, yyyy", Inv)} · {time.ToString("hh:mm tt", Inv)}",
            MinutesAfter = minutes,
            // a few seconds after the change, so the server already sees the new state when the page asks again
            RefreshInSeconds = next is null ? null : (int)Math.Clamp(Math.Ceiling((next.Value - manilaNow).TotalSeconds) + 2, 1, int.MaxValue)
        };
    }
}

/// The blue "Mark In Progress" form of one card (shown at once inside the window, or hidden until the countdown ends).
public sealed record StartButtonViewModel(int BookingId, string Title, string Tab, int Page);

/// Database v2.12: when a job started, shown on In Progress / Completed bookings to the provider, the customer and the
/// administrators (Views/Shared/_StartInfo.cshtml). Null for a job that started before v2.12 (no STARTED_AT).
public sealed class StartInfoViewModel
{
    public string StartedText { get; init; } = string.Empty;    // "Nov 27, 2026 · 9:42 AM" (Cebu time)
    public int? MinutesLate { get; init; }                       // null = on time (within 15 minutes)
    public string? LateReason { get; init; }

    public bool IsLate => MinutesLate is not null;
    public string LateText => MinutesLate is int m ? Plural.Of(m, "minute") + " late" : string.Empty;

    public static StartInfoViewModel? From(string status, DateOnly date, TimeOnly time, DateTime? startedAtUtc, string? lateReason)
    {
        if (startedAtUtc is null || status is not (BookingStatuses.InProgress or BookingStatuses.Completed)) return null;
        var started = PhilippineTime.FromUtc(startedAtUtc.Value);
        var late = StartRules.MinutesLate(date, time, startedAtUtc);
        return new StartInfoViewModel
        {
            StartedText = started.ToString("MMM d, yyyy · h:mm tt", CultureInfo.InvariantCulture),
            MinutesLate = late,
            LateReason = late is null || string.IsNullOrWhiteSpace(lateReason) ? null : lateReason.Trim()
        };
    }
}
