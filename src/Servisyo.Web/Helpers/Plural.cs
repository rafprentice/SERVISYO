namespace Servisyo.Web.Helpers;

public static class Plural
{
    /// Plural.Of(1, "customer") -> "1 customer";  Plural.Of(6, "customer") -> "6 customers"
    public static string Of(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}
