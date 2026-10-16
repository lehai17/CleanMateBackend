using System.Net;
using System.Net.Mail;
using System.Threading.Channels;

namespace CleanMate.Api.Services;

public interface IAccountEmail
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string purpose, string? code, CancellationToken cancellation);
}

public sealed class SmtpAccountEmail(IConfiguration configuration) : IAccountEmail
{
    public bool IsConfigured => configuration.GetValue("Email:Enabled", false) &&
        !string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"]) &&
        int.TryParse(configuration["Email:Smtp:Port"] ?? "587", out var port) && port is > 0 and <= 65535 && port != 465 &&
        (string.IsNullOrWhiteSpace(configuration["Email:Smtp:Username"]) || !string.IsNullOrEmpty(configuration["Email:Smtp:Password"])) &&
        MailAddress.TryCreate(configuration["Email:From"], out _);

    public async Task SendAsync(string email, string purpose, string? code, CancellationToken cancellation)
    {
        if (!IsConfigured) throw new InvalidOperationException("Account email is not configured.");
        var port = configuration.GetValue("Email:Smtp:Port", 587);
        if (port is < 1 or > 65535 || port == 465) throw new InvalidOperationException("Use an SMTP STARTTLS port, normally 587.");
        using var client = new SmtpClient(configuration["Email:Smtp:Host"], port) { EnableSsl = true, UseDefaultCredentials = false };
        if (!string.IsNullOrWhiteSpace(configuration["Email:Smtp:Username"]))
            client.Credentials = new NetworkCredential(configuration["Email:Smtp:Username"], configuration["Email:Smtp:Password"]);
        var subject = purpose switch { "ResetPassword" => "CleanMate: mã khôi phục mật khẩu", "VerifyEmail" => "CleanMate: mã xác minh email", _ => "CleanMate: mật khẩu đã được đặt lại" };
        var body = code is null
            ? "Mật khẩu CleanMate của bạn vừa được đặt lại. Các phiên đăng nhập cũ đã bị thu hồi. Nếu không phải bạn thực hiện, hãy khôi phục mật khẩu và liên hệ đội ngũ hỗ trợ."
            : $"Mã { (purpose == "ResetPassword" ? "khôi phục mật khẩu" : "xác minh email") } CleanMate của bạn:\n\n{code}\n\nMã chỉ dùng một lần, hết hạn sau 30 phút. Chỉ nhập mã trong CleanMate; không chia sẻ cho người khác. Nếu bạn không yêu cầu, hãy bỏ qua email này.";
        using var message = new MailMessage(configuration["Email:From"]!, email, subject, body) { IsBodyHtml = false };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, timeout.Token);
    }
}

public record AccountEmailRequest(string Email, string Purpose);

public sealed class AccountEmailQueue
{
    private readonly Channel<AccountEmailRequest> channel = Channel.CreateBounded<AccountEmailRequest>(new BoundedChannelOptions(100) {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
    });
    public bool TryAdd(string email, string purpose) => channel.Writer.TryWrite(new(email, purpose));
    public IAsyncEnumerable<AccountEmailRequest> ReadAll(CancellationToken cancellation) => channel.Reader.ReadAllAsync(cancellation);
}
