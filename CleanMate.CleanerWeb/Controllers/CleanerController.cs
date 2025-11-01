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
                .FirstOrDefaultAsync(c => c.Id == cleanerId);

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

            // ✅ Lấy CleanerProfile theo Id (chứ không phải User)
            var cleaner = await _db.CleanerProfiles
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == cleanerId);

            if (cleaner == null) return NotFound();

            // ✅ Cập nhật thông tin
            cleaner.User.FullName = model.FullName;
            cleaner.User.PhoneNumber = model.PhoneNumber;
            cleaner.Bio = model.Bio;
            cleaner.AddressText = model.Address;

            await _db.SaveChangesAsync();

            // ✅ Cập nhật lại session (để Dashboard đọc được bản mới)
            HttpContext.Session.SetString("FullName", cleaner.User.FullName);
            HttpContext.Session.SetString("PhoneNumber", cleaner.User.PhoneNumber ?? "");
            HttpContext.Session.SetString("Address", cleaner.AddressText ?? "");
            HttpContext.Session.SetString("Bio", cleaner.Bio ?? "");

            TempData["Message"] = "Profile updated successfully!";
            return RedirectToAction("Index", "Dashboard");
        }


    }
}
