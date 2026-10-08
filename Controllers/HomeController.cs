using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;

namespace DTIOneLink.Controllers
{
    public class HomeController(ILogger<HomeController> logger) : Controller
    {
        public IActionResult Index()
        {
            return Content("Logged in successfully. Replace this with your actual dashboard view.");
        }

        public IActionResult Error()
        {
            var error = HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (error != null)
            {
                logger.LogError(error, "Unhandled exception reached the error page.");
            }

            // Never echo error.Message to the browser: it can carry SQL/server
            // internals. Log it above instead, same as every other controller.
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An unexpected error occurred. Please try again.");
        }
    }
}
