using DTIOneLink.Filters;
using DTIOneLink.Security;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace DTIOneLink.Tests;

public class FilterAttributeTests
{
    private static ActionExecutingContext BuildContext(int? userId, string? userRole)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (userId != null)
        {
            httpContext.Session.SetInt32("UserId", userId.Value);
        }
        if (userRole != null)
        {
            httpContext.Session.SetString("UserRole", userRole);
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    [Fact]
    public void RequireLogin_NoSessionUserId_RedirectsToLogin()
    {
        var context = BuildContext(userId: null, userRole: null);

        new RequireLoginAttribute().OnActionExecuting(context);

        var redirect = Assert.IsType<RedirectToActionResult>(context.Result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public void RequireLogin_HasSessionUserId_LetsTheRequestThrough()
    {
        var context = BuildContext(userId: 1, userRole: "Employee");

        new RequireLoginAttribute().OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void RequirePermission_NotLoggedIn_RedirectsToLogin()
    {
        var context = BuildContext(userId: null, userRole: null);

        new RequirePermissionAttribute(Permissions.ManageUserAccounts).OnActionExecuting(context);

        var redirect = Assert.IsType<RedirectToActionResult>(context.Result);
        Assert.Equal("Login", redirect.ActionName);
    }

    [Fact]
    public void RequirePermission_LoggedInButMissingThePermission_Returns403()
    {
        var context = BuildContext(userId: 1, userRole: "Employee");

        new RequirePermissionAttribute(Permissions.ManageUserAccounts).OnActionExecuting(context);

        var statusResult = Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, statusResult.StatusCode);
    }

    [Fact]
    public void RequirePermission_LoggedInWithThePermission_LetsTheRequestThrough()
    {
        var context = BuildContext(userId: 1, userRole: "Admin");

        new RequirePermissionAttribute(Permissions.ManageUserAccounts).OnActionExecuting(context);

        Assert.Null(context.Result);
    }
}
