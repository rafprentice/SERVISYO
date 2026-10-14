namespace Servisyo.Web.Helpers;

public static class NameFormat
{
    /// "Marco Reyes" -> "MR", "Juan" -> "J"
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    /// "Elena", "Cruz" -> "Elena C."  (used when showing a customer's name publicly, e.g. in reviews)
    public static string ShortName(string first, string last) =>
        string.IsNullOrWhiteSpace(last) ? first : $"{first} {last.Trim()[..1].ToUpperInvariant()}.";

    public static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }
}
