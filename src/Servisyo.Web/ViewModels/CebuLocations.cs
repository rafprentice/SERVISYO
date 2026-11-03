using System.ComponentModel.DataAnnotations;

namespace Servisyo.Web.ViewModels;

/// One address a user can choose when registering. SERVISYO serves Cebu City only.
public sealed record CebuLocation(string Street, string Barangay)
{
    public string City => ServiceArea.City;

    /// "Osmeña Blvd, Brgy. Capitol Site, Cebu City": what the dropdown shows and what is saved in ADDRESS.
    public string Full => $"{Street}, Brgy. {Barangay}, {City}";
}

/// The addresses offered by the registration dropdowns (customer and provider).
/// The database keeps one ADDRESS column, so the full text is saved there and every page that shows an
/// address keeps working; the street, barangay and city are split out here when they are needed separately.
public static class CebuLocations
{
    public static readonly IReadOnlyList<CebuLocation> All = new[]
    {
        new CebuLocation("Osmeña Blvd", "Capitol Site"),
        new CebuLocation("Gov. M. Cuenco Ave", "Banilad"),
        new CebuLocation("N. Bacalso Ave", "Sambag I"),
        new CebuLocation("Salinas Drive", "Lahug"),
        new CebuLocation("C. Padilla St", "Duljo Fatima"),
    };

    /// The option with exactly this full text, or null (a value typed or changed in the browser is never accepted).
    public static CebuLocation? Find(string? full)
    {
        var wanted = full?.Trim();
        return string.IsNullOrEmpty(wanted) ? null : All.FirstOrDefault(l => string.Equals(l.Full, wanted, StringComparison.Ordinal));
    }
}

/// The value must be one of CebuLocations.All (blank is left to [Required]).
[AttributeUsage(AttributeTargets.Property)]
public sealed class CebuLocationAttribute : ValidationAttribute
{
    public CebuLocationAttribute() : base("Please choose one of the addresses in the list.") { }

    public override bool IsValid(object? value) =>
        value is null || (value is string s && (s.Trim().Length == 0 || CebuLocations.Find(s) is not null));
}
