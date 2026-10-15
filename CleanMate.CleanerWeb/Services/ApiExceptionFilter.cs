using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CleanMate.CleanerWeb.Services;

public class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not HttpRequestException && context.Exception is not TaskCanceledException) return;
        if (context.Exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
        {
            context.HttpContext.Session.Clear();
            context.Result = new RedirectToActionResult("Login", "Auth", null);
        }
        else context.Result = new ContentResult { StatusCode = 502, Content = "Không thể kết nối dịch vụ. Vui lòng thử lại." };
        context.ExceptionHandled = true;
    }
}
