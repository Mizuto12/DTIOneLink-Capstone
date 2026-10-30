using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;

namespace DTIOneLink.Controllers
{
    public class HomeController(ILogger<HomeController> logger, IWebHostEnvironment env) : Controller
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

                // Temporary diagnostic, same pattern/reasoning as Program.cs's
                // database-update-error.txt: the host has stdout logging
                // disabled (publish/web.config is regenerated fresh by every
                // `dotnet publish`, always defaulting to stdoutLogEnabled=
                // "false"), so logger.LogError above currently goes nowhere
                // anyone can read. Overwritten on each new error, not
                // appended, so this never grows unbounded; remove once the
                // real cause behind whatever is being chased right now is
                // found and fixed, same as the ErrorLogs table before it
                // (see commit 45820fb).
                try
                {
                    var errorFile = Path.Combine(env.ContentRootPath, "App_Data", "last-error.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(errorFile)!);
                    System.IO.File.WriteAllText(errorFile,
                        $"{DateTime.UtcNow:u}\nPath: {HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path}\n\n{error}");
                }
                catch { /* logging above is enough if the folder is read-only */ }
            }

            // Never echo error.Message to the browser: it can carry SQL/server
            // internals. Log it above instead, same as every other controller.
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An unexpected error occurred. Please try again.");
        }
    }
}
