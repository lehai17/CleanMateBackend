using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CleanMate.CleanerWeb.Services;

public class CleanerSessionAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Session.GetString("Role") != "Cleaner" ||
            string.IsNullOrEmpty(context.HttpContext.Session.GetString("AccessToken")))
            context.Result = new RedirectToActionResult("Login", "Auth", null);
    }
}
