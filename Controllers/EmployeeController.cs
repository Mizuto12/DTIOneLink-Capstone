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
        // Records() and Reports() actions removed — those pages now live at
        // RecordsController.Index() and ReportsController.Index(), shared with Admin.
        // The old Views/Employee/Records.cshtml and Views/Employee/Reports.cshtml
        // files are no longer referenced by anything and should be deleted.
    }
}