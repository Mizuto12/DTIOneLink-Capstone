using Microsoft.AspNetCore.Mvc;
using DTIOneLink.Models;

namespace DTIOneLink.Controllers
{
    public class DashboardController : Controller
    {

        [HttpGet]
        public IActionResult AdminDashboard()
        {
            var model = new LoginViewModel{ ReturnUrl = Url.Action("AdminDashboard", "Dashboard") };
            return View(model);
        }

        [HttpPost]
        public IActionResult AdminDashboard(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            return View(model);
        }
    }
}
