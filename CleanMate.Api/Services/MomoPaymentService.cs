using System.Text;
using System.Text.Json;

namespace CleanMate.Api.Services;

public class MomoPaymentService
{
    public async Task<string> CreatePaymentAsync(HttpContext context)
    {
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var json = JsonDocument.Parse(body);
        var amount = json.RootElement.GetProperty("amount").GetDecimal();
        var orderInfo = json.RootElement.GetProperty("orderInfo").GetString()
            ?? "Thanh toan dich vu CleanMate";

        const string endpoint = "https://test-payment.momo.vn/v2/gateway/api/create";
        const string partnerCode = "MOMOXXXX2024";
        const string accessKey = "your_access_key";
        const string secretKey = "your_secret_key";

        var orderId = DateTime.Now.Ticks.ToString();
        var requestId = Guid.NewGuid().ToString();
        var redirectUrl = "http://localhost:3000/payment-success";
        var ipnUrl = "http://localhost:5238/api/payment/momo-callback";

        var rawSignature =
            $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType=captureWallet";

        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signature = BitConverter.ToString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSignature)))
            .Replace("-", "")
            .ToLowerInvariant();

        var requestBody = new
        {
            partnerCode,
            partnerName = "CleanMate",
            storeId = "CleanMateStore",
            requestId,
            amount,
            orderId,
            orderInfo,
            redirectUrl,
            ipnUrl,
            lang = "vi",
            requestType = "captureWallet",
            signature
        };

        using var client = new HttpClient();
        var response = await client.PostAsync(
            endpoint,
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> ReadCallbackAsync(HttpContext context)
    {
        return await new StreamReader(context.Request.Body).ReadToEndAsync();
    }
}
