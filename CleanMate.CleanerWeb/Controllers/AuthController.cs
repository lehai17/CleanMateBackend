using CleanMate.CleanerWeb.Models;
using CleanMate.CleanerWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace CleanMate.CleanerWeb.Controllers;

public class AuthController(CleanerApiClient api) : Controller
{
    [HttpGet]
    public IActionResult Login() => HttpContext.Session.GetString("AccessToken") is not null
        ? RedirectToAction("Index", "Dashboard") : View(new LoginViewModel());

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
        {
            model.ErrorMessage = "Vui lòng nhập email và mật khẩu.";
            return View(model);
        }
        var auth = await api.AuthenticateAsync("api/auth/login", new { model.Email, model.Password });
        if (auth is null || auth.User.Role != "Cleaner" || !auth.User.IsCleaner)
        {
            model.ErrorMessage = "Email, mật khẩu hoặc quyền cleaner không hợp lệ.";
            return View(model);
        }
        SignIn(auth);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.FullName) ||
            string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password) ||
            model.Password.Length < 8 || model.Password != model.ConfirmPassword)
        {
            model.ErrorMessage = "Nhập đầy đủ thông tin, mật khẩu ít nhất 8 ký tự và xác nhận trùng khớp.";
            return View(model);
        }
        var auth = await api.AuthenticateAsync("api/auth/register",
            new { model.FullName, model.Email, model.Password, role = "Cleaner" });
        if (auth is null)
        {
            model.ErrorMessage = "Không thể đăng ký. Kiểm tra email và thông tin đã nhập.";
            return View(model);
        }
        TempData["ApplicationMessage"] = "Đã gửi hồ sơ. Vui lòng chờ nhân viên duyệt rồi đăng nhập lại để nhận việc.";
        return RedirectToAction("Login");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    private void SignIn(ApiAuth auth)
    {
        HttpContext.Session.Clear();
        HttpContext.Session.SetString("AccessToken", auth.Token);
        HttpContext.Session.SetString("Role", auth.User.Role);
        HttpContext.Session.SetInt32("UserId", auth.User.Id);
        HttpContext.Session.SetString("FullName", auth.User.FullName);
        HttpContext.Session.SetInt32("CleanerId", auth.User.CleanerProfile!.Id);
    }
}
