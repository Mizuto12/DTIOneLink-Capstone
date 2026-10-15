using DTIOneLink.Controllers;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Tests;

// Regression test for the diagnostic error log added after an unhandled
// exception on a production route (Dashboard/Details) couldn't be diagnosed
// without server/host log access. HomeController.Error must persist the
// exception to ErrorLogs — and must still return 500 even if that save
// somehow fails, since the user-facing response must never depend on logging
// succeeding.
public class HomeControllerErrorLogTests
{
    private static HomeController BuildController(DTIOneLink.Data.AppDbContext context, Exception? error)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (error != null)
        {
            httpContext.Features.Set<IExceptionHandlerFeature>(
                new ExceptionHandlerFeature { Error = error, Path = "/Dashboard/Details/40" });
        }
        httpContext.Session.SetInt32("UserId", 7);
        httpContext.Session.SetString("UserRole", "Employee");

        return new HomeController(NullLogger<HomeController>.Instance, context)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext },
        };
    }

    [Fact]
    public async Task Error_WithUnhandledException_PersistsErrorLogAndReturns500()
    {
        var context = InMemoryDbContextFactory.Create();
        var controller = BuildController(context, new InvalidOperationException("boom"));

        var result = await controller.Error();

        var status = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(500, status.StatusCode);

        var saved = await context.ErrorLogs.SingleAsync();
        Assert.Equal("/Dashboard/Details/40", saved.Path);
        Assert.Equal("boom", saved.Message);
        Assert.Contains("InvalidOperationException", saved.ExceptionType);
        Assert.Equal(7, saved.UserId);
        Assert.Equal("Employee", saved.UserRole);
    }

    [Fact]
    public async Task Error_WithNoExceptionFeature_StillReturns500WithoutLogging()
    {
        var context = InMemoryDbContextFactory.Create();
        var controller = BuildController(context, error: null);

        var result = await controller.Error();

        var status = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(500, status.StatusCode);
        Assert.Empty(context.ErrorLogs);
    }
}
