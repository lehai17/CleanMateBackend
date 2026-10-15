namespace CleanMate.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payment/options", () => Results.Ok(new {
            cash = true, momo = false, payos = false, collectionStage = "Completed", model = "DirectCash" }));
        foreach (var route in new[] { "/api/payment/momo", "/api/payment/payos", "/api/payment/{bookingId:int}/reconcile" })
            app.MapPost(route, Retired).RequireAuthorization();
        app.MapPost("/api/payment/momo-callback", Retired);
        app.MapPost("/api/payment/payos/webhook", Retired);
        return app;
    }

    private static IResult Retired() => Results.Problem(statusCode: StatusCodes.Status410Gone,
        detail: "CleanMate chỉ kết nối dịch vụ. Khách trả tiền mặt trực tiếp cho cleaner sau khi hoàn thành ca.");
}