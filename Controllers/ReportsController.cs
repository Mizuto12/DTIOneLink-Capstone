using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class ReportsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
