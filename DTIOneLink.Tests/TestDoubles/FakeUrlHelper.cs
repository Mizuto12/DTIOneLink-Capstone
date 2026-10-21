using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace DTIOneLink.Tests.TestDoubles;

// Minimal IUrlHelper so a manually-instantiated controller can call
// Url.Action(...)/Url.IsLocalUrl(...) without the real MVC routing
// pipeline. Only what AccountController's RoleHomeUrl/Login actually use.
public class FakeUrlHelper : IUrlHelper
{
    public ActionContext ActionContext { get; } = new();

    public string? Action(UrlActionContext actionContext) =>
        $"/{actionContext.Controller}/{actionContext.Action}";

    public string? Content(string? contentPath) => contentPath;

    public bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");

    public string? Link(string? routeName, object? values) => null;

    public string? RouteUrl(UrlRouteContext routeContext) => null;
}
