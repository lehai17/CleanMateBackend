using Microsoft.AspNetCore.Mvc;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.CleanerWeb.Controllers
{
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;

        public DashboardController(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("CleanerId");

            var role = HttpContext.Session.GetString("Role");

            if (userId == null || role != "Cleaner")
            {
                return RedirectToAction("Login", "Auth");
            }

            var cleaner = await _db.CleanerProfiles
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cleaner == null)
            {
                ViewBag.Error = "Không tìm thấy hồ sơ Cleaner.";
                return View();
            }

            return View(cleaner);
        }
    }
}
