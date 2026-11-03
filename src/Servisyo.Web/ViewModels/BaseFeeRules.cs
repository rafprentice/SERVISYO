using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Servisyo.Web.ViewModels;

/// The one rule for a provider's base fee (SERVICE_PROVIDERS.BASE_FEE), used by the registration form and the
/// My Profile page: required, at least 1 PHP, at most 100,000 PHP. Zero and negative fees are never saved.
/// The browser runs the same check (wwwroot/js/base-fee.js); the server checks again because a request can skip the page.
public static class BaseFeeRules
{
    public const int Min = 1;
    public const int Max = 100000;

    public const string RequiredMessage = "Please enter your base fee.";
    public const string MinMessage = "Base fee must be at least 1 PHP.";
    public const string MaxMessage = "Base fee can be at most 100,000 PHP.";
    public const string NotANumberMessage = "Enter the base fee as a number, for example 500.";

    /// null when the fee is fine, otherwise the message to show under the box.
    public static string? Check(decimal? fee) =>
        fee is null ? RequiredMessage
        : fee < Min ? MinMessage
        : fee > Max ? MaxMessage
        : null;

    /// BASE_FEE is DECIMAL(12,2): keep two decimals (1500.555 -> 1500.56). Call after Check.
    public static decimal Round(decimal fee) => Math.Round(fee, 2, MidpointRounding.AwayFromZero);

    /// "1500.5" for value="" of an input type=number (always a dot, whatever the server's culture is).
    public static string? ForInput(decimal? fee) => fee?.ToString("0.##", CultureInfo.InvariantCulture);

    /// A [Range] attribute has one message, so a fee that is too HIGH (or text that is not a number) would also read
    /// "at least 1 PHP". This puts the right message on the BaseFee entry; other fields are left alone.
    public static void FixModelStateMessage(ModelStateDictionary modelState, decimal? fee, string key = "BaseFee")
    {
        if (!modelState.TryGetValue(key, out var entry) || entry.Errors.Count == 0) return;

        var typedSomething = !string.IsNullOrWhiteSpace(entry.AttemptedValue);
        var message = fee is null && typedSomething ? NotANumberMessage : Check(fee) ?? MinMessage;
        entry.Errors.Clear();
        modelState.AddModelError(key, message);
    }
}
