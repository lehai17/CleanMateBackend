using CleanMate.CleanerWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace CleanMate.CleanerWeb.Controllers;

[CleanerSession]
public class BookingController(CleanerApiClient api) : Controller
{
    public async Task<IActionResult> List()
    {
        var available = await api.GetJobsAsync("available");
        var mine = await api.GetJobsAsync("mine");
        foreach (var booking in available.Concat(mine)) booking.OrderCode = "CM" + booking.Id;
        return View(available.Concat(mine).OrderBy(b => b.StartTime));
    }

    [HttpPost]
    public async Task<IActionResult> Accept(int id)
    {
        TempData["Message"] = await api.AcceptAsync(id) ? "Nhận việc thành công." : "Đơn không còn khả dụng.";
        return RedirectToAction("List");
    }

    [HttpPost]
    public async Task<IActionResult> Start(int id)
    {
        TempData["Message"] = await api.StartAsync(id) ? "Đã bắt đầu công việc." : "Không thể bắt đầu: trạng thái đơn đã thay đổi.";
        return RedirectToAction("List");
    }

    [HttpPost]
    public async Task<IActionResult> Complete(int id)
    {
        TempData["Message"] = await api.CompleteAsync(id) ? "Đã hoàn thành công việc." : "Không thể hoàn thành: trạng thái đơn đã thay đổi.";
        return RedirectToAction("List");
    }
}
