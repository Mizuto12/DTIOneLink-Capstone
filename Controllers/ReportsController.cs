using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class ReportsController : Controller
    {
        // GET: /Reports or /Reports/Index
        // Shared by both Admin and Employee sidebars — the view itself should pick
        // AdminLayout or EmployeeLayout based on the logged-in user's session role,
        // the same way Views/Records/Index.cshtml does.
        [HttpGet]
        public IActionResult Index()
        {
            var role = HttpContext.Session.GetString("UserRole");

            if (role != "Admin" && role != "Employee")
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }
    }
}
