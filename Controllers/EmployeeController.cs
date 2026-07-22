using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class EmployeeController : Controller
    {
        // GET: /Employee or /Employee/Index
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // GET: /Employee/TaskManagement
        [HttpGet]
        public IActionResult TaskManagement()
        {
            return View();
        }

        // GET: /Employee/Reports
        [HttpGet]
        public IActionResult Reports()
        {
            return View();
        }
    }
}
