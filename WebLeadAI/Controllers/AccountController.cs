using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebLeadAI.Models;
using WebLeadAI.Security;

namespace WebLeadAI.Controllers;

public sealed class AccountController(IConfiguration config, AdminLoginThrottle throttle) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Leads");
        return View(new AdminLoginViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        // Never trust forwarded headers from arbitrary clients to decide lockout keys.
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (throttle.IsBlocked(key))
        {
            ModelState.AddModelError(string.Empty, "Pārāk daudz mēģinājumu. Mēģini vēlāk.");
            return View(model);
        }

        var expectedUser = config["WebLeadAdmin:Username"];
        var storedHash = config["WebLeadAdmin:PasswordHash"];
        var valid = false;
        if (!string.IsNullOrWhiteSpace(expectedUser) && !string.IsNullOrWhiteSpace(storedHash))
        {
            // Fixed username comparison plus framework's salted password-hash verification.
            if (string.Equals(model.UserName, expectedUser, StringComparison.Ordinal))
            {
                try
                {
                    var result = new PasswordHasher<string>()
                        .VerifyHashedPassword("WebLeadAI-admin", storedHash, model.Password);
                    valid = result != PasswordVerificationResult.Failed;
                }
                catch (FormatException) { valid = false; }
                catch (ArgumentException) { valid = false; }
            }
        }

        if (!valid)
        {
            throttle.RecordFailure(key);
            ModelState.AddModelError(string.Empty, "Nepareizs lietotājvārds vai parole.");
            return View(model);
        }

        throttle.Reset(key);
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, expectedUser!), new Claim(ClaimTypes.Role, "Administrator") },
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        return Url.IsLocalUrl(model.ReturnUrl)
            ? LocalRedirect(model.ReturnUrl!)
            : RedirectToAction("Index", "Leads");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
