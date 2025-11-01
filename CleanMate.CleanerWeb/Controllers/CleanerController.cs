using CleanMate.Api.Infrastructure;
using CleanMate.CleanerWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.CleanerWeb.Controllers
{
    public class CleanerController : Controller
    {
        private readonly AppDbContext _db;

        public CleanerController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /Cleaner/Profile
        public async Task<IActionResult> Profile()
        {
            var cleanerId = HttpContext.Session.GetInt32("CleanerId");
            if (cleanerId == null) return RedirectToAction("Login", "Auth");

            var cleaner = await _db.CleanerProfiles
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == cleanerId);

            if (cleaner == null) return NotFound();

            var model = new ProfileViewModel
            {
                FullName = cleaner.User.FullName,
                PhoneNumber = cleaner.User.PhoneNumber,
                Address = cleaner.AddressText,
                Bio = cleaner.Bio
            };

            return View(model);
        }


        [HttpPost]
        public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
        {
            var cleanerId = HttpContext.Session.GetInt32("CleanerId");
            if (cleanerId == null) return RedirectToAction("Login", "Auth");

            var user = await _db.Users.Include(u => u.CleanerProfile)
                                      .FirstOrDefaultAsync(u => u.Id == cleanerId);
            if (user == null) return NotFound();

            // ✅ Cập nhật thông tin
            user.FullName = model.FullName;
            user.PhoneNumber = model.PhoneNumber;
            user.CleanerProfile!.Bio = model.Bio;
            user.CleanerProfile.AddressText = model.Address;

            await _db.SaveChangesAsync();

            await _db.Entry(user).Reference(u => u.CleanerProfile).LoadAsync();

            // ✅ Cập nhật lại session (để Dashboard đọc được bản mới)
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("PhoneNumber", user.PhoneNumber ?? "");
            HttpContext.Session.SetString("Address", user.CleanerProfile.AddressText ?? "");
            HttpContext.Session.SetString("Bio", user.CleanerProfile.Bio ?? "");

            TempData["Message"] = "Profile updated successfully!";
            return RedirectToAction("Index", "Dashboard");

        }

    }
}
