using CleanMate.Api.Contracts;

namespace CleanMate.Api.Services;

public static class BookingRules
{
    public static readonly IReadOnlyDictionary<string, decimal> HourlyRates =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["cleaning"] = 70000m, ["cooking"] = 90000m,
            ["laundry"] = 70000m, ["childcare"] = 100000m
        };

    public static string? Validate(CreateBookingDto dto, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(dto.ServiceKey) || !HourlyRates.ContainsKey(dto.ServiceKey))
            return "Vui lòng chọn dịch vụ hợp lệ.";
        if (dto.DurationHours is < 1 or > 8) return "Thời lượng phải từ 1 đến 8 giờ.";
        if (ValidateStartTime(dto.StartTime, now) is { } timeError) return timeError;
        if (string.IsNullOrWhiteSpace(dto.Address) || dto.Address.Trim().Length > 500)
            return "Địa chỉ là bắt buộc và tối đa 500 ký tự.";
        if (dto.Notes?.Length > 2000) return "Ghi chú tối đa 2000 ký tự.";
        if (dto.ContactName is not null &&
            (string.IsNullOrWhiteSpace(dto.ContactName) || dto.ContactName.Trim().Length > 200))
            return "Tên người liên hệ phải từ 1 đến 200 ký tự.";
        if (dto.ContactPhone is not null &&
            (dto.ContactPhone.Length > 40 ||
             !System.Text.RegularExpressions.Regex.IsMatch(dto.ContactPhone.Trim(), @"^\+?[0-9\s().-]+$") ||
             dto.ContactPhone.Count(char.IsAsciiDigit) is < 6 or > 15))
            return "Số điện thoại liên hệ không hợp lệ (6 đến 15 chữ số).";
        if (dto.PaymentMethod != "cash") return "Hiện chỉ hỗ trợ thanh toán tiền mặt.";
        return null;
    }

    public static string? ValidateStartTime(DateTimeOffset startTime, DateTimeOffset now) =>
        startTime <= now || startTime > now.AddMonths(6)
            ? "Thời gian bắt đầu phải ở tương lai, trong vòng 6 tháng." : null;
}
