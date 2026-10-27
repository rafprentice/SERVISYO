using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;

namespace Servisyo.Web.Controllers;

/// Admin > Export PDF Report: a print-ready monthly summary. The browser's print window saves it as a PDF
/// ("Save as PDF" / "Microsoft Print to PDF"), so no PDF library is needed on the server.
[Authorize(Roles = Roles.Administrator)]
[Route("admin/reports")]
public class AdminReportsController : Controller
{
    private readonly IAdminReportService _reports;

    public AdminReportsController(IAdminReportService reports) => _reports = reports;

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(string? month, bool print = false, CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();

        var result = await _reports.GetMonthlyAsync(adminId, month, ct);
        if (!result.Allowed)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }
        ViewData["AutoPrint"] = print;
        return View(result.Report!);
    }
}
