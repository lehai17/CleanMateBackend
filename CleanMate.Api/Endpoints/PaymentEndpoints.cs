using CleanMate.Api.Services;

namespace CleanMate.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payment").WithTags("Payment");

        group.MapPost("/momo", async (MomoPaymentService momoPaymentService, HttpContext context) =>
        {
            try
            {
                var responseContent = await momoPaymentService.CreatePaymentAsync(context);
                return Results.Content(responseContent, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Problem("Internal Server Error: " + ex.Message);
            }
        });

        group.MapPost("/momo-callback", async (MomoPaymentService momoPaymentService, HttpContext context) =>
        {
            var body = await momoPaymentService.ReadCallbackAsync(context);
            Console.WriteLine("MoMo callback: " + body);
            return Results.Ok(new { message = "Callback received" });
        });

        return app;
    }
}
