using CleanMate.CleanerWeb.Models;
using CleanMate.CleanerWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace CleanMate.CleanerWeb.Controllers;

[CleanerSession]
public class CleanerController(CleanerApiClient api) : Controller
{
    public async Task<IActionResult> Profile()
    {
        var user = await api.GetProfileAsync();
        return View(new ProfileViewModel { FullName = user.FullName, PhoneNumber = user.PhoneNumber,
            Address = user.CleanerProfile?.AddressText, Bio = user.CleanerProfile?.Bio });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.FullName))
        {
            ModelState.AddModelError("FullName", "Họ tên là bắt buộc.");
            return View("Profile", model);
        }
        var user = await api.GetProfileAsync();
        await api.UpdateProfileAsync(new { model.FullName, model.PhoneNumber, addressText = model.Address ?? "",
            bio = model.Bio ?? "", city = user.CleanerProfile?.City, hourlyRate = user.CleanerProfile?.HourlyRate });
        HttpContext.Session.SetString("FullName", model.FullName);
        return RedirectToAction("Index", "Dashboard");
    }
}