using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class EmployeeController : Controller
{
    public IActionResult Index()
{
    return RedirectToAction("AdminDashboard", "Dashboard");
}
}
}