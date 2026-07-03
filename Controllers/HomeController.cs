using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return Content("Logged in successfully. Replace this with your actual dashboard view.");
        }
    }
}
