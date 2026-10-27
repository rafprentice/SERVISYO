using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Admin > System Logs: read-only view of SYSTEM_LOGS (written by the database triggers and by sign-ins).
[Authorize(Roles = Roles.Administrator)]
[Route("admin/logs")]
public class AdminSystemLogsController : Controller
{
    private readonly ISystemLogService _logs;

    public AdminSystemLogsController(ISystemLogService logs) => _logs = logs;

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] SystemLogQuery query, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();

        var result = await _logs.GetPageAsync(adminId, query, ct);
        if (!result.Allowed)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }
        return View(result.Page!);
    }
}
