using BCrypt.Net;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.CleanerWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.CleanerWeb.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;

        public AuthController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /Auth/Login
        [HttpGet]
        public IActionResult Login()
        {
            // Nếu đã login rồi thì về Dashboard
            if (HttpContext.Session.GetInt32("UserId") != null)
                return RedirectToAction("Index", "Dashboard");

            return View(new LoginViewModel());
        }

        // POST: /Auth/Login
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _db.Users
                .Include(u => u.CleanerProfile)
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                model.ErrorMessage = "❌ Email hoặc mật khẩu không đúng.";
                return View(model);
            }

            if (user.Role.ToString() != "Cleaner")
            {
                model.ErrorMessage = "🚫 Bạn không có quyền truy cập (chỉ dành cho Cleaner).";
                return View(model);
            }

            // Lưu session
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Role", user.Role.ToString());
            if (user.CleanerProfile != null)
                HttpContext.Session.SetInt32("CleanerId", user.CleanerProfile.Id);

            return RedirectToAction("Index", "Dashboard");
        }


        // GET: /Auth/Register
        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        // POST: /Auth/Register
        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.FullName) ||
                string.IsNullOrWhiteSpace(model.Email) ||
                string.IsNullOrWhiteSpace(model.Password))
            {
                model.ErrorMessage = "⚠️ Please fill all required fields.";
                return View(model);
            }

            if (model.Password != model.ConfirmPassword)
            {
                model.ErrorMessage = "❌ Passwords do not match.";
                return View(model);
            }

            var email = model.Email.Trim().ToLowerInvariant();
            if (await _db.Users.AnyAsync(u => u.Email == email))
            {
                model.ErrorMessage = "⚠️ Email already exists.";
                return View(model);
            }

            // Tạo user
            var user = new User
            {
                FullName = model.FullName,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                Role = UserRole.Cleaner
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Tạo hồ sơ cleaner
            var profile = new CleanerProfile
            {
                UserId = user.Id,
                Bio = "New cleaner",
                HourlyRate = 120000,
                AvgRating = 0,
                City = "Hà Nội",
                AddressText = "Updating..."
            };
            _db.CleanerProfiles.Add(profile);
            await _db.SaveChangesAsync();

            // Tự động login
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Role", user.Role.ToString());
            HttpContext.Session.SetInt32("CleanerId", profile.Id);

            model.SuccessMessage = "✅ Registered successfully! Redirecting...";
            return RedirectToAction("Index", "Dashboard");
        }


        // GET: /Auth/Logout
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
