namespace CleanMate.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // Online payments remain unavailable until signed callbacks and reconciliation are implemented.
        app.MapPost("/api/payment/momo", () => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: "Hiện chỉ hỗ trợ thanh toán tiền mặt.")).RequireAuthorization();
        app.MapPost("/api/payment/momo-callback", () => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable, detail: "Online payments are not enabled."));
        return app;
    }
}
