using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthService auth, ILogger<AccountController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    // ---------- Customer registration ----------
    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new RegisterCustomerViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterCustomerViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _auth.RegisterCustomerAsync(vm);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(vm.Email), result.Error!);
            return View(vm);
        }

        _logger.LogInformation("Customer registered. UserId={UserId}", result.User!.UserId);
        await SignInUserAsync(result.User!);
        return RedirectToAction("Index", "Home");
    }

    // ---------- Provider registration ----------
    [HttpGet]
    public IActionResult RegisterProvider()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new RegisterProviderViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> RegisterProvider(RegisterProviderViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _auth.RegisterProviderAsync(vm);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(vm.Email), result.Error!);
            return View(vm);
        }

        _logger.LogInformation("Provider applied. UserId={UserId}", result.User!.UserId);
        await SignInUserAsync(result.User!);
        return RedirectToAction(nameof(PendingApproval));
    }

    [Authorize(Roles = Servisyo.Web.Models.Roles.ServiceProvider)]
    public IActionResult PendingApproval() => View();

    // ---------- Login / logout ----------
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(vm);

        var result = await _auth.ValidateLoginAsync(vm.Email, vm.Password);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Failed sign-in attempt.");
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(vm);
        }

        await SignInUserAsync(result.User!);
        _logger.LogInformation("User signed in. UserId={UserId}", result.User!.UserId);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ---------- helper ----------
    private async Task SignInUserAsync(Servisyo.Web.Models.User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
    }
}
