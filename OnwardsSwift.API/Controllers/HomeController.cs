using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.Interfaces;

namespace OnwardsSwift.API.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {

        public async Task<IActionResult> Index()
        {
            //if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            //{
            //    return RedirectToAction("Login", "Account");
            //}


            return View();
        }

        public IActionResult Error() => View();
    }
}
