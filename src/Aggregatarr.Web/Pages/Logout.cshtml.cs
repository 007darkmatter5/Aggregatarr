using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aggregatarr.Web.Pages;

[IgnoreAntiforgeryToken]
public sealed class LogoutModel : PageModel
{
    public Task<IActionResult> OnPost() => Out();

    public Task<IActionResult> OnGet() => Out();

    private async Task<IActionResult> Out()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }
}
