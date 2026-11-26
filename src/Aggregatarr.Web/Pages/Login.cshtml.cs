using Aggregatarr.Web.Hosting;
using Aggregatarr.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aggregatarr.Web.Pages;

[AllowAnonymous]
public sealed class LoginModel(AccountService accounts, AppOptions options) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";

    public string? Error { get; private set; }
    public string ReturnUrl { get; private set; } = "/";
    public bool DemoMode => options.DemoMode;

    public void OnGet(string? returnUrl) => ReturnUrl = AuthSignIn.LocalReturn(returnUrl);

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        ReturnUrl = AuthSignIn.LocalReturn(returnUrl);
        var user = await accounts.ValidateAsync(Username, Password);
        if (user is null)
        {
            Error = "Those credentials didn't match.";
            return Page();
        }

        await AuthSignIn.SignInAsync(HttpContext, user);
        return Redirect(ReturnUrl);
    }
}
