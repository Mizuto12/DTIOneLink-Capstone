using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class RecordsController : Controller
    {
        // GET: /Records or /Records/Index
        // Shared by both Admin and Employee sidebars — the view itself picks
        // AdminLayout or EmployeeLayout based on the logged-in user's session role.
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
