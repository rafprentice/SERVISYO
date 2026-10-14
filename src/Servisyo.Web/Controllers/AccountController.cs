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
        return View(new CustomerRegistrationViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Register(CustomerRegistrationViewModel form)
    {
        if (!ModelState.IsValid) return View(form);

        var result = await _auth.RegisterCustomerAsync(form.ToServiceModel());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(form.Email), result.Error!);
            return View(form);
        }

        _logger.LogInformation("Customer registered. UserId={UserId}", result.User!.UserId);
        await SignInUserAsync(result.User!, rememberMe: false);
        return RedirectAfterSignIn(result.User!);
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
        await SignInUserAsync(result.User!, rememberMe: false);
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
            vm.Password = string.Empty;
            return View(vm);
        }

        await SignInUserAsync(result.User!, vm.RememberMe);
        _logger.LogInformation("User signed in. UserId={UserId}", result.User!.UserId);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectAfterSignIn(result.User!);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // Password reset is not built yet; this page says so honestly instead of a dead link.
    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ---------- helpers ----------
    private IActionResult RedirectAfterSignIn(Servisyo.Web.Models.User user) => user.Role switch
    {
        Servisyo.Web.Models.Roles.Customer => RedirectToAction("Customer", "Dashboard"),
        Servisyo.Web.Models.Roles.ServiceProvider => RedirectToAction("Provider", "Dashboard"),
        Servisyo.Web.Models.Roles.Administrator => RedirectToAction("Admin", "Dashboard"),
        _ => RedirectToAction("Index", "Home")
    };

    private async Task SignInUserAsync(Servisyo.Web.Models.User user, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties { IsPersistent = rememberMe };
        if (rememberMe) props.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            props);
    }
}
