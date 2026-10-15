using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;
using DTIOneLink.Data;
using DTIOneLink.Models;

namespace DTIOneLink.Controllers
{
    public class HomeController(ILogger<HomeController> logger, AppDbContext context) : Controller
    {
        public IActionResult Index()
        {
            return Content("Logged in successfully. Replace this with your actual dashboard view.");
        }

        public async Task<IActionResult> Error()
        {
            var feature = HttpContext.Features.Get<IExceptionHandlerFeature>();
            var error = feature?.Error;
            if (error != null)
            {
                logger.LogError(error, "Unhandled exception reached the error page.");

                // Persisted so the exception can be diagnosed without server/
                // host log access — this app runs on shared hosting with no
                // IT staff to check IIS/stdout logs. Best-effort: a DB issue
                // here must never hide the original error from the response.
                try
                {
                    context.ErrorLogs.Add(new ErrorLog
                    {
                        Path = feature!.Path,
                        Method = HttpContext.Request.Method,
                        UserId = HttpContext.Session.GetInt32("UserId"),
                        UserRole = HttpContext.Session.GetString("UserRole"),
                        ExceptionType = error.GetType().FullName ?? error.GetType().Name,
                        Message = error.Message,
                        StackTrace = error.ToString()
                    });
                    await context.SaveChangesAsync();
                }
                catch (Exception logEx)
                {
                    logger.LogError(logEx, "Could not persist the error log entry.");
                }
            }

            // Never echo error.Message to the browser: it can carry SQL/server
            // internals. Log it above instead, same as every other controller.
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An unexpected error occurred. Please try again.");
        }
    }
}
