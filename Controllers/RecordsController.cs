using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class RecordsController : Controller
    {
        // GET: /Records/Index
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}
