using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;

namespace DTIOneLink.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return Content("Logged in successfully. Replace this with your actual dashboard view.");
        }

        public IActionResult Error()
        {
            var error = HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
            return StatusCode(StatusCodes.Status500InternalServerError,
                error?.Message ?? "An unexpected error occurred. Please try again.");
        }
    }
}
