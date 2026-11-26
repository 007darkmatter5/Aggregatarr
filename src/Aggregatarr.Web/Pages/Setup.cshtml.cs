using Aggregatarr.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aggregatarr.Web.Pages;

[AllowAnonymous]
public sealed class SetupModel(AccountService accounts) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (ok, error, user) = await accounts.CreateFirstAsync(Username, Password, Confirm);
        if (!ok || user is null)
        {
            Error = error;
            return Page();
        }

        await AuthSignIn.SignInAsync(HttpContext, user);
        return Redirect("/settings");
    }
}
