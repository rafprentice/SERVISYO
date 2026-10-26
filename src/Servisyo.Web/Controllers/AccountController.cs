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
    private readonly IPasswordResetService _passwordReset;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthService auth, IPasswordResetService passwordReset, ILogger<AccountController> logger)
    {
        _auth = auth;
        _passwordReset = passwordReset;
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

    // ---------- Forgot / reset password (6-digit code sent by email) ----------
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // Same answer whether or not the address is registered, so this page cannot be used to find out who has an account.
        await _passwordReset.RequestResetAsync(vm.Email);

        TempData["ResetInfo"] = "If that email is registered, we sent a 6-digit code to it. It expires in 15 minutes.";
        return RedirectToAction(nameof(ResetPassword), new { email = vm.Email.Trim() });
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new ResetPasswordViewModel { Email = email?.Trim() ?? string.Empty });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var status = await _passwordReset.ResetPasswordAsync(vm.Email, vm.Code, vm.NewPassword);
        switch (status)
        {
            case PasswordResetStatus.Ok:
                TempData["LoginNotice"] = "Your password was changed. Please sign in with your new password.";
                return RedirectToAction(nameof(Login));

            case PasswordResetStatus.TooManyAttempts:
                ModelState.AddModelError(nameof(vm.Code), "Too many wrong tries. Please request a new code.");
                break;

            default:
                ModelState.AddModelError(nameof(vm.Code), "That code is incorrect or has expired.");
                break;
        }

        _logger.LogWarning("Password reset code was refused ({Status}).", status);
        return View(vm);       // password boxes are never filled back in by the page
    }

    [HttpPost]
    public async Task<IActionResult> ResendResetCode(string? email)
    {
        // Silent when the address is unknown or asked again within a minute, like the first request.
        await _passwordReset.RequestResetAsync(email);

        TempData["ResetInfo"] = "If that email is registered, a new 6-digit code was sent. You can ask for a new code once a minute.";
        return RedirectToAction(nameof(ResetPassword), new { email = email?.Trim() });
    }

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
