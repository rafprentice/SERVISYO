using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

public class HomeController : Controller
{
    private readonly IHomePageService _homePage;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IHomePageService homePage, ILogger<HomeController> logger)
    {
        _homePage = homePage;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            return View(await _homePage.GetHomePageAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not load the home page data.");
            return View(new HomePageViewModel { LoadFailed = true });
        }
    }

    public IActionResult Privacy() => View();

    public IActionResult Terms() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
