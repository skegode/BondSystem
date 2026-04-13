using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace OnwardsSwift.API.Controllers
{
    [Authorize]
    public abstract class AppController : Controller
    {
        protected string CurrentUserEmail => User.FindFirstValue(ClaimTypes.Email) ?? "system";
        protected string CurrentUserName  => User.FindFirstValue(ClaimTypes.Name)  ?? "system";

        protected void Success(string msg) => TempData["Success"] = msg;
        protected void Error(string msg)   => TempData["Error"]   = msg;
    }
}
