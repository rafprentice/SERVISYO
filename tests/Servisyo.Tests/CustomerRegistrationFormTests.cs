using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class CustomerRegistrationFormTests
{
    private static CustomerRegistrationViewModel Form(string name = "Juan dela Cruz") => new()
    {
        FullName = name, Email = "juan@email.com",
        Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", AcceptTerms = true
    };

    [Fact]
    public void FullName_SplitsIntoFirstAndLastName()
    {
        var m = Form("Juan dela Cruz").ToServiceModel();
        Assert.Equal("Juan", m.FirstName);
        Assert.Equal("dela Cruz", m.LastName);
    }

    [Fact]
    public void TwoWordName_Works_AndExtraSpacesAreIgnored()
    {
        var m = Form("  Maria    Santos ").ToServiceModel();
        Assert.Equal("Maria", m.FirstName);
        Assert.Equal("Santos", m.LastName);
    }

    [Theory]
    [InlineData("Osmeña Blvd, Brgy. Capitol Site, Cebu City", "Osmeña Blvd", "Capitol Site")]
    [InlineData("Gov. M. Cuenco Ave, Brgy. Banilad, Cebu City", "Gov. M. Cuenco Ave", "Banilad")]
    [InlineData("N. Bacalso Ave, Brgy. Sambag I, Cebu City", "N. Bacalso Ave", "Sambag I")]
    [InlineData("Salinas Drive, Brgy. Lahug, Cebu City", "Salinas Drive", "Lahug")]
    [InlineData("C. Padilla St, Brgy. Duljo Fatima, Cebu City", "C. Padilla St", "Duljo Fatima")]
    public void TheChosenAddress_IsSplitIntoStreetBarangayAndCity_AndSavedInFull(string chosen, string street, string barangay)
    {
        var f = Form();
        f.Location = chosen;

        Assert.Equal(street, f.StreetAddress);
        Assert.Equal(barangay, f.Barangay);
        Assert.Equal("Cebu City", f.City);
        Assert.Equal(chosen, f.ToServiceModel().Address);      // ADDRESS keeps the full text every page shows
    }

    [Fact]
    public void ThereAreExactlyTheFiveCebuCityAddresses()
    {
        Assert.Equal(new[]
        {
            "Osmeña Blvd, Brgy. Capitol Site, Cebu City",
            "Gov. M. Cuenco Ave, Brgy. Banilad, Cebu City",
            "N. Bacalso Ave, Brgy. Sambag I, Cebu City",
            "Salinas Drive, Brgy. Lahug, Cebu City",
            "C. Padilla St, Brgy. Duljo Fatima, Cebu City"
        }, CebuLocations.All.Select(l => l.Full));
    }

    [Theory]
    [InlineData("123 Rizal St., Brgy. Lahug")]                       // typed instead of chosen
    [InlineData("osmeña blvd, brgy. capitol site, cebu city")]       // changed in the browser
    [InlineData("Salinas Drive, Brgy. Lahug, Mandaue City")]
    public void AnAddressThatIsNotInTheList_IsNotSplit(string other)
    {
        var f = Form();
        f.Location = other;
        Assert.Null(f.StreetAddress);
        Assert.Null(f.Barangay);
        Assert.Null(CebuLocations.Find(other));
    }

    [Fact]
    public void EmptyOptionalFields_BecomeNull()
    {
        var f = Form();
        f.Location = null;
        f.PhoneNumber = "  ";
        var m = f.ToServiceModel();
        Assert.Null(m.Address);
        Assert.Null(m.PhoneNumber);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MustBeTrue_OnlyAcceptsTrue(bool value, bool expected)
    {
        var attr = new MustBeTrueAttribute();
        Assert.Equal(expected, attr.IsValid(value));
    }
}
