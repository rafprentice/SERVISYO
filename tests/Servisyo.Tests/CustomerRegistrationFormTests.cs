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

    [Fact]
    public void StreetAndBarangay_AreCombinedIntoOneAddress()
    {
        var f = Form();
        f.StreetAddress = "123 Rizal St.";
        f.Barangay = "Brgy. San Isidro";
        f.City = "Cebu City";
        var m = f.ToServiceModel();
        Assert.Equal("123 Rizal St., Brgy. San Isidro", m.Address);
        Assert.Equal("Cebu City", m.City);
    }

    [Fact]
    public void EmptyOptionalFields_BecomeNull()
    {
        var f = Form();
        f.StreetAddress = " ";
        f.Barangay = null;
        f.City = "";
        f.PhoneNumber = "  ";
        var m = f.ToServiceModel();
        Assert.Null(m.Address);
        Assert.Null(m.City);
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
