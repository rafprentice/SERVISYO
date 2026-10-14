using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;

namespace Servisyo.Web.Controllers;

// TEMPORARY: lets us test role authorization in the browser.
// These will be replaced by real dashboards in later stages.
[Authorize]
public class DashboardController : Controller
{
    [Authorize(Roles = Roles.Customer)]
    public IActionResult Customer() => Content("Customer area. Only customers can see this.");

    [Authorize(Roles = Roles.ServiceProvider)]
    public IActionResult Provider() => Content("Provider area. Only service providers can see this.");

    [Authorize(Roles = Roles.Administrator)]
    public IActionResult Admin() => Content("Administrator area. Only administrators can see this.");
}
