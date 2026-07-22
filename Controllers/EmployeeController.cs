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
    }
}
