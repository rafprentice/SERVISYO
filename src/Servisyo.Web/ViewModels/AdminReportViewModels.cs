namespace Servisyo.Web.ViewModels;

public record ReportStatusCount(string Status, int Count);
public record ReportCategory(int Rank, string Name, int Bookings, decimal SharePercent);
public record ReportMonthRevenue(string Month, decimal Revenue, int PaidBookings);

/// The monthly summary that the admin prints / saves as PDF.
public class AdminReportViewModel
{
    public string MonthKey { get; set; } = string.Empty;         // "2026-10" (for the month picker)
    public string MonthLabel { get; set; } = string.Empty;       // "October 2026"
    public string GeneratedText { get; set; } = string.Empty;    // "Oct 28, 2026 10:15 PM (Cebu time)"
    public string GeneratedBy { get; set; } = string.Empty;

    // sales
    public decimal TotalRevenue { get; set; }
    public decimal PlatformFeePercent { get; set; }
    public decimal PlatformFees { get; set; }
    public decimal ProviderEarnings { get; set; }
    public int PaidBookings { get; set; }
    public decimal AverageBookingValue { get; set; }

    // bookings
    public int TotalBookings { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Rejected { get; set; }
    public decimal CompletionRatePercent { get; set; }          // completed / (completed + cancelled + rejected)
    public IReadOnlyList<ReportStatusCount> ByStatus { get; set; } = Array.Empty<ReportStatusCount>();

    // demand
    public IReadOnlyList<ReportCategory> TopCategories { get; set; } = Array.Empty<ReportCategory>();

    // people
    public int ActiveCustomers { get; set; }
    public int ActiveProviders { get; set; }
    public int PendingApplications { get; set; }
    public int NewCustomers { get; set; }
    public int NewProviders { get; set; }

    public IReadOnlyList<ReportMonthRevenue> Trend { get; set; } = Array.Empty<ReportMonthRevenue>();   // last 6 months
}
