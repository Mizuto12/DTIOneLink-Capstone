using DTIOneLink.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DTIOneLink.Filters
{
    // Same session-based pattern as RequireLoginAttribute, but checks a
    // specific permission rather than just "is logged in".
    public class RequirePermissionAttribute : ActionFilterAttribute
    {
        private readonly string _permission;

        public RequirePermissionAttribute(string permission)
        {
            _permission = permission;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var userId = context.HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            var role = context.HttpContext.Session.GetString("UserRole");
            if (!RolePermissions.Has(role, _permission))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            }
        }
    }
}