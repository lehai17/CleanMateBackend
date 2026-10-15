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
        if (dto.StartTime <= now || dto.StartTime > now.AddMonths(6))
            return "Thời gian bắt đầu phải ở tương lai, trong vòng 6 tháng.";
        if (string.IsNullOrWhiteSpace(dto.Address) || dto.Address.Trim().Length > 500)
            return "Địa chỉ là bắt buộc và tối đa 500 ký tự.";
        if (dto.Notes?.Length > 2000) return "Ghi chú tối đa 2000 ký tự.";
        if (dto.PaymentMethod != "cash") return "Hiện chỉ hỗ trợ thanh toán tiền mặt.";
        return null;
    }
}